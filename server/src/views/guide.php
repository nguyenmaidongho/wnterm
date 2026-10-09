<?php
/**
 * Khung trang hướng dẫn (landing SEO). Biến vào:
 * $title, $desc, $h1, $intro (HTML), $sections [[h2, html], ...], $faq [[q, a], ...], $related [[href, label], ...]
 */
$vi = wn_lang() === 'vi';
$active = 'guide';
view('head', compact('title', 'desc', 'active'));
$plain = static function (string $s): string {
    return trim(preg_replace('/\s+/u', ' ', html_entity_decode(strip_tags($s), ENT_QUOTES | ENT_HTML5, 'UTF-8')) ?? '');
};
$ld = [
    '@context' => 'https://schema.org',
    '@graph' => [
        ['@type' => 'Article', 'headline' => $h1, 'description' => $desc, 'inLanguage' => wn_lang(),
         'mainEntityOfPage' => I18n::urlFor(wn_lang()),
         'publisher' => ['@type' => 'Organization', 'name' => brand()['company'], 'url' => brand()['site'],
                         'logo' => ['@type' => 'ImageObject', 'url' => url('assets/logo-n.png')]],
         'image' => url('assets/og-image.png'), 'dateModified' => gmdate('Y-m-d')],
        ['@type' => 'BreadcrumbList', 'itemListElement' => [
            ['@type' => 'ListItem', 'position' => 1, 'name' => 'WNTerm', 'item' => url($vi ? '/' : 'en/')],
            ['@type' => 'ListItem', 'position' => 2, 'name' => $h1, 'item' => I18n::urlFor(wn_lang())],
        ]],
    ],
];
if (!empty($faq)) {
    $ld['@graph'][] = ['@type' => 'FAQPage', 'mainEntity' => array_map(static fn($f) => [
        '@type' => 'Question', 'name' => $plain($f[0]),
        'acceptedAnswer' => ['@type' => 'Answer', 'text' => $plain($f[1])],
    ], $faq)];
}
?>
<main class="legal guide">
  <div class="crumbs"><a href="<?= $vi ? '/' : '/en/' ?>">WNTerm</a> › <span><?= e($h1) ?></span></div>
  <h1><?= e($h1) ?></h1>
  <p class="ver"><?= $intro ?></p>
  <div class="cta" style="justify-content:flex-start;margin:18px 0 6px">
    <a class="btn primary" href="/downloads/WNTerm-Setup.zip"><?= $vi ? 'Tải cho Windows' : 'Download for Windows' ?></a>
    <a class="btn" href="/downloads/WNTerm-android-arm64.apk"><?= $vi ? 'Tải APK Android' : 'Android APK' ?></a>
    <a class="btn" href="/app/"><?= $vi ? 'Dùng trên iPhone' : 'Use on iPhone' ?></a>
  </div>
<?php foreach ($sections as [$h2, $html]): ?>
  <h2><?= e($h2) ?></h2>
  <?= $html ?>

<?php endforeach; ?>
<?php if (!empty($faq)): ?>
  <h2><?= $vi ? 'Câu hỏi thường gặp' : 'FAQ' ?></h2>
<?php foreach ($faq as [$q, $a]): ?>
  <details><summary><?= e($q) ?></summary><p><?= $a ?></p></details>
<?php endforeach; endif; ?>
<?php if (!empty($related)): ?>
  <h2><?= $vi ? 'Xem thêm' : 'See also' ?></h2>
  <ul>
<?php foreach ($related as [$href, $label]): ?>
    <li><a href="<?= e($href) ?>"><?= e($label) ?></a></li>
<?php endforeach; ?>
  </ul>
<?php endif; ?>
</main>
<script type="application/ld+json"><?= json_encode($ld, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_HEX_TAG) ?></script>
<?php view('foot'); ?>
