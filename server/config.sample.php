<?php
/**
 * Cấu hình WNTerm server.
 *
 * Cách dùng: copy file này thành `config.php` (cùng thư mục với thư mục `src/`, NGOÀI thư mục web public),
 * rồi điền thông tin thật. KHÔNG commit config.php, KHÔNG để trong public_html.
 */
return [
    // Địa chỉ site (không có dấu / cuối)
    'app_url' => 'https://wnterm.webnow.vn',

    // MySQL (tạo database + user riêng trong DirectAdmin)
    'db' => [
        'host'    => 'localhost',
        'port'    => 3306,
        'name'    => '',
        'user'    => '',
        'pass'    => '',
        'charset' => 'utf8mb4',
    ],

    // Nơi lưu khối dữ liệu đã mã hóa của người dùng: 'db' (MySQL) hoặc 's3'
    'storage' => 'db',

    // S3 mặc định (chỉ dùng khi storage = 's3'). Key này nằm trên server, app KHÔNG bao giờ thấy.
    's3' => [
        'endpoint'   => 'https://s3.cloudfly.vn',
        'region'     => 'us-east-1',
        'bucket'     => '',
        'access_key' => '',
        'secret_key' => '',
        'prefix'     => 'wnterm-vaults/',
        'path_style' => true,
    ],

    // 'cloudflare' = lấy IP khách từ header CF-Connecting-IP (site chạy sau Cloudflare); '' = dùng REMOTE_ADDR
    'trusted_proxy' => 'cloudflare',

    // Cho phép đăng ký tài khoản mới
    'registration_open' => true,

    // Cloudflare Turnstile (chống bot khi đăng ký). Để trống = tắt.
    'turnstile' => [
        'site_key' => '',
        'secret'   => '',
    ],

    'session_days'    => 90,        // thời hạn phiên đăng nhập của thiết bị
    'kdf_iterations'  => 600000,    // PBKDF2 ở phía người dùng (app/web)
    'max_vault_bytes' => 5242880,   // tối đa 5 MB mỗi khối dữ liệu
    'keep_versions'   => 30,        // luôn giữ ít nhất bấy nhiêu phiên bản gần nhất (để khôi phục)
    'keep_days'       => 30,        // ...và mọi phiên bản trong bấy nhiêu ngày gần đây
    'max_versions'    => 200,       // nhưng không quá bấy nhiêu phiên bản mỗi tài khoản
];
