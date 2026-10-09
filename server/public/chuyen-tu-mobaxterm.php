<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$vi = wn_lang() === 'vi';
$related = [
    [$vi ? '/ssh-windows' : '/en/ssh-windows', $vi ? 'Phần mềm SSH & SFTP cho Windows' : 'SSH & SFTP client for Windows'],
    [$vi ? '/ssh-iphone' : '/en/ssh-iphone', $vi ? 'SSH trên iPhone không cần App Store' : 'SSH on iPhone without the App Store'],
    [$vi ? '/' : '/en/', $vi ? 'Trang chủ WNTerm' : 'WNTerm home'],
    [$vi ? '/dong-bo-pc-dien-thoai' : '/en/dong-bo-pc-dien-thoai', $vi ? 'Đồng bộ danh sách máy chủ PC ↔ điện thoại' : 'Sync your server list PC ↔ phone'],
];

if ($vi) {
    $title = 'Chuyển từ MobaXterm sang WNTerm — nhập file .mxtsessions';
    $desc = 'Đang dùng MobaXterm? Xuất phiên ra file .mxtsessions rồi bấm Import trong WNTerm để mang toàn bộ máy chủ SSH/SFTP sang, dùng trên cả PC, Android và iPhone.';
    $h1 = 'Chuyển từ MobaXterm sang WNTerm';
    $intro = 'Không cần nhập lại từng máy chủ: xuất phiên từ MobaXterm ra file <code>.mxtsessions</code>, bấm Import trong WNTerm là danh sách SSH/SFTP của bạn xuất hiện, kèm cả bản trên Android và iPhone.';
    $sections = [
        ['Các bước chuyển', '<ol><li>Trong MobaXterm, xuất các phiên đã lưu ra file <code>.mxtsessions</code> (chuột phải vào nhóm phiên và chọn xuất phiên ra file).</li><li>Mở WNTerm, bấm <b>Import</b> và chọn file vừa xuất.</li><li>WNTerm đọc các phiên SSH/SFTP và thêm vào danh sách của bạn. Nếu máy chủ đã có sẵn, bạn chọn <b>bỏ qua</b>, <b>ghi đè</b> hoặc <b>thêm bản sao</b>. Có thể bấm quét để WNTerm tự tìm file <code>MobaXterm.ini</code> trên máy.</li><li>Kiểm tra lại từng máy chủ và nhập lại mật khẩu hoặc khóa nếu cần, rồi bấm một lần để kết nối.</li></ol><p>Bản web trên iPhone và bản Android cũng nhập được file này.</p>'],
        ['Bạn được thêm gì so với trước', '<ul><li>Danh sách máy chủ <b>đồng bộ</b> giữa PC, Android và iPhone qua tài khoản, mã hóa đầu-cuối.</li><li><b>Thùng rác 30 ngày</b> và lịch sử backup: lỡ xóa nhầm VM vẫn lấy lại được.</li><li><b>Snippets</b> lưu lệnh dùng thường xuyên, <b>tìm nhanh</b> bằng Ctrl+K, kết nối nhanh không cần lưu.</li><li>Thanh theo dõi CPU/RAM/mạng/ổ đĩa ngay dưới terminal, trạng thái online và độ trễ từng máy chủ.</li></ul>'],
        ['Lưu ý', '<p>WNTerm tập trung vào SSH và SFTP: chỉ các phiên SSH/SFTP được nhập, các loại phiên khác của MobaXterm không được đưa sang. Hãy kiểm tra danh sách sau khi nhập.</p>'],
    ];
    $faq = [
        ['Nhập từ MobaXterm có mất dữ liệu cũ không?', 'Không. Nhập chỉ thêm vào danh sách của WNTerm; file và dữ liệu trong MobaXterm giữ nguyên.'],
        ['Nhập xong có bị trùng máy chủ không?', 'Khi gặp máy chủ đã có sẵn, WNTerm hỏi bạn muốn bỏ qua, ghi đè hay thêm bản sao.'],
        ['Có dùng trên điện thoại được không?', 'Có. Đăng nhập cùng tài khoản trên Android hoặc iPhone để thấy cùng danh sách máy chủ.'],
    ];
} else {
    $title = 'Move from MobaXterm to WNTerm — import .mxtsessions';
    $desc = 'Using MobaXterm? Export your sessions to a .mxtsessions file and hit Import in WNTerm to bring all your SSH/SFTP servers over — on PC, Android and iPhone.';
    $h1 = 'Move from MobaXterm to WNTerm';
    $intro = 'No need to retype every server: export your sessions from MobaXterm to a <code>.mxtsessions</code> file, hit Import in WNTerm and your SSH/SFTP list appears — on Android and iPhone too.';
    $sections = [
        ['Steps', '<ol><li>In MobaXterm, export your saved sessions to a <code>.mxtsessions</code> file (right-click the session group → export sessions to a file).</li><li>Open WNTerm, click <b>Import</b> and pick the exported file.</li><li>WNTerm reads the SSH/SFTP sessions and adds them to your list; if a server already exists you choose to <b>skip</b>, <b>overwrite</b> or <b>add a copy</b>. You can also scan so WNTerm finds your <code>MobaXterm.ini</code> automatically.</li><li>Check each server and re-enter the password or key if needed, then connect in one click.</li></ol><p>The iPhone web app and the Android app can import this file too.</p>'],
        ['What you gain', '<ul><li>A server list <b>synced</b> across PC, Android and iPhone through your account, end-to-end encrypted.</li><li>A <b>30-day trash</b> and backup history: if you delete a VM by mistake you can restore it.</li><li><b>Snippets</b> for frequent commands, <b>quick find</b> with Ctrl+K, quick connect without saving.</li><li>A CPU/RAM/network/disk bar right under the terminal, plus online status and latency per server.</li></ul>'],
        ['Good to know', '<p>WNTerm focuses on SSH and SFTP: only SSH/SFTP sessions are imported, other MobaXterm session types are not brought over. Please review the list after importing.</p>'],
    ];
    $faq = [
        ['Does importing remove my MobaXterm data?', 'No. Import only adds to the WNTerm list; your MobaXterm files and data stay untouched.'],
        ['Will I get duplicate servers?', 'When a server already exists, WNTerm asks whether to skip, overwrite or add a copy.'],
        ['Can I use it on my phone?', 'Yes. Sign in with the same account on Android or iPhone to see the same server list.'],
    ];
}
view('guide', compact('title', 'desc', 'h1', 'intro', 'sections', 'faq', 'related'));
