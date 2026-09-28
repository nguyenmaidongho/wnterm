# SN Term — Kế hoạch triển khai

> Ứng dụng Windows quản lý kết nối SSH tới VM, bố cục giống MobaXterm, chỉ gồm 2 tính năng: **Terminal** và **SFTP**.
> **Tên ứng dụng: SN Term.**
> - Tên hiển thị (tiêu đề cửa sổ, hộp thoại, README, thông báo): **`SN Term`** (có dấu cách).
> - Tên trong code, project, namespace, file `.exe`, thư mục dữ liệu: **`SNTerm`** (không dấu cách, vì C# và tên file không nên có dấu cách).
> - Đuôi file Export: **`.snterm`**.

---

## 0. Hướng dẫn cho AI/CLI thực hiện kế hoạch này

Đọc kỹ toàn bộ file này trước khi viết code. Khi làm việc:

1. Làm **tuần tự từng giai đoạn** (mục 10). Không chuyển giai đoạn khi chưa đạt hết tiêu chí "Hoàn thành khi".
2. Sau mỗi giai đoạn: chạy `dotnet build` (không lỗi, không cảnh báo mới) và `dotnet test`, rồi `git commit` với thông điệp `Giai đoạn N: <tóm tắt>`.
3. Các mục kiểm tra có đánh dấu **[Tay]** cần người dùng tự bấm thử. Khi xong giai đoạn, liệt kê các mục [Tay] để người dùng kiểm tra.
4. **Không thêm tính năng ngoài phạm vi** (mục 2). Nếu thấy cần, hỏi người dùng trước.
5. Toàn bộ chữ hiển thị trên giao diện bằng **tiếng Việt có dấu**. Tên biến, class, file bằng tiếng Anh.
6. Không bao giờ chặn UI thread: mọi thao tác mạng dùng `async/await` hoặc chạy nền.
7. Không ghi mật khẩu, passphrase ra log hay ra file dạng chữ thường. Kể cả file Export: chỉ chứa mật khẩu khi đã mã hóa bằng mật khẩu Export (mục 5.4).
8. Người dùng không phải lập trình viên: khi báo cáo, giải thích ngắn gọn, dễ hiểu, và nói rõ cần họ làm gì.

---

## 1. Mục tiêu

- Cửa sổ chính chia **2 cột**:
  - **Cột trái**: danh sách VM đã lưu; khi đăng nhập vào một VM thì chuyển sang **trình duyệt SFTP** của VM đó.
  - **Cột phải**: các **tab terminal**, mỗi tab là một kết nối SSH.
- Nút **"+ Thêm VM"** mở form nhập: IP/Hostname, Port, User, Password, Key file + Passphrase.
- **Lưu danh sách VM** cùng toàn bộ thông tin đăng nhập: lần sau chỉ cần **nhấp đúp là vào thẳng**, không phải nhập lại gì (mục 6.2).
- **Export / Import danh sách VM** ra một file để sao lưu hoặc chuyển sang máy khác, có thể kèm mật khẩu được bảo vệ bằng mật khẩu riêng (mục 5.4, 6.7, 6.8).
- Terminal:
  - **Quét khối (bôi đen) là tự động copy** vào clipboard.
  - **Chuột phải là dán**, hoặc **hiện menu** (có tùy chọn trong Cài đặt).
  - Hiển thị và gõ **tiếng Việt** chuẩn.

## 2. Phạm vi

**Trong phạm vi:** SSH terminal, SFTP (duyệt, upload, download, đổi tên, xóa, tạo thư mục), quản lý và lưu danh sách VM, Export/Import danh sách VM (định dạng riêng của SN Term), cài đặt cơ bản, đóng gói bản chạy.

**Ngoài phạm vi (không làm):** RDP, VNC, Telnet, Serial, X11 server, SSH tunnel, macro, multi-execution, đồng bộ cloud, kéo file từ SFTP thả ra Explorer, import cấu hình từ MobaXterm/PuTTY.

---

## 3. Công nghệ

| Thành phần | Lựa chọn | Ghi chú |
|---|---|---|
| Ngôn ngữ / nền tảng | **C# / .NET 10 (LTS)**, `net10.0-windows` | |
| Giao diện | **WPF** | |
| MVVM | `CommunityToolkit.Mvvm` | `[ObservableProperty]`, `[RelayCommand]` |
| SSH + SFTP | **`SSH.NET`** (NuGet, bản ≥ 2026.0.0) | Có `ShellStream.ChangeWindowSize`, đọc key OpenSSH / PuTTY `.ppk` / PKCS#8, fingerprint SHA256, `UploadFileAsync`/`DownloadFileAsync` có `IProgress` |
| Ô terminal | **xterm.js 6.x** chạy trong **WebView2** | `@xterm/xterm` 6.0.0, `@xterm/addon-fit`, `@xterm/addon-unicode11` |
| WebView2 | `Microsoft.Web.WebView2` (NuGet) | Runtime có sẵn trên Windows 10/11 |
| Mã hóa mật khẩu | `System.Security.Cryptography.ProtectedData` (DPAPI, `CurrentUser`) | |
| Font terminal | **JetBrains Mono** (giấy phép OFL), đóng gói kèm ứng dụng | Có đủ dấu tiếng Việt |
| Test | xUnit | |

**Vì sao dùng xterm.js trong WebView2:** tự viết bộ giả lập terminal (xử lý mã VT100/xterm cho vim, htop, màu…) rất tốn công và dễ lỗi. xterm.js là thư viện VS Code đang dùng, đã xử lý sẵn quét khối, Unicode, bộ gõ tiếng Việt (IME).

**Tài nguyên xterm.js và font** phải được tải về và **đặt trong project** (không dùng CDN lúc chạy, để ứng dụng chạy offline):
- Nếu máy có Node.js: `npm install @xterm/xterm@6.0.0 @xterm/addon-fit @xterm/addon-unicode11` trong thư mục tạm, rồi copy file `lib/*.js` và `css/xterm.css` vào `wwwroot/lib/`.
- Nếu không có Node.js: tải bằng `curl` từ `https://cdn.jsdelivr.net/npm/<gói>@<phiên bản>/<đường dẫn>`.
- Font: tải JetBrains Mono (Regular, Bold) dạng `.woff2` từ GitHub release chính thức, đặt vào `wwwroot/fonts/`, kèm file `OFL.txt`.
- Chọn phiên bản addon tương thích với xterm 6.x và ghi lại phiên bản đã dùng trong `wwwroot/lib/VERSIONS.txt`.

---

## 4. Cấu trúc thư mục

```
SNTerm/
├─ PLAN.md
├─ SNTerm.slnx
├─ src/SNTerm/
│  ├─ SNTerm.csproj
│  ├─ App.xaml / App.xaml.cs
│  ├─ Models/
│  │  ├─ SessionInfo.cs
│  │  ├─ AppSettings.cs
│  │  ├─ KnownHost.cs
│  │  └─ ExportFile.cs          // cấu trúc file .snterm (mục 5.4)
│  ├─ Services/
│  │  ├─ AppPaths.cs            // đường dẫn %APPDATA%\SNTerm
│  │  ├─ SessionStore.cs        // đọc/ghi sessions.json
│  │  ├─ SettingsStore.cs       // đọc/ghi settings.json
│  │  ├─ SecretProtector.cs     // mã hóa/giải mã bằng DPAPI
│  │  ├─ KnownHostsStore.cs     // lưu fingerprint máy chủ
│  │  ├─ SshConnectionFactory.cs// tạo ConnectionInfo từ SessionInfo
│  │  ├─ SessionExporter.cs     // tạo file .snterm, mã hóa phần bí mật
│  │  ├─ SessionImporter.cs     // đọc file .snterm, giải mã, phát hiện trùng, gộp
│  │  ├─ ErrorTranslator.cs     // đổi exception thành thông báo tiếng Việt
│  │  └─ ClipboardService.cs    // copy/paste có retry
│  ├─ Connections/
│  │  └─ SshConnection.cs       // SshClient + ShellStream + SftpClient của 1 tab
│  ├─ ViewModels/
│  │  ├─ MainViewModel.cs
│  │  ├─ SessionListViewModel.cs
│  │  ├─ SessionEditorViewModel.cs
│  │  ├─ TerminalTabViewModel.cs
│  │  ├─ SftpViewModel.cs
│  │  ├─ TransferQueueViewModel.cs
│  │  ├─ ExportViewModel.cs
│  │  └─ ImportViewModel.cs
│  ├─ Views/
│  │  ├─ MainWindow.xaml
│  │  ├─ SessionEditorDialog.xaml   // form Thêm/Sửa VM
│  │  ├─ PasswordPromptDialog.xaml
│  │  ├─ HostKeyDialog.xaml
│  │  ├─ SettingsDialog.xaml
│  │  ├─ ExportDialog.xaml
│  │  ├─ ImportDialog.xaml
│  │  ├─ TerminalView.xaml          // chứa WebView2
│  │  └─ SftpPanel.xaml
│  └─ wwwroot/
│     ├─ terminal.html
│     ├─ terminal.js
│     ├─ terminal.css
│     ├─ lib/                        // xterm.js + addons + VERSIONS.txt
│     └─ fonts/                      // JetBrains Mono .woff2 + OFL.txt
└─ tests/SNTerm.Tests/
   ├─ SNTerm.Tests.csproj
   └─ TestData/                      // file .snterm mẫu: hợp lệ, sai version, bị sửa, sai mật khẩu
```

`wwwroot/**` phải được copy ra thư mục output (`CopyToOutputDirectory = PreserveNewest`).

---

## 5. Dữ liệu và lưu trữ

Thư mục dữ liệu: `%APPDATA%\SNTerm\`

| File | Nội dung |
|---|---|
| `sessions.json` | Danh sách VM |
| `settings.json` | Cài đặt |
| `known_hosts.json` | Fingerprint máy chủ đã tin cậy |
| `keys\` | File key được giải nén ra khi Import (mục 5.4) |
| `backups\` | Bản sao lưu tự động của `sessions.json` (mục 5.1) |

Dữ liệu WebView2: `%LOCALAPPDATA%\SNTerm\WebView2\`.

### 5.1 `SessionInfo`

```csharp
public class SessionInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";          // để trống thì hiển thị "user@host"
    public string Group { get; set; } = "";         // nhóm, để trống = "Chưa phân nhóm"
    public string Host { get; set; } = "";          // IP hoặc hostname
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public bool SavePassword { get; set; } = true;
    public string? EncryptedPassword { get; set; }  // DPAPI + Base64, null nếu không lưu
    public string? KeyFilePath { get; set; }        // chỉ lưu đường dẫn, không copy nội dung key
    public string? EncryptedPassphrase { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastConnectedAt { get; set; }
}
```

- Ghi file theo kiểu an toàn: ghi ra `sessions.json.tmp` rồi thay thế file cũ, để không hỏng dữ liệu khi tắt máy đột ngột.
- File JSON lưu UTF-8, giữ nguyên ký tự tiếng Việt (không escape `\uXXXX`) để dễ đọc.
- **Lưu ngay** sau mỗi thay đổi (thêm, sửa, xóa, nhân bản, cập nhật `LastConnectedAt`), không đợi tắt app. App bị tắt đột ngột cũng không mất danh sách.
- **Sao lưu tự động**: mỗi ngày, ở lần lưu đầu tiên, chép `sessions.json` cũ sang `backups\sessions-yyyyMMdd.json`; giữ 10 bản gần nhất. Trước mỗi lần Import cũng sao lưu một bản `sessions-before-import-yyyyMMdd-HHmmss.json`.
- Nếu `sessions.json` hỏng (không đọc được JSON): không ghi đè; đổi tên thành `sessions.corrupt-<thời gian>.json`, báo người dùng và đề nghị khôi phục từ bản sao lưu mới nhất.
- Có trường `"version": 1` ở đầu file để sau này nâng cấp định dạng.

### 5.2 `AppSettings`

| Thuộc tính | Mặc định | Ý nghĩa |
|---|---|---|
| `FontFamily` | `"JetBrains Mono"` | Font terminal |
| `FontSize` | `14` | Cỡ chữ |
| `Theme` | `"Dark"` | `Dark` / `Light` |
| `CopyOnSelect` | `true` | Quét khối là copy |
| `RightClickAction` | `"Paste"` | `Paste` = chuột phải dán, Shift+chuột phải hiện menu; `Menu` = ngược lại |
| `ConfirmMultilinePaste` | `true` | Hỏi lại khi dán nội dung nhiều dòng |
| `Scrollback` | `10000` | Số dòng lịch sử |
| `CursorBlink` | `true` | |
| `KeepAliveSeconds` | `30` | |
| `ShowHiddenFiles` | `false` | SFTP hiện file bắt đầu bằng `.` |
| `CollapsedGroups` | `[]` | Các nhóm VM đang thu gọn |
| `LastExportFolder` / `LastImportFolder` | `""` | Nhớ thư mục lần Export/Import trước |
| `MaxParallelConnects` | `4` | Số kết nối mở song song khi mở nhiều VM |

### 5.3 `KnownHost`

Khóa là `"host:port"`, giá trị gồm `Algorithm`, `FingerprintSha256`, `AddedAt`.

### 5.4 File Export `.snterm`

**Vấn đề cần giải quyết:** mật khẩu trong `sessions.json` được mã hóa bằng DPAPI, **chỉ giải mã được trên đúng máy và tài khoản Windows đó**. Vì vậy khi Export không thể chép nguyên `sessions.json`, mà phải giải mã rồi mã hóa lại bằng một **mật khẩu Export** do người dùng đặt.

Có 2 chế độ Export:

| Chế độ | Nội dung | Dùng khi |
|---|---|---|
| **Không kèm mật khẩu** (mặc định) | Tên, nhóm, host, port, user, tên file key. **Không có** mật khẩu, passphrase, nội dung key. | Chia sẻ danh sách cho đồng nghiệp, lưu trữ ít rủi ro |
| **Kèm mật khẩu (có bảo vệ)** | Như trên + mật khẩu, passphrase, và (tùy chọn) nội dung file key, tất cả được mã hóa bằng mật khẩu Export | Chuyển sang máy mới, sao lưu đầy đủ |

**Không có** chế độ xuất mật khẩu dạng chữ thường.

Cấu trúc file (JSON, UTF-8, đuôi `.snterm`):

```json
{
  "format": "snterm-sessions",
  "version": 1,
  "exportedAt": "2026-09-28T14:30:00+07:00",
  "appVersion": "1.0.0",
  "protection": {
    "kdf": "PBKDF2-SHA256",
    "iterations": 600000,
    "salt": "<Base64 16 byte>",
    "check": { "nonce": "<Base64>", "cipherText": "<Base64>", "tag": "<Base64>" }
  },
  "sessions": [
    {
      "id": "…guid…",
      "name": "web-01",
      "group": "Dev",
      "host": "10.0.0.5",
      "port": 22,
      "username": "ubuntu",
      "keyFileName": "id_ed25519",
      "secrets": { "nonce": "<Base64>", "cipherText": "<Base64>", "tag": "<Base64>" }
    }
  ]
}
```

- Chế độ không kèm mật khẩu: `protection = null`, không có trường `secrets`.
- **Mã hóa** (chế độ có bảo vệ):
  - Khóa 32 byte = `Rfc2898DeriveBytes.Pbkdf2(exportPassword, salt, 600000, SHA256, 32)`; `salt` ngẫu nhiên 16 byte cho mỗi lần Export.
  - Mỗi VM: `secrets` là JSON `{ "password", "passphrase", "keyFileContent" (Base64, có thể null) }` được mã hóa bằng **AES-256-GCM** (`System.Security.Cryptography.AesGcm`), `nonce` ngẫu nhiên 12 byte, tag 16 byte, **AAD = chuỗi `id` của VM** (để không tráo `secrets` giữa các VM được).
  - `check`: mã hóa chuỗi cố định `"SNTERM-OK"` cùng khóa, để khi Import kiểm tra nhanh mật khẩu đúng/sai.
  - Xóa (`CryptographicOperations.ZeroMemory`) mảng byte chứa khóa và dữ liệu rõ ngay sau khi dùng.
- Mật khẩu Export: tối thiểu 8 ký tự, nhập 2 lần, có thanh báo độ mạnh đơn giản (yếu / trung bình / mạnh).
- `keyFileName` chỉ là tên file (không kèm đường dẫn máy cũ), dùng để hiển thị và đặt tên khi giải nén.
- Ghi file theo kiểu an toàn (ghi `.tmp` rồi đổi tên).

**Quy tắc Import:**
- Kiểm tra `format == "snterm-sessions"` và `version` được hỗ trợ; nếu không → "File không đúng định dạng SN Term" hoặc "File được tạo bởi phiên bản SN Term mới hơn, hãy cập nhật ứng dụng".
- Giới hạn kích thước file 20 MB; JSON lỗi → báo lỗi, không nhập gì.
- File có `protection` → hỏi mật khẩu Export; sai (giải mã `check` thất bại) → "Sai mật khẩu file", cho thử lại. Giải mã `secrets` của một VM thất bại (file bị sửa) → bỏ qua phần bí mật của VM đó, vẫn nhập thông tin còn lại và ghi rõ trong báo cáo.
- Mỗi VM được kiểm tra như form Thêm VM (host không trống, port 1–65535, user không trống); VM không hợp lệ → bỏ qua, ghi vào báo cáo.
- Mật khẩu/passphrase sau khi giải mã được **mã hóa lại bằng DPAPI** của máy hiện tại rồi mới lưu.
- Nội dung file key (nếu có) → ghi ra `%APPDATA%\SNTerm\keys\<id>_<keyFileName>`, đặt quyền NTFS **chỉ tài khoản hiện tại đọc được** (tắt kế thừa quyền, chỉ giữ user hiện tại), và gán `KeyFilePath` trỏ tới file này.
- VM có `keyFileName` nhưng không kèm nội dung key → `KeyFilePath = null`, đánh dấu "Thiếu file key — hãy chọn lại" trong danh sách (biểu tượng ⚠).
- **Phát hiện trùng**: trùng khi cùng `Id`, **hoặc** cùng bộ `host + port + username` (so sánh host không phân biệt hoa thường). Với VM trùng, người dùng chọn: **Bỏ qua** (mặc định) / **Ghi đè** / **Thêm bản sao** (tạo `Id` mới, tên thêm hậu tố " (nhập)").
- `CreatedAt` giữ theo máy nhận (thời điểm Import); `LastConnectedAt` để trống.
- Không nhập `known_hosts`: lần đầu kết nối trên máy mới vẫn hỏi xác nhận host key như bình thường (an toàn hơn).

---

## 6. Giao diện

### 6.1 Cửa sổ chính

```
┌──────────────────────────────────────────────────────────────────┐
│ [+ Thêm VM]  [⇪ Export]  [⇩ Import]  [⚙ Cài đặt]                 │
├────────────────────┬─┬───────────────────────────────────────────┤
│ [ Sessions ][ SFTP ]│ │ [● web-01 ×] [● db-02 ×]                   │
│ 🔍 Tìm VM...        │ │                                           │
│ ▾ Dev               │↔│  user@web-01:~$                            │
│    web-01           │ │                                           │
│    db-02            │ │          (terminal xterm.js)              │
│ ▾ Prod              │ │                                           │
│    api-01           │ │                                           │
├────────────────────┴─┴───────────────────────────────────────────┤
│ Thanh trạng thái: Đã kết nối web-01 (10.0.0.5:22)                │
└──────────────────────────────────────────────────────────────────┘
```

- Cột trái mặc định rộng 280px, có `GridSplitter` để kéo; nhớ độ rộng vào settings.
- Cột trái là `TabControl` gồm 2 tab **Sessions** và **SFTP**.
- **Khi kết nối SSH thành công → tự chuyển sang tab SFTP** và hiển thị thư mục home của VM đó.
- **Khi đổi tab terminal → SFTP đổi theo** VM của tab đang chọn.
- Không có tab terminal nào đang kết nối → tab SFTP hiện dòng "Chưa kết nối VM nào".
- Cột phải có thanh tab ngang, mỗi VM một tab; chi tiết ở mục 6.6.
- Font giao diện WPF: `Segoe UI` (có đủ tiếng Việt).
- Tiêu đề cửa sổ: `SN Term`; khi có tab đang chọn: `web-01 — SN Term`.

### 6.2 Danh sách VM (tab Sessions)

- Danh sách gom theo nhóm, sắp xếp theo tên. Ô tìm kiếm lọc theo tên/host/user.
- **Chọn nhiều VM** bằng `Ctrl+nhấp` / `Shift+nhấp` / `Ctrl+A` (để mở nhiều VM cùng lúc, mục 6.6). Lưu ý: `TreeView` của WPF không hỗ trợ chọn nhiều, nên dùng **`ListBox` với `SelectionMode="Extended"` + `CollectionViewSource` gom nhóm (`GroupStyle` có `Expander`)** để có giao diện dạng cây nhóm mà vẫn chọn nhiều được.
- Trên cùng có nhóm **"⏱ Gần đây"** hiện 5 VM kết nối gần nhất (theo `LastConnectedAt`); VM vẫn nằm trong nhóm gốc của nó.
- Mỗi dòng VM hiển thị: tên (đậm) và `user@host:port` (chữ nhỏ, màu nhạt). VM thiếu file key có biểu tượng ⚠ kèm tooltip.
- Nhóm nhớ trạng thái mở/đóng (lưu vào settings).

**Kết nối nhanh ("nhấp là vào"):**
- **Nhấp đúp** vào VM, hoặc chọn VM rồi nhấn **Enter** → mở tab terminal và kết nối ngay.
- Nếu VM đã lưu mật khẩu (hoặc key không cần passphrase / passphrase đã lưu) và host key đã được tin cậy → **vào thẳng, không hiện hộp thoại nào**.
- Chỉ hỏi khi thật sự cần: chưa lưu mật khẩu → `PasswordPromptDialog` (có ô "Lưu lại cho lần sau", mặc định bật); máy chủ mới/đổi host key → `HostKeyDialog`.
- Mật khẩu đã lưu nhưng bị máy chủ từ chối (đã đổi mật khẩu) → hiện `PasswordPromptDialog` với dòng "Mật khẩu đã lưu không còn đúng, hãy nhập mật khẩu mới"; nhập đúng thì cập nhật mật khẩu đã lưu.
- Nhấp đúp VM đang có tab mở → vẫn mở **tab mới** (giống MobaXterm), để làm việc nhiều cửa sổ trên cùng VM.
- Sau khi kết nối thành công → cập nhật `LastConnectedAt` và lưu ngay.

Chuột phải trên VM: **Kết nối** (khi chọn nhiều: **Kết nối N VM đã chọn**), **Sửa**, **Nhân bản**, **Export VM này...** (khi chọn nhiều: **Export N VM đã chọn...**), **Xóa** (hỏi xác nhận, liệt kê tên). Chuột phải trên nhóm: **Kết nối tất cả**, **Đổi tên nhóm**, **Export nhóm này...**, **Xóa nhóm** (hỏi xác nhận, liệt kê số VM sẽ bị xóa).
Phím tắt khi danh sách đang được chọn: `Enter` kết nối (tất cả VM đang chọn), `F2` sửa, `Delete` xóa, `Ctrl+D` nhân bản.

### 6.3 Form "Thêm VM" / "Sửa VM" (`SessionEditorDialog`)

```
┌─ Thêm VM ─────────────────────────────────────────┐
│ Tên hiển thị:  [ web-01                        ]  │
│ Nhóm:          [ Dev                         ▾ ]  │  (ComboBox cho gõ tự do)
│ IP/Hostname: * [ 10.0.0.5                      ]  │
│ Port:        * [ 22   ]                           │
│ User:        * [ ubuntu                        ]  │
│ ─ Xác thực ────────────────────────────────────── │
│ Password:      [ ••••••••                      ]  │
│                [x] Lưu mật khẩu                   │
│ Key file:      [ C:\keys\id_ed25519   ] [Chọn...] │
│ Passphrase:    [ ••••••                        ]  │  (chỉ bật khi có key file)
│                                                   │
│ [Kiểm tra kết nối]   [Lưu & Kết nối] [Lưu] [Hủy]  │
└───────────────────────────────────────────────────┘
```

Quy tắc:
- Bắt buộc: IP/Hostname, Port (1–65535, mặc định 22), User. Báo lỗi đỏ ngay dưới ô sai.
- Host được `Trim()`; chấp nhận IPv4, IPv6, hostname.
- Password và Key file **đều không bắt buộc**. Có thể nhập cả hai (thử key trước, rồi password).
- Nếu không lưu mật khẩu, hoặc không có cả password lẫn key → khi kết nối sẽ hiện `PasswordPromptDialog` hỏi mật khẩu (có ô "Lưu lại").
- Nút **Chọn...** mở hộp chọn file, bộ lọc: `Key (*.pem;*.key;*.ppk;id_*)` và `Tất cả (*.*)`.
- Khi chọn key file, đọc thử key (dùng passphrase đang nhập). Nếu key cần passphrase mà chưa nhập → nhắc "Key này cần passphrase". Nếu sai định dạng → "Không đọc được file key".
- **Kiểm tra kết nối**: kết nối thử (có kiểm tra host key), xác thực, rồi ngắt; báo "Kết nối thành công" hoặc lỗi tiếng Việt. Có hiệu ứng đang chờ và nút Hủy.
- Khi Sửa VM: ô Password để trống nghĩa là **giữ mật khẩu cũ**; có nút nhỏ "Xóa mật khẩu đã lưu".
- Password và Passphrase dùng `PasswordBox`; có nút 👁 để hiện/ẩn.

### 6.4 Hộp thoại xác nhận host key (`HostKeyDialog`)

- **Máy chủ mới**: "Lần đầu kết nối tới `host:port`. Fingerprint (SHA256): `...`. Bạn có tin máy chủ này không?" → [Tin cậy & lưu] [Chỉ lần này] [Hủy].
- **Fingerprint thay đổi**: cảnh báo đỏ "Khóa máy chủ đã thay đổi! Có thể bị tấn công giả mạo, hoặc VM vừa được cài lại." → mặc định **Hủy**; có nút [Vẫn kết nối & cập nhật].

### 6.5 Cài đặt (`SettingsDialog`)

Các mục trong `AppSettings` (mục 5.2), có xem trước font. Áp dụng ngay cho mọi tab đang mở.

### 6.6 Thanh tab terminal và mở nhiều VM cùng lúc

Thanh tab **nằm ngang phía trên** vùng terminal, giống MobaXterm:

```
┌───────────────────────────────────────────────────────────────────┐
│ ◀ [● web-01 ×] [● db-02 ×] [◐ api-01 ×] [○ cache-01 ×] [+]  ▾ ▶ │
├───────────────────────────────────────────────────────────────────┤
│  ubuntu@web-01:~$                                                 │
```

**Mở nhiều VM một lần:**
- Chọn nhiều VM (mục 6.2) rồi nhấn `Enter`, nhấp đúp, hoặc chuột phải → **Kết nối N VM đã chọn**.
- Chuột phải trên nhóm → **Kết nối tất cả** (mọi VM trong nhóm).
- Mỗi VM mở **một tab riêng**, theo thứ tự trong danh sách; tab của VM đầu tiên được chọn làm tab đang xem.
- Mở từ 10 VM trở lên → hỏi xác nhận "Bạn sắp mở N kết nối, tiếp tục?".
- Các tab **kết nối song song**, tối đa **4 kết nối cùng lúc** (`MaxParallelConnects`, hàng đợi `SemaphoreSlim`), để không làm nghẽn mạng/máy.
- Hộp thoại cần người dùng trả lời (host key, hỏi mật khẩu) được **xếp hàng, hiện lần lượt từng cái**, tiêu đề ghi rõ VM nào (VD: "web-01 — Nhập mật khẩu"). Các tab khác vẫn tiếp tục kết nối trong lúc chờ.
- Một VM kết nối lỗi → chỉ tab đó báo lỗi (in lỗi tiếng Việt trong terminal, chấm xám, nút/Enter để thử lại), không ảnh hưởng tab khác.

**Thanh tab:**
- Mỗi tab: chấm trạng thái (● xanh = đã kết nối, ◐ vàng = đang kết nối/kết nối lại, ○ xám = đã ngắt/lỗi), tên VM, nút `×`. Tooltip hiện `user@host:port`.
- Hai tab cùng tên VM → tự đánh số: `web-01`, `web-01 (2)`.
- Nhiều tab tràn độ rộng → thanh tab **cuộn ngang** (nút ◀ ▶ và lăn chuột), kèm nút **▾** liệt kê tất cả tab để nhảy nhanh.
- **Kéo thả** để sắp xếp lại thứ tự tab.
- **Nhấp chuột giữa** vào tab → đóng tab.
- Nút **[+]** → mở danh sách VM (chuyển cột trái sang tab Sessions và focus ô tìm kiếm).
- Chuột phải trên tab: **Kết nối lại**, **Nhân bản tab** (mở thêm một kết nối tới cùng VM), **Đổi tên tab**, **Đóng**, **Đóng các tab khác**, **Đóng các tab bên phải**, **Đóng tất cả**.
- Đóng nhiều tab đang kết nối → hỏi xác nhận một lần ("Đóng N tab đang kết nối?").
- Phím tắt (bổ sung cho mục 8.6): `Ctrl+Tab` / `Ctrl+Shift+Tab` chuyển tab, `Alt+1`…`Alt+9` nhảy tới tab thứ 1–9.
- **Mỗi tab giữ WebView2 riêng và không bị hủy khi chuyển tab** (giữ nguyên nội dung terminal, lịch sử cuộn). Cách làm: không dùng `TabControl` mặc định với `ContentTemplate` (WPF sẽ tạo lại nội dung khi đổi tab); thay vào đó dùng thanh tab tự vẽ + một `Grid` chứa tất cả `TerminalView`, chỉ đổi `Visibility` của view đang chọn.

### 6.7 Hộp thoại Export (`ExportDialog`)

```
┌─ Export danh sách VM ─────────────────────────────┐
│ Chọn VM cần export:                               │
│ [x] ▾ Dev                                         │
│ [x]     web-01   ubuntu@10.0.0.5:22               │
│ [x]     db-02    root@10.0.0.6:22                 │
│ [ ] ▾ Prod                                        │
│ [ ]     api-01   deploy@api.example.com:2222      │
│ [Chọn tất cả] [Bỏ chọn]                           │
│ ─ Mật khẩu ────────────────────────────────────── │
│ (•) Không kèm mật khẩu                            │
│ ( ) Kèm mật khẩu, bảo vệ bằng mật khẩu Export     │
│       Mật khẩu Export:   [ •••••••• ]  [Mạnh]     │
│       Nhập lại:          [ •••••••• ]             │
│       [x] Kèm nội dung file key                   │
│  ⚠ Hãy nhớ mật khẩu Export. Mất mật khẩu thì     │
│    không lấy lại được mật khẩu VM trong file.     │
│                                                   │
│                     [Export...]   [Hủy]           │
└───────────────────────────────────────────────────┘
```

- Mở từ nút **Export** trên thanh công cụ (mặc định chọn tất cả), hoặc từ menu chuột phải VM/nhóm (chỉ chọn sẵn VM/nhóm đó).
- Nút **Export...** mở hộp lưu file, tên mặc định `SNTerm-yyyyMMdd.snterm`; nhớ thư mục lần trước.
- Không chọn VM nào → nút Export mờ.
- VM có mật khẩu đã lưu nhưng DPAPI giải mã lỗi → vẫn export thông tin còn lại và báo trong thông điệp kết quả.
- File key không còn tồn tại khi export → bỏ qua nội dung key của VM đó, báo trong kết quả.
- Kết quả: "Đã export N VM ra `<đường dẫn>`" kèm nút **Mở thư mục**.

### 6.8 Hộp thoại Import (`ImportDialog`)

Bước 1 — chọn file `.snterm` (hoặc **kéo thả file `.snterm` vào cửa sổ chính**). Nếu file có bảo vệ → hỏi mật khẩu Export.

Bước 2 — xem trước:

```
┌─ Import danh sách VM ─────────────────────────────────────────┐
│ File: SNTerm-20260928.snterm  (12 VM, có kèm mật khẩu)        │
│ ┌───┬──────────┬──────────────────────┬────────┬────────────┐ │
│ │[x]│ Tên      │ user@host:port       │ Trạng thái│ Xử lý   │ │
│ │[x]│ web-01   │ ubuntu@10.0.0.5:22   │ Mới     │           │ │
│ │[x]│ db-02    │ root@10.0.0.6:22     │ Trùng   │ [Bỏ qua ▾]│ │
│ │[ ]│ bad-vm   │ @:0                  │ Lỗi: thiếu host │   │ │
│ └───┴──────────┴──────────────────────┴────────┴────────────┘ │
│ Với tất cả VM trùng: [Bỏ qua ▾] [Áp dụng]                     │
│ Nhập vào nhóm: (•) Giữ nhóm trong file  ( ) Nhóm: [ Nhập  ▾ ] │
│                               [Import]   [Hủy]                │
└───────────────────────────────────────────────────────────────┘
```

- Trạng thái mỗi dòng: **Mới**, **Trùng** (kèm tên VM đang có), **Thiếu file key**, **Lỗi: <lý do>** (không chọn được).
- Nhấn **Import** → sao lưu `sessions.json` (mục 5.1) → gộp → lưu → làm mới danh sách.
- Báo cáo cuối: "Đã thêm X, ghi đè Y, bỏ qua Z, lỗi W" và danh sách VM cần chọn lại file key (nếu có).
- Hủy ở bất kỳ bước nào → không thay đổi gì.

---

## 7. Kết nối SSH

### 7.1 `SshConnection` (mỗi tab một đối tượng)

Gồm `SshClient`, `ShellStream`, `SftpClient`. SSH.NET không cho `SshClient` và `SftpClient` dùng chung một kết nối, nên mỗi tab mở **2 kết nối** với cùng thông tin đăng nhập. Người dùng chỉ nhập mật khẩu một lần: mật khẩu được giữ trong bộ nhớ suốt phiên của tab.

Trạng thái: `Connecting` → `Connected` → `Disconnected` (→ `Reconnecting`).

### 7.2 Xác thực (`SshConnectionFactory`)

```
methods = []
nếu có KeyFilePath:
    methods += PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(path, passphrase))
nếu có password:
    methods += PasswordAuthenticationMethod(user, password)
    methods += KeyboardInteractiveAuthenticationMethod(user)  // trả lời prompt bằng password
ConnectionInfo(host, port, user, methods) với Timeout = 15 giây
client.KeepAliveInterval = KeepAliveSeconds
```

Lý do thêm keyboard-interactive: nhiều máy chủ chỉ bật kiểu này cho đăng nhập bằng mật khẩu.

### 7.3 Kiểm tra host key

- Gắn sự kiện `HostKeyReceived`, so sánh `e.FingerPrintSHA256` với `known_hosts.json`, đặt `e.CanTrust`.
- Sự kiện chạy ở luồng nền → hiện `HostKeyDialog` bằng `Dispatcher.Invoke`.
- **Quan trọng**: gọi `ConnectAsync` từ UI thread bằng `await`, **không** gọi `Connect()` đồng bộ trên UI thread (sẽ bị treo vì `Dispatcher.Invoke`).
- Kết nối SFTP thứ hai tự tin cậy nếu fingerprint trùng với kết nối SSH vừa xác nhận.

### 7.4 Thông báo lỗi tiếng Việt (`ErrorTranslator`)

| Lỗi | Thông báo |
|---|---|
| `SshAuthenticationException` | Sai user, mật khẩu hoặc key. |
| Sai passphrase / key cần passphrase | Sai passphrase của key, hoặc key cần passphrase. |
| `SocketException` (từ chối, không tìm thấy host) | Không kết nối được tới `host:port`. Kiểm tra IP/port và mạng. |
| `SshOperationTimeoutException` | Hết thời gian chờ kết nối. |
| `SshConnectionException` khi đang dùng | Mất kết nối tới máy chủ. |
| `FileNotFoundException` (key) | Không tìm thấy file key: `<đường dẫn>`. |
| Khác | Lỗi không xác định: `<message>`, kèm ghi log chi tiết. |

---

## 8. Terminal

### 8.1 Cách hoạt động

```
xterm.js (WebView2)  ⇄  postMessage (JSON)  ⇄  TerminalTabViewModel (C#)  ⇄  ShellStream (SSH)
```

- Mỗi tab có một `WebView2`, dùng **chung một `CoreWebView2Environment`** (tiết kiệm RAM).
- Nạp trang bằng `SetVirtualHostNameToFolderMapping("snterm.local", <wwwroot>, Allow)` rồi mở `https://snterm.local/terminal.html`.
- Cấu hình WebView2: tắt menu chuột phải mặc định (`AreDefaultContextMenusEnabled = false`), tắt phím tắt trình duyệt (`AreBrowserAcceleratorKeysEnabled = false`), tắt zoom, tắt status bar; DevTools chỉ bật ở bản Debug.

### 8.2 Giao thức tin nhắn

JS → C#:

| `type` | Dữ liệu | Ý nghĩa |
|---|---|---|
| `ready` | `cols`, `rows` | Terminal đã sẵn sàng (đã nạp xong font) |
| `input` | `data` (string) | Người dùng gõ phím |
| `resize` | `cols`, `rows` | Đổi kích thước (debounce 100ms) |
| `copy` | `text` | Có vùng chọn mới cần copy |
| `requestPaste` | | Muốn dán |
| `contextMenu` | `hasSelection` | Muốn hiện menu |
| `hotkey` | `name` | Phím tắt ứng dụng (mục 8.6) |

C# → JS:

| `type` | Dữ liệu | Ý nghĩa |
|---|---|---|
| `output` | `b64` (Base64 của byte thô) | Dữ liệu từ server |
| `paste` | `text` | Nội dung cần dán |
| `settings` | font, cỡ chữ, theme, scrollback… | Áp dụng cài đặt |
| `status` | `text` | Dòng thông báo in vào terminal (VD: "Đang kết nối...") |
| `selectAll` / `clear` / `focus` | | Lệnh từ menu |

### 8.3 Luồng dữ liệu và tiếng Việt

- **Gửi lên server**: chuỗi `input` → `Encoding.UTF8.GetBytes` → `ShellStream.Write` + `Flush`.
- **Nhận từ server**: vòng đọc nền `ReadAsync` bộ đệm 32KB. **Gửi byte thô dạng Base64**, JS chuyển thành `Uint8Array` rồi `term.write(bytes)`. xterm.js tự giải mã UTF-8 liên tục, nên **ký tự tiếng Việt không bị vỡ** khi một ký tự nhiều byte bị cắt giữa hai lần đọc. Không tự giải mã UTF-8 ở phía C#.
- **Gom dữ liệu**: gộp output mỗi ~16ms hoặc khi đủ 64KB rồi mới gửi sang JS, để lệnh in nhiều (VD `cat` file lớn) không làm treo giao diện. Gửi bằng `Dispatcher.InvokeAsync`.
- Tạo shell: `CreateShellStream("xterm-256color", cols, rows, 0, 0, 65536)`, dùng `cols/rows` nhận từ tin nhắn `ready`.
- Đổi kích thước: `ShellStream.ChangeWindowSize(cols, rows, 0, 0)`.
- Khi `ShellStream.Closed` hoặc đọc trả về 0: in `[Đã ngắt kết nối] Nhấn Enter để kết nối lại`, đổi trạng thái tab. Enter lúc đó → kết nối lại.

### 8.4 `terminal.js` — cấu hình xterm.js

```js
const term = new Terminal({
  fontFamily: "'JetBrains Mono', 'Cascadia Mono', Consolas, monospace",
  fontSize: 14,
  scrollback: 10000,
  cursorBlink: true,
  allowProposedApi: true,          // cần cho addon unicode11
  rightClickSelectsWord: false,    // để chuột phải không tự chọn từ
});
term.loadAddon(fitAddon);
term.loadAddon(new Unicode11Addon.Unicode11Addon());
term.unicode.activeVersion = '11';
```

- **Phải chờ font nạp xong** (`await document.fonts.load("14px 'JetBrains Mono'")`) rồi mới `term.open()`, nếu không xterm đo sai độ rộng ký tự.
- Dùng `ResizeObserver` trên khung chứa để gọi `fitAddon.fit()`.

### 8.5 Quét khối để copy, chuột phải để dán/menu

```js
// Quét khối là copy (khi nhả chuột)
term.element.addEventListener('mouseup', (e) => {
  if (e.button !== 0 || !settings.copyOnSelect) return;
  const text = term.getSelection();
  if (text) post({ type: 'copy', text });
});

// Chuột phải
term.element.addEventListener('contextmenu', (e) => {
  e.preventDefault();
  const wantPaste = (settings.rightClickAction === 'Paste') !== e.shiftKey;
  if (wantPaste) post({ type: 'requestPaste' });
  else post({ type: 'contextMenu', hasSelection: term.hasSelection() });
});

// Nhận nội dung dán từ C#
// term.paste(text) tự đổi xuống dòng thành \r và hỗ trợ bracketed paste
```

Phía C#:
- `copy` → `ClipboardService.SetText(text)`. **Thử lại tối đa 5 lần, cách nhau 50ms** nếu gặp `COMException` (clipboard đang bị chương trình khác giữ).
- `requestPaste` → đọc clipboard; nếu có xuống dòng và `ConfirmMultilinePaste` bật → hỏi "Nội dung có N dòng, bạn chắc chắn muốn dán?"; đồng ý → gửi `paste`.
- `contextMenu` → hiện `ContextMenu` WPF tại vị trí chuột (`PlacementMode.MousePoint`) gồm: **Copy** (mờ nếu không có vùng chọn), **Dán**, **Chọn tất cả**, **Xóa màn hình**.

### 8.6 Phím tắt

Khi WebView2 đang giữ focus, phím tắt WPF **không** nhận được. Vì vậy bắt phím trong JS bằng `term.attachCustomKeyEventHandler`, trả về `false` và gửi `hotkey` sang C#:

| Phím | Chức năng |
|---|---|
| `Ctrl+Shift+C` | Copy |
| `Ctrl+Shift+V` | Dán |
| `Ctrl+Shift+W` | Đóng tab (hỏi xác nhận nếu đang kết nối) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Chuyển tab |
| `Alt+1` … `Alt+9` | Nhảy tới tab thứ 1–9 |
| `Ctrl+=` / `Ctrl+-` / `Ctrl+0` | Tăng / giảm / đặt lại cỡ chữ |

Không chiếm `Ctrl+C`, `Ctrl+W`, `Ctrl+V` thường, vì bash/vim cần các phím này.

Khi chọn tab: focus vào WebView2 và gửi `focus` để gọi `term.focus()`.

---

## 9. SFTP

### 9.1 Giao diện `SftpPanel`

```
[⬆ Lên] [⟳] [🏠] [+ Thư mục] [Upload] [Download]  [ ] Hiện file ẩn
[ /home/ubuntu/project                                   ]  ← gõ đường dẫn, Enter
┌──────────────────────┬─────────┬──────────────────┬───────────┐
│ Tên                  │ Kích thước│ Sửa đổi         │ Quyền     │
│ 📁 src               │         │ 2026-09-20 10:12 │ rwxr-xr-x │
│ 📄 báo_cáo.txt       │ 12 KB   │ 2026-09-21 08:30 │ rw-r--r-- │
└──────────────────────┴─────────┴──────────────────┴───────────┘
[Thanh tiến trình truyền file: báo_cáo.txt 45%  1.2 MB/s  (Hủy)]
```

- Thư mục lên trước, rồi file, sắp theo tên (không phân biệt hoa thường). Ẩn `.` và `..`.
- Nhấp đúp thư mục → vào trong. Nhấp đúp file → không làm gì (ngoài phạm vi).
- Chuột phải: **Download**, **Đổi tên**, **Xóa** (hỏi xác nhận, liệt kê tên), **Tạo thư mục**, **Làm mới**, **Copy đường dẫn**.
- Chọn nhiều file bằng Ctrl/Shift.
- **Kéo file/thư mục từ Windows Explorer thả vào danh sách → upload** vào thư mục đang xem.
- Upload/Download thư mục → làm đệ quy.
- Trùng tên → hỏi: [Ghi đè] [Bỏ qua] [Ghi đè tất cả] [Hủy].
- Download → chọn thư mục đích bằng hộp thoại; nhớ thư mục lần trước.

### 9.2 Kỹ thuật

- Danh sách: `SftpClient.ListDirectoryAsync`.
- Truyền file: hàng đợi `TransferQueueViewModel`, **chạy lần lượt từng file**, dùng `UploadFileAsync`/`DownloadFileAsync` có `IProgress` và `CancellationToken`. Hiển thị %, tốc độ, nút Hủy; hủy thì xóa file dở dang.
- Tên file tiếng Việt: SSH.NET dùng UTF-8 mặc định; kiểm tra upload/download/đổi tên với tên có dấu.
- Lỗi quyền (`SftpPermissionDeniedException`) → "Không có quyền thực hiện thao tác này".
- Sau mỗi thao tác thay đổi → tự làm mới danh sách.

---

## 10. Các giai đoạn triển khai

### Giai đoạn 0 — Khung dự án
- Tạo solution, project WPF `net10.0-windows`, project test xUnit, `.gitignore` cho .NET, `git init`.
- Cài NuGet: `CommunityToolkit.Mvvm`, `SSH.NET`, `Microsoft.Web.WebView2`, `System.Security.Cryptography.ProtectedData`.
- Tải xterm.js, addons, font JetBrains Mono vào `wwwroot` (mục 3).
- Dựng `MainWindow` theo 6.1 với dữ liệu giả: cột trái 2 tab, `GridSplitter`, cột phải là thanh tab ngang tự vẽ + vùng chứa terminal (mục 6.6, chưa cần chức năng), thanh trạng thái.

**Hoàn thành khi:** `dotnet build` thành công; **[Tay]** mở app thấy bố cục 2 cột, kéo được thanh chia.

### Giai đoạn 1 — Quản lý VM và form Thêm VM
- `AppPaths`, `SessionStore`, `SecretProtector`, `SettingsStore`.
- `SessionListViewModel` + danh sách gom nhóm chọn được nhiều VM (`ListBox` Extended + `GroupStyle`), nhóm "Gần đây", tìm kiếm, menu chuột phải, phím tắt (mục 6.2).
- `SessionStore` lưu ngay sau mỗi thay đổi, sao lưu tự động hằng ngày, xử lý file hỏng (mục 5.1).
- `SessionEditorDialog` đầy đủ theo 6.3 (nút "Kiểm tra kết nối" tạm để mờ, bật ở giai đoạn 2).
- Unit test: lưu/đọc session, mã hóa/giải mã, kiểm tra dữ liệu nhập (port sai, host trống…).

**Hoàn thành khi:** test qua; **[Tay]** thêm/sửa/nhân bản/xóa VM, tắt mở lại app vẫn còn đủ VM và đúng nhóm; chọn nhiều VM bằng Ctrl/Shift được; thư mục `backups\` có bản sao lưu; mở `sessions.json` **không** thấy mật khẩu dạng chữ thường; tên VM tiếng Việt hiển thị đúng.

### Giai đoạn 2 — Kết nối SSH và terminal
- `SshConnectionFactory`, `KnownHostsStore`, `HostKeyDialog`, `PasswordPromptDialog`, `ErrorTranslator`.
- `SshConnection` (phần SSH), `TerminalView` + WebView2 + `terminal.html/js/css`, giao thức tin nhắn 8.2, luồng dữ liệu 8.3.
- Thanh tab ngang đầy đủ theo mục 6.6: mở nhiều VM cùng lúc (song song tối đa `MaxParallelConnects`, mặc định 4), hàng đợi hộp thoại, cuộn tab, kéo thả sắp xếp, chuột giữa đóng tab, menu chuột phải trên tab, `Alt+1…9`, giữ nội dung terminal khi chuyển tab.
- Kết nối nhanh theo mục 6.2: VM đã lưu mật khẩu + host key đã tin → vào thẳng; mật khẩu đã lưu sai → hỏi lại và cập nhật; cập nhật `LastConnectedAt`.
- Kết nối lại.
- Bật nút "Kiểm tra kết nối" trong form.

**Hoàn thành khi:** **[Tay]**
- Đăng nhập bằng mật khẩu; bằng key OpenSSH (ed25519 và RSA); bằng key có passphrase; bằng key `.ppk`.
- Lần đầu kết nối hiện hộp thoại host key; lần sau không hỏi nữa.
- `htop`, `vim`, `nano`, `top` hiển thị đúng; kéo to/nhỏ cửa sổ thì nội dung tự dàn lại.
- `echo "Xin chào, tiếng Việt có dấu: ắ ằ ẳ ẵ ặ ơ ư đ"` hiển thị đúng.
- Gõ tiếng Việt bằng **Unikey/EVKey** (Telex và VNI) trong bash và trong `nano` đúng dấu.
- `cat` một file log lớn (vài MB) không làm treo app.
- Sai mật khẩu / sai IP / sai port → thông báo tiếng Việt dễ hiểu.
- Tắt mạng hoặc `sudo reboot` VM → tab báo ngắt; Enter để kết nối lại được.
- **Nhấp đúp VM đã lưu mật khẩu → vào thẳng terminal, không hiện hộp thoại nào.** Tắt app, mở lại, nhấp đúp → vẫn vào thẳng.
- Chọn 5 VM (Ctrl+nhấp) → Enter → mở 5 tab ngang, mỗi tab một VM, kết nối song song. Một VM sai IP thì chỉ tab đó báo lỗi.
- "Kết nối tất cả" trên một nhóm mở đủ tab. Mở 15 tab → thanh tab cuộn được, nút ▾ liệt kê đủ tab.
- Chuyển qua lại giữa các tab: nội dung và lịch sử cuộn của từng tab vẫn còn nguyên.
- Kéo thả đổi thứ tự tab; chuột giữa đóng tab; "Đóng các tab khác" hoạt động đúng.

### Giai đoạn 3 — Copy/Paste
- Theo mục 8.5 và 8.6, `ClipboardService` có retry.

**Hoàn thành khi:** **[Tay]**
- Bôi đen chữ trong terminal → mở Notepad, Ctrl+V ra đúng nội dung (kể cả tiếng Việt).
- Chuột phải trong terminal → dán; Shift+chuột phải → hiện menu (và ngược lại khi đổi cài đặt).
- Dán nội dung nhiều dòng → hỏi xác nhận.
- `Ctrl+C` vẫn dừng được lệnh `ping` đang chạy.

### Giai đoạn 4 — SFTP
- Mở `SftpClient` khi kết nối; `SftpViewModel`, `SftpPanel`, `TransferQueueViewModel` theo mục 9.
- Tự chuyển cột trái sang SFTP khi đăng nhập; đổi tab thì SFTP đổi theo.

**Hoàn thành khi:** **[Tay]**
- Đăng nhập xong cột trái tự sang SFTP ở thư mục home.
- Mở 2 tab 2 VM, chuyển qua lại thì SFTP đổi đúng VM.
- Upload/download file và thư mục (có thư mục con), có tiến trình, hủy được.
- Kéo file từ Explorer thả vào → upload.
- Đổi tên, xóa, tạo thư mục; tên có dấu tiếng Việt đều đúng.
- Upload vào thư mục không có quyền → thông báo lỗi rõ ràng, app không crash.

### Giai đoạn 5 — Export / Import danh sách VM
- `ExportFile` model, `SessionExporter`, `SessionImporter` theo mục 5.4.
- `ExportDialog` (6.7), `ImportDialog` (6.8), nút Export/Import trên thanh công cụ, mục Export trong menu chuột phải VM/nhóm, kéo thả file `.snterm` vào cửa sổ để Import.
- Unit test (bắt buộc):
  - Export rồi Import lại (round-trip) ở cả 2 chế độ: dữ liệu giống hệt, tên tiếng Việt giữ nguyên.
  - File chế độ "không kèm mật khẩu": tìm trong nội dung file **không có** chuỗi mật khẩu/passphrase nào.
  - File chế độ có bảo vệ: tìm trong nội dung file **không có** mật khẩu dạng chữ thường.
  - Sai mật khẩu Export → báo sai, không nhập gì.
  - Sửa 1 byte trong `cipherText` → VM đó mất phần bí mật, các VM khác vẫn đúng.
  - Tráo `secrets` giữa 2 VM → giải mã thất bại (nhờ AAD).
  - `version` lớn hơn hỗ trợ / `format` sai / JSON hỏng → báo lỗi đúng, không nhập gì.
  - Phát hiện trùng theo `Id` và theo `host+port+user`; 3 cách xử lý Bỏ qua / Ghi đè / Thêm bản sao cho kết quả đúng.
  - Key file được giải nén ra `keys\` và `KeyFilePath` trỏ đúng.

**Hoàn thành khi:** test qua; **[Tay]**
- Export tất cả (không kèm mật khẩu) → mở file bằng Notepad: thấy host/user, **không** thấy mật khẩu.
- Export có mật khẩu + kèm key → chép sang máy Windows khác (hoặc tài khoản Windows khác) → Import, nhập mật khẩu Export → nhấp đúp VM **vào thẳng được** (chỉ hỏi host key lần đầu).
- Import file vào máy đã có một số VM giống → hiện "Trùng", chọn Bỏ qua / Ghi đè / Thêm bản sao đều đúng.
- Import sai mật khẩu → báo "Sai mật khẩu file", thử lại được.
- Sau Import, thư mục `backups\` có bản `sessions-before-import-…json`.

### Giai đoạn 6 — Cài đặt và hoàn thiện
- `SettingsDialog`, theme Sáng/Tối cho cả giao diện và terminal, đổi cỡ chữ bằng phím tắt.
- Nhớ kích thước/vị trí cửa sổ, độ rộng cột trái.
- Ghi log lỗi ra `%LOCALAPPDATA%\SNTerm\logs\` (không ghi mật khẩu); bắt lỗi toàn cục (`DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`) để app không tự tắt.
- Đóng app khi còn tab kết nối → hỏi xác nhận, ngắt kết nối gọn gàng.

**Hoàn thành khi:** **[Tay]** đổi font/cỡ chữ/theme áp dụng ngay cho mọi tab; mở lại app giữ đúng cài đặt và kích thước cửa sổ.

### Giai đoạn 7 — Đóng gói
- Bản portable: `dotnet publish src/SNTerm -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`, kèm thư mục `wwwroot`. Nén thành `SNTerm-portable.zip`.
- Kiểm tra WebView2 Runtime lúc khởi động; nếu thiếu → thông báo kèm link tải của Microsoft.
- (Tùy chọn) Bộ cài bằng Inno Setup.
- Viết `README.md` tiếng Việt: cách cài, cách dùng, nơi lưu dữ liệu, cách sao lưu, **cách chuyển danh sách VM sang máy mới bằng Export/Import**.

**Hoàn thành khi:** **[Tay]** chép file zip sang máy Windows khác (không cài .NET), giải nén, chạy được và kết nối được VM.

---

## 11. Lưu ý và rủi ro

- **Locale trên VM**: nếu VM đặt `LANG=C`/`POSIX`, lệnh `ls` có thể hiện tên tiếng Việt thành `?`. Đây là cấu hình phía server (đặt `LANG=en_US.UTF-8` hoặc `C.UTF-8`), ghi vào README phần "Xử lý sự cố".
- **Mã hóa DPAPI** gắn với tài khoản Windows: chép `sessions.json` sang máy khác thì mật khẩu không giải mã được. Cách đúng để chuyển máy là **Export có kèm mật khẩu** rồi Import (mục 5.4). Ghi rõ trong README.
- **SmartScreen**: file `.exe` chưa ký số sẽ bị Windows cảnh báo lần đầu chạy. Ký số (code signing) là tùy chọn, tốn phí.
- **RAM**: mỗi tab một WebView2, khoảng vài chục MB/tab (các tab dùng chung một tiến trình trình duyệt nên tăng chậm hơn). Mở 20–30 tab vẫn chấp nhận được; ghi chú trong README nếu người dùng hay mở rất nhiều tab.
- **File Export có kèm mật khẩu** vẫn là dữ liệu nhạy cảm: ai có file **và** mật khẩu Export sẽ đăng nhập được mọi VM trong đó. README khuyên dùng mật khẩu Export mạnh, không gửi file và mật khẩu qua cùng một kênh.
- Nếu API của SSH.NET hoặc xterm.js khác với mô tả ở đây (do phiên bản), làm theo tài liệu của phiên bản đang dùng và ghi chú lại thay đổi.

---

## 12. Cách kiểm thử nhanh khi chưa có VM

Có thể dùng WSL trên chính máy Windows:

```bash
sudo apt update && sudo apt install -y openssh-server
sudo service ssh start
```

Rồi thêm VM với host `127.0.0.1`, port `22`, user/mật khẩu của WSL.
