# WN Term

Ứng dụng Windows quản lý kết nối SSH và SFTP tới máy chủ ảo (VM), giao diện gọn nhẹ 2 cột phong cách MobaXterm.

## 1. Tính năng chính

- **Quản lý VM tiện lợi**: Lưu thông tin đăng nhập (mật khẩu mã hóa an toàn bằng Windows DPAPI hoặc SSH Key). Nhấp đúp là vào thẳng terminal.
- **Terminal xterm.js mượt mà**:
  - Tự động copy vào clipboard ngay khi bôi đen (quét khối).
  - Chuột phải để dán nhanh (hoặc hiện menu chuột phải).
  - Hỗ trợ đầy đủ tiếng Việt có dấu (Unikey, EVKey với Telex/VNI).
  - Phím tắt tiện lợi: `Ctrl+Shift+C`, `Ctrl+Shift+V`, `Ctrl+Tab`, `Alt+1..9`.
- **SFTP tích hợp**:
  - Cột trái tự động chuyển sang SFTP khi đăng nhập SSH thành công.
  - Tự động đồng bộ theo tab VM đang chọn.
  - Duyệt file/thư mục, tạo thư mục mới, đổi tên, xóa.
  - Kéo thả file từ Windows Explorer vào panel để upload trực tiếp.
  - Tải file/thư mục về máy với thanh tiến trình % và tốc độ truyền.
- **Export & Import danh sách VM an toàn (`.wnterm`)**:
  - Xuất ra file `.wnterm` được bảo vệ bằng chuẩn mã hóa cao cấp AES-256-GCM (PBKDF2 600.000 vòng).
  - Dễ dàng chuyển toàn bộ danh sách VM sang máy tính khác mà vẫn giữ nguyên mật khẩu.
  - Tự động phát hiện trùng lặp với 3 tùy chọn: Bỏ qua, Ghi đè, Thêm bản sao.
- **Sao lưu tự động**:
  - Tự động sao lưu `sessions.json` hằng ngày và trước mỗi lần Import.
- **Bản chạy Portable**:
  - File nhẹ (~7MB), chạy ngay không cần cài đặt.

---

## 2. Hướng dẫn cài đặt & Chạy ứng dụng

### Chạy trực tiếp (Bản Portable)
1. Tải hoặc giải nén file `WNTerm-portable.zip`.
2. Chạy file `WNTerm.exe`.

*(Lưu ý: Đây là bản framework-dependent (nhẹ) — máy đích cần đã cài sẵn **.NET 10 Desktop Runtime** (tải tại https://dotnet.microsoft.com/download/dotnet/10.0, mục "Desktop Runtime"). Nếu chưa có, Windows sẽ tự hiện thông báo và link tải khi chạy `WNTerm.exe`. Nếu máy tính chưa có Microsoft Edge WebView2 Runtime, ứng dụng sẽ hiện thông báo hướng dẫn cài đặt riêng).*

### Chạy từ mã nguồn (Dành cho lập trình viên)
Yêu cầu: .NET 10 SDK
```powershell
dotnet build WNTerm.slnx
dotnet run --project src/WNTerm
```

---

## 3. Hướng dẫn sử dụng

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

## 4. Vị trí lưu trữ dữ liệu & Sao lưu

- **Danh sách cấu hình VM**: `%APPDATA%\WNTerm\sessions.json`
- **Bản sao lưu tự động**: `%APPDATA%\WNTerm\backups\`
- **Cài đặt người dùng**: `%APPDATA%\WNTerm\settings.json`
- **Khóa máy chủ đã tin cậy (Host Keys)**: `%APPDATA%\WNTerm\known_hosts.json`
- **File nhật ký sự cố (Logs)**: `%LOCALAPPDATA%\WNTerm\logs\`

---

## 5. Xử lý sự cố thường gặp

- **Tên file tiếng Việt hiển thị dấu chấm hỏi (`?`) trên Linux**:
  Nếu VM cấu hình locale mặc định là `LANG=C` hoặc `POSIX`, các lệnh dòng lệnh như `ls` có thể không hiển thị đúng ký tự Unicode. Hãy cấu hình lại locale trên server Linux:
  ```bash
  sudo locale-gen en_US.UTF-8
  sudo update-locale LANG=en_US.UTF-8
  ```
- **Không giải mã được mật khẩu khi chép file `sessions.json` thủ công sang máy khác**:
  `sessions.json` được mã hóa bằng DPAPI theo tài khoản người dùng Windows trên từng máy riêng biệt. Để chuyển sang máy tính khác, luôn sử dụng tính năng **Export có kèm mật khẩu** rồi dùng tính năng **Import** trên máy mới.
