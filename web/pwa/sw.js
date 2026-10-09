/* WN Term PWA — service worker: lưu sẵn giao diện + bộ SSH để mở nhanh / mở được khi mạng yếu.
   API (/api/) và trạm chuyển tiếp (/relay) luôn đi thẳng mạng, không lưu. */
var CACHE = 'wnweb-__VERSION__';
var FILES = ['./', 'index.html', 'app.js', 'i18n.js', 'ui.js', 'vault.js', 'wnfile.js', 'app.css', 'wn-crypto.js', 'wasm_exec.js', 'wnssh.wasm', 'manifest.webmanifest',
  'lib/xterm.js', 'lib/xterm.css', 'lib/addon-fit.js', 'lib/addon-unicode11.js',
  'fonts/JetBrainsMono-Regular.woff2', 'fonts/JetBrainsMono-Bold.woff2', 'icons/icon-192.png', 'icons/apple-touch-icon.png', 'icons/logo-n.png',
  'icons/os/os_linux.png', 'icons/os/os_ubuntu.png', 'icons/os/os_debian.png', 'icons/os/os_redhat.png', 'icons/os/os_alpine.png', 'icons/os/os_arch.png', 'icons/os/os_suse.png'];

self.addEventListener('install', function (e) {
  e.waitUntil(caches.open(CACHE).then(function (c) { return c.addAll(FILES); }).then(function () { return self.skipWaiting(); }));
});

self.addEventListener('activate', function (e) {
  e.waitUntil(caches.keys().then(function (keys) {
    return Promise.all(keys.filter(function (k) { return k.indexOf('wnweb-') === 0 && k !== CACHE; }).map(function (k) { return caches.delete(k); }));
  }).then(function () { return self.clients.claim(); }));
});

// Giao diện (HTML/JS/CSS nhỏ): lấy bản mới trên mạng trước, mất mạng thì dùng bản đã lưu.
// Bộ SSH, thư viện, font, icon (lớn, ít đổi): dùng bản đã lưu trước cho nhanh.
var FRESH = /\/app\/(|index\.html|app\.js|i18n\.js|ui\.js|vault\.js|wnfile\.js|app\.css|manifest\.webmanifest)$/;
self.addEventListener('fetch', function (e) {
  var url = new URL(e.request.url);
  if (e.request.method !== 'GET' || url.origin !== location.origin || url.pathname.indexOf('/app/') !== 0) return;
  if (FRESH.test(url.pathname)) {
    e.respondWith(fetch(e.request).then(function (res) {
      if (res.ok) { var copy = res.clone(); caches.open(CACHE).then(function (c) { c.put(e.request, copy); }); }
      return res;
    }).catch(function () { return caches.match(e.request, { ignoreSearch: true }).then(function (hit) { return hit || caches.match('index.html'); }); }));
    return;
  }
  e.respondWith(caches.match(e.request, { ignoreSearch: true }).then(function (hit) {
    return hit || fetch(e.request);
  }));
});
