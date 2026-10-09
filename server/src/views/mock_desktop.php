<div class="win" aria-label="<?= e(t('mock.desktop_aria')) ?>">
      <div class="bar"><i class="dot"></i><i class="dot"></i><i class="dot"></i><span>WN Term — web-prod-01</span></div>
      <div class="tb"><i class="p"><?= t('mock.connect') ?></i><i><?= t('mock.addvm') ?></i><i>Export</i><i>Import</i><i>Cloud</i><i><?= t('mock.quick') ?></i><i><?= t('mock.settings') ?></i></div>
      <div class="body">
        <div class="side">
          <div class="tabs2"><b class="a">Sessions</b><b>SFTP</b></div>
          <div class="search"></div>
          <div class="chips"><u class="a"><?= t('mock.all') ?></u><u>prod 4</u><u>staging 3</u><u>db 2</u></div>
          <div class="grp"><span><?= t('mock.pinned') ?></span><span>2/2 <?= t('mock.online') ?></span></div>
          <div class="vm sel live"><span class="ic"></span><div><b>web-prod-01</b><small>deploy@web-prod-01</small></div><em>12 ms</em></div>
          <div class="vm"><span class="ic"></span><div><b>db-master</b><small>root@db-master</small></div><em>18 ms</em></div>
          <div class="grp"><span>prod</span><span>3/4 <?= t('mock.online') ?></span></div>
          <div class="vm"><span class="ic"></span><div><b>api-gateway</b><small>root@api-gateway</small></div><em>9 ms</em></div>
          <div class="vm"><span class="ic"></span><div><b>cache-redis</b><small>root@cache-redis</small></div><em>11 ms</em></div>
          <div class="vm off"><span class="ic"></span><div><b>backup-old</b><small>root@backup-old</small></div><em><?= t('mock.offline') ?></em></div>
        </div>
        <div class="main">
          <div class="tabstrip"><div class="tab a">web-prod-01</div><div class="tab">db-master</div><div class="tab">api-gateway</div></div>
<div class="term"><span class="g">deploy@web-prod-01</span>:<span class="b">~/app</span>$ git pull &amp;&amp; systemctl restart app
<span class="d">Updating 3f2a91c..b7d04e8</span>
<span class="d">Fast-forward</span>
 src/api/orders.php   | <span class="y">24</span> <span class="g">+++++++++++</span><span class="r">---</span>
 src/queue/worker.php | <span class="y"> 9</span> <span class="g">++++</span><span class="r">-</span>
<span class="g">●</span> app.service - WebNow App
   Active: <span class="g">active (running)</span> since 09:41:07
<span class="g">deploy@web-prod-01</span>:<span class="b">~/app</span>$ tail -f storage/logs/app.log
<span class="c">[09:41:09]</span> INFO  queue: 12 jobs processed
<span class="c">[09:41:10]</span> <span class="y">WARN</span>  slow query 412ms /orders/export
<span class="g">deploy@web-prod-01</span>:<span class="b">~/app</span>$ <span class="cur"></span></div>
          <div class="mon"><span><b>web-prod-01</b></span><span>CPU 23% <i class="spark"></i></span><span>RAM 3.1 GB / 8 GB <i class="spark"></i></span><span>↑ 0.4 Mb/s</span><span>↓ 1.2 Mb/s</span><span>⏱ 42 <?= t('mock.days') ?></span><span>/: 61%</span></div>
        </div>
      </div>
    </div>
