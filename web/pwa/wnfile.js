/*
 * WNFile — file danh sách VM (.wnterm, đúng định dạng của app Windows/Android), nhập MobaXterm, và ZIP để tải thư mục.
 *  .wnterm: JSON { format:"wnterm-sessions", version:1, protection:{kdf:"PBKDF2-SHA256", iterations, salt, check}, sessions:[...] }
 *  Khóa = PBKDF2-SHA256(mật khẩu UTF-8, salt 16 byte, 600000 vòng); mỗi VM có khối "secrets" AES-256-GCM với AAD = id.
 */
(function (global) {
  'use strict';
  var L = (global.WNi18n && global.WNi18n.t) || function (s) { return s; }, LT = (global.WNi18n && global.WNi18n.tr) || function (s) { return s; };
  var enc = new TextEncoder(), dec = new TextDecoder();
  var subtle = global.crypto && global.crypto.subtle;

  function b64(u8) { var s = ''; for (var i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000)); return btoa(s); }
  function unb64(s) { var r = atob(s), o = new Uint8Array(r.length); for (var i = 0; i < r.length; i++) o[i] = r.charCodeAt(i); return o; }

  async function deriveKey(password, salt, iterations) {
    var base = await subtle.importKey('raw', enc.encode(password), 'PBKDF2', false, ['deriveKey']);
    return subtle.deriveKey({ name: 'PBKDF2', hash: 'SHA-256', salt: salt, iterations: iterations }, base, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
  }

  async function encBlock(key, plain, aad) {
    var iv = crypto.getRandomValues(new Uint8Array(12));
    var ct = new Uint8Array(await subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: enc.encode(aad), tagLength: 128 }, key, plain));
    return { nonce: b64(iv), cipherText: b64(ct.subarray(0, ct.length - 16)), tag: b64(ct.subarray(ct.length - 16)) };
  }
  async function decBlock(key, block, aad) {
    var ct = unb64(block.cipherText), tag = unb64(block.tag), all = new Uint8Array(ct.length + tag.length);
    all.set(ct, 0); all.set(tag, ct.length);
    return new Uint8Array(await subtle.decrypt({ name: 'AES-GCM', iv: unb64(block.nonce), additionalData: enc.encode(aad), tagLength: 128 }, key, all));
  }

  /** sessions: [{id,name,group,tags,host,port,username,password,passphrase,keyFileName,keyFileContent}] → chuỗi JSON của file .wnterm */
  async function exportSessions(sessions, password, includeKeys) {
    var file = { format: 'wnterm-sessions', version: 1, exportedAt: new Date().toISOString(), appVersion: 'web', protection: null, sessions: [] };
    var key = null;
    if (password) {
      var salt = crypto.getRandomValues(new Uint8Array(16));
      key = await deriveKey(password, salt, 600000);
      file.protection = { kdf: 'PBKDF2-SHA256', iterations: 600000, salt: b64(salt), check: await encBlock(key, enc.encode('WNTERM-OK'), 'WNTERM-CHECK') };
    }
    for (var i = 0; i < sessions.length; i++) {
      var s = sessions[i];
      var item = {
        id: s.id, name: s.name || '', group: s.group || '', tags: s.tags && s.tags.length ? s.tags : null,
        host: s.host, port: s.port, username: s.username, keyFileName: s.keyFileName || null, secrets: null
      };
      if (key) {
        var payload = { password: s.password || null, passphrase: s.passphrase || null, keyFileContent: includeKeys ? (s.keyFileContent || null) : null };
        item.secrets = await encBlock(key, enc.encode(JSON.stringify(payload)), String(s.id).toLowerCase());
      }
      file.sessions.push(item);
    }
    return JSON.stringify(file, null, 2);
  }

  // ===== MobaXterm (.mxtsessions / .ini) =====
  function isMoba(text) { return /\[Bookmarks|#109#|#106#/i.test(text); }

  function parseMoba(text) {
    var out = [], group = L('Chưa phân nhóm');
    text.split(/\r?\n/).forEach(function (line) {
      line = line.trim(); if (!line) return;
      if (line[0] === '[' && line[line.length - 1] === ']') { group = L('Chưa phân nhóm'); return; }
      if (/^SubRep=/i.test(line)) { var sv = line.slice(7).trim(); group = sv ? sv.replace(/\\/g, '/') : L('Chưa phân nhóm'); return; }
      if (/^ImgNum=/i.test(line)) return;
      var eq = line.indexOf('='); if (eq <= 0) return;
      var key = line.slice(0, eq).trim(), val = line.slice(eq + 1).trim();
      var idx = val.toLowerCase().indexOf('#109#'); if (idx < 0) idx = val.toLowerCase().indexOf('#106#'); if (idx < 0) return;
      var parts = val.slice(idx + 5).split('%'); if (parts.length < 4) return;
      var host = parts[1].trim(); if (!host) return;
      var port = parseInt(parts[2], 10); if (!(port > 0 && port <= 65535)) port = 22;
      var user = parts[3].trim(); if (!user || user.toLowerCase() === '<default>') user = 'root';
      out.push({ id: WNVault.uuid(), name: key || (user + '@' + host), group: group, tags: [], host: host, port: port, username: user, password: null, passphrase: null, keyFileName: null, keyFileContent: null });
    });
    return out;
  }

  /** Đọc file: trả { kind:'wnterm'|'moba', protected:bool, file, items } (items chưa giải mã nếu có mật khẩu). */
  function parse(text) {
    text = String(text || '').replace(/^﻿/, '');
    if (text.length > 20 * 1024 * 1024) throw new Error(L('File vượt quá giới hạn 20 MB.'));
    var f = null;
    try { f = JSON.parse(text); } catch (e) { f = null; }
    if (f && f.format === 'wnterm-sessions') {
      if (f.version > 1) throw new Error(L('File được tạo bởi phiên bản WN Term mới hơn, hãy cập nhật ứng dụng.'));
      return { kind: 'wnterm', protected: !!f.protection, file: f, count: (f.sessions || []).length };
    }
    if (isMoba(text)) {
      var items = parseMoba(text);
      if (!items.length) throw new Error(L('Không tìm thấy cấu hình VM (SSH/SFTP) nào trong file MobaXterm.'));
      return { kind: 'moba', protected: false, items: items, count: items.length };
    }
    throw new Error(L('File không đúng định dạng WN Term hoặc MobaXterm.'));
  }

  /** Giải mã (nếu cần) và trả về danh sách VM để nhập. Ném Error('wrong_password') nếu sai mật khẩu. */
  async function open(parsed, password) {
    if (parsed.kind === 'moba') return { items: parsed.items, corrupt: 0 };
    var f = parsed.file, key = null;
    if (f.protection) {
      var wrong = new Error(L('Sai mật khẩu file.')); wrong.code = 'wrong_password';
      if (!password) throw wrong;
      try {
        key = await deriveKey(password, unb64(f.protection.salt), f.protection.iterations || 600000);
        var chk = await decBlock(key, f.protection.check, 'WNTERM-CHECK');
        if (dec.decode(chk) !== 'WNTERM-OK') throw wrong;
      } catch (e) { throw wrong; }
    }
    var corrupt = 0, items = [];
    for (var i = 0; i < f.sessions.length; i++) {
      var it = f.sessions[i];
      var s = { id: String(it.id || '').toLowerCase() || WNVault.uuid(), name: it.name || '', group: it.group || '', tags: it.tags || [], host: it.host, port: it.port || 22, username: it.username,
        password: null, passphrase: null, keyFileName: it.keyFileName || null, keyFileContent: null };
      if (key && it.secrets) {
        try {
          var p = JSON.parse(dec.decode(await decBlock(key, it.secrets, s.id)));
          s.password = p.password || null; s.passphrase = p.passphrase || null; s.keyFileContent = p.keyFileContent || null;
        } catch (e) { corrupt++; }
      }
      if (s.keyFileName) s.keyFileName = String(s.keyFileName).split(/[\\/]/).pop();
      items.push(s);
    }
    return { items: items, corrupt: corrupt };
  }

  // ===== ZIP (không nén — đủ để gom nhiều file/thư mục thành một file tải về) =====
  var CRC = (function () { var t = new Uint32Array(256); for (var n = 0; n < 256; n++) { var c = n; for (var k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
  function crc32(u8) { var c = 0xFFFFFFFF; for (var i = 0; i < u8.length; i++) c = CRC[(c ^ u8[i]) & 255] ^ (c >>> 8); return (c ^ 0xFFFFFFFF) >>> 0; }

  /** entries: [{ name:'a/b.txt', data:Uint8Array|null (null = thư mục), mtime:ms }] → Blob */
  function makeZip(entries) {
    var parts = [], central = [], offset = 0;
    function u16(v) { return [v & 255, (v >> 8) & 255]; }
    function u32(v) { return [v & 255, (v >>> 8) & 255, (v >>> 16) & 255, (v >>> 24) & 255]; }
    entries.forEach(function (e) {
      var isDir = e.data == null, name = enc.encode(e.name + (isDir && !/\/$/.test(e.name) ? '/' : ''));
      var data = isDir ? new Uint8Array(0) : e.data, crc = isDir ? 0 : crc32(data);
      var d = new Date(e.mtime || Date.now());
      var time = (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1);
      var date = (((d.getFullYear() - 1980) < 0 ? 0 : d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
      var head = new Uint8Array([].concat([0x50, 0x4b, 3, 4], u16(20), u16(0x0800), u16(0), u16(time), u16(date), u32(crc), u32(data.length), u32(data.length), u16(name.length), u16(0)));
      parts.push(head, name, data);
      central.push(new Uint8Array([].concat([0x50, 0x4b, 1, 2], u16(20), u16(20), u16(0x0800), u16(0), u16(time), u16(date), u32(crc), u32(data.length), u32(data.length),
        u16(name.length), u16(0), u16(0), u16(0), u16(0), u32(isDir ? 0x10 : 0), u32(offset))), name);
      offset += head.length + name.length + data.length;
    });
    var cdSize = central.reduce(function (n, p) { return n + p.length; }, 0);
    var end = new Uint8Array([].concat([0x50, 0x4b, 5, 6], u16(0), u16(0), u16(entries.length), u16(entries.length), u32(cdSize), u32(offset), u16(0)));
    return new Blob(parts.concat(central, [end]), { type: 'application/zip' });
  }

  global.WNFile = { exportSessions: exportSessions, parse: parse, open: open, makeZip: makeZip, crc32: crc32 };
})(typeof window !== 'undefined' ? window : globalThis);
