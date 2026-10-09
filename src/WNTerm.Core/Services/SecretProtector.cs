using System.Security.Cryptography;
using System.Text;

namespace WNTerm.Services;

/// <summary>Bảo vệ bí mật (mật khẩu, passphrase) theo từng nền tảng.</summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string? Unprotect(string cipherBase64);
}

/// <summary>Windows: DPAPI theo user hiện tại.</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WNTerm.DPAPI.Entropy.v1");

    public string Protect(string plainText)
    {
        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public string? Unprotect(string cipherBase64)
    {
        byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(cipherBase64), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>
/// Dự phòng cho nền tảng chưa có kho khóa riêng: AES-GCM với khóa ngẫu nhiên lưu cạnh dữ liệu
/// (quyền 600 trên Unix). Android/iOS nên thay bằng Keystore/Keychain qua <see cref="SecretProtector.Provider"/>.
/// </summary>
public sealed class FileKeySecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public FileKeySecretProtector(string keyFile)
    {
        if (File.Exists(keyFile))
        {
            _key = File.ReadAllBytes(keyFile);
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
            File.WriteAllBytes(keyFile, _key);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public string Protect(string plainText)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plainText);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(string cipherBase64)
    {
        byte[] all = Convert.FromBase64String(cipherBase64);
        if (all.Length < 28) return null;
        byte[] plain = new byte[all.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public static class SecretProtector
{
    private static ISecretProtector? _provider;

    /// <summary>Có thể gán từ head của từng nền tảng (Keystore, Keychain, libsecret...).</summary>
    public static ISecretProtector Provider
    {
        get => _provider ??= OperatingSystem.IsWindows()
            ? new DpapiSecretProtector()
            : new FileKeySecretProtector(Path.Combine(AppPaths.Default.DataDir, "secret.key"));
        set => _provider = value;
    }

    public static string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;
        return Provider.Protect(plainText);
    }

    public static string? Decrypt(string? cipherBase64)
    {
        if (string.IsNullOrEmpty(cipherBase64))
            return null;
        try { return Provider.Unprotect(cipherBase64); }
        catch { return null; }
    }
}
