<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('acc.title');
$desc = t('acc.desc');
$active = 'account';
$noindex = true;
$scripts = ['wn-crypto.js', 'account.js'];
view('head', compact('title', 'desc', 'active', 'noindex'));
?>
<main class="authwrap" style="align-items:start">
  <div class="auth wide" id="card">
    <div id="loading" class="sub"><?= t('acc.loading') ?></div>

    <section id="main" hidden>
      <h1><?= t('acc.h1') ?></h1>
      <p class="sub" id="who"></p>
      <div id="msg" class="alert ok" hidden></div>
      <div id="err" class="alert err" hidden></div>

      <div class="acc-grid">
        <div class="panel">
          <h3><?= t('acc.info') ?></h3>
          <dl class="kv">
            <dt><?= t('reg.email') ?></dt><dd id="kvEmail"></dd>
            <dt><?= t('acc.created') ?></dt><dd id="kvCreated"></dd>
            <dt><?= t('acc.vault') ?></dt><dd id="kvVault"></dd>
          </dl>
        </div>
        <div class="panel">
          <h3><?= t('acc.devices') ?></h3>
          <div id="devices"></div>
        </div>
      </div>

      <details class="sec panel">
        <summary><?= t('acc.chpw') ?></summary>
        <form id="pwForm" style="margin-top:14px" autocomplete="off">
          <div class="field"><label><?= t('acc.oldpw') ?></label><input id="oldPw" type="password" autocomplete="current-password" required></div>
          <div class="field"><label><?= t('acc.newpw') ?></label><input id="newPw" type="password" autocomplete="new-password" minlength="10" required></div>
          <div class="field"><label><?= t('rec.newpw2') ?></label><input id="newPw2" type="password" autocomplete="new-password" required></div>
          <button class="btn" type="submit" id="pwBtn"><?= t('acc.chpw') ?></button>
          <div class="hint" style="margin-top:8px"><?= t('acc.chpw_hint') ?></div>
        </form>
      </details>

      <details class="sec panel danger">
        <summary style="color:#ff8a86"><?= t('acc.del') ?></summary>
        <form id="delForm" style="margin-top:14px" autocomplete="off">
          <div class="alert warn"><?= t('acc.del_warn') ?></div>
          <div class="field"><label><?= t('acc.del_pw') ?></label><input id="delPw" type="password" autocomplete="current-password" required></div>
          <button class="btn" type="submit" id="delBtn" style="border-color:rgba(212,40,58,.6);color:#ffb3b9"><?= t('acc.del_btn') ?></button>
        </form>
      </details>

      <div style="display:flex;gap:10px;margin-top:22px;flex-wrap:wrap">
        <a class="btn" href="/#tai-ve"><?= t('acc.getapp') ?></a>
        <button class="btn" id="logout" type="button"><?= t('acc.logout') ?></button>
      </div>
    </section>
  </div>
</main>
<?php view('foot', compact('scripts')); ?>
