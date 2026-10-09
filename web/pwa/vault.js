/*
 * WNVault — kho VM cục bộ của bản web + đồng bộ backup với máy chủ WN Term.
 * Cùng quy tắc với AccountService.cs của app (Windows/Android) để ba nền tảng dùng chung một tài khoản:
 *   - mỗi VM có updatedAt; xóa VM → vào thùng rác (30 ngày) và để lại "dấu xóa" (180 ngày) để các máy khác biết;
 *   - gộp theo từng VM: sự kiện MỚI NHẤT thắng (sửa hoặc xóa), hòa nhau thì giữ VM;
 *   - cùng một VM tạo riêng ở hai máy (trùng tên + host + port + user) được coi là một;
 *   - máy chủ giữ lịch sử phiên bản để khôi phục.
 * Dữ liệu trong blob (JSON, mã hóa AES-GCM bằng khóa vault): { version:2, savedAt, device, sessions:[...], deleted:[{id,at,name}],
 *   snippets:[{id,name,command,vmId|null,autoEnter,createdAt,updatedAt}], deletedSnippets:[{id,deletedAt}] }.
 * Snippet (lệnh lưu sẵn) dùng CHUNG bộ quy tắc gộp với VM (pickWinner/tombMap/pruneTombs): mỗi snippet có updatedAt, xóa → dấu xóa 180 ngày;
 * sự kiện mới nhất thắng; hòa nhau thì bản ở máy này > bản máy chủ > dấu xóa. Trường snippets/deletedSnippets vắng = rỗng (bản cũ không xóa snippet của máy này).
 */
(function (global) {
  'use strict';
  var L = (global.WNi18n && global.WNi18n.t) || function (s) { return s; }, LT = (global.WNi18n && global.WNi18n.tr) || function (s) { return s; };

  var DAY = 86400000, KEEP_DAYS = 30, TOMB_DAYS = 180;
  var enc = new TextEncoder(), dec = new TextDecoder();

  // ===== Tiện ích =====
  function uuid() {
    if (global.crypto && crypto.randomUUID) return crypto.randomUUID().toLowerCase();
    var b = crypto.getRandomValues(new Uint8Array(16)); b[6] = (b[6] & 15) | 64; b[8] = (b[8] & 63) | 128;
    var h = Array.prototype.map.call(b, function (x) { return ('0' + x.toString(16)).slice(-2); }).join('');
    return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
  }

  /** Đọc thời gian ISO (kể cả C# với 7 chữ số lẻ giây / không có 'Z') → ms. */
  function parseTime(s) {
    if (!s) return 0;
    var m = String(s).match(/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:?\d{2})?$/);
    if (!m) { var p = Date.parse(s); return isNaN(p) ? 0 : p; }
    var frac = ((m[2] || '0') + '000').slice(0, 3);
    var v = Date.parse(m[1] + '.' + frac + (m[3] || 'Z'));
    return isNaN(v) ? 0 : v;
  }
  function iso(ms) { return new Date(ms).toISOString(); }
  function nowIso() { return new Date().toISOString(); }

  function isValid(v) {
    return !!(v && String(v.host || '').trim() && String(v.username || '').trim() && v.port >= 1 && v.port <= 65535);
  }

  /** Chuẩn hóa một VM (giữ nguyên chuỗi thời gian gốc để không làm lệch bản của app). */
  function clean(v) {
    return {
      id: String(v.id || '').toLowerCase(),
      name: v.name || '', group: v.group || '',
      tags: Array.isArray(v.tags) ? v.tags.map(String) : [],
      pinned: !!v.pinned,
      host: String(v.host || '').trim(), port: parseInt(v.port, 10) || 22, username: String(v.username || '').trim(),
      password: v.password || null, passphrase: v.passphrase || null,
      keyFileName: v.keyFileName || null, keyFileContent: v.keyFileContent || null,
      createdAt: v.createdAt || null, updatedAt: v.updatedAt || null
    };
  }
  function effUpdated(s) { return parseTime(s.updatedAt) || parseTime(s.createdAt) || 0; }
  function displayName(s) { return s.name && s.name.trim() ? s.name : s.username + '@' + s.host; }
  function twinKey(s) { return [(s.name || '').trim().toLowerCase(), s.host.trim().toLowerCase(), s.port, s.username.trim().toLowerCase()].join('|'); }
  function sameTags(a, b) { return a.length === b.length && a.every(function (x, i) { return x === b[i]; }); }
  function contentKey(s) { return JSON.stringify([s.name, s.group, s.tags, s.pinned, s.host, s.port, s.username, s.password, s.passphrase, s.keyFileContent]); }

  function toVaultSession(s) {
    return {
      id: s.id, name: s.name, group: s.group, tags: s.tags.slice(), pinned: s.pinned,
      host: s.host, port: s.port, username: s.username, password: s.password, passphrase: s.passphrase,
      keyFileName: s.keyFileName, keyFileContent: s.keyFileContent,
      createdAt: s.createdAt, updatedAt: s.updatedAt || s.createdAt
    };
  }

  /** Chép nội dung VM từ vault vào bản ghi cục bộ; trả true nếu có khác biệt về nội dung. */
  function apply(v, s) {
    var changed = s.name !== v.name || s.group !== v.group || s.host !== v.host || s.port !== v.port ||
      s.username !== v.username || s.pinned !== v.pinned || !sameTags(s.tags, v.tags) ||
      s.password !== v.password || s.passphrase !== v.passphrase ||
      s.keyFileContent !== v.keyFileContent || s.keyFileName !== v.keyFileName;
    s.name = v.name; s.group = v.group; s.tags = v.tags.slice(); s.pinned = v.pinned;
    s.host = v.host; s.port = v.port; s.username = v.username;
    s.password = v.password; s.passphrase = v.passphrase; s.keyFileName = v.keyFileName; s.keyFileContent = v.keyFileContent;
    if (v.createdAt) s.createdAt = v.createdAt;
    s.updatedAt = v.updatedAt || v.createdAt || s.updatedAt || null;
    return changed;
  }

  // ----- Lõi gộp dùng chung cho VM và snippet -----
  /** Gom dấu xóa theo id (chữ thường), giữ cái mới nhất: { id: {ms, str} }. atKey: tên trường thời gian ('at' | 'deletedAt'). */
  function tombMap(list, atKey) {
    var m = {};
    (list || []).forEach(function (t) {
      var id = String(t.id).toLowerCase(), ms = parseTime(t[atKey]);
      if (!m[id] || ms > m[id].ms) m[id] = { ms: ms, str: t[atKey] };
    });
    return m;
  }
  /** Sự kiện mới nhất cho một id: l/r = mục ở máy này / máy chủ (hoặc null), lt/rt = dấu xóa {ms,str} (hoặc null).
   *  Hòa thời điểm: xem rk bên dưới. → {kind:'L'|'R'|'l'|'r', str?} */
  function pickWinner(l, r, lt, rt, tombWins) {
    // thứ tự thắng khi hòa: VM = bản local > bản remote > dấu xóa; snippet (tombWins) = dấu xóa local > dấu xóa remote > bản local > bản remote
    var rk = tombWins ? { L: 1, R: 0, l: 3, r: 2 } : { L: 3, R: 2, l: 1, r: 0 };
    var cands = [];
    if (l) cands.push({ at: effUpdated(l), rank: rk.L, kind: 'L' });
    if (r) cands.push({ at: effUpdated(r), rank: rk.R, kind: 'R' });
    if (lt) cands.push({ at: lt.ms, rank: rk.l, kind: 'l', str: lt.str });
    if (rt) cands.push({ at: rt.ms, rank: rk.r, kind: 'r', str: rt.str });
    cands.sort(function (a, b) { return b.at - a.at || b.rank - a.rank; });
    return cands[0];
  }
  /** Dấu xóa còn hiệu lực: bỏ id đang sống hoặc quá hạn (180 ngày), mỗi id giữ cái mới nhất → { id: bản ghi gốc }. */
  function pruneTombs(live, tombstones, atKey) {
    var best = {};
    tombstones.forEach(function (t) {
      var id = String(t.id).toLowerCase(), ms = parseTime(t[atKey]);
      if (live[id] || Date.now() - ms > TOMB_DAYS * DAY) return;
      if (!best[id] || ms > parseTime(best[id][atKey])) best[id] = t;
    });
    return best;
  }

  // ----- Snippet (lệnh lưu sẵn) -----
  function cleanSnip(v) {
    return {
      id: String(v.id || '').toLowerCase(), name: v.name || '', command: v.command == null ? '' : String(v.command),
      vmId: v.vmId ? String(v.vmId).toLowerCase() : null, autoEnter: v.autoEnter == null ? true : !!v.autoEnter,
      createdAt: v.createdAt || null, updatedAt: v.updatedAt || null
    };
  }
  function isValidSnip(v) { return !!(v && String(v.id || '').trim()); }
  function snipKey(s) { return JSON.stringify([s.name, s.command, s.vmId, s.autoEnter]); }
  function byId(a, b) { return a.id < b.id ? -1 : a.id > b.id ? 1 : 0; }
  function cleanSnips(list) { var seen = {}; return (list || []).map(cleanSnip).filter(function (s) { if (!isValidSnip(s) || seen[s.id]) return false; seen[s.id] = 1; return true; }); }
  function cleanSnipTombs(list) { return (list || []).filter(function (t) { return t && t.id; }).map(function (t) { return { id: String(t.id).toLowerCase(), deletedAt: t.deletedAt }; }); }

  /** Gộp snippet: local (đã chuẩn hóa) + dấu xóa cục bộ + vault từ máy chủ → { snippets, tombs, changed }. Cùng quy tắc với gộp VM. */
  function mergeSnippets(localSnips, localTombs, remote) {
    var localById = {}; localSnips.forEach(function (s) { localById[s.id] = s; });
    var remoteById = {}; cleanSnips(remote && remote.snippets).forEach(function (s) { remoteById[s.id] = s; });
    var lt = tombMap(localTombs, 'deletedAt'), rt = tombMap(remote && remote.deletedSnippets, 'deletedAt');
    var seen = {}, ids = [], out = [], tombs = [], added = 0, updated = 0, deleted = 0;
    [localById, lt, remoteById, rt].forEach(function (m) { Object.keys(m).forEach(function (k) { if (!seen[k]) { seen[k] = 1; ids.push(k); } }); });
    ids.forEach(function (id) {
      var l = localById[id] || null, r = remoteById[id] || null, w = pickWinner(l, r, lt[id], rt[id], true);
      if (w.kind === 'L') out.push(l);
      else if (w.kind === 'R') { out.push(r); if (!l) added++; else if (snipKey(l) !== snipKey(r) || l.updatedAt !== r.updatedAt) updated++; }
      else { tombs.push({ id: id, deletedAt: w.str }); if (l) deleted++; }
    });
    var live = {}; out.forEach(function (s) { live[s.id] = 1; });
    var best = pruneTombs(live, tombs, 'deletedAt');
    return {
      snippets: out.sort(byId),
      tombs: Object.keys(best).sort().map(function (k) { return { id: k, deletedAt: best[k].deletedAt }; }),
      added: added, updated: updated, deleted: deleted, changed: added + updated + deleted > 0
    };
  }

  var KNOWN = { version: 1, savedAt: 1, device: 1, sessions: 1, deleted: 1, snippets: 1, deletedSnippets: 1 };
  /** Trường cấp cao mà bản này không hiểu (do app/phiên bản khác thêm) — giữ nguyên khi đẩy lên lại. */
  function extrasOf(d) { var x = {}, any = false; Object.keys(d || {}).forEach(function (k) { if (!KNOWN[k]) { x[k] = d[k]; any = true; } }); return any ? x : null; }
  function buildVault(sessions, tombstones, device, snips, snipTombs, extra) {
    var live = {}; sessions.forEach(function (s) { live[s.id] = true; });
    var best = pruneTombs(live, tombstones, 'at');
    var sl = {}; (snips || []).forEach(function (s) { sl[s.id] = true; });
    var sb = pruneTombs(sl, snipTombs || [], 'deletedAt');
    var out = {
      version: 2, savedAt: nowIso(), device: device || null,
      sessions: sessions.map(toVaultSession),
      deleted: Object.keys(best).sort().map(function (k) { return { id: k, at: best[k].at, name: best[k].name || '' }; }),
      snippets: (snips || []).slice().sort(byId).map(cleanSnip),
      deletedSnippets: Object.keys(sb).sort().map(function (k) { return { id: k, deletedAt: sb[k].deletedAt }; })
    };
    if (extra) Object.keys(extra).forEach(function (k) { if (!(k in out)) out[k] = extra[k]; });
    return out;
  }

  /** Hai bản vault có cùng nội dung không (bỏ qua savedAt/device) — để khỏi đẩy lên bản trùng. */
  function sameContent(a, b) {
    function key(d) {
      var ss = (d.sessions || []).map(clean).filter(isValid).sort(function (x, y) { return x.id < y.id ? -1 : x.id > y.id ? 1 : 0; });
      var dd = (d.deleted || []).map(function (t) { return { id: String(t.id).toLowerCase(), at: t.at }; })
        .sort(function (x, y) { return x.id < y.id ? -1 : x.id > y.id ? 1 : 0; });
      var sn = cleanSnips(d.snippets).sort(byId);
      var sd = cleanSnipTombs(d.deletedSnippets).sort(byId);
      return JSON.stringify({ s: ss, d: dd, n: sn, nd: sd });
    }
    return key(a) === key(b);
  }

  // ===== Lớp Vault =====
  /**
   * opts: { api(method, path, body) → Promise<{ok, _status, ...}>, WN, key:Uint8Array, device:string,
   *         persist(string), restore() → string|null }
   */
  function Vault(opts) {
    this.o = opts;
    this.state = { sessions: [], trash: [], snippets: [], snipTombs: [], version: 0, lastSync: null, dirty: false };
  }

  Vault.prototype._call = async function (method, path, body) {
    var r;
    try { r = await this.o.api(method, path, body); }
    catch (e) { var ne = new Error(L('Không kết nối được máy chủ.')); ne.status = 0; ne.code = 'network'; throw ne; }
    if (!r.ok) { var e2 = new Error(LT(r.message) || L('Lỗi máy chủ ({0}).', r._status)); e2.status = r._status; e2.code = r.error || ''; e2.data = r; throw e2; }
    return r;
  };

  // ----- Lưu cục bộ (mã hóa bằng khóa vault) -----
  Vault.prototype.load = async function () {
    try {
      var b = this.o.restore && this.o.restore();
      if (!b) return false;
      var plain = await this.o.WN.open(this.o.key, b);
      if (!plain) return false;
      var st = JSON.parse(dec.decode(plain));
      this.state = {
        sessions: (st.sessions || []).map(clean).filter(isValid),
        trash: (st.trash || []).map(function (e) { return { id: e.id, name: e.name || '', subtitle: e.subtitle || '', deletedAt: e.deletedAt, localOnly: !!e.localOnly, session: e.session ? clean(e.session) : null }; }),
        snippets: cleanSnips(st.snippets), snipTombs: cleanSnipTombs(st.snipTombs), extra: st.extra || null,
        version: st.version || 0, lastSync: st.lastSync || null, dirty: !!st.dirty
      };
      this._purgeTrash(); this._purgeSnipTombs();
      return true;
    } catch (e) { return false; }
  };

  Vault.prototype.save = async function () {
    try {
      var b = await this.o.WN.seal(this.o.key, enc.encode(JSON.stringify(this.state)));
      this.o.persist(b);
    } catch (e) { /* localStorage đầy/bị chặn: bỏ qua, lần đồng bộ sau sẽ tải lại */ }
  };

  Vault.prototype.clear = function () {
    this.state = { sessions: [], trash: [], snippets: [], snipTombs: [], version: 0, lastSync: null, dirty: false };
    try { this.o.persist(null); } catch (e) {}
  };

  Vault.prototype._purgeTrash = function () {
    var now = Date.now();
    this.state.trash.forEach(function (e) { if (e.session && now - parseTime(e.deletedAt) > KEEP_DAYS * DAY) e.session = null; });
    this.state.trash = this.state.trash.filter(function (e) {
      return now - parseTime(e.deletedAt) <= TOMB_DAYS * DAY && !(e.session == null && e.localOnly);
    });
  };

  Vault.prototype._purgeSnipTombs = function () {
    var now = Date.now();
    this.state.snipTombs = this.state.snipTombs.filter(function (t) { return now - parseTime(t.deletedAt) <= TOMB_DAYS * DAY; });
  };

  // ----- Snippet (lệnh lưu sẵn): toàn cục (vmId null) hoặc riêng từng VM -----
  Vault.prototype.snippets = function () { return this.state.snippets; };
  Vault.prototype.findSnippet = function (id) { return this.state.snippets.find(function (s) { return s.id === id; }) || null; };
  Vault.prototype.addSnippet = function (f) {
    var s = cleanSnip(Object.assign({}, f, { id: uuid() }));
    s.createdAt = nowIso(); s.updatedAt = s.createdAt;
    this.state.snippets.push(s);
    this._touch(); return s;
  };
  Vault.prototype.updateSnippet = function (id, f) {
    var s = this.findSnippet(id); if (!s) return null;
    var before = snipKey(s);
    var n = cleanSnip(Object.assign({}, s, f, { id: s.id, createdAt: s.createdAt, updatedAt: s.updatedAt }));
    Object.keys(n).forEach(function (k) { s[k] = n[k]; });
    if (snipKey(s) !== before) { s.updatedAt = nowIso(); this._touch(); }
    return s;
  };
  Vault.prototype.removeSnippet = function (id) {
    var s = this.findSnippet(id); if (!s) return false;
    this.state.snippets = this.state.snippets.filter(function (x) { return x.id !== id; });
    this.state.snipTombs = this.state.snipTombs.filter(function (t) { return t.id !== id; });
    this.state.snipTombs.push({ id: id, deletedAt: nowIso() });
    this._touch(); return true;
  };

  // ----- Truy vấn -----
  Vault.prototype.list = function () { return this.state.sessions; };
  Vault.prototype.find = function (id) { return this.state.sessions.find(function (s) { return s.id === id; }) || null; };
  Vault.prototype.trashItems = function () {
    this._purgeTrash();
    return this.state.trash.filter(function (e) { return e.session; }).sort(function (a, b) { return parseTime(b.deletedAt) - parseTime(a.deletedAt); });
  };
  Vault.prototype.tags = function () {
    var c = {}; this.state.sessions.forEach(function (s) { s.tags.forEach(function (t) { c[t] = (c[t] || 0) + 1; }); }); return c;
  };

  // ----- Thao tác của người dùng -----
  Vault.prototype._touch = function () { this.state.dirty = true; this.save(); if (this.onChange) this.onChange(); if (this.onUserChange) this.onUserChange(); };

  Vault.prototype.add = function (f) {
    var s = clean(Object.assign({ id: uuid() }, f));
    s.createdAt = nowIso(); s.updatedAt = s.createdAt;
    this.state.sessions.push(s);
    this.state.trash = this.state.trash.filter(function (e) { return e.id !== s.id; });
    this._touch(); return s;
  };

  Vault.prototype.update = function (id, f) {
    var s = this.find(id); if (!s) return null;
    var before = contentKey(s), pk = [s.keyFileName];
    var n = clean(Object.assign({}, s, f, { id: s.id, createdAt: s.createdAt, updatedAt: s.updatedAt }));
    Object.keys(n).forEach(function (k) { s[k] = n[k]; });
    if (contentKey(s) !== before || pk[0] !== s.keyFileName) { s.updatedAt = nowIso(); this._touch(); }
    return s;
  };

  Vault.prototype.duplicate = function (id) {
    var s = this.find(id); if (!s) return null;
    return this.add(Object.assign({}, s, { id: uuid(), name: (s.name ? s.name + ' ' : '') + L('(bản sao)') }));
  };

  Vault.prototype.remove = function (ids) {
    var set = {}; ids.forEach(function (i) { set[i] = 1; });
    var now = nowIso(), self = this, removed = 0;
    this.state.sessions = this.state.sessions.filter(function (s) {
      if (!set[s.id]) return true;
      self.state.trash = self.state.trash.filter(function (e) { return e.id !== s.id; });
      self.state.trash.push({ id: s.id, name: displayName(s), subtitle: s.username + '@' + s.host, deletedAt: now, localOnly: false, session: s });
      removed++; return false;
    });
    if (removed) this._touch();
    return removed;
  };

  Vault.prototype.restoreFromTrash = function (ids) {
    var set = {}; ids.forEach(function (i) { set[i] = 1; });
    var n = 0, self = this;
    this.state.trash = this.state.trash.filter(function (e) {
      if (!set[e.id] || !e.session) return true;
      e.session.updatedAt = nowIso();            // mới hơn mọi dấu xóa → các máy khác cũng nhận lại
      self.state.sessions = self.state.sessions.filter(function (s) { return s.id !== e.id; });
      self.state.sessions.push(e.session); n++; return false;
    });
    if (n) this._touch();
    return n;
  };

  Vault.prototype.forget = function (ids) {
    var set = {}; ids.forEach(function (i) { set[i] = 1; });
    this.state.trash.forEach(function (e) { if (set[e.id]) e.session = null; });
    this._purgeTrash(); this.save();
  };

  /** Đổi tên tag ở mọi VM (dùng cho "đổi tên nhóm"). */
  Vault.prototype.renameTag = function (oldTag, newTag) {
    var n = 0, now = nowIso();
    this.state.sessions.forEach(function (s) {
      var i = s.tags.indexOf(oldTag); if (i < 0) return;
      s.tags[i] = newTag;
      s.tags = s.tags.filter(function (t, j) { return s.tags.indexOf(t) === j; });
      s.updatedAt = now; n++;
    });
    if (n) this._touch();
    return n;
  };

  /**
   * Nhập danh sách VM (từ file .wnterm / MobaXterm). resolution: 'skip' | 'overwrite' | 'copy'.
   * items: [{id,name,group,tags,host,port,username,password,passphrase,keyFileName,keyFileContent}]
   */
  Vault.prototype.importItems = function (items, resolution) {
    var res = { total: items.length, imported: 0, overwritten: 0, skipped: 0, copies: 0 }, self = this, now = nowIso();
    items.forEach(function (it) {
      if (!isValid(it)) { res.skipped++; return; }
      var c = clean(it);
      var ex = self.state.sessions.find(function (s) {
        return s.id === c.id || (s.host.toLowerCase() === c.host.toLowerCase() && s.port === c.port &&
          s.username.toLowerCase() === c.username.toLowerCase() && (s.name || '').toLowerCase() === (c.name || '').toLowerCase());
      });
      if (ex) {
        if (resolution === 'overwrite') {
          ex.name = c.name; ex.group = c.group; ex.tags = c.tags; ex.host = c.host; ex.port = c.port; ex.username = c.username;
          if (c.password != null) ex.password = c.password;
          if (c.passphrase != null) ex.passphrase = c.passphrase;
          if (c.keyFileContent) { ex.keyFileContent = c.keyFileContent; ex.keyFileName = c.keyFileName; }
          ex.updatedAt = now; res.overwritten++;
        } else if (resolution === 'copy') {
          c.id = uuid(); c.name = (c.name || '') + L(' (nhập)'); c.createdAt = now; c.updatedAt = now;
          self.state.sessions.push(c); res.copies++; res.imported++;
        } else res.skipped++;
        return;
      }
      if (!c.id) c.id = uuid();
      c.createdAt = now; c.updatedAt = now;
      self.state.trash = self.state.trash.filter(function (e) { return e.id !== c.id; });
      self.state.sessions.push(c); res.imported++;
    });
    if (res.imported || res.overwritten) this._touch();
    return res;
  };

  // ----- Máy chủ -----
  Vault.prototype._pull = async function (version) {
    var r = await this._call('GET', version ? '/vault?version=' + version : '/vault');
    if (!r.version || !r.blob) return { version: r.version || 0, current: r.current || 0, data: null };
    var plain = await this.o.WN.open(this.o.key, r.blob);
    if (!plain) { var e = new Error(L('Không giải mã được dữ liệu đồng bộ (sai khóa).')); e.code = 'bad_key'; e.status = 0; throw e; }
    var data = JSON.parse(dec.decode(plain));
    data.sessions = data.sessions || []; data.deleted = data.deleted || [];
    // snippets / deletedSnippets có thể vắng (bản backup cũ) — giữ nguyên trạng 'vắng' để biết đó là bản cũ; chỗ dùng tự coi như rỗng
    return { version: r.version, current: r.current || r.version, data: data };
  };

  Vault.prototype._push = async function (data, baseVersion) {
    var blob = await this.o.WN.seal(this.o.key, enc.encode(JSON.stringify(data)));
    var r = await this._call('PUT', '/vault', { baseVersion: baseVersion, blob: blob });
    return r.version;
  };

  Vault.prototype._markSynced = function (ver) {
    this.state.version = ver; this.state.lastSync = nowIso(); this.state.dirty = false; this.save();
  };

  /** Gộp bản trên máy chủ vào máy này (bản sao của MergeIntoLocal trong AccountService.cs). */
  Vault.prototype._merge = function (remote) {
    var st = this.state, local = st.sessions, trash = st.trash;
    var localById = {}; local.forEach(function (s) { localById[s.id] = s; });
    var localTomb = tombMap(trash.filter(function (e) { return !e.localOnly; }), 'deletedAt');
    var remoteLive = {};
    ((remote && remote.sessions) || []).forEach(function (v) { var c = clean(v); if (isValid(c) && !remoteLive[c.id]) remoteLive[c.id] = c; });
    var remoteTomb = tombMap(remote && remote.deleted, 'at'), names = {};
    ((remote && remote.deleted) || []).forEach(function (t) { var id = String(t.id).toLowerCase(); if (!(id in names)) names[id] = t.name || ''; });
    trash.forEach(function (e) { names[e.id] = e.name; });

    // Cùng một VM tạo riêng ở hai máy (khác Id, trùng tên + host + port + user) → dùng Id của máy chủ.
    var idsChanged = false, groups = {};
    Object.keys(remoteLive).forEach(function (id) {
      if (localById[id] || localTomb[id]) return;
      var k = twinKey(remoteLive[id]); (groups[k] = groups[k] || []).push(remoteLive[id]);
    });
    local.slice().forEach(function (s) {
      if (remoteLive[s.id] || remoteTomb[s.id]) return;
      var g = groups[twinKey(s)]; if (!g || g.length !== 1) return;
      var twin = g[0]; delete groups[twinKey(s)];
      delete localById[s.id]; s.id = twin.id; localById[s.id] = s; idsChanged = true;
    });

    var added = 0, updated = 0, deleted = 0, localChanged = idsChanged, toTrash = [], untrash = [], tombs = [];
    var seen = {}, ids = [];
    [localById, localTomb, remoteLive, remoteTomb].forEach(function (m) { Object.keys(m).forEach(function (k) { if (!seen[k]) { seen[k] = 1; ids.push(k); } }); });

    ids.forEach(function (id) {
      var l = localById[id], r = remoteLive[id], lt = localTomb[id], rt = remoteTomb[id];
      var win = pickWinner(l, r, lt, rt);
      if (win.kind === 'L') {
        if (lt) untrash.push(id);
      } else if (win.kind === 'R') {
        if (!l) { var ns = { id: id, createdAt: r.createdAt || nowIso(), tags: [] }; apply(r, ns); local.push(ns); added++; localChanged = true; }
        else {
          var before = l.updatedAt;
          if (apply(r, l)) { updated++; localChanged = true; }
          else if (before !== l.updatedAt) localChanged = true;
        }
        if (trash.some(function (e) { return e.id === id; })) untrash.push(id);
      } else {
        if (l) { toTrash.push({ s: l, at: win.str }); deleted++; localChanged = true; }
        tombs.push({ id: id, at: win.str, name: (id in names ? names[id] : (l ? displayName(l) : '')) });
      }
    });

    if (toTrash.length) {
      var gone = {}; toTrash.forEach(function (x) { gone[x.s.id] = x; });
      st.sessions = local.filter(function (s) { return !gone[s.id]; });
      toTrash.forEach(function (x) {
        st.trash = st.trash.filter(function (e) { return e.id !== x.s.id; });
        st.trash.push({ id: x.s.id, name: displayName(x.s), subtitle: x.s.username + '@' + x.s.host, deletedAt: x.at, localOnly: false, session: x.s });
      });
    }
    if (untrash.length) st.trash = st.trash.filter(function (e) { return untrash.indexOf(e.id) < 0; });
    this._purgeTrash();
    var sm = mergeSnippets(st.snippets, st.snipTombs, remote);
    st.snippets = sm.snippets; st.snipTombs = sm.tombs;
    if (remote) st.extra = extrasOf(remote);
    return { snipStats: sm, vault: buildVault(st.sessions, tombs, this.o.device, st.snippets, st.snipTombs, st.extra), added: added, updated: updated, deleted: deleted, snipChanged: sm.changed };
  };

  /** Kéo bản trên máy chủ, gộp với máy này, rồi đẩy bản gộp lên (nếu có khác biệt). */
  Vault.prototype.sync = async function () {
    for (var attempt = 0; attempt < 4; attempt++) {
      var p = await this._pull();
      var m = this._merge(p.data);
      try {
        var ver = p.version;
        if (!p.data || !sameContent(p.data, m.vault)) ver = await this._push(m.vault, p.version);
        this._markSynced(ver);
        if (this.onChange && (m.added || m.updated || m.deleted || m.snipChanged)) this.onChange();
        return { added: m.added, updated: m.updated, deleted: m.deleted, snipChanged: m.snipChanged, total: this.state.sessions.length, version: ver };
      } catch (e) { if (e.status !== 409) throw e; await this.save(); }
    }
    var ce = new Error(L('Xung đột đồng bộ, hãy thử lại.')); ce.status = 409; throw ce;
  };

  Vault.prototype.count = async function () {
    var p = await this._pull();
    return { remote: p.data ? p.data.sessions.map(clean).filter(isValid).length : 0, local: this.state.sessions.length };
  };

  /** Lấy bản trên máy chủ thay cho máy này (VM chỉ có ở máy này vào thùng rác của máy này). */
  Vault.prototype.forcePull = async function () {
    var p = await this._pull(), st = this.state, now = nowIso();
    var rl = (p.data ? p.data.sessions : []).map(clean).filter(isValid), ids = {}; rl.forEach(function (v) { ids[v.id] = 1; });
    var gone = st.sessions.filter(function (s) { return !ids[s.id]; }), added = 0, updated = 0, result = [];
    rl.forEach(function (v) {
      var s = st.sessions.find(function (x) { return x.id === v.id; });
      if (!s) { s = { id: v.id, createdAt: v.createdAt || now, tags: [] }; added++; apply(v, s); }
      else if (apply(v, s)) updated++;
      result.push(s);
    });
    gone.forEach(function (s) {
      st.trash = st.trash.filter(function (e) { return e.id !== s.id; });
      st.trash.push({ id: s.id, name: displayName(s), subtitle: s.username + '@' + s.host, deletedAt: now, localOnly: true, session: s });
    });
    st.trash = st.trash.filter(function (e) { return !ids[e.id]; });
    st.sessions = result;
    if (p.data && Array.isArray(p.data.snippets)) { st.snippets = cleanSnips(p.data.snippets); st.snipTombs = cleanSnipTombs(p.data.deletedSnippets); this._purgeSnipTombs(); }
    this._markSynced(p.version);
    if (this.onChange) this.onChange();
    return { added: added, updated: updated, total: result.length, deleted: gone.length, version: p.version };
  };

  /** Ghi đè máy chủ bằng dữ liệu máy này (VM chỉ có trên máy chủ bị đánh dấu xóa; bản cũ vẫn còn trong lịch sử). */
  Vault.prototype.forcePush = async function () {
    for (var attempt = 0; attempt < 4; attempt++) {
      var p = await this._pull(), st = this.state, now = nowIso();
      st.sessions.forEach(function (s) { s.updatedAt = now; });
      var live = {}; st.sessions.forEach(function (s) { live[s.id] = 1; });
      var rl = p.data ? p.data.sessions.map(clean) : [];
      var tombs = st.trash.filter(function (e) { return !e.localOnly; }).map(function (e) { return { id: e.id, at: e.deletedAt, name: e.name }; })
        .concat(p.data ? p.data.deleted : [])
        .concat(rl.filter(function (v) { return !live[v.id]; }).map(function (v) { return { id: v.id, at: now, name: displayName(v) }; }));
      var removed = rl.filter(function (v) { return !live[v.id]; }).length;
      // snippet: cùng cách — bản ở máy này thắng mọi thứ, snippet chỉ có trên máy chủ bị đánh dấu xóa
      st.snippets.forEach(function (x) { x.updatedAt = now; });
      var slive = {}; st.snippets.forEach(function (x) { slive[x.id] = 1; });
      var rsn = cleanSnips(p.data && p.data.snippets);
      st.snipTombs = st.snipTombs.concat(cleanSnipTombs(p.data && p.data.deletedSnippets))
        .concat(rsn.filter(function (x) { return !slive[x.id]; }).map(function (x) { return { id: x.id, deletedAt: now }; }));
      try {
        var ver = await this._push(buildVault(st.sessions, tombs, this.o.device, st.snippets, st.snipTombs, p.data ? extrasOf(p.data) : st.extra), p.version);
        this._markSynced(ver);
        return { total: st.sessions.length, deleted: removed, version: ver };
      } catch (e) { if (e.status !== 409) throw e; }
    }
    var ce = new Error(L('Xung đột đồng bộ, hãy thử lại.')); ce.status = 409; throw ce;
  };

  // ----- Lịch sử phiên bản -----
  Vault.prototype.history = async function () {
    var r = await this._call('GET', '/vault/history');
    return (r.versions || []).map(function (v) {
      return { version: v.version, createdAt: v.createdAt, size: v.size, device: v.device || '', current: v.version === r.current };
    });
  };
  Vault.prototype.loadVersion = async function (version) { var p = await this._pull(version); return p.data || { sessions: [], deleted: [] }; };

  Vault.prototype.preview = function (data) {
    var local = {}; this.state.sessions.forEach(function (s) { local[s.id] = s; });
    var snap = (data.sessions || []).map(clean).filter(isValid), snapIds = {}; snap.forEach(function (v) { snapIds[v.id] = 1; });
    var missing = snap.filter(function (v) { return !local[v.id]; });
    var different = snap.filter(function (v) { return local[v.id] && contentKey(local[v.id]) !== contentKey(v); }).length;
    var snSnap = cleanSnips(data.snippets), snLocal = {}; this.state.snippets.forEach(function (x) { snLocal[x.id] = x; });
    return {
      snippets: Array.isArray(data.snippets) ? snSnap.length : null,
      snippetsMissing: snSnap.filter(function (x) { return !snLocal[x.id]; }).length,
      total: snap.length, missingHere: missing.length, different: different,
      onlyHere: this.state.sessions.filter(function (s) { return !snapIds[s.id]; }).length,
      missingNames: missing.map(displayName)
    };
  };

  /** Khôi phục từ một bản cũ. replaceAll=false: chỉ thêm lại VM đang thiếu; true: đưa danh sách về đúng bản đó. */
  Vault.prototype.restore = async function (data, replaceAll) {
    var st = this.state, now = nowIso(), snap = (data.sessions || []).map(clean).filter(isValid);
    var added = 0, updated = 0, removed = 0, snapIds = {};
    snap.forEach(function (v) {
      snapIds[v.id] = 1;
      var s = st.sessions.find(function (x) { return x.id === v.id; });
      if (!s) { s = { id: v.id, createdAt: v.createdAt || now, tags: [] }; apply(v, s); st.sessions.push(s); added++; }
      else if (replaceAll) { if (apply(v, s)) updated++; }
      else return;
      s.updatedAt = now;
    });
    if (replaceAll) {
      var gone = st.sessions.filter(function (s) { return !snapIds[s.id]; });
      gone.forEach(function (s) {
        st.trash = st.trash.filter(function (e) { return e.id !== s.id; });
        st.trash.push({ id: s.id, name: displayName(s), subtitle: s.username + '@' + s.host, deletedAt: now, localOnly: false, session: s });
      });
      st.sessions = st.sessions.filter(function (s) { return snapIds[s.id]; });
      removed = gone.length;
    }
    st.trash = st.trash.filter(function (e) { return !snapIds[e.id]; });
    // snippet: bản backup cũ KHÔNG có trường snippets thì giữ nguyên snippet của máy này (không xóa nhầm)
    if (Array.isArray(data.snippets)) {
      var ssnap = cleanSnips(data.snippets), sids = {};
      ssnap.forEach(function (v) {
        sids[v.id] = 1;
        var x = st.snippets.find(function (y) { return y.id === v.id; });
        if (!x) { x = { id: v.id }; st.snippets.push(x); }
        else if (!replaceAll) return;
        x.name = v.name; x.command = v.command; x.vmId = v.vmId; x.autoEnter = v.autoEnter; x.createdAt = v.createdAt || x.createdAt || now; x.updatedAt = now;
      });
      if (replaceAll) {
        st.snippets.filter(function (x) { return !sids[x.id]; }).forEach(function (x) { st.snipTombs.push({ id: x.id, deletedAt: now }); });
        st.snippets = st.snippets.filter(function (x) { return sids[x.id]; });
      }
      st.snipTombs = st.snipTombs.filter(function (t) { return !sids[t.id]; });
    }
    await this.save();
    var r = await this.sync();
    return { added: added, updated: updated, deleted: removed, total: r.total, version: r.version };
  };

  global.WNVault = { mergeSnippets: mergeSnippets, cleanSnip: cleanSnip, Vault: Vault, uuid: uuid, parseTime: parseTime, iso: iso, nowIso: nowIso, clean: clean, isValid: isValid, displayName: displayName, buildVault: buildVault, sameContent: sameContent };
})(typeof window !== 'undefined' ? window : globalThis);
