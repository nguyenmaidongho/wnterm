# Triển khai WNTerm lên hosting DirectAdmin (wnterm.webnow.vn)

Yêu cầu: PHP 7.4+ (hosting đang là 8.2 — OK) với extension `pdo_mysql`, `curl`, `openssl`, `mbstring`; MySQL; HTTPS (Cloudflare).

## Cấu trúc thư mục trên hosting

```
/home/wnterm/domains/wnterm.webnow.vn/
├── public_html/        ← thư mục web (nội dung của gói zip: public_html/)
│   ├── index.php, dang-ky.php, dang-nhap.php, khoi-phuc.php, tai-khoan.php
│   ├── api/index.php, .htaccess
│   ├── assets/…        (css, js, ảnh, font)
│   └── downloads/      (bộ cài + APK)
├── src/                ← mã nguồn PHP  (NGOÀI web, không ai truy cập trực tiếp được)
├── tools/              ← migrate.php, bài kiểm thử
├── schema.sql          ← lược đồ CSDL
└── config.php          ← cấu hình (mật khẩu DB, key S3)  (NGOÀI web)
```

> `src/` và `config.php` PHẢI nằm cùng cấp với `public_html/`, không nằm trong `public_html/`.

## Các bước

1. **Upload & giải nén** `wnterm-site.zip` vào thư mục `/home/wnterm/domains/wnterm.webnow.vn/` (DirectAdmin → File Manager → Upload → Extract). Sau khi giải nén phải thấy `public_html/`, `src/`, `config.php`… ngay trong thư mục đó.
2. **Điền `config.php`** (File Manager → chuột phải → Edit): thông tin MySQL (và S3 nếu muốn lưu dữ liệu đồng bộ lên S3, khi đó đổi `'storage' => 's3'`).
3. **Tạo bảng CSDL** — chọn 1 trong 2 cách:
   - SSH: `cd /home/wnterm/domains/wnterm.webnow.vn && php tools/migrate.php`
   - hoặc phpMyAdmin → chọn database → tab *Import* → chọn file `schema.sql`.
4. **Upload bộ cài** (tùy chọn): giải nén `wnterm-downloads.zip` vào `public_html/downloads/` (gồm `WNTerm-Setup.exe`, `WNTerm-android-arm64.apk`).
5. **Cloudflare**: SSL/TLS = *Full (strict)*. Thêm Cache Rule: đường dẫn `wnterm.webnow.vn/api/*` → *Bypass cache*. (Trang tĩnh/ảnh cache được.)
6. **Kiểm tra**: mở `https://wnterm.webnow.vn/api/v1/health` → thấy `{"ok":true,...}`. Rồi vào `/dang-ky` thử tạo tài khoản.
7. **Dọn**: sau khi chạy ổn, có thể xóa thư mục `tools/` trên hosting (chỉ cần khi cập nhật CSDL).

## Bảo mật cần nhớ

- Không để `config.php`, `src/`, `schema.sql` trong `public_html/`.
- Key S3 mặc định chỉ nằm trong `config.php` trên server; ứng dụng không bao giờ thấy.
- Nên bật Cloudflare Turnstile (miễn phí) để chống bot đăng ký: điền `turnstile.site_key` và `turnstile.secret` trong `config.php`.
- Sao lưu định kỳ database (DirectAdmin → Backup) — mất DB là mất tài khoản và dữ liệu đồng bộ.

## Cập nhật phiên bản sau này

Chép đè `public_html/` và `src/` (giữ nguyên `config.php`), chạy lại `php tools/migrate.php` nếu `schema.sql` có bảng mới (lệnh dùng `CREATE TABLE IF NOT EXISTS` nên an toàn khi chạy lại).
