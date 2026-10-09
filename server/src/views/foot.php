<?php $b = brand(); ?>
<footer>
  <div class="wrap">
    <div class="cols">
      <div>
        <a class="lg" href="<?= e($b['site']) ?>" rel="noopener"><img src="/assets/webnow-logo.webp" alt="WebNow — <?= e($b['slogan']) ?>" width="300" height="59" loading="lazy" decoding="async"></a>
        <div><?= e($b['company']) ?></div>
        <div style="margin-top:6px"><?= t('foot.tax') ?>: <?= e($b['mst']) ?></div>
        <div style="margin-top:6px"><?= e($b['address']) ?></div>
      </div>
      <div>
        <div class="fh"><?= t('foot.contact') ?></div>
        <ul>
          <li><a href="tel:<?= e(preg_replace('/\s+/', '', $b['phone'])) ?>"><?= e($b['phone']) ?></a></li>
          <li><a href="tel:<?= e(preg_replace('/\s+/', '', $b['phone2'])) ?>"><?= e($b['phone2']) ?></a></li>
          <li><a href="mailto:<?= e($b['email']) ?>"><?= e($b['email']) ?></a></li>
          <li><a href="<?= e($b['zalo']) ?>" rel="noopener">Zalo</a> · <a href="<?= e($b['facebook']) ?>" rel="noopener">Facebook</a> · <a href="<?= e($b['tiktok']) ?>" rel="noopener">TikTok</a></li>
        </ul>
      </div>
      <div>
        <div class="fh">WNTerm</div>
        <ul>
          <li><a href="/#tai-ve"><?= t('nav.download') ?></a></li>
          <li><a href="/dang-ky"><?= t('nav.register') ?></a></li>
          <li><a href="/dang-nhap"><?= t('nav.login') ?></a></li>
          <li><a href="/khoi-phuc"><?= t('foot.recover') ?></a></li>
          <li><a href="/dieu-khoan"><?= t('foot.terms') ?></a></li>
          <li><a href="<?= e($b['site']) ?>" rel="noopener"><?= e($b['site']) ?></a></li>
        </ul>
      </div>
      <div>
        <div class="fh"><?= wn_lang() === 'vi' ? 'Tin tức' : 'News' ?></div>
        <ul>
          <li><a href="<?= wn_lang() === 'vi' ? '/ssh-windows' : '/en/ssh-windows' ?>"><?= wn_lang() === 'vi' ? 'SSH cho Windows' : 'SSH for Windows' ?></a></li>
          <li><a href="<?= wn_lang() === 'vi' ? '/ssh-iphone' : '/en/ssh-iphone' ?>"><?= wn_lang() === 'vi' ? 'SSH trên iPhone' : 'SSH on iPhone' ?></a></li>
          <li><a href="<?= wn_lang() === 'vi' ? '/chuyen-tu-mobaxterm' : '/en/chuyen-tu-mobaxterm' ?>"><?= wn_lang() === 'vi' ? 'Chuyển từ MobaXterm' : 'Move from MobaXterm' ?></a></li>
          <li><a href="<?= wn_lang() === 'vi' ? '/dong-bo-pc-dien-thoai' : '/en/dong-bo-pc-dien-thoai' ?>"><?= wn_lang() === 'vi' ? 'Đồng bộ PC ↔ điện thoại' : 'Sync PC ↔ phone' ?></a></li>
          <li><a href="<?= wn_lang() === 'vi' ? '/ssh-tren-trinh-duyet' : '/en/ssh-tren-trinh-duyet' ?>"><?= wn_lang() === 'vi' ? 'SSH trên trình duyệt' : 'SSH in the browser' ?></a></li>
        </ul>
      </div>
    </div>
    <div class="copy">© <?= date('Y') ?> WebNow Terminal (WNTerm) — <?= e($b['slogan']) ?>.</div>
  </div>
</footer>
<?php if (!empty($scripts)): ?>
<script src="/assets/js/wn-i18n.js?v=2.1.0"></script>
<?php foreach ((array)$scripts as $s): ?>
<script src="/assets/js/<?= e($s) ?>?v=2.1.0"></script>
<?php endforeach; endif; ?>
</body>
</html>
