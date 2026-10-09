<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$open = (bool)wn_config('registration_open', true);
$title = t('reg.title');
$desc = t('reg.desc');
$active = 'register';
$scripts = ['wn-crypto.js', 'register.js'];
$turnstileKey = (string)wn_config('turnstile.site_key', '');
if ($turnstileKey !== '') {
    $extraHead = '<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>';
}
$noindex = true;
view('head', compact('title', 'desc', 'active', 'noindex'));
if (!empty($extraHead)) { echo $extraHead; }
?>
<main class="authwrap">
  <div class="auth" id="card">
    <?php if (!$open): ?>
      <h1><?= t('reg.closed.h1') ?></h1>
      <p class="sub"><?= t('reg.closed.p') ?></p>
      <a class="btn block" href="/"><?= t('reg.closed.btn') ?></a>
    <?php else: ?>

    <!-- Bước 1: nhập thông tin -->
    <section id="step1">
      <div class="steps"><i class="on"></i><i></i></div>
      <h1><?= t('reg.h1') ?></h1>
      <p class="sub"><?= t('reg.sub') ?></p>

      <div class="safe">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>
        <div><?= t('reg.safe') ?></div>
      </div>

      <div id="err" class="alert err" hidden></div>
      <noscript><div class="alert err"><?= t('reg.noscript') ?></div></noscript>

      <form id="form" autocomplete="on" novalidate>
        <div class="field">
          <label for="email"><?= t('reg.email') ?></label>
          <input id="email" name="email" type="email" autocomplete="username" placeholder="<?= e(t('reg.email_ph')) ?>" required>
        </div>
        <div class="field">
          <label for="pw"><?= t('reg.pw') ?></label>
          <div class="pwrow">
            <input id="pw" name="pw" type="password" autocomplete="new-password" minlength="10" placeholder="<?= e(t('reg.pw_ph')) ?>" required>
            <button type="button" id="showpw" aria-label="<?= e(t('reg.show_aria')) ?>"><?= t('reg.show') ?></button>
          </div>
          <div class="meter"><i id="meter"></i></div>
          <div class="hint" id="pwhint"><?= t('reg.pwhint') ?></div>
        </div>
        <div class="field">
          <label for="pw2"><?= t('reg.pw2') ?></label>
          <input id="pw2" name="pw2" type="password" autocomplete="new-password" required>
        </div>
        <label class="check"><input id="agree" type="checkbox"> <span><?= t('reg.agree') ?></span></label>
        <label class="check"><input id="tos" type="checkbox" data-tos="<?= e(Api::TOS_VERSION) ?>"> <span><?= t('reg.tos') ?></span></label>
        <?php if ($turnstileKey !== ''): ?>
          <div class="cf-turnstile" data-sitekey="<?= e($turnstileKey) ?>" data-theme="dark" style="margin:12px 0"></div>
        <?php endif; ?>
        <button class="btn brand block" id="submit" type="submit"><?= t('reg.submit') ?></button>
      </form>
      <div class="alt"><?= t('reg.have') ?></div>
    </section>

    <!-- Bước 2: mã khôi phục -->
    <section id="step2" hidden>
      <div class="steps"><i class="on"></i><i class="on"></i></div>
      <h1><?= t('reg.s2.h1') ?></h1>
      <p class="sub"><?= t('reg.s2.sub') ?></p>
      <div class="alert warn"><?= t('reg.s2.warn') ?></div>
      <div class="recovery">
        <code id="code"></code>
        <div class="row">
          <button class="btn" type="button" id="copy"><?= t('reg.s2.copy') ?></button>
          <button class="btn" type="button" id="dl"><?= t('reg.s2.dl') ?></button>
          <button class="btn" type="button" id="print"><?= t('reg.s2.print') ?></button>
        </div>
      </div>
      <label class="check"><input id="saved" type="checkbox"> <span><?= t('reg.s2.saved') ?></span></label>
      <button class="btn brand block" id="done" type="button" disabled><?= t('reg.s2.done') ?></button>
    </section>

    <?php endif; ?>
  </div>
</main>
<?php view('foot', compact('scripts')); ?>
