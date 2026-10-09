#!/usr/bin/env bash
# Build bản web (PWA) + trạm chuyển tiếp. Kết quả: web/dist/app (đưa lên public_html/app) và web/dist/wnterm-relay-linux-amd64.
set -euo pipefail
. /c/Users/nguye/.dotnet-user/goenv.sh
WEB="$(cd "$(dirname "$0")" && pwd)"
SN="$WEB/.."
OUT="$WEB/dist/app"
rm -rf "$OUT"; mkdir -p "$OUT/lib" "$OUT/fonts" "$OUT/icons"
(cd "$WEB/sshwasm" && GOOS=js GOARCH=wasm go build -trimpath -ldflags="-s -w" -o "$OUT/wnssh.wasm" .)
cp "$(go env GOROOT)/lib/wasm/wasm_exec.js" "$OUT/"
cp "$WEB/pwa/"{index.html,app.js,i18n.js,ui.js,vault.js,wnfile.js,app.css,manifest.webmanifest,.htaccess} "$OUT/"
cp -r "$WEB/pwa/icons/." "$OUT/icons/"
VER="$(date +%Y%m%d%H%M%S)"
sed "s/__VERSION__/$VER/" "$WEB/pwa/sw.js" > "$OUT/sw.js"
# Gắn phiên bản vào mọi file tĩnh (Cloudflare/trình duyệt giữ cache theo URL → bản mới luôn có URL mới).
sed -i "s/__VERSION__/$VER/g" "$OUT/app.js"
python -c "import re,sys; p=sys.argv[1]; s=open(p,encoding='utf-8').read(); s=re.sub(r'(href|src)=\"((?:app\.css|app\.js|i18n\.js|ui\.js|vault\.js|wnfile\.js|wn-crypto\.js|wasm_exec\.js|lib/[a-z0-9-]+\.(?:js|css)))\"', lambda m: m.group(1)+'=\"'+m.group(2)+'?v='+sys.argv[2]+'\"', s); open(p,'w',encoding='utf-8').write(s)" "$OUT/index.html" "$VER"
cp "$SN/server/public/assets/js/wn-crypto.js" "$OUT/"
cp "$SN/src/WNTerm/wwwroot/lib/"{xterm.js,xterm.css,addon-fit.js,addon-unicode11.js} "$OUT/lib/"
cp "$SN/src/WNTerm/wwwroot/fonts/"*.woff2 "$OUT/fonts/"
(cd "$WEB/relay" && GOOS=linux GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o "$WEB/dist/wnterm-relay-linux-amd64" . && go build -o "$WEB/dist/wnterm-relay.exe" .)
echo "built $VER"; du -sh "$OUT"
