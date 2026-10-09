<?php
declare(strict_types=1);

/**
 * Đa ngôn ngữ tối giản (vi | en) cho các trang web.
 *
 * Chọn ngôn ngữ: ?lang=vi|en  →  cookie `wnlang` (1 năm)  →  Accept-Language (vi → vi, còn lại → en).
 * Từ điển nằm ở src/lang/vi.php và src/lang/en.php (mảng key => chuỗi, có thể chứa HTML tin cậy
 * và các chỗ %s/%1$s cho sprintf). Vì từ điển là nội dung tin cậy nên t() KHÔNG escape;
 * dùng e(t(...)) khi đặt vào thuộc tính HTML.
 */
final class I18n
{
    public const LANGS = ['vi', 'en'];
    private static ?string $lang = null;
    /** @var array<string,array<string,string>> */
    private static array $dict = [];

    public static function lang(): string
    {
        if (self::$lang === null) {
            self::$lang = self::detect();
        }
        return self::$lang;
    }

    private static function detect(): string
    {
        $q = $_GET['lang'] ?? null;
        if (is_string($q) && in_array($q, self::LANGS, true)) {
            self::remember($q);
            return $q;
        }
        $c = $_COOKIE['wnlang'] ?? null;
        if (is_string($c) && in_array($c, self::LANGS, true)) {
            return $c;
        }
        $al = strtolower((string)($_SERVER['HTTP_ACCEPT_LANGUAGE'] ?? ''));
        // Lấy ngôn ngữ ưu tiên cao nhất (đứng đầu danh sách): vi → vi, còn lại → en.
        if ($al !== '' && preg_match('/^\s*([a-z]{1,8})/', $al, $m) && $m[1] === 'vi') {
            return 'vi';
        }
        return 'en';
    }

    private static function remember(string $lang): void
    {
        if (headers_sent()) {
            return;
        }
        $https = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off')
            || strtolower((string)($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '')) === 'https';
        setcookie('wnlang', $lang, [
            'expires' => time() + 365 * 86400,
            'path' => '/',
            'secure' => $https,
            'samesite' => 'Lax',
        ]);
    }

    /** Bản dịch; thiếu key trong en thì lùi về vi, thiếu nữa thì trả về chính key. */
    public static function t(string $key, ...$args): string
    {
        $lang = self::lang();
        foreach ([$lang, 'vi'] as $l) {
            if (!isset(self::$dict[$l])) {
                $f = WN_SRC . '/lang/' . $l . '.php';
                self::$dict[$l] = is_file($f) ? (array)require $f : [];
            }
            if (isset(self::$dict[$l][$key])) {
                $s = self::$dict[$l][$key];
                return $args ? vsprintf($s, $args) : $s;
            }
        }
        return $key;
    }

    /** Đường dẫn hiện tại (không query) để dựng liên kết đổi ngôn ngữ / hreflang. */
    public static function path(): string
    {
        $p = (string)parse_url((string)($_SERVER['REQUEST_URI'] ?? '/'), PHP_URL_PATH);
        $p = preg_replace('/\.php$/', '', $p) ?? $p;
        if ($p === '' || $p === '/index') {
            return '/';
        }
        return $p;
    }
}

function wn_lang(): string
{
    return I18n::lang();
}

function t(string $key, ...$args): string
{
    return I18n::t($key, ...$args);
}
