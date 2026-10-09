/*
 * WNTerm — mã hóa phía người dùng (WebCrypto). Cùng giao thức với app (xem src/Api.php):
 *   masterKey = PBKDF2-HMAC-SHA256(mật khẩu, "wnterm:v1:" + email thường, N vòng, 32 byte)
 *   authKey   = HKDF-SHA256(masterKey, info "wnterm-auth")   -> gửi server
 *   encKey    = HKDF-SHA256(masterKey, info "wnterm-enc")    -> giữ lại ở máy, KHÔNG gửi
 *   vaultKey  = 32 byte ngẫu nhiên, được bọc bằng AES-256-GCM bởi encKey và bởi khóa từ mã khôi phục.
 * Mật khẩu và khóa dữ liệu không bao giờ rời khỏi trình duyệt.
 */
(function (global) {
  'use strict';

  var enc = new TextEncoder();
  var subtle = global.crypto && global.crypto.subtle;

  function b64(buf) {
    var bytes = new Uint8Array(buf), s = '';
    for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
    return btoa(s);
  }
  function unb64(str) {
    var s = atob(str), out = new Uint8Array(s.length);
    for (var i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
    return out;
  }
  function concat(a, b, c) {
    var out = new Uint8Array(a.length + b.length + (c ? c.length : 0));
    out.set(a, 0); out.set(b, a.length); if (c) out.set(c, a.length + b.length);
    return out;
  }

  async function pbkdf2(password, saltStr, iterations) {
    var key = await subtle.importKey('raw', enc.encode(password.normalize('NFC')), 'PBKDF2', false, ['deriveBits']);
    var bits = await subtle.deriveBits(
      { name: 'PBKDF2', hash: 'SHA-256', salt: enc.encode(saltStr), iterations: iterations }, key, 256);
    return new Uint8Array(bits);
  }

  async function hkdf(keyBytes, info) {
    var key = await subtle.importKey('raw', keyBytes, 'HKDF', false, ['deriveBits']);
    var bits = await subtle.deriveBits(
      { name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(0), info: enc.encode(info) }, key, 256);
    return new Uint8Array(bits);
  }

  /** Từ mật khẩu + email → { auth, enc } (Uint8Array). */
  async function derive(password, email, iterations) {
    var master = await pbkdf2(password, 'wnterm:v1:' + String(email).trim().toLowerCase(), iterations);
    return { auth: await hkdf(master, 'wnterm-auth'), enc: await hkdf(master, 'wnterm-enc') };
  }

  /** AES-256-GCM: trả base64( iv12 | ciphertext | tag16 ). */
  async function seal(keyBytes, plainBytes) {
    var key = await subtle.importKey('raw', keyBytes, 'AES-GCM', false, ['encrypt']);
    var iv = global.crypto.getRandomValues(new Uint8Array(12));
    var ct = new Uint8Array(await subtle.encrypt({ name: 'AES-GCM', iv: iv, tagLength: 128 }, key, plainBytes));
    return b64(concat(iv, ct));
  }

  /** Giải mã chuỗi base64 từ seal(); trả null nếu sai khóa. */
  async function open(keyBytes, b64str) {
    try {
      var raw = unb64(b64str);
      var key = await subtle.importKey('raw', keyBytes, 'AES-GCM', false, ['decrypt']);
      var pt = await subtle.decrypt({ name: 'AES-GCM', iv: raw.slice(0, 12), tagLength: 128 }, key, raw.slice(12));
      return new Uint8Array(pt);
    } catch (e) { return null; }
  }

  // ===== Mã khôi phục: 20 byte → base32 (32 ký tự) nhóm 4 =====
  var B32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

  function toBase32(bytes) {
    var bits = 0, value = 0, out = '';
    for (var i = 0; i < bytes.length; i++) {
      value = (value << 8) | bytes[i]; bits += 8;
      while (bits >= 5) { out += B32[(value >>> (bits - 5)) & 31]; bits -= 5; }
    }
    if (bits > 0) out += B32[(value << (5 - bits)) & 31];
    return out;
  }
  function fromBase32(str) {
    var bits = 0, value = 0, out = [];
    str = str.toUpperCase().replace(/[^A-Z2-7]/g, '');
    for (var i = 0; i < str.length; i++) {
      value = (value << 5) | B32.indexOf(str[i]); bits += 5;
      if (bits >= 8) { out.push((value >>> (bits - 8)) & 255); bits -= 8; }
    }
    return new Uint8Array(out);
  }

  function newRecoveryBytes() { return global.crypto.getRandomValues(new Uint8Array(20)); }
  function formatRecovery(bytes) { return toBase32(bytes).replace(/(.{4})(?=.)/g, '$1-'); }
  function parseRecovery(text) {
    var bytes = fromBase32(text);
    return bytes.length === 20 ? bytes : null;
  }
  async function recoveryKeys(bytes) {
    return { auth: await hkdf(bytes, 'wnterm-recovery-auth'), enc: await hkdf(bytes, 'wnterm-recovery-enc') };
  }

  // ===== Gọi API + lưu token (chỉ trong phiên trình duyệt) =====
  var TOKEN_KEY = 'wnterm.token';
  function getToken() { try { return sessionStorage.getItem(TOKEN_KEY); } catch (e) { return null; } }
  function setToken(t) { try { t ? sessionStorage.setItem(TOKEN_KEY, t) : sessionStorage.removeItem(TOKEN_KEY); } catch (e) {} }

  async function api(method, path, body, withToken) {
    var headers = { 'Content-Type': 'application/json' };
    if (withToken) { var t = getToken(); if (t) headers['Authorization'] = 'Bearer ' + t; }
    var res = await fetch('/api/v1' + path, { method: method, headers: headers, body: body ? JSON.stringify(body) : undefined });
    var data = {};
    try { data = await res.json(); } catch (e) { data = { ok: false, error: 'bad_response' }; }
    data._status = res.status;
    return data;
  }

  function downloadText(filename, text) {
    var a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
    a.download = filename;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 1000);
  }

  global.WN = {
    available: !!subtle,
    b64: b64, unb64: unb64, derive: derive, seal: seal, open: open,
    newRecoveryBytes: newRecoveryBytes, formatRecovery: formatRecovery, parseRecovery: parseRecovery, recoveryKeys: recoveryKeys,
    getToken: getToken, setToken: setToken, api: api, downloadText: downloadText,
    DEFAULT_ITERATIONS: 600000
  };
})(window);
