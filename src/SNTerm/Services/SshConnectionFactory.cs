using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Renci.SshNet;
using Renci.SshNet.Common;
using SNTerm.Models;
using SNTerm.Views;

namespace SNTerm.Services;

public class SshConnectionFactory
{
    private readonly KnownHostsStore _knownHostsStore;
    private static readonly object DialogQueueLock = new();

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

        var connectionInfo = new ConnectionInfo(
            session.Host.Trim(),
            session.Port,
            session.Username.Trim(),
            authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(15)
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

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dlg = new HostKeyDialog(host, port, e.HostKeyName, fingerprint, isChanged)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        dlg.ShowDialog();
                        decision = dlg.Decision;
                    });
                }

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
