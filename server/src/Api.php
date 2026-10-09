<?php
declare(strict_types=1);

/**
 * API tài khoản + đồng bộ (JSON). Mọi bí mật (khóa dữ liệu) được mã hóa ở phía người dùng;
 * server chỉ nhận authKey (đã băm lại khi lưu) và các khối dữ liệu đã mã hóa.
 *
 * Quy ước mã hóa phía người dùng (app/web dùng chung):
 *   masterKey   = PBKDF2-HMAC-SHA256(mật khẩu UTF-8, salt "wnterm:v1:" + email thường, N vòng, 32 byte)
 *   authKey     = HKDF-SHA256(masterKey, info "wnterm-auth", 32)  -> gửi lên server (base64)
 *   encKey      = HKDF-SHA256(masterKey, info "wnterm-enc", 32)   -> KHÔNG gửi
 *   vaultKey    = 32 byte ngẫu nhiên; wrappedPw = AES-256-GCM(encKey, vaultKey), dạng base64(iv12 | ciphertext | tag16)
 *   mã khôi phục = 20 byte ngẫu nhiên; recoveryAuth/recoveryEnc = HKDF(mã, "wnterm-recovery-auth"/"wnterm-recovery-enc")
 */
final class Api
{
    public static function handle(): void
    {
        $method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
        $path = parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH) ?: '/';
        $path = preg_replace('#^.*?/api/v1#', '', $path);
        $path = '/' . trim((string)$path, '/');

        header('Access-Control-Allow-Origin: ' . (wn_config('app_url') ?: '*'));
        header('Access-Control-Allow-Headers: Authorization, Content-Type');
        header('Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS');
        header('Vary: Origin');
        if ($method === 'OPTIONS') {
            http_response_code(204);
            exit;
        }

        try {
            self::housekeeping();
            switch ($method . ' ' . $path) {
                case 'GET /health':           self::health(); break;
                case 'POST /register':        self::register(); break;
                case 'POST /prelogin':        self::prelogin(); break;
                case 'POST /login':           self::login(); break;
                case 'POST /logout':          self::logout(); break;
                case 'GET /me':               self::me(); break;
                case 'GET /keys':             self::keys(); break;
                case 'GET /devices':          self::devices(); break;
                case 'DELETE /devices':       self::revokeDevice(); break;
                case 'GET /vault':            self::vaultGet(); break;
                case 'PUT /vault':            self::vaultPut(); break;
                case 'GET /vault/history':    self::vaultHistory(); break;
                case 'POST /recover/start':   self::recoverStart(); break;
                case 'POST /recover/reset':  self::recoverReset(); break;
                case 'POST /password/change': self::passwordChange(); break;
                case 'POST /account/delete':  self::accountDelete(); break;
                default:
                    Http::fail(404, 'not_found', 'Không có đường dẫn này.');
            }
        } catch (Throwable $e) {
            error_log('[wnterm] ' . get_class($e) . ': ' . $e->getMessage() . ' @ ' . $e->getFile() . ':' . $e->getLine());
            Http::fail(500, 'server_error', 'Lỗi máy chủ, vui lòng thử lại sau.');
        }
    }

    /** Thỉnh thoảng (~1% request) dọn bộ đếm giới hạn và phiên đăng nhập đã hết hạn. */
    private static function housekeeping(): void
    {
        if (random_int(1, 100) !== 1) {
            return;
        }
        try {
            RateLimit::purge(86400);
            Db::pdo()->prepare('DELETE FROM sessions WHERE expires_at < ?')->execute([Db::now()]);
        } catch (Throwable $e) {
            error_log('[wnterm] housekeeping: ' . $e->getMessage());
        }
    }

    // ===== Kiểm tra dữ liệu đầu vào =====

    private static function email(array $b): string
    {
        $e = strtolower(trim((string)($b['email'] ?? '')));
        if ($e === '' || strlen($e) > 190 || !filter_var($e, FILTER_VALIDATE_EMAIL)) {
            Http::fail(422, 'bad_email', 'Email không hợp lệ.');
        }
        return $e;
    }

    /** Khóa 32 byte dạng base64 (44 ký tự). */
    private static function key32(array $b, string $field): string
    {
        $v = (string)($b[$field] ?? '');
        $raw = base64_decode($v, true);
        if ($raw === false || strlen($raw) !== 32 || strlen($v) !== 44) {
            Http::fail(422, 'bad_' . $field, 'Dữ liệu không hợp lệ.');
        }
        return $v;
    }

    private static function wrapped(array $b, string $field): string
    {
        $v = (string)($b[$field] ?? '');
        $raw = base64_decode($v, true);
        // iv(12) + khóa(32) + tag(16) = 60 byte
        if ($raw === false || strlen($raw) < 60 || strlen($raw) > 256) {
            Http::fail(422, 'bad_' . $field, 'Dữ liệu không hợp lệ.');
        }
        return $v;
    }

    private static function user(string $email): ?array
    {
        $st = Db::pdo()->prepare('SELECT * FROM users WHERE email = ?');
        $st->execute([$email]);
        $u = $st->fetch();
        return $u ?: null;
    }

    // ===== Endpoint =====

    private static function health(): void
    {
        Db::pdo()->query('SELECT 1');
        Http::json(200, ['ok' => true, 'service' => 'wnterm', 'time' => gmdate('c')]);
    }

    private static function register(): void
    {
        if (!wn_config('registration_open', true)) {
            Http::fail(403, 'closed', 'Hiện chưa mở đăng ký tài khoản mới.');
        }
        $ip = Http::ip();
        if (!RateLimit::hit('reg:' . $ip, 5, 3600)) {
            Http::fail(429, 'rate_limited', 'Bạn thao tác quá nhanh, hãy thử lại sau ít phút.');
        }

        $b = Http::body(65536);
        self::checkTurnstile($b, $ip);

        $email = self::email($b);
        $authKey = self::key32($b, 'authKey');
        $recoveryAuth = self::key32($b, 'recoveryAuth');
        $wrappedPw = self::wrapped($b, 'wrappedPw');
        $wrappedRecovery = self::wrapped($b, 'wrappedRecovery');
        $iter = (int)($b['kdfIterations'] ?? 0);
        if ($iter < 200000 || $iter > 5000000) {
            Http::fail(422, 'bad_kdf', 'Tham số mã hóa không hợp lệ.');
        }

        if (self::user($email) !== null) {
            Http::fail(409, 'email_taken', 'Email này đã được đăng ký.');
        }

        $pdo = Db::pdo();
        try {
            $pdo->prepare(
                'INSERT INTO users (email, auth_hash, recovery_hash, wrapped_pw, wrapped_recovery, kdf_iterations, created_at)
                 VALUES (?, ?, ?, ?, ?, ?, ?)'
            )->execute([
                $email,
                password_hash($authKey, PASSWORD_DEFAULT),
                password_hash($recoveryAuth, PASSWORD_DEFAULT),
                $wrappedPw, $wrappedRecovery, $iter, Db::now(),
            ]);
        } catch (PDOException $e) {
            if ((int)$e->errorInfo[1] === 1062) {
                Http::fail(409, 'email_taken', 'Email này đã được đăng ký.');
            }
            throw $e;
        }

        $uid = (int)$pdo->lastInsertId();
        $device = (string)($b['deviceName'] ?? 'Trình duyệt web');
        $t = Auth::newToken($uid, $device, $ip);

        Http::json(201, ['ok' => true, 'token' => $t['token'], 'expiresAt' => $t['expiresAt'], 'email' => $email]);
    }

    private static function prelogin(): void
    {
        if (!RateLimit::hit('pre:' . Http::ip(), 60, 600)) {
            Http::fail(429, 'rate_limited', 'Bạn thao tác quá nhanh, hãy thử lại sau ít phút.');
        }
        $b = Http::body(4096);
        $email = self::email($b);
        $u = self::user($email);
        // Không tiết lộ email có tồn tại hay không: luôn trả số vòng lặp.
        Http::json(200, ['ok' => true, 'kdfIterations' => $u ? (int)$u['kdf_iterations'] : (int)wn_config('kdf_iterations', 600000)]);
    }

    private static function login(): void
    {
        $ip = Http::ip();
        $b = Http::body(8192);
        $email = self::email($b);
        $authKey = self::key32($b, 'authKey');

        // Giới hạn chặt theo (email + IP) để người khác không khóa được tài khoản của bạn từ IP của họ;
        // thêm một trần lỏng theo email để chống dò phân tán.
        $k1 = 'login:' . $email . '|' . $ip;
        $k2 = 'loginip:' . $ip;
        $k3 = 'loginall:' . $email;
        if (!RateLimit::hit($k1, 8, 900) || !RateLimit::hit($k2, 40, 900) || !RateLimit::hit($k3, 100, 900)) {
            Http::fail(429, 'rate_limited', 'Đăng nhập sai quá nhiều lần, hãy thử lại sau 15 phút.');
        }

        $u = self::user($email);
        if (!Auth::verify($u['auth_hash'] ?? null, $authKey) || ($u && $u['disabled'])) {
            Http::fail(401, 'bad_credentials', 'Email hoặc mật khẩu không đúng.');
        }
        RateLimit::reset($k1);

        $uid = (int)$u['id'];
        Db::pdo()->prepare('UPDATE users SET last_login_at = ? WHERE id = ?')->execute([Db::now(), $uid]);
        $t = Auth::newToken($uid, (string)($b['deviceName'] ?? ''), $ip);

        Http::json(200, [
            'ok'            => true,
            'token'         => $t['token'],
            'expiresAt'     => $t['expiresAt'],
            'email'         => $u['email'],
            'wrappedPw'     => $u['wrapped_pw'],
            'kdfIterations' => (int)$u['kdf_iterations'],
            'vault'         => ['version' => (int)$u['vault_version'], 'updatedAt' => $u['vault_updated_at'] ? $u['vault_updated_at'] . 'Z' : null],
        ]);
    }

    private static function logout(): void
    {
        [, $sid] = Auth::require();
        Db::pdo()->prepare('DELETE FROM sessions WHERE id = ?')->execute([$sid]);
        Http::json(200, ['ok' => true]);
    }

    private static function me(): void
    {
        [$u] = Auth::require();
        Http::json(200, [
            'ok'        => true,
            'email'     => $u['email'],
            'createdAt' => $u['created_at'] . 'Z',
            'vault'     => ['version' => (int)$u['vault_version'], 'updatedAt' => $u['vault_updated_at'] ? $u['vault_updated_at'] . 'Z' : null],
        ]);
    }

    /** Khóa dữ liệu đã bọc (server không giải được) — để đổi mật khẩu mà không mất dữ liệu. */
    private static function keys(): void
    {
        [$u] = Auth::require();
        Http::json(200, ['ok' => true, 'wrappedPw' => $u['wrapped_pw'], 'kdfIterations' => (int)$u['kdf_iterations']]);
    }

    private static function devices(): void
    {
        [$u, $sid] = Auth::require();
        $st = Db::pdo()->prepare('SELECT id, device_name, ip, created_at, last_used_at FROM sessions WHERE user_id = ? AND expires_at > ? ORDER BY last_used_at DESC');
        $st->execute([(int)$u['id'], Db::now()]);
        $list = [];
        foreach ($st->fetchAll() as $r) {
            $list[] = [
                'id'         => (int)$r['id'],
                'name'       => $r['device_name'] !== '' ? $r['device_name'] : 'Thiết bị',
                'ip'         => $r['ip'],
                'createdAt'  => $r['created_at'] . 'Z',
                'lastUsedAt' => $r['last_used_at'] . 'Z',
                'current'    => (int)$r['id'] === $sid,
            ];
        }
        Http::json(200, ['ok' => true, 'devices' => $list]);
    }

    private static function revokeDevice(): void
    {
        [$u, $sid] = Auth::require();
        $id = (int)($_GET['id'] ?? 0);
        if ($id <= 0) {
            Http::fail(422, 'bad_id', 'Thiếu mã thiết bị.');
        }
        Db::pdo()->prepare('DELETE FROM sessions WHERE id = ? AND user_id = ?')->execute([$id, (int)$u['id']]);
        Http::json(200, ['ok' => true]);
    }

    /** GET /vault — bản hiện hành; ?version=N để lấy một phiên bản cũ còn giữ trong lịch sử. */
    private static function vaultGet(): void
    {
        [$u] = Auth::require();
        $uid = (int)$u['id'];
        $cur = (int)$u['vault_version'];
        $ver = isset($_GET['version']) ? (int)$_GET['version'] : $cur;
        if ($ver === 0) {
            Http::json(200, ['ok' => true, 'version' => 0, 'current' => $cur, 'blob' => null, 'updatedAt' => null]);
        }
        if ($ver !== $cur && ($ver < 1 || !VaultHistory::exists($uid, $ver))) {
            Http::fail(404, 'no_version', 'Không còn phiên bản này trên máy chủ.');
        }
        $data = BlobStore::create()->get($uid, $ver);
        if ($data === null) {
            Http::fail(500, 'blob_missing', 'Không đọc được dữ liệu đồng bộ.');
        }
        Http::json(200, [
            'ok' => true, 'version' => $ver, 'current' => $cur,
            'blob' => base64_encode($data),
            'updatedAt' => $ver === $cur && $u['vault_updated_at'] ? $u['vault_updated_at'] . 'Z' : null,
        ]);
    }

    /** GET /vault/history — các phiên bản còn giữ, mới nhất trước (để khôi phục bản cũ). */
    private static function vaultHistory(): void
    {
        [$u] = Auth::require();
        $list = [];
        foreach (VaultHistory::list((int)$u['id']) as $r) {
            $list[] = [
                'version'   => (int)$r['version'],
                'createdAt' => $r['created_at'] . 'Z',
                'size'      => (int)$r['size'],
                'device'    => (string)$r['device'],
            ];
        }
        Http::json(200, ['ok' => true, 'current' => (int)$u['vault_version'], 'versions' => $list]);
    }

    private static function vaultPut(): void
    {
        [$u] = Auth::require();
        $max = (int)wn_config('max_vault_bytes', 5242880);
        $b = Http::body((int)($max * 1.4) + 4096);

        $base = (int)($b['baseVersion'] ?? -1);
        $raw = base64_decode((string)($b['blob'] ?? ''), true);
        if ($raw === false || $raw === '' || $base < 0) {
            Http::fail(422, 'bad_blob', 'Dữ liệu đồng bộ không hợp lệ.');
        }
        if (strlen($raw) > $max) {
            Http::fail(413, 'too_large', 'Dữ liệu đồng bộ vượt quá giới hạn.');
        }
        if (!RateLimit::hit('vault:' . (int)$u['id'], 120, 3600)) {
            Http::fail(429, 'rate_limited', 'Đồng bộ quá thường xuyên, hãy thử lại sau.');
        }

        $uid = (int)$u['id'];
        $store = BlobStore::create();
        $pdo = Db::pdo();

        // Khóa dòng người dùng rồi mới so phiên bản: chỉ ghi khi baseVersion đúng là bản hiện hành, nên không bao giờ
        // ghi đè hay xóa một phiên bản đã có (kể cả khi hai thiết bị đẩy cùng lúc).
        $pdo->beginTransaction();
        try {
            $st = $pdo->prepare('SELECT vault_version FROM users WHERE id = ? FOR UPDATE');
            $st->execute([$uid]);
            $cur = (int)$st->fetchColumn();
            if ($cur !== $base) {
                $pdo->rollBack();
                Http::json(409, [
                    'ok' => false, 'error' => 'conflict',
                    'message' => 'Dữ liệu trên máy chủ mới hơn. Hãy tải về, gộp rồi đồng bộ lại.',
                    'serverVersion' => $cur,
                ]);
            }
            $new = $cur + 1;
            $store->put($uid, $new, $raw);
            $pdo->prepare('UPDATE users SET vault_version = ?, vault_updated_at = ? WHERE id = ?')->execute([$new, Db::now(), $uid]);
            VaultHistory::record($uid, $new, strlen($raw), (string)($u['device_name'] ?? ''));
            $pdo->commit();
        } catch (Throwable $e) {
            if ($pdo->inTransaction()) {
                $pdo->rollBack();
            }
            throw $e;
        }

        try {
            VaultHistory::prune($uid, $new, $store);
        } catch (Throwable $e) {
            error_log('[wnterm] prune: ' . $e->getMessage());
        }
        Http::json(200, ['ok' => true, 'version' => $new]);
    }

    private static function recoverStart(): void
    {
        $ip = Http::ip();
        $b = Http::body(4096);
        $email = self::email($b);
        $rec = self::key32($b, 'recoveryAuth');

        if (!RateLimit::hit('rec:' . $email . '|' . $ip, 10, 3600) ||!RateLimit::hit('recip:' . $ip, 20, 3600)) {
            Http::fail(429, 'rate_limited', 'Thử quá nhiều lần, hãy quay lại sau.');
        }
        $u = self::user($email);
        if (!Auth::verify($u['recovery_hash'] ?? null, $rec)) {
            Http::fail(401, 'bad_recovery', 'Email hoặc mã khôi phục không đúng.');
        }
        Http::json(200, ['ok' => true, 'wrappedRecovery' => $u['wrapped_recovery'], 'kdfIterations' => (int)$u['kdf_iterations']]);
    }

    private static function recoverReset(): void
    {
        $ip = Http::ip();
        $b = Http::body(8192);
        $email = self::email($b);
        $rec = self::key32($b, 'recoveryAuth');
        $newAuth = self::key32($b, 'authKey');
        $newWrapped = self::wrapped($b, 'wrappedPw');

        if (!RateLimit::hit('recf:' . $email . '|' . $ip, 10, 3600) ||!RateLimit::hit('recfip:' . $ip, 20, 3600)) {
            Http::fail(429, 'rate_limited', 'Thử quá nhiều lần, hãy quay lại sau.');
        }
        $u = self::user($email);
        if (!Auth::verify($u['recovery_hash'] ?? null, $rec)) {
            Http::fail(401, 'bad_recovery', 'Email hoặc mã khôi phục không đúng.');
        }
        Db::pdo()->prepare('UPDATE users SET auth_hash = ?, wrapped_pw = ? WHERE id = ?')
            ->execute([password_hash($newAuth, PASSWORD_DEFAULT), $newWrapped, (int)$u['id']]);
        Auth::revokeAll((int)$u['id']);
        Http::json(200, ['ok' => true]);
    }

    private static function passwordChange(): void
    {
        [$u, $sid] = Auth::require();
        $b = Http::body(8192);
        $old = self::key32($b, 'oldAuthKey');
        $new = self::key32($b, 'newAuthKey');
        $newWrapped = self::wrapped($b, 'newWrappedPw');

        if (!RateLimit::hit('chg:' . (int)$u['id'], 10, 3600)) {
            Http::fail(429, 'rate_limited', 'Thử quá nhiều lần, hãy quay lại sau.');
        }
        if (!password_verify($old, (string)$u['auth_hash'])) {
            Http::fail(401, 'bad_credentials', 'Mật khẩu hiện tại không đúng.');
        }
        Db::pdo()->prepare('UPDATE users SET auth_hash = ?, wrapped_pw = ? WHERE id = ?')
            ->execute([password_hash($new, PASSWORD_DEFAULT), $newWrapped, (int)$u['id']]);
        Auth::revokeAll((int)$u['id'], $sid);   // đăng xuất các thiết bị khác
        Http::json(200, ['ok' => true]);
    }

    private static function accountDelete(): void
    {
        [$u] = Auth::require();
        $b = Http::body(4096);
        $authKey = self::key32($b, 'authKey');
        if (!RateLimit::hit('del:' . (int)$u['id'], 10, 3600)) {
            Http::fail(429, 'rate_limited', 'Thử quá nhiều lần, hãy quay lại sau.');
        }
        if (!password_verify($authKey, (string)$u['auth_hash'])) {
            Http::fail(401, 'bad_credentials', 'Mật khẩu không đúng.');
        }
        $uid = (int)$u['id'];
        try {
            VaultHistory::deleteAll($uid, (int)$u['vault_version'], BlobStore::create());
        } catch (Throwable $e) {
            error_log('[wnterm] delete blobs: ' . $e->getMessage());   // vẫn xóa tài khoản
        }
        Db::pdo()->prepare('DELETE FROM users WHERE id = ?')->execute([$uid]);   // sessions + blobs + lịch sử xóa theo (CASCADE)
        Http::json(200, ['ok' => true]);
    }

    // ===== Chống bot (tùy chọn) =====

    private static function checkTurnstile(array $b, string $ip): void
    {
        $secret = (string)wn_config('turnstile.secret', '');
        if ($secret === '') {
            return;
        }
        $token = (string)($b['turnstile'] ?? '');
        if ($token === '') {
            Http::fail(422, 'captcha', 'Vui lòng xác minh bạn không phải robot.');
        }
        $ch = curl_init('https://challenges.cloudflare.com/turnstile/v0/siteverify');
        curl_setopt_array($ch, [
            CURLOPT_POST           => true,
            CURLOPT_POSTFIELDS     => http_build_query(['secret' => $secret, 'response' => $token, 'remoteip' => $ip]),
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT        => 10,
        ]);
        $resp = curl_exec($ch);
        curl_close($ch);
        $data = is_string($resp) ? json_decode($resp, true) : null;
        if (!is_array($data) || empty($data['success'])) {
            Http::fail(422, 'captcha', 'Xác minh robot không thành công, hãy thử lại.');
        }
    }
}
