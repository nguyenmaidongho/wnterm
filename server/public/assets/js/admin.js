(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };

  // Tạo phần tử an toàn (mọi nội dung đi qua textContent — dữ liệu từ người dùng/log không bao giờ thành HTML).
  function el(tag, props, kids) {
    var e = document.createElement(tag);
    Object.keys(props || {}).forEach(function (k) {
      if (k === 'text') e.textContent = props[k]; else if (k === 'class') e.className = props[k]; else if (k === 'onclick') e.onclick = props[k]; else e.setAttribute(k, props[k]);
    });
    (kids || []).forEach(function (c) { if (c) e.appendChild(c); });
    return e;
  }
  function fmt(iso) { if (!iso) return '—'; try { return new Date(iso).toLocaleString('vi-VN'); } catch (e) { return iso; } }
  function dur(s) { var h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60); return h ? h + 'g ' + m + 'p' : m ? m + 'p ' + (s % 60) + 's' : s + 's'; }
  function msg(t, bad) {
    $('msg').hidden = bad || !t; $('err').hidden = !bad || !t;
    (bad ? $('err') : $('msg')).textContent = t || '';
    if (t) window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  var token = WN.getToken();
  async function relay(method, path, body) {
    var res = await fetch('/relay/admin/' + path, { method: method, headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    var d = {}; try { d = await res.json(); } catch (e) { d = { ok: false, error: 'Relay không phản hồi hợp lệ.' }; }
    return d;
  }
  var api = function (m, p, b) { return WN.api(m, p, b, true); };

  // ===== Người dùng =====
  async function loadUsers(q) {
    var r = await api('GET', '/admin/users' + (q ? '?q=' + encodeURIComponent(q) : ''));
    if (!r.ok) return msg(r.message || 'Không tải được danh sách.', true);
    var tb = $('usersTbl').tBodies[0]; tb.innerHTML = '';
    r.users.forEach(function (u) {
      var name = el('td', {}, [el('b', { text: u.email })]);
      if (u.admin) name.appendChild(el('span', { class: 'tag ok', text: 'ADMIN' }));
      if (u.disabled) name.appendChild(el('span', { class: 'tag bad', text: 'ĐÃ KHÓA' }));
      var acts = el('td', { class: 'acts' }, [el('button', { class: 'linkbtn', text: 'Chi tiết', onclick: function () { showDetail(u.email); } })]);
      if (!u.admin) {
        acts.appendChild(el('button', { class: 'linkbtn ' + (u.disabled ? '' : 'danger'), text: u.disabled ? 'Mở khóa' : 'Khóa & ngắt', onclick: function () { setDisabled(u.email, !u.disabled); } }));
      }
      tb.appendChild(el('tr', {}, [name, el('td', { text: fmt(u.createdAt) }), el('td', { text: fmt(u.lastLogin) }), el('td', { text: String(u.sessions) }), el('td', { text: u.lastIp || '—' }), acts]));
    });
    if (!r.users.length) tb.appendChild(el('tr', {}, [el('td', { colspan: '6', text: 'Không có kết quả.' })]));
  }

  async function setDisabled(email, disable) {
    if (disable && !confirm('Khóa tài khoản ' + email + ' và ngắt mọi kết nối của họ?')) return;
    var r = await api('POST', disable ? '/admin/disable' : '/admin/enable', { email: email });
    if (!r.ok) return msg(r.message || 'Không thực hiện được.', true);
    var extra = '';
    if (disable) { var k = await relay('POST', 'kick', { email: email }); extra = k.ok ? ' Đã ngắt ' + k.kicked + ' kết nối đang mở.' : ' (Không ngắt được kết nối đang mở: ' + (k.error || 'lỗi relay') + ')'; }
    msg((disable ? 'Đã khóa ' : 'Đã mở khóa ') + email + '.' + extra);
    loadUsers($('q').value.trim());
  }

  async function showDetail(email) {
    var r = await api('GET', '/admin/user?email=' + encodeURIComponent(email));
    if (!r.ok) return msg(r.message || 'Không tải được chi tiết.', true);
    var box = $('detail'); box.innerHTML = ''; box.hidden = false;
    box.appendChild(el('h3', { text: r.email }));
    box.appendChild(el('p', { class: 'sub', text: 'Tạo: ' + fmt(r.createdAt) + ' · Đăng nhập cuối: ' + fmt(r.lastLogin) + ' · ' + (r.disabled ? 'ĐÃ KHÓA' : 'hoạt động') }));
    box.appendChild(el('h4', { text: 'Phiên đăng nhập (' + r.sessions.length + ')' }));
    r.sessions.forEach(function (s) { box.appendChild(el('div', { class: 'dev', text: s.device + ' · ' + s.ip + ' · tạo ' + fmt(s.createdAt) + ' · dùng cuối ' + fmt(s.lastUsedAt) })); });
    box.appendChild(el('h4', { text: 'Đồng ý điều khoản' }));
    if (!r.tos.length) box.appendChild(el('div', { class: 'dev', text: 'Chưa có bản ghi (tài khoản tạo trước khi có điều khoản).' }));
    r.tos.forEach(function (t) { box.appendChild(el('div', { class: 'dev', text: 'Phiên bản ' + t.version + ' · ' + fmt(t.at) + ' · IP ' + t.ip })); });
    var lg = el('button', { class: 'btn', text: 'Xem log relay của người này', onclick: function () { $('logq').value = r.email; tab('log'); loadLog(); } });
    box.appendChild(el('div', { class: 'row' }, [lg]));
    box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  // ===== Kết nối đang mở =====
  var connTimer = null;
  async function loadConns() {
    var r = await relay('GET', 'conns');
    var tb = $('connsTbl').tBodies[0]; tb.innerHTML = '';
    if (!r.ok) { tb.appendChild(el('tr', {}, [el('td', { colspan: '5', text: r.error || 'Relay không phản hồi (chưa cập nhật bản có trang quản trị?).' })])); return; }
    r.conns.forEach(function (c) {
      var target = c.host + ':' + c.port;
      tb.appendChild(el('tr', {}, [el('td', { text: c.email }), el('td', { text: c.ip }), el('td', { text: target }), el('td', { text: dur(c.seconds) }),
        el('td', { class: 'acts' }, [
          el('button', { class: 'linkbtn danger', text: 'Ngắt', onclick: async function () { var k = await relay('POST', 'kick', { email: c.email }); msg(k.ok ? 'Đã ngắt ' + k.kicked + ' kết nối của ' + c.email + '.' : (k.error || 'Lỗi'), !k.ok); loadConns(); } }),
          el('button', { class: 'linkbtn', text: 'Chặn đích', onclick: function () { addBlock(c.host); } })
        ])]));
    });
    if (!r.conns.length) tb.appendChild(el('tr', {}, [el('td', { colspan: '5', text: 'Không có kết nối nào đang mở.' })]));
  }

  // ===== Nhật ký =====
  async function loadLog() {
    var q = $('logq').value.trim();
    var r = await relay('GET', 'log?n=300' + (q ? '&q=' + encodeURIComponent(q) : ''));
    $('logbox').textContent = r.ok ? (r.lines.length ? r.lines.join('\n') : '(không có dòng nào khớp)') : (r.error || 'Relay không phản hồi.');
    $('logbox').scrollTop = $('logbox').scrollHeight;
  }

  // ===== Danh sách chặn =====
  async function loadBlock() {
    var r = await relay('GET', 'blocklist'); var box = $('blockList'); box.innerHTML = '';
    if (!r.ok) { box.appendChild(el('div', { class: 'dev', text: r.error || 'Relay không phản hồi.' })); return; }
    r.entries.forEach(function (e) { box.appendChild(el('div', { class: 'dev' }, [el('code', { text: e }), el('button', { class: 'linkbtn', text: 'Bỏ chặn', onclick: async function () { var x = await relay('POST', 'unblock', { entry: e }); msg(x.ok ? 'Đã bỏ chặn ' + e : x.error, !x.ok); loadBlock(); } })])); });
    if (!r.entries.length) box.appendChild(el('div', { class: 'dev', text: 'Chưa có mục nào do quản trị thêm.' }));
  }
  async function addBlock(entry) {
    var x = await relay('POST', 'block', { entry: entry });
    msg(x.ok ? 'Đã chặn ' + entry : (x.error || 'Lỗi'), !x.ok);
    if (x.ok) loadBlock();
  }

  // ===== Tabs =====
  function tab(name) {
    document.querySelectorAll('.tabs button').forEach(function (b) { b.classList.toggle('on', b.getAttribute('data-tab') === name); });
    ['users', 'conns', 'log', 'block'].forEach(function (n) { $('tab-' + n).hidden = n !== name; });
    clearInterval(connTimer);
    if (name === 'conns') { loadConns(); connTimer = setInterval(loadConns, 5000); }
    if (name === 'log') loadLog();
    if (name === 'block') loadBlock();
  }

  async function init() {
    if (!token) return location.replace('/dang-nhap');
    var me = await api('GET', '/me');
    if (!me.ok) { WN.setToken(null); return location.replace('/dang-nhap'); }
    if (!me.admin) { $('loading').textContent = 'Tài khoản này không có quyền quản trị.'; return; }
    $('who').textContent = me.email;
    $('loading').hidden = true; $('main').hidden = false;

    document.querySelectorAll('.tabs button').forEach(function (b) { b.onclick = function () { tab(b.getAttribute('data-tab')); }; });
    $('userSearch').onsubmit = function (e) { e.preventDefault(); loadUsers($('q').value.trim()); };
    $('usersReload').onclick = function () { $('q').value = ''; loadUsers(''); };
    $('logForm').onsubmit = function (e) { e.preventDefault(); loadLog(); };
    document.querySelectorAll('[data-logq]').forEach(function (b) { b.onclick = function () { $('logq').value = b.getAttribute('data-logq').trim(); loadLog(); }; });
    $('blockForm').onsubmit = function (e) { e.preventDefault(); var v = $('blockEntry').value.trim(); if (v) { addBlock(v); $('blockEntry').value = ''; } };
    loadUsers('');
  }
  init();
})();
