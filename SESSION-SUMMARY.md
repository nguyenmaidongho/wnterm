# WNTerm — tóm tắt để mở phiên mới (cập nhật 2026-10-09)

Thư mục dự án: `D:\Software\WNTerm\snterm` (không phải git repo). Chi tiết kỹ thuật: memory của Claude (`wnterm-multiplatform.md`), `PORTING.md`, `web/README.md`, `server/DEPLOY.md`.

## Mục tiêu của chủ dự án
SSH + SFTP (xterm.js) dùng trên **Windows + Android (APK) + iPhone (bản web)**, một tài khoản đồng bộ danh sách VM giữa các máy, kèm trang giới thiệu.

## Hiện trạng (tất cả đã chạy trên production https://wnterm.webnow.vn)
| Thành phần | Vị trí | Trạng thái |
|---|---|---|
| App Windows + Android (Avalonia) | `src/WNTerm.Core`, `WNTerm.App`, `WNTerm.Desktop`, `WNTerm.Android` (bản WPF cũ `src/WNTerm` không dùng) | Installer **2.2.1** (`release/WNTerm-Setup.exe`), APK **1.2** (`release/WNTerm-android-arm64.apk`, đã cài trên Z Flip4). Đã sửa hàng chục lỗi SSH/SFTP (chmod, path traversal, hủy truyền, ghi đè an toàn…) |
| Website + API tài khoản | `server/` (PHP) | Đã cập nhật lên host: sửa lỗi 409 mất dữ liệu, `vault_history`, chống giả IP. Trang chủ mới (iPhone, backup, thùng rác, FAQ) |
| Bản web iPhone (PWA) | `web/` → `https://wnterm.webnow.vn/app/` | Đủ tính năng: thêm/sửa/xóa VM, Cloud (backup/khôi phục/thùng rác/kéo-đẩy), import/export `.wnterm` + MobaXterm, SFTP (chọn nhiều, ZIP, sửa văn bản, chmod), sáng/tối |
| Trạm chuyển tiếp WS→SSH | `web/relay` (Go), chạy dịch vụ systemd `wnterm-relay` trên control3, nginx `location /relay` | Đã cài bằng `web/install-relay.sh` (user root chạy). Chỉ cho tài khoản đã đăng nhập, chặn IP nội bộ |

**Đồng bộ (3 nền tảng dùng chung quy tắc):** mỗi VM có `UpdatedAt`; xóa → thùng rác 30 ngày + dấu xóa 180 ngày; sự kiện mới nhất thắng (hòa thì giữ VM); VM trùng tên+host+port+user khác Id được gộp; máy chủ giữ lịch sử phiên bản 30 ngày. Port JS: `web/pwa/vault.js` ↔ C#: `src/WNTerm.Core/Services/AccountService.cs` + `TrashStore.cs`. Đã kiểm thử chéo C# ↔ JS cả máy thử lẫn production.

**Kiểm thử đã có:** đồng bộ 2 máy 27/27 (server thật), API 41/41, PWA end-to-end 41/41 (Chrome headless giả lập iPhone), import/export chéo C# ↔ JS. Bộ test nằm ở scratchpad của phiên cũ (có thể mất) — viết lại theo mô tả trong `web/README.md` nếu cần.

## Chưa làm / hạn chế
- Bản web **chưa thử trên iPhone thật**; lỗi chủ dự án đã báo (header bị mờ, terminal bị cắt, dải trống dưới) đã sửa trong giả lập nhưng cần xác nhận trên máy — đặc biệt lớp mờ thanh trạng thái iOS 26.
- Bản web: không có tiếng Anh, không ping online/offline từng VM, không tới được máy chủ mạng nội bộ/VPN (dùng app Windows/Android). Terminal: iOS ngắt kết nối nếu rời app lâu.
- Cloudflare: nên thêm luật Cache *Bypass* cho `wnterm.webnow.vn/api/*` và `/relay`; cân nhắc bật Turnstile (`turnstile` trong `config.php`).
- Luồng khôi phục mật khẩu (`recover/reset`) qua Cloudflare mới thử một phần (giới hạn đăng ký 5/giờ/IP của chính máy chủ cản bài thử lặp).
- Mật khẩu lưu trên Android dùng khóa-trong-file (chưa Keystore). Installer Windows chưa ký số (SmartScreen cảnh báo).
- Dọn: `release/wnterm-site.zip`, `wnterm-downloads.zip`, `wnterm-server-update.zip` đã cũ (host mới là bản đúng).
- **Sao lưu `snterm/signing/`** (khóa ký APK `wnterm.keystore` + `keystore.pass`, khóa SSH `wnterm_deploy`) — mất khóa APK = không cập nhật đè được.

## Thông tin vận hành (không chứa bí mật)
- Host: `wnterm@36.50.26.92`, SSH cổng **8282**, khóa `snterm/signing/wnterm_deploy`. Đường dẫn: `/home/wnterm/domains/wnterm.webnow.vn/{public_html,src,tools,config.php,schema.sql}` (`src/` và `config.php` nằm ngoài `public_html`). Trạm chuyển tiếp + script cài ở `~/relay-install/`. **Không đọc/in `config.php`; không sửa khóa khác trên server.**
- Bản sao lưu trên host: `src.bak-20261008`, `src/views.bak-20261009`, `public_html/index.php.bak-20261009`, `schema.sql.bak-20261008`.
- Triển khai PWA: `bash web/build.sh` rồi scp `web/dist/app` → `public_html/app.new`, đổi tên thay `app` (xem `web/README.md`). Triển khai website: scp `server/src/*` và `server/public/*` (trừ `config.php`).
- Build Android (không cần admin): `%USERPROFILE%\.dotnet-user` (dotnet 10.0.401 + workload android + JDK 17 + Android SDK + AVD `wnterm_test`); `dotnet publish -c Release -r android-arm64 -p:PublishTrimmed=false -p:RunAOTCompilation=false -p:AndroidLinkMode=None` + khóa ký alias `wnterm` (chi tiết trong memory). Go (cho `web/`): `. /c/Users/nguye/.dotnet-user/goenv.sh`. Inno Setup: `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe WNTerm.iss`.
- Thử cục bộ: dùng `WNTERM_DATA_DIR`/`WNTERM_LOCAL_DIR` (không đụng dữ liệu VM thật ở `%APPDATA%\WNTerm`); MySQL thử cổng 3399 + `php -S 127.0.0.1:8099 -t public tools/router.php` với `WNTERM_CONFIG` thử; sshd thử bằng `C:\laragon\bin\git\usr\bin\sshd.exe`.
- **Cẩn thận:** MySQL của Laragon (cổng 3306) chạy trên máy chủ dự án — **không kill `mysqld.exe` theo tên**, chỉ kill theo PID/cổng của tiến trình thử.

## Quy ước làm việc đã thống nhất
- Hỏi trước khi chạm vào khóa/cấu hình sẵn có trên server; chỉ xóa file do chính mình tạo.
- Hệ thống phân quyền có thể chặn SSH/scp tới production — nếu bị chặn, dừng và hỏi chủ dự án (chủ dự án đã từng cho phép bằng lời trong chat).
- Trả lời bằng tiếng Việt, ngắn gọn, nói rõ việc nào đã kiểm chứng, việc nào chưa; tên tắt/gọi thân mật là bình thường.
