<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

// Trang quản trị: chỉ hiển thị khung; mọi dữ liệu lấy qua API /api/v1/admin/* và /relay/admin/* (server kiểm tra quyền).
$title = 'Quản trị — WNTerm';
$desc = 'Quản trị tài khoản và kết nối WNTerm.';
$active = 'account';
$noindex = true;
$scripts = ['wn-crypto.js', 'admin.js'];
view('head', compact('title', 'desc', 'active', 'noindex'));
?>
<main class="adm">
  <div id="loading" class="sub">Đang kiểm tra quyền…</div>
  <section id="main" hidden>
    <div class="adm-head">
      <h1>Quản trị</h1>
      <span class="sub" id="who"></span>
    </div>
    <div id="msg" class="alert ok" hidden></div>
    <div id="err" class="alert err" hidden></div>

    <div class="tabs" role="tablist">
      <button type="button" data-tab="users" class="on">Người dùng</button>
      <button type="button" data-tab="conns">Đang kết nối</button>
      <button type="button" data-tab="log">Nhật ký relay</button>
      <button type="button" data-tab="block">Danh sách chặn</button>
    </div>

    <!-- Người dùng -->
    <div class="tabp" id="tab-users">
      <form class="row" id="userSearch"><input id="q" type="search" placeholder="Tìm theo email…" autocomplete="off"><button class="btn" type="submit">Tìm</button><button class="btn" type="button" id="usersReload">Mới nhất</button></form>
      <div class="tblwrap"><table class="tbl" id="usersTbl"><thead><tr><th>Email</th><th>Tạo lúc</th><th>Đăng nhập cuối</th><th>Phiên</th><th>IP cuối</th><th></th></tr></thead><tbody></tbody></table></div>
      <div class="panel" id="detail" hidden></div>
    </div>

    <!-- Kết nối đang mở -->
    <div class="tabp" id="tab-conns" hidden>
      <p class="sub">Tự làm mới mỗi 5 giây. “Ngắt” đóng mọi kết nối của người dùng và chặn kết nối mới của họ trong 10 phút (không khóa tài khoản — muốn khóa hẳn hãy dùng tab Người dùng).</p>
      <div class="tblwrap"><table class="tbl" id="connsTbl"><thead><tr><th>Người dùng</th><th>IP</th><th>Đích</th><th>Thời lượng</th><th></th></tr></thead><tbody></tbody></table></div>
    </div>

    <!-- Nhật ký -->
    <div class="tabp" id="tab-log" hidden>
      <form class="row" id="logForm"><input id="logq" type="search" placeholder="Lọc (email, IP, host, abuse, admin…)" autocomplete="off"><button class="btn" type="submit">Lọc</button>
        <button class="btn" type="button" data-logq="abuse">abuse</button><button class="btn" type="button" data-logq="admin">admin</button><button class="btn" type="button" data-logq="open ">open</button></form>
      <pre class="logbox" id="logbox"></pre>
    </div>

    <!-- Danh sách chặn -->
    <div class="tabp" id="tab-block" hidden>
      <p class="sub">Đích bị chặn không ai kết nối được qua bản web. Nhập tên máy, <code>*.ten-mien.com</code>, IP hoặc CIDR. Có hiệu lực ngay.</p>
      <form class="row" id="blockForm"><input id="blockEntry" type="text" placeholder="vd. 203.0.113.7 hoặc *.victim.example" autocomplete="off" spellcheck="false"><button class="btn brand" type="submit">Chặn</button></form>
      <div id="blockList" class="blist"></div>
    </div>
  </section>
</main>
<?php view('foot', compact('scripts')); ?>
