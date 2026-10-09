<?php
declare(strict_types=1);

/** Giới hạn số lần thử (chống dò mật khẩu / spam đăng ký) theo cửa sổ thời gian cố định. */
final class RateLimit
{
    /**
     * Ghi nhận 1 lần thử cho khóa $key. Trả false nếu đã vượt $max lần trong $windowSeconds.
     */
    public static function hit(string $key, int $max, int $windowSeconds): bool
    {
        $pdo = Db::pdo();
        $now = time();
        $key = substr($key, 0, 190);

        // Một câu lệnh nguyên tử (không transaction / FOR UPDATE → không deadlock khi nhiều request cùng lúc).
        // Lưu ý: MySQL gán theo thứ tự, nên tính hits trước khi đổi window_start.
        $pdo->prepare(
            'INSERT INTO rate_limits (k, window_start, hits) VALUES (?, ?, 1)
             ON DUPLICATE KEY UPDATE
               hits = IF(? - window_start >= ?, 1, hits + 1),
               window_start = IF(? - window_start >= ?, ?, window_start)'
        )->execute([$key, $now, $now, $windowSeconds, $now, $windowSeconds, $now]);

        $st = $pdo->prepare('SELECT hits FROM rate_limits WHERE k = ?');
        $st->execute([$key]);
        return (int)$st->fetchColumn() <= $max;
    }

    /** Xóa bộ đếm (vd. sau khi đăng nhập đúng). */
    public static function reset(string $key): void
    {
        Db::pdo()->prepare('DELETE FROM rate_limits WHERE k = ?')->execute([substr($key, 0, 190)]);
    }

    /** Dọn các bản ghi cũ (gọi thỉnh thoảng). */
    public static function purge(int $olderThanSeconds = 86400): void
    {
        Db::pdo()->prepare('DELETE FROM rate_limits WHERE window_start < ?')->execute([time() - $olderThanSeconds]);
    }
}
