package main

// Chống lạm dụng cho trạm chuyển tiếp: giới hạn tốc độ mở kết nối, giới hạn "quét" nhiều máy chủ, khóa tạm khi
// thất bại liên tiếp tới cùng một đích (dấu hiệu dò mật khẩu), hạn mức thấp cho tài khoản mới, danh sách chặn đích,
// và ghi log ra file (giữ lâu hơn journald). Trạm không đọc được nội dung SSH (đã mã hóa đầu-cuối) nên chỉ nhìn
// được hành vi: ai, lúc nào, tới đâu, bao lâu, bao nhiêu lần.

import (
	"bufio"
	"flag"
	"io"
	"log"
	"net"
	"os"
	"strings"
	"sync"
	"time"
)

var (
	ipRate          = flag.Int("ip-rate", 30, "số kết nối mới tối đa mỗi phút từ một địa chỉ IP")
	userRate        = flag.Int("user-rate", 10, "số kết nối mới tối đa mỗi phút của một tài khoản")
	newAcctHours    = flag.Int("new-acct-hours", 24, "tài khoản tạo chưa quá số giờ này bị coi là tài khoản mới (hạn mức thấp hơn); 0 = tắt")
	newAcctMax      = flag.Int("new-acct-max", 3, "số kết nối đồng thời tối đa của tài khoản mới")
	newAcctRate     = flag.Int("new-acct-rate", 4, "số kết nối mới tối đa mỗi phút của tài khoản mới")
	targetsPerHour  = flag.Int("targets-per-hour", 30, "số máy chủ (host:port) khác nhau tối đa mỗi giờ của một tài khoản")
	newAcctTargets  = flag.Int("new-acct-targets", 5, "như trên nhưng cho tài khoản mới")
	failMax         = flag.Int("fail-max", 10, "số lần thất bại/ngắt sớm tới cùng một đích trong 10 phút trước khi khóa tạm 15 phút")
	blocklistPath   = flag.String("blocklist", "", "file danh sách đích bị chặn (mỗi dòng: tên máy, *.tên-miền, IP hoặc CIDR; # là chú thích); nạp lại mỗi 60 giây")
	blocklistDyn    = flag.String("blocklist-dynamic", "", "file danh sách chặn do quản trị thêm/bớt qua trang web (relay được phép ghi vào)")
	logFilePath     = flag.String("log-file", "", "ghi log thêm vào file này (ngoài stderr)")
	shortSessionSec = 20 // phiên đóng trong chừng này giây sau khi mở được tính là một lần "thất bại"
)

const (
	failWindow = 10 * time.Minute
	failLock   = 15 * time.Minute
)

// ===== Giới hạn tốc độ theo cửa sổ cố định =====

type window struct {
	start time.Time
	d     time.Duration
	n     int
}

var (
	rateMu sync.Mutex
	rates  = map[string]*window{}
)

func rateAllow(key string, max int, d time.Duration) bool {
	if max <= 0 {
		return true
	}
	now := time.Now()
	rateMu.Lock()
	defer rateMu.Unlock()
	w := rates[key]
	if w == nil || now.Sub(w.start) >= w.d {
		if len(rates) > 50000 {
			for k, v := range rates {
				if now.Sub(v.start) >= v.d {
					delete(rates, k)
				}
			}
		}
		rates[key] = &window{start: now, d: d, n: 1}
		return true
	}
	w.n++
	return w.n <= max
}

// ===== Số máy chủ khác nhau mỗi giờ (phát hiện quét) =====

type spread struct {
	start time.Time
	set   map[string]struct{}
}

var (
	spreadMu sync.Mutex
	spreads  = map[string]*spread{}
)

func spreadAllow(email, target string, max int) bool {
	if max <= 0 {
		return true
	}
	now := time.Now()
	spreadMu.Lock()
	defer spreadMu.Unlock()
	s := spreads[email]
	if s == nil || now.Sub(s.start) >= time.Hour {
		if len(spreads) > 20000 {
			for k, v := range spreads {
				if now.Sub(v.start) >= time.Hour {
					delete(spreads, k)
				}
			}
		}
		s = &spread{start: now, set: map[string]struct{}{}}
		spreads[email] = s
	}
	if _, ok := s.set[target]; ok {
		return true
	}
	if len(s.set) >= max {
		return false
	}
	s.set[target] = struct{}{}
	return true
}

// ===== Khóa tạm khi thất bại liên tiếp tới cùng một đích =====

type failRec struct {
	start time.Time
	n     int
	lock  time.Time
}

var (
	failMu sync.Mutex
	fails  = map[string]*failRec{}
)

func failKey(email, target string) string { return email + "|" + target }

// failLocked cho biết (user, đích) có đang bị khóa tạm không.
func failLocked(email, target string) bool {
	failMu.Lock()
	defer failMu.Unlock()
	f := fails[failKey(email, target)]
	return f != nil && time.Now().Before(f.lock)
}

// failRecord ghi một lần thất bại; trả true nếu vừa kích hoạt khóa.
func failRecord(email, target string) bool {
	if *failMax <= 0 {
		return false
	}
	now := time.Now()
	failMu.Lock()
	defer failMu.Unlock()
	if len(fails) > 20000 {
		for k, v := range fails {
			if now.Sub(v.start) >= failWindow && now.After(v.lock) {
				delete(fails, k)
			}
		}
	}
	k := failKey(email, target)
	f := fails[k]
	if f == nil || (now.Sub(f.start) >= failWindow && now.After(f.lock)) {
		f = &failRec{start: now}
		fails[k] = f
	}
	f.n++
	if f.n >= *failMax && !now.Before(f.lock) {
		f.lock = now.Add(failLock)
		f.n = 0
		f.start = now
		return true
	}
	return false
}

// ===== Danh sách chặn đích =====

type blockList struct {
	hosts  map[string]bool
	suffix []string // dạng ".example.com"
	ips    map[string]bool
	nets   []*net.IPNet
}

var (
	blMu sync.RWMutex
	bl   = &blockList{}
)

func loadBlocklist() {
	nb := &blockList{hosts: map[string]bool{}, ips: map[string]bool{}}
	for _, p := range []string{*blocklistPath, *blocklistDyn} {
		if p != "" {
			readBlockFile(p, nb)
		}
	}
	blMu.Lock()
	bl = nb
	blMu.Unlock()
}

func readBlockFile(path string, nb *blockList) {
	f, err := os.Open(path)
	if err != nil {
		if !os.IsNotExist(err) {
			log.Printf("blocklist: %v", err)
		}
		return
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := sc.Text()
		if i := strings.IndexByte(line, '#'); i >= 0 {
			line = line[:i]
		}
		line = strings.ToLower(strings.TrimSpace(line))
		if line == "" {
			continue
		}
		switch {
		case strings.Contains(line, "/"):
			if _, n, err := net.ParseCIDR(line); err == nil {
				nb.nets = append(nb.nets, n)
			}
		case net.ParseIP(line) != nil:
			nb.ips[net.ParseIP(line).String()] = true
		case strings.HasPrefix(line, "*."):
			nb.suffix = append(nb.suffix, line[1:])
		default:
			nb.hosts[line] = true
		}
	}
}

// isBlocked: host là tên/IP người dùng nhập, ip là địa chỉ đã phân giải.
func isBlocked(host string, ip net.IP) bool {
	blMu.RLock()
	b := bl
	blMu.RUnlock()
	host = strings.ToLower(strings.TrimSuffix(strings.TrimSpace(host), "."))
	if b.hosts[host] || b.ips[host] {
		return true
	}
	for _, s := range b.suffix {
		if strings.HasSuffix(host, s) || host == s[1:] {
			return true
		}
	}
	if ip != nil {
		if b.ips[ip.String()] {
			return true
		}
		for _, n := range b.nets {
			if n.Contains(ip) {
				return true
			}
		}
	}
	return false
}

// ===== Khởi động: log file + nạp blocklist định kỳ =====

func startAbuse() {
	if *logFilePath != "" {
		f, err := os.OpenFile(*logFilePath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o640)
		if err != nil {
			log.Printf("log-file: %v", err)
		} else {
			log.SetOutput(io.MultiWriter(os.Stderr, f))
		}
	}
	loadBlocklist()
	go func() {
		for range time.Tick(60 * time.Second) {
			loadBlocklist()
		}
	}()
}

// ===== Tài khoản mới =====

func isNewAccount(created time.Time) bool {
	if *newAcctHours <= 0 {
		return false
	}
	if created.IsZero() {
		return true // không biết tuổi tài khoản → áp hạn mức thấp cho an toàn
	}
	return time.Since(created) < time.Duration(*newAcctHours)*time.Hour
}

func parseCreated(s string) time.Time {
	for _, layout := range []string{time.RFC3339, "2006-01-02 15:04:05Z07:00"} {
		if t, err := time.Parse(layout, s); err == nil {
			return t
		}
	}
	return time.Time{}
}
