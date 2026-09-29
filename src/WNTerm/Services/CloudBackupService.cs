using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using WNTerm.Models;

namespace WNTerm.Services;

public record CloudBackupItem(string Key, long Size, DateTime LastModifiedUtc)
{
    public string FileName => Key.Contains('/') ? Key[(Key.LastIndexOf('/') + 1)..] : Key;
    public string LastModifiedLocal => LastModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public string SizeText => Size >= 1024 * 1024 ? $"{Size / 1024.0 / 1024.0:0.0} MB" : $"{Math.Max(1, Size / 1024)} KB";
}

/// <summary>
/// Sao lưu danh sách VM lên S3 (hoặc S3-compatible). File tải lên chính là file .wnterm
/// (mã hóa AES-256-GCM bằng mật khẩu backup) nên khôi phục dùng lại luồng Import sẵn có.
/// </summary>
public class CloudBackupService
{
    private readonly AppSettings _settings;
    private readonly SessionStore _store;

    public CloudBackupService(AppSettings settings, SessionStore store)
    {
        _settings = settings;
        _store = store;
    }

    public static bool IsConfigured(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.S3Bucket) &&
        !string.IsNullOrWhiteSpace(s.S3AccessKey) &&
        !string.IsNullOrEmpty(s.S3SecretKeyEnc) &&
        !string.IsNullOrEmpty(s.CloudBackupPasswordEnc);

    private string Prefix
    {
        get
        {
            string p = (_settings.S3Prefix ?? "").Trim().TrimStart('/');
            if (p.Length > 0 && !p.EndsWith('/')) p += "/";
            return p;
        }
    }

    private AmazonS3Client CreateClient()
    {
        var cfg = new AmazonS3Config
        {
            ForcePathStyle = _settings.S3PathStyle,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        string endpoint = (_settings.S3Endpoint ?? "").Trim();
        string region = string.IsNullOrWhiteSpace(_settings.S3Region) ? "us-east-1" : _settings.S3Region.Trim();
        if (endpoint.Length > 0)
        {
            cfg.ServiceURL = endpoint;
            cfg.AuthenticationRegion = region;
        }
        else
        {
            cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }

        string secret = SecretProtector.Decrypt(_settings.S3SecretKeyEnc) ?? "";
        return new AmazonS3Client(new BasicAWSCredentials(_settings.S3AccessKey.Trim(), secret), cfg);
    }

    public async Task TestAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _settings.S3Bucket.Trim(),
            Prefix = Prefix,
            MaxKeys = 1
        }, ct);
    }

    /// <summary>Backup toàn bộ VM (kèm mật khẩu + key file, mã hóa) lên S3. Trả về key đã tải lên.</summary>
    public async Task<string> BackupAsync(CancellationToken ct = default)
    {
        string? password = SecretProtector.Decrypt(_settings.CloudBackupPasswordEnc);
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException(WNTerm.Services.LocalizationManager.Tr("Backup password is not set.", "Chưa đặt mật khẩu backup."));

        var sessions = _store.Load(out _);
        string tmp = Path.Combine(Path.GetTempPath(), $"wnterm-{Guid.NewGuid():N}.wnterm");
        try
        {
            await Task.Run(() => new SessionExporter().Export(sessions, tmp, password, includeKeyContent: true), ct);

            string machine = new string(Environment.MachineName.Where(char.IsLetterOrDigit).ToArray());
            string key = $"{Prefix}wnterm-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{machine}.wnterm";

            using var client = CreateClient();
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _settings.S3Bucket.Trim(),
                Key = key,
                FilePath = tmp,
                ContentType = "application/octet-stream"
            }, ct);

            _settings.LastCloudBackupUtc = DateTime.UtcNow;
            await PruneAsync(client, ct);
            return key;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private async Task PruneAsync(AmazonS3Client client, CancellationToken ct)
    {
        int keep = Math.Max(1, _settings.CloudKeepCount);
        var all = await ListInternalAsync(client, ct);
        foreach (var old in all.Skip(keep))
        {
            try { await client.DeleteObjectAsync(_settings.S3Bucket.Trim(), old.Key, ct); } catch { }
        }
    }

    private async Task<List<CloudBackupItem>> ListInternalAsync(AmazonS3Client client, CancellationToken ct)
    {
        var items = new List<CloudBackupItem>();
        string? token = null;
        do
        {
            var res = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _settings.S3Bucket.Trim(),
                Prefix = Prefix,
                ContinuationToken = token
            }, ct);
            foreach (var o in res.S3Objects ?? new List<S3Object>())
            {
                if (o.Key.EndsWith(".wnterm", StringComparison.OrdinalIgnoreCase))
                    items.Add(new CloudBackupItem(o.Key, o.Size ?? 0, (o.LastModified ?? DateTime.UtcNow).ToUniversalTime()));
            }
            token = res.IsTruncated == true ? res.NextContinuationToken : null;
        } while (token != null);

        return items.OrderByDescending(i => i.LastModifiedUtc).ToList();
    }

    public async Task<List<CloudBackupItem>> ListAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        return await ListInternalAsync(client, ct);
    }

    /// <summary>Tải bản backup về file tạm và trả về đường dẫn.</summary>
    public async Task<string> DownloadAsync(string key, CancellationToken ct = default)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"restore-{Guid.NewGuid():N}.wnterm");
        using var client = CreateClient();
        using var res = await client.GetObjectAsync(_settings.S3Bucket.Trim(), key, ct);
        await res.WriteResponseStreamToFileAsync(tmp, false, ct);
        return tmp;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var client = CreateClient();
        await client.DeleteObjectAsync(_settings.S3Bucket.Trim(), key, ct);
    }
}
