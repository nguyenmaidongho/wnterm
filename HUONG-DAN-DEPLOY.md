# Hướng dẫn deploy bản chống lạm dụng + Điều khoản (2026-10-09)

Máy chủ: `wnterm@203.0.113.10`, cổng `2222`, khóa `signing/wnterm_deploy`. Thư mục site: `~/domains/wnterm.webnow.vn/`.
Chạy các lệnh dưới đây trong **Git Bash** tại `D:\Software\WNTerm\snterm`.

Đặt sẵn biến cho gọn:

```bash
cd /d/Software/WNTerm/snterm
K="-i signing/wnterm_deploy -p 2222 -o IdentitiesOnly=yes"
H=wnterm@203.0.113.10
D=domains/wnterm.webnow.vn
```

> Thứ tự quan trọng: **tạo bảng CSDL (bước 3) trước khi bật code PHP mới (bước 4)**, vì form đăng ký mới sẽ ghi vào bảng `tos_acceptances`.

## 0. Kiểm tra kết nối

```bash
ssh $K $H "hostname; ls $D"
```

Phải thấy `public_html  src  tools  config.php  schema.sql`.

## 1. Sao lưu bản đang chạy

```bash
ssh $K $H "cd $D && cp -a src src.bak-20261009b && cp -a public_html public_html.bak-20261009b && cp schema.sql schema.sql.bak-20261009b && echo OK"
```

(`public_html.bak…` có thể nặng vì chứa `downloads/`; nếu lo dung lượng, xóa thư mục này sau khi chạy ổn.)

## 2. Đưa code PHP mới lên (chưa bật)

Chỉ đưa file mới/đổi, **không đụng `config.php`**:

```bash
scp $(echo $K | sed 's/-p 2222/-P 2222/') server/schema.sql            $H:$D/schema.sql
scp $(echo $K | sed 's/-p 2222/-P 2222/') server/tools/admin.php       $H:$D/tools/admin.php
scp $(echo $K | sed 's/-p 2222/-P 2222/') server/tools/test_api.php    $H:$D/tools/test_api.php
```

## 3. Tạo bảng CSDL mới

```bash
ssh $K $H "cd $D && php tools/migrate.php | tail -12"
```

Phải thấy `tos_acceptances` trong danh sách bảng. Chạy lại nhiều lần vẫn an toàn.

## 4. Bật code PHP mới

```bash
S="scp $(echo $K | sed 's/-p 2222/-P 2222/')"
$S server/src/Api.php                 $H:$D/src/Api.php
$S server/src/lang/vi.php             $H:$D/src/lang/vi.php
$S server/src/lang/en.php             $H:$D/src/lang/en.php
$S server/src/views/foot.php          $H:$D/src/views/foot.php
$S server/public/dieu-khoan.php       $H:$D/public_html/dieu-khoan.php
$S server/public/dang-ky.php          $H:$D/public_html/dang-ky.php
$S server/public/assets/css/site.css  $H:$D/public_html/assets/css/site.css
$S server/public/assets/js/register.js $H:$D/public_html/assets/js/register.js
$S server/public/assets/js/wn-i18n.js  $H:$D/public_html/assets/js/wn-i18n.js
```

Kiểm tra:

```bash
curl -s https://wnterm.webnow.vn/api/v1/health
curl -s -o /dev/null -w "%{http_code}\n" "https://wnterm.webnow.vn/dieu-khoan?lang=vi"   # 200
```

Mở `https://wnterm.webnow.vn/dang-ky`: phải có thêm ô tick “Tôi đồng ý với Điều khoản sử dụng”.
Cloudflare đang cache trang tĩnh: nếu vẫn thấy bản cũ, vào Cloudflare → Caching → **Purge Everything**.

## 5. Bật Cloudflare Turnstile (chống đăng ký hàng loạt)

1. Cloudflare Dashboard → **Turnstile** → Add site → domain `wnterm.webnow.vn` → lấy **Site key** và **Secret key**.
2. Sửa `~/domains/wnterm.webnow.vn/config.php` trên server (DirectAdmin File Manager → Edit), mục `turnstile`:
   ```php
   'turnstile' => ['site_key' => '…', 'secret' => '…'],
   ```
3. Thử đăng ký: phải hiện khung “xác minh không phải robot”.

## 6. Deploy PWA (bản web)

```bash
scp -r -P 2222 -i signing/wnterm_deploy -o IdentitiesOnly=yes web/dist/app $H:$D/public_html/app.new
ssh $K $H "cd $D/public_html && rm -rf app.old && mv app app.old && mv app.new app && rm -rf app.old && echo OK"
```

Mở `https://wnterm.webnow.vn/app/` → màn đăng nhập phải có link **Điều khoản**.

## 7. Cập nhật relay (cần root)

Đưa bộ cài lên (user `wnterm` làm được):

```bash
ssh $K $H "mkdir -p relay-install"
scp -P 2222 -i signing/wnterm_deploy -o IdentitiesOnly=yes web/dist/wnterm-relay-linux-amd64 web/dist/install-relay.sh $H:relay-install/
```

Rồi **đăng nhập root** vào máy chủ và chạy:

```bash
bash /home/wnterm/relay-install/install-relay.sh
```

Script sẽ: cài relay mới, tạo `/etc/wnterm-relay/blocklist.txt`, logrotate 90 ngày, restart dịch vụ và tự kiểm tra. Cuối cùng phải thấy `XONG: https://wnterm.webnow.vn/relay đã hoạt động`.

Kiểm tra sau khi cài:

```bash
systemctl status wnterm-relay --no-pager | head -5
tail -n 5 /var/log/wnterm-relay/relay.log
```

## 8. Kiểm tra cuối

- Đăng nhập PWA, mở 1 VM thật → vẫn kết nối bình thường.
- Tài khoản mới tạo trong 24 giờ đầu chỉ mở được 3 kết nối cùng lúc, 5 máy khác nhau mỗi giờ.
- Đăng ký tài khoản thử, rồi kiểm tra bằng chứng đồng ý điều khoản:
  ```bash
  ssh $K $H "cd $D && php tools/admin.php recent 3"
  ```

## Vận hành hằng ngày

| Việc | Lệnh (trên server) |
|---|---|
| Xem các dòng lạm dụng | `grep abuse /var/log/wnterm-relay/relay.log \| tail -50` |
| Xem ai kết nối tới đâu | `tail -f /var/log/wnterm-relay/relay.log` |
| Chặn một đích | thêm dòng vào `/etc/wnterm-relay/blocklist.txt` (tên máy, `*.tên-miền`, IP hoặc CIDR) — tự nạp trong 60 giây |
| Xem tài khoản | `php tools/admin.php info <email>` |
| Khóa / mở khóa tài khoản | `php tools/admin.php disable <email>` / `enable <email>` |
| Tài khoản mới nhất | `php tools/admin.php recent 20` |

Khi có báo cáo lạm dụng gửi tới `hi@webnow.vn`: tìm IP/thời điểm trong log relay → ra email tài khoản → `disable` + thêm đích vào blocklist nếu cần.

## Quay lui nếu có sự cố

```bash
ssh $K $H "cd $D && rm -rf src && mv src.bak-20261009b src && cp schema.sql.bak-20261009b schema.sql"
```

Với `public_html`: chép lại các file từ `public_html.bak-20261009b`. Bảng `tos_acceptances` có thể để nguyên (không ảnh hưởng gì), nhưng bản code PHP cũ **không** gửi `tos` nên cũng không bị chặn đăng ký. Relay: chạy lại `install-relay.sh` của bản cũ.
