/*
 * WN Term — bản web (PWA) cho iPhone / trình duyệt. Bố cục giống bản Android (Sessions / Terminal / SFTP).
 * - Đăng nhập tài khoản WN Term (mã hóa đầu-cuối, dùng chung giao thức với app — xem wn-crypto.js).
 * - Danh sách VM nằm trong kho cục bộ (vault.js) và tự đồng bộ với bản backup trên máy chủ: thêm/sửa/xóa, thùng rác, lịch sử.
 * - SSH/SFTP chạy trong trình duyệt (wnssh.wasm), byte SSH đã mã hóa đi qua trạm chuyển tiếp /relay.
 */
(function () {
  'use strict';

  var $ = UI.$, esc = UI.esc, ico = UI.ico, store = UI.store, toast = UI.toast, modal = UI.modal;
  var API = '/api/v1';
  var RELAY = store.get('relay', null) || ((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/relay');
  var td = new TextDecoder(), te = new TextEncoder();
  var VERSION = '__VERSION__';
  var L = WNi18n.t, LT = WNi18n.tr;

  // ===== Cài đặt =====
  var DEFAULTS = { theme: 'dark', fontSize: 0, scrollback: 5000, confirmPaste: true, showHidden: true, autoSync: true, lang: '' };
  var settings = Object.assign({}, DEFAULTS, store.get('settings', {}));
  function saveSettings() { store.set('settings', settings); }
  function defaultFont() { return window.innerWidth < 500 ? 13 : 14; }
  function termFont() { return settings.fontSize || defaultFont(); }

  function applyTheme() {
    document.documentElement.dataset.theme = settings.theme === 'light' ? 'light' : 'dark';
    var m = document.querySelector('meta[name=theme-color]'); if (m) m.content = '#252526';
  }
  applyTheme();
  UI.hydrate();

  // ===== Trạng thái =====
  var auth = store.get('auth', null);       // { email, token, vaultKey(b64) }
  var vault = null;
  var tabs = [], active = null;
  var selectedId = null, activeTags = [];
  var grouped = store.get('grouped', true), collapsed = store.get('collapsed', {});
  var view = 'vSessions';

  // ===== Giao diện chung =====
  function screen(id) { $('login').hidden = id !== 'login'; $('app').hidden = id !== 'app'; }

  function setView(id) {
    view = id;
    ['vSessions', 'vTerminal', 'vSftp'].forEach(function (v) { $(v).hidden = v !== id; });
    document.querySelectorAll('.bottomnav button').forEach(function (b) { b.classList.toggle('active', b.dataset.view === id); });
    if (id === 'vTerminal') { setTimeout(fitActive, 30); monitorKick(); }
    if (id === 'vSftp') sftpShow();
    if (id === 'vSessions') probeNow(false);
    updateStatus();
  }

  var statusTimer;
  function setStatus(text, ttl) {
    $('status').textContent = text;
    clearTimeout(statusTimer);
    if (ttl) statusTimer = setTimeout(updateStatus, ttl);
  }
  function updateStatus() {
    if (view !== 'vSessions' && active) setStatus(L('Đang xem tab: {0} ({1}@{2})', active.vm.name || active.vm.host, active.vm.username, active.vm.host));
    else if (vault && vault.state.lastSync) setStatus(L('Backup gần nhất: {0}', fmtTime(vault.state.lastSync)) + (vault.state.dirty ? L(' · có thay đổi chưa backup') : ''));
    else setStatus(tabs.length ? L('{0} phiên đang mở', tabs.length) : L('Sẵn sàng'));
  }
  function fmtTime(isoStr) {
    var d = new Date(WNVault.parseTime(isoStr)), p = function (n) { return ('0' + n).slice(-2); };
    return p(d.getHours()) + ':' + p(d.getMinutes()) + ' ' + p(d.getDate()) + '/' + p(d.getMonth() + 1) + '/' + d.getFullYear();
  }
  function b64utf8(s) { var u = te.encode(s), r = ''; for (var i = 0; i < u.length; i += 0x8000) r += String.fromCharCode.apply(null, u.subarray(i, i + 0x8000)); return btoa(r); }
  function parseTags(s) {
    var out = []; String(s || '').split(/[,;\n]/).forEach(function (t) { t = t.trim(); if (t && !out.some(function (x) { return x.toLowerCase() === t.toLowerCase(); })) out.push(t); });
    return out;
  }

  /** Nhấn giữ trên các phần tử khớp `selector` trong `container` → cb(phần_tử). Cú chạm thường vẫn hoạt động. */
  function longPress(container, selector, cb) {
    var timer = null, fired = false, sx = 0, sy = 0;
    container.addEventListener('pointerdown', function (ev) {
      var t = ev.target.closest(selector); if (!t) return;
      fired = false; sx = ev.clientX; sy = ev.clientY;
      timer = setTimeout(function () { fired = true; timer = null; if (navigator.vibrate) try { navigator.vibrate(12); } catch (e) {} cb(t); }, 520);
    });
    container.addEventListener('pointermove', function (ev) { if (timer && (Math.abs(ev.clientX - sx) > 8 || Math.abs(ev.clientY - sy) > 8)) { clearTimeout(timer); timer = null; } });
    ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (n) { container.addEventListener(n, function () { clearTimeout(timer); timer = null; }); });
    container.addEventListener('click', function (ev) { if (fired) { fired = false; ev.stopPropagation(); ev.preventDefault(); } }, true);
    container.addEventListener('contextmenu', function (ev) { if (ev.target.closest(selector)) ev.preventDefault(); });
  }

  // ===== API =====
  async function api(method, path, body) {
    var headers = { 'Content-Type': 'application/json' };
    if (auth && auth.token) headers.Authorization = 'Bearer ' + auth.token;
    var res = await fetch(API + path, { method: method, headers: headers, body: body ? JSON.stringify(body) : undefined, cache: 'no-store' });
    var data = {};
    try { data = await res.json(); } catch (e) { data = { ok: false, message: L('Máy chủ phản hồi không hợp lệ ({0}).', res.status) }; }
    data._status = res.status;
    return data;
  }

  function deviceName() {
    var ua = navigator.userAgent;
    if (/iPhone/.test(ua)) return 'iPhone (web)';
    if (/iPad|Macintosh.*Mobile/.test(ua)) return 'iPad (web)';
    if (/Android/.test(ua)) return 'Android (web)';
    return 'Trình duyệt web';
  }

  // ===== Đăng nhập / đăng xuất =====
  $('loginForm').addEventListener('submit', async function (ev) {
    ev.preventDefault();
    var err = $('loginErr'), btn = $('loginBtn'), label = btn.lastElementChild;
    err.hidden = true;
    var email = $('email').value.trim().toLowerCase(), pw = $('password').value;
    if (!WN.available) { err.textContent = L('Trình duyệt không hỗ trợ mã hóa an toàn (cần HTTPS).'); err.hidden = false; return; }
    btn.disabled = true; label.textContent = L('Đang đăng nhập…');
    try {
      auth = null;
      var pre = await api('POST', '/prelogin', { email: email });
      if (!pre.ok) throw new Error(LT(pre.message) || L('Không kết nối được máy chủ.'));
      var k = await WN.derive(pw, email, pre.kdfIterations);
      var res = await api('POST', '/login', { email: email, authKey: WN.b64(k.auth), deviceName: deviceName() });
      if (!res.ok) throw new Error(LT(res.message) || L('Đăng nhập không thành công.'));
      var vaultKey = await WN.open(k.enc, res.wrappedPw);
      if (!vaultKey) throw new Error(L('Không mở được khóa dữ liệu của tài khoản.'));
      auth = { email: email, token: res.token, vaultKey: WN.b64(vaultKey) };
      store.set('auth', auth);
      $('password').value = '';
      await startSession(true);
    } catch (e) {
      err.textContent = LT(e.message || String(e)); err.hidden = false;
    } finally {
      btn.disabled = false; label.textContent = L('Đăng nhập');
    }
  });

  async function logout(skipConfirm) {
    if (!skipConfirm) {
      var dirty = vault && vault.state.dirty;
      var ok = await UI.confirm(L('Đăng xuất?'), L('<p>Các phiên đang mở sẽ bị đóng và danh sách VM trên thiết bị này sẽ được xóa (vẫn còn trong bản backup của bạn).</p>') +
        (dirty ? L('<p class="error">Có thay đổi chưa backup — đang backup trước khi đăng xuất…</p>') : ''), L('Đăng xuất'), true);
      if (!ok) return;
      if (dirty) await syncNow(true);
    }
    tabs.slice().forEach(function (t) { closeTab(t, true); });
    try { await api('POST', '/logout'); } catch (e) {}
    if (vault) vault.clear();
    vault = null; auth = null; probeState = {}; probeNext = 0;
    store.del('auth'); store.del('state'); store.del('vault');
    clearTimeout(syncTimer);
    screen('login');
  }

  async function sessionExpired() {
    await modal({ title: L('Phiên đăng nhập hết hạn'), html: L('<p>Hãy đăng nhập lại. Các thay đổi chưa backup vẫn được giữ trên máy này và sẽ đồng bộ sau khi đăng nhập lại cùng tài khoản.</p>') });
    tabs.slice().forEach(function (t) { closeTab(t, true); });
    auth = null; vault = null; store.del('auth'); clearTimeout(syncTimer);
    screen('login');
  }

  // ===== Kho VM + đồng bộ =====
  function makeVault() {
    vault = new WNVault.Vault({
      api: api, WN: WN, key: WN.unb64(auth.vaultKey), device: deviceName(),
      persist: function (b) { if (b == null) store.del('state'); else store.set('state', { email: auth.email, blob: b }); },
      restore: function () { var st = store.get('state', null); return st && st.email === auth.email ? st.blob : null; }
    });
    vault.onChange = function () { renderList(); updateBadge(); };
    vault.onUserChange = function () { if (settings.autoSync) scheduleSync(5000); };
  }

  var syncing = false, syncTimer = null, syncErr = null;
  function scheduleSync(ms) { clearTimeout(syncTimer); syncTimer = setTimeout(function () { syncNow(true); }, ms || 5000); }
  function updateBadge() {
    var b = $('cloudBadge'); if (!vault) return;
    b.hidden = !(vault.state.dirty || syncErr); b.classList.toggle('err', !!syncErr);
  }
  /** Đồng bộ (backup + nhận thay đổi từ máy khác). silent=true: không báo khi không có gì mới. */
  async function syncNow(silent) {
    if (!vault || syncing) return null;
    syncing = true; updateBadge();
    if (!silent) setStatus(L('Đang backup…'));
    try {
      var r = await vault.sync();
      syncErr = null;
      var got = r.added + r.updated + r.deleted;
      if (!silent || got) setStatus(L('Đã backup {0} VM', r.total) + (got ? L(' · nhận từ máy khác: {0} mới, {1} cập nhật, {2} vào thùng rác', r.added, r.updated, r.deleted) : ''), 5000);
      else updateStatus();
      renderList();
      return r;
    } catch (e) {
      syncErr = e;
      if (e.status === 401) { syncing = false; await sessionExpired(); return null; }
      if (!silent) setStatus(L('Backup lỗi: {0}', LT(e.message)), 8000); else setStatus(L('Chưa backup được ({0}) — sẽ thử lại', LT(e.message)), 6000);
      return null;
    } finally { syncing = false; updateBadge(); }
  }

  async function startSession(fresh) {
    makeVault();
    await vault.load();
    screen('app');
    setView('vSessions');
    renderList(); updateBadge();
    ensureWasm().catch(function () {});     // tải sẵn bộ SSH trong nền
    var r = await syncNow(false);
    if (fresh && r && r.total === 0) toast(L('Chưa có VM nào trong backup — bấm ＋ để thêm.'), 4000);
  }

  setInterval(function () { if (vault && settings.autoSync && !document.hidden) syncNow(true); }, 10 * 60 * 1000);
  document.addEventListener('visibilitychange', function () {
    if (!document.hidden && vault && settings.autoSync) {
      var last = vault.state.lastSync ? WNVault.parseTime(vault.state.lastSync) : 0;
      if (Date.now() - last > 2 * 60 * 1000) syncNow(true);
    }
  });

  // ===== Danh sách VM =====
  function sessionsList() { return vault ? vault.list() : []; }
  function isLive(vm) { return tabs.some(function (t) { return t.vm.id === vm.id && t.state === 'connected'; }); }
  function isOpen(vm) { return tabs.some(function (t) { return t.vm.id === vm.id; }); }
  function groupLabel(g) { return g === '★ Ghim' ? L('★ Ghim') : g === 'Chưa có tag' ? L('Chưa có tag') : g; }
  function groupKey(s) { return s.pinned ? '★ Ghim' : ((s.tags && s.tags[0]) || 'Chưa có tag'); }

  function renderChips() {
    var counts = vault ? vault.tags() : {};
    var tags = Object.keys(counts).sort(function (a, b) { return a.localeCompare(b, 'vi'); });
    activeTags = activeTags.filter(function (t) { return counts[t]; });
    var html = '<button class="chip' + (activeTags.length ? '' : ' on') + '" data-tag="">' + L('Tất cả') + '</button>';
    tags.forEach(function (t) { html += '<button class="chip' + (activeTags.indexOf(t) >= 0 ? ' on' : '') + '" data-tag="' + esc(t) + '">' + esc(t) + ' ' + counts[t] + '</button>'; });
    $('chips').innerHTML = html;
    $('chipGroup').classList.toggle('on', grouped);
  }

  function renderList() {
    if (!vault) return;
    renderChips();
    var all = sessionsList();
    var q = $('search').value.trim().toLowerCase();
    var list = all.filter(function (s) {
      if (activeTags.length && !s.tags.some(function (t) { return activeTags.indexOf(t) >= 0; })) return false;
      if (!q) return true;
      if (q[0] === '#') return s.tags.some(function (t) { return t.toLowerCase().indexOf(q.slice(1)) >= 0; });
      return [s.name, s.host, s.username].concat(s.tags).some(function (v) { return v && String(v).toLowerCase().indexOf(q) >= 0; });
    });
    var byName = function (a, b) { return (a.name || a.host).localeCompare(b.name || b.host, 'vi'); };
    var row = function (s) {
      var live = isLive(s), pr = probeOf(s), up = live || !!(pr && pr.ok);
      return '<div class="vm' + (live ? ' live' : '') + (s.id === selectedId ? ' sel' : '') + '" data-id="' + esc(s.id) + '">' +
        '<div class="vm-ico">' + ico('monitor') + '<span class="dot' + (up ? ' up' : '') + '" title="' + esc(pr ? (pr.ok ? L('Đang bật') + ' · ' + pr.ms + ' ms' : L('Không phản hồi')) : '') + '"></span></div>' +
        '<div class="vm-main"><div class="vm-name">' + (s.pinned && !grouped ? L('<span class="pin">★</span>') : '') + esc(s.name || (s.username + '@' + s.host)) + '</div>' +
        '<div class="vm-sub">' + esc(s.username + '@' + s.host + (s.port !== 22 ? ':' + s.port : '')) + (pr && pr.ok ? '<span class="lat' + (pr.ms >= 400 ? ' slow' : '') + '"> · ' + pr.ms + ' ms</span>' : '') + '</div></div>' +
        '<div class="vm-right">' + (live ? L('đang mở') : (isOpen(s) ? L('đang kết nối') : '')) + '</div></div>';
    };
    var html = '';
    if (grouped) {
      var groups = {};
      list.forEach(function (s) { (groups[groupKey(s)] = groups[groupKey(s)] || []).push(s); });
      Object.keys(groups).sort(function (a, b) {
        if (a === '★ Ghim') return -1; if (b === '★ Ghim') return 1;
        if (a === 'Chưa có tag') return 1; if (b === 'Chưa có tag') return -1;
        return a.localeCompare(b, 'vi');
      }).forEach(function (g) {
        var items = groups[g].sort(byName), c = !!collapsed[g], live = items.filter(isLive).length;
        html += '<div class="group' + (c ? ' collapsed' : '') + '" data-group="' + esc(g) + '">' + ico('chevron') +
          '<span class="gname">' + esc(groupLabel(g)) + '</span><span>' + (live ? L('{0}/{1} đang mở', live, items.length) : L('{0} VM', items.length)) + '</span></div>';
        if (!c) items.forEach(function (s) { html += row(s); });
      });
    } else {
      list.sort(function (a, b) { return (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0) || byName(a, b); }).forEach(function (s) { html += row(s); });
    }
    $('vmList').innerHTML = html;
    $('emptyState').hidden = all.length > 0;
    $('vmList').hidden = all.length === 0;
    var liveCount = all.filter(isLive).length, trash = vault.trashItems().length;
    var sum = $('summary');
    sum.innerHTML = '<span class="grow">' + L('{0} VM', list.length) + (list.length !== all.length ? L(' (lọc từ {0})', all.length) : '') + (liveCount ? L(' · {0} đang mở', liveCount) : '') + '</span>' +
      (trash ? '<button id="sumTrash">' + ico('trash') + ' ' + trash + '</button>' : '');
    if (view === 'vSessions') updateStatus();
  }

  $('search').addEventListener('input', renderList);
  $('chips').addEventListener('click', function (ev) {
    var c = ev.target.closest('.chip'); if (!c) return;
    var t = c.dataset.tag;
    if (!t) activeTags = []; else { var i = activeTags.indexOf(t); if (i >= 0) activeTags.splice(i, 1); else activeTags.push(t); }
    renderList();
  });
  $('chipGroup').addEventListener('click', function () { grouped = !grouped; store.set('grouped', grouped); renderList(); });
  $('summary').addEventListener('click', function (ev) { if (ev.target.closest('#sumTrash')) trashDialog(); });

  // ===== Chấm xanh/xám + độ trễ: trình duyệt không thử TCP được, nên nhờ trạm chuyển tiếp (POST /relay/probe, chỉ kết nối, không gửi dữ liệu) =====
  var probeState = {}, probeBusy = false, probeNext = 0;
  function probeKey(s) { return String(s.host || '').trim().toLowerCase() + ':' + (s.port || 22); }
  function probeOf(s) { var p = probeState[probeKey(s)]; return p && Date.now() - p.at < 5 * 60 * 1000 ? p : null; }
  function probeUrl() { return RELAY.replace(/^ws/i, 'http').replace(/\/+$/, '') + '/probe'; }
  async function probeNow(manual) {
    if (!vault || !auth || probeBusy) return;
    if (!manual && (document.hidden || view !== 'vSessions')) return;
    var now = Date.now();
    if (now < probeNext) { if (manual) toast(L('Hãy đợi {0} giây rồi kiểm tra lại.', Math.ceil((probeNext - now) / 1000))); return; }
    var seen = {}, list = [];
    sessionsList().forEach(function (s) { var k = probeKey(s); if (s.host && !seen[k]) { seen[k] = 1; list.push({ k: k, host: String(s.host).trim(), port: s.port || 22 }); } });
    if (!list.length) return;
    list.sort(function (a, b) { return ((probeState[a.k] || {}).at || 0) - ((probeState[b.k] || {}).at || 0); });   // quá 40 VM: lần sau thử nhóm cũ nhất
    list = list.slice(0, 40);
    probeBusy = true; probeNext = now + 20000;
    var btn = $('sumProbe'); if (btn) btn.classList.add('busy');
    try {
      var res = await fetch(probeUrl(), { method: 'POST', headers: { 'Content-Type': 'application/json' }, cache: 'no-store',
        body: JSON.stringify({ token: auth.token, targets: list.map(function (x) { return { host: x.host, port: x.port }; }) }) });
      var data = {}; try { data = await res.json(); } catch (e) {}
      if (res.status === 429 && data.retryAfter) probeNext = Date.now() + data.retryAfter * 1000;
      if (data.ok && data.results) {
        var at = Date.now();
        data.results.forEach(function (r) { probeState[String(r.host).trim().toLowerCase() + ':' + r.port] = { ok: !!r.ok, ms: r.ms, at: at }; });
        renderList();
        if (manual) toast(L('Đã kiểm tra {0} VM.', data.results.length));
      } else if (manual) toast(L('Không kiểm tra được: {0}', LT(data.error || ('HTTP ' + res.status))), 4000);
    } catch (e) { if (manual) toast(L('Không kiểm tra được: {0}', LT(e.message || String(e))), 4000); }
    finally { probeBusy = false; var b2 = $('sumProbe'); if (b2) b2.classList.remove('busy'); }
  }
  setInterval(function () { probeNow(false); }, 60 * 1000);
  document.addEventListener('visibilitychange', function () { if (!document.hidden) probeNow(false); });

  longPress($('vmList'), '.vm, .group', function (el) { if (el.classList.contains('group')) groupMenu(el.dataset.group); else vmMenu(el.dataset.id); });
  $('vmList').addEventListener('click', function (ev) {
    var g = ev.target.closest('.group');
    if (g) { var n = g.dataset.group; collapsed[n] = !collapsed[n]; store.set('collapsed', collapsed); return renderList(); }
    var r = ev.target.closest('.vm'); if (!r) return;
    var vm = vault.find(r.dataset.id);
    if (vm) { selectedId = vm.id; openTab(vm); }   // giống Android: chạm một lần là kết nối
  });

  async function openMany(list) {
    if (!list.length) return;
    if (list.length >= 10 && !(await UI.confirm(L('Mở nhiều VM'), L('<p>Bạn sắp mở <b>{0}</b> kết nối cùng lúc. Tiếp tục?</p>', list.length), L('Mở'), false))) return;
    for (var i = 0; i < list.length; i++) { openTab(list[i], false, i > 0); await new Promise(function (r) { setTimeout(r, 250); }); }
  }

  async function vmMenu(id) {
    var vm = vault.find(id); if (!vm) return;
    selectedId = id; renderList();
    var menu = [
      { text: L('Kết nối'), value: 'connect', icon: 'play' },
      { text: L('Kết nối và mở SFTP'), value: 'sftp', icon: 'folder' },
      { text: L('Sửa VM'), value: 'edit', icon: 'edit' },
      { text: vm.pinned ? L('Bỏ ghim') : L('Ghim lên đầu'), value: 'pin', icon: 'pin' },
      { text: L('Nhân bản'), value: 'dup', icon: 'copy' }
    ];
    if (vm.tags.length) menu.push({ text: L('Mở tất cả VM cùng tag "{0}"', vm.tags[0]), value: 'tag', icon: 'tag' });
    menu.push({ text: L('Xuất VM này ra file…'), value: 'export', icon: 'export' }, { text: L('Sao chép host'), value: 'copy', icon: 'link' }, { text: L('Xóa (vào thùng rác)'), value: 'del', icon: 'trash', cls: 'danger' });
    var r = await modal({ title: vm.name || vm.host, html: '<p class="muted small">' + esc(vm.username + '@' + vm.host + ':' + vm.port) + (vm.tags.length ? ' · ' + esc(vm.tags.join(', ')) : '') + '</p>', menu: menu });
    switch (r.value) {
      case 'connect': openTab(vm); break;
      case 'sftp': openTab(vm, true); break;
      case 'edit': editVm(vm); break;
      case 'pin': vault.update(vm.id, { pinned: !vm.pinned }); break;
      case 'dup': var d = vault.duplicate(vm.id); if (d) toast(L('Đã nhân bản: {0}', d.name || d.host)); break;
      case 'tag': openMany(sessionsList().filter(function (s) { return s.tags.indexOf(vm.tags[0]) >= 0; })); break;
      case 'export': exportDialog([vm.id]); break;
      case 'copy': {
        var cr = await modal({
          title: L('Sao chép host'), html: '<p class="muted small">' + esc(vm.host) + '</p>' + (vm.password ? '' : '<p class="muted small">' + esc(L('VM này chưa lưu mật khẩu nên phần Pass sẽ để trống.')) + '</p>'),
          fields: [{ id: 'full', label: L('Chép cả thông tin đăng nhập (IP, Port, User, Pass)'), type: 'checkbox', value: false }],
          buttons: [{ text: L('Hủy'), value: false }, { text: L('Sao chép'), value: true, cls: 'primary' }], cancelValue: false
        });
        if (!cr.value) break;
        var nlc = String.fromCharCode(10);
        var txt = cr.fields.full ? ['IP: ' + vm.host, 'Port: ' + (vm.port || 22), 'User: ' + vm.username, 'Pass: ' + (vm.password || '')].join(nlc) : vm.host;
        try { await navigator.clipboard.writeText(txt); toast(L('Đã sao chép.')); } catch (e) { toast(L('Không sao chép được.')); }
        break;
      }
      case 'del': deleteVms([vm.id]); break;
    }
  }

  async function groupMenu(name) {
    var tag = name === '★ Ghim' || name === 'Chưa có tag' ? null : name;
    var members = sessionsList().filter(function (s) { return groupKey(s) === name; });
    var menu = [{ text: L('Mở tất cả VM trong nhóm ({0})', members.length), value: 'open', icon: 'play' }];
    if (tag) menu.push({ text: L('Đổi tên tag/nhóm…'), value: 'rename', icon: 'edit' });
    menu.push({ text: L('Xuất cả nhóm ra file…'), value: 'export', icon: 'export' });
    var r = await modal({ title: groupLabel(name), menu: menu });
    if (r.value === 'open') openMany(members);
    else if (r.value === 'export') exportDialog(members.map(function (s) { return s.id; }));
    else if (r.value === 'rename') {
      var n = await modal({ title: L('Đổi tên nhóm'), fields: [{ id: 'n', label: L('Tên mới'), value: tag }], buttons: [{ text: L('Hủy'), value: false }, { text: L('Đổi tên'), value: true, cls: 'primary' }], cancelValue: false });
      var nn = (n.fields.n || '').trim();
      if (n.value && nn && nn !== tag) { vault.renameTag(tag, nn); toast(L('Đã đổi tên nhóm.')); }
    }
  }

  async function deleteVms(ids) {
    var vms = ids.map(function (i) { return vault.find(i); }).filter(Boolean);
    if (!vms.length) return;
    var msg = vms.length === 1 ? L('<p>Xóa <b>{0}</b>?</p>', esc(vms[0].name || vms[0].host)) : L('<p>Xóa <b>{0}</b> VM?</p>', vms.length) + '<ul>' + vms.slice(0, 5).map(function (v) { return '<li>' + esc(v.name || v.host) + '</li>'; }).join('') + '</ul>';
    if (!(await UI.confirm(L('Xác nhận xóa'), msg + L('<p class="muted small">VM đã xóa nằm trong Thùng rác 30 ngày và khôi phục được trên mọi thiết bị.</p>'), L('Xóa'), true))) return;
    vault.remove(ids); toast(L('Đã chuyển vào thùng rác.'));
  }

  // ===== Thêm / sửa VM =====
  async function editVm(vm, quick) {
    var isNew = !vm, v = vm || { port: 22, username: 'root', tags: [], password: '' };
    var hasKey = !!v.keyFileContent, picked = { name: null };
    var fields = [
      { id: 'name', label: L('Tên hiển thị (tùy chọn)'), value: v.name || '' },
      { id: 'tags', label: L('Tag (cách nhau bằng dấu phẩy, tùy chọn)'), value: (v.tags || []).join(', ') },
      { id: 'host', label: 'IP / Hostname *', value: v.host || '', inputmode: 'url' },
      { id: 'port', label: 'Port *', value: String(v.port || 22), type: 'number', inputmode: 'numeric' },
      { id: 'username', label: 'User *', value: v.username || '' },
      { id: 'password', label: L('Mật khẩu'), type: 'password', value: v.password || '' },
      { id: 'savePw', label: L('Lưu mật khẩu'), type: 'checkbox', value: isNew || !!v.password },
      { id: 'key', label: L('SSH key (OpenSSH / PEM) — dán nội dung hoặc chọn file'), type: 'textarea', rows: 4, mono: true, placeholder: '-----BEGIN OPENSSH PRIVATE KEY-----', value: '' },
      { id: 'passphrase', label: L('Passphrase của key'), type: 'password', value: v.passphrase || '' }
    ];
    if (quick) fields = fields.filter(function (x) { return x.id !== 'name' && x.id !== 'tags' && x.id !== 'savePw'; });
    if (hasKey) fields.splice(8, 0, { id: 'delKey', label: L('Xóa key đã lưu ({0})', v.keyFileName || 'key'), type: 'checkbox', value: false });

    var r = await modal({
      title: quick ? L('Kết nối nhanh') : isNew ? L('Thêm VM') : L('Sửa VM'), fields: fields, noAutofocus: !isNew,
      validate: function (f) {
        if (!f.host.trim()) return L('Nhập IP/Hostname.');
        var p = parseInt(f.port, 10); if (!(p >= 1 && p <= 65535)) return L('Port phải từ 1 đến 65535.');
        if (!f.username.trim()) return L('Nhập user.');
        return null;
      },
      buttons: [
        { text: L('Hủy'), value: false },
        {
          text: L('Kiểm tra'), onClick: async function (ctx) {
            var f = ctx.values();
            if (!f.host.trim() || !f.username.trim()) return ctx.setError(L('Nhập host và user trước.'));
            var port = parseInt(f.port, 10) || 22; ctx.setInfo(L('Đang kiểm tra kết nối…'));
            var trusted = (store.get('hosts', {}))[f.host.trim() + ':' + port], seen = null;
            try {
              await ensureWasm();
              var key = f.key && f.key.trim() ? f.key : (hasKey && !f.delKey ? td.decode(WN.unb64(v.keyFileContent)) : '');
              var ssh = await window.WNSSH.connect({
                relayUrl: RELAY, token: auth.token, host: f.host.trim(), port: port, user: f.username.trim(),
                password: f.password || '', key: key, passphrase: f.passphrase || '', cols: 80, rows: 24,
                verifyHostKey: function (info) { seen = info.fingerprint; return !!trusted && trusted === info.fingerprint; },
                prompt: function () { return null; }
              });
              ssh.close(); ctx.setInfo(L('Kết nối thành công ✓'));
            } catch (e) {
              if (seen && trusted !== seen) ctx.setInfo(L('Máy chủ trả lời (khóa {0}). Bấm "Lưu & kết nối" để xác nhận khóa rồi đăng nhập.', seen));
              else ctx.setError(e.message || String(e));
            }
          }
        },
        quick ? { text: L('Kết nối'), value: 'connect', cls: 'primary' } : { text: L('Lưu'), value: 'save', cls: 'primary' },
        quick ? null : { text: L('Lưu & kết nối'), value: 'connect', cls: 'primary' }
      ].filter(Boolean),
      onOpen: function (ctx) {
        // chip tag có sẵn
        var tags = quick ? [] : Object.keys(vault.tags()).sort();
        if (tags.length) {
          var pick = document.createElement('div'); pick.className = 'tagpick';
          pick.innerHTML = tags.map(function (t) { return '<button type="button" class="chip" data-tag="' + esc(t) + '">' + esc(t) + '</button>'; }).join('');
          pick.addEventListener('click', function (ev) {
            var c = ev.target.closest('.chip'); if (!c) return;
            var cur = parseTags(ctx.inputs.tags.value); if (cur.indexOf(c.dataset.tag) < 0) cur.push(c.dataset.tag);
            ctx.inputs.tags.value = cur.join(', ');
          });
          ctx.inputs.tags.parentNode.insertAdjacentElement('afterend', pick);
        }
        // chọn file key
        var fi = document.createElement('input'); fi.type = 'file'; fi.hidden = true;
        var fb = document.createElement('button'); fb.type = 'button'; fb.className = 'btn'; fb.textContent = L('Chọn file key…'); fb.style.marginTop = '6px';
        fb.onclick = function () { fi.click(); };
        fi.onchange = async function () { var file = fi.files[0]; if (!file) return; picked.name = file.name; ctx.inputs.key.value = await file.text(); ctx.setInfo(L('Đã đọc {0}', file.name)); };
        ctx.inputs.key.parentNode.appendChild(fb); ctx.inputs.key.parentNode.appendChild(fi);
      }
    });
    if (!r.value) return;
    var f = r.fields, keyText = (f.key || '').trim();
    var data = {
      name: quick ? '' : f.name.trim(), tags: quick ? [] : parseTags(f.tags), host: f.host.trim(), port: parseInt(f.port, 10) || 22, username: f.username.trim(),
      password: (quick || f.savePw) && f.password ? f.password : null, passphrase: f.passphrase ? f.passphrase : null
    };
    if (keyText) { data.keyFileContent = b64utf8(keyText.replace(/\r\n/g, '\n') + (/\n$/.test(keyText) ? '' : '\n')); data.keyFileName = picked.name || 'id_key'; }
    else if (hasKey && !f.delKey) { data.keyFileContent = v.keyFileContent; data.keyFileName = v.keyFileName; }
    else { data.keyFileContent = null; data.keyFileName = null; }
    if (quick) { data.id = 'quick-' + Date.now(); openTab(data); return; }   // kết nối nhanh: không lưu vào danh sách VM
    var saved = isNew ? vault.add(data) : vault.update(v.id, data);
    selectedId = saved.id;
    toast(isNew ? L('Đã thêm VM.') : L('Đã lưu.'));
    if (r.value === 'connect') openTab(saved);
  }

  // ===== Thanh công cụ =====
  $('tbAdd').addEventListener('click', function () { setView('vSessions'); editVm(null); });
  $('tbQuick').addEventListener('click', function () { editVm(null, true); });
  $('tbExport').addEventListener('click', function () { exportDialog(null); });
  $('tbImport').addEventListener('click', function () { $('importFile').click(); });
  $('tbCloud').addEventListener('click', cloudDialog);
  $('tbSnip').addEventListener('click', function () { snippetsDialog(); });
  $('tbSearch').addEventListener('click', function () { setView('vSessions'); $('search').focus(); });
  $('tbSettings').addEventListener('click', settingsDialog);
  document.querySelectorAll('.bottomnav button').forEach(function (b) { b.addEventListener('click', function () { setView(b.dataset.view); }); });
  $('tabAdd').addEventListener('click', function () { setView('vSessions'); });

  async function quickConnect() {
    var last = store.get('quick', {});
    var r = await modal({
      title: L('Kết nối nhanh'),
      fields: [
        { id: 'host', label: L('Máy chủ (IP / tên miền)'), value: last.host || '', inputmode: 'url' },
        { id: 'port', label: L('Cổng'), value: String(last.port || 22), type: 'number', inputmode: 'numeric' },
        { id: 'user', label: 'User', value: last.user || 'root' },
        { id: 'pw', label: L('Mật khẩu (để trống nếu nhập sau)'), type: 'password' }
      ],
      validate: function (f) { return !f.host.trim() || !f.user.trim() ? L('Nhập máy chủ và user.') : null; },
      buttons: [{ text: L('Hủy'), value: false }, { text: L('Kết nối'), value: true, cls: 'primary' }], cancelValue: false
    });
    if (!r.value) return;
    var f = r.fields, port = parseInt(f.port, 10) || 22;
    store.set('quick', { host: f.host.trim(), port: port, user: f.user.trim() });
    openTab({ id: 'quick-' + Date.now(), name: f.user.trim() + '@' + f.host.trim(), host: f.host.trim(), port: port, username: f.user.trim(), password: f.pw || null, tags: [] });
  }

  // ===== Cloud: backup / khôi phục / thùng rác =====
  async function cloudDialog() {
    for (;;) {
      var d = vault.state, trash = vault.trashItems().length;
      var r = await modal({
        title: L('Backup cloud WN Term'),
        html: '<p><b>' + esc(auth.email) + '</b></p><p class="muted small">' + (d.lastSync ? L('Backup gần nhất: {0}', esc(fmtTime(d.lastSync))) : L('Chưa backup lần nào.')) +
          (d.dirty ? L('<br><span style="color:#F79009">Có thay đổi chưa backup.</span>') : '') + (syncErr ? L('<br><span class="error">Lần gần nhất lỗi: {0}</span>', esc(LT(syncErr.message))) : '') + '</p>' +
          L('<p class="muted small">Backup ở máy này, đăng nhập ở máy khác là có cùng danh sách VM. Mỗi bản backup được giữ 30 ngày; VM đã xóa nằm trong thùng rác 30 ngày.</p>'),
        menu: [
          { text: L('Backup ngay'), value: 'sync', icon: 'cloud' },
          { text: L('Khôi phục từ backup…'), value: 'restore', icon: 'history' },
          { text: L('Thùng rác'), value: 'trash', icon: 'trash', right: trash ? String(trash) : '' },
          { text: L('Nâng cao…'), value: 'adv', icon: 'settings' },
          { text: L('Đăng xuất'), value: 'logout', icon: 'logout', cls: 'danger' }
        ]
      });
      if (r.value === 'sync') {
        var res = await syncNow(false);
        if (res) toast(L('Đã backup {0} VM.', res.total));
        else if (syncErr) toast(L('Lỗi: {0}', LT(syncErr.message)), 5000);
      } else if (r.value === 'restore') await historyDialog();
      else if (r.value === 'trash') await trashDialog();
      else if (r.value === 'adv') await advancedDialog();
      else if (r.value === 'logout') { await logout(); return; }
      else return;
    }
  }

  async function advancedDialog() {
    var r = await modal({
      title: L('Nâng cao'),
      html: L('<p class="muted small">Dùng khi cần ép một chiều. Dữ liệu bị ghi đè vẫn còn trong lịch sử/thùng rác.</p>'),
      menu: [
        { text: L('Kéo về: thay danh sách máy này bằng bản backup'), value: 'pull', icon: 'down' },
        { text: L('Đẩy lên: thay bản backup bằng danh sách máy này'), value: 'push', icon: 'up' },
        { text: L('Đổi mật khẩu / xóa tài khoản (trang web)'), value: 'web', icon: 'link' }
      ]
    });
    try {
      if (r.value === 'pull') {
        if (!(await UI.confirm(L('Kéo về cưỡng bức'), L('<p>Danh sách trên máy này sẽ giống hệt bản backup. VM chỉ có ở máy này sẽ vào thùng rác của máy này. Tiếp tục?</p>'), L('Kéo về'), true))) return;
        var a = await vault.forcePull(); toast(L('Đã kéo về: {0} VM ({1} vào thùng rác).', a.total, a.deleted));
      } else if (r.value === 'push') {
        if (!(await UI.confirm(L('Đẩy lên cưỡng bức'), L('<p>Bản backup và mọi thiết bị khác sẽ nhận đúng danh sách của máy này (VM không có ở đây vào thùng rác của các máy đó; bản cũ vẫn còn trong lịch sử). Tiếp tục?</p>'), L('Đẩy lên'), true))) return;
        var b = await vault.forcePush(); toast(L('Đã đẩy lên: {0} VM.', b.total)); updateBadge(); updateStatus();
      } else if (r.value === 'web') window.open('/tai-khoan', '_blank', 'noopener');
    } catch (e) { toast(L('Lỗi: {0}', LT(e.message)), 5000); }
  }

  async function historyDialog() {
    var versions;
    try { versions = await vault.history(); } catch (e) { return toast(L('Không tải được lịch sử: {0}', LT(e.message)), 5000); }
    if (!versions.length) return modal({ title: L('Khôi phục từ backup'), html: L('<p>Chưa có bản backup nào trên máy chủ. Hãy bấm "Backup ngay" ở máy đang có VM.</p>') });
    var r = await modal({
      title: L('Chọn bản backup để khôi phục'),
      html: L('<p class="muted small">Backup từ mọi thiết bị của bạn, mới nhất ở trên (giữ 30 ngày).</p>'),
      menu: versions.map(function (v) { return { text: fmtTime(v.createdAt) + (v.device ? ' · ' + v.device : ''), right: v.current ? L('mới nhất') : '', value: v.version, icon: 'history' }; })
    });
    if (!r.value) return;
    var ver = versions.find(function (x) { return x.version === r.value; });
    var data, p;
    try { data = await vault.loadVersion(r.value); p = vault.preview(data); } catch (e) { return toast(L('Không đọc được bản backup: {0}', LT(e.message)), 5000); }
    var names = p.missingNames.length ? '<br><span class="muted small">' + esc(p.missingNames.slice(0, 8).join(', ')) + (p.missingNames.length > 8 ? L(', …') : '') + '</span>' : '';
    var a = await modal({
      title: L('Bản backup {0}', fmtTime(ver.createdAt)),
      html: '<div class="kv"><span>' + L('Số VM') + '</span><span>' + p.total + '</span>' + (p.snippets != null ? '<span>' + L('Lệnh lưu sẵn') + '</span><span>' + p.snippets + '</span>' : '') + '<span>' + L('Máy này đang thiếu') + '</span><span>' + p.missingHere + names + '</span><span>' + L('Khác nội dung') + '</span><span>' + p.different + '</span><span>' + L('Chỉ có ở máy này') + '</span><span>' + p.onlyHere + '</span></div>',
      stack: true,
      buttons: [
        { text: L('Khôi phục bản này (đưa danh sách về đúng bản này)'), value: 'all', cls: 'primary' },
        { text: L('Chỉ thêm lại VM còn thiếu'), value: 'missing' },
        { text: L('Quay lại'), value: null }
      ]
    });
    try {
      if (a.value === 'all') {
        var ok = await UI.confirm(L('Khôi phục bản backup'), L('<p>Danh sách VM (trên mọi thiết bị) sẽ trở về đúng bản này.') + (p.onlyHere ? L(' <b>{0}</b> VM không có trong bản đó sẽ vào thùng rác.', p.onlyHere) : '') + L(' Tiếp tục?</p>'), L('Khôi phục'), true);
        if (!ok) return;
        var res = await vault.restore(data, true);
        toast(L('Đã khôi phục: {0} VM ({1} vào thùng rác).', res.total, res.deleted), 4000);
      } else if (a.value === 'missing') {
        var res2 = await vault.restore(data, false);
        toast(L('Đã thêm lại {0} VM.', res2.added), 4000);
      }
      renderList(); updateBadge(); updateStatus();
    } catch (e) { toast(L('Lỗi: {0}', LT(e.message)), 5000); }
  }

  async function trashDialog() {
    for (var round = 0; ; round++) {
      var items = vault.trashItems();
      if (!items.length) {
        if (round === 0) await modal({ title: L('Thùng rác'), html: L('<p>Thùng rác trống.</p><p class="muted small">VM đã xóa (ở thiết bị này hoặc đồng bộ từ thiết bị khác) được giữ ở đây 30 ngày.</p>') });
        return;
      }
      var r = await modal({
        title: L('Thùng rác ({0})', items.length), wide: true,
        html: L('<p class="muted small">VM đã xóa được giữ 30 ngày. Chọn VM rồi khôi phục hoặc xóa vĩnh viễn.</p>'),
        fields: items.map(function (e) { return { id: e.id, type: 'checkbox', label: e.name + ' — ' + e.subtitle + L(' · xóa {0}', fmtTime(e.deletedAt)), value: false }; }),
        buttons: [{ text: L('Đóng'), value: null }, { text: L('Xóa vĩnh viễn'), value: 'forget', cls: 'danger' }, { text: L('Khôi phục'), value: 'restore', cls: 'primary' }]
      });
      var ids = items.filter(function (e) { return r.fields[e.id]; }).map(function (e) { return e.id; });
      if (!r.value) return;
      if (!ids.length) { toast(L('Chọn VM trước.')); continue; }
      if (r.value === 'restore') { var n = vault.restoreFromTrash(ids); toast(L('Đã khôi phục {0} VM.', n)); }
      else if (r.value === 'forget') {
        if (await UI.confirm(L('Xóa vĩnh viễn'), L('<p>Xóa vĩnh viễn <b>{0}</b> VM khỏi thùng rác? Không hoàn tác được trên thiết bị này.</p>', ids.length), L('Xóa vĩnh viễn'), true)) vault.forget(ids);
      }
      renderList();
    }
  }

  // ===== Import / Export file .wnterm =====
  /** Gửi file cho người dùng: ưu tiên bảng chia sẻ của iOS (Lưu vào Tệp, AirDrop…); không có thì tải xuống. */
  async function deliverFile(file, label) {
    var canShare = navigator.canShare && navigator.canShare({ files: [file] });
    if (canShare) {
      try { await navigator.share({ files: [file] }); return true; }
      catch (e) {
        if (e && e.name === 'AbortError') return false;
        // hết "cử chỉ chạm" sau khi mã hóa → hỏi lại bằng một lần bấm mới
        var r = await modal({ title: L('File đã sẵn sàng'), html: L('<p>{0} ({1} KB). Bấm nút dưới để {2}.</p>', esc(file.name), Math.ceil(file.size / 1024), label || L('lưu')), buttons: [{ text: L('Đóng'), value: false }, { text: label || L('Lưu file'), value: true, cls: 'primary' }], cancelValue: false });
        if (!r.value) return false;
        try { await navigator.share({ files: [file] }); return true; } catch (e2) { return false; }
      }
    }
    var a = document.createElement('a'); a.href = URL.createObjectURL(file); a.download = file.name;
    document.body.appendChild(a); a.click(); a.remove(); setTimeout(function () { URL.revokeObjectURL(a.href); }, 10000);
    return true;
  }

  async function exportDialog(preIds) {
    var all = sessionsList().slice().sort(function (a, b) { return (a.name || a.host).localeCompare(b.name || b.host, 'vi'); });
    if (!all.length) return toast(L('Chưa có VM để xuất.'));
    var fields = all.map(function (s) { return { id: 'v' + s.id, type: 'checkbox', label: (s.name || s.host) + ' — ' + s.username + '@' + s.host, value: !preIds || preIds.indexOf(s.id) >= 0 }; });
    fields.push({ id: 'withPw', type: 'checkbox', label: L('Kèm mật khẩu & SSH key (bảo vệ bằng mật khẩu export)'), value: true });
    fields.push({ id: 'p1', label: L('Mật khẩu export (tối thiểu 8 ký tự)'), type: 'password' });
    fields.push({ id: 'p2', label: L('Nhập lại mật khẩu'), type: 'password' });
    var chosen = null;
    var r = await modal({
      title: L('Export danh sách VM'), wide: true, fields: fields, noAutofocus: true,
      html: L('<p class="muted small">Chọn VM cần xuất. File <code>.wnterm</code> mở được trong app Windows/Android (nút Import).</p>'),
      validate: function (f) {
        chosen = all.filter(function (s) { return f['v' + s.id]; });
        if (!chosen.length) return L('Chọn ít nhất một VM.');
        if (f.withPw) { if ((f.p1 || '').length < 8) return L('Mật khẩu export phải có tối thiểu 8 ký tự.'); if (f.p1 !== f.p2) return L('Mật khẩu xác nhận không khớp.'); }
        return null;
      },
      buttons: [{ text: L('Hủy'), value: false }, { text: 'Export', value: true, cls: 'primary' }], cancelValue: false
    });
    if (!r.value) return;
    var f = r.fields;
    setStatus(L('Đang mã hóa file export…'));
    try {
      var json = await WNFile.exportSessions(chosen, f.withPw ? f.p1 : '', true);
      var d = new Date(), p = function (n) { return ('0' + n).slice(-2); };
      var name = 'wnterm_backup_' + d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate()) + '_' + p(d.getHours()) + p(d.getMinutes()) + p(d.getSeconds()) + '.wnterm';
      var ok = await deliverFile(new File([json], name, { type: 'application/octet-stream' }), L('Lưu file export'));
      if (ok) toast(L('Đã export {0} VM.', chosen.length));
    } catch (e) { toast(L('Lỗi export: {0}', LT(e.message || String(e))), 5000); }
    updateStatus();
  }

  $('importFile').addEventListener('change', async function () {
    var file = this.files[0]; this.value = '';
    if (!file) return;
    try { runImport(file.name, await file.text()); } catch (e) { toast(L('Không đọc được file: {0}', LT(e.message)), 5000); }
  });

  async function runImport(fileName, text) {
    var parsed;
    try { parsed = WNFile.parse(text); } catch (e) { return modal({ title: L('Không nhập được'), html: '<p>' + esc(e.message) + '</p>' }); }
    var opened = null;
    var fields = [];
    if (parsed.protected) fields.push({ id: 'pw', label: L('File được bảo vệ — nhập mật khẩu export'), type: 'password' });
    fields.push({ id: 'res', type: 'radio', label: L('Khi VM đã có trong danh sách (trùng ID hoặc cùng host/user/port/tên):'), value: 'skip', options: [
      { value: 'skip', label: L('Bỏ qua (giữ VM hiện có)') }, { value: 'overwrite', label: L('Ghi đè (cập nhật theo file)') }, { value: 'copy', label: L('Thêm bản sao') }] });
    var r = await modal({
      title: L('Import danh sách VM'),
      html: '<p><b>' + esc(fileName) + '</b><br><span class="muted small">' + (parsed.kind === 'moba' ? 'MobaXterm — ' : 'WN Term — ') + parsed.count + ' VM' + (parsed.kind === 'moba' ? L(' (không kèm mật khẩu)') : parsed.protected ? L(' (có mã hóa)') : L(' (không kèm mật khẩu)')) + '</span></p>',
      fields: fields,
      validate: async function (f) {
        try { opened = await WNFile.open(parsed, f.pw || ''); return null; }
        catch (e) { return e.code === 'wrong_password' ? L('Sai mật khẩu file.') : (e.message || String(e)); }
      },
      buttons: [{ text: L('Hủy'), value: false }, { text: 'Import', value: true, cls: 'primary' }], cancelValue: false
    });
    if (!r.value || !opened) return;
    var res = vault.importItems(opened.items, r.fields.res || 'skip');
    renderList();
    await modal({
      title: L('Import hoàn tất'),
      html: '<div class="kv"><span>' + L('VM trong file') + '</span><span>' + res.total + '</span><span>' + L('Nhập mới') + '</span><span>' + res.imported + '</span><span>' + L('Ghi đè') + '</span><span>' + res.overwritten + '</span><span>' + L('Bỏ qua') + '</span><span>' + res.skipped + '</span></div>' +
        (opened.corrupt ? L('<p class="error">{0} mật khẩu không giải mã được.</p>', opened.corrupt) : '') +
        (parsed.kind === 'moba' ? L('<p class="muted small">MobaXterm không chứa mật khẩu/key — hãy sửa VM để thêm.</p>') : '')
    });
  }

  // ===== Cài đặt =====
  /** Đổi ngôn ngữ ngay: dựng lại phần giao diện đang hiển thị (các tab terminal giữ nguyên). */
  function relocalize() {
    WNi18n.apply();
    if (vault) renderList();
    renderTabs(); showMonitor(active && active.mon);
    if (view === 'vSftp') sftpShow(); else if (sftpOk()) sftpRender();
    updateStatus(); updateBadge();
  }
  async function settingsDialog() {
    var r = await modal({
      title: L('Cài đặt'),
      html: L('<p class="muted small">Tài khoản: <b>{0}</b> · bản web {1}</p>', esc(auth.email), esc(VERSION)),
      menu: [
        { text: L('Xóa các khóa máy chủ đã tin cậy'), value: 'hosts', icon: 'key' },
        { text: L('Đăng xuất'), value: 'logout', icon: 'logout', cls: 'danger' }
      ],
      fields: [
        { id: 'lang', label: 'Ngôn ngữ / Language', type: 'select', value: WNi18n.lang, options: [{ value: 'vi', label: 'Tiếng Việt' }, { value: 'en', label: 'English' }] },
        { id: 'theme', label: L('Giao diện'), type: 'select', value: settings.theme, options: [{ value: 'dark', label: L('Tối') }, { value: 'light', label: L('Sáng') }] },
        { id: 'fontSize', label: L('Cỡ chữ terminal (cũng chỉnh được bằng 2 ngón tay)'), type: 'select', value: String(termFont()), options: [9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22].map(function (n) { return { value: String(n), label: String(n) }; }) },
        { id: 'scrollback', label: L('Số dòng cuộn lại'), type: 'select', value: String(settings.scrollback), options: [1000, 5000, 10000, 20000].map(function (n) { return { value: String(n), label: String(n) }; }) },
        { id: 'confirmPaste', label: L('Hỏi xác nhận khi dán nhiều dòng'), type: 'checkbox', value: settings.confirmPaste },
        { id: 'showHidden', label: L('Hiện file ẩn trong SFTP'), type: 'checkbox', value: settings.showHidden },
        { id: 'autoSync', label: L('Tự động backup (khi mở app, sau mỗi thay đổi, mỗi 10 phút)'), type: 'checkbox', value: settings.autoSync }
      ],
      buttons: [{ text: L('Hủy'), value: false }, { text: L('Lưu'), value: true, cls: 'primary' }], cancelValue: false, noAutofocus: true
    });
    if (r.value === 'hosts') { store.del('hosts'); return toast(L('Đã xóa danh sách khóa máy chủ.')); }
    if (r.value === 'logout') return logout();
    if (!r.value) return;
    var f = r.fields;
    settings.theme = f.theme; settings.fontSize = parseInt(f.fontSize, 10); settings.scrollback = parseInt(f.scrollback, 10);
    settings.confirmPaste = f.confirmPaste; settings.showHidden = f.showHidden; settings.autoSync = f.autoSync;
    if ((f.lang === 'en' ? 'en' : 'vi') !== WNi18n.lang) { settings.lang = f.lang === 'en' ? 'en' : 'vi'; WNi18n.set(settings.lang); relocalize(); }
    saveSettings(); applyTheme();
    tabs.forEach(function (t) { t.term.options.fontSize = termFont(); t.term.options.scrollback = settings.scrollback; fitTab(t); });
    if (view === 'vSftp') sftpRender();
    if (settings.autoSync && vault && vault.state.dirty) scheduleSync(2000);
  }

  // ===== Bộ SSH (WebAssembly) =====
  var wasmReady = null, WASM_API = '2';
  async function purgeCaches() {
    try { if (navigator.serviceWorker) { var regs = await navigator.serviceWorker.getRegistrations(); for (var r of regs) await r.unregister(); } } catch (e) {}
    try { if (window.caches) { var keys = await caches.keys(); for (var k of keys) await caches.delete(k); } } catch (e) {}
  }
  function ensureWasm() {
    if (window.WNSSH) return Promise.resolve();
    if (!wasmReady) {
      wasmReady = (async function () {
        var go = new Go();
        var buf = await (await fetch('wnssh.wasm?v=' + VERSION)).arrayBuffer();
        var res = await WebAssembly.instantiate(buf, go.importObject);
        go.run(res.instance);
        for (var i = 0; i < 100 && !window.WNSSH; i++) await new Promise(function (r) { setTimeout(r, 20); });
        if (!window.WNSSH) throw new Error(L('Không khởi động được bộ SSH.'));
        if (window.WNSSH.version !== WASM_API) {
          // bộ SSH cũ còn trong bộ nhớ đệm (service worker bản trước) → xóa đệm và tải lại một lần
          if (!sessionStorage.getItem('wnweb.purged')) { sessionStorage.setItem('wnweb.purged', '1'); await purgeCaches(); location.reload(); await new Promise(function () {}); }
          throw new Error(L('Bộ SSH chưa được cập nhật — hãy đóng hẳn app rồi mở lại.'));
        }
      })();
      wasmReady.catch(function () { wasmReady = null; });
    }
    return wasmReady;
  }

  async function verifyHostKey(info) {
    var hosts = store.get('hosts', {}), key = info.host + ':' + info.port;
    if (hosts[key] === info.fingerprint) return true;
    var changed = !!hosts[key];
    var r = await modal({
      title: changed ? L('⚠️ Khóa máy chủ ĐÃ THAY ĐỔI') : L('Xác nhận khóa máy chủ'),
      html: changed
        ? L('<p>Khóa của <b>{0}</b> khác với lần trước. Nếu bạn không vừa cài lại máy chủ, có thể có người đang giả mạo. Chỉ tiếp tục khi chắc chắn.</p><p class="muted small">Cũ:</p><div class="fp">{1}</div><p class="muted small">Mới ({2}):</p><div class="fp">{3}</div>', esc(key), esc(hosts[key]), esc(info.type), esc(info.fingerprint))
        : L('<p>Lần đầu kết nối <b>{0}</b> từ thiết bị này.</p><p class="muted small">Vân tay khóa ({1}):</p><div class="fp">{2}</div>', esc(key), esc(info.type), esc(info.fingerprint)),
      buttons: changed ? [{ text: L('Vẫn kết nối'), value: true, cls: 'danger' }, { text: L('Ngắt kết nối'), value: false, cls: 'primary' }]
        : [{ text: L('Hủy'), value: false }, { text: L('Tin cậy & kết nối'), value: true, cls: 'primary' }], cancelValue: false
    });
    if (r.value) { hosts[key] = info.fingerprint; store.set('hosts', hosts); }
    return !!r.value;
  }

  async function promptText(text, echo) {
    var r = await modal({ title: L('Máy chủ yêu cầu'), fields: [{ id: 'v', label: LT(text), type: echo ? 'text' : 'password' }],
      buttons: [{ text: L('Hủy'), value: false }, { text: 'OK', value: true, cls: 'primary' }], cancelValue: false });
    return r.value ? r.fields.v : null;
  }

  // ===== Terminal =====
  function makeTerm() {
    var T = window.Terminal.Terminal || window.Terminal;
    var term = new T({
      fontFamily: "'JetBrains Mono', Menlo, Consolas, monospace", fontSize: termFont(),
      scrollback: settings.scrollback, cursorBlink: true, allowProposedApi: true,
      theme: { background: '#1e1e1e', foreground: '#d4d4d4', cursor: '#aeafad', selectionBackground: '#264f78' }
    });
    var Fit = window.FitAddon.FitAddon || window.FitAddon, fit = new Fit(); term.loadAddon(fit);
    try { var U = window.Unicode11Addon.Unicode11Addon || window.Unicode11Addon; term.loadAddon(new U()); term.unicode.activeVersion = '11'; } catch (e) {}
    return { term: term, fit: fit };
  }

  var fontReady = (document.fonts ? Promise.all([document.fonts.load("13px 'JetBrains Mono'"), document.fonts.load("bold 13px 'JetBrains Mono'")]).catch(function () {}) : Promise.resolve());
  function refitAfterFont(tab) {
    fontReady.then(function () { try { tab.term.options.fontFamily = "'JetBrains Mono', Menlo, Consolas, monospace"; } catch (e) {} fitTab(tab); });
  }
  if (document.fonts) document.fonts.addEventListener('loadingdone', function () { tabs.forEach(function (t) { t.term.options.fontFamily = t.term.options.fontFamily; fitTab(t); }); });

  function openTab(vm, thenSftp, background) {
    var pane = document.createElement('div'); pane.className = 'term-pane'; $('termHost').appendChild(pane);
    var t = makeTerm();
    var tab = { id: 'tab' + Date.now() + Math.random(), vm: vm, pane: pane, term: t.term, fit: t.fit, ssh: null, state: 'idle', sftpPath: null, mon: null, dead: false, sftpSel: {}, line: '', lineBad: false, lastCmd: '' };
    tabs.push(tab);
    t.term.open(pane);
    refitAfterFont(tab);
    t.term.onData(function (d) { sendInput(tab, d); });
    t.term.onResize(function (sz) { if (tab.ssh) tab.ssh.resize(sz.cols, sz.rows); });
    // dán nhiều dòng bằng thao tác dán của iOS cũng được hỏi xác nhận
    pane.addEventListener('paste', function (ev) {
      var text = ev.clipboardData && ev.clipboardData.getData('text'); if (!text) return;
      if (settings.confirmPaste && /[\r\n]/.test(text.replace(/[\r\n]+$/, ''))) { ev.preventDefault(); ev.stopPropagation(); pasteText(tab, text); }
    }, true);
    var rc = document.createElement('div'); rc.className = 'reconnect'; rc.hidden = true;
    rc.innerHTML = L('<button class="btn">Kết nối lại</button>'); rc.firstChild.onclick = function () { connect(tab); };
    pane.appendChild(rc); tab.reconnectEl = rc;
    if (!background) { activate(tab); setView('vTerminal'); } else { pane.hidden = true; renderTabs(); }
    renderList();
    return connect(tab).then(function () { if (thenSftp && tab.ssh) { activate(tab); setView('vSftp'); } });
  }

  function setState(tab, state) {
    tab.state = state; tab.reconnectEl.hidden = state !== 'closed';
    renderTabs(); renderList();
  }

  async function connect(tab) {
    if (tab.state === 'connecting' || tab.dead) return;
    setState(tab, 'connecting');
    var term = tab.term, vm = tab.vm;
    term.write('\x1b[90m' + L('Đang kết nối {0}@{1}', vm.username, vm.host + (vm.port && vm.port !== 22 ? ':' + vm.port : '')) + '…\x1b[0m\r\n');
    try {
      if (!window.WNSSH) term.write('\x1b[90m' + L('Đang tải bộ SSH (lần đầu, khoảng 2,5 MB)…') + '\x1b[0m\r\n');
      await ensureWasm();
      fitTab(tab);
      var key = null;
      if (vm.keyFileContent) { try { key = td.decode(WN.unb64(vm.keyFileContent)); } catch (e) {} }
      var ssh = await window.WNSSH.connect({
        relayUrl: RELAY, token: auth ? auth.token : '',
        host: vm.host, port: vm.port || 22, user: vm.username,
        password: vm.password || '', key: key || '', passphrase: vm.passphrase || '',
        cols: term.cols, rows: term.rows,
        onData: function (u8) { term.write(u8); },
        onClose: function (msg) {
          tab.ssh = null; tab.mon = null; if (tab.dead) return; setState(tab, 'closed');
          term.write('\r\n\x1b[33m' + LT(msg) + '\x1b[0m\r\n');
          if (tab === active) { showMonitor(null); if (view === 'vSftp') sftpShow(); }
        },
        onStatus: function (s) { if (s.indexOf('keyerror:') === 0) term.write('\x1b[33m' + L('Không đọc được SSH key: {0}', s.slice(9)) + '\x1b[0m\r\n'); },
        verifyHostKey: verifyHostKey, prompt: promptText
      });
      if (tab.dead) { try { ssh.close(); } catch (e) {} return; }
      tab.ssh = ssh; setState(tab, 'connected');
      if (tab === active) { if (view === 'vTerminal') term.focus(); monitorKick(); }
    } catch (e) {
      tab.ssh = null; if (tab.dead) return; setState(tab, 'closed');
      var msg = LT(e && e.message ? e.message : String(e));
      if (/hết hạn|đăng nhập lại|expired|sign in again/.test(msg)) msg += L(' (Cloud → Đăng xuất rồi đăng nhập lại)');
      term.write('\r\n\x1b[31m' + L('Lỗi: {0}', msg) + '\x1b[0m\r\n');
    }
  }

  var mods = { ctrl: false, alt: false };
  function setMod(name, on) { mods[name] = on; document.querySelector('#keybar [data-k="' + name + '"]').classList.toggle('on', on); }
  function sendInput(tab, d) {
    if (!tab.ssh) { if (tab.state === 'closed' && /^[rR]$/.test(d)) connect(tab); return; }
    if (mods.ctrl && d.length === 1) {
      var c = d.toUpperCase().charCodeAt(0);
      if (c >= 64 && c <= 95) d = String.fromCharCode(c - 64); else if (d === ' ') d = '\x00';
      setMod('ctrl', false);
    }
    if (mods.alt) { d = '\x1b' + d; setMod('alt', false); }
    trackInput(tab, d);
    tab.ssh.write(d);
  }

  function activate(tab) {
    active = tab;
    tabs.forEach(function (t) { t.pane.hidden = t !== tab; });
    $('noTab').hidden = !!tab;
    renderTabs(); showMonitor(tab && tab.mon);
    setTimeout(function () { fitTab(tab); }, 20);
    updateStatus();
    if (view === 'vSftp') sftpShow();
  }

  function closeTab(tab, quiet) {
    tab.dead = true;
    if (tab.ssh) { try { tab.ssh.close(); } catch (e) {} }
    try { tab.term.dispose(); } catch (e) {}
    tab.pane.remove();
    tabs = tabs.filter(function (t) { return t !== tab; });
    if (active === tab) active = tabs[tabs.length - 1] || null;
    if (quiet) return;
    activate(active); renderList();
    if (!tabs.length && view === 'vTerminal') updateStatus();
  }

  function renderTabs() {
    var box = $('tabs'); box.innerHTML = '';
    tabs.forEach(function (t) {
      var b = document.createElement('div'); b.className = 'tab' + (t === active ? ' active' : ''); b.dataset.id = t.id;
      b.innerHTML = '<span class="tdot ' + (t.state === 'connected' ? 'connected' : t.state === 'connecting' ? 'connecting' : '') + '"></span>' +
        '<span class="tname">' + esc(t.vm.name || t.vm.host) + '</span><button class="tclose" title="' + esc(L('Đóng')) + '">' + ico('close') + '</button>';
      b.onclick = function (ev) {
        if (ev.target.closest('.tclose')) {
          if (t.state !== 'connected') return closeTab(t);
          UI.confirm(L('Đóng tab?'), L('<p>Ngắt kết nối <b>{0}</b>?</p>', esc(t.vm.name || t.vm.host)), L('Đóng'), true).then(function (ok) { if (ok) closeTab(t); });
          return;
        }
        activate(t); if (view === 'vTerminal') t.term.focus();
      };
      box.appendChild(b);
    });
    var a = box.querySelector('.tab.active'); if (a && a.scrollIntoView) a.scrollIntoView({ inline: 'nearest', block: 'nearest' });
  }
  longPress($('tabs'), '.tab', async function (el) {
    var tab = tabs.find(function (t) { return t.id === el.dataset.id; }); if (!tab) return;
    var r = await modal({ title: tab.vm.name || tab.vm.host, menu: [
      { text: L('Kết nối lại'), value: 'reconnect', icon: 'refresh' },
      { text: L('Mở thêm một tab cùng VM'), value: 'dup', icon: 'copy' },
      { text: L('Đóng các tab khác'), value: 'others', icon: 'close' },
      { text: L('Đóng tất cả'), value: 'all', icon: 'close', cls: 'danger' }
    ] });
    if (r.value === 'reconnect') { if (tab.ssh) { try { tab.ssh.close(); } catch (e) {} } setTimeout(function () { tab.state = 'closed'; connect(tab); }, 200); }
    else if (r.value === 'dup') openTab(tab.vm);
    else if (r.value === 'others') tabs.filter(function (t) { return t !== tab; }).forEach(function (t) { closeTab(t, true); }), activate(tab), renderList();
    else if (r.value === 'all') { tabs.slice().forEach(function (t) { closeTab(t, true); }); activate(null); renderList(); }
  });

  function fitTab(tab) {
    if (!tab || tab.pane.hidden || $('vTerminal').hidden) return;
    try { tab.fit.fit(); } catch (e) {}
  }
  function fitActive() { fitTab(active); }
  if (window.ResizeObserver) { var raf = 0; new ResizeObserver(function () { cancelAnimationFrame(raf); raf = requestAnimationFrame(fitActive); }).observe($('termHost')); }

  // Hàng phím
  var KEYS = { esc: '\x1b', tab: '\t', enter: '\r', up: '\x1b[A', down: '\x1b[B', right: '\x1b[C', left: '\x1b[D', home: '\x1b[H', end: '\x1b[F', pgup: '\x1b[5~', pgdn: '\x1b[6~', '^c': '\x03', '^d': '\x04' };
  $('keybar').addEventListener('mousedown', function (ev) { ev.preventDefault(); });   // giữ bàn phím ảo đang mở
  $('keybar').addEventListener('click', async function (ev) {
    var b = ev.target.closest('button'); if (!b || !active) return;
    var k = b.dataset.k;
    if (k === 'ctrl' || k === 'alt') return setMod(k, !mods[k]);
    if (k === 'kbd') { active.term.focus(); return; }
    if (k === 'snip') return snippetsDialog();
    if (k === 'copy') return screenTextDialog();
    if (k === 'paste') {
      try { var text = await navigator.clipboard.readText(); if (text) pasteText(active, text); }
      catch (e) { toast(L('Trình duyệt không cho đọc clipboard — nhấn giữ vào terminal để dán.')); }
      return;
    }
    sendInput(active, KEYS[k] || k);
    active.term.focus();
  });

  async function pasteText(tab, text) {
    if (settings.confirmPaste && /[\r\n]/.test(text.replace(/[\r\n]+$/, ''))) {
      var ok = await UI.confirm(L('Dán nhiều dòng?'), L('<p>Nội dung có nhiều dòng — mỗi dòng có thể chạy như một lệnh.</p>'), L('Dán'), false);
      if (!ok) return;
    }
    tab.term.paste(text);
  }

  /** Terminal trên iOS không bôi đen được bằng ngón tay → hiện nội dung màn hình trong ô văn bản để chọn/sao chép. */
  async function screenTextDialog() {
    if (!active) return;
    var b = active.term.buffer.active, lines = [];
    for (var i = Math.max(0, b.length - 400); i < b.length; i++) { var l = b.getLine(i); if (l) lines.push(l.translateToString(true)); }
    var text = lines.join('\n').replace(/\s+$/, '');
    var r = await modal({
      title: L('Nội dung terminal'), wide: true,
      html: L('<p class="muted small">Chạm giữ để chọn một đoạn rồi Sao chép, hoặc bấm "Sao chép tất cả".</p>'),
      fields: [{ id: 't', type: 'textarea', rows: 14, mono: true, value: text, noFocus: true }], noAutofocus: true,
      buttons: [{ text: L('Đóng'), value: null }, { text: L('Sao chép tất cả'), value: 'copy', cls: 'primary' }]
    });
    if (r.value === 'copy') { try { await navigator.clipboard.writeText(text); toast(L('Đã sao chép.')); } catch (e) { toast(L('Không sao chép được.')); } }
  }

  // Chụm 2 ngón để đổi cỡ chữ terminal
  (function () {
    var host = $('termHost'), start = null;
    function dist(t) { return Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY); }
    host.addEventListener('touchstart', function (e) { if (e.touches.length === 2) start = { d: dist(e.touches), fs: termFont() }; }, { passive: true });
    host.addEventListener('touchmove', function (e) {
      if (!start || e.touches.length !== 2) return;
      e.preventDefault();
      var fs = Math.max(9, Math.min(24, Math.round(start.fs * dist(e.touches) / start.d)));
      if (fs !== termFont()) { settings.fontSize = fs; tabs.forEach(function (t) { t.term.options.fontSize = fs; fitTab(t); }); }
    }, { passive: false });
    host.addEventListener('touchend', function (e) { if (e.touches.length < 2 && start) { start = null; saveSettings(); } });
  })();

  // Bàn phím ảo của iOS: khi mở thì co khung theo vùng nhìn thấy; khi đóng thì khung luôn phủ kín màn hình.
  function syncViewport() {
    var vv = window.visualViewport, root = document.documentElement, kb = 0;
    if (vv) kb = Math.max(0, window.innerHeight - vv.height - vv.offsetTop);
    var open = kb > 120;
    root.classList.toggle('kb-open', open);
    if (open) { root.style.setProperty('--vvt', vv.offsetTop + 'px'); root.style.setProperty('--vvh', vv.height + 'px'); }
    setTimeout(fitActive, 60);
  }
  if (window.visualViewport) { visualViewport.addEventListener('resize', syncViewport); visualViewport.addEventListener('scroll', syncViewport); }
  window.addEventListener('resize', syncViewport);
  syncViewport();

  // ===== Snippet (lệnh lưu sẵn): toàn cục hoặc riêng từng VM, đồng bộ qua vault =====
  /** Theo dõi dòng lệnh người dùng đang gõ ở tab (mọi đường vào terminal đều qua sendInput: bàn phím, hàng phím, snippet).
   *  Ký tự in được thêm vào dòng, Backspace/DEL xóa ký tự cuối, Enter chốt thành lastCmd (nếu dòng tin cậy và không rỗng) rồi đặt lại.
   *  Chuỗi escape (mũi tên, gọi lại lịch sử), Tab hay ký tự điều khiển khác khiến dòng "không tin cậy": Enter kế tiếp KHÔNG chốt. */
  function trackInput(tab, d) {
    for (var i = 0; i < d.length; i++) {
      var c = d.charCodeAt(i);
      if (c === 13 || c === 10) {
        var cmd = tab.line.trim();
        if (!tab.lineBad && cmd) tab.lastCmd = cmd;
        tab.line = ''; tab.lineBad = false;
      } else if (c === 127 || c === 8) tab.line = Array.from(tab.line).slice(0, -1).join('');
      else if (c === 27) { tab.lineBad = true; break; }   // phần còn lại của chunk là đuôi chuỗi escape
      else if (c < 32) tab.lineBad = true;
      else tab.line += d.charAt(i);
    }
  }

  function snipVmName(id) { var v = id && vault ? vault.find(id) : null; return v ? (v.name || v.username + '@' + v.host) : ''; }
  /** Danh sách hiển thị: có tab (terminal) → snippet riêng của VM đó trước, rồi toàn cục; không có tab (Sessions) → mọi snippet có VM còn tồn tại + toàn cục. */
  function snippetsFor(tab) {
    var list = vault.snippets().filter(function (s) { return !s.vmId || (tab ? s.vmId === tab.vm.id : !!vault.find(s.vmId)); });
    var nm = function (s) { return (s.name || s.command).toLowerCase(); };
    return list.sort(function (a, b) { return (a.vmId ? 0 : 1) - (b.vmId ? 0 : 1) || snipVmName(a.vmId).localeCompare(snipVmName(b.vmId), 'vi') || nm(a).localeCompare(nm(b), 'vi'); });
  }

  /** Gửi lệnh như vừa gõ (qua sendInput, cùng đường với hàng phím); nhiều dòng → CR; autoEnter → thêm Enter. */
  function sendSnippet(tab, s) {
    if (!tab || !tab.ssh || tab.state !== 'connected') return false;
    var cr = String.fromCharCode(13);
    var text = s.command.replace(/\r\n|\r|\n/g, cr);
    if (s.autoEnter) text = text.replace(/\r+$/, '') + cr;
    setMod('ctrl', false); setMod('alt', false);
    sendInput(tab, text);
    return true;
  }

  async function snippetsDialog() {
    if (!vault) return;
    var q = '';
    for (;;) {
      var tab = view === 'vTerminal' ? active : null;
      var canSend = !!(tab && tab.ssh && tab.state === 'connected');
      var res = await modal({
        title: L('Snippet (lệnh lưu sẵn)'), cls: 'sheet', noAutofocus: true, cancelValue: null,
        fields: [{ id: 'q', type: 'text', placeholder: L('Tìm snippet…'), value: q, noFocus: true, inputmode: 'search' }],
        buttons: [
          { text: L('Đóng'), onClick: function (ctx) { ctx.close(null); } },
          { text: L('Lưu lệnh vừa gõ'), onClick: function (ctx) { ctx.close('last'); } },
          { text: L('＋ Mới'), cls: 'primary', onClick: function (ctx) { ctx.close('new'); } }
        ],
        onOpen: function (ctx) {
          var box = document.createElement('div'); box.className = 'snip-list'; ctx.body.appendChild(box);
          var note = document.createElement('p'); note.className = 'muted small snip-note'; note.hidden = canSend;
          note.textContent = L('Mở một terminal đã kết nối để gửi lệnh — ở đây chỉ quản lý (thêm/sửa/xóa).'); ctx.body.appendChild(note);
          function render() {
            var qq = ctx.inputs.q.value.trim().toLowerCase(), all = snippetsFor(tab);
            var list = all.filter(function (s) { return !qq || [s.name, s.command, snipVmName(s.vmId)].some(function (v) { return v && String(v).toLowerCase().indexOf(qq) >= 0; }); });
            if (!list.length) { box.innerHTML = '<p class="muted small snip-empty">' + esc(all.length ? L('Không có snippet nào khớp.') : L('Chưa có snippet nào. Bấm "＋ Mới" để thêm lệnh dùng hay.')) + '</p>'; return; }
            box.innerHTML = list.map(function (s) {
              var first = s.command.split(/\r\n|\r|\n/)[0], multi = /\r|\n/.test(s.command.replace(/[\r\n]+$/, ''));
              return '<div class="snip' + (canSend ? '' : ' nosend') + '" data-id="' + esc(s.id) + '">' +
                '<button type="button" class="snip-main" data-a="send"' + (canSend ? '' : ' disabled') + '><span class="snip-top"><span class="snip-name">' + esc(s.name || first) + '</span>' +
                '<span class="snip-badge' + (s.vmId ? ' vm' : '') + '">' + esc(s.vmId ? snipVmName(s.vmId) : L('Tất cả VM')) + '</span></span>' +
                '<span class="snip-cmd">' + esc(first) + (multi ? ' …' : '') + (s.autoEnter ? ' ↵' : '') + '</span></button>' +
                '<button type="button" class="icon-btn" data-a="edit" title="' + esc(L('Sửa')) + '">' + ico('edit') + '</button>' +
                '<button type="button" class="icon-btn" data-a="del" title="' + esc(L('Xóa')) + '">' + ico('trash') + '</button></div>';
            }).join('');
            UI.hydrate(box);
          }
          ctx.inputs.q.addEventListener('input', render);
          box.addEventListener('click', function (ev) {
            var b = ev.target.closest('button[data-a]'), row = ev.target.closest('.snip'); if (!b || !row || b.disabled) return;
            ctx.close({ act: b.dataset.a, id: row.dataset.id });
          });
          render();
        }
      });
      q = res.fields.q || '';
      var v = res.value;
      if (!v) return;
      if (v === 'new') await snippetEditor(null, tab, '');
      else if (v === 'last') {
        if (!tab || !tab.lastCmd) toast(L('Chưa gõ lệnh nào trong tab này (hoặc dòng vừa rồi dùng phím mũi tên/Tab nên không chắc chắn).'), 4000);
        await snippetEditor(null, tab, tab ? tab.lastCmd : '');
      } else if (v.act === 'edit') await snippetEditor(vault.findSnippet(v.id), tab, '');
      else if (v.act === 'del') {
        var sn = vault.findSnippet(v.id);
        if (sn && await UI.confirm(L('Xóa snippet?'), L('<p>Xóa snippet <b>{0}</b>?</p>', esc(sn.name || sn.command.slice(0, 40))), L('Xóa'), true)) { vault.removeSnippet(sn.id); toast(L('Đã xóa snippet.')); }
      } else if (v.act === 'send') {
        var s = vault.findSnippet(v.id);
        if (s && sendSnippet(tab, s)) { toast(L('Đã gửi: {0}', s.name || s.command.split(/\r\n|\r|\n/)[0]), 1800); tab.term.focus(); return; }
      }
    }
  }

  async function snippetEditor(sn, tab, prefill) {
    var isNew = !sn, vms = tab ? [vault.find(tab.vm.id)].filter(Boolean) : sessionsList().slice().sort(function (a, b) { return (a.name || a.host).localeCompare(b.name || b.host, 'vi'); });
    if (sn && sn.vmId && !vms.some(function (v) { return v.id === sn.vmId; })) { var own = vault.find(sn.vmId); if (own) vms.push(own); }
    var options = [{ value: '', label: L('Tất cả VM') }].concat(vms.map(function (v) { return { value: v.id, label: L('Chỉ {0}', v.name || v.username + '@' + v.host) }; }));
    var r = await modal({
      title: isNew ? L('Thêm snippet') : L('Sửa snippet'), noAutofocus: !isNew && !!sn,
      fields: [
        { id: 'name', label: L('Tên (tùy chọn)'), value: sn ? sn.name : '' },
        { id: 'command', label: L('Lệnh *'), type: 'textarea', rows: 5, mono: true, value: sn ? sn.command : (prefill || '') },
        { id: 'vm', label: L('Dùng cho'), type: 'select', value: sn && sn.vmId ? sn.vmId : '', options: options },
        { id: 'autoEnter', label: L('Tự bấm Enter'), type: 'checkbox', value: sn ? sn.autoEnter : true }
      ],
      validate: function (f) { return f.command.trim() ? null : L('Nhập lệnh.'); },
      buttons: [{ text: L('Hủy'), value: false }, { text: L('Lưu'), value: true, cls: 'primary' }], cancelValue: false
    });
    if (!r.value) return null;
    var f = r.fields, cmd = f.command.replace(/\r\n?/g, String.fromCharCode(10)).replace(/\s+$/, '');
    var data = { name: f.name.trim() || cmd.split(String.fromCharCode(10))[0].slice(0, 40), command: cmd, vmId: f.vm || null, autoEnter: !!f.autoEnter };
    var saved = isNew ? vault.addSnippet(data) : vault.updateSnippet(sn.id, data);
    toast(L('Đã lưu snippet.'));
    return saved;
  }

  // ===== Theo dõi máy chủ (CPU / RAM / mạng / ổ đĩa) — giống thanh Server Monitor của app =====
  var MON_CMD = "export LC_ALL=C; to=''; command -v timeout >/dev/null 2>&1 && to='timeout 2'; hn=$(uname -n 2>/dev/null || hostname); usr=$(whoami 2>/dev/null); " +
    "osp=$(grep -E '^PRETTY_NAME=' /etc/os-release 2>/dev/null | head -1 | cut -d= -f2- | tr -d '\"'); [ -f /etc/cloudlinux-release ] && osp=$(cat /etc/cloudlinux-release); " +
    "[ -z \"$osp\" ] && osp=$(cat /etc/redhat-release 2>/dev/null); [ -z \"$osp\" ] && osp=$(uname -s); " +
    "up=$(awk '{print int($1)}' /proc/uptime 2>/dev/null); mem=$(awk '/MemTotal/{t=$2} /MemAvailable/{a=$2} END{if(t>0) printf \"%d %d\", (t-a)/1024, t/1024}' /proc/meminfo 2>/dev/null); " +
    "cpu=$(awk '/^cpu /{print $2+$3+$4+$7+$8, $2+$3+$4+$5+$6+$7+$8}' /proc/stat 2>/dev/null); net=$(awk '$1 !~ /lo:|Inter|face/{rx+=$2; tx+=$10} END{printf \"%d %d\", rx, tx}' /proc/net/dev 2>/dev/null); " +
    "dsk=$($to df -hP / 2>/dev/null | awk 'NR==2{print $5}'); echo \"MON|$hn|$usr|$osp|$up|$mem|$cpu|$net|$dsk\"";
  var monTimer = null;

  function osIcon(osp) {
    var o = (osp || '').toLowerCase();
    if (o.indexOf('ubuntu') >= 0) return 'os_ubuntu'; if (o.indexOf('debian') >= 0) return 'os_debian'; if (o.indexOf('alpine') >= 0) return 'os_alpine';
    if (o.indexOf('arch') >= 0) return 'os_arch'; if (o.indexOf('suse') >= 0) return 'os_suse';
    if (/(red ?hat|centos|rocky|alma|cloudlinux|fedora|oracle)/.test(o)) return 'os_redhat';
    return 'os_linux';
  }
  function human(mb) { return mb >= 1024 ? (mb / 1024).toFixed(2).replace('.', WNi18n.dec()) + ' GB' : mb + ' MB'; }
  function rate(bps) { var mb = bps * 8 / 1e6; return mb >= 1 ? mb.toFixed(2).replace('.', WNi18n.dec()) + ' Mb/s' : (bps * 8 / 1e3).toFixed(0) + ' Kb/s'; }
  function spark(id, arr) {
    var n = arr.length; if (!n) { $(id).setAttribute('points', ''); return; }
    $(id).setAttribute('points', arr.map(function (v, i) { return (n === 1 ? 40 : i * 40 / (n - 1)).toFixed(1) + ',' + (13 - v * 12 / 100).toFixed(1); }).join(' '));
  }
  function showMonitor(m) {
    $('monitor').hidden = !m;
    if (!m) return;
    $('monOs').src = 'icons/os/' + m.os + '.png'; $('monOs').title = m.osp; $('monHost').textContent = m.host;
    $('monCpu').textContent = m.cpu + '%'; $('monRam').textContent = m.ram; $('monUp').textContent = m.up; $('monDown').textContent = m.down;
    $('monUptime').textContent = m.uptime; $('monUser').textContent = m.user; $('monDisk').textContent = '/: ' + m.disk;
    spark('monCpuLine', m.cpuHist); spark('monRamLine', m.ramHist);
  }
  async function monitorTick() {
    var tab = active;
    if (!tab || !tab.ssh || tab.state !== 'connected' || document.hidden || view !== 'vTerminal' || tab.monBusy) return;
    tab.monBusy = true;
    try {
      var out = await tab.ssh.exec(MON_CMD);
      var line = (out || '').split('\n').filter(function (l) { return l.indexOf('MON|') === 0; })[0];
      if (!line) throw new Error('no data');
      var p = line.split('|'), mem = (p[5] || '').split(' ').map(Number), cpu = (p[6] || '').split(' ').map(Number), net = (p[7] || '').split(' ').map(Number);
      var now = Date.now(), m = tab.mon || { cpuHist: [], ramHist: [] }, prev = tab.monPrev, cpuPct = 0, upTxt = '0 Kb/s', downTxt = '0 Kb/s';
      if (prev && cpu.length === 2 && cpu[1] > prev.cpu[1]) cpuPct = Math.max(0, Math.min(100, Math.round((cpu[0] - prev.cpu[0]) * 100 / (cpu[1] - prev.cpu[1]))));
      if (prev && net.length === 2) { var dt = (now - prev.t) / 1000; if (dt > 0) { downTxt = rate(Math.max(0, net[0] - prev.net[0]) / dt); upTxt = rate(Math.max(0, net[1] - prev.net[1]) / dt); } }
      tab.monPrev = { cpu: cpu, net: net, t: now };
      var secs = +p[4] || 0, d = Math.floor(secs / 86400), h = Math.floor(secs % 86400 / 3600), ramPct = mem[1] ? Math.round(mem[0] * 100 / mem[1]) : 0;
      m.cpuHist.push(cpuPct); m.ramHist.push(ramPct);
      if (m.cpuHist.length > 20) { m.cpuHist.shift(); m.ramHist.shift(); }
      m.host = p[1] || tab.vm.host; m.user = p[2] || tab.vm.username; m.osp = p[3] || 'Linux'; m.os = osIcon(p[3]);
      m.cpu = cpuPct; m.ram = mem[1] ? human(mem[0]) + ' / ' + human(mem[1]) : '–'; m.up = upTxt; m.down = downTxt;
      m.uptime = d > 0 ? L('{0} ngày {1} giờ', d, h) : L('{0} giờ', h); m.disk = p[8] || '–';
      var first = !tab.mon; tab.mon = m;
      if (tab === active) { showMonitor(m); if (first) setTimeout(fitActive, 30); }
    } catch (e) { /* máy chủ không phải Linux / không cho chạy lệnh: ẩn thanh, terminal vẫn bình thường */ }
    finally { tab.monBusy = false; }
  }
  function monitorKick() { if (!monTimer) monTimer = setInterval(monitorTick, 3000); monitorTick(); }
  document.addEventListener('visibilitychange', function () { if (!document.hidden) monitorKick(); });

  // ===== SFTP =====
  function fmtSize(n) {
    if (n < 1024) return n + ' B'; if (n < 1048576) return (n / 1024).toFixed(1) + ' KB';
    if (n < 1073741824) return (n / 1048576).toFixed(1) + ' MB'; return (n / 1073741824).toFixed(1) + ' GB';
  }
  function joinPath(dir, name) { return dir === '/' ? '/' + name : dir.replace(/\/+$/, '') + '/' + name; }
  function parentPath(p) { if (p === '/' || !p) return '/'; var i = p.replace(/\/+$/, '').lastIndexOf('/'); return i <= 0 ? '/' : p.slice(0, i); }
  function sftpOk() { return active && active.ssh && active.state === 'connected'; }

  var sftpEntries = [], selMode = false, sel = {};
  var xferCancelled = false;
  var xferBusy = false;
  /** Bắt đầu một tác vụ truyền file; trả false (và báo) nếu đang có tác vụ khác — tránh hai tác vụ chạy chồng lên nhau. */
  function xferStart(text) {
    if (xferBusy) { toast(L('Đang có một tác vụ truyền file — hãy đợi hoặc bấm Hủy.'), 3000); return false; }
    xferBusy = true; xferCancelled = false; $('xfer').hidden = false; $('xferText').textContent = text; $('xferBar').style.width = '0';
    if (active && active.ssh) active.ssh.beginTransfer();
    return true;
  }
  function xferProgress(text, done, total) { if (text) $('xferText').textContent = text; $('xferBar').style.width = total ? Math.min(100, done * 100 / total) + '%' : '0'; }
  function xferEnd() { xferBusy = false; $('xfer').hidden = true; }
  $('xferCancel').addEventListener('click', function () { xferCancelled = true; if (active && active.ssh) active.ssh.cancelTransfers(); $('xferText').textContent = L('Đang hủy…'); });

  function sftpShow() {
    var ok = sftpOk();
    $('sftpEmpty').hidden = !!ok;
    $('sftpEmpty').innerHTML = active ? L('Phiên <b>{0}</b> chưa kết nối.', esc(active.vm.name || active.vm.host)) : L('Chưa có VM nào được kết nối.<br>Chọn VM ở mục <b>Sessions</b>.');
    $('sftpList').hidden = !ok;
    if (!ok) $('selBar').hidden = true;
    if (ok) sftpOpen(active.sftpPath);
  }

  function visibleEntries() { return sftpEntries.filter(function (e) { return settings.showHidden || e.name[0] !== '.'; }); }
  function sftpRender() {
    if (!sftpOk()) return;
    var path = active.sftpPath, list = visibleEntries();
    $('sftpHidden').classList.toggle('on', settings.showHidden);
    $('sftpSelect').classList.toggle('on', selMode);
    $('sftpList').classList.toggle('sel-mode', selMode);
    $('selBar').hidden = !selMode;
    var html = path !== '/' ? '<div class="f" data-up="1"><span class="fn">' + ico('folder', 'dir') + '<span>..</span></span><span class="fs"></span><span class="fp"></span><span></span></div>' : '';
    list.forEach(function (e, i) {
      html += '<div class="f' + (sel[e.name] ? ' sel' : '') + '" data-n="' + esc(e.name) + '"><span class="fn"><span class="chk">' + ico('check') + '</span>' + ico(e.dir ? 'folder' : 'file', e.dir ? 'dir' : 'file') + '<span>' + esc(e.name) + '</span></span>' +
        '<span class="fs">' + (e.dir ? '' : fmtSize(e.size)) + '</span><span class="fp">' + esc((e.mode || '').slice(-9)) + '</span>' +
        '<button class="fm" data-m="' + esc(e.name) + '">' + ico('more') + '</button></div>';
    });
    $('sftpList').innerHTML = html;
    $('selCount').textContent = L('{0} đã chọn', Object.keys(sel).length);
  }

  async function sftpOpen(path) {
    var tab = active; if (!sftpOk()) return;
    setStatus(L('SFTP: đang tải…'));
    try {
      if (!path) path = await tab.ssh.sftpRealpath('.');
      var list = JSON.parse(await tab.ssh.sftpList(path));
      tab.sftpPath = path; sftpEntries = list; sel = {};
      $('sftpPath').value = path;
      sftpRender();
      setStatus(L('SFTP: {0} — {1} mục', path, list.length));
    } catch (e) { setStatus(L('SFTP lỗi: {0}', LT(e.message || String(e)))); }
  }
  function entryByName(n) { return sftpEntries.find(function (e) { return e.name === n; }); }
  function selectedEntries() { return Object.keys(sel).map(entryByName).filter(Boolean); }

  $('sftpRefresh').addEventListener('click', function () { if (active) sftpOpen(active.sftpPath); });
  $('sftpUp').addEventListener('click', function () { if (active) sftpOpen(parentPath(active.sftpPath || '/')); });
  $('sftpHome').addEventListener('click', function () { if (active) sftpOpen(null); });
  $('sftpHidden').addEventListener('click', function () { settings.showHidden = !settings.showHidden; saveSettings(); sftpRender(); });
  $('sftpSelect').addEventListener('click', function () { selMode = !selMode; sel = {}; sftpRender(); });
  $('selAll').addEventListener('click', function () { var all = visibleEntries(); var every = all.every(function (e) { return sel[e.name]; }); sel = {}; if (!every) all.forEach(function (e) { sel[e.name] = true; }); sftpRender(); });
  $('sftpPath').addEventListener('keydown', function (ev) { if (ev.key === 'Enter') { ev.preventDefault(); sftpOpen(this.value.trim() || '/'); this.blur(); } });
  $('sftpPath').addEventListener('blur', function () { if (active && active.sftpPath) this.value = active.sftpPath; });

  $('sftpNewDir').addEventListener('click', async function () {
    if (!sftpOk()) return;
    var r = await modal({ title: L('Thư mục mới'), fields: [{ id: 'n', label: L('Tên thư mục') }], validate: function (f) { return f.n.trim() && f.n.indexOf('/') < 0 ? null : L('Nhập tên hợp lệ (không chứa "/").'); },
      buttons: [{ text: L('Hủy'), value: false }, { text: L('Tạo'), value: true, cls: 'primary' }], cancelValue: false });
    if (!r.value) return;
    try { await active.ssh.sftpMkdir(joinPath(active.sftpPath, r.fields.n.trim())); sftpOpen(active.sftpPath); } catch (e) { toast(L('Lỗi: {0}', LT(e.message || String(e)))); }
  });

  // --- Upload ---
  $('sftpUpload').addEventListener('click', function () { if (sftpOk()) $('sftpFile').click(); });
  $('sftpFile').addEventListener('change', async function () {
    var files = Array.prototype.slice.call(this.files || []); this.value = '';
    if (!files.length || !sftpOk()) return;
    var tab = active, dir = tab.sftpPath, ok = 0, mode = null;
    if (!xferStart(L('Upload…'))) return;
    try {
      for (var i = 0; i < files.length && !xferCancelled; i++) {
        var f = files[i];
        if (sftpEntries.some(function (e) { return e.name === f.name; })) {
          if (mode === null) {
            var r = await modal({ title: L('File đã tồn tại'), html: L('<p><b>{0}</b> đã có trong thư mục này.</p>', esc(f.name)), stack: true,
              buttons: [{ text: L('Ghi đè'), value: 'over', cls: 'danger' }, { text: L('Ghi đè tất cả'), value: 'overall', cls: 'danger' }, { text: L('Bỏ qua'), value: 'skip' }, { text: L('Hủy upload'), value: 'cancel' }], cancelValue: 'cancel' });
            if (r.value === 'cancel') break;
            if (r.value === 'overall') mode = 'over';
            if (r.value === 'skip') continue;
          } else if (mode === 'skip') continue;
        }
        var label = f.name + ' (' + (i + 1) + '/' + files.length + ')';
        xferProgress('Upload ' + label, 0, f.size);
        try { await tab.ssh.sftpWrite(joinPath(dir, f.name), new Uint8Array(await f.arrayBuffer()), function (d, t) { xferProgress('Upload ' + label, d, t); }); ok++; }
        catch (e) { if (xferCancelled) break; toast(L('Upload lỗi {0}: {1}', f.name, LT(e.message || String(e))), 5000); }
      }
    } finally { xferEnd(); }
    toast((xferCancelled ? L('Đã hủy. ') : '') + L('Đã upload {0}/{1} file.', ok, files.length));
    if (active === tab) sftpOpen(dir);
  });

  // --- Tải về ---
  async function saveBlob(file) {
    var share = navigator.canShare && navigator.canShare({ files: [file] });
    if (share) { try { await navigator.share({ files: [file] }); return; } catch (e) { if (e && e.name === 'AbortError') return; } }
    var a = document.createElement('a'); a.href = URL.createObjectURL(file); a.download = file.name;
    document.body.appendChild(a); a.click(); a.remove(); setTimeout(function () { URL.revokeObjectURL(a.href); }, 10000);
  }

  /** Duyệt đệ quy một thư mục/tập file → danh sách mục để nén ZIP. */
  async function collectZip(tab, items, basePath) {
    var entries = [], total = 0, count = 0, LIMIT = 500 * 1024 * 1024;
    async function walk(remote, rel) {
      var list = JSON.parse(await tab.ssh.sftpList(remote));
      entries.push({ name: rel, data: null });
      for (var i = 0; i < list.length; i++) {
        if (xferCancelled) throw new Error(L('đã hủy'));
        var e = list[i], r = joinPath(remote, e.name), rr = rel + '/' + e.name;
        if (e.dir) { if (!e.link) await walk(r, rr); }
        else {
          total += e.size; if (total > LIMIT) throw new Error(L('Tổng dung lượng vượt quá 500 MB — hãy tải từng phần nhỏ hơn.'));
          xferProgress(L('Đang tải {0} file — {1}', ++count, e.name), 0, 0);
          entries.push({ name: rr, data: await tab.ssh.sftpRead(r), mtime: e.mtime * 1000 });
        }
      }
    }
    for (var i = 0; i < items.length; i++) {
      var e = items[i], r = joinPath(basePath, e.name);
      if (e.dir) await walk(r, e.name);
      else { total += e.size; if (total > LIMIT) throw new Error(L('Tổng dung lượng vượt quá 500 MB.')); xferProgress(L('Đang tải {0}', e.name), 0, 0); entries.push({ name: e.name, data: await tab.ssh.sftpRead(r), mtime: e.mtime * 1000 }); }
    }
    return entries;
  }

  async function sftpDownload(items) {
    if (!sftpOk() || !items.length) return;
    var tab = active, base = tab.sftpPath;
    if (!xferStart(L('Đang tải…'))) return;
    try {
      if (items.length === 1 && !items[0].dir) {
        var e = items[0];
        var data = await tab.ssh.sftpRead(joinPath(base, e.name), function (d, t) { xferProgress(L('Tải {0}', e.name), d, t); });
        xferEnd();
        await saveBlob(new File([data], e.name, { type: 'application/octet-stream' }));
        setStatus(L('Đã tải {0} ({1})', e.name, fmtSize(data.length)), 6000);
      } else {
        var entries = await collectZip(tab, items, base);
        xferProgress(L('Đang nén…'), 0, 0);
        var name = (items.length === 1 ? items[0].name : (base.split('/').filter(Boolean).pop() || 'sftp')) + '.zip';
        var blob = WNFile.makeZip(entries); xferEnd();
        await saveBlob(new File([blob], name, { type: 'application/zip' }));
        setStatus(L('Đã tải {0} ({1})', name, fmtSize(blob.size)), 6000);
      }
    } catch (e) { xferEnd(); if (!xferCancelled) toast(L('Tải lỗi: {0}', LT(e.message || String(e))), 6000); else toast(L('Đã hủy.')); }
    xferEnd();
  }

  // --- Sửa văn bản ngay trên máy chủ ---
  async function sftpEdit(e) {
    var tab = active, path = joinPath(tab.sftpPath, e.name);
    if (e.size > 1024 * 1024) return toast(L('File lớn hơn 1 MB — không mở để sửa bằng bản web.'), 4000);
    var text;
    if (!xferStart(L('Đang mở {0}', e.name) + '…')) return;
    try {
      var data = await tab.ssh.sftpRead(path, function (d, t) { xferProgress(L('Đang mở {0}', e.name), d, t); });
      xferEnd();
      if (data.indexOf(0) >= 0) return toast(L('Đây không phải file văn bản.'), 4000);
      text = td.decode(data);
    } catch (err) { xferEnd(); return toast(L('Lỗi: {0}', LT(err.message || String(err))), 5000); }
    var orig = text;
    for (;;) {
      var r = await modal({
        title: e.name, wide: true, noAutofocus: true,
        html: '<p class="muted small">' + esc(path) + '</p>',
        fields: [{ id: 't', type: 'textarea', rows: 16, mono: true, value: text, noFocus: true }],
        onOpen: function (ctx) { var ta = ctx.inputs.t; ta.classList.add('editor'); ta.wrap = 'off'; },
        buttons: [
          { text: L('Đóng'), value: 'close' },
          { text: L('Lưu'), onClick: async function (ctx) {
            var v = ctx.values().t;
            try { await tab.ssh.sftpWrite(path, te.encode(v), null, true); orig = v; text = v; ctx.setInfo(L('Đã lưu {0}', new Date().toLocaleTimeString(WNi18n.locale())) + ' ✓'); }
            catch (err) { ctx.setError(L('Lưu lỗi: {0}', LT(err.message || String(err)))); }
          } }
        ]
      });
      var cur = r.fields.t;
      if (cur !== orig && !(await UI.confirm(L('Đóng không lưu?'), L('<p>Bạn có thay đổi chưa lưu trong <b>{0}</b>.</p>', esc(e.name)), L('Đóng không lưu'), true))) { text = cur; continue; }
      break;
    }
    if (active === tab) sftpOpen(tab.sftpPath);
  }

  // --- chmod ---
  function modeToOctal(mode) {
    var m = (mode || '').slice(-9), v = 0, bits = 'rwxrwxrwx';
    for (var i = 0; i < 9; i++) if (m[i] && m[i] !== '-' && bits[i] === (m[i] === 's' || m[i] === 't' ? 'x' : m[i])) v |= 1 << (8 - i);
    return v;
  }
  async function chmodDialog(items) {
    var init = modeToOctal(items[0].mode), hasDir = items.some(function (e) { return e.dir; });
    var rows = [L('Chủ sở hữu'), L('Nhóm'), L('Người khác')], grid = L('<div class="permgrid"><span></span><span class="h">Đọc</span><span class="h">Ghi</span><span class="h">Chạy</span>');
    for (var i = 0; i < 3; i++) for (var j = -1; j < 3; j++) grid += j < 0 ? '<span>' + rows[i] + '</span>' : '<input type="checkbox" data-r="' + i + '" data-b="' + j + '">';
    grid += '</div>';
    var r = await modal({
      title: L('Đổi quyền (chmod) — {0}', items.length === 1 ? items[0].name : L('{0} mục', items.length)), html: grid,
      fields: [{ id: 'oct', label: L('Dạng số (ví dụ 755, 644)'), value: init.toString(8).padStart(3, '0'), inputmode: 'numeric' }].concat(hasDir ? [{ id: 'rec', type: 'checkbox', label: L('Áp dụng cho cả thư mục con và file bên trong'), value: false }] : []),
      validate: function (f) { return /^[0-7]{3,4}$/.test(f.oct) ? null : L('Nhập quyền dạng số bát phân, ví dụ 755.'); },
      onOpen: function (ctx) {
        var boxes = ctx.body.querySelectorAll('.permgrid input'), oct = ctx.inputs.oct;
        function fromOct() { var v = parseInt(oct.value, 8) || 0; boxes.forEach(function (b) { var bit = 8 - (parseInt(b.dataset.r, 10) * 3 + parseInt(b.dataset.b, 10)); b.checked = !!(v & (1 << bit)); }); }
        function fromBoxes() { var v = 0; boxes.forEach(function (b) { if (b.checked) v |= 1 << (8 - (parseInt(b.dataset.r, 10) * 3 + parseInt(b.dataset.b, 10))); }); oct.value = v.toString(8).padStart(3, '0'); }
        boxes.forEach(function (b) { b.onchange = fromBoxes; }); oct.oninput = fromOct; fromOct();
      },
      buttons: [{ text: L('Hủy'), value: false }, { text: L('Áp dụng'), value: true, cls: 'primary' }], cancelValue: false
    });
    if (!r.value) return;
    var mode = r.fields.oct, tab = active, ok = 0, fail = 0, first = '';
    if (!xferStart(L('Đang đổi quyền…'))) return;
    async function apply(path) { try { await tab.ssh.sftpChmod(path, mode); ok++; } catch (e) { fail++; first = first || (e.message || String(e)); } }
    async function walk(path) {
      var list = JSON.parse(await tab.ssh.sftpList(path));
      for (var i = 0; i < list.length && !xferCancelled; i++) {
        var e = list[i], p = joinPath(path, e.name);
        xferProgress('chmod ' + mode + ' — ' + p, 0, 0);
        await apply(p); if (e.dir && !e.link) await walk(p);
      }
    }
    try {
      for (var k = 0; k < items.length && !xferCancelled; k++) {
        var p = joinPath(tab.sftpPath, items[k].name); await apply(p);
        if (items[k].dir && r.fields.rec) await walk(p);
      }
    } finally { xferEnd(); }
    toast(fail ? L('Đã đổi {0}, lỗi {1}: {2}', ok, fail, LT(first)) : L('Đã đổi quyền {0} mục.', ok), fail ? 6000 : 3000);
    sftpOpen(tab.sftpPath);
  }

  async function sftpDelete(items) {
    var names = items.map(function (e) { return esc(e.name); });
    var ok = await UI.confirm(L('Xóa trên máy chủ?'), L('<p>Xóa vĩnh viễn {0}', items.length === 1 ? '<b>' + names[0] + '</b>' : L('<b>{0}</b> mục', items.length)) +
      (items.some(function (e) { return e.dir; }) ? L(' (thư mục sẽ bị xóa cùng mọi thứ bên trong)') : '') + L(' trên máy chủ? Không hoàn tác được.</p>'), L('Xóa'), true);
    if (!ok) return;
    var tab = active, done = 0, failed = 0, firstErr = '';
    if (!xferStart(L('Đang xóa…'))) return;
    try {
      for (var i = 0; i < items.length && !xferCancelled; i++) {
        xferProgress(L('Xóa {0} ({1}/{2})', items[i].name, i + 1, items.length), i, items.length);
        try { await tab.ssh.sftpRemove(joinPath(tab.sftpPath, items[i].name)); done++; } catch (e) { failed++; firstErr = firstErr || (e.message || String(e)); }
      }
    } finally { xferEnd(); }
    toast(failed ? L('Đã xóa {0}, lỗi {1}: {2}', done, failed, LT(firstErr)) : L('Đã xóa {0} mục.', done), failed ? 6000 : 3000);
    selMode = false; sftpOpen(tab.sftpPath);
  }

  async function sftpMenu(e) {
    var p = joinPath(active.sftpPath, e.name);
    var menu = e.dir ? [{ text: L('Mở thư mục'), value: 'open', icon: 'folder' }, { text: L('Tải về (nén ZIP)'), value: 'dl', icon: 'download' }]
      : [{ text: L('Tải về'), value: 'dl', icon: 'download' }, { text: L('Sửa văn bản'), value: 'edit', icon: 'edit' }];
    menu = menu.concat([{ text: L('Đổi tên'), value: 'ren', icon: 'edit' }, { text: L('Đổi quyền (chmod)'), value: 'chmod', icon: 'key' }, { text: L('Sao chép đường dẫn'), value: 'copy', icon: 'copy' }, { text: L('Xóa'), value: 'del', icon: 'trash', cls: 'danger' }]);
    var r = await modal({ title: e.name, html: '<p class="muted small">' + esc((e.mode || '') + (e.dir ? '' : ' · ' + fmtSize(e.size)) + ' · ' + new Date(e.mtime * 1000).toLocaleString(WNi18n.locale())) + '</p>', menu: menu });
    try {
      if (r.value === 'open') sftpOpen(p);
      else if (r.value === 'dl') sftpDownload([e]);
      else if (r.value === 'edit') sftpEdit(e);
      else if (r.value === 'chmod') chmodDialog([e]);
      else if (r.value === 'copy') { await navigator.clipboard.writeText(p); toast(L('Đã sao chép.')); }
      else if (r.value === 'ren') {
        var n = await modal({ title: L('Đổi tên'), fields: [{ id: 'n', label: L('Tên mới'), value: e.name }], validate: function (f) { return f.n.trim() && f.n.indexOf('/') < 0 ? null : L('Tên không hợp lệ.'); },
          buttons: [{ text: L('Hủy'), value: false }, { text: L('Đổi tên'), value: true, cls: 'primary' }], cancelValue: false });
        if (n.value && n.fields.n.trim() !== e.name) { await active.ssh.sftpRename(p, joinPath(active.sftpPath, n.fields.n.trim())); sftpOpen(active.sftpPath); }
      } else if (r.value === 'del') sftpDelete([e]);
    } catch (err) { toast(L('Lỗi: {0}', LT(err.message || String(err))), 5000); }
  }

  $('sftpList').addEventListener('click', function (ev) {
    var m = ev.target.closest('[data-m]');
    if (m) { ev.stopPropagation(); var me = entryByName(m.dataset.m); if (me) sftpMenu(me); return; }
    var row = ev.target.closest('.f'); if (!row || !sftpOk()) return;
    if (row.dataset.up) return sftpOpen(parentPath(active.sftpPath));
    var e = entryByName(row.dataset.n); if (!e) return;
    if (selMode) { if (sel[e.name]) delete sel[e.name]; else sel[e.name] = true; return sftpRender(); }
    if (e.dir) sftpOpen(joinPath(active.sftpPath, e.name)); else sftpMenu(e);
  });
  longPress($('sftpList'), '.f[data-n]', function (row) { var e = entryByName(row.dataset.n); if (!e) return; selMode = true; sel = {}; sel[e.name] = true; sftpRender(); });
  $('selDownload').addEventListener('click', function () { var s = selectedEntries(); if (s.length) sftpDownload(s); else toast(L('Chọn mục cần tải.')); });
  $('selChmod').addEventListener('click', function () { var s = selectedEntries(); if (s.length) chmodDialog(s); else toast(L('Chọn mục cần đổi quyền.')); });
  $('selDelete').addEventListener('click', function () { var s = selectedEntries(); if (s.length) sftpDelete(s); else toast(L('Chọn mục cần xóa.')); });

  // Hỗ trợ chẩn đoán/kiểm thử: chỉ đọc trạng thái, không chứa dữ liệu bí mật nào ngoài những gì đang hiện trên màn hình.
  window.__wnweb = {
    screen: function () {
      if (!active) return '';
      var b = active.term.buffer.active, out = [];
      for (var i = Math.max(0, b.length - 40); i < b.length; i++) { var l = b.getLine(i); if (l) out.push(l.translateToString(true)); }
      return out.join('\n').replace(/\n+$/, '');
    },
    state: function () { return tabs.map(function (t) { return t.vm.name + ':' + t.state; }); },
    type: function (text) { if (active) sendInput(active, text); },
    ssh: function () { return active && active.ssh; },
    vault: function () { return vault; },
    tab: function () { return active; },
    sync: function () { return syncNow(false); }
  };

  // ===== Khởi động =====
  var standalone = window.matchMedia('(display-mode: standalone)').matches || navigator.standalone;
  if (/iPhone|iPad|iPod/.test(navigator.userAgent) && !standalone) $('installTip').hidden = false;
  if ('serviceWorker' in navigator && location.protocol === 'https:') navigator.serviceWorker.register('sw.js').catch(function () {});
  window.addEventListener('beforeunload', function (ev) {
    if (tabs.some(function (t) { return t.state === 'connected'; }) || (vault && vault.state.dirty)) { ev.preventDefault(); ev.returnValue = ''; }
  });
  store.del('vault');   // bản cũ (trước khi có kho VM cục bộ)

  if (auth && auth.token && auth.vaultKey) startSession(false);
  else screen('login');
})();
