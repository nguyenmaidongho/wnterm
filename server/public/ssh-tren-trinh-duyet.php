<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$vi = wn_lang() === 'vi';
$related = [
    [$vi ? '/ssh-iphone' : '/en/ssh-iphone', $vi ? 'SSH trên iPhone không cần App Store' : 'SSH on iPhone without the App Store'],
    [$vi ? '/dong-bo-pc-dien-thoai' : '/en/dong-bo-pc-dien-thoai', $vi ? 'Đồng bộ danh sách máy chủ PC ↔ điện thoại' : 'Sync your server list PC ↔ phone'],
    [$vi ? '/ssh-windows' : '/en/ssh-windows', $vi ? 'Phần mềm SSH & SFTP cho Windows' : 'SSH & SFTP client for Windows'],
];

if ($vi) {
    $title = 'SSH trên trình duyệt — dùng mọi nơi, không cần cài đặt | WNTerm';
    $desc = 'SSH và SFTP ngay trong trình duyệt, không cần cài phần mềm: mở web app WNTerm trên mọi máy, đăng nhập là có danh sách máy chủ. Cần mạng nội bộ thì dùng app cài đặt.';
    $h1 = 'SSH trên trình duyệt: dùng mọi nơi, không cần cài đặt';
    $intro = 'Mượn máy của bạn bè, dùng máy công ty không cài được phần mềm, hay chỉ cần vào server gấp từ iPad? Mở web app WNTerm trong trình duyệt là có terminal và SFTP đầy đủ, với danh sách máy chủ đồng bộ từ tài khoản của bạn.';
    $sections = [
        ['Cách dùng', '<ol><li>Mở <a href="/app/">wnterm.webnow.vn/app</a> bằng trình duyệt hiện đại trên bất kỳ thiết bị nào (máy tính, điện thoại, máy tính bảng).</li><li>Đăng nhập tài khoản WNTerm để lấy danh sách máy chủ đã đồng bộ, hoặc thêm máy chủ mới / nhập file <code>.wnterm</code>.</li><li>Chạm hoặc bấm vào máy chủ để kết nối. Có thể “Thêm vào Màn hình chính” để dùng như một app.</li></ol><p>Chưa có tài khoản? <a href="/dang-ky">Tạo tài khoản</a> và nhớ lưu mã khôi phục.</p>'],
        ['Có những gì trong bản web', '<ul><li>Terminal đầy đủ với hàng phím phụ (Ctrl, Alt, Esc, Tab, mũi tên…), nhiều tab, thanh theo dõi CPU/RAM/mạng/ổ đĩa.</li><li>SFTP: duyệt, tải lên/xuống, chọn nhiều file, nén ZIP khi tải thư mục, sửa file văn bản, chmod.</li><li>Backup, thùng rác và khôi phục phiên bản giống bản máy tính; giao diện sáng/tối.</li><li>Nhập/xuất file <code>.wnterm</code>, nhập từ MobaXterm.</li></ul>'],
        ['Cách hoạt động và độ an toàn', '<p>Giao thức SSH chạy ngay trong trình duyệt của bạn. Dữ liệu đi qua một trạm trung chuyển của WebNow nhưng đã được mã hóa giữa trình duyệt và máy chủ SSH, nên trạm không đọc được mật khẩu, lệnh hay nội dung file. Trạm chỉ phục vụ tài khoản đã đăng nhập, chặn địa chỉ nội bộ và giới hạn số kết nối. Dùng trên máy lạ thì nhớ đăng xuất khi xong.</p>'],
        ['Khi nào nên dùng ứng dụng cài đặt', '<p>Bản web <b>chỉ tới được máy chủ SSH có địa chỉ công khai trên internet</b>. Máy chủ trong mạng nội bộ hoặc sau VPN thì dùng <a href="/ssh-windows">ứng dụng Windows</a> hoặc Android — chúng kết nối trực tiếp từ thiết bị của bạn. Bản web cũng có thể bị ngắt khi để ở nền quá lâu trên điện thoại; quay lại và bấm “Kết nối lại”.</p>'],
    ];
    $faq = [
        ['Dùng SSH trên trình duyệt có cần cài gì không?', 'Không. Chỉ cần mở trang web app và đăng nhập tài khoản WNTerm.'],
        ['Dùng trên máy lạ có an toàn không?', 'Mật khẩu máy chủ được mã hóa trước khi lưu vào tài khoản và trạm trung chuyển không đọc được nội dung phiên. Dù vậy, trên máy công cộng bạn nên đăng xuất và không lưu thông tin ở trình duyệt khi xong.'],
        ['Tại sao không vào được máy chủ trong mạng nội bộ?', 'Bản web kết nối qua trạm trên internet nên chỉ tới được máy chủ có địa chỉ công khai. Dùng ứng dụng Windows hoặc Android cho máy chủ nội bộ/VPN.'],
        ['Có tốn phí không?', 'Hiện bạn dùng được toàn bộ tính năng. Mọi thay đổi về gói sẽ được thông báo trên trang chủ.'],
    ];
} else {
    $title = 'SSH in your browser — use it anywhere, no install | WNTerm';
    $desc = 'SSH and SFTP right in your browser, nothing to install: open the WNTerm web app on any device and sign in to get your server list. Use the app for private networks.';
    $h1 = 'SSH in your browser: use it anywhere, no install';
    $intro = 'Borrowing a friend’s computer, on a work PC where you cannot install software, or need to reach a server from an iPad in a hurry? Open the WNTerm web app in your browser for a full terminal and SFTP with the server list synced from your account.';
    $sections = [
        ['How to use it', '<ol><li>Open <a href="/app/">wnterm.webnow.vn/app</a> in a modern browser on any device (computer, phone, tablet).</li><li>Sign in to your WNTerm account to get your synced servers, or add a new server / import a <code>.wnterm</code> file.</li><li>Tap or click a server to connect. You can “Add to Home Screen” to use it like an app.</li></ol><p>No account yet? <a href="/en/dang-ky">Create one</a> and keep the recovery code.</p>'],
        ['What the web app includes', '<ul><li>Full terminal with an extra key row (Ctrl, Alt, Esc, Tab, arrows…), multiple tabs and a CPU/RAM/network/disk monitor bar.</li><li>SFTP: browse, upload/download, multi-select, ZIP when downloading folders, text editing, chmod.</li><li>Backup, trash and version restore just like the desktop app; light/dark theme.</li><li>Import/export <code>.wnterm</code> files and import from MobaXterm.</li></ul>'],
        ['How it works and how safe it is', '<p>The SSH protocol runs inside your browser. Traffic goes through a WebNow relay, but it is encrypted between your browser and the SSH server, so the relay cannot read your passwords, commands or file contents. The relay only serves signed-in accounts, blocks internal addresses and limits connections. On a shared computer, remember to sign out when you are done.</p>'],
        ['When to use the installed app instead', '<p>The web app <b>only reaches SSH servers with a public address on the internet</b>. For servers on a private network or behind a VPN, use the <a href="/en/ssh-windows">Windows app</a> or Android — they connect directly from your device. On phones the web app may also be dropped when left in the background for a long time; come back and tap “Reconnect”.</p>'],
    ];
    $faq = [
        ['Do I need to install anything to use SSH in the browser?', 'No. Just open the web app and sign in with your WNTerm account.'],
        ['Is it safe on a computer that is not mine?', 'Server passwords are encrypted before they are stored in your account and the relay cannot read session contents. Still, on a public machine you should sign out and not let the browser save anything when you finish.'],
        ['Why can I not reach a server on my internal network?', 'The web app connects through a relay on the internet, so it only reaches servers with a public address. Use the Windows or Android app for internal/VPN servers.'],
        ['Does it cost anything?', 'Right now every feature is usable. Any plan changes will be announced on the home page.'],
    ];
}
view('guide', compact('title', 'desc', 'h1', 'intro', 'sections', 'faq', 'related'));
