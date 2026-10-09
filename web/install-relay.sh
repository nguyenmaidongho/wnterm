#!/usr/bin/env bash
# Cài trạm chuyển tiếp WN Term (wss://wnterm.webnow.vn/relay) lên máy DirectAdmin. Chạy bằng root:
#     bash install-relay.sh            # cài / cập nhật
#     bash install-relay.sh uninstall  # gỡ
# Script này: tạo user hệ thống "wnrelay" (không đăng nhập được), cài /usr/local/bin/wnterm-relay,
# tạo dịch vụ systemd "wnterm-relay" (chỉ nghe 127.0.0.1:8787), thêm khối "location /relay" vào cấu hình nginx
# riêng của domain qua DirectAdmin (cust_nginx), build lại cấu hình rồi tự kiểm tra.
set -euo pipefail

DOMAIN="wnterm.webnow.vn"
DAUSER="wnterm"
PORT=8787
SELF_PORTS="22,8282"
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN=/usr/local/bin/wnterm-relay
UNIT=/etc/systemd/system/wnterm-relay.service
CUST="/usr/local/directadmin/data/users/$DAUSER/domains/$DOMAIN.cust_nginx"
BEGIN="# >>> wnterm-relay (do not edit between these markers)"
END="# <<< wnterm-relay"

# nginx của domain có thể chỉ nghe trên IP công khai (không nghe 127.0.0.1) → thử lần lượt các IP của máy.
local_ips() { echo 127.0.0.1; hostname -I 2>/dev/null | tr ' ' '
' | grep -E '^[0-9.]+$' || true; }
probe() {   # probe <path> → in IP đầu tiên trả về nội dung chứa chuỗi $2
  for ip in $(local_ips); do
    # -k: nginx gốc dùng chứng chỉ Cloudflare Origin / tự cấp nên curl không tin cậy được — ở đây chỉ cần biết /relay có tới trạm hay không.
    if curl -sk --max-time 8 --resolve "$DOMAIN:443:$ip" "https://$DOMAIN$1" 2>/dev/null | grep -q "$2"; then echo "$ip"; return 0; fi
  done
  # Dự phòng: gọi thẳng qua tên miền công khai (đi qua Cloudflare), giống cách người dùng thật truy cập.
  if curl -s --max-time 10 "https://$DOMAIN$1" 2>/dev/null | grep -q "$2"; then echo "public"; return 0; fi
  return 1
}

say() { printf '\n\033[1;36m== %s\033[0m\n' "$*"; }
die() { printf '\n\033[1;31mLỖI: %s\033[0m\n' "$*"; exit 1; }
[ "$(id -u)" = 0 ] || die "Hãy chạy bằng root (sudo -i rồi chạy lại)."

rewrite_nginx() {
  say "Build lại cấu hình web của DirectAdmin"
  if [ -x /usr/local/directadmin/custombuild/build ]; then
    (cd /usr/local/directadmin/custombuild && ./build rewrite_confs >/tmp/wnterm-relay-rewrite.log 2>&1) || die "build rewrite_confs lỗi, xem /tmp/wnterm-relay-rewrite.log"
  else
    echo "action=rewrite&value=nginx" >> /usr/local/directadmin/data/task.queue
    /usr/local/directadmin/directadmin taskq --run >/dev/null 2>&1 || /usr/local/directadmin/dataskq d >/dev/null 2>&1 || true
  fi
  nginx -t || die "nginx -t báo lỗi cấu hình"
  systemctl reload nginx || nginx -s reload
}

strip_block() {   # bỏ khối cũ của chúng ta (nếu có) khỏi file cust_nginx
  [ -f "$CUST" ] || return 0
  awk -v b="$BEGIN" -v e="$END" '$0==b{skip=1;next} $0==e{skip=0;next} !skip' "$CUST" > "$CUST.tmp" && cat "$CUST.tmp" > "$CUST" && rm -f "$CUST.tmp"
}

if [ "${1:-}" = "uninstall" ]; then
  say "Gỡ wnterm-relay"
  systemctl disable --now wnterm-relay 2>/dev/null || true
  rm -f "$UNIT" "$BIN"; systemctl daemon-reload
  strip_block; rewrite_nginx
  echo "Đã gỡ. (User hệ thống wnrelay vẫn giữ; xóa bằng: userdel wnrelay)"
  exit 0
fi

[ -f "$HERE/wnterm-relay-linux-amd64" ] || die "Không thấy file wnterm-relay-linux-amd64 cạnh script."
[ -d "/usr/local/directadmin/data/users/$DAUSER/domains" ] || die "Không thấy user DirectAdmin '$DAUSER'."
command -v nginx >/dev/null || die "Máy này không chạy nginx (cần nginx hoặc nginx_apache)."

say "1/5 User hệ thống + chương trình"
id wnrelay >/dev/null 2>&1 || useradd --system --no-create-home --shell /sbin/nologin wnrelay
install -m 0755 -o root -g root "$HERE/wnterm-relay-linux-amd64" "$BIN"
"$BIN" -h 2>&1 | head -1 || true

say "2/5 Chọn đường gọi API kiểm tra đăng nhập"
API_ARGS="-api https://$DOMAIN/api/v1"
if APIIP="$(probe /api/v1/health '"service":"wnterm"')"; then
  API_ARGS="$API_ARGS -api-connect $APIIP:443"; echo "Gọi thẳng $APIIP:443 (không vòng qua Cloudflare)."
else
  echo "Gọi qua https://$DOMAIN (Cloudflare)."
fi

say "3/5 Dịch vụ systemd"
cat > "$UNIT" <<EOF
[Unit]
Description=WN Term relay (WebSocket -> SSH) for the web app
After=network-online.target
Wants=network-online.target

[Service]
User=wnrelay
Group=wnrelay
ExecStart=$BIN -listen 127.0.0.1:$PORT $API_ARGS -origins $DOMAIN -self-ports $SELF_PORTS
Restart=always
RestartSec=3
LimitNOFILE=65536
MemoryMax=512M
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
LockPersonality=yes
CapabilityBoundingSet=
AmbientCapabilities=

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable wnterm-relay >/dev/null 2>&1
systemctl restart wnterm-relay
sleep 1
systemctl is-active --quiet wnterm-relay || { journalctl -u wnterm-relay -n 20 --no-pager; die "Dịch vụ không chạy."; }
curl -s --max-time 5 "http://127.0.0.1:$PORT/relay/health" && echo

say "4/5 Cấu hình nginx cho /relay ($CUST)"
touch "$CUST"; chown diradmin:diradmin "$CUST" 2>/dev/null || true
strip_block
cat >> "$CUST" <<EOF
$BEGIN
location /relay {
    proxy_pass http://127.0.0.1:$PORT;
    proxy_http_version 1.1;
    proxy_set_header Upgrade \$http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host \$host;
    proxy_set_header X-Real-IP \$remote_addr;
    proxy_set_header CF-Connecting-IP \$http_cf_connecting_ip;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
    proxy_buffering off;
}
$END
EOF
rewrite_nginx

say "5/5 Kiểm tra qua nginx"
if IP="$(probe /relay/health wnterm-relay)"; then
  if [ "$IP" = public ]; then H="$(curl -s --max-time 10 "https://$DOMAIN/relay/health")"; else H="$(curl -sk --max-time 8 --resolve "$DOMAIN:443:$IP" "https://$DOMAIN/relay/health")"; fi
  echo "OK qua $IP: $H"
  printf '\n\033[1;32mXONG: https://%s/relay đã hoạt động. Mở https://%s/app trên iPhone để dùng.\033[0m\n' "$DOMAIN" "$DOMAIN"
  echo "Xem log:  journalctl -u wnterm-relay -f      Gỡ:  bash $0 uninstall"
else
  grep -rl "wnterm-relay" /usr/local/directadmin/data/users/$DAUSER/nginx*.conf 2>/dev/null || echo "(không thấy khối /relay trong nginx.conf của user — cấu hình DA có thể khác)"
  die "nginx chưa chuyển /relay vào trạm. Gửi lại kết quả ở trên cho mình."
fi
