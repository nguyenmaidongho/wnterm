using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.ViewModels;

public partial class SessionEditorViewModel : ObservableObject
{
    private readonly SessionInfo? _editingSession;

    [ObservableProperty]
    private string dialogTitle = LocalizationManager.Get("Str_AddVmTitle");

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string group = "";

    [ObservableProperty]
    private string host = "";

    [ObservableProperty]
    private int port = 22;

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private bool savePassword = true;

    [ObservableProperty]
    private string keyFilePath = "";

    [ObservableProperty]
    private string passphrase = "";

    [ObservableProperty]
    private bool hasStoredPassword;

    [ObservableProperty]
    private bool isPasswordCleared;

    [ObservableProperty]
    private bool isTestingConnection;

    [ObservableProperty]
    private string? hostError;

    [ObservableProperty]
    private string? portError;

    [ObservableProperty]
    private string? usernameError;

    [ObservableProperty]
    private string? keyFileWarning;

    public bool CanTestConnection => !IsTestingConnection;

    public ObservableCollection<string> ExistingGroups { get; } = new();

    public event Action<bool>? RequestClose;

    public SessionInfo? ResultSession { get; private set; }
    public bool ConnectImmediately { get; private set; }

    public SessionEditorViewModel(SessionInfo? sessionToEdit = null, IEnumerable<string>? existingGroups = null)
    {
        _editingSession = sessionToEdit;

        if (existingGroups != null)
        {
            foreach (var g in existingGroups)
            {
                if (!string.IsNullOrWhiteSpace(g) && !ExistingGroups.Contains(g))
                    ExistingGroups.Add(g);
            }
        }

        if (_editingSession != null)
        {
            DialogTitle = LocalizationManager.Get("Str_EditVmTitle");
            Name = _editingSession.Name;
            Group = _editingSession.Group;
            Host = _editingSession.Host;
            Port = _editingSession.Port;
            Username = _editingSession.Username;
            SavePassword = _editingSession.SavePassword;
            KeyFilePath = _editingSession.KeyFilePath ?? "";
            HasStoredPassword = !string.IsNullOrEmpty(_editingSession.EncryptedPassword);
        }
    }

    partial void OnHostChanged(string value) => Validate();
    partial void OnPortChanged(int value) => Validate();
    partial void OnUsernameChanged(string value) => Validate();
    partial void OnKeyFilePathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !File.Exists(value))
        {
            KeyFileWarning = "Không tìm thấy file key";
        }
        else
        {
            KeyFileWarning = null;
        }
    }

    public bool Validate()
    {
        bool valid = true;

        if (string.IsNullOrWhiteSpace(Host))
        {
            HostError = LocalizationManager.Get("Str_HostError");
            valid = false;
        }
        else
        {
            HostError = null;
        }

        if (Port < 1 || Port > 65535)
        {
            PortError = LocalizationManager.Get("Str_PortError");
            valid = false;
        }
        else
        {
            PortError = null;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            UsernameError = LocalizationManager.Get("Str_UserError");
            valid = false;
        }
        else
        {
            UsernameError = null;
        }

        return valid;
    }

    [RelayCommand]
    private void ClearStoredPasswordAction()
    {
        IsPasswordCleared = true;
        HasStoredPassword = false;
        Password = "";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!Validate()) return;

        IsTestingConnection = true;
        OnPropertyChanged(nameof(CanTestConnection));

        try
        {
            var tempSession = new SessionInfo
            {
                Host = Host.Trim(),
                Port = Port,
                Username = Username.Trim(),
                KeyFilePath = string.IsNullOrWhiteSpace(KeyFilePath) ? null : KeyFilePath.Trim()
            };

            string? testPassword = Password;
            if (string.IsNullOrEmpty(testPassword) && !IsPasswordCleared && _editingSession != null)
            {
                testPassword = SecretProtector.Decrypt(_editingSession.EncryptedPassword);
            }

            string? testPassphrase = Passphrase;
            if (string.IsNullOrEmpty(testPassphrase) && _editingSession != null)
            {
                testPassphrase = SecretProtector.Decrypt(_editingSession.EncryptedPassphrase);
            }

            var factory = new SshConnectionFactory(new KnownHostsStore());
            var connInfo = factory.CreateConnectionInfo(tempSession, testPassword, testPassphrase);

            using var client = new SshClient(connInfo);
            factory.AttachHostKeyVerification(client, tempSession.Host, tempSession.Port);

            await Task.Run(() => client.Connect());
            client.Disconnect();

            MessageBox.Show("Kết nối SSH thành công!", "Kiểm tra kết nối", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            string msg = ErrorTranslator.Translate(ex, Host, Port);
            MessageBox.Show(msg, "Kiểm tra kết nối thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsTestingConnection = false;
            OnPropertyChanged(nameof(CanTestConnection));
        }
    }

    [RelayCommand]
    private void SaveAndClose(bool andConnect)
    {
        if (!Validate()) return;

        var session = _editingSession != null ? _editingSession.Clone() : new SessionInfo();
        if (_editingSession != null)
        {
            session.Id = _editingSession.Id;
            session.CreatedAt = _editingSession.CreatedAt;
            session.LastConnectedAt = _editingSession.LastConnectedAt;
        }

        session.Name = (Name ?? string.Empty).Trim();
        session.Group = (Group ?? string.Empty).Trim();
        session.Host = (Host ?? string.Empty).Trim();
        session.Port = Port;
        session.Username = (Username ?? string.Empty).Trim();
        session.SavePassword = SavePassword;
        session.KeyFilePath = string.IsNullOrWhiteSpace(KeyFilePath) ? null : KeyFilePath.Trim();

        if (SavePassword)
        {
            if (!string.IsNullOrEmpty(Password))
            {
                session.EncryptedPassword = SecretProtector.Encrypt(Password);
            }
            else if (IsPasswordCleared)
            {
                session.EncryptedPassword = null;
            }
            else if (_editingSession != null)
            {
                session.EncryptedPassword = _editingSession.EncryptedPassword;
            }

            if (!string.IsNullOrEmpty(Passphrase))
            {
                session.EncryptedPassphrase = SecretProtector.Encrypt(Passphrase);
            }
            else if (_editingSession != null)
            {
                session.EncryptedPassphrase = _editingSession.EncryptedPassphrase;
            }
        }
        else
        {
            session.EncryptedPassword = null;
            session.EncryptedPassphrase = null;
        }

        ResultSession = session;
        ConnectImmediately = andConnect;
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}
