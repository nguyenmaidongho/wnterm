<?php
declare(strict_types=1);

/** Phiên đăng nhập theo thiết bị bằng token ngẫu nhiên (chỉ lưu băm SHA-256 trong CSDL). */
final class Auth
{
    public static function newToken(int $userId, string $device, string $ip): array
    {
        $token = bin2hex(random_bytes(32));
        $days = (int)wn_config('session_days', 90);
        $expires = Db::now($days * 86400);

        $st = Db::pdo()->prepare(
            'INSERT INTO sessions (user_id, token_hash, device_name, ip, created_at, last_used_at, expires_at)
             VALUES (?, ?, ?, ?, ?, ?, ?)'
        );
        $st->execute([$userId, hash('sha256', $token), mb_substr($device, 0, 120), $ip, Db::now(), Db::now(), $expires]);

        return ['token' => $token, 'expiresAt' => $expires . 'Z'];
    }

    /** Trả về [user, session] của token hiện tại hoặc kết thúc 401. */
    public static function require(): array
    {
        $token = Http::bearer();
        if ($token === null) {
            Http::fail(401, 'unauthorized', 'Cần đăng nhập.');
        }
        $pdo = Db::pdo();
        $st = $pdo->prepare(
            'SELECT s.id AS sid, s.user_id, s.expires_at, s.device_name, u.*
               FROM sessions s JOIN users u ON u.id = s.user_id
              WHERE s.token_hash = ?'
        );
        $st->execute([hash('sha256', $token)]);
        $row = $st->fetch();
        if (!$row || $row['disabled'] || strtotime($row['expires_at'] . ' UTC') < time()) {
            Http::fail(401, 'unauthorized', 'Phiên đăng nhập hết hạn, hãy đăng nhập lại.');
        }
        // Gia hạn trượt: mỗi lần dùng (tối đa 1 phút/lần để giảm ghi) đẩy hạn phiên thêm session_days ngày.
        $days = (int)wn_config('session_days', 90);
        $pdo->prepare('UPDATE sessions SET last_used_at = ?, expires_at = ? WHERE id = ? AND last_used_at < ?')
            ->execute([Db::now(), Db::now($days * 86400), $row['sid'], Db::now(-60)]);

        return [$row, (int)$row['sid']];
    }

    public static function revokeAll(int $userId, ?int $exceptSessionId = null): void
    {
        if ($exceptSessionId === null) {
            Db::pdo()->prepare('DELETE FROM sessions WHERE user_id = ?')->execute([$userId]);
        } else {
            Db::pdo()->prepare('DELETE FROM sessions WHERE user_id = ? AND id <> ?')->execute([$userId, $exceptSessionId]);
        }
    }

    /** So khớp mật khẩu (authKey) kể cả khi tài khoản không tồn tại, để không lộ thời gian phản hồi. */
    public static function verify(?string $hash, string $secret): bool
    {
        static $dummy = null;
        if ($dummy === null) {
            $dummy = password_hash('dummy-secret', PASSWORD_DEFAULT);
        }
        $ok = password_verify($secret, $hash ?? $dummy);
        return $hash !== null && $ok;
    }
}
