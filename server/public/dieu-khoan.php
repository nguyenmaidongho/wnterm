<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('terms.title');
$desc = t('terms.desc');
$active = 'terms';
$b = brand();
$vi = wn_lang() === 'vi';
$mail = e($b['email']);
$co = e($b['company']);
view('head', compact('title', 'desc', 'active'));
?>
<main class="legal">
<?php if ($vi): ?>
  <h1>Điều khoản sử dụng &amp; chính sách chống lạm dụng</h1>
  <p class="ver">Phiên bản <?= e(Api::TOS_VERSION) ?> · Đơn vị cung cấp: <?= $co ?> (“WebNow”, “chúng tôi”)</p>

  <div class="box">Tóm tắt: WNTerm là công cụ để bạn quản trị <b>máy chủ của chính bạn</b> (hoặc máy chủ bạn được phép). Bạn chịu trách nhiệm về mọi hành vi của mình qua dịch vụ. Chúng tôi giới hạn, ghi nhật ký kết nối và có quyền khóa tài khoản khi phát hiện lạm dụng.</div>

  <h2>1. Phạm vi dịch vụ</h2>
  <p>Dịch vụ gồm ứng dụng WNTerm (Windows, Android…), tài khoản đồng bộ danh sách máy chủ được mã hóa đầu-cuối, và <b>bản web</b> (trình duyệt/iPhone). Khi bạn dùng bản web, phiên SSH chạy ngay trong trình duyệt của bạn; dữ liệu SSH đã mã hóa được chuyển qua <b>trạm chuyển tiếp</b> của WebNow tới máy chủ đích, vì trình duyệt không tự mở được kết nối SSH. Dùng bản web hay ứng dụng, bạn đều phải tuân thủ điều khoản này.</p>

  <h2>2. Chúng tôi chỉ là “đường ống”</h2>
  <p>Trạm chuyển tiếp chỉ sao chép các byte đã mã hóa giữa trình duyệt của bạn và máy chủ đích; chúng tôi <b>không đọc được</b> mật khẩu, lệnh hay dữ liệu trong phiên SSH. Chúng tôi không sở hữu, không điều hành và không kiểm soát các máy chủ đích mà bạn kết nối tới.</p>

  <h2>3. Quy tắc sử dụng được chấp nhận</h2>
  <p>Bạn chỉ được kết nối tới máy chủ <b>của bạn</b> hoặc máy chủ mà chủ sở hữu đã <b>cho phép rõ ràng</b>. Nghiêm cấm:</p>
  <ul>
    <li>truy cập trái phép, hoặc thử vượt qua xác thực/phân quyền của hệ thống không thuộc quyền của bạn;</li>
    <li>dò mật khẩu (brute-force, credential stuffing), thử hàng loạt tài khoản;</li>
    <li>quét cổng, dò tìm máy chủ hay lỗ hổng hàng loạt;</li>
    <li>tấn công từ chối dịch vụ, phát tán mã độc, đào tiền mã hóa trái phép, spam;</li>
    <li>dùng dịch vụ làm bàn đạp/ẩn danh hóa để thực hiện hành vi vi phạm pháp luật Việt Nam hoặc pháp luật của nơi đặt máy chủ đích;</li>
    <li>né tránh hay làm vô hiệu các giới hạn kỹ thuật bên dưới (tạo nhiều tài khoản, xoay IP…), bán lại hoặc chia sẻ tài khoản cho người khác lạm dụng.</li>
  </ul>

  <h2>4. Trách nhiệm của bạn</h2>
  <p>Bạn tự chịu trách nhiệm hoàn toàn về mọi hành vi thực hiện bằng tài khoản của mình, kể cả khi do người khác dùng tài khoản đó vì bạn không bảo mật nó. Bạn đồng ý bồi thường và giữ cho WebNow không bị thiệt hại từ khiếu nại, tổn thất hoặc chi phí phát sinh do hành vi vi phạm của bạn, trong phạm vi pháp luật cho phép.</p>

  <h2>5. Biện pháp kỹ thuật chống lạm dụng</h2>
  <p>Để bảo vệ dịch vụ và các bên thứ ba, chúng tôi áp dụng (có thể tự động và thay đổi theo thời gian): giới hạn số kết nối mới theo phút, theo tài khoản và theo địa chỉ IP; giới hạn số máy chủ khác nhau trong một giờ; <b>hạn mức thấp hơn cho tài khoản mới</b> trong 24 giờ đầu; tạm khóa kết nối tới một máy chủ khi có nhiều lần thất bại liên tiếp; danh sách chặn đích; chặn địa chỉ nội bộ; và yêu cầu đích phải là máy chủ SSH. Hạn mức này có thể làm một số trường hợp sử dụng hợp lệ bị chậm lại — khi đó hãy dùng ứng dụng máy tính (kết nối trực tiếp, không qua trạm của chúng tôi).</p>

  <h2>6. Nhật ký kết nối</h2>
  <p>Trạm chuyển tiếp ghi lại: email tài khoản, địa chỉ IP của bạn, thời điểm mở/đóng, máy chủ và cổng đích, thời lượng phiên, và các sự kiện bị giới hạn/chặn. <b>Không</b> ghi nội dung phiên SSH. Nhật ký được giữ khoảng <b>90 ngày</b> nhằm bảo mật, chống lạm dụng, xử lý khiếu nại, và <b>cung cấp cho cơ quan nhà nước có thẩm quyền khi có yêu cầu hợp pháp</b>. Khi đăng ký, chúng tôi cũng lưu phiên bản điều khoản bạn đồng ý, thời điểm và địa chỉ IP.</p>

  <h2>7. Báo cáo lạm dụng</h2>
  <p>Nếu bạn là chủ một máy chủ bị tấn công hoặc dò quét từ địa chỉ IP của chúng tôi, hãy gửi email tới <a href="mailto:<?= $mail ?>?subject=Abuse%20report%20WNTerm"><?= $mail ?></a> với tiêu đề “Abuse report WNTerm”, kèm: địa chỉ IP bị ảnh hưởng, thời điểm (ghi rõ múi giờ) và đoạn log liên quan. Chúng tôi sẽ xem xét trong vòng 1–2 ngày làm việc, chặn đích/khóa tài khoản vi phạm khi xác minh được, và phối hợp với cơ quan chức năng theo quy định pháp luật.</p>

  <h2>8. Đình chỉ và chấm dứt</h2>
  <p>Chúng tôi có quyền tạm khóa hoặc xóa tài khoản, thu hồi phiên đăng nhập và chặn truy cập <b>ngay lập tức, không cần báo trước</b> khi nghi ngờ có vi phạm điều khoản này hoặc theo yêu cầu của cơ quan có thẩm quyền.</p>

  <h2>9. Miễn trừ và giới hạn trách nhiệm</h2>
  <p>Dịch vụ được cung cấp “nguyên trạng”, không bảo đảm hoạt động liên tục hay không lỗi. Trong phạm vi pháp luật cho phép, WebNow không chịu trách nhiệm về hành vi của người dùng, nội dung/dữ liệu trên máy chủ đích, hay thiệt hại gián tiếp phát sinh từ việc dùng hoặc không dùng được dịch vụ; tổng trách nhiệm (nếu có) không vượt quá số tiền bạn đã trả cho dịch vụ trong 12 tháng gần nhất. Nếu bạn quên mật khẩu <b>và</b> mất mã khôi phục, dữ liệu đã mã hóa không thể lấy lại.</p>

  <h2>10. Thay đổi và luật áp dụng</h2>
  <p>Chúng tôi có thể cập nhật điều khoản này; bản mới có hiệu lực khi đăng trên trang này (kèm số phiên bản). Điều khoản được điều chỉnh bởi pháp luật Việt Nam. Liên hệ: <a href="mailto:<?= $mail ?>"><?= $mail ?></a>.</p>

<?php else: ?>
  <h1>Terms of Use &amp; abuse policy</h1>
  <p class="ver">Version <?= e(Api::TOS_VERSION) ?> · Provided by <?= $co ?> (“WebNow”, “we”)</p>

  <div class="box">In short: WNTerm is a tool for administering <b>your own servers</b> (or servers you are authorised to access). You are responsible for everything you do through the service. We rate-limit, log connections, and may suspend accounts when we detect abuse.</div>

  <h2>1. The service</h2>
  <p>The service consists of the WNTerm apps (Windows, Android…), an account that syncs your server list with end-to-end encryption, and the <b>web app</b> (browser/iPhone). In the web app the SSH session runs inside your browser; the encrypted SSH traffic is carried by a WebNow <b>relay</b> to the destination server, because browsers cannot open SSH connections themselves. These terms apply whether you use the web app or the apps.</p>

  <h2>2. We are only a pipe</h2>
  <p>The relay only copies encrypted bytes between your browser and the destination server; we <b>cannot read</b> passwords, commands or data in your SSH session. We do not own, operate or control the destination servers you connect to.</p>

  <h2>3. Acceptable use</h2>
  <p>You may only connect to servers that are <b>yours</b> or that the owner has <b>explicitly authorised</b> you to access. Strictly prohibited:</p>
  <ul>
    <li>unauthorised access, or attempting to bypass authentication/authorisation on systems you have no right to;</li>
    <li>password guessing (brute force, credential stuffing) or mass account testing;</li>
    <li>port scanning or mass probing for hosts or vulnerabilities;</li>
    <li>denial-of-service attacks, malware distribution, unauthorised crypto-mining, spam;</li>
    <li>using the service as a stepping stone or to anonymise activity that breaks the law of Vietnam or of the country where the destination server is located;</li>
    <li>evading or disabling the technical limits below (multiple accounts, IP rotation…), or reselling/sharing accounts for others to abuse.</li>
  </ul>

  <h2>4. Your responsibility</h2>
  <p>You are fully responsible for everything done with your account, including by others if you failed to keep it secure. To the extent permitted by law, you agree to indemnify and hold WebNow harmless from claims, losses and costs arising from your violations.</p>

  <h2>5. Technical anti-abuse measures</h2>
  <p>To protect the service and third parties we apply (automatically, and subject to change): limits on new connections per minute per account and per IP address; a limit on distinct servers per hour; <b>lower quotas for new accounts</b> during their first 24 hours; a temporary lock on a server after repeated consecutive failures; a destination blocklist; blocking of internal addresses; and a requirement that the destination is an SSH server. These limits may slow down some legitimate uses — in that case use the desktop app, which connects directly without our relay.</p>

  <h2>6. Connection logs</h2>
  <p>The relay records: account email, your IP address, open/close time, destination host and port, session duration, and rate-limit/block events. It does <b>not</b> record SSH session content. Logs are kept for about <b>90 days</b> for security, abuse handling, dispute resolution and to <b>provide to competent authorities upon a lawful request</b>. At registration we also store the terms version you accepted, the time and your IP address.</p>

  <h2>7. Reporting abuse</h2>
  <p>If you run a server that is being attacked or scanned from our IP address, email <a href="mailto:<?= $mail ?>?subject=Abuse%20report%20WNTerm"><?= $mail ?></a> with the subject “Abuse report WNTerm”, including: the affected IP address, the time (with time zone) and relevant log lines. We will review it within 1–2 business days, block the destination / suspend the offending account once verified, and cooperate with the authorities as required by law.</p>

  <h2>8. Suspension and termination</h2>
  <p>We may suspend or delete accounts, revoke sessions and block access <b>immediately and without notice</b> if we suspect a violation of these terms or when requested by a competent authority.</p>

  <h2>9. Disclaimer and limitation of liability</h2>
  <p>The service is provided “as is”, without any guarantee of uninterrupted or error-free operation. To the extent permitted by law, WebNow is not liable for users’ conduct, for content/data on destination servers, or for indirect damages arising from using or being unable to use the service; our total liability (if any) is limited to the amount you paid for the service in the previous 12 months. If you forget your password <b>and</b> lose your recovery code, your encrypted data cannot be recovered.</p>

  <h2>10. Changes and governing law</h2>
  <p>We may update these terms; a new version takes effect when published on this page (with its version number). These terms are governed by the laws of Vietnam. Contact: <a href="mailto:<?= $mail ?>"><?= $mail ?></a>.</p>
<?php endif; ?>
</main>
<?php view('foot', []); ?>
