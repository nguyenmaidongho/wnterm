<?php
declare(strict_types=1);

/**
 * Máy khách S3 tối giản (AWS Signature V4) dùng cURL — không cần Composer.
 * Dùng cho S3-compatible (CloudFly, R2, MinIO, Wasabi...) kiểu path-style hoặc virtual-host.
 */
final class S3
{
    private string $endpoint;
    private string $region;
    private string $bucket;
    private string $accessKey;
    private string $secretKey;
    private bool $pathStyle;

    public function __construct(array $cfg)
    {
        $this->endpoint  = rtrim((string)$cfg['endpoint'], '/');
        $this->region    = (string)($cfg['region'] ?? 'us-east-1');
        $this->bucket    = (string)$cfg['bucket'];
        $this->accessKey = (string)$cfg['access_key'];
        $this->secretKey = (string)$cfg['secret_key'];
        $this->pathStyle = (bool)($cfg['path_style'] ?? true);
    }

    /** Mã hóa từng đoạn của khóa đối tượng theo chuẩn S3 (giữ dấu /). */
    public static function encodeKey(string $key): string
    {
        return implode('/', array_map('rawurlencode', explode('/', $key)));
    }

    /**
     * Tính các header ký (Authorization, x-amz-date, x-amz-content-sha256...).
     * Tách riêng để kiểm thử bằng ví dụ chính thức của AWS.
     *
     * @param array<string,string> $headers header cần ký (đã có Host)
     */
    public static function sign(
        string $method,
        string $canonicalUri,
        string $query,
        array $headers,
        string $payloadHash,
        string $accessKey,
        string $secretKey,
        string $region,
        string $amzDate,
        string $service = 's3'
    ): array {
        $headers['x-amz-date'] = $amzDate;
        $headers['x-amz-content-sha256'] = $payloadHash;

        $lower = [];
        foreach ($headers as $k => $v) {
            $lower[strtolower($k)] = trim(preg_replace('/\s+/', ' ', (string)$v));
        }
        ksort($lower);

        $canonicalHeaders = '';
        foreach ($lower as $k => $v) {
            $canonicalHeaders .= $k . ':' . $v . "\n";
        }
        $signedHeaders = implode(';', array_keys($lower));

        $canonicalRequest = implode("\n", [
            $method, $canonicalUri, $query, $canonicalHeaders, $signedHeaders, $payloadHash,
        ]);

        $date = substr($amzDate, 0, 8);
        $scope = "$date/$region/$service/aws4_request";
        $stringToSign = implode("\n", ['AWS4-HMAC-SHA256', $amzDate, $scope, hash('sha256', $canonicalRequest)]);

        $kDate = hash_hmac('sha256', $date, 'AWS4' . $secretKey, true);
        $kRegion = hash_hmac('sha256', $region, $kDate, true);
        $kService = hash_hmac('sha256', $service, $kRegion, true);
        $kSigning = hash_hmac('sha256', 'aws4_request', $kService, true);
        $signature = hash_hmac('sha256', $stringToSign, $kSigning);

        $lower['authorization'] = "AWS4-HMAC-SHA256 Credential=$accessKey/$scope, SignedHeaders=$signedHeaders, Signature=$signature";
        return $lower;
    }

    private function request(string $method, string $key, string $body = '', array $extra = []): array
    {
        $parsed = parse_url($this->endpoint);
        $scheme = $parsed['scheme'] ?? 'https';
        $host = $parsed['host'] ?? '';
        $port = isset($parsed['port']) ? ':' . $parsed['port'] : '';
        $basePath = rtrim($parsed['path'] ?? '', '/');

        if ($this->pathStyle) {
            $reqHost = $host . $port;
            $uri = $basePath . '/' . $this->bucket . '/' . self::encodeKey($key);
        } else {
            $reqHost = $this->bucket . '.' . $host . $port;
            $uri = $basePath . '/' . self::encodeKey($key);
        }

        $payloadHash = hash('sha256', $body);
        $amzDate = gmdate('Ymd\THis\Z');
        $signed = self::sign(
            $method, $uri, '', array_merge(['host' => $reqHost], $extra), $payloadHash,
            $this->accessKey, $this->secretKey, $this->region, $amzDate
        );

        $hdr = [];
        foreach ($signed as $k => $v) {
            if ($k !== 'host') {
                $hdr[] = $k . ': ' . $v;
            }
        }

        $ch = curl_init($scheme . '://' . $reqHost . $uri);
        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST  => $method,
            CURLOPT_HTTPHEADER     => $hdr,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_CONNECTTIMEOUT => 10,
            CURLOPT_TIMEOUT        => 60,
            CURLOPT_SSL_VERIFYPEER => true,
        ]);
        if ($method === 'PUT') {
            curl_setopt($ch, CURLOPT_POSTFIELDS, $body);
        }
        $resp = curl_exec($ch);
        $code = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
        $err = curl_error($ch);
        curl_close($ch);

        if ($resp === false) {
            throw new RuntimeException('S3 lỗi kết nối: ' . $err);
        }
        return [$code, (string)$resp];
    }

    public function put(string $key, string $data): void
    {
        [$code, $resp] = $this->request('PUT', $key, $data, ['content-type' => 'application/octet-stream']);
        if ($code < 200 || $code >= 300) {
            throw new RuntimeException("S3 PUT lỗi HTTP $code: " . substr($resp, 0, 200));
        }
    }

    public function get(string $key): ?string
    {
        [$code, $resp] = $this->request('GET', $key);
        if ($code === 404) {
            return null;
        }
        if ($code < 200 || $code >= 300) {
            throw new RuntimeException("S3 GET lỗi HTTP $code: " . substr($resp, 0, 200));
        }
        return $resp;
    }

    public function delete(string $key): void
    {
        [$code, $resp] = $this->request('DELETE', $key);
        if ($code >= 300 && $code !== 404) {
            throw new RuntimeException("S3 DELETE lỗi HTTP $code: " . substr($resp, 0, 200));
        }
    }
}
