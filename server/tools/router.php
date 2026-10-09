<?php
// Router cho `php -S` khi chạy thử cục bộ (thay cho .htaccess của Apache).
$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
if (strpos($path, '/api/v1') === 0) {
    require __DIR__ . '/../public/api/index.php';
    return true;
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
