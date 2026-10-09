<?php
/** @var string $title */
/** @var string $desc */
/** @var string $active */
$b = brand();
if (!headers_sent()) { header('Vary: Cookie, Accept-Language', false); }
$assets = rtrim(dirname($_SERVER['SCRIPT_NAME'] ?? '/'), '/');
// Phiên bản CSS theo thời điểm sửa file để trình duyệt/Cloudflare lấy bản mới ngay.
$cssFile = rtrim((string)($_SERVER['DOCUMENT_ROOT'] ?? ''), '/') . '/assets/css/site.css';
$ver = is_file($cssFile) ? (string)filemtime($cssFile) : '2';
?>
<!doctype html>
<html lang="<?= e(wn_lang()) ?>">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title><?= e($title) ?></title>
<meta name="description" content="<?= e($desc) ?>">
<meta property="og:title" content="<?= e($title) ?>">
<meta property="og:description" content="<?= e($desc) ?>">
<meta property="og:type" content="website">
<meta property="og:locale" content="<?= wn_lang() === 'vi' ? 'vi_VN' : 'en_US' ?>">
<meta property="og:image" content="<?= e(url('assets/favicon.webp')) ?>">
<meta name="theme-color" content="#141517">
<?php if (!empty($noindex)): ?><meta name="robots" content="noindex"><?php endif; ?>
<?php $lp = I18n::path(); ?>
<link rel="alternate" hreflang="vi" href="<?= e(url($lp) . '?lang=vi') ?>">
<link rel="alternate" hreflang="en" href="<?= e(url($lp) . '?lang=en') ?>">
<link rel="alternate" hreflang="x-default" href="<?= e(url($lp)) ?>">
<link rel="icon" href="/assets/favicon.webp" type="image/webp">
<link rel="apple-touch-icon" href="/assets/apple-touch-icon.webp">
<link rel="stylesheet" href="/assets/css/site.css?v=<?= e($ver) ?>">
</head>
<body>
<nav>
  <div class="wrap">
    <a class="brand" href="/"><span class="logo"><img src="/assets/webnow-logo.webp" alt="WebNow — <?= e($b['slogan']) ?>"></span><span class="pname">WNTerm<small>WebNow Terminal</small></span></a>
    <div class="links">
      <a href="/#tinh-nang"><?= t('nav.features') ?></a>
      <a href="/#sftp">SFTP</a>
      <a href="/#di-dong"><?= t('nav.mobile') ?></a>
      <a href="/#iphone">iPhone</a>
      <a href="/#bao-mat"><?= t('nav.security') ?></a>
      <a href="/#faq"><?= t('nav.faq') ?></a>
    </div>
    <div class="actions">
      <div class="lang" role="group" aria-label="<?= e(t('nav.language')) ?>">
        <a href="<?= e($lp) ?>?lang=vi" hreflang="vi" lang="vi"<?= wn_lang() === 'vi' ? ' class="on" aria-current="true"' : '' ?>>VI</a><a href="<?= e($lp) ?>?lang=en" hreflang="en" lang="en"<?= wn_lang() === 'en' ? ' class="on" aria-current="true"' : '' ?>>EN</a>
      </div>
      <a class="btn ghost" href="/dang-nhap"><?= ($active ?? '') === 'account' ? t('nav.account') : t('nav.login') ?></a>
      <a class="btn brand" href="/#tai-ve"><?= t('nav.download') ?></a>
    </div>
  </div>
</nav>
