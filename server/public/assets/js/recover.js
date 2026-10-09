(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var L = WNI18N, form = $('form'), errBox = $('err'), okBox = $('ok'), btn = $('submit');
  function show(m) { errBox.textContent = m; errBox.hidden = false; okBox.hidden = true; }
  if (!WN.available) { show(L.t('crypto.unsupported_short')); btn.disabled = true; }

  form.addEventListener('submit', async function (ev) {
    ev.preventDefault();
    errBox.hidden = true; okBox.hidden = true;

    var email = $('email').value.trim().toLowerCase();
    var recBytes = WN.parseRecovery($('code').value);
    var pw = $('pw').value, pw2 = $('pw2').value;
    if (!email) return show(L.t('rec.need_email'));
    if (!recBytes) return show(L.t('rec.code_bad'));
    if (pw.length < 10) return show(L.t('rec.pw_short'));
    if (pw !== pw2) return show(L.t('reg.pw_mismatch'));

    btn.disabled = true; var old = btn.textContent; btn.textContent = L.t('rec.working');
    try {
      var rk = await WN.recoveryKeys(recBytes);
      var start = await WN.api('POST', '/recover/start', { email: email, recoveryAuth: WN.b64(rk.auth) }, false);
      if (!start.ok) return show(L.err(start, 'rec.failed', 'rec.bad'));

      var vaultKey = await WN.open(rk.enc, start.wrappedRecovery);
      if (!vaultKey) return show(L.t('rec.undecryptable'));

      var k = await WN.derive(pw, email, start.kdfIterations);
      var fin = await WN.api('POST', '/recover/reset', {
        email: email, recoveryAuth: WN.b64(rk.auth),
        authKey: WN.b64(k.auth), wrappedPw: await WN.seal(k.enc, vaultKey)
      }, false);
      if (!fin.ok) return show(L.err(fin, 'rec.reset_failed', 'rec.bad'));

      form.hidden = true;
      okBox.innerHTML = L.t('rec.done');
      okBox.hidden = false;
    } catch (e) {
      show(L.t('common.error', e && e.message ? e.message : e));
    } finally {
      btn.disabled = false; btn.textContent = old; $('pw').value = ''; $('pw2').value = '';
    }
  });
})();
