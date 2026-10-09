<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$vi = wn_lang() === 'vi';
$related = [
    [$vi ? '/ssh-windows' : '/en/ssh-windows', $vi ? 'Phần mềm SSH & SFTP cho Windows' : 'SSH & SFTP client for Windows'],
    [$vi ? '/chuyen-tu-mobaxterm' : '/en/chuyen-tu-mobaxterm', $vi ? 'Chuyển từ MobaXterm sang WNTerm' : 'Move from MobaXterm to WNTerm'],
    [$vi ? '/dieu-khoan' : '/en/dieu-khoan', $vi ? 'Điều khoản sử dụng' : 'Terms of use'],
    [$vi ? '/dong-bo-pc-dien-thoai' : '/en/dong-bo-pc-dien-thoai', $vi ? 'Đồng bộ danh sách máy chủ PC ↔ điện thoại' : 'Sync your server list PC ↔ phone'],
];

if ($vi) {
    $title = 'SSH trên iPhone không cần App Store — WNTerm';
    $desc = 'Dùng SSH và SFTP trên iPhone/iPad ngay trong Safari, không cần App Store. Terminal đầy đủ, đồng bộ danh sách máy chủ với PC và Android.';
    $h1 = 'SSH trên iPhone không cần App Store';
    $intro = 'WNTerm có bản web (PWA) chạy ngay trong Safari: mở trang, thêm vào Màn hình chính và dùng như một app — có terminal đầy đủ, SFTP và đồng bộ danh sách máy chủ với PC, Android.';
    $sections = [
        ['Cách dùng', '<ol><li>Mở <a href="/app/">wnterm.webnow.vn/app</a> bằng Safari trên iPhone/iPad.</li><li>Bấm nút Chia sẻ → <b>Thêm vào Màn hình chính</b> để có biểu tượng như app thật.</li><li>Đăng nhập tài khoản WNTerm để lấy danh sách máy chủ đã đồng bộ, hoặc thêm máy chủ mới / nhập file <code>.wnterm</code>.</li><li>Chạm một lần vào máy chủ để kết nối.</li></ol>'],
        ['Có những gì', '<ul><li>Terminal đầy đủ với hàng phím phụ (Ctrl, Alt, Esc, Tab, mũi tên…), nhiều tab và thanh theo dõi CPU/RAM/mạng/ổ đĩa.</li><li>SFTP: duyệt, tải lên/xuống, chọn nhiều file, nén ZIP khi tải thư mục, sửa file văn bản, chmod.</li><li>Backup, thùng rác và khôi phục phiên bản giống bản máy tính; giao diện sáng/tối.</li><li>Chụm hai ngón để đổi cỡ chữ, nút “Chép” để chọn và sao chép nội dung terminal.</li></ul>'],
        ['Cách hoạt động và độ an toàn', '<p>SSH chạy ngay trong trình duyệt của bạn. Dữ liệu đi qua một trạm trung chuyển của WebNow nhưng đã được mã hóa giữa điện thoại và máy chủ SSH, nên trạm không đọc được mật khẩu, lệnh hay nội dung file. Trạm chỉ phục vụ tài khoản đã đăng nhập và chặn địa chỉ nội bộ.</p>'],
        ['Giới hạn cần biết', '<p><b>Chỉ kết nối được tới máy chủ SSH có địa chỉ công khai trên internet.</b> Vì đi qua trạm trên internet nên bản iPhone không tới được máy chủ trong mạng nội bộ hoặc sau VPN; với những máy chủ đó hãy dùng ứng dụng Windows hoặc Android. Nếu để app ở nền quá lâu, iOS có thể ngắt kết nối — quay lại và bấm “Kết nối lại”.</p>'],
    ];
    $faq = [
        ['Có cần cài từ App Store không?', 'Không. Bản iPhone là web app chạy trong Safari, thêm vào Màn hình chính là dùng được.'],
        ['iPhone có kết nối được máy chủ trong mạng nội bộ/VPN không?', 'Không. Bản iPhone kết nối qua trạm trung chuyển trên internet nên chỉ tới được máy chủ có địa chỉ công khai. Dùng ứng dụng Windows hoặc Android cho máy chủ nội bộ.'],
        ['Trạm trung chuyển có đọc được mật khẩu của tôi không?', 'Không. SSH được mã hóa giữa trình duyệt của bạn và máy chủ đích; trạm chỉ chuyển các gói đã mã hóa.'],
    ];
} else {
    $title = 'SSH on iPhone without the App Store — WNTerm';
    $desc = 'Use SSH and SFTP on iPhone/iPad right in Safari, no App Store needed. Full terminal, server list synced with your PC and Android.';
    $h1 = 'SSH on iPhone without the App Store';
    $intro = 'WNTerm has a web app (PWA) that runs in Safari: open the page, add it to your Home Screen and use it like an app — full terminal, SFTP and a server list synced with your PC and Android.';
    $sections = [
        ['How to use it', '<ol><li>Open <a href="/app/">wnterm.webnow.vn/app</a> in Safari on your iPhone/iPad.</li><li>Tap Share → <b>Add to Home Screen</b> to get an app-like icon.</li><li>Sign in to your WNTerm account to get your synced servers, or add a new server / import a <code>.wnterm</code> file.</li><li>Tap a server once to connect.</li></ol>'],
        ['What you get', '<ul><li>Full terminal with an extra key row (Ctrl, Alt, Esc, Tab, arrows…), multiple tabs and a CPU/RAM/network/disk monitor bar.</li><li>SFTP: browse, upload/download, multi-select, ZIP when downloading folders, text editing, chmod.</li><li>Backup, trash and version restore just like the desktop app; light/dark theme.</li><li>Pinch to change the font size and use the “Copy” button to select and copy terminal output.</li></ul>'],
        ['How it works and how safe it is', '<p>SSH runs inside your browser. Traffic goes through a WebNow relay, but it is encrypted between your phone and your SSH server, so the relay cannot read your passwords, commands or file contents. The relay only serves signed-in accounts and blocks internal addresses.</p>'],
        ['Limits you should know', '<p><b>It only reaches SSH servers with a public address on the internet.</b> Because it goes through a relay on the internet, the iPhone app cannot reach servers on private networks or behind a VPN; for those, use the Windows or Android app. If the app stays in the background for a long time, iOS may drop the connection — come back and tap “Reconnect”.</p>'],
    ];
    $faq = [
        ['Do I need to install from the App Store?', 'No. The iPhone version is a web app that runs in Safari; add it to your Home Screen and it is ready.'],
        ['Can the iPhone reach servers on an internal network or VPN?', 'No. It connects through a relay on the internet, so it only reaches servers with a public address. Use the Windows or Android app for internal servers.'],
        ['Can the relay read my password?', 'No. SSH is encrypted between your browser and the target server; the relay only forwards encrypted packets.'],
    ];
}
view('guide', compact('title', 'desc', 'h1', 'intro', 'sections', 'faq', 'related'));
