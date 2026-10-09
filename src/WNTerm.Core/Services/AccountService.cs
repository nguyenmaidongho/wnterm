using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WNTerm.Models;

namespace WNTerm.Services;

public class AccountException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public AccountException(int status, string code, string message) : base(message) { Status = status; Code = code; }
}

public class VaultSession
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("pinned")] public bool Pinned { get; set; }
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("port")] public int Port { get; set; } = 22;
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("passphrase")] public string? Passphrase { get; set; }
    [JsonPropertyName("keyFileName")] public string? KeyFileName { get; set; }
    [JsonPropertyName("keyFileContent")] public string? KeyFileContent { get; set; }
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }

    [JsonIgnore] public DateTime EffectiveUpdatedAt => UpdatedAt ?? CreatedAt ?? DateTime.MinValue;
    [JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username) && Port is >= 1 and <= 65535;
}

/// <summary>Dấu xóa: VM đã bị xóa lúc nào (để máy khác cũng chuyển VM đó vào thùng rác).</summary>
public class VaultTombstone
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("at")] public DateTime At { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class VaultData
{
    /// <summary>1 = bản đầu (không có thời điểm sửa/dấu xóa); 2 = gộp theo thời điểm sửa + dấu xóa.</summary>
    [JsonPropertyName("version")] public int Version { get; set; } = 2;
    [JsonPropertyName("savedAt")] public DateTime? SavedAt { get; set; }
    [JsonPropertyName("device")] public string? Device { get; set; }
    [JsonPropertyName("sessions")] public List<VaultSession> Sessions { get; set; } = new();
    [JsonPropertyName("deleted")] public List<VaultTombstone> Deleted { get; set; } = new();
    /// <summary>Lệnh đã lưu (tùy chọn; thiếu = rỗng). Thiết bị cũ không biết trường này sẽ bỏ nó khi ghi lại.</summary>
    [JsonPropertyName("snippets")] public List<Snippet> Snippets { get; set; } = new();
    [JsonPropertyName("deletedSnippets")] public List<SnippetTombstone> DeletedSnippets { get; set; } = new();
}

public record SyncResult(int Added, int Updated, int Total, int ServerVersion, int Deleted = 0);

/// <summary>Một phiên bản dữ liệu còn giữ trên máy chủ.</summary>
public record VaultVersionInfo(int Version, DateTime CreatedAtUtc, long Size, string Device, bool IsCurrent)
{
    public string CreatedAtText => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>So sánh một bản (trên máy chủ) với dữ liệu máy này.</summary>
public record VaultPreview(int Total, int MissingHere, int Different, int OnlyHere, List<string> MissingNames);

/// <summary>
/// Tài khoản WNTerm (https://wnterm.webnow.vn): mã hóa đầu-cuối ở máy khách, giao thức khớp với
/// server/tools/test_api.php và server/public/assets/js/wn-crypto.js. Máy chủ không bao giờ thấy mật khẩu
/// hay khóa dữ liệu. Danh sách VM được đồng bộ dưới dạng một "vault" mã hóa AES-256-GCM.
/// </summary>
public class AccountService
{
    public const string DefaultBaseUrl = "https://wnterm.webnow.vn/api/v1";
    private const int NewKdfIterations = 600000;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _store;
    private readonly SnippetStore _snippets;
    private readonly AppPaths _paths;

    public AccountService(AppSettings settings, SettingsStore settingsStore, SessionStore store, AppPaths? paths = null, SnippetStore? snippets = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _store = store;
        _paths = paths ?? AppPaths.Default;
        _snippets = snippets ?? new SnippetStore(_paths);
    }

    private string BaseUrl => string.IsNullOrWhiteSpace(_settings.AccountApiUrl) ? DefaultBaseUrl : _settings.AccountApiUrl.TrimEnd('/');

    public bool IsLoggedIn => !string.IsNullOrEmpty(_settings.AccountTokenEnc) && !string.IsNullOrEmpty(_settings.AccountVaultKeyEnc);
    public string Email => _settings.AccountEmail ?? "";

    // ===== Mã hóa =====

    private static string NormEmail(string email) => email.Trim().ToLowerInvariant();

    private static (byte[] auth, byte[] enc) Derive(string password, string email, int iterations)
    {
        byte[] master = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)),
            Encoding.UTF8.GetBytes("wnterm:v1:" + NormEmail(email)), iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            return (HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes("wnterm-auth")),
                    HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes("wnterm-enc")));
        }
        finally { CryptographicOperations.ZeroMemory(master); }
    }

    private static byte[] Hkdf(byte[] key, string info) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes(info));

    /// <summary>iv(12) | ciphertext | tag(16)</summary>
    private static byte[] Seal(byte[] key, byte[] plain)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(12);
        byte[] ct = new byte[plain.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plain, ct, tag);
        return iv.Concat(ct).Concat(tag).ToArray();
    }

    private static byte[]? Open(byte[] key, byte[] raw)
    {
        if (raw.Length < 28) return null;
        try
        {
            byte[] pt = new byte[raw.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(raw.AsSpan(0, 12), raw.AsSpan(12, pt.Length), raw.AsSpan(raw.Length - 16), pt);
            return pt;
        }
        catch (CryptographicException) { return null; }
    }

    private const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string FormatRecovery(byte[] bytes)
    {
        var sb = new StringBuilder();
        int bits = 0, value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(B32[(value >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(B32[(value << (5 - bits)) & 31]);
        string s = sb.ToString();
        return string.Join("-", Enumerable.Range(0, (s.Length + 3) / 4).Select(i => s.Substring(i * 4, Math.Min(4, s.Length - i * 4))));
    }

    public static byte[]? ParseRecovery(string text)
    {
        var out_ = new List<byte>();
        int bits = 0, value = 0;
        foreach (char c in text.ToUpperInvariant())
        {
            int i = B32.IndexOf(c);
            if (i < 0) continue;
            value = ((value << 5) | i) & 0xFFFF; bits += 5;
            if (bits >= 8) { out_.Add((byte)((value >> (bits - 8)) & 255)); bits -= 8; }
        }
        return out_.Count == 20 ? out_.ToArray() : null;
    }

    // ===== HTTP =====

    /// <summary>Gọi API; yêu cầu GET bị mạng chập chờn (timeout/lỗi kết nối) được tự thử lại tối đa 2 lần.</summary>
    private async Task<JsonElement> CallAsync(HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        int attempts = method == HttpMethod.Get ? 3 : 1;
        for (int i = 1; ; i++)
        {
            try { return await CallOnceAsync(method, path, body, auth, ct); }
            catch (AccountException ex) when (ex.Code is "network" or "timeout" && i < attempts)
            {
                await Task.Delay(1000 * i, ct);
            }
        }
    }

    private async Task<JsonElement> CallOnceAsync(HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (auth)
        {
            string? token = SecretProtector.Decrypt(_settings.AccountTokenEnc);
            if (string.IsNullOrEmpty(token)) throw new AccountException(401, "not_logged_in", LocalizationManager.Tr("Not signed in.", "Chưa đăng nhập."));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage res;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try { res = await Http.SendAsync(req, timeout.Token); }
        catch (HttpRequestException ex)
        {
            throw new AccountException(0, "network", LocalizationManager.Tr("Cannot reach the server: ", "Không kết nối được máy chủ: ") + ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AccountException(0, "timeout", LocalizationManager.Tr("The server did not respond in time, please try again.", "Máy chủ phản hồi quá lâu, hãy thử lại."));
        }
        using (res)
        {
            string text = await res.Content.ReadAsStringAsync(timeout.Token);
            JsonElement root;
            try { root = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException)
            {
                throw new AccountException((int)res.StatusCode, "bad_response", LocalizationManager.Tr("Unexpected server response (", "Máy chủ trả về dữ liệu lạ (") + (int)res.StatusCode + ").");
            }
            if (!res.IsSuccessStatusCode)
            {
                string code = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "HTTP " + (int)res.StatusCode;
                throw new AccountException((int)res.StatusCode, code, msg);
            }
            return root;
        }
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out int i) ? i : 0;

    /// <summary>Tên thiết bị hiển thị trong danh sách thiết bị / lịch sử backup. Điện thoại gán tên máy thật (vd. "Samsung SM-F721B").</summary>
    public static string? DeviceNameOverride { get; set; }

    private static string DeviceName() => string.IsNullOrWhiteSpace(DeviceNameOverride) ? Environment.MachineName : DeviceNameOverride!;

    private void SaveLogin(string email, string token, byte[] vaultKey, int iterations, int vaultVersion)
    {
        _settings.AccountEmail = NormEmail(email);
        _settings.AccountTokenEnc = SecretProtector.Encrypt(token);
        _settings.AccountVaultKeyEnc = SecretProtector.Encrypt(Convert.ToBase64String(vaultKey));
        _settings.AccountKdfIterations = iterations;
        _settings.AccountVaultVersion = vaultVersion;
        _settingsStore.Save(_settings);
    }

    private byte[] VaultKey()
    {
        string? b = SecretProtector.Decrypt(_settings.AccountVaultKeyEnc);
        if (string.IsNullOrEmpty(b)) throw new AccountException(401, "not_logged_in", LocalizationManager.Tr("Not signed in.", "Chưa đăng nhập."));
        return Convert.FromBase64String(b);
    }

    // ===== Tài khoản =====

    /// <summary>Đăng ký tài khoản mới; trả về mã khôi phục (chỉ hiện một lần — người dùng phải cất).</summary>
    public async Task<string> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        var (auth, enc) = await Task.Run(() => Derive(password, email, NewKdfIterations), ct);
        byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
        byte[] recBytes = RandomNumberGenerator.GetBytes(20);

        var root = await CallAsync(HttpMethod.Post, "/register", new
        {
            email = NormEmail(email),
            authKey = Convert.ToBase64String(auth),
            kdfIterations = NewKdfIterations,
            wrappedPw = Convert.ToBase64String(Seal(enc, vaultKey)),
            recoveryAuth = Convert.ToBase64String(Hkdf(recBytes, "wnterm-recovery-auth")),
            wrappedRecovery = Convert.ToBase64String(Seal(Hkdf(recBytes, "wnterm-recovery-enc"), vaultKey)),
            deviceName = DeviceName()
        }, false, ct);

        SaveLogin(email, Str(root, "token"), vaultKey, NewKdfIterations, 0);
        return FormatRecovery(recBytes);
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var pre = await CallAsync(HttpMethod.Post, "/prelogin", new { email = NormEmail(email) }, false, ct);
        int iter = Int(pre, "kdfIterations");
        if (iter < 200000) iter = NewKdfIterations;

        var (auth, enc) = await Task.Run(() => Derive(password, email, iter), ct);
        var root = await CallAsync(HttpMethod.Post, "/login", new
        {
            email = NormEmail(email),
            authKey = Convert.ToBase64String(auth),
            deviceName = DeviceName()
        }, false, ct);

        byte[]? vaultKey = Open(enc, Convert.FromBase64String(Str(root, "wrappedPw")));
        if (vaultKey == null)
            throw new AccountException(0, "bad_key", LocalizationManager.Tr("Cannot unlock account data.", "Không mở được khóa dữ liệu của tài khoản."));

        SaveLogin(email, Str(root, "token"), vaultKey, iter, 0);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { await CallAsync(HttpMethod.Post, "/logout", null, true, ct); } catch { }
        Clear();
    }

    private void Clear()
    {
        _settings.AccountTokenEnc = null;
        _settings.AccountVaultKeyEnc = null;
        _settings.AccountVaultVersion = 0;
        _settingsStore.Save(_settings);
    }

    /// <summary>Đặt lại mật khẩu bằng mã khôi phục (không cần đăng nhập). Sau đó phải đăng nhập lại.</summary>
    public async Task RecoverAsync(string email, string recoveryCode, string newPassword, CancellationToken ct = default)
    {
        byte[]? rec = ParseRecovery(recoveryCode);
        if (rec == null) throw new AccountException(0, "bad_code", LocalizationManager.Tr("The recovery code is not valid.", "Mã khôi phục không hợp lệ."));

        byte[] recAuth = Hkdf(rec, "wnterm-recovery-auth");
        var start = await CallAsync(HttpMethod.Post, "/recover/start", new { email = NormEmail(email), recoveryAuth = Convert.ToBase64String(recAuth) }, false, ct);
        byte[]? vaultKey = Open(Hkdf(rec, "wnterm-recovery-enc"), Convert.FromBase64String(Str(start, "wrappedRecovery")));
        if (vaultKey == null) throw new AccountException(0, "bad_key", LocalizationManager.Tr("Cannot unlock data with this code.", "Không mở được dữ liệu bằng mã này."));

        int iter = Int(start, "kdfIterations");
        if (iter < 200000) iter = NewKdfIterations;
        var (auth, enc) = await Task.Run(() => Derive(newPassword, email, iter), ct);
        await CallAsync(HttpMethod.Post, "/recover/reset", new
        {
            email = NormEmail(email),
            recoveryAuth = Convert.ToBase64String(recAuth),
            authKey = Convert.ToBase64String(auth),
            wrappedPw = Convert.ToBase64String(Seal(enc, vaultKey))
        }, false, ct);
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
    {
        byte[] vaultKey = VaultKey();
        int iter = _settings.AccountKdfIterations >= 200000 ? _settings.AccountKdfIterations : NewKdfIterations;
        var (oldAuth, _) = await Task.Run(() => Derive(oldPassword, Email, iter), ct);
        var (newAuth, newEnc) = await Task.Run(() => Derive(newPassword, Email, iter), ct);
        await CallAsync(HttpMethod.Post, "/password/change", new
        {
            oldAuthKey = Convert.ToBase64String(oldAuth),
            newAuthKey = Convert.ToBase64String(newAuth),
            newWrappedPw = Convert.ToBase64String(Seal(newEnc, vaultKey))
        }, true, ct);
    }

    /// <summary>Xóa vĩnh viễn tài khoản và dữ liệu đồng bộ trên máy chủ (cần mật khẩu).</summary>
    public async Task DeleteAccountAsync(string password, CancellationToken ct = default)
    {
        int iter = _settings.AccountKdfIterations >= 200000 ? _settings.AccountKdfIterations : NewKdfIterations;
        var (auth, _) = await Task.Run(() => Derive(password, Email, iter), ct);
        await CallAsync(HttpMethod.Post, "/account/delete", new { authKey = Convert.ToBase64String(auth) }, true, ct);
        Clear();
    }

    // ===== Đồng bộ danh sách VM =====
    //
    // Quy tắc gộp (mỗi VM theo Id): sự kiện MỚI NHẤT thắng — sửa (UpdatedAt) hoặc xóa (dấu xóa trong thùng rác).
    // Xóa không bao giờ làm mất dữ liệu ngay: VM bị xóa (ở máy này hay máy khác) nằm trong thùng rác 30 ngày;
    // khôi phục từ thùng rác = một lần sửa mới hơn → thắng dấu xóa trên mọi máy. Máy chủ còn giữ lịch sử phiên bản.

    private static string NewDeviceName() => DeviceName();

    private VaultSession ToVault(SessionInfo s)
    {
        var v = new VaultSession
        {
            Id = s.Id, Name = s.Name, Group = s.Group, Tags = new List<string>(s.Tags), Pinned = s.IsPinned,
            Host = s.Host, Port = s.Port, Username = s.Username,
            Password = SecretProtector.Decrypt(s.EncryptedPassword),
            Passphrase = SecretProtector.Decrypt(s.EncryptedPassphrase),
            CreatedAt = s.CreatedAt, UpdatedAt = s.EffectiveUpdatedAt,
        };
        if (!string.IsNullOrWhiteSpace(s.KeyFilePath))
        {
            v.KeyFileName = Path.GetFileName(s.KeyFilePath);
            try { if (File.Exists(s.KeyFilePath)) v.KeyFileContent = Convert.ToBase64String(File.ReadAllBytes(s.KeyFilePath)); } catch { }
        }
        return v;
    }

    private VaultData BuildVault(List<SessionInfo> sessions, IEnumerable<VaultTombstone> tombstones)
    {
        var live = sessions.Select(s => s.Id).ToHashSet();
        return new VaultData
        {
            SavedAt = DateTime.UtcNow,
            Device = NewDeviceName(),
            Sessions = sessions.Select(ToVault).ToList(),
            Deleted = tombstones.Where(t => !live.Contains(t.Id) && (DateTime.UtcNow - t.At).TotalDays <= TrashStore.TombstoneDays)
                                .GroupBy(t => t.Id).Select(g => g.OrderByDescending(t => t.At).First())
                                .OrderBy(t => t.Id).ToList(),
            Snippets = _snippets.Load(),
            DeletedSnippets = _snippets.LoadTombstones(),
        };
    }

    /// <summary>Chép nội dung một VM từ vault vào bản ghi cục bộ. Trả true nếu có khác biệt.</summary>
    private bool Apply(VaultSession v, SessionInfo s)
    {
        string? keyPath = null;
        if (!string.IsNullOrEmpty(v.KeyFileContent) && !string.IsNullOrEmpty(v.KeyFileName))
        {
            try
            {
                keyPath = Path.Combine(_paths.KeysDir, $"{v.Id:N}_{Path.GetFileName(v.KeyFileName)}");
                byte[] bytes = Convert.FromBase64String(v.KeyFileContent);
                if (!File.Exists(keyPath) || !File.ReadAllBytes(keyPath).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(keyPath, bytes);
            }
            catch { keyPath = null; }
        }

        bool changed = s.Name != v.Name || s.Group != v.Group || s.Host != v.Host.Trim() || s.Port != v.Port ||
                       s.Username != v.Username.Trim() || s.IsPinned != v.Pinned || !s.Tags.SequenceEqual(v.Tags) ||
                       SecretProtector.Decrypt(s.EncryptedPassword) != v.Password ||
                       SecretProtector.Decrypt(s.EncryptedPassphrase) != v.Passphrase ||
                       (keyPath != null && s.KeyFilePath != keyPath);

        s.Name = v.Name; s.Group = v.Group; s.Tags = new List<string>(v.Tags); s.IsPinned = v.Pinned;
        s.Host = v.Host.Trim(); s.Port = v.Port; s.Username = v.Username.Trim();
        s.SavePassword = v.Password != null;
        if (changed || s.EncryptedPassword == null)
        {
            s.EncryptedPassword = SecretProtector.Encrypt(v.Password);
            s.EncryptedPassphrase = SecretProtector.Encrypt(v.Passphrase);
        }
        if (keyPath != null) s.KeyFilePath = keyPath;
        if (v.CreatedAt is DateTime c) s.CreatedAt = c;
        s.UpdatedAt = v.UpdatedAt ?? v.CreatedAt ?? s.UpdatedAt;
        return changed;
    }

    private static SessionInfo NewFrom(VaultSession v) => new() { Id = v.Id, CreatedAt = v.CreatedAt ?? DateTime.UtcNow };

    private async Task<(int version, VaultData? data)> PullAsync(CancellationToken ct, int? version = null)
    {
        var root = await CallAsync(HttpMethod.Get, version is int n ? "/vault?version=" + n : "/vault", null, true, ct);
        int ver = Int(root, "version");
        if (ver == 0 || !root.TryGetProperty("blob", out var b) || b.ValueKind != JsonValueKind.String) return (ver, null);

        byte[]? plain = Open(VaultKey(), Convert.FromBase64String(b.GetString()!));
        if (plain == null) throw new AccountException(0, "bad_key", LocalizationManager.Tr("Cannot decrypt synced data (wrong key).", "Không giải mã được dữ liệu đồng bộ (sai khóa)."));
        var data = JsonSerializer.Deserialize<VaultData>(plain) ?? new VaultData();
        data.Sessions ??= new();
        data.Deleted ??= new();
        data.Snippets ??= new();
        data.DeletedSnippets ??= new();
        return (ver, data);
    }

    private async Task<int> PushAsync(VaultData data, int baseVersion, CancellationToken ct)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(data);
        byte[] blob = Seal(VaultKey(), plain);
        var root = await CallAsync(HttpMethod.Put, "/vault", new { baseVersion, blob = Convert.ToBase64String(blob) }, true, ct);
        return Int(root, "version");
    }

    /// <summary>Nội dung có giống nhau không (bỏ qua thời điểm lưu / tên thiết bị) — để khỏi đẩy lên bản trùng.</summary>
    private static bool SameContent(VaultData a, VaultData b)
    {
        static string Key(VaultData d) => JsonSerializer.Serialize(new
        {
            s = d.Sessions.Where(x => x.IsValid).OrderBy(x => x.Id),
            d = d.Deleted.OrderBy(x => x.Id).Select(x => new { x.Id, x.At }),
            n = d.Snippets.Where(x => x.IsValid).OrderBy(x => x.Id, StringComparer.Ordinal),
            nd = d.DeletedSnippets.OrderBy(x => x.Id, StringComparer.Ordinal)
        });
        return Key(a) == Key(b);
    }

    private void MarkSynced(int version)
    {
        _settings.AccountVaultVersion = version;
        _settings.AccountLastSyncUtc = DateTime.UtcNow;
        _settingsStore.Save(_settings);
    }

    /// <summary>Gộp bản trên máy chủ vào máy này theo quy tắc "sự kiện mới nhất thắng"; ghi lại máy này nếu có thay đổi.</summary>
    private (VaultData merged, int added, int updated, int deleted) MergeIntoLocal(VaultData? remote)
    {
        var local = _store.Load(out _);
        var trash = _store.Trash.Load();
        var localById = local.ToDictionary(s => s.Id);
        var localTomb = trash.Where(e => !e.LocalOnly).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Max(e => e.DeletedAt));
        var remoteLive = (remote?.Sessions ?? new()).Where(v => v.IsValid).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var remoteTomb = (remote?.Deleted ?? new()).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.Max(t => t.At));
        var names = (remote?.Deleted ?? new()).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First().Name);
        foreach (var e in trash) names[e.Id] = e.Name;

        // Cùng một VM được tạo riêng ở hai máy (khác Id nhưng trùng tên + host + port + user) → coi là một,
        // lấy Id của máy chủ, để gộp không sinh ra bản trùng.
        bool idsChanged = false;
        static string Key(string name, string host, int port, string user) =>
            $"{name.Trim().ToLowerInvariant()}|{host.Trim().ToLowerInvariant()}|{port}|{user.Trim().ToLowerInvariant()}";
        var remoteOnly = remoteLive.Values.Where(v => !localById.ContainsKey(v.Id) && !localTomb.ContainsKey(v.Id))
            .GroupBy(v => Key(v.Name, v.Host, v.Port, v.Username)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        foreach (var s in local.ToList())
        {
            if (remoteLive.ContainsKey(s.Id) || remoteTomb.ContainsKey(s.Id)) continue;
            if (!remoteOnly.Remove(Key(s.Name, s.Host, s.Port, s.Username), out var twin)) continue;
            localById.Remove(s.Id);
            s.Id = twin.Id;
            localById[s.Id] = s;
            idsChanged = true;
        }

        int added = 0, updated = 0, deleted = 0;
        var toTrash = new List<(SessionInfo s, DateTime at)>();
        var untrash = new List<Guid>();
        var tombs = new List<VaultTombstone>();
        bool localChanged = idsChanged;

        var ids = localById.Keys.Concat(localTomb.Keys).Concat(remoteLive.Keys).Concat(remoteTomb.Keys).Distinct().ToList();
        foreach (var id in ids)
        {
            // Quy tắc chung (xem LwwMerge): sự kiện mới nhất thắng; hòa thì bản máy này > bản máy chủ > dấu xóa.
            localById.TryGetValue(id, out var l);
            remoteLive.TryGetValue(id, out var r);
            var win = LwwMerge.Resolve(l?.EffectiveUpdatedAt, r?.EffectiveUpdatedAt,
                localTomb.TryGetValue(id, out var lt) ? lt : null, remoteTomb.TryGetValue(id, out var rt) ? rt : null);
            var kind = win.kind switch { LwwKind.Local => 'L', LwwKind.Remote => 'R', _ => 'x' };

            switch (kind)
            {
                case 'L':
                    if (localTomb.ContainsKey(id)) untrash.Add(id);
                    break;
                case 'R':
                    if (l == null)
                    {
                        var s = NewFrom(r!);
                        Apply(r!, s);
                        local.Add(s);
                        added++;
                        localChanged = true;
                    }
                    else
                    {
                        var before = l.UpdatedAt;
                        if (Apply(r!, l)) { updated++; localChanged = true; }
                        else if (before != l.UpdatedAt) localChanged = true; // chỉ khác thời điểm: lưu lại cho khớp
                    }
                    if (trash.Any(e => e.Id == id)) untrash.Add(id);
                    break;
                default: // dấu xóa thắng
                    if (l != null)
                    {
                        toTrash.Add((l, win.at));
                        local.Remove(l);
                        deleted++;
                        localChanged = true;
                    }
                    tombs.Add(new VaultTombstone { Id = id, At = win.at, Name = names.GetValueOrDefault(id, l?.DisplayName ?? "") });
                    break;
            }
        }

        if (localChanged)
        {
            foreach (var g in toTrash.GroupBy(x => x.at)) _store.Trash.Add(g.Select(x => x.s), g.Key);
            _store.Trash.Remove(untrash);
            _store.SaveSynced(local);
        }
        else if (untrash.Count > 0) _store.Trash.Remove(untrash);

        // Snippet: cùng quy tắc, gộp bằng SnippetMerge rồi ghi lại nếu đổi.
        var sm = SnippetMerge.Merge(_snippets.Load(), _snippets.LoadTombstones(), remote?.Snippets, remote?.DeletedSnippets, DateTime.UtcNow);
        if (sm.LocalChanged) _snippets.SaveSynced(sm.Snippets, sm.Tombstones);

        var vault = BuildVault(local, tombs);
        vault.Snippets = sm.Snippets;
        vault.DeletedSnippets = sm.Tombstones;
        return (vault, added, updated, deleted);
    }

    /// <summary>Kéo bản trên máy chủ, gộp với máy này (thêm/sửa/xóa — bản mới nhất thắng), rồi đẩy bản gộp lên.</summary>
    /// <param name="forceNewVersion">true khi người dùng bấm "Backup ngay": luôn tạo bản mới trong lịch sử dù dữ liệu không đổi.</param>
    public async Task<SyncResult> SyncAsync(CancellationToken ct = default, bool forceNewVersion = false)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (serverVer, remote) = await PullAsync(ct);
            var (merged, added, updated, deleted) = MergeIntoLocal(remote);
            try
            {
                int ver = serverVer;
                if (forceNewVersion || remote == null || !SameContent(remote, merged)) ver = await PushAsync(merged, serverVer, ct);
                MarkSynced(ver);
                return new SyncResult(added, updated, merged.Sessions.Count, ver, deleted);
            }
            catch (AccountException ex) when (ex.Status == 409) { /* thiết bị khác vừa ghi: kéo lại rồi gộp */ }
        }
        throw new AccountException(409, "conflict", LocalizationManager.Tr("Sync conflict, please try again.", "Xung đột đồng bộ, hãy thử lại."));
    }

    /// <summary>Số VM trên máy chủ và ở máy này (dùng ngay sau khi đăng nhập để hỏi cách đồng bộ lần đầu).</summary>
    public async Task<(int remote, int local)> CountAsync(CancellationToken ct = default)
    {
        var (_, remote) = await PullAsync(ct);
        return (remote?.Sessions.Count(v => v.IsValid) ?? 0, _store.Load(out _).Count);
    }

    /// <summary>
    /// Lấy bản trên máy chủ thay cho máy này: máy này trở nên giống hệt máy chủ. VM chỉ có ở máy này được chuyển vào
    /// thùng rác (chỉ ở máy này — không xóa trên máy khác).
    /// </summary>
    public async Task<SyncResult> ForcePullAsync(CancellationToken ct = default)
    {
        var (ver, remote) = await PullAsync(ct);
        var local = _store.Load(out _);
        var remoteLive = (remote?.Sessions ?? new()).Where(v => v.IsValid).ToList();
        var ids = remoteLive.Select(v => v.Id).ToHashSet();

        var gone = local.Where(s => !ids.Contains(s.Id)).ToList();
        int added = 0, updated = 0;
        var result = new List<SessionInfo>();
        foreach (var v in remoteLive)
        {
            var s = local.FirstOrDefault(x => x.Id == v.Id);
            if (s == null) { s = NewFrom(v); added++; Apply(v, s); }
            else if (Apply(v, s)) updated++;
            result.Add(s);
        }

        if (gone.Count > 0) _store.Trash.Add(gone, DateTime.UtcNow, localOnly: true);
        _store.Trash.Remove(ids);
        _store.SaveSynced(result);
        _snippets.SaveSynced((remote?.Snippets ?? new()).Where(x => x.IsValid), remote?.DeletedSnippets ?? new());
        MarkSynced(ver);
        return new SyncResult(added, updated, result.Count, ver, gone.Count);
    }

    /// <summary>
    /// Ghi đè máy chủ bằng dữ liệu máy này: mọi VM của máy này thành bản mới nhất; VM chỉ có trên máy chủ bị đánh dấu xóa
    /// (các máy khác sẽ chuyển chúng vào thùng rác). Bản cũ trên máy chủ vẫn còn trong lịch sử phiên bản.
    /// </summary>
    public async Task<SyncResult> ForcePushAsync(CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (serverVer, remote) = await PullAsync(ct);
            var local = _store.Load(out _);
            var now = DateTime.UtcNow;
            foreach (var s in local) s.UpdatedAt = now;
            _store.SaveSynced(local);

            var liveIds = local.Select(s => s.Id).ToHashSet();
            var tombs = _store.Trash.Load().Where(e => !e.LocalOnly).Select(e => new VaultTombstone { Id = e.Id, At = e.DeletedAt, Name = e.Name })
                .Concat(remote?.Deleted ?? new())
                .Concat((remote?.Sessions ?? new()).Where(v => !liveIds.Contains(v.Id)).Select(v => new VaultTombstone { Id = v.Id, At = now, Name = v.Name }))
                .ToList();
            int removed = (remote?.Sessions ?? new()).Count(v => !liveIds.Contains(v.Id));
            var snips = _snippets.Load();
            foreach (var n in snips) n.UpdatedAt = now;
            var snipIds = snips.Select(n => n.Id.ToLowerInvariant()).ToHashSet();
            var snipTombs = _snippets.LoadTombstones().Where(t => !snipIds.Contains(t.Id.ToLowerInvariant()))
                .Concat((remote?.DeletedSnippets ?? new()).Where(t => !snipIds.Contains(t.Id.ToLowerInvariant())))
                .Concat((remote?.Snippets ?? new()).Where(n => !snipIds.Contains(n.Id.ToLowerInvariant())).Select(n => new SnippetTombstone { Id = n.Id.ToLowerInvariant(), DeletedAt = now }))
                .GroupBy(t => t.Id.ToLowerInvariant()).Select(g => new SnippetTombstone { Id = g.Key, DeletedAt = g.Max(t => t.DeletedAt) }).ToList();
            _snippets.SaveSynced(snips, snipTombs);
            try
            {
                int ver = await PushAsync(BuildVault(local, tombs), serverVer, ct);
                MarkSynced(ver);
                return new SyncResult(0, local.Count, local.Count, ver, removed);
            }
            catch (AccountException ex) when (ex.Status == 409) { }
        }
        throw new AccountException(409, "conflict", LocalizationManager.Tr("Sync conflict, please try again.", "Xung đột đồng bộ, hãy thử lại."));
    }

    // ===== Lịch sử phiên bản & khôi phục =====

    public async Task<List<VaultVersionInfo>> GetHistoryAsync(CancellationToken ct = default)
    {
        var root = await CallAsync(HttpMethod.Get, "/vault/history", null, true, ct);
        int cur = Int(root, "current");
        var list = new List<VaultVersionInfo>();
        if (root.TryGetProperty("versions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                int v = Int(e, "version");
                DateTime.TryParse(Str(e, "createdAt"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var at);
                long size = e.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long n) ? n : 0;
                list.Add(new VaultVersionInfo(v, at, size, Str(e, "device"), v == cur));
            }
        }
        return list;
    }

    /// <summary>Tải và giải mã một phiên bản cũ.</summary>
    public async Task<VaultData> LoadVersionAsync(int version, CancellationToken ct = default)
    {
        var (_, data) = await PullAsync(ct, version);
        return data ?? new VaultData();
    }

    /// <summary>So một bản (đã giải mã) với dữ liệu máy này.</summary>
    public VaultPreview Preview(VaultData data)
    {
        var local = _store.Load(out _).ToDictionary(s => s.Id);
        var snap = data.Sessions.Where(v => v.IsValid).ToList();
        var missing = snap.Where(v => !local.ContainsKey(v.Id)).ToList();
        int different = snap.Count(v => local.TryGetValue(v.Id, out var s) && ToVaultKey(ToVault(s)) != ToVaultKey(v));
        var snapIds = snap.Select(v => v.Id).ToHashSet();
        return new VaultPreview(snap.Count, missing.Count, different, local.Keys.Count(id => !snapIds.Contains(id)),
            missing.Select(v => string.IsNullOrWhiteSpace(v.Name) ? $"{v.Username}@{v.Host}" : v.Name).ToList());
    }

    private static string ToVaultKey(VaultSession v) => JsonSerializer.Serialize(new { v.Name, v.Group, v.Tags, v.Pinned, v.Host, v.Port, v.Username, v.Password, v.Passphrase, v.KeyFileContent });

    /// <summary>
    /// Khôi phục từ một bản cũ. replaceAll = false: chỉ thêm lại các VM máy này đang thiếu.
    /// replaceAll = true: đưa danh sách về đúng bản đó (VM không có trong bản đó vào thùng rác). Sau đó đồng bộ lên máy chủ.
    /// </summary>
    public async Task<SyncResult> RestoreAsync(VaultData data, bool replaceAll, CancellationToken ct = default)
    {
        var local = _store.Load(out _);
        var now = DateTime.UtcNow;
        var snap = data.Sessions.Where(v => v.IsValid).ToList();
        int added = 0, updated = 0;
        foreach (var v in snap)
        {
            var s = local.FirstOrDefault(x => x.Id == v.Id);
            if (s == null) { s = NewFrom(v); Apply(v, s); local.Add(s); added++; }
            else if (replaceAll) { if (Apply(v, s)) updated++; }
            else continue;
            s.UpdatedAt = now; // mới hơn mọi dấu xóa → các máy khác cũng nhận lại
        }

        var snapIds = snap.Select(v => v.Id).ToHashSet();
        int removed = 0;
        if (replaceAll)
        {
            var gone = local.Where(s => !snapIds.Contains(s.Id)).ToList();
            if (gone.Count > 0) { _store.Trash.Add(gone, now); local.RemoveAll(s => !snapIds.Contains(s.Id)); removed = gone.Count; }
        }
        _store.Trash.Remove(snapIds);
        _store.SaveSynced(local);

        // Snippet: thêm lại các mục đang thiếu (replaceAll: đưa về đúng bản đó), đóng dấu "bây giờ".
        var curSn = _snippets.Load();
        var curTomb = _snippets.LoadTombstones();
        var snapSn = data.Snippets.Where(x => x.IsValid).ToList();
        var snapSnIds = snapSn.Select(x => x.Id.ToLowerInvariant()).ToHashSet();
        foreach (var n in snapSn)
        {
            int i = curSn.FindIndex(x => x.Id.Equals(n.Id, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && !replaceAll) continue;
            n.UpdatedAt = now;
            if (i >= 0) curSn[i] = n; else curSn.Add(n);
            curTomb.RemoveAll(t => t.Id.Equals(n.Id, StringComparison.OrdinalIgnoreCase));
        }
        if (replaceAll)
            foreach (var g in curSn.Where(x => !snapSnIds.Contains(x.Id.ToLowerInvariant())).ToList())
            {
                curSn.Remove(g);
                curTomb.RemoveAll(t => t.Id.Equals(g.Id, StringComparison.OrdinalIgnoreCase));
                curTomb.Add(new SnippetTombstone { Id = g.Id.ToLowerInvariant(), DeletedAt = now });
            }
        _snippets.SaveSynced(curSn, curTomb);

        var r = await SyncAsync(ct);
        return new SyncResult(added, updated, r.Total, r.ServerVersion, removed);
    }
}
