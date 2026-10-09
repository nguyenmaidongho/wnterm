<?php
declare(strict_types=1);

/**
 * Kiểm thử đầu-cuối API qua HTTP, đồng thời là cài đặt THAM CHIẾU của giao thức mã hóa phía người dùng.
 * Chạy:  php tools/test_api.php http://127.0.0.1:8099
 */
if (PHP_SAPI !== 'cli') {
    exit('CLI only');
}

$base = rtrim($argv[1] ?? 'http://127.0.0.1:8099', '/') . '/api/v1';
$iter = 200000;          // thấp hơn mặc định cho nhanh khi chạy thử (server chấp nhận >= 200000)
$fail = 0;
$total = 0;

function check(string $name, bool $ok, string $detail = ''): void
{
    global $fail, $total;
    $total++;
    if (!$ok) {
        $fail++;
    }
    echo ($ok ? '[ OK ] ' : '[FAIL] ') . $name . ($ok || $detail === '' ? '' : "  → $detail") . "\n";
}

function call(string $method, string $path, ?array $body = null, ?string $token = null): array
{
    global $base;
    $ch = curl_init($base . $path);
    $h = ['Content-Type: application/json'];
    if (getenv('WN_HOST')) {            // gọi thẳng máy chủ gốc (bỏ qua Cloudflare): WN_HOST=tên-miền, base = https://127.0.0.1
        $h[] = 'Host: ' . getenv('WN_HOST');
    }
    if ($token) {
        $h[] = 'Authorization: Bearer ' . $token;
    }
    curl_setopt_array($ch, [
        CURLOPT_CUSTOMREQUEST  => $method,
        CURLOPT_HTTPHEADER     => $h,
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_TIMEOUT        => 60,
        CURLOPT_SSL_VERIFYPEER => !getenv('WN_HOST'),
        CURLOPT_SSL_VERIFYHOST => getenv('WN_HOST') ? 0 : 2,
    ]);
    if ($body !== null) {
        curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($body));
    }
    $resp = curl_exec($ch);
    $code = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
    curl_close($ch);
    return [$code, json_decode((string)$resp, true) ?? ['raw' => $resp]];
}

// ===== Giao thức mã hóa phía người dùng (tham chiếu) =====

function b64(string $s): string { return base64_encode($s); }

function derive(string $password, string $email, int $iter): array
{
    $master = hash_pbkdf2('sha256', $password, 'wnterm:v1:' . strtolower(trim($email)), $iter, 32, true);
    return [
        'auth' => hash_hkdf('sha256', $master, 32, 'wnterm-auth'),
        'enc'  => hash_hkdf('sha256', $master, 32, 'wnterm-enc'),
    ];
}

function gcmSeal(string $key, string $plain): string
{
    $iv = random_bytes(12);
    $ct = openssl_encrypt($plain, 'aes-256-gcm', $key, OPENSSL_RAW_DATA, $iv, $tag, '', 16);
    return b64($iv . $ct . $tag);
}

function gcmOpen(string $key, string $b64): ?string
{
    $raw = base64_decode($b64, true);
    if ($raw === false || strlen($raw) < 28) {
        return null;
    }
    $iv = substr($raw, 0, 12);
    $tag = substr($raw, -16);
    $ct = substr($raw, 12, -16);
    $p = openssl_decrypt($ct, 'aes-256-gcm', $key, OPENSSL_RAW_DATA, $iv, $tag);
    return $p === false ? null : $p;
}

function newRecoveryCode(): array
{
    $bytes = random_bytes(20);
    return [
        'bytes' => $bytes,
        'auth'  => hash_hkdf('sha256', $bytes, 32, 'wnterm-recovery-auth'),
        'enc'   => hash_hkdf('sha256', $bytes, 32, 'wnterm-recovery-enc'),
    ];
}

// ===== Kịch bản =====

$email = 'test+' . bin2hex(random_bytes(3)) . '@example.com';
$pw = 'Mật-khẩu thử 123!';

echo "== Đăng ký ==\n";
$k = derive($pw, $email, $iter);
$vaultKey = random_bytes(32);
$rec = newRecoveryCode();
$reg = [
    'email' => $email, 'authKey' => b64($k['auth']), 'kdfIterations' => $iter,
    'wrappedPw' => gcmSeal($k['enc'], $vaultKey),
    'recoveryAuth' => b64($rec['auth']), 'wrappedRecovery' => gcmSeal($rec['enc'], $vaultKey),
    'deviceName' => 'Máy thử', 'tos' => '2026-10-09',
];
[$c, $r] = call('POST', '/register', $reg);
check('đăng ký mới → 201', $c === 201 && !empty($r['token']), json_encode($r));
$token1 = $r['token'] ?? '';

[$c, $r] = call('POST', '/register', $reg);
check('đăng ký trùng email → 409', $c === 409 && ($r['error'] ?? '') === 'email_taken', json_encode($r));

$bad = $reg; $bad['email'] = 'khong-hop-le';
[$c, $r] = call('POST', '/register', $bad);
check('email sai định dạng → 422', $c === 422, json_encode($r));

$bad = $reg; $bad['email'] = 'x' . $email; $bad['authKey'] = 'abc';
[$c, $r] = call('POST', '/register', $bad);
check('authKey sai độ dài → 422', $c === 422, json_encode($r));

echo "== Đăng nhập ==\n";
[$c, $r] = call('POST', '/prelogin', ['email' => $email]);
check('prelogin trả số vòng lặp', $c === 200 && ($r['kdfIterations'] ?? 0) === $iter, json_encode($r));

[$c, $r] = call('POST', '/login', ['email' => $email, 'authKey' => b64(derive('sai mật khẩu', $email, $iter)['auth'])]);
check('sai mật khẩu → 401', $c === 401, json_encode($r));

[$c, $r] = call('POST', '/login', ['email' => 'khongco@example.com', 'authKey' => b64($k['auth'])]);
check('email không tồn tại → 401 (cùng thông báo)', $c === 401 && ($r['error'] ?? '') === 'bad_credentials', json_encode($r));

[$c, $r] = call('POST', '/login', ['email' => strtoupper($email), 'authKey' => b64($k['auth']), 'deviceName' => 'Điện thoại thử']);
check('đăng nhập đúng (email viết hoa) → 200', $c === 200 && !empty($r['token']), json_encode($r));
$token2 = $r['token'] ?? '';
$opened = gcmOpen($k['enc'], (string)($r['wrappedPw'] ?? ''));
check('giải được khóa dữ liệu từ wrappedPw', $opened === $vaultKey);

[$c, $r] = call('GET', '/me');
check('GET /me không token → 401', $c === 401);
[$c, $r] = call('GET', '/me', null, $token2);
check('GET /me có token → email đúng', $c === 200 && ($r['email'] ?? '') === $email, json_encode($r));

echo "== Đồng bộ dữ liệu (vault) ==\n";
[$c, $r] = call('GET', '/vault', null, $token1);
check('vault trống: version 0', $c === 200 && ($r['version'] ?? -1) === 0);

$data1 = base64_decode(gcmSeal($vaultKey, json_encode(['sessions' => [['name' => 'web-1', 'password' => 'bí mật']]])));   // byte thô của khối dữ liệu
[$c, $r] = call('PUT', '/vault', ['baseVersion' => 0, 'blob' => b64($data1)], $token1);
check('ghi vault v1', $c === 200 && ($r['version'] ?? 0) === 1, json_encode($r));

[$c, $r] = call('GET', '/vault', null, $token2);
check('thiết bị khác đọc đúng v1', $c === 200 && ($r['version'] ?? 0) === 1 && base64_decode($r['blob'] ?? '') === $data1);
$plain = gcmOpen($vaultKey, (string)($r['blob'] ?? ''));   // blob trả về là base64 của khối đã mã hóa
check('giải mã được nội dung vault', is_string($plain) && strpos($plain, 'web-1') !== false);

$data2 = base64_decode(gcmSeal($vaultKey, json_encode(['sessions' => []])));
[$c, $r] = call('PUT', '/vault', ['baseVersion' => 1, 'blob' => b64($data2)], $token2);
check('ghi vault v2', $c === 200 && ($r['version'] ?? 0) === 2, json_encode($r));

[$c, $r] = call('PUT', '/vault', ['baseVersion' => 1, 'blob' => b64($data1)], $token1);
check('ghi đè bằng bản cũ → 409 conflict', $c === 409 && ($r['serverVersion'] ?? 0) === 2, json_encode($r));
[$c, $r] = call('GET', '/vault', null, $token2);
check('sau xung đột, bản hiện hành v2 vẫn nguyên vẹn', $c === 200 && ($r['version'] ?? 0) === 2 && base64_decode($r['blob'] ?? '') === $data2, json_encode($r));
[$c, $r] = call('GET', '/vault/history', null, $token1);
check('lịch sử có v2, v1 (mới trước)', $c === 200 && array_column($r['versions'] ?? [], 'version') === [2, 1], json_encode($r));
[$c, $r] = call('GET', '/vault?version=1', null, $token1);
check('đọc lại được phiên bản cũ v1', $c === 200 && ($r['version'] ?? 0) === 1 && ($r['current'] ?? 0) === 2 && base64_decode($r['blob'] ?? '') === $data1, json_encode($r));
[$c] = call('GET', '/vault?version=99', null, $token1);
check('phiên bản không tồn tại → 404', $c === 404);

for ($v = 2; $v < 6; $v++) {
    [$c, $r] = call('PUT', '/vault', ['baseVersion' => $v, 'blob' => gcmSeal($vaultKey, "v$v")], $token1);
}
check('ghi liên tiếp tới v6', ($r['version'] ?? 0) === 6, json_encode($r));

[$c, $r] = call('PUT', '/vault', ['baseVersion' => 6, 'blob' => '###'], $token1);
check('blob không phải base64 → 422', $c === 422);

echo "== Thiết bị ==\n";
[$c, $r] = call('GET', '/devices', null, $token1);
check('liệt kê 2 thiết bị, 1 hiện tại', $c === 200 && count($r['devices'] ?? []) === 2 && count(array_filter($r['devices'], fn($d) => $d['current'])) === 1, json_encode($r));
$otherId = 0;
foreach ($r['devices'] ?? [] as $d) { if (!$d['current']) { $otherId = $d['id']; } }
[$c] = call('DELETE', '/devices?id=' . $otherId, null, $token1);
[$c2] = call('GET', '/me', null, $token2);
check('thu hồi thiết bị → token kia bị vô hiệu (401)', $c === 200 && $c2 === 401);

echo "== Đổi mật khẩu ==\n";
$newPw = 'Mật khẩu mới 456?';
$k2 = derive($newPw, $email, $iter);
[$c, $r] = call('POST', '/password/change', ['oldAuthKey' => b64(derive('sai', $email, $iter)['auth']), 'newAuthKey' => b64($k2['auth']), 'newWrappedPw' => gcmSeal($k2['enc'], $vaultKey)], $token1);
check('đổi mật khẩu với mật khẩu cũ sai → 401', $c === 401, json_encode($r));
[$c, $r] = call('POST', '/password/change', ['oldAuthKey' => b64($k['auth']), 'newAuthKey' => b64($k2['auth']), 'newWrappedPw' => gcmSeal($k2['enc'], $vaultKey)], $token1);
check('đổi mật khẩu đúng → 200', $c === 200, json_encode($r));
[$c, $r] = call('POST', '/login', ['email' => $email, 'authKey' => b64($k['auth'])]);
check('mật khẩu cũ không còn dùng được', $c === 401);
[$c, $r] = call('POST', '/login', ['email' => $email, 'authKey' => b64($k2['auth'])]);
check('mật khẩu mới đăng nhập được', $c === 200);
check('khóa dữ liệu vẫn giải được bằng mật khẩu mới', gcmOpen($k2['enc'], (string)($r['wrappedPw'] ?? '')) === $vaultKey);
$token3 = $r['token'] ?? '';

echo "== Khôi phục bằng mã ==\n";
[$c, $r] = call('POST', '/recover/start', ['email' => $email, 'recoveryAuth' => b64(random_bytes(32))]);
check('mã khôi phục sai → 401', $c === 401, json_encode($r));
[$c, $r] = call('POST', '/recover/start', ['email' => $email, 'recoveryAuth' => b64($rec['auth'])]);
check('mã khôi phục đúng → nhận wrappedRecovery', $c === 200 && !empty($r['wrappedRecovery']), json_encode($r));
$recovered = gcmOpen($rec['enc'], (string)($r['wrappedRecovery'] ?? ''));
check('giải được khóa dữ liệu bằng mã khôi phục', $recovered === $vaultKey);
$pw3 = 'Quên rồi, đặt lại 789';
$k3 = derive($pw3, $email, $iter);
[$c, $r] = call('POST', '/recover/reset', ['email' => $email, 'recoveryAuth' => b64($rec['auth']), 'authKey' => b64($k3['auth']), 'wrappedPw' => gcmSeal($k3['enc'], $recovered)]);
check('đặt lại mật khẩu bằng mã khôi phục → 200', $c === 200, json_encode($r));
[$c] = call('GET', '/me', null, $token3);
check('mọi phiên cũ bị đăng xuất sau khi khôi phục', $c === 401);
[$c, $r] = call('POST', '/login', ['email' => $email, 'authKey' => b64($k3['auth'])]);
check('đăng nhập bằng mật khẩu đặt lại', $c === 200);
$token4 = $r['token'] ?? '';
[$c, $r] = call('GET', '/vault', null, $token4);
check('dữ liệu vault còn nguyên sau khôi phục', $c === 200 && ($r['version'] ?? 0) === 6);

echo "== Giới hạn tốc độ ==\n";
$got429 = false;
for ($i = 0; $i < 12; $i++) {
    [$c] = call('POST', '/login', ['email' => $email, 'authKey' => b64(random_bytes(32))]);
    if ($c === 429) { $got429 = true; break; }
}
check('đăng nhập sai nhiều lần → 429', $got429);

echo "== Xóa tài khoản ==\n";
[$c, $r] = call('POST', '/account/delete', ['authKey' => b64(random_bytes(32))], $token4);
check('xóa với mật khẩu sai → 401', $c === 401);
[$c, $r] = call('POST', '/account/delete', ['authKey' => b64($k3['auth'])], $token4);
check('xóa tài khoản → 200', $c === 200, json_encode($r));
[$c] = call('GET', '/me', null, $token4);
check('token sau khi xóa không dùng được', $c === 401);

echo "\nKết quả: " . ($total - $fail) . "/$total đạt" . ($fail ? "  — CÓ $fail LỖI" : '') . "\n";
exit($fail ? 1 : 0);
