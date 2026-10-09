<?php
declare(strict_types=1);
require __DIR__ . '/../src/bootstrap.php';

$title = t('home.title');
$desc = t('home.desc');
$active = 'home';
view('head', compact('title', 'desc', 'active'));
view('home');
view('foot');
