(function () {
  'use strict';
  var form = document.getElementById('fbForm');
  if (!form) return;
  var btn = document.getElementById('fbBtn'), box = document.getElementById('fbMsg');
  var D = form.dataset;

  function show(text, ok) {
    box.textContent = text; box.hidden = !text;
    box.className = 'alert ' + (ok ? 'ok' : 'err');
  }
  var errMap = { captcha: D.errCaptcha, rate_limited: D.errRate, bad_input: D.errRequired };

  form.addEventListener('submit', async function (ev) {
    ev.preventDefault();
    show('');
    var f = form.elements;
    var body = {
      name: f.name.value.trim(), email: f.email.value.trim(), phone: f.phone.value.trim(),
      message: f.message.value.trim(), website: f.website.value
    };
    if (!body.name || !body.message || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(body.email)) return show(D.errRequired);
    var ts = form.querySelector('[name="cf-turnstile-response"]');
    if (ts) body.turnstile = ts.value;

    btn.disabled = true; btn.textContent = D.sending;
    try {
      var res = await fetch('/api/v1/feedback', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      var data = {}; try { data = await res.json(); } catch (e) {}
      if (data.ok) {
        show(D.ok, true);
        f.message.value = '';
      } else {
        show(errMap[data.error] || D.errFail);
      }
    } catch (e) {
      show(D.errFail);
    } finally {
      btn.disabled = false; btn.textContent = D.submit;
      if (window.turnstile) { try { window.turnstile.reset(); } catch (e) {} }
    }
  });
})();
