<?php
declare(strict_types=1);

/**
 * Khởi tạo chung: nạp cấu hình, autoload lớp trong src/, hàm tiện ích.
 * Thư mục `src/` và `config.php` nằm NGOÀI thư mục web public.
 */

define('WN_ROOT', dirname(__DIR__));          // thư mục chứa src/, config.php, storage/
define('WN_SRC', __DIR__);

spl_autoload_register(function (string $class): void {
    $file = WN_SRC . '/' . str_replace('\\', '/', $class) . '.php';
    if (is_file($file)) {
        require $file;
    }
});

require_once WN_SRC . '/I18n.php';

function wn_config(?string $key = null, $default = null)
{
    static $cfg = null;
    if ($cfg === null) {
        $path = getenv('WNTERM_CONFIG') ?: WN_ROOT . '/config.php';
        if (!is_file($path)) {
            http_response_code(500);
            header('Content-Type: text/plain; charset=utf-8');
            echo "Thiếu config.php (copy từ config.sample.php).";
            exit;
        }
        $cfg = require $path;
    }
    if ($key === null) {
        return $cfg;
    }
    $cur = $cfg;
    foreach (explode('.', $key) as $part) {
        if (!is_array($cur) || !array_key_exists($part, $cur)) {
            return $default;
        }
        $cur = $cur[$part];
    }
    return $cur;
}

/** Escape HTML. */
function e($s): string
{
    return htmlspecialchars((string)$s, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

/** URL tuyệt đối tới tài nguyên của site. */
function url(string $path = ''): string
{
    return rtrim((string)wn_config('app_url', ''), '/') . '/' . ltrim($path, '/');
}

/** Hiển thị một view trong src/views với biến truyền vào. */
function view(string $name, array $vars = []): void
{
    extract($vars, EXTR_SKIP);
    require WN_SRC . '/views/' . $name . '.php';
}

/** Thông tin thương hiệu lấy từ webnow.vn. */
function brand(): array
{
    return [
        'company' => 'Công ty TNHH Giải pháp số WebNow',
        'short'   => 'WebNow',
        'slogan'  => 'Nay code mai giao',
        'site'    => 'https://webnow.vn',
        'phone'   => '02862 722 577',
        'phone2'  => '0375 445 916',
        'email'   => 'contact@webnow.vn',
        'address' => '81 Liên khu 5-11-12, Phường Bình Trị Đông, Quận Bình Tân, TP. Hồ Chí Minh',
        'mst'     => '0318865407',
        'facebook' => 'https://www.facebook.com/webnow.vn',
        'tiktok'   => 'https://www.tiktok.com/@webnow.vn',
        'zalo'     => 'https://zalo.me/575373668670610552',
    ];
}
