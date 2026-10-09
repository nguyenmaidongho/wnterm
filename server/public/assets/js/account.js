(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var L = WNI18N, email = '';

  function err(m) { $('err').textContent = m; $('err').hidden = !m; if (m) $('msg').hidden = true; }
  function ok(m) { $('msg').textContent = m; $('msg').hidden = !m; if (m) $('err').hidden = true; }
  function fmt(iso) { try { return new Date(iso).toLocaleString(L.lang === 'en' ? 'en-US' : 'vi-VN'); } catch (e) { return iso; } }
  function esc(s) { var d = document.createElement('div'); d.textContent = s == null ? '' : s; return d.innerHTML; }

  async function load() {
    if (!WN.getToken()) return location.replace('/dang-nhap');
    var me = await WN.api('GET', '/me', null, true);
    if (!me.ok) { WN.setToken(null); return location.replace('/dang-nhap'); }
    email = me.email;
    $('who').textContent = L.t('acc.hello', me.email);
    $('kvEmail').textContent = me.email;
    $('kvCreated').textContent = fmt(me.createdAt);
    $('kvVault').textContent = me.vault.version > 0
      ? L.t('acc.vault_ver', me.vault.version, fmt(me.vault.updatedAt))
      : L.t('acc.vault_none');
    await loadDevices();
    $('loading').hidden = true; $('main').hidden = false;
  }

  async function loadDevices() {
    var r = await WN.api('GET', '/devices', null, true);
    var box = $('devices'); box.innerHTML = '';
    (r.devices || []).forEach(function (d) {
      var row = document.createElement('div'); row.className = 'dev';
      row.innerHTML = '<div class="nm"><b>' + esc(d.name) + '</b>' + (d.current ? '<span class="tag ok">' + esc(L.t('acc.this_device')) + '</span>' : '') +
        '<small>' + esc(d.ip) + ' · ' + esc(L.t('acc.last_used')) + ' ' + esc(fmt(d.lastUsedAt)) + '</small></div>';
      if (!d.current) {
        var b = document.createElement('button'); b.className = 'linkbtn'; b.textContent = L.t('acc.signout');
        b.onclick = async function () { await WN.api('DELETE', '/devices?id=' + d.id, null, true); loadDevices(); };
        row.appendChild(b);
      }
      box.appendChild(row);
    });
  }

  $('logout').onclick = async function () {
    await WN.api('POST', '/logout', {}, true); WN.setToken(null); location.href = '/';
  };

  $('pwForm').onsubmit = async function (ev) {
    ev.preventDefault(); err(''); ok('');
    var oldPw = $('oldPw').value, np = $('newPw').value, np2 = $('newPw2').value, btn = $('pwBtn');
    if (np.length < 10) return err(L.t('acc.new_short'));
    if (np !== np2) return err(L.t('acc.new_mismatch'));
    btn.disabled = true;
    try {
      var keys = await WN.api('GET', '/keys', null, true);
      if (!keys.ok) return err(L.err(keys, 'acc.keys_failed'));
      var ko = await WN.derive(oldPw, email, keys.kdfIterations);
      var vaultKey = await WN.open(ko.enc, keys.wrappedPw);
      if (!vaultKey) return err(L.t('acc.old_bad'));
      var kn = await WN.derive(np, email, keys.kdfIterations);
      var res = await WN.api('POST', '/password/change', {
        oldAuthKey: WN.b64(ko.auth), newAuthKey: WN.b64(kn.auth), newWrappedPw: await WN.seal(kn.enc, vaultKey)
      }, true);
      if (!res.ok) return err(L.err(res, 'acc.pw_failed', 'acc.old_bad'));
      ok(L.t('acc.pw_done'));
      $('pwForm').reset(); loadDevices();
    } catch (e) { err(L.t('common.error', e && e.message ? e.message : e)); }
    finally { btn.disabled = false; }
  };

  $('delForm').onsubmit = async function (ev) {
    ev.preventDefault(); err(''); ok('');
    if (!confirm(L.t('acc.del_confirm', email))) return;
    var btn = $('delBtn'); btn.disabled = true;
    try {
      var keys = await WN.api('GET', '/keys', null, true);
      var k = await WN.derive($('delPw').value, email, keys.kdfIterations);
      var res = await WN.api('POST', '/account/delete', { authKey: WN.b64(k.auth) }, true);
      if (!res.ok) return err(L.err(res, 'acc.del_failed', 'acc.del_bad'));
      WN.setToken(null); alert(L.t('acc.del_done')); location.href = '/';
    } catch (e) { err(L.t('common.error', e && e.message ? e.message : e)); }
    finally { btn.disabled = false; $('delPw').value = ''; }
  };

  load().catch(function (e) { $('loading').textContent = L.t('acc.load_failed', e && e.message ? e.message : e); });
})();
