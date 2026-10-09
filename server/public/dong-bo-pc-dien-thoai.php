<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$vi = wn_lang() === 'vi';
$related = [
    [$vi ? '/ssh-windows' : '/en/ssh-windows', $vi ? 'Phần mềm SSH & SFTP cho Windows' : 'SSH & SFTP client for Windows'],
    [$vi ? '/ssh-iphone' : '/en/ssh-iphone', $vi ? 'SSH trên iPhone không cần App Store' : 'SSH on iPhone without the App Store'],
    [$vi ? '/chuyen-tu-mobaxterm' : '/en/chuyen-tu-mobaxterm', $vi ? 'Chuyển từ MobaXterm sang WNTerm' : 'Move from MobaXterm to WNTerm'],
];

if ($vi) {
    $title = 'Đồng bộ danh sách máy chủ SSH từ PC sang điện thoại — WNTerm';
    $desc = 'Quản lý máy chủ SSH ở một nơi: tạo tài khoản WNTerm, bấm Backup ngay trên PC và đăng nhập trên Android/iPhone là có đúng danh sách. Mã hóa đầu-cuối.';
    $h1 = 'Đồng bộ danh sách máy chủ SSH từ PC sang điện thoại';
    $intro = 'Thêm máy chủ một lần trên máy tính, ra ngoài mở điện thoại là có đủ: WNTerm đồng bộ danh sách VM, nhóm, thẻ và snippets giữa Windows, Android và iPhone qua một tài khoản, mã hóa đầu-cuối.';
    $sections = [
        ['Cách đồng bộ từ PC sang điện thoại', '<ol><li><b>Tạo tài khoản</b> tại <a href="/dang-ky">trang đăng ký</a>. Lưu lại <b>mã khôi phục</b> được cấp — đây là cách duy nhất lấy lại dữ liệu nếu quên mật khẩu.</li><li>Trên PC, đăng nhập trong ứng dụng WNTerm rồi bấm <b>Backup ngay</b>. Ứng dụng cũng tự backup khi mở, sau mỗi thay đổi và định kỳ.</li><li>Trên điện thoại, cài <a href="/downloads/WNTerm-android-arm64.apk">bản Android</a> hoặc mở <a href="/app/">bản web trên iPhone</a>, đăng nhập cùng tài khoản.</li><li>Danh sách máy chủ xuất hiện đầy đủ; từ đó mọi thay đổi ở một thiết bị sẽ tự đến các thiết bị còn lại.</li></ol>'],
        ['Đồng bộ gồm những gì', '<ul><li>Danh sách máy chủ (VM): địa chỉ, cổng, người dùng, nhóm, thẻ, ghim.</li><li>Mật khẩu và khóa của VM — chỉ ở dạng đã mã hóa.</li><li>Snippets (lệnh lưu sẵn), dùng chung hoặc riêng từng VM.</li></ul>'],
        ['Sửa ở hai nơi có bị ghi đè nhầm không?', '<p>Không. Mỗi VM được đồng bộ riêng: bản sửa <b>sau cùng</b> thắng, còn các VM khác của thiết bị kia vẫn được giữ. Cùng một VM tạo riêng ở hai máy được nhận ra và gộp thành một, không bị nhân đôi.</p>'],
        ['Xóa nhầm vẫn lấy lại được', '<p>Xóa VM ở một máy, các máy còn lại chuyển nó vào <b>Thùng rác</b> (30 ngày). Khôi phục một lần là VM quay lại ở mọi nơi. Ngoài ra có <b>lịch sử backup 30 ngày</b>: chọn một bản cũ để xem nó có gì, thêm lại các VM còn thiếu hoặc đưa cả danh sách về đúng thời điểm đó (Cloud → Khôi phục từ backup).</p>'],
        ['Có an toàn không?', '<p>Mật khẩu tài khoản và khóa dữ liệu không bao giờ rời khỏi thiết bị. Dữ liệu được mã hóa ngay trên máy bạn trước khi tải lên; máy chủ WNTerm chỉ giữ các khối đã mã hóa và không có khóa để đọc. Nếu quên mật khẩu, dùng mã khôi phục tại <a href="/khoi-phuc">trang khôi phục</a>; mất cả mật khẩu lẫn mã khôi phục thì không ai lấy lại được dữ liệu.</p>'],
        ['Lưu ý khi dùng trên iPhone', '<p>Bản iPhone kết nối qua trạm trung chuyển trên internet nên chỉ tới được máy chủ có địa chỉ công khai; máy chủ trong mạng nội bộ hoặc sau VPN hãy dùng ứng dụng Windows hoặc Android. Danh sách máy chủ vẫn đồng bộ bình thường.</p>'],
    ];
    $faq = [
        ['Đồng bộ có mất phí không?', 'Hiện bạn dùng được toàn bộ tính năng, kể cả tài khoản đồng bộ. Mọi thay đổi về gói sẽ được thông báo trên trang chủ.'],
        ['Tôi không muốn tạo tài khoản, có chuyển được sang điện thoại không?', 'Có. Xuất danh sách ra file .wnterm (nút Export) rồi nhập file đó trên điện thoại. Cách này không tự cập nhật về sau như tài khoản.'],
        ['Quên mật khẩu tài khoản thì sao?', 'Dùng mã khôi phục nhận khi tạo tài khoản để đặt mật khẩu mới mà vẫn giữ dữ liệu. Mất cả hai thì không thể lấy lại.'],
        ['Dữ liệu của tôi lưu ở đâu?', 'Bản đã mã hóa lưu trên máy chủ WNTerm; khóa giải mã chỉ nằm trên thiết bị của bạn.'],
    ];
} else {
    $title = 'Sync your SSH server list from PC to phone — WNTerm';
    $desc = 'Manage SSH servers in one place: create a WNTerm account, hit Backup now on your PC and sign in on Android/iPhone to get the same list. End-to-end encrypted.';
    $h1 = 'Sync your SSH server list from PC to phone';
    $intro = 'Add a server once on your computer and have it on your phone when you are out: WNTerm syncs your VM list, groups, tags and snippets across Windows, Android and iPhone through one account, end-to-end encrypted.';
    $sections = [
        ['How to sync from PC to phone', '<ol><li><b>Create an account</b> on the <a href="/en/dang-ky">sign-up page</a>. Keep the <b>recovery code</b> you receive — it is the only way to get your data back if you forget your password.</li><li>On your PC, sign in inside the WNTerm app and click <b>Backup now</b>. The app also backs up on launch, after each change and periodically.</li><li>On your phone, install the <a href="/downloads/WNTerm-android-arm64.apk">Android app</a> or open the <a href="/app/">iPhone web app</a> and sign in with the same account.</li><li>Your full server list appears; from then on, changes on one device reach the others automatically.</li></ol>'],
        ['What gets synced', '<ul><li>Your server (VM) list: host, port, user, groups, tags, pins.</li><li>VM passwords and keys — only in encrypted form.</li><li>Snippets (saved commands), global or per VM.</li></ul>'],
        ['Will editing in two places overwrite things?', '<p>No. Each VM syncs on its own: the <b>latest</b> edit wins while the other VMs on the other device are kept. The same VM created separately on two machines is recognized and merged into one, not duplicated.</p>'],
        ['Accidental deletes can be undone', '<p>Delete a VM on one device and the others move it to the <b>Trash</b> (30 days). Restore it once and it comes back everywhere. There is also a <b>30-day backup history</b>: pick an older backup to see what it holds, add back missing VMs or roll the whole list back to that point (Cloud → Restore from backup).</p>'],
        ['Is it safe?', '<p>Your account password and data key never leave your device. Data is encrypted on your machine before upload; the WNTerm server only keeps encrypted blocks and has no key to read them. If you forget your password, use the recovery code on the <a href="/en/khoi-phuc">recovery page</a>; if you lose both, nobody can recover the data.</p>'],
        ['Using it on iPhone', '<p>The iPhone app connects through a relay on the internet, so it only reaches servers with a public address; for servers on a private network or behind a VPN use the Windows or Android app. Your server list still syncs normally.</p>'],
    ];
    $faq = [
        ['Does syncing cost anything?', 'Right now every feature is usable, including sync accounts. Any plan changes will be announced on the home page.'],
        ['I do not want an account — can I still move servers to my phone?', 'Yes. Export your list to a .wnterm file (Export button) and import it on your phone. It will not update automatically later like an account does.'],
        ['What if I forget my account password?', 'Use the recovery code you got at sign-up to set a new password and keep your data. If you lose both, it cannot be recovered.'],
        ['Where is my data stored?', 'The encrypted copy lives on the WNTerm server; the decryption key stays on your devices only.'],
    ];
}
view('guide', compact('title', 'desc', 'h1', 'intro', 'sections', 'faq', 'related'));
