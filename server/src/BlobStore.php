<?php
declare(strict_types=1);

/** Nơi lưu khối dữ liệu (đã mã hóa phía người dùng) theo từng phiên bản. */
interface BlobStoreInterface
{
    public function put(int $userId, int $version, string $data): void;
    public function get(int $userId, int $version): ?string;
    public function delete(int $userId, int $version): void;
}

final class BlobStore
{
    public static function create(): BlobStoreInterface
    {
        if (wn_config('storage', 'db') === 's3') {
            return new S3BlobStore(new S3((array)wn_config('s3')), (string)wn_config('s3.prefix', 'wnterm-vaults/'));
        }
        return new DbBlobStore();
    }
}

final class DbBlobStore implements BlobStoreInterface
{
    public function put(int $userId, int $version, string $data): void
    {
        Db::pdo()->prepare('INSERT INTO vault_blobs (user_id, version, data, created_at) VALUES (?, ?, ?, ?)
                            ON DUPLICATE KEY UPDATE data = VALUES(data), created_at = VALUES(created_at)')
            ->execute([$userId, $version, $data, Db::now()]);
    }

    public function get(int $userId, int $version): ?string
    {
        $st = Db::pdo()->prepare('SELECT data FROM vault_blobs WHERE user_id = ? AND version = ?');
        $st->execute([$userId, $version]);
        $v = $st->fetchColumn();
        return $v === false ? null : (string)$v;
    }

    public function delete(int $userId, int $version): void
    {
        Db::pdo()->prepare('DELETE FROM vault_blobs WHERE user_id = ? AND version = ?')->execute([$userId, $version]);
    }
}

final class S3BlobStore implements BlobStoreInterface
{
    private S3 $s3;
    private string $prefix;

    public function __construct(S3 $s3, string $prefix)
    {
        $this->s3 = $s3;
        $this->prefix = rtrim($prefix, '/') . '/';
    }

    private function key(int $userId, int $version): string
    {
        return $this->prefix . $userId . '/' . $version . '.bin';
    }

    public function put(int $userId, int $version, string $data): void
    {
        $this->s3->put($this->key($userId, $version), $data);
    }

    public function get(int $userId, int $version): ?string
    {
        return $this->s3->get($this->key($userId, $version));
    }

    public function delete(int $userId, int $version): void
    {
        $this->s3->delete($this->key($userId, $version));
    }
}
