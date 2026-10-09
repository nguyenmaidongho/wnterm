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
    /** Phiên bản Điều khoản sử dụng hiện hành (public/dieu-khoan.php) — đổi khi nội dung điều khoản thay đổi. */
    public const TOS_VERSION = '2026-10-09';

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
                case 'GET /admin/users':      self::adminUsers(); break;
                case 'GET /admin/user':       self::adminUser(); break;
                case 'POST /admin/disable':   self::adminSetDisabled(true); break;
                case 'POST /admin/enable':    self::adminSetDisabled(false); break;
                case 'POST /feedback':        self::feedback(); break;
                case 'GET /version':          self::version(); break;
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

        if ((string)($b['tos'] ?? '') !== self::TOS_VERSION) {
            Http::fail(422, 'tos_required', 'Bạn cần đồng ý với Điều khoản sử dụng.');
        }

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
        try { // bằng chứng đã đồng ý điều khoản (phiên bản + thời điểm + IP); lỗi ghi không chặn đăng ký
            $pdo->prepare('INSERT INTO tos_acceptances (user_id, version, ip, accepted_at) VALUES (?, ?, ?, ?)')
                ->execute([$uid, self::TOS_VERSION, $ip, Db::now()]);
        } catch (Throwable $e) {
            error_log('[wnterm] tos_acceptances: ' . $e->getMessage());
        }
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
            'admin'     => self::isAdmin($u),
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


    // ===== Quản trị (trang /quan-tri) =====

    /** Danh sách email quản trị: config 'admins' (mảng email). Mặc định chỉ donghoc3@gmail.com. */
    public static function isAdmin(array $u): bool
    {
        $list = array_map('strtolower', (array)wn_config('admins', ['donghoc3@gmail.com']));
        return in_array(strtolower((string)$u['email']), $list, true);
    }

    private static function admin(): array
    {
        [$u] = Auth::require();
        if (!self::isAdmin($u)) {
            Http::fail(403, 'forbidden', 'Không có quyền quản trị.');
        }
        if (!RateLimit::hit('admin:' . (int)$u['id'], 240, 60)) {
            Http::fail(429, 'rate_limited', 'Bạn thao tác quá nhanh, hãy thử lại sau ít phút.');
        }
        return $u;
    }

    private static function adminUsers(): void
    {
        self::admin();
        $q = trim((string)($_GET['q'] ?? ''));
        $like = '%' . addcslashes(strtolower($q), '%_\\') . '%';
        $st = Db::pdo()->prepare(
            'SELECT u.id, u.email, u.created_at, u.last_login_at, u.disabled,
                    (SELECT COUNT(*) FROM sessions s WHERE s.user_id = u.id AND s.expires_at > ?) AS sessions,
                    (SELECT s.ip FROM sessions s WHERE s.user_id = u.id ORDER BY s.last_used_at DESC LIMIT 1) AS last_ip
               FROM users u
              WHERE (? = \'\' OR u.email LIKE ?)
              ORDER BY u.id DESC LIMIT 100'
        );
        $st->execute([Db::now(), $q, $like]);
        $list = [];
        foreach ($st->fetchAll() as $r) {
            $list[] = [
                'id'        => (int)$r['id'],
                'email'     => $r['email'],
                'createdAt' => $r['created_at'] . 'Z',
                'lastLogin' => $r['last_login_at'] ? $r['last_login_at'] . 'Z' : null,
                'disabled'  => (bool)$r['disabled'],
                'admin'     => self::isAdmin($r),
                'sessions'  => (int)$r['sessions'],
                'lastIp'    => $r['last_ip'],
            ];
        }
        Http::json(200, ['ok' => true, 'users' => $list]);
    }

    private static function adminUser(): void
    {
        self::admin();
        $email = self::email(['email' => $_GET['email'] ?? '']);
        $u = self::user($email);
        if ($u === null) {
            Http::fail(404, 'not_found', 'Không thấy tài khoản.');
        }
        $st = Db::pdo()->prepare('SELECT device_name, ip, created_at, last_used_at FROM sessions WHERE user_id = ? AND expires_at > ? ORDER BY last_used_at DESC LIMIT 50');
        $st->execute([(int)$u['id'], Db::now()]);
        $sessions = [];
        foreach ($st->fetchAll() as $s) {
            $sessions[] = ['device' => $s['device_name'], 'ip' => $s['ip'], 'createdAt' => $s['created_at'] . 'Z', 'lastUsedAt' => $s['last_used_at'] . 'Z'];
        }
        $tosRows = [];
        try {
            $ts = Db::pdo()->prepare('SELECT version, ip, accepted_at FROM tos_acceptances WHERE user_id = ?');
            $ts->execute([(int)$u['id']]);
            $tosRows = $ts->fetchAll();
        } catch (Throwable $e) {
        }
        Http::json(200, [
            'ok' => true, 'email' => $u['email'], 'createdAt' => $u['created_at'] . 'Z',
            'lastLogin' => $u['last_login_at'] ? $u['last_login_at'] . 'Z' : null,
            'disabled' => (bool)$u['disabled'], 'admin' => self::isAdmin($u),
            'sessions' => $sessions,
            'tos' => array_map(fn($r) => ['version' => $r['version'], 'ip' => $r['ip'], 'at' => $r['accepted_at'] . 'Z'], $tosRows),
        ]);
    }

    private static function adminSetDisabled(bool $disable): void
    {
        $me = self::admin();
        $b = Http::body(4096);
        $email = self::email($b);
        $u = self::user($email);
        if ($u === null) {
            Http::fail(404, 'not_found', 'Không thấy tài khoản.');
        }
        if (self::isAdmin($u)) {
            Http::fail(422, 'admin_protected', 'Không thể khóa tài khoản quản trị.');
        }
        Db::pdo()->prepare('UPDATE users SET disabled = ? WHERE id = ?')->execute([$disable ? 1 : 0, (int)$u['id']]);
        if ($disable) {
            Auth::revokeAll((int)$u['id']);
        }
        error_log('[wnterm] admin ' . $me['email'] . ($disable ? ' disabled ' : ' enabled ') . $email);
        Http::json(200, ['ok' => true, 'email' => $email, 'disabled' => $disable]);
    }

    /** Phiên bản mới nhất từng nền tảng (công khai) — app dùng để báo "có bản mới". */
    private static function version(): void
    {
        header('Cache-Control: public, max-age=300');
        Http::json(200, Versions::forApp(wn_lang()));
    }

    // ===== Góp ý / báo lỗi → Telegram =====

    private static function feedback(): void
    {
        $token = (string)wn_config('telegram.bot_token', '');
        $chat = (string)wn_config('telegram.chat_id', '');
        if ($token === '' || $chat === '') {
            Http::fail(503, 'not_configured', 'Chức năng góp ý chưa được cấu hình.');
        }
        $ip = Http::ip();
        if (!RateLimit::hit('fb:' . $ip, 3, 3600) || !RateLimit::hit('fb:all', 60, 3600)) {
            Http::fail(429, 'rate_limited', 'Bạn gửi quá nhiều lần, hãy thử lại sau.');
        }
        $b = Http::body(16384);
        if (trim((string)($b['website'] ?? '')) !== '') { // ô bẫy bot: giả vờ thành công
            Http::json(200, ['ok' => true]);
        }
        self::checkTurnstile($b, $ip);

        $name = trim((string)($b['name'] ?? ''));
        $email = strtolower(trim((string)($b['email'] ?? '')));
        $phone = trim((string)($b['phone'] ?? ''));
        $msg = trim((string)($b['message'] ?? ''));
        if ($name === '' || mb_strlen($name) > 80 || $msg === '' || mb_strlen($msg) > 3000
            || $email === '' || strlen($email) > 190 || !filter_var($email, FILTER_VALIDATE_EMAIL)
            || ($phone !== '' && !preg_match('/^[0-9+().\-\s]{6,20}$/', $phone))) {
            Http::fail(422, 'bad_input', 'Vui lòng nhập tên, email hợp lệ và nội dung góp ý.');
        }

        $h = fn(string $s): string => htmlspecialchars($s, ENT_NOQUOTES | ENT_SUBSTITUTE, 'UTF-8');
        $text = "📬 <b>Góp ý mới — WNTerm</b>\n"
            . '👤 ' . $h($name) . "\n"
            . '✉️ ' . $h($email) . "\n"
            . ($phone !== '' ? '📞 ' . $h($phone) . "\n" : '')
            . '🌐 ' . $h($ip) . ' · ' . $h(wn_lang()) . ' · ' . gmdate('Y-m-d H:i') . " UTC\n\n"
            . $h($msg);

        $base = rtrim((string)wn_config('telegram.api_base', 'https://api.telegram.org'), '/');
        $ch = curl_init($base . '/bot' . $token . '/sendMessage');
        curl_setopt_array($ch, [
            CURLOPT_POST           => true,
            CURLOPT_HTTPHEADER     => ['Content-Type: application/json'],
            CURLOPT_POSTFIELDS     => json_encode(['chat_id' => $chat, 'text' => $text, 'parse_mode' => 'HTML', 'disable_web_page_preview' => true]),
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT        => 10,
        ]);
        $resp = curl_exec($ch);
        $code = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
        $data = is_string($resp) ? json_decode($resp, true) : null;
        if ($code !== 200 || !is_array($data) || empty($data['ok'])) {
            error_log('[wnterm] telegram: HTTP ' . $code . ' ' . (is_array($data) ? (string)($data['description'] ?? '') : 'no response'));
            Http::fail(502, 'send_failed', 'Chưa gửi được, hãy thử lại sau.');
        }
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
