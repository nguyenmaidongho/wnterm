<?php
// Router cho `php -S` khi chạy thử cục bộ (thay cho .htaccess của Apache).
$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
if (strpos($path, '/api/v1') === 0) {
    require __DIR__ . '/../public/api/index.php';
    return true;
}
if ($path === '/sitemap.xml') {
    require __DIR__ . '/../public/sitemap.php';
    return true;
}
// Bản tiếng Anh: /en/ và /en/<trang> (giống RewriteRule trong .htaccess)
if (preg_match('#^/en(?:/([a-z0-9-]*))?$#', $path, $m)) {
    $_GET['lang'] = 'en';
    $page = $m[1] ?? '';
    $f = __DIR__ . '/../public/' . ($page === '' ? 'index' : $page) . '.php';
    if (is_file($f)) {
        require $f;
        return true;
    }
}
$file = __DIR__ . '/../public' . $path;
if ($path !== '/' && is_file($file)) {
    return false;                       // file tĩnh
}
if ($path !== '/' && is_file($file . '.php')) {
    require $file . '.php';
    return true;
}
require __DIR__ . '/../public/index.php';
return true;
