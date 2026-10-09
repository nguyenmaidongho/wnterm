using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WNTerm.Services;

namespace WNTerm.App.Services;

/// <summary>Thông tin bản mới nhất của nền tảng hiện tại (từ GET /api/v1/version).</summary>
public sealed record UpdateInfo(string Version, string MinVersion, string Notes, string PageUrl, string? DownloadUrl, string? Sha256, long Size)
{
    /// <summary>Bản đang chạy thấp hơn bản tối thiểu server còn hỗ trợ → bắt buộc cập nhật.</summary>
    public bool IsRequired(Version current) => UpdateChecker.TryParse(MinVersion, out var min) && current < min;
}

/// <summary>
/// Kiểm tra bản mới. App chỉ gửi một yêu cầu GET công khai (không kèm dữ liệu cá nhân); lỗi mạng thì im lặng bỏ qua.
/// Phiên bản đang chạy do "đầu" nền tảng khai báo (Desktop: version của assembly, Android: versionName).
/// </summary>
public static class UpdateChecker
{
    /// <summary>Do head từng nền tảng gán lúc khởi động. Null = không biết → không kiểm tra.</summary>
    public static string? CurrentVersionText { get; set; }

    public static string PlatformKey =>
        OperatingSystem.IsAndroid() ? "android" : OperatingSystem.IsIOS() ? "ios" : "windows";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Chỉ tải bộ cài từ chính máy chủ WNTerm qua HTTPS. (Đặt lại được để kiểm thử với máy chủ cục bộ.)</summary>
    public static Func<Uri, bool> IsTrustedDownload { get; set; } =
        u => u.Scheme == Uri.UriSchemeHttps && u.Host.EndsWith("webnow.vn", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? s, out Version v)
    {
        v = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(s)) return false;
        var core = s.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out v!);
    }

    public static Version? Current => TryParse(CurrentVersionText, out var v) ? v : null;

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var cur = Current;
        if (cur == null) return null;
        try
        {
            string baseUrl = AccountService.DefaultBaseUrl;
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/version?lang=" + LocalizationManager.CurrentLanguage);
            req.Headers.UserAgent.ParseAdd("WNTerm/" + CurrentVersionText);
            using var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty(PlatformKey, out var p)) return null;
            string S(string n) => p.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            var info = new UpdateInfo(S("version"), S("minVersion"), S("notes"), S("url"),
                S("downloadUrl") is { Length: > 0 } d ? d : null,
                S("sha256") is { Length: 64 } h ? h : null,
                p.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0);
            return TryParse(info.Version, out var latest) && latest > cur ? info : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Windows: tải file cài (zip) về thư mục tạm có báo tiến độ, kiểm tra SHA-256 do server công bố, giải nén bộ cài.
    /// Trả đường dẫn file .exe; ném lỗi nếu hỏng/không khớp (không bao giờ chạy file chưa kiểm tra).
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        if (info.DownloadUrl == null || info.Sha256 == null) throw new InvalidOperationException("no download info");
        var uri = new Uri(info.DownloadUrl);
        if (!IsTrustedDownload(uri)) throw new InvalidOperationException("untrusted download host");

        string dir = Path.Combine(Path.GetTempPath(), "WNTerm-update");
        if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch { } }
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, "update.zip");

        using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
        using (var res = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? info.Size;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report(done * 100.0 / total);
            }
        }

        string hash;
        await using (var fs = File.OpenRead(zip))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(zip); } catch { }
            throw new InvalidDataException("checksum mismatch");
        }

        string exe = Path.Combine(dir, "WNTerm-Setup.exe");
        using (var za = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            var entry = za.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("no installer in archive");
            System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, exe, true);
        }
        return exe;
    }
}
