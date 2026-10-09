<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('home.title');
$desc = t('home.desc');
$active = 'home';
$turnstileKey = (string)wn_config('turnstile.site_key', '');
$feedbackOn = (string)wn_config('telegram.bot_token', '') !== '' && (string)wn_config('telegram.chat_id', '') !== '';
view('head', compact('title', 'desc', 'active'));
echo '<main>';
view('home', compact('turnstileKey', 'feedbackOn'));
echo '</main>';
view('foot', $feedbackOn ? ['scripts' => ['feedback.js']] : []);
