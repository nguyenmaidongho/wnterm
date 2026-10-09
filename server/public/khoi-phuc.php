<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('rec.title');
$desc = t('rec.desc');
$active = 'account';
$scripts = ['wn-crypto.js', 'recover.js'];
view('head', compact('title', 'desc', 'active'));
?>
<main class="authwrap">
  <div class="auth">
    <h1><?= t('rec.h1') ?></h1>
    <p class="sub"><?= t('rec.sub') ?></p>
    <div id="err" class="alert err" hidden></div>
    <div id="ok" class="alert ok" hidden></div>
    <form id="form" autocomplete="off" novalidate>
      <div class="field">
        <label for="email"><?= t('reg.email') ?></label>
        <input id="email" type="email" autocomplete="username" required>
      </div>
      <div class="field">
        <label for="code"><?= t('rec.code') ?></label>
        <input id="code" type="text" autocomplete="off" spellcheck="false" placeholder="XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX" required>
      </div>
      <div class="field">
        <label for="pw"><?= t('rec.newpw') ?></label>
        <input id="pw" type="password" autocomplete="new-password" minlength="10" required>
        <div class="hint"><?= t('rec.hint') ?></div>
      </div>
      <div class="field">
        <label for="pw2"><?= t('rec.newpw2') ?></label>
        <input id="pw2" type="password" autocomplete="new-password" required>
      </div>
      <button class="btn brand block" id="submit" type="submit"><?= t('rec.submit') ?></button>
    </form>
    <div class="alt"><a href="/dang-nhap"><?= t('rec.back') ?></a></div>
  </div>
</main>
<?php view('foot', compact('scripts')); ?>
