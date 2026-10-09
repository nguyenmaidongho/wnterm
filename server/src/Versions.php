<?php
declare(strict_types=1);

/**
 * Phiên bản mới nhất của từng nền tảng — nguồn duy nhất cho: endpoint GET /api/v1/version (app tự báo bản mới),
 * số phiên bản hiển thị ở trang chủ.
 *
 * Ra bản mới: (1) chép file cài mới vào public_html/downloads/ (2) đổi số 'version' trong config.php, mục 'versions'
 * (không cần sửa mã). Mặc định bên dưới là bản đã phát hành khi viết mã này.
 */
final class Versions
{
    private const DEFAULTS = [
        'windows' => ['version' => '2.4.0', 'file' => 'WNTerm-Setup.zip', 'notes' => '', 'min' => '0'],
        'android' => ['version' => '1.5.0', 'file' => 'WNTerm-android-arm64.apk', 'notes' => '', 'min' => '0'],
    ];

    /** @return array<string, array<string,mixed>> */
    public static function all(): array
    {
        return array_replace_recursive(self::DEFAULTS, (array)wn_config('versions', []));
    }

    public static function version(string $platform): string
    {
        return (string)(self::all()[$platform]['version'] ?? '');
    }

    /** Dữ liệu cho app: phiên bản, link tải, SHA-256 + kích thước file cài (để app tải trong ứng dụng và kiểm tra toàn vẹn). */
    public static function forApp(string $lang): array
    {
        $out = ['ok' => true];
        foreach (self::all() as $platform => $v) {
            $notes = $v['notes'] ?? '';
            if (is_array($notes)) {
                $notes = (string)($notes[$lang] ?? $notes['vi'] ?? reset($notes) ?: '');
            }
            $entry = [
                'version'    => (string)$v['version'],
                'minVersion' => (string)($v['min'] ?? '0'),
                'notes'      => (string)$notes,
                'url'        => url('/#tai-ve'),
            ];
            $file = (string)($v['file'] ?? '');
            $path = self::downloadsDir() . '/' . $file;
            if ($file !== '' && preg_match('/^[A-Za-z0-9._-]+$/', $file) && is_file($path)) {
                $entry['downloadUrl'] = url('/downloads/' . $file);
                $entry['size'] = filesize($path);
                $entry['sha256'] = self::sha256Cached($path);
            }
            $out[$platform] = $entry;
        }
        return $out;
    }

    private static function downloadsDir(): string
    {
        $root = rtrim((string)($_SERVER['DOCUMENT_ROOT'] ?? ''), '/\\');
        return ($root !== '' ? $root : WN_ROOT . '/public_html') . '/downloads';
    }

    /** Băm cả file mỗi lần gọi thì tốn; lưu kết quả theo (đường dẫn, kích thước, thời điểm sửa) vào thư mục tạm. */
    private static function sha256Cached(string $path): string
    {
        $key = sys_get_temp_dir() . '/wnterm-sha-' . md5($path . '|' . filesize($path) . '|' . filemtime($path)) . '.txt';
        $c = @file_get_contents($key);
        if (is_string($c) && preg_match('/^[a-f0-9]{64}$/', $c)) {
            return $c;
        }
        $h = hash_file('sha256', $path);
        @file_put_contents($key, $h, LOCK_EX);
        return $h;
    }
}
