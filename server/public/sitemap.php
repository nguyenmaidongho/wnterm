<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

header('Content-Type: application/xml; charset=utf-8');
header('Cache-Control: public, max-age=3600');

// Trang được lập chỉ mục: [đường dẫn vi, file nguồn dùng lấy lastmod, ưu tiên]
$pages = [
    ['/', __DIR__ . '/../src/views/home.php', '1.0'],
    ['/dieu-khoan', __DIR__ . '/dieu-khoan.php', '0.3'],
];
$lastmodOf = static function (string $f): string {
    $t = max((int)@filemtime($f), (int)@filemtime(WN_SRC . '/lang/vi.php'), (int)@filemtime(WN_SRC . '/lang/en.php'));
    return gmdate('Y-m-d', $t ?: time());
};
echo '<?xml version="1.0" encoding="UTF-8"?>' . "\n";
?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
<?php foreach ($pages as [$p, $src, $prio]):
    $vi = url($p);
    $en = url('en' . ($p === '/' ? '/' : $p));
    $lm = $lastmodOf($src);
    foreach ([$vi, $en] as $loc): ?>
  <url>
    <loc><?= e($loc) ?></loc>
    <lastmod><?= $lm ?></lastmod>
    <priority><?= $prio ?></priority>
    <xhtml:link rel="alternate" hreflang="vi" href="<?= e($vi) ?>"/>
    <xhtml:link rel="alternate" hreflang="en" href="<?= e($en) ?>"/>
    <xhtml:link rel="alternate" hreflang="x-default" href="<?= e($vi) ?>"/>
  </url>
<?php endforeach; endforeach; ?>
</urlset>
