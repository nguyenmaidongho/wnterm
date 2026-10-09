# WN Term (WebNow Terminal)

Ứng dụng **SSH & SFTP** đa nền tảng: lưu hàng chục máy chủ (VM), bấm một lần là vào terminal, kéo thả file qua SFTP — trên **Windows**, **Android** và **iPhone** (web app/PWA). Một tài khoản, danh sách VM luôn giống nhau ở mọi thiết bị.

*English: a cross-platform SSH & SFTP client (Windows, Android, iPhone PWA) with a saved VM list, quick connect, snippets and end-to-end encrypted cloud sync.*

Trang chủ & tải về: **https://wnterm.webnow.vn** · Bản web cho iPhone: https://wnterm.webnow.vn/app/

## 1. Tính năng chính

- **Quản lý VM**: thêm/sửa/xóa, ghim, nhóm, tag, tìm nhanh (Ctrl+K), mở nhiều VM cùng lúc; mật khẩu lưu mã hóa (DPAPI trên Windows) hoặc dùng SSH Key (kèm passphrase).
- **Kết nối nhanh (Quick connect)**: thử một máy chủ ngay mà không cần lưu VM.
- **Terminal xterm.js**: gõ tiếng Việt (Unikey/EVKey) không lỗi, bôi đen tự copy, chuột phải dán, hỏi xác nhận khi dán nhiều dòng, phím tắt, thanh phím trên điện thoại.
- **Snippets**: lưu lệnh dùng lại (dùng chung hoặc riêng từng VM), bấm là gửi vào terminal; "Lưu lệnh vừa gõ".
- **SFTP**: duyệt, tải lên/xuống (kéo thả, thanh tiến trình), đổi tên, xóa, chmod, sửa file ngay trên máy chủ.
- **Giám sát máy chủ**: CPU / RAM / mạng / ổ đĩa, chấm online + độ trễ từng VM.
- **Tài khoản & đồng bộ cloud (mã hóa đầu-cuối)**: backup tự động, lịch sử phiên bản, thùng rác, khôi phục; máy chủ chỉ giữ dữ liệu đã mã hóa.
- **Export / Import** danh sách VM (`.wnterm`, AES-256-GCM, PBKDF2 600.000 vòng) và nhập từ MobaXterm.
- **Đa ngôn ngữ**: tiếng Việt và English (app Windows/Android, web iPhone, trang chủ).

## 2. Thành phần & công nghệ

| Thành phần | Thư mục | Ngôn ngữ / công nghệ |
|---|---|---|
| Lõi dùng chung (kết nối SSH/SFTP, kho VM, đồng bộ, mã hóa, snippets) | `src/WNTerm.Core` | **C#** (.NET 10), SSH.NET |
| Giao diện đa nền tảng | `src/WNTerm.App` | **C#**, Avalonia UI 12 |
| Bản Windows / Android | `src/WNTerm.Desktop`, `src/WNTerm.Android` | **C#** (.NET 10, `net10.0-android`) |
| Bản WPF cũ (giữ nguyên, dùng lại Core) | `src/WNTerm` | **C#**, WPF, WebView2 |
| Web app iPhone (PWA) | `web/pwa` | **JavaScript**, HTML, CSS, xterm.js |
| Bộ SSH/SFTP chạy trong trình duyệt | `web/sshwasm` | **Go** → WebAssembly (`x/crypto/ssh`, `pkg/sftp`) |
| Trạm chuyển tiếp WebSocket→TCP cho bản web | `web/relay` | **Go** |
| Trang chủ + tài khoản + API đồng bộ | `server` | **PHP** 7.4+, MySQL (tùy chọn S3) |
| Kiểm thử | `tests` | **C#** (xUnit), Node.js |
| Bộ cài Windows | `WNTerm.iss` | Inno Setup |

Chi tiết: [`PORTING.md`](PORTING.md) (quy ước đa nền tảng), [`web/README.md`](web/README.md) (PWA + trạm chuyển tiếp), [`server/DEPLOY.md`](server/DEPLOY.md) (máy chủ tài khoản).

## 3. Cài đặt & chạy

- **Người dùng**: tải bản Windows / APK Android tại https://wnterm.webnow.vn, hoặc mở https://wnterm.webnow.vn/app/ trên iPhone rồi "Thêm vào MH chính".
- **Từ mã nguồn** (cần .NET 10 SDK):
```powershell
dotnet build WNTerm.slnx
dotnet run --project src/WNTerm.Desktop      # bản Avalonia (Windows)
```
  Bản Android cần workload `android` (không nằm trong `WNTerm.slnx`); bản web cần Go (xem `web/README.md`).
- Chạy thử an toàn (không đụng dữ liệu thật): đặt biến môi trường `WNTERM_DATA_DIR` và `WNTERM_LOCAL_DIR` sang một thư mục tạm.

---

## 4. Hướng dẫn sử dụng

### Quản lý VM
- **Thêm VM**: Bấm nút **"+ Thêm VM"** trên thanh công cụ, nhập IP/Hostname, Port, User, Mật khẩu hoặc file SSH Key. Bấm **Lưu** hoặc **Lưu & Kết nối**.
- **Kết nối nhanh**: Nhấp đúp vào VM trong danh sách hoặc chọn VM rồi nhấn phím `Enter`.
- **Mở nhiều VM cùng lúc**: Giữ phím `Ctrl` hoặc `Shift` để chọn nhiều VM, sau đó nhấn `Enter`. Các VM sẽ mở song song thành từng tab riêng biệt.
- **Chuột phải trên dòng VM**: Hỗ trợ Sửa, Nhân bản, Export VM đã chọn, hoặc Xóa.

### Terminal & SFTP
- **Bôi đen**: Chuột bôi đen văn bản trong ô terminal sẽ tự động copy vào clipboard.
- **Dán**: Nhấp chuột phải vào terminal để dán nội dung. Nếu nội dung có nhiều dòng, ứng dụng sẽ hỏi xác nhận để tránh lệnh chạy ngoài ý muốn.
- **Truyền file SFTP**:
  - Chuyển sang tab **SFTP** ở cột trái.
  - Kéo file/thư mục từ máy tính thả vào danh sách SFTP để tải lên.
  - Chọn file và bấm nút **Download** hoặc chuột phải chọn **Download** để tải về máy.

### Chuyển danh sách VM sang máy khác (Export / Import)
1. Trên máy cũ: Bấm nút **"⇪ Export"**, chọn các VM cần xuất, chọn **"Kèm mật khẩu, bảo vệ bằng mật khẩu Export"**, nhập mật khẩu bảo vệ file và bấm **Export...**.
2. Chép file `.wnterm` sang máy tính mới.
3. Trên máy mới: Mở WN Term, bấm nút **"⇩ Import"** (hoặc kéo thả file `.wnterm` vào cửa sổ ứng dụng), nhập mật khẩu Export và bấm **Import**. Mọi thông tin và mật khẩu sẽ tự động được giải mã và mã hóa lại an toàn theo tài khoản máy mới.

---

## 5. Vị trí lưu trữ dữ liệu & Sao lưu

- **Danh sách cấu hình VM**: `%APPDATA%\WNTerm\sessions.json`
- **Bản sao lưu tự động**: `%APPDATA%\WNTerm\backups\`
- **Cài đặt người dùng**: `%APPDATA%\WNTerm\settings.json`
- **Khóa máy chủ đã tin cậy (Host Keys)**: `%APPDATA%\WNTerm\known_hosts.json`
- **File nhật ký sự cố (Logs)**: `%LOCALAPPDATA%\WNTerm\logs\`

---

## 6. Xử lý sự cố thường gặp

- **Tên file tiếng Việt hiển thị dấu chấm hỏi (`?`) trên Linux**:
  Nếu VM cấu hình locale mặc định là `LANG=C` hoặc `POSIX`, các lệnh dòng lệnh như `ls` có thể không hiển thị đúng ký tự Unicode. Hãy cấu hình lại locale trên server Linux:
  ```bash
  sudo locale-gen en_US.UTF-8
  sudo update-locale LANG=en_US.UTF-8
  ```
- **Không giải mã được mật khẩu khi chép file `sessions.json` thủ công sang máy khác**:
  `sessions.json` được mã hóa bằng DPAPI theo tài khoản người dùng Windows trên từng máy riêng biệt. Để chuyển sang máy tính khác, luôn sử dụng tính năng **Export có kèm mật khẩu** rồi dùng tính năng **Import** trên máy mới.
