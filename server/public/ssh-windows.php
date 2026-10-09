<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$vi = wn_lang() === 'vi';
$related = [
    [$vi ? '/ssh-iphone' : '/en/ssh-iphone', $vi ? 'SSH trên iPhone không cần App Store' : 'SSH on iPhone without the App Store'],
    [$vi ? '/chuyen-tu-mobaxterm' : '/en/chuyen-tu-mobaxterm', $vi ? 'Chuyển từ MobaXterm sang WNTerm' : 'Move from MobaXterm to WNTerm'],
    [$vi ? '/' : '/en/', $vi ? 'Trang chủ WNTerm' : 'WNTerm home'],
    [$vi ? '/dong-bo-pc-dien-thoai' : '/en/dong-bo-pc-dien-thoai', $vi ? 'Đồng bộ danh sách máy chủ PC ↔ điện thoại' : 'Sync your server list PC ↔ phone'],
];

if ($vi) {
    $title = 'Phần mềm SSH & SFTP cho Windows — WNTerm';
    $desc = 'WNTerm là ứng dụng SSH và SFTP cho Windows 10/11: quản lý nhiều máy chủ, terminal mượt, kéo thả file, đồng bộ mã hóa đầu-cuối. Tải miễn phí.';
    $h1 = 'Phần mềm SSH & SFTP cho Windows';
    $intro = 'WebNow Terminal (WNTerm) là ứng dụng SSH client kèm SFTP cho Windows 10/11: lưu hàng chục máy chủ, bấm một lần là vào terminal, kéo thả file lên server mà không cần mở thêm công cụ khác.';
    $sections = [
        ['Vì sao dùng WNTerm để SSH vào máy chủ trên Windows?', '<p>Windows có sẵn OpenSSH nhưng chỉ là dòng lệnh: bạn phải tự nhớ địa chỉ, cổng, khóa và không có trình duyệt file. WNTerm gom tất cả vào một cửa sổ: danh sách máy chủ có nhóm và thẻ, nhiều tab terminal, SFTP cạnh bên và thanh theo dõi CPU/RAM.</p>'],
        ['Tính năng chính', '<ul><li><b>Terminal mượt</b> dựa trên xterm.js, gõ tiếng Việt (Unikey/EVKey, Telex/VNI) không lỗi, bôi đen là tự copy, chuột phải để dán.</li><li><b>SFTP tích hợp</b>: kéo thả tải lên, tải xuống có phần trăm và tốc độ, tạo thư mục, đổi tên, chmod, mở file bằng trình soạn thảo trên máy rồi tự tải lại khi lưu.</li><li><b>Theo dõi máy chủ</b>: hệ điều hành, CPU, RAM, tốc độ mạng, uptime, dung lượng ổ đĩa ngay dưới terminal.</li><li><b>Tìm nhanh</b> (Ctrl+K), <b>Snippets</b> lưu lệnh hay dùng, <b>kết nối nhanh</b> không cần lưu.</li><li><b>Backup và đồng bộ</b> danh sách máy chủ giữa PC, Android, iPhone; có thùng rác 30 ngày và lịch sử backup để khôi phục.</li><li><b>Nhập từ MobaXterm</b> bằng file <code>.mxtsessions</code>.</li></ul>'],
        ['Cài đặt trên Windows', '<ol><li>Tải file .zip (~52 MB) ở nút phía trên, giải nén.</li><li>Chạy <b>WNTerm-Setup.exe</b> — đã kèm sẵn .NET.</li><li>Cần Microsoft Edge WebView2 Runtime (thường đã có sẵn trên Windows 10/11).</li></ol><p>Bộ cài chưa ký số thương mại nên SmartScreen có thể cảnh báo; chọn “Thông tin thêm” → “Vẫn chạy” nếu bạn tải từ đúng trang này.</p>'],
        ['Bảo mật', '<p>Mật khẩu server lưu trên máy bạn ở dạng mã hóa. Nếu đăng nhập tài khoản để đồng bộ, dữ liệu được mã hóa đầu-cuối ngay trên máy trước khi tải lên: máy chủ WNTerm chỉ giữ bản đã mã hóa và không có khóa để đọc. Xem thêm <a href="/#bao-mat">phần Bảo mật</a>.</p>'],
    ];
    $faq = [
        ['WNTerm có miễn phí không?', 'Hiện bạn tải và dùng ngay toàn bộ tính năng, kể cả tài khoản đồng bộ. Mọi thay đổi về gói sẽ được thông báo trên trang chủ.'],
        ['WNTerm hỗ trợ đăng nhập bằng khóa SSH không?', 'Có. Bạn có thể dùng mật khẩu hoặc khóa riêng (kể cả khóa có passphrase) cho từng máy chủ.'],
        ['Tôi có dùng được trên Android và iPhone không?', 'Có: Android có APK riêng, iPhone dùng bản web chạy ngay trong Safari. Cùng một tài khoản, danh sách máy chủ giống nhau ở mọi thiết bị.'],
    ];
} else {
    $title = 'SSH & SFTP client for Windows — WNTerm';
    $desc = 'WNTerm is an SSH and SFTP client for Windows 10/11: manage many servers, smooth terminal, drag-and-drop files, end-to-end encrypted sync. Free download.';
    $h1 = 'SSH & SFTP client for Windows';
    $intro = 'WebNow Terminal (WNTerm) is an SSH client with built-in SFTP for Windows 10/11: save dozens of servers, open a terminal in one click and drag files onto your server without extra tools.';
    $sections = [
        ['Why use WNTerm to SSH into servers on Windows?', '<p>Windows ships OpenSSH, but only as a command line: you have to remember hosts, ports and keys, and there is no file browser. WNTerm puts everything in one window: a grouped and tagged server list, multiple terminal tabs, SFTP alongside and a CPU/RAM monitor bar.</p>'],
        ['Key features', '<ul><li><b>Smooth terminal</b> built on xterm.js, with correct Vietnamese input, select-to-copy and right-click paste.</li><li><b>Built-in SFTP</b>: drag-and-drop upload, downloads with percentage and speed, create folders, rename, chmod, open a file in your local editor and it uploads back when you save.</li><li><b>Server monitoring</b>: OS, CPU, RAM, network speed, uptime and disk usage right under the terminal.</li><li><b>Quick find</b> (Ctrl+K), <b>Snippets</b> for frequent commands, <b>quick connect</b> without saving.</li><li><b>Backup and sync</b> of your server list across PC, Android and iPhone, with a 30-day trash and backup history to restore from.</li><li><b>MobaXterm import</b> from a <code>.mxtsessions</code> file.</li></ul>'],
        ['Installing on Windows', '<ol><li>Download the .zip (~52 MB) with the button above and extract it.</li><li>Run <b>WNTerm-Setup.exe</b> — .NET is included.</li><li>Requires the Microsoft Edge WebView2 Runtime (usually already present on Windows 10/11).</li></ol><p>The installer is not commercially code-signed, so SmartScreen may warn; choose “More info” → “Run anyway” if you downloaded it from this site.</p>'],
        ['Security', '<p>Server passwords are stored encrypted on your computer. If you sign in for sync, data is end-to-end encrypted on your device before upload: the WNTerm server only keeps the encrypted copy and has no key to read it. See the <a href="/en/#bao-mat">Security section</a>.</p>'],
    ];
    $faq = [
        ['Is WNTerm free?', 'Right now you can download and use every feature, including sync accounts. Any plan changes will be announced on the home page.'],
        ['Does WNTerm support SSH key login?', 'Yes. You can use a password or a private key (including passphrase-protected keys) per server.'],
        ['Can I use it on Android and iPhone?', 'Yes: Android has its own APK and iPhone uses the web app that runs in Safari. One account keeps the same server list on every device.'],
    ];
}
view('guide', compact('title', 'desc', 'h1', 'intro', 'sections', 'faq', 'related'));
