package main

// POST /relay/probe — kiểm tra "VM có đang mở cổng không" cho danh sách VM trong PWA.
//
// Trình duyệt không mở được kết nối TCP thô, nên trạm làm hộ: với mỗi đích {host,port} chỉ thử kết nối TCP
// (không gửi, không đọc byte nào) rồi đóng ngay, trả {host,port,ok,ms}. Dùng đúng các kiểm tra an toàn của /relay:
// token tài khoản, chặn địa chỉ nội bộ, quy tắc cổng khi đích là chính máy này. Giới hạn chặt: tối đa 40 đích mỗi lần,
// mỗi tài khoản 1 lần / 20 giây, tối đa vài lượt kiểm tra chạy cùng lúc toàn trạm.

import (
	"context"
	"encoding/json"
	"io"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"time"
)

const (
	probeMaxTargets  = 40
	probeDialTimeout = 2500 * time.Millisecond
	probeParallel    = 10
	probeMaxBody     = 16 << 10
)

var (
	probeInterval = 20 * time.Second
	probeMu       sync.Mutex
	probeLast     = map[string]time.Time{}
	probeSlots    = make(chan struct{}, 4) // tối đa 4 lượt kiểm tra đồng thời toàn trạm
)

type probeTarget struct {
	Host string `json:"host"`
	Port int    `json:"port"`
}

type probeRequest struct {
	Token   string        `json:"token"`
	Targets []probeTarget `json:"targets"`
}

type probeResult struct {
	Host  string `json:"host"`
	Port  int    `json:"port"`
	OK    bool   `json:"ok"`
	MS    int    `json:"ms"`
	Error string `json:"error,omitempty"`
}

func probeWriteJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}

// originAllowed áp dụng cùng danh sách Origin như WebSocket: không có Origin (curl, cùng nguồn) thì cho qua.
func originAllowed(r *http.Request) bool {
	o := r.Header.Get("Origin")
	if o == "" || strings.TrimSpace(*origins) == "" {
		return true
	}
	u, err := url.Parse(o)
	if err != nil || u.Host == "" {
		return false
	}
	if strings.EqualFold(u.Host, r.Host) {
		return true
	}
	for _, p := range strings.Split(*origins, ",") {
		if p = strings.TrimSpace(p); p != "" && strings.EqualFold(p, u.Host) {
			return true
		}
	}
	return false
}

// probeAllow trả false (kèm số giây phải đợi) nếu tài khoản vừa kiểm tra chưa đủ probeInterval.
func probeAllow(email string) (bool, int) {
	probeMu.Lock()
	defer probeMu.Unlock()
	now := time.Now()
	if last, ok := probeLast[email]; ok && now.Sub(last) < probeInterval {
		return false, int((probeInterval-now.Sub(last))/time.Second) + 1
	}
	if len(probeLast) > 5000 {
		for k, v := range probeLast {
			if now.Sub(v) > probeInterval {
				delete(probeLast, k)
			}
		}
	}
	probeLast[email] = now
	return true, 0
}

func probeOne(ctx context.Context, t probeTarget) probeResult {
	res := probeResult{Host: t.Host, Port: t.Port}
	tctx, cancel := context.WithTimeout(ctx, probeDialTimeout+3*time.Second) // gồm cả phân giải tên
	defer cancel()
	target, err := resolveTarget(tctx, t.Host, t.Port)
	if err != nil {
		res.Error = "blocked"
		return res
	}
	d := net.Dialer{Timeout: probeDialTimeout}
	start := time.Now()
	c, err := d.DialContext(tctx, "tcp", target)
	if err != nil {
		res.Error = "unreachable"
		return res
	}
	res.MS = int(time.Since(start) / time.Millisecond)
	if res.MS < 1 {
		res.MS = 1
	}
	res.OK = true
	_ = c.Close() // không gửi/đọc gì
	return res
}

func handleProbe(w http.ResponseWriter, r *http.Request) {
	if r.Method == http.MethodOptions {
		w.WriteHeader(http.StatusNoContent)
		return
	}
	if r.Method != http.MethodPost {
		probeWriteJSON(w, http.StatusMethodNotAllowed, map[string]any{"ok": false, "error": "method"})
		return
	}
	if !originAllowed(r) {
		probeWriteJSON(w, http.StatusForbidden, map[string]any{"ok": false, "error": "origin"})
		return
	}
	if !rateAllow("probeip:"+clientIP(r), 12, time.Minute) {
		probeWriteJSON(w, http.StatusTooManyRequests, map[string]any{"ok": false, "error": "quá nhiều yêu cầu kiểm tra", "retryAfter": 30})
		return
	}
	var req probeRequest
	if err := json.NewDecoder(io.LimitReader(r.Body, probeMaxBody)).Decode(&req); err != nil {
		probeWriteJSON(w, http.StatusBadRequest, map[string]any{"ok": false, "error": "yêu cầu không hợp lệ"})
		return
	}
	if len(req.Targets) == 0 || len(req.Targets) > probeMaxTargets {
		probeWriteJSON(w, http.StatusBadRequest, map[string]any{"ok": false, "error": "tối đa 40 đích mỗi lần"})
		return
	}
	email, err := checkToken(r.Context(), req.Token)
	if err != nil {
		probeWriteJSON(w, http.StatusUnauthorized, map[string]any{"ok": false, "error": err.Error()})
		return
	}
	if ok, wait := probeAllow(email); !ok {
		w.Header().Set("Retry-After", strconv.Itoa(wait))
		probeWriteJSON(w, http.StatusTooManyRequests, map[string]any{"ok": false, "error": "quá nhiều yêu cầu kiểm tra", "retryAfter": wait})
		return
	}
	select {
	case probeSlots <- struct{}{}:
		defer func() { <-probeSlots }()
	default:
		probeWriteJSON(w, http.StatusServiceUnavailable, map[string]any{"ok": false, "error": "trạm đang bận"})
		return
	}

	results := make([]probeResult, len(req.Targets))
	sem := make(chan struct{}, probeParallel)
	var wg sync.WaitGroup
	cache := map[string]int{} // đích trùng nhau chỉ thử một lần
	var cmu sync.Mutex
	for i, t := range req.Targets {
		t.Host = strings.TrimSpace(t.Host)
		key := strings.ToLower(t.Host) + ":" + strconv.Itoa(t.Port)
		cmu.Lock()
		_, dup := cache[key]
		if !dup {
			cache[key] = i
		}
		cmu.Unlock()
		if dup {
			results[i].Host, results[i].Port = t.Host, t.Port
			continue
		}
		wg.Add(1)
		sem <- struct{}{}
		go func(i int, t probeTarget) {
			defer wg.Done()
			defer func() { <-sem }()
			results[i] = probeOne(r.Context(), t)
		}(i, t)
	}
	wg.Wait()
	// điền kết quả cho các đích trùng
	for i, t := range req.Targets {
		key := strings.ToLower(strings.TrimSpace(t.Host)) + ":" + strconv.Itoa(t.Port)
		if first := cache[key]; first != i {
			results[i] = results[first]
			results[i].Host, results[i].Port = t.Host, t.Port
		}
	}
	probeWriteJSON(w, http.StatusOK, map[string]any{"ok": true, "results": results})
}
