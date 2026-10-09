<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('home.title');
$desc = t('home.desc');
$active = 'home';
$turnstileKey = (string)wn_config('turnstile.site_key', '');
$feedbackOn = (string)wn_config('telegram.bot_token', '') !== '' && (string)wn_config('telegram.chat_id', '') !== '';
view('head', compact('title', 'desc', 'active'));
if ($turnstileKey !== '' && $feedbackOn) {
    echo '<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>';
}
view('home', compact('turnstileKey', 'feedbackOn'));
view('foot', $feedbackOn ? ['scripts' => ['feedback.js']] : []);
