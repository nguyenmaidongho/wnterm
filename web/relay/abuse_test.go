package main

import (
	"net"
	"os"
	"testing"
	"time"
)

func TestRateAllow(t *testing.T) {
	for i := 0; i < 3; i++ {
		if !rateAllow("t1", 3, time.Minute) {
			t.Fatalf("hit %d should pass", i)
		}
	}
	if rateAllow("t1", 3, time.Minute) {
		t.Fatal("4th hit should be blocked")
	}
}

func TestSpread(t *testing.T) {
	if !spreadAllow("u", "a:22", 2) || !spreadAllow("u", "b:22", 2) || !spreadAllow("u", "a:22", 2) {
		t.Fatal("first two distinct + repeat should pass")
	}
	if spreadAllow("u", "c:22", 2) {
		t.Fatal("third distinct should be blocked")
	}
}

func TestFailLock(t *testing.T) {
	*failMax = 3
	locked := false
	for i := 0; i < 3; i++ {
		locked = failRecord("u2", "x:22")
	}
	if !locked || !failLocked("u2", "x:22") {
		t.Fatal("should lock after 3 fails")
	}
	if failLocked("u2", "y:22") {
		t.Fatal("other target must not be locked")
	}
}

func TestBlocklist(t *testing.T) {
	f, _ := os.CreateTemp("", "bl")
	f.WriteString("# c\nbad.example.com\n*.evil.org\n203.0.113.7\n198.51.100.0/24 # net\n")
	f.Close()
	defer os.Remove(f.Name())
	*blocklistPath = f.Name()
	loadBlocklist()
	cases := []struct {
		h  string
		ip string
		w  bool
	}{
		{"bad.example.com", "1.1.1.1", true},
		{"BAD.example.com.", "1.1.1.1", true},
		{"a.evil.org", "1.1.1.1", true},
		{"evil.org", "1.1.1.1", true},
		{"good.example.com", "203.0.113.7", true},
		{"good.example.com", "198.51.100.200", true},
		{"good.example.com", "8.8.8.8", false},
		{"notevil.org", "8.8.8.8", false},
	}
	for _, c := range cases {
		if got := isBlocked(c.h, net.ParseIP(c.ip)); got != c.w {
			t.Errorf("%s %s: got %v want %v", c.h, c.ip, got, c.w)
		}
	}
}

func TestParseCreated(t *testing.T) {
	if parseCreated("2026-10-09 12:00:00Z").IsZero() || parseCreated("2026-10-09T12:00:00Z").IsZero() {
		t.Fatal("parse failed")
	}
	if !isNewAccount(time.Time{}) || isNewAccount(time.Now().Add(-48*time.Hour)) || !isNewAccount(time.Now().Add(-time.Hour)) {
		t.Fatal("isNewAccount wrong")
	}
}

func TestKickAndBan(t *testing.T) {
	cancelled := 0
	un := registerConn(&conn{Email: "a@x.vn", cancel: func() { cancelled++ }})
	un2 := registerConn(&conn{Email: "b@x.vn", cancel: func() { t.Fatal("must not cancel other users") }})
	if n := kickUser("A@x.vn"); n != 1 || cancelled != 1 {
		t.Fatalf("kick: n=%d cancelled=%d", n, cancelled)
	}
	if !isBanned("a@x.vn") || isBanned("b@x.vn") {
		t.Fatal("ban state wrong")
	}
	un()
	un2()
}

func TestNormEntry(t *testing.T) {
	ok := []string{"Evil.Example.com", "*.evil.org", "203.0.113.7", "198.51.100.0/24"}
	bad := []string{"", "a b", "x;rm", "1.2.3.4/99", "../etc", "-bad"}
	for _, s := range ok {
		if _, v := normEntry(s); !v {
			t.Errorf("%q should be valid", s)
		}
	}
	for _, s := range bad {
		if _, v := normEntry(s); v {
			t.Errorf("%q should be invalid", s)
		}
	}
}

func TestDynBlocklistAndLogTail(t *testing.T) {
	d := t.TempDir()
	*blocklistDyn = d + "/dyn.txt"
	*blocklistPath = ""
	if err := writeDyn([]string{"bad.example.com", "203.0.113.0/24"}); err != nil {
		t.Fatal(err)
	}
	loadBlocklist()
	if !isBlocked("bad.example.com", nil) || !isBlocked("x.com", net.ParseIP("203.0.113.9")) || isBlocked("ok.com", net.ParseIP("8.8.8.8")) {
		t.Fatal("dynamic blocklist not applied")
	}
	*logFilePath = d + "/relay.log"
	os.WriteFile(*logFilePath, []byte("l1 open\nl2 abuse ip-rate\nl3 open\nl4 abuse locked\n"), 0o640)
	got := logTail("ABUSE", 10)
	if len(got) != 2 || got[1] != "l4 abuse locked" {
		t.Fatalf("logTail: %v", got)
	}
	if got := logTail("", 3); len(got) != 3 || got[0] != "l2 abuse ip-rate" {
		t.Fatalf("logTail n: %v", got)
	}
	*logFilePath = ""
}
