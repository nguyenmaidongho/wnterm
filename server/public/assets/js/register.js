(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var form = $('form');
  if (!form) return;

  var L = WNI18N, errBox = $('err'), btn = $('submit');
  if (!WN.available) {
    showErr(L.t('crypto.unsupported'));
    btn.disabled = true;
  }

  function showErr(msg) { errBox.textContent = msg; errBox.hidden = false; errBox.scrollIntoView({ block: 'nearest' }); }
  function clearErr() { errBox.hidden = true; }

  // Hiện/ẩn mật khẩu
  $('showpw').addEventListener('click', function () {
    var show = $('pw').type === 'password';
    $('pw').type = show ? 'text' : 'password';
    $('pw2').type = $('pw').type;
    this.textContent = show ? L.t('common.hide') : L.t('common.show');
  });

  // Đo độ mạnh mật khẩu (ước lượng đơn giản theo độ dài + đa dạng ký tự)
  function strength(p) {
    var score = 0;
    if (p.length >= 10) score++;
    if (p.length >= 14) score++;
    if (p.length >= 18) score++;
    if (/[a-z]/.test(p) && /[A-Z]/.test(p)) score++;
    if (/\d/.test(p)) score++;
    if (/[^A-Za-z0-9]/.test(p)) score++;
    if (/(.)\1{3,}/.test(p) || /^(?:password|matkhau|123456|qwerty)/i.test(p)) score = Math.max(0, score - 2);
    return Math.min(score, 6);
  }
  $('pw').addEventListener('input', function () {
    var s = strength(this.value), pct = Math.round(s / 6 * 100);
    var m = $('meter'); m.style.width = pct + '%';
    m.style.background = s <= 2 ? '#d4283a' : (s <= 4 ? '#faad14' : '#52c41a');
    $('pwhint').textContent = this.value.length < 10 ? L.t('reg.hint_short') : (s <= 2 ? L.t('reg.hint_weak') : (s <= 4 ? L.t('reg.hint_ok') : L.t('reg.hint_strong')));
  });

  var session = { token: null, code: null };

  form.addEventListener('submit', async function (ev) {
    ev.preventDefault();
    clearErr();

    var email = $('email').value.trim().toLowerCase();
    var pw = $('pw').value, pw2 = $('pw2').value;
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) return showErr(L.t('reg.email_bad'));
    if (pw.length < 10) return showErr(L.t('reg.pw_short'));
    if (pw !== pw2) return showErr(L.t('reg.pw_mismatch'));
    if (!$('agree').checked) return showErr(L.t('reg.agree_req'));
    if (!$('tos').checked) return showErr(L.t('reg.tos_req'));

    btn.disabled = true;
    var oldLabel = btn.textContent;
    btn.textContent = L.t('reg.working');

    try {
      var iter = WN.DEFAULT_ITERATIONS;
      var k = await WN.derive(pw, email, iter);
      var vaultKey = crypto.getRandomValues(new Uint8Array(32));
      var recBytes = WN.newRecoveryBytes();
      var rk = await WN.recoveryKeys(recBytes);

      var body = {
        email: email,
        authKey: WN.b64(k.auth),
        kdfIterations: iter,
        wrappedPw: await WN.seal(k.enc, vaultKey),
        recoveryAuth: WN.b64(rk.auth),
        wrappedRecovery: await WN.seal(rk.enc, vaultKey),
        tos: $('tos').getAttribute('data-tos'),
        deviceName: L.t('device.web')
      };
      var ts = document.querySelector('[name="cf-turnstile-response"]');
      if (ts) body.turnstile = ts.value;

      var res = await WN.api('POST', '/register', body, false);
      if (!res.ok) {
        showErr(L.err(res, 'reg.failed'));
        if (window.turnstile) { try { window.turnstile.reset(); } catch (e) {} }
        return;
      }

      WN.setToken(res.token);
      session.code = WN.formatRecovery(recBytes);
      $('code').textContent = session.code;
      $('step1').hidden = true;
      $('step2').hidden = false;
      window.scrollTo({ top: 0 });
    } catch (e) {
      showErr(L.t('reg.crypto_err', e && e.message ? e.message : e));
    } finally {
      btn.disabled = false;
      btn.textContent = oldLabel;
      // Xóa mật khẩu khỏi ô nhập
      $('pw').value = ''; $('pw2').value = '';
    }
  });

  // Bước 2
  $('copy').addEventListener('click', async function () {
    try { await navigator.clipboard.writeText(session.code); this.textContent = L.t('reg.copied'); }
    catch (e) { this.textContent = L.t('reg.copy_manual'); }
  });
  $('dl').addEventListener('click', function () {
    WN.downloadText(L.t('reg.file_name'),
      L.t('reg.file_head') + '\r\n\r\n' + session.code +
      '\r\n\r\n' + L.t('reg.file_foot') + '\r\n');
  });
  $('print').addEventListener('click', function () { window.print(); });
  $('saved').addEventListener('change', function () { $('done').disabled = !this.checked; });
  $('done').addEventListener('click', function () { session.code = null; location.href = '/tai-khoan'; });
})();
