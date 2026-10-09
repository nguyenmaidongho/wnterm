// wnterm-relay: trạm chuyển tiếp WebSocket → SSH cho bản web (PWA) của WN Term.
//
// Trình duyệt không mở được kết nối TCP tới cổng SSH, nên PWA chạy SSH ngay trong trình duyệt (WebAssembly)
// và gửi các byte SSH ĐÃ MÃ HÓA qua WebSocket tới trạm này; trạm chỉ chép byte qua lại với máy chủ SSH đích,
// không giải mã được gì (mật khẩu, lệnh, dữ liệu đều mã hóa đầu-cuối giữa trình duyệt và máy chủ SSH).
//
// Giao thức: client mở wss://…/relay, gửi 1 tin văn bản JSON {"token","host","port"}.
// Trạm kiểm tra token với API tài khoản WN Term, kiểm tra đích hợp lệ, kết nối TCP, xác nhận đích đúng là máy chủ SSH
// (dòng chào "SSH-…"), trả {"ok":true} rồi chuyển byte hai chiều bằng tin nhị phân.
//
// Chống lạm dụng: chỉ tài khoản WN Term đã đăng nhập; chỉ nói chuyện với máy chủ SSH; chặn địa chỉ nội bộ
// (127.x, 10.x, 192.168.x…) và chỉ cho phép một số cổng khi đích là chính máy chạy trạm; giới hạn số kết nối.
//
// Ngoài ra POST /relay/probe (probe.go) kiểm tra nhanh các đích có đang mở cổng TCP hay không (chấm xanh/xám trong danh sách VM).
package main

import (
	"bufio"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"

	"github.com/coder/websocket"
)

var (
	listen       = flag.String("listen", "127.0.0.1:8787", "địa chỉ lắng nghe (nginx chuyển /relay vào đây)")
	apiBase      = flag.String("api", "https://wnterm.webnow.vn/api/v1", "API tài khoản WN Term để kiểm tra token")
	apiConnect   = flag.String("api-connect", "", "gọi API qua địa chỉ này thay vì phân giải tên miền (vd. 127.0.0.1:443 — gọi thẳng máy này, không vòng qua Cloudflare)")
	origins      = flag.String("origins", "wnterm.webnow.vn", "các Origin được phép mở WebSocket, cách nhau dấu phẩy (rỗng = mọi nơi)")
	allowPrivate = flag.Bool("allow-private", false, "cho phép đích là địa chỉ nội bộ (CHỈ dùng khi thử nghiệm)")
	selfPorts    = flag.String("self-ports", "22,8282", "các cổng được phép khi đích là chính máy này")
	selfIPsFlag  = flag.String("self-ips", "", "thêm IP công khai của máy này (nếu không gắn trực tiếp trên card mạng)")
	maxPerUser   = flag.Int("max-per-user", 20, "số kết nối tối đa mỗi tài khoản")
	maxTotal     = flag.Int("max-total", 500, "số kết nối tối đa toàn trạm")
)

type hello struct {
	Token string `json:"token"`
	Host  string `json:"host"`
	Port  int    `json:"port"`
}

type reply struct {
	OK    bool   `json:"ok"`
	Error string `json:"error,omitempty"`
}

// ===== Kiểm tra token (có bộ nhớ đệm ngắn hạn) =====

type authEntry struct {
	email   string
	created time.Time
	admin   bool
	until   time.Time
}

var (
	authMu    sync.Mutex
	authCache = map[string]authEntry{}
	httpc     *http.Client
)

func checkToken(ctx context.Context, token string) (string, error) {
	email, _, err := checkUser(ctx, token)
	return email, err
}

// checkUser trả email + thời điểm tạo tài khoản (để áp hạn mức thấp cho tài khoản mới).
func checkUser(ctx context.Context, token string) (string, time.Time, error) {
	e, err := lookupUser(ctx, token)
	return e.email, e.created, err
}

// lookupUser hỏi API tài khoản (có cache 5 phút): email, ngày tạo, có phải quản trị không.
func lookupUser(ctx context.Context, token string) (authEntry, error) {
	if len(token) < 20 || len(token) > 200 {
		return authEntry{}, errors.New("token không hợp lệ")
	}
	sum := sha256.Sum256([]byte(token))
	key := hex.EncodeToString(sum[:])

	authMu.Lock()
	if e, ok := authCache[key]; ok && time.Now().Before(e.until) {
		authMu.Unlock()
		return e, nil
	}
	authMu.Unlock()

	req, _ := http.NewRequestWithContext(ctx, http.MethodGet, strings.TrimRight(*apiBase, "/")+"/me", nil)
	req.Header.Set("Authorization", "Bearer "+token)
	req.Header.Set("User-Agent", "wnterm-relay")
	res, err := httpc.Do(req)
	if err != nil {
		return authEntry{}, fmt.Errorf("không kiểm tra được đăng nhập: %w", err)
	}
	defer res.Body.Close()
	var me struct {
		OK        bool   `json:"ok"`
		Email     string `json:"email"`
		CreatedAt string `json:"createdAt"`
		Admin     bool   `json:"admin"`
	}
	_ = json.NewDecoder(io.LimitReader(res.Body, 1<<16)).Decode(&me)
	if res.StatusCode != http.StatusOK || !me.OK || me.Email == "" {
		return authEntry{}, errors.New("phiên đăng nhập hết hạn, hãy đăng nhập lại")
	}
	e := authEntry{email: me.Email, created: parseCreated(me.CreatedAt), admin: me.Admin, until: time.Now().Add(5 * time.Minute)}

	authMu.Lock()
	if len(authCache) > 10000 {
		authCache = map[string]authEntry{}
	}
	authCache[key] = e
	authMu.Unlock()
	return e, nil
}

// forgetUser xóa mọi mục cache của một tài khoản (sau khi khóa) để lần kiểm tra kế tiếp hỏi lại API.
func forgetUser(email string) {
	authMu.Lock()
	for k, v := range authCache {
		if v.email == email {
			delete(authCache, k)
		}
	}
	authMu.Unlock()
}
// ===== Kiểm tra đích =====

var cgnat = mustCIDR("100.64.0.0/10")
var thisNet = mustCIDR("0.0.0.0/8")

func mustCIDR(s string) *net.IPNet {
	_, n, err := net.ParseCIDR(s)
	if err != nil {
		panic(err)
	}
	return n
}

func isInternal(ip net.IP) bool {
	return ip.IsLoopback() || ip.IsPrivate() || ip.IsLinkLocalUnicast() || ip.IsLinkLocalMulticast() ||
		ip.IsInterfaceLocalMulticast() || ip.IsMulticast() || ip.IsUnspecified() || cgnat.Contains(ip) || thisNet.Contains(ip)
}

var selfIPs = map[string]bool{}
var selfPortSet = map[int]bool{}

func loadSelf() {
	if addrs, err := net.InterfaceAddrs(); err == nil {
		for _, a := range addrs {
			if n, ok := a.(*net.IPNet); ok {
				selfIPs[n.IP.String()] = true
			}
		}
	}
	for _, s := range strings.Split(*selfIPsFlag, ",") {
		if ip := net.ParseIP(strings.TrimSpace(s)); ip != nil {
			selfIPs[ip.String()] = true
		}
	}
	for _, s := range strings.Split(*selfPorts, ",") {
		if p, err := strconv.Atoi(strings.TrimSpace(s)); err == nil {
			selfPortSet[p] = true
		}
	}
}

var errBlocked = errors.New("đích này bị chặn do vi phạm điều khoản sử dụng")

func resolveTarget(ctx context.Context, host string, port int) (string, error) {
	host = strings.TrimSpace(host)
	if host == "" || len(host) > 253 || port < 1 || port > 65535 {
		return "", errors.New("địa chỉ máy chủ không hợp lệ")
	}
	host = strings.TrimSuffix(strings.TrimPrefix(host, "["), "]")
	var ips []net.IP
	if ip := net.ParseIP(host); ip != nil {
		ips = []net.IP{ip}
	} else {
		rctx, cancel := context.WithTimeout(ctx, 5*time.Second)
		defer cancel()
		addrs, err := net.DefaultResolver.LookupIPAddr(rctx, host)
		if err != nil || len(addrs) == 0 {
			return "", fmt.Errorf("không tìm thấy máy chủ %s", host)
		}
		for _, a := range addrs {
			ips = append(ips, a.IP)
		}
	}
	for _, ip := range ips {
		if isBlocked(host, ip) {
			return "", errBlocked
		}
	}
	for _, ip := range ips {
		if !*allowPrivate && isInternal(ip) {
			continue
		}
		if selfIPs[ip.String()] && !selfPortSet[port] && !*allowPrivate {
			continue
		}
		return net.JoinHostPort(ip.String(), strconv.Itoa(port)), nil
	}
	return "", errors.New("không được phép kết nối tới địa chỉ này qua bản web (địa chỉ nội bộ)")
}

// ===== Đếm kết nối =====

var (
	countMu  sync.Mutex
	perUser  = map[string]int{}
	totalNow int
)

func acquire(email string, userMax int) bool {
	countMu.Lock()
	defer countMu.Unlock()
	if totalNow >= *maxTotal || perUser[email] >= userMax {
		return false
	}
	totalNow++
	perUser[email]++
	return true
}

func release(email string) {
	countMu.Lock()
	defer countMu.Unlock()
	totalNow--
	if perUser[email]--; perUser[email] <= 0 {
		delete(perUser, email)
	}
}

// ===== Xử lý một kết nối =====

func clientIP(r *http.Request) string {
	if ip := r.Header.Get("CF-Connecting-IP"); ip != "" {
		return ip
	}
	if ip := r.Header.Get("X-Real-IP"); ip != "" {
		return ip
	}
	return r.RemoteAddr
}

func sendJSON(ctx context.Context, c *websocket.Conn, v any) error {
	b, _ := json.Marshal(v)
	wctx, cancel := context.WithTimeout(ctx, 10*time.Second)
	defer cancel()
	return c.Write(wctx, websocket.MessageText, b)
}

func handle(w http.ResponseWriter, r *http.Request) {
	opts := &websocket.AcceptOptions{}
	if strings.TrimSpace(*origins) == "" {
		opts.InsecureSkipVerify = true
	} else {
		for _, o := range strings.Split(*origins, ",") {
			if o = strings.TrimSpace(o); o != "" {
				opts.OriginPatterns = append(opts.OriginPatterns, o)
			}
		}
	}
	c, err := websocket.Accept(w, r, opts)
	if err != nil {
		return
	}
	defer c.CloseNow()
	c.SetReadLimit(1 << 20)
	ctx := r.Context()
	fail := func(msg string) {
		_ = sendJSON(ctx, c, reply{OK: false, Error: msg})
		c.Close(websocket.StatusPolicyViolation, "")
	}

	// 1) Tin chào: token + đích
	hctx, cancel := context.WithTimeout(ctx, 15*time.Second)
	typ, data, err := c.Read(hctx)
	cancel()
	if err != nil || typ != websocket.MessageText {
		return
	}
	var h hello
	if json.Unmarshal(data, &h) != nil {
		fail("yêu cầu không hợp lệ")
		return
	}
	cip := clientIP(r)
	if !rateAllow("ip:"+cip, *ipRate, time.Minute) {
		log.Printf("abuse ip-rate ip=%s", cip)
		fail("quá nhiều yêu cầu từ địa chỉ của bạn, hãy thử lại sau ít phút")
		return
	}
	email, created, err := checkUser(ctx, h.Token)
	if err != nil {
		fail(err.Error())
		return
	}
	if isBanned(email) {
		fail("tài khoản đã bị khóa")
		return
	}
	newAcct := isNewAccount(created)
	rate, maxConn, maxTargets := *userRate, *maxPerUser, *targetsPerHour
	if newAcct {
		rate, maxConn, maxTargets = *newAcctRate, *newAcctMax, *newAcctTargets
	}
	if !rateAllow("user:"+email, rate, time.Minute) {
		log.Printf("abuse user-rate user=%s ip=%s new=%v", email, cip, newAcct)
		fail("bạn mở kết nối quá nhanh, hãy đợi một chút rồi thử lại")
		return
	}
	if !acquire(email, maxConn) {
		if newAcct {
			fail(fmt.Sprintf("tài khoản mới bị giới hạn số kết nối cùng lúc trong %d giờ đầu", *newAcctHours))
		} else {
			fail("quá nhiều kết nối cùng lúc, hãy đóng bớt tab")
		}
		return
	}
	defer release(email)

	target, err := resolveTarget(ctx, h.Host, h.Port)
	if err != nil {
		if err == errBlocked {
			log.Printf("abuse blocked user=%s ip=%s -> %s:%d", email, cip, h.Host, h.Port)
		}
		fail(err.Error())
		return
	}
	if failLocked(email, target) {
		log.Printf("abuse locked user=%s ip=%s -> %s:%d", email, cip, h.Host, h.Port)
		fail("tạm khóa kết nối tới máy chủ này do nhiều lần thất bại liên tiếp, hãy thử lại sau 15 phút")
		return
	}
	if !spreadAllow(email, target, maxTargets) {
		log.Printf("abuse too-many-targets user=%s ip=%s new=%v -> %s:%d", email, cip, newAcct, h.Host, h.Port)
		fail("bạn đã kết nối tới quá nhiều máy chủ khác nhau trong 1 giờ, hãy thử lại sau")
		return
	}
	noteFail := func(why string) {
		if failRecord(email, target) {
			log.Printf("abuse fail-lock user=%s ip=%s -> %s:%d (%s)", email, cip, h.Host, h.Port, why)
		}
	}

	// 2) Kết nối TCP + xác nhận đích là máy chủ SSH
	d := net.Dialer{Timeout: 10 * time.Second, KeepAlive: 30 * time.Second}
	tcp, err := d.DialContext(ctx, "tcp", target)
	if err != nil {
		noteFail("dial")
		fail(fmt.Sprintf("không kết nối được %s:%d", h.Host, h.Port))
		return
	}
	defer tcp.Close()
	_ = tcp.SetReadDeadline(time.Now().Add(15 * time.Second))
	br := bufio.NewReaderSize(tcp, 32*1024)
	var banner []byte
	for len(banner) < 8192 {
		line, err := br.ReadSlice('\n')
		banner = append(banner, line...)
		if err != nil && !errors.Is(err, bufio.ErrBufferFull) {
			noteFail("banner")
			fail("máy chủ đích không phản hồi như máy chủ SSH")
			return
		}
		// RFC 4253: máy chủ có thể gửi vài dòng chữ trước dòng "SSH-…"
		if strings.HasPrefix(string(line), "SSH-") {
			break
		}
	}
	if !strings.Contains(string(banner), "SSH-") {
		noteFail("not-ssh")
		fail("đích không phải máy chủ SSH")
		return
	}
	_ = tcp.SetReadDeadline(time.Time{})

	start := time.Now()
	log.Printf("open %s %s -> %s:%d (%s)", email, cip, h.Host, h.Port, target)
	if err := sendJSON(ctx, c, reply{OK: true}); err != nil {
		return
	}
	if err := c.Write(ctx, websocket.MessageBinary, banner); err != nil {
		return
	}

	// 3) Chuyển byte hai chiều + ping giữ kết nối (Cloudflare cắt WebSocket im lặng quá 100 giây)
	pctx, stop := context.WithCancel(ctx)
	defer stop()
	defer registerConn(&conn{Email: email, IP: cip, Host: h.Host, Port: h.Port, Start: start, cancel: stop})()
	go func() {
		t := time.NewTicker(25 * time.Second)
		defer t.Stop()
		for {
			select {
			case <-pctx.Done():
				return
			case <-t.C:
				ctx2, cancel := context.WithTimeout(pctx, 20*time.Second)
				err := c.Ping(ctx2)
				cancel()
				if err != nil {
					stop()
					return
				}
			}
		}
	}()
	go func() { // SSH → trình duyệt
		defer stop()
		buf := make([]byte, 32*1024)
		for {
			n, err := br.Read(buf)
			if n > 0 {
				if c.Write(pctx, websocket.MessageBinary, buf[:n]) != nil {
					return
				}
			}
			if err != nil {
				return
			}
		}
	}()
	go func() { // trình duyệt → SSH
		defer stop()
		for {
			typ, data, err := c.Read(pctx)
			if err != nil {
				return
			}
			if typ == websocket.MessageBinary && len(data) > 0 {
				if _, err := tcp.Write(data); err != nil {
					return
				}
			}
		}
	}()
	<-pctx.Done()
	c.Close(websocket.StatusNormalClosure, "")
	dur := time.Since(start)
	if dur < time.Duration(shortSessionSec)*time.Second {
		noteFail("short-session") // đóng rất sớm: thường là sai mật khẩu hoặc dò thử
	}
	log.Printf("close %s -> %s:%d after %s", email, h.Host, h.Port, dur.Round(time.Second))
}

func main() {
	flag.Parse()
	tr := http.DefaultTransport.(*http.Transport).Clone()
	if *apiConnect != "" {
		// Vẫn kiểm tra chứng chỉ theo tên miền trong -api (SNI), chỉ đổi địa chỉ kết nối.
		target := *apiConnect
		d := &net.Dialer{Timeout: 5 * time.Second}
		tr.DialContext = func(ctx context.Context, network, _ string) (net.Conn, error) { return d.DialContext(ctx, network, target) }
	}
	httpc = &http.Client{Timeout: 10 * time.Second, Transport: tr}
	log.SetFlags(log.LstdFlags)
	loadSelf()
	startAbuse()

	mux := http.NewServeMux()
	mux.HandleFunc("/relay", handle)
	mux.HandleFunc("/relay/probe", handleProbe)
	mux.HandleFunc("/relay/admin/conns", handleAdminConns)
	mux.HandleFunc("/relay/admin/kick", handleAdminKick)
	mux.HandleFunc("/relay/admin/log", handleAdminLog)
	mux.HandleFunc("/relay/admin/blocklist", handleAdminBlocklist)
	mux.HandleFunc("/relay/admin/block", handleAdminBlock(false))
	mux.HandleFunc("/relay/admin/unblock", handleAdminBlock(true))
	mux.HandleFunc("/relay/health", func(w http.ResponseWriter, _ *http.Request) {
		countMu.Lock()
		n := totalNow
		countMu.Unlock()
		w.Header().Set("Content-Type", "application/json")
		_, _ = fmt.Fprintf(w, `{"ok":true,"service":"wnterm-relay","connections":%d}`, n)
	})

	srv := &http.Server{Addr: *listen, Handler: mux, ReadHeaderTimeout: 10 * time.Second}
	go func() {
		log.Printf("wnterm-relay listening on %s (api %s)", *listen, *apiBase)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatal(err)
		}
	}()
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, syscall.SIGINT, syscall.SIGTERM)
	<-sig
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	_ = srv.Shutdown(ctx)
}
