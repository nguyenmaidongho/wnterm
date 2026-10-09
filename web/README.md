# WN Term bản web (PWA) + trạm chuyển tiếp

Dành cho iPhone/iPad/trình duyệt. SSH chạy **trong trình duyệt** (Go → WebAssembly); byte SSH đã mã hóa đi qua trạm chuyển tiếp `wss://wnterm.webnow.vn/relay`.

| Thư mục | Nội dung |
|---|---|
| `pwa/` | Giao diện: `index.html`, `app.js` (logic), `i18n.js` (Tiếng Việt/English), `ui.js` (hộp thoại/icon), `vault.js` (kho VM + đồng bộ — cùng quy tắc `AccountService.cs`), `wnfile.js` (.wnterm, MobaXterm, ZIP), `app.css`, `sw.js`, manifest, icon |
| `sshwasm/` | Bộ SSH/SFTP biên dịch ra `wnssh.wasm` (x/crypto/ssh + pkg/sftp). Đổi API thì tăng `version` trong `main.go` **và** `WASM_API` trong `app.js` |
| `relay/` | Trạm chuyển tiếp WebSocket→SSH (Go). Kiểm tra token tài khoản, chặn địa chỉ nội bộ, giới hạn kết nối; thêm `POST /relay/probe` (chấm xanh/xám + độ trễ) |
| `build.sh` | Build tất cả → `dist/app` (đưa lên `public_html/app`) + `dist/wnterm-relay-linux-amd64` |
| `install-relay.sh` | Chạy bằng root trên máy chủ DirectAdmin: tạo user `wnrelay`, dịch vụ systemd, `location /relay` trong nginx |

## Build
Cần Go (cài người dùng: `. /c/Users/nguye/.dotnet-user/goenv.sh`) và Python (để gắn `?v=` vào file tĩnh).
```
bash web/build.sh
```
Mỗi bản build có mã phiên bản riêng gắn vào URL của mọi file tĩnh và tên cache của service worker → Cloudflare/iPhone luôn lấy bản mới.

## Triển khai (user `wnterm`, không cần root)
```
scp -r web/dist/app wnterm@HOST:domains/wnterm.webnow.vn/public_html/app.new
ssh wnterm@HOST 'cd ~/domains/wnterm.webnow.vn/public_html && rm -rf app.old && mv app app.old && mv app.new app && rm -rf app.old'
```
Trạm chuyển tiếp cập nhật: chép `wnterm-relay-linux-amd64` + `install-relay.sh` lên máy chủ rồi chạy lại `bash install-relay.sh` bằng root.

## Kiểm thử cục bộ
Cần: MySQL thử + `php -S` (xem `server/DEPLOY.md`), trạm chuyển tiếp với `-allow-private -origins ""`, một sshd thử. Kiểm thử chéo với app C# nằm ở scratchpad của phiên làm việc (vault/wnfile/e2e bằng Chrome headless giả lập iPhone).

## Ngôn ngữ (Tiếng Việt / English)
`pwa/i18n.js` chứa từ điển: khóa là câu tiếng Việt gốc (nên giao diện tiếng Việt giữ nguyên văn), `en` ánh xạ sang tiếng Anh; `t('Đã backup {0} VM', n)` trong JS, `data-i18n` / `data-i18n="html"` / `data-i18n-ph` / `data-i18n-title` / `data-i18n-content` trong `index.html`. Ngôn ngữ mặc định: cài đặt đã lưu (`wnweb.settings.lang`), nếu chưa có thì theo `navigator.language` (bắt đầu bằng `vi` → Tiếng Việt, còn lại English). Đổi trong **Cài đặt → Ngôn ngữ / Language**, áp dụng ngay, không cần tải lại. Thông báo lỗi tiếng Việt từ trạm/bộ SSH wasm được dịch qua `WNi18n.tr` (danh sách `msgs`/`rules` trong `i18n.js`). Thêm câu mới: bọc bằng `L('...')`, thêm bản dịch vào `en`.

## Chấm xanh/xám + độ trễ (`POST /relay/probe`)
Trình duyệt không thử TCP được, nên danh sách VM gọi trạm: `POST /relay/probe` với JSON `{"token":"<token tài khoản>","targets":[{"host":"…","port":22}, …]}` (tối đa 40 đích). Trạm kiểm tra token như `/relay`, áp dụng **cùng quy tắc an toàn** (chặn IP nội bộ, quy tắc cổng khi đích là chính máy chủ), chỉ `connect()` TCP (timeout 2,5 giây, không gửi/đọc byte nào) rồi trả `{"ok":true,"results":[{"host","port","ok":true|false,"ms":12,"error":"blocked|unreachable"?}]}`. Giới hạn: 1 lần / 20 giây / tài khoản (429 + `retryAfter`), tối đa 4 lượt đồng thời toàn trạm, kiểm tra Origin như WebSocket. Nằm dưới `/relay/…` nên nginx không cần đổi gì. PWA thử khi mở danh sách, mỗi 60 giây khi đang ở tab Sessions, và khi bấm "Kiểm tra kết nối" cạnh số VM; bản trạm cũ (không có endpoint) thì chấm vẫn xám. Cập nhật trạm: chạy lại `install-relay.sh` bằng root (xem trên).

## Chống lạm dụng (relay) + điều khoản
Trạm giới hạn: `-ip-rate` (kết nối mới/phút/IP), `-user-rate`, hạn mức thấp cho tài khoản mới (`-new-acct-hours/-max/-rate/-targets`), `-targets-per-hour` (phát hiện quét), khóa 15 phút khi `-fail-max` lần thất bại/ngắt sớm tới cùng một đích trong 10 phút, danh sách chặn `-blocklist` (`/etc/wnterm-relay/blocklist.txt`: tên máy, `*.tên-miền`, IP, CIDR; tự nạp lại mỗi 60 giây). Log vào `-log-file` (`/var/log/wnterm-relay/relay.log`, logrotate giữ 90 ngày); các dòng lạm dụng có tiền tố `abuse`. Mã nguồn: `relay/abuse.go` (+ `abuse_test.go`). Khóa tài khoản vi phạm: `php server/tools/admin.php disable <email>`. Trang điều khoản: `server/public/dieu-khoan.php` — đổi nội dung thì tăng `Api::TOS_VERSION`; đăng ký lưu bằng chứng đồng ý vào bảng `tos_acceptances` (chạy `php tools/migrate.php`). Bật Cloudflare Turnstile (`turnstile` trong `config.php`) để chặn đăng ký hàng loạt.
