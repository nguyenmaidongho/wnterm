<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('login.title');
$desc = t('login.desc');
$active = 'account';
$scripts = ['wn-crypto.js', 'login.js'];
$noindex = true;
view('head', compact('title', 'desc', 'active', 'noindex'));
?>
<main class="authwrap">
  <div class="auth">
    <h1><?= t('login.h1') ?></h1>
    <p class="sub"><?= t('login.sub') ?></p>
    <div id="err" class="alert err" hidden></div>
    <noscript><div class="alert err"><?= t('login.noscript') ?></div></noscript>
    <form id="form" autocomplete="on" novalidate>
      <div class="field">
        <label for="email"><?= t('reg.email') ?></label>
        <input id="email" type="email" autocomplete="username" required>
      </div>
      <div class="field">
        <label for="pw"><?= t('reg.pw') ?></label>
        <div class="pwrow">
          <input id="pw" type="password" autocomplete="current-password" required>
          <button type="button" id="showpw" aria-label="<?= e(t('reg.show_aria')) ?>"><?= t('reg.show') ?></button>
        </div>
      </div>
      <button class="btn brand block" id="submit" type="submit"><?= t('login.h1') ?></button>
    </form>
    <div class="alt"><a href="/khoi-phuc"><?= t('login.forgot') ?></a> · <a href="/dang-ky"><?= t('nav.register') ?></a></div>
  </div>
</main>
<?php view('foot', compact('scripts')); ?>
