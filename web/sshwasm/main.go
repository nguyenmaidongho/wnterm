//go:build js && wasm

// WNSSH: SSH + SFTP chạy trong trình duyệt (WebAssembly) cho bản web của WN Term.
// Byte SSH đi qua trạm chuyển tiếp (wss://…/relay) nhưng được mã hóa đầu-cuối giữa trình duyệt và máy chủ SSH.
//
// JS: const s = await WNSSH.connect({ relayUrl, token, host, port, user, password, key, passphrase, cols, rows,
//                                      onData(u8), onClose(msg), verifyHostKey({type,fingerprint,host,port}) → Promise<bool>,
//                                      prompt(text, echo) → Promise<string|null> })
//     s.write(str|u8); s.resize(cols, rows); s.close();
//     await s.sftpList(path) → JSON; s.sftpRealpath(p); s.sftpRead(p) → u8; s.sftpWrite(p, u8); s.sftpMkdir(p); s.sftpRemove(p); s.sftpRename(a, b)
package main

import (
	"context"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"path"
	"sort"
	"strings"
	"os"
	"strconv"
	"sync"
	"sync/atomic"
	"syscall/js"
	"time"

	"github.com/coder/websocket"
	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"
)

func main() {
	js.Global().Set("WNSSH", js.ValueOf(map[string]any{
		"connect": js.FuncOf(connect),
		"version": "2",
	}))
	select {}
}

// ===== Cầu nối Promise =====

func jsError(err error) js.Value { return js.Global().Get("Error").New(err.Error()) }

func promise(fn func() (any, error)) js.Value {
	var handler js.Func
	handler = js.FuncOf(func(this js.Value, args []js.Value) any {
		resolve, reject := args[0], args[1]
		go func() {
			defer handler.Release()
			v, err := fn()
			if err != nil {
				reject.Invoke(jsError(err))
			} else {
				resolve.Invoke(v)
			}
		}()
		return nil
	})
	return js.Global().Get("Promise").New(handler)
}

// awaitJS chờ một Promise của JS (gọi từ goroutine, không được gọi trên luồng callback của JS).
func awaitJS(p js.Value) (js.Value, error) {
	if p.Type() != js.TypeObject || p.Get("then").Type() != js.TypeFunction {
		return p, nil
	}
	type result struct {
		v   js.Value
		err error
	}
	ch := make(chan result, 1)
	var ok, fail js.Func
	ok = js.FuncOf(func(this js.Value, args []js.Value) any {
		v := js.Undefined()
		if len(args) > 0 {
			v = args[0]
		}
		ch <- result{v: v}
		return nil
	})
	fail = js.FuncOf(func(this js.Value, args []js.Value) any {
		msg := "lỗi"
		if len(args) > 0 {
			msg = args[0].Call("toString").String()
		}
		ch <- result{err: errors.New(msg)}
		return nil
	})
	p.Call("then", ok, fail)
	r := <-ch
	ok.Release()
	fail.Release()
	return r.v, r.err
}

func toU8(b []byte) js.Value {
	u := js.Global().Get("Uint8Array").New(len(b))
	js.CopyBytesToJS(u, b)
	return u
}

func fromJSData(v js.Value) []byte {
	if v.Type() == js.TypeString {
		return []byte(v.String())
	}
	n := v.Get("length").Int()
	b := make([]byte, n)
	js.CopyBytesToGo(b, v)
	return b
}

func str(o js.Value, k string) string {
	v := o.Get(k)
	if v.Type() == js.TypeString {
		return v.String()
	}
	return ""
}

func num(o js.Value, k string, def int) int {
	v := o.Get(k)
	if v.Type() == js.TypeNumber {
		return v.Int()
	}
	return def
}

// ===== Kết nối =====

type session struct {
	client *ssh.Client
	sess   *ssh.Session
	stdin  io.WriteCloser
	in     chan []byte
	sftpMu sync.Mutex
	sftp   *sftp.Client
	cancel atomic.Bool // người dùng bấm Hủy khi đang truyền file
	once   sync.Once
	closed chan struct{}
}

// stdout chuyển dữ liệu terminal sang JS.
type jsWriter struct{ fn js.Value }

func (w jsWriter) Write(p []byte) (int, error) {
	if len(p) > 0 && w.fn.Type() == js.TypeFunction {
		w.fn.Invoke(toU8(p))
	}
	return len(p), nil
}

func algorithms() (kex, ciphers, macs, hostKeys []string) {
	sup, insec := ssh.SupportedAlgorithms(), ssh.InsecureAlgorithms()
	// Ưu tiên thuật toán an toàn; vẫn chấp nhận thuật toán cũ để nói chuyện được với máy chủ đời cũ.
	return append(sup.KeyExchanges, insec.KeyExchanges...),
		append(sup.Ciphers, insec.Ciphers...),
		append(sup.MACs, insec.MACs...),
		append(sup.HostKeys, insec.HostKeys...)
}

func connect(this js.Value, args []js.Value) any {
	if len(args) < 1 {
		return promise(func() (any, error) { return nil, errors.New("thiếu tham số") })
	}
	o := args[0]
	return promise(func() (any, error) {
		s, err := dial(o)
		if err != nil {
			return nil, err
		}
		return s.jsObject(o), nil
	})
}

func dial(o js.Value) (*session, error) {
	relayURL, token := str(o, "relayUrl"), str(o, "token")
	host, user := strings.TrimSpace(str(o, "host")), strings.TrimSpace(str(o, "user"))
	port := num(o, "port", 22)
	if host == "" || user == "" {
		return nil, errors.New("thiếu máy chủ hoặc user")
	}
	status := func(msg string) {
		if f := o.Get("onStatus"); f.Type() == js.TypeFunction {
			f.Invoke(msg)
		}
	}

	// 1) WebSocket tới trạm chuyển tiếp
	status("relay")
	ctx := context.Background()
	dctx, cancel := context.WithTimeout(ctx, 20*time.Second)
	ws, _, err := websocket.Dial(dctx, relayURL, nil)
	cancel()
	if err != nil {
		return nil, fmt.Errorf("không kết nối được trạm chuyển tiếp: %v", err)
	}
	ws.SetReadLimit(4 << 20)
	hello, _ := json.Marshal(map[string]any{"token": token, "host": host, "port": port})
	if err := ws.Write(ctx, websocket.MessageText, hello); err != nil {
		ws.CloseNow()
		return nil, err
	}
	rctx, cancel := context.WithTimeout(ctx, 30*time.Second)
	typ, data, err := ws.Read(rctx)
	cancel()
	if err != nil || typ != websocket.MessageText {
		ws.CloseNow()
		return nil, errors.New("trạm chuyển tiếp không phản hồi")
	}
	var rep struct {
		OK    bool   `json:"ok"`
		Error string `json:"error"`
	}
	_ = json.Unmarshal(data, &rep)
	if !rep.OK {
		ws.CloseNow()
		if rep.Error == "" {
			rep.Error = "trạm chuyển tiếp từ chối kết nối"
		}
		return nil, errors.New(rep.Error)
	}
	conn := websocket.NetConn(ctx, ws, websocket.MessageBinary)

	// 2) SSH
	status("ssh")
	password, keyPEM, passphrase := str(o, "password"), str(o, "key"), str(o, "passphrase")
	ask := func(q string, echo bool) (string, bool) {
		f := o.Get("prompt")
		if f.Type() != js.TypeFunction {
			return "", false
		}
		v, err := awaitJS(f.Invoke(q, echo))
		if err != nil || v.Type() != js.TypeString {
			return "", false
		}
		return v.String(), true
	}

	var auths []ssh.AuthMethod
	if keyPEM != "" {
		var signer ssh.Signer
		signer, err = ssh.ParsePrivateKey([]byte(keyPEM))
		var missing *ssh.PassphraseMissingError
		if errors.As(err, &missing) {
			if passphrase == "" {
				passphrase, _ = ask("Passphrase của SSH key:", false)
			}
			signer, err = ssh.ParsePrivateKeyWithPassphrase([]byte(keyPEM), []byte(passphrase))
		}
		if err == nil {
			auths = append(auths, ssh.PublicKeys(signer))
		} else {
			status("keyerror:" + err.Error())
		}
	}
	triedPw := false
	getPw := func() (string, error) {
		if password != "" && !triedPw {
			triedPw = true
			return password, nil
		}
		p, ok := ask(fmt.Sprintf("Mật khẩu cho %s@%s:", user, host), false)
		if !ok {
			return "", errors.New("đã hủy")
		}
		password = p
		return p, nil
	}
	auths = append(auths,
		ssh.RetryableAuthMethod(ssh.PasswordCallback(getPw), 3),
		ssh.RetryableAuthMethod(ssh.KeyboardInteractive(func(name, instruction string, questions []string, echos []bool) ([]string, error) {
			answers := make([]string, len(questions))
			for i, q := range questions {
				ql := strings.ToLower(q)
				if !echos[i] && strings.Contains(ql, "password") && password != "" && !triedPw {
					triedPw = true
					answers[i] = password
					continue
				}
				a, ok := ask(strings.TrimSpace(instruction+"\n"+q), echos[i])
				if !ok {
					return nil, errors.New("đã hủy")
				}
				answers[i] = a
			}
			return answers, nil
		}), 3),
	)

	kex, ciphers, macs, hostKeys := algorithms()
	cfg := &ssh.ClientConfig{
		User:              user,
		Auth:              auths,
		HostKeyAlgorithms: hostKeys,
		Timeout:           30 * time.Second,
		HostKeyCallback: func(hostname string, remote net.Addr, key ssh.PublicKey) error {
			sum := sha256.Sum256(key.Marshal())
			fp := "SHA256:" + strings.TrimRight(base64.StdEncoding.EncodeToString(sum[:]), "=")
			f := o.Get("verifyHostKey")
			if f.Type() != js.TypeFunction {
				return nil
			}
			info := js.ValueOf(map[string]any{"type": key.Type(), "fingerprint": fp, "host": host, "port": port})
			v, err := awaitJS(f.Invoke(info))
			if err != nil || !v.Truthy() {
				return errors.New("đã từ chối khóa máy chủ (host key)")
			}
			return nil
		},
	}
	cfg.KeyExchanges, cfg.Ciphers, cfg.MACs = kex, ciphers, macs

	c, chans, reqs, err := ssh.NewClientConn(conn, net.JoinHostPort(host, fmt.Sprint(port)), cfg)
	if err != nil {
		conn.Close()
		msg := err.Error()
		if strings.Contains(msg, "unable to authenticate") {
			msg = "sai user/mật khẩu hoặc key (máy chủ từ chối đăng nhập)"
		}
		return nil, errors.New(msg)
	}
	client := ssh.NewClient(c, chans, reqs)

	// 3) Terminal
	status("shell")
	sess, err := client.NewSession()
	if err != nil {
		client.Close()
		return nil, err
	}
	modes := ssh.TerminalModes{ssh.ECHO: 1, ssh.TTY_OP_ISPEED: 115200, ssh.TTY_OP_OSPEED: 115200}
	if err := sess.RequestPty("xterm-256color", num(o, "rows", 24), num(o, "cols", 80), modes); err != nil {
		client.Close()
		return nil, err
	}
	_ = sess.Setenv("LANG", "en_US.UTF-8")
	stdin, err := sess.StdinPipe()
	if err != nil {
		client.Close()
		return nil, err
	}
	out := jsWriter{fn: o.Get("onData")}
	sess.Stdout, sess.Stderr = out, out
	if err := sess.Shell(); err != nil {
		client.Close()
		return nil, err
	}

	s := &session{client: client, sess: sess, stdin: stdin, in: make(chan []byte, 1024), closed: make(chan struct{})}
	go func() { // ghi bàn phím theo thứ tự, không chặn luồng JS
		for b := range s.in {
			if _, err := s.stdin.Write(b); err != nil {
				return
			}
		}
	}()
	go func() { // giữ kết nối sống
		t := time.NewTicker(20 * time.Second)
		defer t.Stop()
		for {
			select {
			case <-s.closed:
				return
			case <-t.C:
				if _, _, err := client.SendRequest("keepalive@openssh.com", true, nil); err != nil {
					return
				}
			}
		}
	}()
	go func() {
		err := sess.Wait()
		reason := "Đã ngắt kết nối"
		if err != nil {
			var exit *ssh.ExitError
			if !errors.As(err, &exit) {
				reason = "Mất kết nối: " + err.Error()
			}
		}
		s.close()
		if f := o.Get("onClose"); f.Type() == js.TypeFunction {
			f.Invoke(reason)
		}
	}()
	return s, nil
}

func (s *session) close() {
	s.once.Do(func() {
		close(s.closed)
		close(s.in)
		s.sftpMu.Lock()
		if s.sftp != nil {
			s.sftp.Close()
		}
		s.sftpMu.Unlock()
		s.sess.Close()
		s.client.Close()
	})
}

func (s *session) getSftp() (*sftp.Client, error) {
	s.sftpMu.Lock()
	defer s.sftpMu.Unlock()
	if s.sftp == nil {
		c, err := sftp.NewClient(s.client)
		if err != nil {
			return nil, fmt.Errorf("máy chủ không hỗ trợ SFTP: %v", err)
		}
		s.sftp = c
	}
	return s.sftp, nil
}

type entry struct {
	Name  string `json:"name"`
	Size  int64  `json:"size"`
	Dir   bool   `json:"dir"`
	Link  bool   `json:"link"`
	Mode  string `json:"mode"`
	MTime int64  `json:"mtime"`
}

func (s *session) jsObject(o js.Value) js.Value {
	fn := func(f func(args []js.Value) (any, error)) js.Func {
		return js.FuncOf(func(this js.Value, args []js.Value) any {
			return promise(func() (any, error) { return f(args) })
		})
	}
	arg := func(args []js.Value, i int) string {
		if i < len(args) && args[i].Type() == js.TypeString {
			return args[i].String()
		}
		return ""
	}
	return js.ValueOf(map[string]any{
		"write": js.FuncOf(func(this js.Value, args []js.Value) any {
			if len(args) > 0 {
				b := fromJSData(args[0])
				select {
				case <-s.closed:
				default:
					func() {
						defer func() { recover() }() // kênh đã đóng
						s.in <- b
					}()
				}
			}
			return nil
		}),
		"resize": js.FuncOf(func(this js.Value, args []js.Value) any {
			if len(args) >= 2 {
				cols, rows := args[0].Int(), args[1].Int()
				go s.sess.WindowChange(rows, cols)
			}
			return nil
		}),
		"close": js.FuncOf(func(this js.Value, args []js.Value) any {
			go s.close()
			return nil
		}),
		// exec(cmd): chạy một lệnh trên kênh riêng (không ảnh hưởng terminal), trả stdout — dùng cho thanh theo dõi CPU/RAM.
		"exec": fn(func(args []js.Value) (any, error) {
			sess, err := s.client.NewSession()
			if err != nil {
				return nil, err
			}
			defer sess.Close()
			type res struct {
				out []byte
				err error
			}
			ch := make(chan res, 1)
			go func() { o, e := sess.Output(arg(args, 0)); ch <- res{o, e} }()
			select {
			case r := <-ch:
				if r.err != nil && len(r.out) == 0 {
					return nil, r.err
				}
				return string(r.out), nil
			case <-time.After(15 * time.Second):
				return nil, errors.New("hết thời gian chờ")
			}
		}),
		"sftpRealpath": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			p := arg(args, 0)
			if p == "" {
				p = "."
			}
			return c.RealPath(p)
		}),
		"sftpList": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			infos, err := c.ReadDir(arg(args, 0))
			if err != nil {
				return nil, err
			}
			list := make([]entry, 0, len(infos))
			for _, fi := range infos {
				e := entry{Name: fi.Name(), Size: fi.Size(), Dir: fi.IsDir(), Mode: fi.Mode().String(), MTime: fi.ModTime().Unix()}
				if fi.Mode()&0o20000000000 != 0 || strings.HasPrefix(fi.Mode().String(), "L") {
					e.Link = true
					if st, err := c.Stat(path.Join(arg(args, 0), fi.Name())); err == nil {
						e.Dir = st.IsDir()
					}
				}
				list = append(list, e)
			}
			sort.Slice(list, func(i, j int) bool {
				if list[i].Dir != list[j].Dir {
					return list[i].Dir
				}
				return strings.ToLower(list[i].Name) < strings.ToLower(list[j].Name)
			})
			b, _ := json.Marshal(list)
			return string(b), nil
		}),
		// sftpRead(path, onProgress?) → Uint8Array. onProgress(đã_đọc, tổng).
		"sftpRead": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			f, err := c.Open(arg(args, 0))
			if err != nil {
				return nil, err
			}
			defer f.Close()
			var total int64
			if st, err := f.Stat(); err == nil {
				total = st.Size()
			}
			if total > 300<<20 {
				return nil, errors.New("file quá lớn để tải bằng bản web (tối đa 300 MB)")
			}
			var cb js.Value
			if len(args) > 1 && args[1].Type() == js.TypeFunction {
				cb = args[1]
			}
			buf := make([]byte, 0, total+1)
			chunk := make([]byte, 256<<10)
			for {
				if s.cancel.Load() {
					return nil, errors.New("đã hủy")
				}
				n, err := f.Read(chunk)
				buf = append(buf, chunk[:n]...)
				if cb.Type() == js.TypeFunction && n > 0 {
					cb.Invoke(len(buf), total)
				}
				if err == io.EOF {
					break
				}
				if err != nil {
					return nil, err
				}
			}
			return toU8(buf), nil
		}),
		// sftpWrite(path, data, onProgress?, inPlace?) — mặc định ghi vào file tạm rồi đổi tên (lỗi giữa chừng không làm hỏng file cũ,
		// giữ nguyên quyền file cũ). inPlace=true: ghi thẳng vào file hiện có (dùng khi sửa văn bản, giữ chủ sở hữu/liên kết).
		"sftpWrite": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			if len(args) < 2 {
				return nil, errors.New("thiếu dữ liệu")
			}
			target := arg(args, 0)
			data := fromJSData(args[1])
			var cb js.Value
			if len(args) > 2 && args[2].Type() == js.TypeFunction {
				cb = args[2]
			}
			inPlace := len(args) > 3 && args[3].Truthy()
			tmp := target + ".wnpart"
			if inPlace {
				tmp = target
			}
			f, err := c.OpenFile(tmp, os.O_WRONLY|os.O_CREATE|os.O_TRUNC)
			if err != nil {
				return nil, err
			}
			const step = 256 << 10
			for off := 0; off < len(data); off += step {
				if s.cancel.Load() {
					f.Close()
					if !inPlace {
						c.Remove(tmp)
					}
					return nil, errors.New("đã hủy")
				}
				end := off + step
				if end > len(data) {
					end = len(data)
				}
				if _, err := f.Write(data[off:end]); err != nil {
					f.Close()
					if !inPlace {
						c.Remove(tmp)
					}
					return nil, err
				}
				if cb.Type() == js.TypeFunction {
					cb.Invoke(end, len(data))
				}
			}
			f.Close()
			if inPlace {
				return true, nil
			}
			if st, err := c.Stat(target); err == nil {
				_ = c.Chmod(tmp, st.Mode().Perm()) // giữ nguyên quyền của file bị ghi đè (vd. script chạy được)
			}
			if err := c.PosixRename(tmp, target); err != nil {
				_ = c.Remove(target)
				if err2 := c.Rename(tmp, target); err2 != nil {
					c.Remove(tmp)
					return nil, err2
				}
			}
			return true, nil
		}),
		// sftpChmod(path, quyền) — quyền viết dạng số bát phân như 755 hoặc 644.
		"sftpChmod": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			m, err := strconv.ParseUint(arg(args, 1), 8, 32)
			if err != nil || m > 0o7777 {
				return nil, errors.New("quyền không hợp lệ")
			}
			return true, c.Chmod(arg(args, 0), os.FileMode(m))
		}),
		"cancelTransfers": js.FuncOf(func(this js.Value, args []js.Value) any { s.cancel.Store(true); return nil }),
		"beginTransfer":   js.FuncOf(func(this js.Value, args []js.Value) any { s.cancel.Store(false); return nil }),
		"sftpMkdir": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			return true, c.Mkdir(arg(args, 0))
		}),
		"sftpRemove": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			p := arg(args, 0)
			st, err := c.Lstat(p)
			if err != nil {
				return nil, err
			}
			if st.IsDir() {
				return true, c.RemoveAll(p)
			}
			return true, c.Remove(p)
		}),
		"sftpRename": fn(func(args []js.Value) (any, error) {
			c, err := s.getSftp()
			if err != nil {
				return nil, err
			}
			return true, c.Rename(arg(args, 0), arg(args, 1))
		}),
	})
}
