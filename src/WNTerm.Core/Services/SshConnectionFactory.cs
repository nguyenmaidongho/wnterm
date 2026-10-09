using System;
using System.Collections.Generic;
using System.IO;
using Renci.SshNet;
using Renci.SshNet.Common;
using WNTerm.Models;

namespace WNTerm.Services;

public enum HostKeyDecision
{
    Cancel,
    TrustOnce,
    TrustAndSave
}

public sealed record HostKeyPromptInfo(string Host, int Port, string KeyName, string Fingerprint, bool IsChanged);

/// <summary>Không có cách xác thực nào dùng được (không mật khẩu, key không có/không đọc được).</summary>
public sealed class SshNoCredentialsException : Exception
{
    public SshNoCredentialsException(string message) : base(message) { }
}

public class SshConnectionFactory
{
    private readonly KnownHostsStore _knownHostsStore;
    private static readonly object DialogQueueLock = new();

    /// <summary>UI cung cấp hộp thoại xác nhận host key. Chưa gán thì từ chối kết nối tới host lạ.</summary>
    public static Func<HostKeyPromptInfo, HostKeyDecision>? HostKeyPrompt { get; set; }

    public SshConnectionFactory(KnownHostsStore? knownHostsStore = null)
    {
        _knownHostsStore = knownHostsStore ?? new KnownHostsStore();
    }

    public ConnectionInfo CreateConnectionInfo(
        SessionInfo session,
        string? password = null,
        string? passphrase = null,
        Action<string>? onStatusUpdate = null)
    {
        var authMethods = new List<AuthenticationMethod>();

        // 1. Private Key authentication
        if (!string.IsNullOrWhiteSpace(session.KeyFilePath) && File.Exists(session.KeyFilePath))
        {
            string? effectivePassphrase = passphrase;
            if (string.IsNullOrEmpty(effectivePassphrase) && !string.IsNullOrEmpty(session.EncryptedPassphrase))
            {
                effectivePassphrase = SecretProtector.Decrypt(session.EncryptedPassphrase);
            }

            PrivateKeyFile keyFile = string.IsNullOrEmpty(effectivePassphrase)
                ? new PrivateKeyFile(session.KeyFilePath)
                : new PrivateKeyFile(session.KeyFilePath, effectivePassphrase);

            authMethods.Add(new PrivateKeyAuthenticationMethod(session.Username, keyFile));
        }

        // 2. Password & Keyboard-interactive authentication
        string? effectivePassword = password;
        if (string.IsNullOrEmpty(effectivePassword) && !string.IsNullOrEmpty(session.EncryptedPassword))
        {
            effectivePassword = SecretProtector.Decrypt(session.EncryptedPassword);
        }

        if (!string.IsNullOrEmpty(effectivePassword))
        {
            authMethods.Add(new PasswordAuthenticationMethod(session.Username, effectivePassword));

            var kbi = new KeyboardInteractiveAuthenticationMethod(session.Username);
            kbi.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = effectivePassword;
                }
            };
            authMethods.Add(kbi);
        }

        // Không bao giờ tạo ConnectionInfo rỗng: SSH.NET sẽ báo lỗi khó hiểu. Báo rõ cho người dùng.
        if (authMethods.Count == 0)
        {
            bool keyMissing = !string.IsNullOrWhiteSpace(session.KeyFilePath) && !File.Exists(session.KeyFilePath);
            string msg = keyMissing
                ? string.Format(LocalizationManager.Tr(
                    "No usable credentials: key file not found ({0}) and no password.",
                    "Không có thông tin đăng nhập dùng được: không tìm thấy file key ({0}) và không có mật khẩu."), session.KeyFilePath)
                : LocalizationManager.Tr(
                    "No usable credentials: no password or SSH key (the saved password could not be decrypted on this device).",
                    "Không có thông tin đăng nhập dùng được: không có mật khẩu hoặc SSH key (mật khẩu đã lưu không giải mã được trên máy này).");
            throw new SshNoCredentialsException(msg);
        }

        var connectionInfo = new ConnectionInfo(
            session.Host.Trim(),
            session.Port,
            session.Username.Trim(),
            authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(5),
            RetryAttempts = 1
        };

        return connectionInfo;
    }

    public void AttachHostKeyVerification(BaseClient client, string host, int port)
    {
        client.HostKeyReceived += (sender, e) =>
        {
            string fingerprint = e.FingerPrintSHA256 ?? Convert.ToBase64String(e.FingerPrint);
            var status = _knownHostsStore.CheckHost(host, port, fingerprint);

            if (status == HostKeyStatus.Trusted)
            {
                e.CanTrust = true;
                return;
            }

            // Hàng đợi hộp thoại xếp hàng hiện lần lượt
            lock (DialogQueueLock)
            {
                // Kiểm tra lại sau khi vào lock
                status = _knownHostsStore.CheckHost(host, port, fingerprint);
                if (status == HostKeyStatus.Trusted)
                {
                    e.CanTrust = true;
                    return;
                }

                bool isChanged = (status == HostKeyStatus.Changed);
                HostKeyDecision decision = HostKeyDecision.Cancel;

                if (HostKeyPrompt != null)
                    decision = HostKeyPrompt(new HostKeyPromptInfo(host, port, e.HostKeyName, fingerprint, isChanged));

                switch (decision)
                {
                    case HostKeyDecision.TrustAndSave:
                        _knownHostsStore.AddOrUpdate(host, port, e.HostKeyName, fingerprint);
                        e.CanTrust = true;
                        break;
                    case HostKeyDecision.TrustOnce:
                        e.CanTrust = true;
                        break;
                    default:
                        e.CanTrust = false;
                        break;
                }
            }
        };
    }
}
