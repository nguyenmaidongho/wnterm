<?php
declare(strict_types=1);

/** Kiểm tra bộ ký AWS SigV4 bằng ví dụ chính thức "GET Object" trong tài liệu AWS S3. */
require __DIR__ . '/../src/S3.php';

$h = S3::sign(
    'GET', '/test.txt', '',
    ['host' => 'examplebucket.s3.amazonaws.com', 'range' => 'bytes=0-9'],
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
    'AKIAIOSFODNN7EXAMPLE', 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY',
    'us-east-1', '20130524T000000Z'
);
$expected = 'f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41';
preg_match('/Signature=([0-9a-f]+)/', $h['authorization'], $m);
$ok = ($m[1] ?? '') === $expected;
echo ($ok ? '[ OK ] ' : '[FAIL] ') . "chữ ký SigV4 khớp ví dụ AWS\n";
echo "  nhận được: " . ($m[1] ?? '?') . "\n  mong đợi:  $expected\n";
echo '  SignedHeaders: ' . (preg_match('/SignedHeaders=([^,]+)/', $h['authorization'], $s) ? $s[1] : '?') . "\n";
exit($ok ? 0 : 1);
