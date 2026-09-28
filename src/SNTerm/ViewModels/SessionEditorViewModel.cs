using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNTerm.Models;
using SNTerm.Services;

namespace SNTerm.ViewModels;

public partial class SessionEditorViewModel : ObservableObject
{
    private readonly SessionInfo? _editingSession;

    [ObservableProperty]
    private string dialogTitle = "Thêm VM";

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
    private string? hostError;

    [ObservableProperty]
    private string? portError;

    [ObservableProperty]
    private string? usernameError;

    [ObservableProperty]
    private string? keyFileWarning;

    public bool CanTestConnection => false; // Bật ở giai đoạn 2

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
            DialogTitle = "Sửa VM";
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
            HostError = "IP hoặc Hostname không được để trống";
            valid = false;
        }
        else
        {
            HostError = null;
        }

        if (Port < 1 || Port > 65535)
        {
            PortError = "Port phải từ 1 đến 65535";
            valid = false;
        }
        else
        {
            PortError = null;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            UsernameError = "User không được để trống";
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

        session.Name = Name.Trim();
        session.Group = Group.Trim();
        session.Host = Host.Trim();
        session.Port = Port;
        session.Username = Username.Trim();
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
