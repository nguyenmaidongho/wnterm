package main

import (
	"bytes"
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"strconv"
	"testing"
	"time"
)

func setupProbe(t *testing.T) {
	api := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Authorization") == "Bearer good-token-aaaaaaaaaaaaaaaaaaaa" {
			w.Write([]byte(`{"ok":true,"email":"a@b.c"}`))
			return
		}
		w.WriteHeader(401)
		w.Write([]byte(`{"ok":false}`))
	}))
	t.Cleanup(api.Close)
	*apiBase = api.URL
	httpc = api.Client()
	authCache = map[string]authEntry{}
	probeLast = map[string]time.Time{}
	*origins = "wnterm.webnow.vn"
}

func doProbe(t *testing.T, token, origin string, targets []probeTarget) (int, map[string]any) {
	body, _ := json.Marshal(probeRequest{Token: token, Targets: targets})
	req := httptest.NewRequest(http.MethodPost, "/relay/probe", bytes.NewReader(body))
	if origin != "" {
		req.Header.Set("Origin", origin)
	}
	rec := httptest.NewRecorder()
	handleProbe(rec, req)
	var out map[string]any
	json.Unmarshal(rec.Body.Bytes(), &out)
	return rec.Code, out
}

func TestProbe(t *testing.T) {
	setupProbe(t)
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	go func() {
		for {
			c, err := ln.Accept()
			if err != nil {
				return
			}
			c.Close()
		}
	}()
	port := ln.Addr().(*net.TCPAddr).Port
	closed, _ := net.Listen("tcp", "127.0.0.1:0")
	closedPort := closed.Addr().(*net.TCPAddr).Port
	closed.Close()
	tok := "good-token-aaaaaaaaaaaaaaaaaaaa"

	// internal blocked without -allow-private
	*allowPrivate = false
	code, out := doProbe(t, tok, "", []probeTarget{{"127.0.0.1", port}})
	if code != 200 || out["results"].([]any)[0].(map[string]any)["ok"] != false {
		t.Fatalf("internal must be blocked: %v %v", code, out)
	}
	probeLast = map[string]time.Time{}

	*allowPrivate = true
	code, out = doProbe(t, tok, "https://wnterm.webnow.vn", []probeTarget{{"127.0.0.1", port}, {"127.0.0.1", closedPort}, {"127.0.0.1", port}})
	if code != 200 {
		t.Fatalf("code %d %v", code, out)
	}
	res := out["results"].([]any)
	r0, r1, r2 := res[0].(map[string]any), res[1].(map[string]any), res[2].(map[string]any)
	if r0["ok"] != true || r0["ms"].(float64) < 1 || r1["ok"] != false || r2["ok"] != true {
		t.Fatalf("results %v", res)
	}
	if int(r0["port"].(float64)) != port {
		t.Fatalf("port echo")
	}
	t.Logf("open=%v ms closed=%v", r0["ms"], r1["error"])

	// rate limit: second call within 20s
	code, out = doProbe(t, tok, "", []probeTarget{{"127.0.0.1", port}})
	if code != 429 {
		t.Fatalf("expected 429 got %d %v", code, out)
	}
	probeLast = map[string]time.Time{}
	// bad token
	if code, _ = doProbe(t, "bad-token-bbbbbbbbbbbbbbbbbbbbbb", "", []probeTarget{{"127.0.0.1", port}}); code != 401 {
		t.Fatalf("expected 401 got %d", code)
	}
	// bad origin
	if code, _ = doProbe(t, tok, "https://evil.example", []probeTarget{{"127.0.0.1", port}}); code != 403 {
		t.Fatalf("expected 403 got %d", code)
	}
	// too many targets
	many := make([]probeTarget, 41)
	for i := range many {
		many[i] = probeTarget{"127.0.0.1", 1000 + i}
	}
	if code, _ = doProbe(t, tok, "", many); code != 400 {
		t.Fatalf("expected 400 got %d", code)
	}
	// 40 targets incl. closed ones finish quickly
	probeLast = map[string]time.Time{}
	for i := range many {
		many[i] = probeTarget{"127.0.0.1", closedPort + i%3}
	}
	start := time.Now()
	if code, _ = doProbe(t, tok, "", many[:40]); code != 200 {
		t.Fatalf("40 targets: %d", code)
	}
	t.Logf("40 targets in %v", time.Since(start))
	_ = strconv.Itoa
}
