<?php
declare(strict_types=1);

/**
 * Tạo/cập nhật các bảng CSDL.  Chạy:  php tools/migrate.php
 * (Đặt biến môi trường WNTERM_CONFIG để dùng file cấu hình khác.)
 */
if (PHP_SAPI !== 'cli') {
    http_response_code(403);
    exit('CLI only');
}

require __DIR__ . '/../src/bootstrap.php';

$sql = file_get_contents(__DIR__ . '/../schema.sql');
$pdo = Db::pdo();
$count = 0;
foreach (array_filter(array_map('trim', explode(';', (string)$sql))) as $stmt) {
    // bỏ dòng chú thích đầu câu lệnh
    $clean = trim(preg_replace('/^--.*$/m', '', $stmt));
    if ($clean === '') {
        continue;
    }
    $pdo->exec($clean);
    $count++;
}
echo "OK: đã chạy $count câu lệnh.\n";
foreach ($pdo->query('SHOW TABLES')->fetchAll(PDO::FETCH_NUM) as $t) {
    echo ' - ' . $t[0] . "\n";
}
