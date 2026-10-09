<header class="hero" id="top">
  <div class="wrap">
    <div>
      <span class="pill"><?= t('hero.pill') ?></span>
      <h1><?= t('hero.h1') ?></h1>
      <p class="lead"><?= t('hero.lead') ?></p>
      <div class="cta">
        <a class="btn primary" href="/downloads/WNTerm-Setup.zip">
          <svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 5.5l7.5-1v7H3v-6zm8.5-1.2L21 3v8.5h-9.5V4.3zM3 12.5h7.5v7L3 18.5v-6zm8.5 0H21V21l-9.5-1.3v-7.2z"/></svg>
          <?= t('hero.dl_win') ?>
        </a>
        <a class="btn" href="/downloads/WNTerm-android-arm64.apk">
          <svg viewBox="0 0 24 24" fill="currentColor"><path d="M17.6 9.5a5.6 5.6 0 0 0-11.2 0h11.2zM9.2 7.7a.7.7 0 1 1 0-1.4.7.7 0 0 1 0 1.4zm5.6 0a.7.7 0 1 1 0-1.4.7.7 0 0 1 0 1.4zM6 10.3v7.3c0 .7.6 1.3 1.3 1.3h.9v2.6a1.2 1.2 0 0 0 2.4 0V19h2.8v2.5a1.2 1.2 0 0 0 2.4 0V19h.9c.7 0 1.3-.6 1.3-1.3v-7.4H6zM3.7 10.3a1.2 1.2 0 0 0-1.2 1.2v5a1.2 1.2 0 0 0 2.4 0v-5a1.2 1.2 0 0 0-1.2-1.2zm16.6 0a1.2 1.2 0 0 0-1.2 1.2v5a1.2 1.2 0 0 0 2.4 0v-5a1.2 1.2 0 0 0-1.2-1.2z"/></svg>
          <?= t('hero.dl_apk') ?>
        </a>
        <a class="btn" href="/app/">
          <svg viewBox="0 0 24 24" fill="currentColor"><path d="M16.4 12.6c0-2.3 1.9-3.4 2-3.5-1.1-1.6-2.8-1.8-3.4-1.8-1.4-.1-2.8.9-3.5.9-.7 0-1.8-.8-3-.8-1.5 0-3 .9-3.8 2.3-1.6 2.8-.4 6.9 1.2 9.200.8 1.100 1.700 2.300 2.900 2.300 1.200 0 1.600-.7 3-.7s1.800.7 3 .7c1.300 0 2.100-1.100 2.800-2.200.9-1.300 1.300-2.500 1.300-2.600-.1 0-2.500-1-2.500-3.800zM14.200 5.800c.6-.8 1.100-1.900.9-3-.9 0-2.100.6-2.700 1.400-.6.7-1.100 1.800-1 2.900 1.100.1 2.200-.5 2.800-1.300z"/></svg>
          <?= t('hero.dl_iphone') ?>
        </a>
      </div>
      <div class="meta"><?= t('hero.meta') ?></div>
    </div>

    <!-- Mô phỏng cửa sổ ứng dụng -->
    <?php view('mock_desktop'); ?>
  </div>
</header>

<section id="tinh-nang">
  <div class="wrap">
    <div class="head">
      <div class="eyebrow"><?= t('feat.eyebrow') ?></div>
      <h2><?= t('feat.h2') ?></h2>
      <p><?= t('feat.p') ?></p>
    </div>
    <div class="grid">
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 9l3 3-3 3M12 15h5"/></svg></div><h3><?= t('feat.1.h') ?></h3><p><?= t('feat.1.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg></div><h3><?= t('feat.2.h') ?></h3><p><?= t('feat.2.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12h4l3-8 4 16 3-8h4"/></svg></div><h3><?= t('feat.3.h') ?></h3><p><?= t('feat.3.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 18a4 4 0 0 1-.6-7.95A6 6 0 0 1 18 9a4.5 4.5 0 0 1-.5 9H7z"/></svg></div><h3><?= t('feat.4.h') ?></h3><p><?= t('feat.4.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6"/></svg></div><h3><?= t('feat.5.h') ?></h3><p><?= t('feat.5.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="7" y="2" width="10" height="20" rx="2"/><path d="M11 18h2"/></svg></div><h3><?= t('feat.6.h') ?></h3><p><?= t('feat.6.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="6" rx="1.5"/><rect x="3" y="15" width="18" height="6" rx="1.5"/><path d="M7 6h.01M7 18h.01"/></svg></div><h3><?= t('feat.7.h') ?></h3><p><?= t('feat.7.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/></svg></div><h3><?= t('feat.8.h') ?></h3><p><?= t('feat.8.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v12m0 0l-4-4m4 4l4-4M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"/></svg></div><h3><?= t('feat.9.h') ?></h3><p><?= t('feat.9.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg></div><h3><?= t('feat.10.h') ?></h3><p><?= t('feat.10.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="10" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg></div><h3><?= t('feat.11.h') ?></h3><p><?= t('feat.11.p') ?></p></div>
      <div class="card"><div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 17V9l8-5 8 5v8l-8 4z"/><path d="M4 9l8 5 8-5M12 14v7"/></svg></div><h3><?= t('feat.12.h') ?></h3><p><?= t('feat.12.p') ?></p></div>
    </div>
  </div>
</section>

<section id="sftp" style="background:linear-gradient(180deg,transparent,var(--bg2) 30%,var(--bg2) 70%,transparent)">
  <div class="wrap split">
    <div class="txt">
      <div class="eyebrow">SFTP</div>
      <h2><?= t('sftp.h2') ?></h2>
      <p><?= t('sftp.p') ?></p>
      <ul class="ticks">
        <li><?= t('sftp.l1') ?></li>
        <li><?= t('sftp.l2') ?></li>
        <li><?= t('sftp.l3') ?></li>
        <li><?= t('sftp.l4') ?></li>
      </ul>
    </div>
    <?php view('mock_sftp'); ?>
  </div>
</section>

<section id="dong-bo">
  <div class="wrap">
    <div class="head">
      <div class="eyebrow"><?= t('sync.eyebrow') ?></div>
      <h2><?= t('sync.h2') ?></h2>
      <p><?= t('sync.p') ?></p>
    </div>
    <div class="grid">
      <div class="card"><h3><?= t('sync.1.h') ?></h3><p><?= t('sync.1.p') ?></p></div>
      <div class="card"><h3><?= t('sync.2.h') ?></h3><p><?= t('sync.2.p') ?></p></div>
      <div class="card"><h3><?= t('sync.3.h') ?></h3><p><?= t('sync.3.p') ?></p></div>
    </div>
    <p style="text-align:center;color:var(--muted);margin:26px 0 0;font-size:14px"><?= t('sync.cta') ?></p>
  </div>
</section>

<section id="di-dong">
  <div class="wrap split rev">
    <div class="txt">
      <div class="eyebrow"><?= t('mob.eyebrow') ?></div>
      <h2><?= t('mob.h2') ?></h2>
      <p><?= t('mob.p') ?></p>
      <ul class="ticks">
        <li><?= t('mob.l1') ?></li>
        <li><?= t('mob.l2') ?></li>
        <li><?= t('mob.l3') ?></li>
        <li><?= t('mob.l4') ?></li>
        <li><?= t('mob.l5') ?></li>
      </ul>
    </div>
    <?php view('mock_phones'); ?>
  </div>
</section>

<section id="iphone" style="background:var(--bg2)">
  <div class="wrap">
    <div class="head">
      <div class="eyebrow"><?= t('ip.eyebrow') ?></div>
      <h2><?= t('ip.h2') ?></h2>
      <p><?= t('ip.p') ?></p>
    </div>
    <div class="grid">
      <div class="card"><h3><?= t('ip.1.h') ?></h3><p><?= t('ip.1.p') ?></p></div>
      <div class="card"><h3><?= t('ip.2.h') ?></h3><p><?= t('ip.2.p') ?></p></div>
      <div class="card"><h3><?= t('ip.3.h') ?></h3><p><?= t('ip.3.p') ?></p></div>
    </div>
    <ul class="ticks" style="max-width:760px;margin:28px auto 0">
      <li><?= t('ip.l1') ?></li>
      <li><?= t('ip.l2') ?></li>
      <li><?= t('ip.l3') ?></li>
      <li><?= t('ip.l4') ?></li>
      <li><?= t('ip.l5') ?></li>
    </ul>
  </div>
</section>

<section id="bao-mat">
  <div class="wrap">
    <div class="head">
      <div class="eyebrow"><?= t('sec.eyebrow') ?></div>
      <h2><?= t('sec.h2') ?></h2>
      <p><?= t('sec.p') ?></p>
    </div>
    <div class="grid">
      <div class="card"><h3><?= t('sec.1.h') ?></h3><p><?= t('sec.1.p') ?></p></div>
      <div class="card"><h3><?= t('sec.2.h') ?></h3><p><?= t('sec.2.p') ?></p></div>
      <div class="card"><h3><?= t('sec.3.h') ?></h3><p><?= t('sec.3.p') ?></p></div>
      <div class="card"><h3><?= t('sec.4.h') ?></h3><p><?= t('sec.4.p') ?></p></div>
      <div class="card"><h3><?= t('sec.5.h') ?></h3><p><?= t('sec.5.p') ?></p></div>
      <div class="card"><h3><?= t('sec.6.h') ?></h3><p><?= t('sec.6.p') ?></p></div>
    </div>
  </div>
</section>

<section id="tai-ve" style="background:var(--bg2)">
  <div class="wrap">
    <div class="head">
      <div class="eyebrow"><?= t('dl.eyebrow') ?></div>
      <h2><?= t('dl.h2') ?></h2>
      <p><?= t('dl.p') ?></p>
    </div>
    <div class="dl">
      <div class="dlc">
        <div class="os"><svg viewBox="0 0 24 24" fill="#58b6f5"><path d="M3 5.5l7.5-1v7H3v-6zm8.5-1.2L21 3v8.5h-9.5V4.3zM3 12.5h7.5v7L3 18.5v-6zm8.5 0H21V21l-9.5-1.3v-7.2z"/></svg>Windows <span class="tag ok"><?= t('dl.ready') ?></span></div>
        <ul><li><?= t('dl.win.1') ?></li><li><?= t('dl.win.2') ?></li><li><?= t('dl.win.3') ?></li></ul>
        <a class="btn primary" href="/downloads/WNTerm-Setup.zip"><?= t('dl.win.btn') ?></a>
      </div>
      <div class="dlc">
        <div class="os"><svg viewBox="0 0 24 24" fill="#7fdc4a"><path d="M17.6 9.5a5.6 5.6 0 0 0-11.2 0h11.2zM6 10.3v7.3c0 .7.6 1.3 1.3 1.3h.9v2.6a1.2 1.2 0 0 0 2.4 0V19h2.8v2.5a1.2 1.2 0 0 0 2.4 0V19h.9c.7 0 1.3-.6 1.3-1.3v-7.4H6z"/></svg>Android <span class="tag ok"><?= t('dl.ready') ?></span></div>
        <ul><li><?= t('dl.and.1') ?></li><li><?= t('dl.and.2') ?></li><li><?= t('dl.and.3') ?></li></ul>
        <a class="btn" href="/downloads/WNTerm-android-arm64.apk"><?= t('dl.and.btn') ?></a>
      </div>
      <div class="dlc">
        <div class="os"><svg viewBox="0 0 24 24" fill="#c9d1d9"><path d="M16.4 12.6c0-2.3 1.9-3.4 2-3.5-1.1-1.6-2.8-1.8-3.4-1.8-1.4-.1-2.8.9-3.5.9-.7 0-1.8-.8-3-.8-1.5 0-3 .9-3.8 2.3-1.6 2.8-.4 6.900 1.200 9.200.8 1.100 1.700 2.300 2.900 2.300 1.200 0 1.600-.7 3-.7s1.800.7 3 .7c1.300 0 2.100-1.100 2.800-2.200.9-1.300 1.300-2.500 1.300-2.600-.1 0-2.500-1-2.500-3.800zM14.200 5.800c.6-.8 1.100-1.900.9-3-.9 0-2.100.6-2.700 1.400-.6.7-1.100 1.800-1 2.900 1.100.1 2.200-.5 2.800-1.300z"/></svg>iPhone / iPad <span class="tag ok"><?= t('dl.web') ?></span></div>
        <ul><li><?= t('dl.ios.1') ?></li><li><?= t('dl.ios.2') ?></li><li><?= t('dl.ios.3') ?></li><li><?= t('dl.ios.4') ?></li></ul>
        <a class="btn primary" href="/app/"><?= t('dl.ios.btn') ?></a>
      </div>
    </div>
    <div class="install"><?= t('dl.install') ?></div>
  </div>
</section>

<section id="faq" style="padding-top:20px">
  <div class="wrap" style="max-width:820px">
    <div class="head"><div class="eyebrow"><?= t('faq.eyebrow') ?></div><h2><?= t('faq.h2') ?></h2></div>
    <details><summary><?= t('faq.1.q') ?></summary><p><?= t('faq.1.a') ?></p></details>
    <details><summary><?= t('faq.2.q') ?></summary><p><?= t('faq.2.a') ?></p></details>
    <details><summary><?= t('faq.3.q') ?></summary><p><?= t('faq.3.a') ?></p></details>
    <details><summary><?= t('faq.4.q') ?></summary><p><?= t('faq.4.a') ?></p></details>
    <details><summary><?= t('faq.5.q') ?></summary><p><?= t('faq.5.a') ?></p></details>
    <details><summary><?= t('faq.6.q') ?></summary><p><?= t('faq.6.a') ?></p></details>
    <details><summary><?= t('faq.7.q') ?></summary><p><?= t('faq.7.a') ?></p></details>
    <details><summary><?= t('faq.8.q') ?></summary><p><?= t('faq.8.a') ?></p></details>
    <details><summary><?= t('faq.9.q') ?></summary><p><?= t('faq.9.a') ?></p></details>
  </div>
</section>
