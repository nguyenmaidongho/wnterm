package main

// Các endpoint quản trị cho trang /quan-tri của website: xem kết nối đang mở, ngắt (kick) người dùng, xem log,
// thêm/bớt đích bị chặn. Chỉ tài khoản quản trị (API /me trả "admin": true) mới gọi được; xác thực bằng header
// "Authorization: Bearer <token>" như API tài khoản, và kiểm tra Origin như các endpoint khác.

import (
	"bufio"
	"encoding/json"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

// ===== Kết nối đang mở =====

type conn struct {
	ID     int64
	Email  string
	IP     string
	Host   string
	Port   int
	Start  time.Time
	cancel func()
}

var (
	connMu     sync.Mutex
	conns      = map[int64]*conn{}
	nextConnID int64
	banMu      sync.Mutex
	banned     = map[string]time.Time{}
)

const banFor = 10 * time.Minute

// registerConn ghi nhận một kết nối đang mở; hàm trả về dùng để gỡ khi kết thúc.
func registerConn(c *conn) func() {
	connMu.Lock()
	nextConnID++
	c.ID = nextConnID
	conns[c.ID] = c
	connMu.Unlock()
	return func() {
		connMu.Lock()
		delete(conns, c.ID)
		connMu.Unlock()
	}
}

func isBanned(email string) bool {
	email = strings.ToLower(email)
	banMu.Lock()
	defer banMu.Unlock()
	until, ok := banned[email]
	if ok && time.Now().After(until) {
		delete(banned, email)
		return false
	}
	return ok
}

// kickUser ngắt mọi kết nối của tài khoản và từ chối kết nối mới trong banFor (đủ lâu để API thu hồi phiên).
func kickUser(email string) int {
	email = strings.ToLower(email)
	banMu.Lock()
	banned[email] = time.Now().Add(banFor)
	banMu.Unlock()
	forgetUser(email)
	connMu.Lock()
	var victims []*conn
	for _, c := range conns {
		if strings.EqualFold(c.Email, email) {
			victims = append(victims, c)
		}
	}
	connMu.Unlock()
	for _, c := range victims {
		c.cancel()
	}
	return len(victims)
}

// ===== Xác thực quản trị =====

func adminAuth(w http.ResponseWriter, r *http.Request) (string, bool) {
	if r.Method == http.MethodOptions {
		w.WriteHeader(http.StatusNoContent)
		return "", false
	}
	if !originAllowed(r) {
		probeWriteJSON(w, http.StatusForbidden, map[string]any{"ok": false, "error": "origin"})
		return "", false
	}
	if !rateAllow("admin:"+clientIP(r), 120, time.Minute) {
		probeWriteJSON(w, http.StatusTooManyRequests, map[string]any{"ok": false, "error": "quá nhiều yêu cầu"})
		return "", false
	}
	token := strings.TrimSpace(strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer "))
	e, err := lookupUser(r.Context(), token)
	if err != nil {
		probeWriteJSON(w, http.StatusUnauthorized, map[string]any{"ok": false, "error": err.Error()})
		return "", false
	}
	if !e.admin {
		probeWriteJSON(w, http.StatusForbidden, map[string]any{"ok": false, "error": "không có quyền quản trị"})
		return "", false
	}
	return e.email, true
}

func readJSON(r *http.Request, v any) bool {
	return json.NewDecoder(io.LimitReader(r.Body, 8<<10)).Decode(v) == nil
}

// ===== Handlers =====

func handleAdminConns(w http.ResponseWriter, r *http.Request) {
	if _, ok := adminAuth(w, r); !ok {
		return
	}
	connMu.Lock()
	list := make([]map[string]any, 0, len(conns))
	for _, c := range conns {
		list = append(list, map[string]any{
			"id": c.ID, "email": c.Email, "ip": c.IP, "host": c.Host, "port": c.Port,
			"since": c.Start.UTC().Format(time.RFC3339), "seconds": int(time.Since(c.Start).Seconds()),
		})
	}
	connMu.Unlock()
	sort.Slice(list, func(i, j int) bool { return list[i]["id"].(int64) > list[j]["id"].(int64) })
	probeWriteJSON(w, http.StatusOK, map[string]any{"ok": true, "conns": list})
}

func handleAdminKick(w http.ResponseWriter, r *http.Request) {
	admin, ok := adminAuth(w, r)
	if !ok {
		return
	}
	var req struct {
		Email string `json:"email"`
	}
	if r.Method != http.MethodPost || !readJSON(r, &req) || strings.TrimSpace(req.Email) == "" {
		probeWriteJSON(w, http.StatusBadRequest, map[string]any{"ok": false, "error": "yêu cầu không hợp lệ"})
		return
	}
	email := strings.ToLower(strings.TrimSpace(req.Email))
	n := kickUser(email)
	log.Printf("admin %s kick %s (%d kết nối)", admin, email, n)
	probeWriteJSON(w, http.StatusOK, map[string]any{"ok": true, "kicked": n})
}

// logTail đọc tối đa ~2 MB cuối file log, lọc theo q (không phân biệt hoa thường), trả tối đa n dòng mới nhất.
func logTail(q string, n int) []string {
	if *logFilePath == "" {
		return nil
	}
	f, err := os.Open(*logFilePath)
	if err != nil {
		return nil
	}
	defer f.Close()
	st, err := f.Stat()
	if err != nil {
		return nil
	}
	const maxRead = 2 << 20
	off := int64(0)
	if st.Size() > maxRead {
		off = st.Size() - maxRead
	}
	_, _ = f.Seek(off, io.SeekStart)
	sc := bufio.NewScanner(f)
	sc.Buffer(make([]byte, 64<<10), 1<<20)
	q = strings.ToLower(q)
	var out []string
	first := off > 0
	for sc.Scan() {
		if first { // dòng đầu có thể bị cắt giữa chừng
			first = false
			continue
		}
		line := sc.Text()
		if q == "" || strings.Contains(strings.ToLower(line), q) {
			out = append(out, line)
			if len(out) > n {
				out = out[1:]
			}
		}
	}
	return out
}

func handleAdminLog(w http.ResponseWriter, r *http.Request) {
	if _, ok := adminAuth(w, r); !ok {
		return
	}
	n, _ := strconv.Atoi(r.URL.Query().Get("n"))
	if n <= 0 || n > 500 {
		n = 200
	}
	lines := logTail(strings.TrimSpace(r.URL.Query().Get("q")), n)
	if lines == nil {
		lines = []string{}
	}
	probeWriteJSON(w, http.StatusOK, map[string]any{"ok": true, "lines": lines})
}

// ===== Danh sách chặn động =====

var (
	dynMu    sync.Mutex
	entryRe  = regexp.MustCompile(`^[a-z0-9*][a-z0-9.\-_:/*]{0,251}$`)
	errNoDyn = "chưa cấu hình -blocklist-dynamic trên trạm"
)

func normEntry(s string) (string, bool) {
	s = strings.ToLower(strings.TrimSpace(s))
	if !entryRe.MatchString(s) {
		return "", false
	}
	if strings.Contains(s, "/") {
		if _, _, err := net.ParseCIDR(s); err != nil {
			return "", false
		}
	}
	return s, true
}

func dynEntries() []string {
	out := []string{}
	f, err := os.Open(*blocklistDyn)
	if err != nil {
		return out
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		if s := strings.TrimSpace(sc.Text()); s != "" && !strings.HasPrefix(s, "#") {
			out = append(out, s)
		}
	}
	return out
}

func writeDyn(entries []string) error {
	tmp := *blocklistDyn + ".tmp"
	if err := os.WriteFile(tmp, []byte(strings.Join(entries, "\n")+"\n"), 0o640); err != nil {
		return err
	}
	return os.Rename(tmp, *blocklistDyn)
}

func handleAdminBlocklist(w http.ResponseWriter, r *http.Request) {
	if _, ok := adminAuth(w, r); !ok {
		return
	}
	if *blocklistDyn == "" {
		probeWriteJSON(w, http.StatusServiceUnavailable, map[string]any{"ok": false, "error": errNoDyn})
		return
	}
	dynMu.Lock()
	list := dynEntries()
	dynMu.Unlock()
	probeWriteJSON(w, http.StatusOK, map[string]any{"ok": true, "entries": list})
}

func handleAdminBlock(remove bool) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		admin, ok := adminAuth(w, r)
		if !ok {
			return
		}
		if *blocklistDyn == "" {
			probeWriteJSON(w, http.StatusServiceUnavailable, map[string]any{"ok": false, "error": errNoDyn})
			return
		}
		var req struct {
			Entry string `json:"entry"`
		}
		if r.Method != http.MethodPost || !readJSON(r, &req) {
			probeWriteJSON(w, http.StatusBadRequest, map[string]any{"ok": false, "error": "yêu cầu không hợp lệ"})
			return
		}
		entry, valid := normEntry(req.Entry)
		if !valid {
			probeWriteJSON(w, http.StatusBadRequest, map[string]any{"ok": false, "error": "mục chặn không hợp lệ (tên máy, *.tên-miền, IP hoặc CIDR)"})
			return
		}
		dynMu.Lock()
		next := []string{}
		found := false
		for _, e := range dynEntries() {
			if strings.EqualFold(e, entry) {
				found = true
				if remove {
					continue
				}
			}
			next = append(next, e)
		}
		if !remove && !found {
			next = append(next, entry)
		}
		err := writeDyn(next)
		dynMu.Unlock()
		if err != nil {
			log.Printf("admin blocklist write: %v", err)
			probeWriteJSON(w, http.StatusInternalServerError, map[string]any{"ok": false, "error": "không ghi được danh sách chặn"})
			return
		}
		loadBlocklist()
		act := "block"
		if remove {
			act = "unblock"
		}
		log.Printf("admin %s %s %s", admin, act, entry)
		probeWriteJSON(w, http.StatusOK, map[string]any{"ok": true, "entries": next})
	}
}
