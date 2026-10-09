(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var L = WNI18N, form = $('form'), errBox = $('err'), btn = $('submit');

  if (WN.getToken()) {   // đã có phiên: vào thẳng trang tài khoản
    WN.api('GET', '/me', null, true).then(function (r) { if (r.ok) location.replace('/tai-khoan'); else WN.setToken(null); });
  }
  if (!WN.available) { show(L.t('crypto.unsupported_short')); btn.disabled = true; }

  function show(m) { errBox.textContent = m; errBox.hidden = false; }

  $('showpw').addEventListener('click', function () {
    var s = $('pw').type === 'password'; $('pw').type = s ? 'text' : 'password'; this.textContent = s ? L.t('common.hide') : L.t('common.show');
  });

  form.addEventListener('submit', async function (ev) {
    ev.preventDefault();
    errBox.hidden = true;
    var email = $('email').value.trim().toLowerCase(), pw = $('pw').value;
    if (!email || !pw) return show(L.t('login.need'));

    btn.disabled = true; var old = btn.textContent; btn.textContent = L.t('login.working');
    try {
      var pre = await WN.api('POST', '/prelogin', { email: email }, false);
      if (!pre.ok) return show(L.err(pre, 'login.noserver', 'login.bad'));
      var k = await WN.derive(pw, email, pre.kdfIterations);
      var res = await WN.api('POST', '/login', { email: email, authKey: WN.b64(k.auth), deviceName: L.t('device.web') }, false);
      if (!res.ok) return show(L.err(res, 'login.failed', 'login.bad'));
      WN.setToken(res.token);
      location.href = '/tai-khoan';
    } catch (e) {
      show(L.t('common.error', e && e.message ? e.message : e));
    } finally {
      btn.disabled = false; btn.textContent = old; $('pw').value = '';
    }
  });
})();
