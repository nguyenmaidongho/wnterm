<?php
declare(strict_types=1);

/**
 * Lịch sử phiên bản vault của người dùng: ghi nhận mỗi lần đồng bộ, liệt kê để khôi phục bản cũ,
 * và dọn bớt theo chính sách giữ (ít nhất `keep_versions` bản gần nhất + mọi bản trong `keep_days` ngày,
 * tối đa `max_versions` bản). Dùng chung cho kiểu lưu trữ db và s3.
 */
final class VaultHistory
{
    public static function record(int $userId, int $version, int $size, string $device): void
    {
        Db::pdo()->prepare(
            'INSERT INTO vault_history (user_id, version, created_at, size, device) VALUES (?, ?, ?, ?, ?)
             ON DUPLICATE KEY UPDATE created_at = VALUES(created_at), size = VALUES(size), device = VALUES(device)'
        )->execute([$userId, $version, Db::now(), $size, mb_substr($device, 0, 120)]);
    }

    /** Các phiên bản còn giữ, mới nhất trước. */
    public static function list(int $userId): array
    {
        $st = Db::pdo()->prepare('SELECT version, created_at, size, device FROM vault_history WHERE user_id = ? ORDER BY version DESC');
        $st->execute([$userId]);
        return $st->fetchAll();
    }

    public static function exists(int $userId, int $version): bool
    {
        $st = Db::pdo()->prepare('SELECT 1 FROM vault_history WHERE user_id = ? AND version = ?');
        $st->execute([$userId, $version]);
        return $st->fetchColumn() !== false;
    }

    /** Xóa các phiên bản cũ ngoài chính sách giữ (không bao giờ xóa bản hiện hành). */
    public static function prune(int $userId, int $currentVersion, BlobStoreInterface $store): void
    {
        $keep = max(1, (int)wn_config('keep_versions', 30));
        $days = max(0, (int)wn_config('keep_days', 30));
        $max = max($keep, (int)wn_config('max_versions', 200));
        $cutoff = Db::now(-$days * 86400);

        $drop = [];
        foreach (self::list($userId) as $i => $row) {
            $v = (int)$row['version'];
            if ($v === $currentVersion) {
                continue;
            }
            $old = $i >= $keep && (string)$row['created_at'] < $cutoff;
            if ($old || $i >= $max) {
                $drop[] = $v;
            }
        }
        self::deleteVersions($userId, $drop, $store);
    }

    /** Xóa toàn bộ dữ liệu vault của người dùng (khi xóa tài khoản). */
    public static function deleteAll(int $userId, int $currentVersion, BlobStoreInterface $store): void
    {
        $versions = array_map(fn($r) => (int)$r['version'], self::list($userId));
        if ($currentVersion > 0 && !in_array($currentVersion, $versions, true)) {
            $versions[] = $currentVersion;
        }
        self::deleteVersions($userId, $versions, $store);
    }

    private static function deleteVersions(int $userId, array $versions, BlobStoreInterface $store): void
    {
        $del = Db::pdo()->prepare('DELETE FROM vault_history WHERE user_id = ? AND version = ?');
        foreach ($versions as $v) {
            try {
                $store->delete($userId, $v);
            } catch (Throwable $e) {
                error_log('[wnterm] prune blob ' . $userId . '/' . $v . ': ' . $e->getMessage());
                continue; // giữ dòng lịch sử để lần sau thử xóa lại
            }
            $del->execute([$userId, $v]);
        }
    }
}
