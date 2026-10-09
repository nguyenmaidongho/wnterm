<?php
declare(strict_types=1);

/** Tiện ích HTTP/JSON cho API. */
final class Http
{
    /** Kết thúc request bằng JSON. */
    public static function json(int $status, array $data): void
    {
        http_response_code($status);
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        header('X-Content-Type-Options: nosniff');
        echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    }

    public static function fail(int $status, string $code, string $message): void
    {
        self::json($status, ['ok' => false, 'error' => $code, 'message' => $message]);
    }

    /** Đọc body JSON (object). */
    public static function body(int $maxBytes = 8388608): array
    {
        $raw = file_get_contents('php://input', false, null, 0, $maxBytes + 1);
        if ($raw === false || $raw === '') {
            return [];
        }
        if (strlen($raw) > $maxBytes) {
            self::fail(413, 'too_large', 'Dữ liệu gửi lên quá lớn.');
        }
        $data = json_decode($raw, true);
        if (!is_array($data)) {
            self::fail(400, 'bad_json', 'Dữ liệu không hợp lệ.');
        }
        return $data;
    }

    public static function bearer(): ?string
    {
        $h = $_SERVER['HTTP_AUTHORIZATION'] ?? ($_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '');
        if ($h === '' && function_exists('getallheaders')) {
            foreach (getallheaders() as $k => $v) {
                if (strtolower($k) === 'authorization') {
                    $h = $v;
                }
            }
        }
        if (stripos($h, 'Bearer ') === 0) {
            $t = trim(substr($h, 7));
            return $t !== '' ? $t : null;
        }
        return null;
    }

    /** Dải IP của Cloudflare (https://www.cloudflare.com/ips/). */
    private const CF_RANGES = [
        '173.245.48.0/20', '103.21.244.0/22', '103.22.200.0/22', '103.31.4.0/22', '141.101.64.0/18',
        '108.162.192.0/18', '190.93.240.0/20', '188.114.96.0/20', '197.234.240.0/22', '198.41.128.0/17',
        '162.158.0.0/15', '104.16.0.0/13', '104.24.0.0/14', '172.64.0.0/13', '131.0.72.0/22',
        '2400:cb00::/32', '2606:4700::/32', '2803:f800::/32', '2405:b500::/32', '2405:8100::/32',
        '2a06:98c0::/29', '2c0f:f248::/32',
    ];

    private static function inCidr(string $ip, string $cidr): bool
    {
        [$net, $bits] = explode('/', $cidr);
        $a = @inet_pton($ip);
        $b = @inet_pton($net);
        if ($a === false || $b === false || strlen($a) !== strlen($b)) {
            return false;
        }
        $bits = (int)$bits;
        $bytes = intdiv($bits, 8);
        if (substr($a, 0, $bytes) !== substr($b, 0, $bytes)) {
            return false;
        }
        $rem = $bits % 8;
        if ($rem === 0) {
            return true;
        }
        $mask = (0xFF << (8 - $rem)) & 0xFF;
        return (ord($a[$bytes]) & $mask) === (ord($b[$bytes]) & $mask);
    }

    private static function isCloudflare(string $ip): bool
    {
        foreach (self::CF_RANGES as $r) {
            if (self::inCidr($ip, $r)) {
                return true;
            }
        }
        return false;
    }

    /**
     * IP thật của người dùng. Chỉ tin header CF-Connecting-IP khi request thực sự đi qua Cloudflare
     * (REMOTE_ADDR thuộc dải của Cloudflare) hoặc qua proxy nội bộ (nginx → Apache trên cùng máy);
     * gọi thẳng vào IP gốc thì không giả mạo được IP để lách giới hạn.
     */
    public static function ip(): string
    {
        $remote = (string)($_SERVER['REMOTE_ADDR'] ?? '0.0.0.0');
        if (wn_config('trusted_proxy') === 'cloudflare' && !empty($_SERVER['HTTP_CF_CONNECTING_IP'])) {
            $local = !filter_var($remote, FILTER_VALIDATE_IP, FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE);
            if ($local || self::isCloudflare($remote)) {
                $ip = trim((string)$_SERVER['HTTP_CF_CONNECTING_IP']);
                if (filter_var($ip, FILTER_VALIDATE_IP)) {
                    return $ip;
                }
            }
        }
        return $remote;
    }
}
