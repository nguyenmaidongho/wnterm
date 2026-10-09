<?php
declare(strict_types=1);

/**
 * Quản trị tài khoản khi có báo cáo lạm dụng.   Chạy:  php tools/admin.php <lệnh> [tham số]
 *
 *   info <email>       thông tin tài khoản + các phiên đăng nhập gần đây (IP, thiết bị)
 *   disable <email>    khóa tài khoản và thu hồi mọi phiên (relay ngừng nhận trong tối đa ~5 phút)
 *   enable <email>     mở khóa
 *   recent [n]         n tài khoản mới nhất (mặc định 20) — để soi đợt đăng ký bất thường
 */
if (PHP_SAPI !== 'cli') {
    http_response_code(403);
    exit('CLI only');
}

require __DIR__ . '/../src/bootstrap.php';

$pdo = Db::pdo();
$cmd = $argv[1] ?? '';
$arg = isset($argv[2]) ? strtolower(trim($argv[2])) : '';

function find_user(PDO $pdo, string $email): array
{
    $st = $pdo->prepare('SELECT id, email, created_at, last_login_at, disabled FROM users WHERE email = ?');
    $st->execute([$email]);
    $u = $st->fetch();
    if (!$u) {
        fwrite(STDERR, "Không thấy tài khoản: $email\n");
        exit(1);
    }
    return $u;
}

switch ($cmd) {
    case 'info':
        $u = find_user($pdo, $arg);
        echo "{$u['email']}  id={$u['id']}  tạo={$u['created_at']}  đăng nhập cuối=" . ($u['last_login_at'] ?? '-') . '  ' . ($u['disabled'] ? 'ĐÃ KHÓA' : 'hoạt động') . "\n";
        $st = $pdo->prepare('SELECT device_name, ip, created_at, last_used_at FROM sessions WHERE user_id = ? ORDER BY last_used_at DESC LIMIT 20');
        $st->execute([(int)$u['id']]);
        foreach ($st->fetchAll() as $s) {
            echo "  - {$s['device_name']}  ip={$s['ip']}  tạo={$s['created_at']}  dùng cuối={$s['last_used_at']}\n";
        }
        break;

    case 'disable':
    case 'enable':
        $u = find_user($pdo, $arg);
        $dis = $cmd === 'disable' ? 1 : 0;
        $pdo->prepare('UPDATE users SET disabled = ? WHERE id = ?')->execute([$dis, (int)$u['id']]);
        if ($dis) {
            Auth::revokeAll((int)$u['id']);
        }
        echo ($dis ? 'Đã khóa' : 'Đã mở khóa') . " {$u['email']}\n";
        break;

    case 'recent':
        $n = max(1, min(200, (int)($argv[2] ?? 20)));
        foreach ($pdo->query('SELECT email, created_at, last_login_at, disabled FROM users ORDER BY id DESC LIMIT ' . $n)->fetchAll() as $r) {
            echo "{$r['created_at']}  {$r['email']}" . ($r['disabled'] ? '  [KHÓA]' : '') . "\n";
        }
        break;

    default:
        echo "Dùng: php tools/admin.php info|disable|enable <email>  |  recent [n]\n";
        exit(1);
}
