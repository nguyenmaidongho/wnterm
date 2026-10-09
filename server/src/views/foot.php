<?php $b = brand(); ?>
<footer>
  <div class="wrap">
    <div class="cols">
      <div>
        <a class="lg" href="<?= e($b['site']) ?>" rel="noopener"><img src="/assets/webnow-logo.webp" alt="WebNow — <?= e($b['slogan']) ?>"></a>
        <div><?= e($b['company']) ?></div>
        <div style="margin-top:6px">MST: <?= e($b['mst']) ?></div>
        <div style="margin-top:6px"><?= e($b['address']) ?></div>
      </div>
      <div>
        <h4><?= t('foot.contact') ?></h4>
        <ul>
          <li><a href="tel:<?= e(preg_replace('/\s+/', '', $b['phone'])) ?>"><?= e($b['phone']) ?></a></li>
          <li><a href="tel:<?= e(preg_replace('/\s+/', '', $b['phone2'])) ?>"><?= e($b['phone2']) ?></a></li>
          <li><a href="mailto:<?= e($b['email']) ?>"><?= e($b['email']) ?></a></li>
          <li><a href="<?= e($b['zalo']) ?>" rel="noopener">Zalo</a> · <a href="<?= e($b['facebook']) ?>" rel="noopener">Facebook</a> · <a href="<?= e($b['tiktok']) ?>" rel="noopener">TikTok</a></li>
        </ul>
      </div>
      <div>
        <h4>WNTerm</h4>
        <ul>
          <li><a href="/#tai-ve"><?= t('nav.download') ?></a></li>
          <li><a href="/dang-ky"><?= t('nav.register') ?></a></li>
          <li><a href="/dang-nhap"><?= t('nav.login') ?></a></li>
          <li><a href="/khoi-phuc"><?= t('foot.recover') ?></a></li>
          <li><a href="<?= e($b['site']) ?>" rel="noopener">webnow.vn</a></li>
        </ul>
      </div>
    </div>
    <div class="copy">© <?= date('Y') ?> <?= e($b['short']) ?>. WebNow Terminal (WNTerm) — <?= e($b['slogan']) ?>.</div>
  </div>
</footer>
<?php if (!empty($scripts)): ?>
<script src="/assets/js/wn-i18n.js?v=2.1.0"></script>
<?php foreach ((array)$scripts as $s): ?>
<script src="/assets/js/<?= e($s) ?>?v=2.1.0"></script>
<?php endforeach; endif; ?>
</body>
</html>
