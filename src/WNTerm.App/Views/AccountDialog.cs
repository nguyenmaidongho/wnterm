using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;

namespace WNTerm.App.Views;

/// <summary>
/// Cửa chính của nút Cloud: tài khoản WNTerm (đăng nhập/đăng ký/khôi phục mật khẩu) và đồng bộ VM giữa các thiết bị.
/// Có lịch sử phiên bản trên máy chủ để khôi phục, thùng rác cho VM đã xóa, và kéo/đẩy cưỡng bức khi cần.
/// S3 riêng chỉ là tùy chọn nâng cao (backup thêm vào S3 của người dùng).
/// </summary>
public sealed class AccountDialog : DialogView<bool?>
{
    private enum Mode { Login, Register, Recover, Account, RecoveryCode, FirstSync, History, ChangePassword, DeleteAccount }

    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _sessionStore;
    private readonly AccountService _account;
    private readonly StackPanel _root = new() { Spacing = 10 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private string _emailText = "";
    private string _recoveryCode = "";
    private (int remote, int local) _counts;

    public AccountDialog(AppSettings settings, SettingsStore settingsStore, SessionStore sessionStore, SnippetStore? snippetStore = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;
        _account = new AccountService(settings, settingsStore, sessionStore, null, snippetStore);
        Title = T("WNTerm cloud backup", "Backup cloud WNTerm");
        DialogWidth = 500;
        Content = _root;
        Show(_account.IsLoggedIn ? Mode.Account : Mode.Login);
    }

    public override void OnCancelRequested() => Close(true);

    private static string T(string en, string vi) => LocalizationManager.Tr(en, vi);

    private static TextBox Field(string? text = null, bool password = false) =>
        new() { Text = text ?? "", PasswordChar = password ? '•' : '\0' };

    private static Button Btn(string text, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    /// <summary>Hàng nút tự xuống dòng (vừa màn hình điện thoại).</summary>
    private void Buttons(params Control[] items)
    {
        var w = new WrapPanel();
        foreach (var c in items) w.Children.Add(c);
        _root.Children.Add(w);
    }

    private void SetStatus(string text, bool ok = false)
    {
        _status.Text = text;
        _status.Foreground = ok ? Brushes.LimeGreen : Brushes.OrangeRed;
    }

    private void Label(string text) => _root.Children.Add(new TextBlock { Text = text });

    private void Heading(string text) => _root.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });

    private void Muted(string text) => _root.Children.Add(new TextBlock { Text = text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });

    private void Link(string text, Action onClick)
    {
        var b = new Button { Content = text, Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => onClick();
        _root.Children.Add(b);
    }

    private async void Run(Control trigger, Func<Task> action)
    {
        trigger.IsEnabled = false;
        SetStatus(T("Working...", "Đang xử lý..."), true);
        try { await action(); }
        catch (AccountException ex)
        {
            SetStatus(ex.Message);
            if (ex.Status == 401 && _account.IsLoggedIn && ex.Code != "bad_credentials")
            {
                await _account.LogoutAsync();
                Show(Mode.Login);
                SetStatus(T("Session expired, please sign in again.", "Phiên đăng nhập hết hạn, hãy đăng nhập lại."));
            }
        }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { trigger.IsEnabled = true; }
    }

    private void Show(Mode mode)
    {
        _root.Children.Clear();
        _status.Text = "";
        switch (mode)
        {
            case Mode.Login: BuildLogin(); break;
            case Mode.Register: BuildRegister(); break;
            case Mode.Recover: BuildRecover(); break;
            case Mode.Account: BuildAccount(); break;
            case Mode.RecoveryCode: BuildRecoveryCode(); break;
            case Mode.FirstSync: BuildFirstSync(); break;
            case Mode.History: BuildHistory(); break;
            case Mode.ChangePassword: BuildChangePassword(); break;
            case Mode.DeleteAccount: BuildDeleteAccount(); break;
        }
        _root.Children.Add(_status);
    }

    // ===== Đăng nhập / đăng ký / quên mật khẩu =====

    private void BuildLogin()
    {
        Muted(T("Sign in to sync your VM list between your PC and phone and to keep restorable backups on the WNTerm server (end-to-end encrypted — the server cannot read your data).",
                "Đăng nhập để đồng bộ danh sách VM giữa máy tính và điện thoại, và giữ các bản backup khôi phục được trên máy chủ WNTerm (mã hóa đầu-cuối — máy chủ không đọc được dữ liệu của bạn)."));
        var email = Field(_emailText); var pw = Field(password: true);
        Label("Email:"); _root.Children.Add(email);
        Label(T("Password:", "Mật khẩu:")); _root.Children.Add(pw);
        var ok = Btn(T("Sign in", "Đăng nhập"), true); ok.IsDefault = true;
        ok.Click += (_, _) => Run(ok, async () =>
        {
            _emailText = email.Text ?? "";
            await _account.LoginAsync(_emailText, pw.Text ?? "");
            await AfterLoginAsync();
        });
        Buttons(ok);
        Link(T("Create a new account", "Tạo tài khoản mới"), () => { _emailText = email.Text ?? ""; Show(Mode.Register); });
        Link(T("Forgot password (use recovery code)", "Quên mật khẩu (dùng mã khôi phục)"), () => { _emailText = email.Text ?? ""; Show(Mode.Recover); });
        AddS3Link();
    }

    /// <summary>Lần đầu đăng nhập trên máy này: nếu cả máy chủ lẫn máy này đều có VM thì hỏi cách gộp.</summary>
    private async Task AfterLoginAsync()
    {
        _counts = await _account.CountAsync();
        if (_counts.remote > 0 && _counts.local > 0) { Show(Mode.FirstSync); return; }
        Show(Mode.Account);
        await DoSync();
    }

    private void BuildFirstSync()
    {
        Heading(string.Format(T("Your cloud backup has {0} VMs, and this device already has {1} VMs.",
                                "Bản backup trên cloud có {0} VM, máy này đang có sẵn {1} VM."), _counts.remote, _counts.local));
        Muted(T("What do you want to do? (Nothing is lost: VMs removed from this device go to the recycle bin.)",
                "Bạn muốn làm gì? (Không mất gì: VM bị bỏ khỏi máy này sẽ nằm trong thùng rác.)"));

        var pull = Btn(T("Restore the cloud backup to this device", "Khôi phục bản backup trên cloud về máy này"), true);
        pull.Click += (_, _) => Run(pull, async () => { Show(Mode.Account); await DoForcePull(); });
        var merge = Btn(T("Merge both (keep VMs from both)", "Gộp cả hai (giữ VM của cả hai bên)"));
        merge.Click += (_, _) => Run(merge, async () => { Show(Mode.Account); await DoSync(); });
        var push = Btn(T("Back up this device instead (replace the cloud backup)", "Backup máy này lên, thay bản trên cloud"));
        push.Click += (_, _) => Run(push, async () => { Show(Mode.Account); await DoForcePush(); });

        foreach (var b in new[] { pull, merge, push })
        {
            // Chữ dài: cho xuống dòng để vừa màn hình điện thoại.
            b.Content = new TextBlock { Text = b.Content as string, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Margin = new Thickness(0);
            _root.Children.Add(b);
        }
    }

    private void BuildRegister()
    {
        var email = Field(_emailText); var pw = Field(password: true); var pw2 = Field(password: true);
        Label("Email:"); _root.Children.Add(email);
        Label(T("Password (min 8 characters):", "Mật khẩu (tối thiểu 8 ký tự):")); _root.Children.Add(pw);
        Label(T("Repeat password:", "Nhập lại mật khẩu:")); _root.Children.Add(pw2);
        Muted(T("The password cannot be recovered by the server. You will get a recovery code — keep it safe.",
                "Máy chủ không thể khôi phục mật khẩu giúp bạn. Bạn sẽ nhận một mã khôi phục — hãy cất cẩn thận."));
        var ok = Btn(T("Create account", "Tạo tài khoản"), true); ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(email.Text) || !email.Text.Contains('@')) { SetStatus(T("Enter a valid email.", "Nhập email hợp lệ.")); return; }
            if ((pw.Text ?? "").Length < 8) { SetStatus(T("Password is too short.", "Mật khẩu quá ngắn.")); return; }
            if (pw.Text != pw2.Text) { SetStatus(T("Passwords do not match.", "Hai mật khẩu không khớp.")); return; }
            Run(ok, async () =>
            {
                _emailText = email.Text!;
                _recoveryCode = await _account.RegisterAsync(_emailText, pw.Text!);
                Show(Mode.RecoveryCode);
            });
        };
        var back = Btn(T("Back", "Quay lại")); back.Click += (_, _) => { _emailText = email.Text ?? ""; Show(Mode.Login); };
        Buttons(ok, back);
    }

    private void BuildRecoveryCode()
    {
        Heading(T("Account created. Save your recovery code:", "Đã tạo tài khoản. Hãy lưu mã khôi phục:"));
        _root.Children.Add(new TextBox { Text = _recoveryCode, IsReadOnly = true, FontFamily = new FontFamily("Consolas, monospace"), FontSize = 16, TextWrapping = TextWrapping.Wrap });
        Muted(T("It is shown only once. Without it and your password, your encrypted data cannot be recovered.",
                "Mã chỉ hiện một lần. Mất cả mã lẫn mật khẩu thì không thể lấy lại dữ liệu đã mã hóa."));
        var copy = Btn(T("Copy", "Sao chép"));
        copy.Click += async (_, _) => { if (await Ui.SetClipboardTextAsync(_recoveryCode)) SetStatus(T("Copied.", "Đã sao chép."), true); };
        var ok = Btn(T("I have saved it", "Tôi đã lưu"), true);
        ok.Click += (_, _) => Run(ok, async () => { _recoveryCode = ""; Show(Mode.Account); await DoSync(); });
        Buttons(ok, copy);
    }

    private void BuildRecover()
    {
        var email = Field(_emailText); var code = Field(); var pw = Field(password: true);
        Label("Email:"); _root.Children.Add(email);
        Label(T("Recovery code:", "Mã khôi phục:")); _root.Children.Add(code);
        Label(T("New password:", "Mật khẩu mới:")); _root.Children.Add(pw);
        var ok = Btn(T("Reset password", "Đặt lại mật khẩu"), true); ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            if ((pw.Text ?? "").Length < 8) { SetStatus(T("Password is too short.", "Mật khẩu quá ngắn.")); return; }
            Run(ok, async () =>
            {
                _emailText = email.Text ?? "";
                await _account.RecoverAsync(_emailText, code.Text ?? "", pw.Text!);
                Show(Mode.Login);
                SetStatus(T("Password reset. Please sign in — your synced data is intact.", "Đã đặt lại mật khẩu. Hãy đăng nhập — dữ liệu đồng bộ vẫn còn nguyên."), true);
            });
        };
        var back = Btn(T("Back", "Quay lại")); back.Click += (_, _) => { _emailText = email.Text ?? ""; Show(Mode.Login); };
        Buttons(ok, back);
    }

    // ===== Đã đăng nhập =====

    private TextBlock? _syncInfo;

    private void BuildAccount()
    {
        _root.Children.Add(new TextBlock { Text = _account.Email, FontWeight = FontWeight.SemiBold, FontSize = 15 });
        _syncInfo = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        UpdateSyncInfo();
        _root.Children.Add(_syncInfo);

        var backup = Btn(T("☁  Back up now", "☁  Backup ngay"), true);
        backup.Click += (_, _) => Run(backup, DoSync);
        var restore = Btn(T("Restore from a backup...", "Khôi phục từ backup..."));
        restore.Click += (_, _) => Show(Mode.History);
        int trash = _sessionStore.Trash.Count;
        var trashBtn = Btn(trash > 0 ? string.Format(T("Recycle bin ({0})", "Thùng rác ({0})"), trash) : T("Recycle bin", "Thùng rác"));
        trashBtn.Click += async (_, _) => { await Dialogs.ShowAsync(new TrashDialog(_sessionStore)); Show(Mode.Account); };
        Buttons(backup, restore, trashBtn);

        var auto = new CheckBox
        {
            Content = T("Back up automatically (on start, after every change, every 10 minutes)", "Tự động backup (khi mở app, sau mỗi thay đổi, mỗi 10 phút)"),
            IsChecked = _settings.AccountAutoSync
        };
        auto.IsCheckedChanged += (_, _) => { _settings.AccountAutoSync = auto.IsChecked == true; _settingsStore.Save(_settings); };
        _root.Children.Add(auto);

        Muted(T("Back up on one device, sign in on another and it gets the same VM list. Each backup is kept 30 days so you can restore an older one; deleted VMs stay in the recycle bin for 30 days.",
                "Backup ở máy này, đăng nhập ở máy khác là có cùng danh sách VM. Mỗi bản backup được giữ 30 ngày để khôi phục bản cũ; VM đã xóa nằm trong thùng rác 30 ngày."));

        // Thao tác ít dùng: link nhỏ, xếp hàng ngang (tự xuống dòng trên điện thoại).
        var chg = new Button { Content = T("Change password...", "Đổi mật khẩu..."), Classes = { "toolbar" }, Margin = new Thickness(0, 0, 6, 4) };
        chg.Click += (_, _) => Show(Mode.ChangePassword);
        var del = new Button { Content = T("Delete account...", "Xóa tài khoản..."), Classes = { "toolbar" }, Margin = new Thickness(0, 0, 6, 4) };
        del.Click += (_, _) => Show(Mode.DeleteAccount);
        var s3 = new Button { Content = T("Extra backup to my own S3...", "Backup thêm vào S3 riêng..."), Classes = { "toolbar" }, Margin = new Thickness(0, 0, 6, 4) };
        s3.Click += async (_, _) => { await Dialogs.ShowAsync(new CloudBackupDialog(_settings, _settingsStore, _sessionStore)); Show(Mode.Account); };
        _root.Children.Add(new WrapPanel { Children = { chg, s3, del } });

        var logout = Btn(T("Sign out", "Đăng xuất"));
        logout.Click += (_, _) => Run(logout, async () => { await _account.LogoutAsync(); Show(Mode.Login); });
        var close = Btn(T("Close", "Đóng"));
        close.Click += (_, _) => Close(true);
        Buttons(close, logout);
    }

    // ===== Lịch sử phiên bản =====

    private void BuildHistory()
    {
        Heading(T("Choose a backup to restore", "Chọn bản backup để khôi phục"));
        Muted(T("Backups from all your devices, newest first (kept 30 days). Tap one to see what it contains.",
                "Các bản backup từ mọi máy của bạn, mới nhất ở trên (giữ 30 ngày). Chạm vào một bản để xem nội dung."));

        var list = new ListBox { MaxHeight = 260, MinHeight = 100 };
        list.ItemTemplate = new FuncDataTemplate<VaultVersionInfo>((v, _) => v == null ? new TextBlock() : new TextBlock
        {
            Text = v.CreatedAtText + (string.IsNullOrEmpty(v.Device) ? "" : T("  ·  from ", "  ·  từ ") + v.Device) +
                   (v.IsCurrent ? T("  (latest)", "  (mới nhất)") : ""),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 3)
        });
        _root.Children.Add(list);

        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _root.Children.Add(detail);

        VaultData? loaded = null;
        var restore = Btn(T("Restore this backup", "Khôi phục bản này"), true);
        var addMissing = Btn(T("Only add missing VMs", "Chỉ thêm VM còn thiếu"));
        var back = Btn(T("Back", "Quay lại"));
        restore.IsEnabled = addMissing.IsEnabled = false;
        back.Click += (_, _) => Show(Mode.Account);
        Buttons(restore, addMissing, back);

        async Task LoadListAsync()
        {
            var versions = await _account.GetHistoryAsync();
            list.ItemsSource = versions;
            if (versions.Count > 0) list.SelectedIndex = 0; // chọn sẵn bản mới nhất
            else SetStatus(T("No backup yet — press \"Back up now\" on the device that has your VMs.", "Chưa có bản backup nào — bấm \"Backup ngay\" ở máy đang có VM."), true);
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not VaultVersionInfo v) return;
            restore.IsEnabled = addMissing.IsEnabled = false;
            Run(list, async () =>
            {
                loaded = await _account.LoadVersionAsync(v.Version);
                var p = _account.Preview(loaded);
                string names = p.MissingNames.Count == 0 ? "" : "\n   " + string.Join(", ", p.MissingNames.Take(8)) + (p.MissingNames.Count > 8 ? ", ..." : "");
                detail.Text = string.Format(T("Backup of {0}{1}: {2} VMs.\n• Missing on this device: {3}{4}\n• Different from this device: {5}\n• Only on this device (not in that backup): {6}",
                                              "Bản backup lúc {0}{1}: {2} VM.\n• Máy này đang thiếu: {3}{4}\n• Khác nội dung với máy này: {5}\n• Chỉ có ở máy này (không có trong bản đó): {6}"),
                                              v.CreatedAtText, string.IsNullOrEmpty(v.Device) ? "" : T(" from ", " từ ") + v.Device,
                                              p.Total, p.MissingHere, names, p.Different, p.OnlyHere);
                restore.IsEnabled = true;
                addMissing.IsEnabled = p.MissingHere > 0;
                SetStatus("", true);
            });
        };

        restore.Click += async (_, _) =>
        {
            if (loaded == null) return;
            var p = _account.Preview(loaded);
            string msg = p.OnlyHere > 0
                ? string.Format(T("This device will get exactly the VMs of this backup ({0} VMs). {1} VMs that are not in it go to the recycle bin (restorable for 30 days). Continue?",
                                  "Máy này sẽ có đúng các VM của bản backup này ({0} VM). {1} VM không có trong bản đó sẽ vào thùng rác (khôi phục được trong 30 ngày). Tiếp tục?"), p.Total, p.OnlyHere)
                : string.Format(T("This device will get exactly the VMs of this backup ({0} VMs). Continue?", "Máy này sẽ có đúng các VM của bản backup này ({0} VM). Tiếp tục?"), p.Total);
            if (!await Dialogs.ConfirmAsync(T("Restore backup", "Khôi phục backup"), msg, DialogIcon.Question)) return;
            Run(restore, async () =>
            {
                var r = await _account.RestoreAsync(loaded, replaceAll: true);
                UpdateSyncInfo();
                SetStatus(string.Format(T("Restored: {0} VMs on this device ({1} added, {2} updated, {3} moved to the recycle bin).", "Đã khôi phục: máy này có {0} VM (thêm {1}, cập nhật {2}, {3} vào thùng rác)."),
                    r.Total, r.Added, r.Updated, r.Deleted), true);
            });
        };

        addMissing.Click += (_, _) => Run(addMissing, async () =>
        {
            if (loaded == null) return;
            var r = await _account.RestoreAsync(loaded, replaceAll: false);
            UpdateSyncInfo();
            SetStatus(string.Format(T("Added back {0} VMs.", "Đã thêm lại {0} VM."), r.Added), true);
            addMissing.IsEnabled = false;
        });

        Run(list, LoadListAsync);
    }

    // ===== Đổi mật khẩu / xóa tài khoản =====

    private void BuildChangePassword()
    {
        var old = Field(password: true); var nw = Field(password: true);
        Label(T("Current password:", "Mật khẩu hiện tại:")); _root.Children.Add(old);
        Label(T("New password (min 8 characters):", "Mật khẩu mới (tối thiểu 8 ký tự):")); _root.Children.Add(nw);
        var ok = Btn(T("Change", "Đổi"), true);
        ok.Click += (_, _) =>
        {
            if ((nw.Text ?? "").Length < 8) { SetStatus(T("Password is too short.", "Mật khẩu quá ngắn.")); return; }
            Run(ok, async () =>
            {
                await _account.ChangePasswordAsync(old.Text ?? "", nw.Text!);
                Show(Mode.Account);
                SetStatus(T("Password changed. Other devices were signed out.", "Đã đổi mật khẩu. Các thiết bị khác đã bị đăng xuất."), true);
            });
        };
        var back = Btn(T("Back", "Quay lại")); back.Click += (_, _) => Show(Mode.Account);
        Buttons(ok, back);
    }

    private void BuildDeleteAccount()
    {
        Heading(T("Delete account", "Xóa tài khoản"));
        Muted(T("This permanently deletes your account and all synced data/versions on the server. VMs on this device are kept.",
                "Thao tác này xóa vĩnh viễn tài khoản và mọi dữ liệu/phiên bản đồng bộ trên máy chủ. VM trên máy này vẫn giữ nguyên."));
        var pw = Field(password: true);
        Label(T("Password:", "Mật khẩu:")); _root.Children.Add(pw);
        var ok = Btn(T("Delete forever", "Xóa vĩnh viễn"));
        ok.Click += async (_, _) =>
        {
            if (!await Dialogs.ConfirmAsync(T("Delete account", "Xóa tài khoản"), T("Delete the account permanently?", "Xóa tài khoản vĩnh viễn?"), DialogIcon.Warning)) return;
            Run(ok, async () =>
            {
                await _account.DeleteAccountAsync(pw.Text ?? "");
                Show(Mode.Login);
                SetStatus(T("Account deleted.", "Đã xóa tài khoản."), true);
            });
        };
        var back = Btn(T("Back", "Quay lại"), true); back.Click += (_, _) => Show(Mode.Account);
        Buttons(back, ok);
    }

    // ===== Thao tác đồng bộ =====

    private void UpdateSyncInfo()
    {
        if (_syncInfo == null) return;
        _syncInfo.Text = _settings.AccountLastSyncUtc is DateTime t
            ? string.Format(T("Last backup: {0}", "Backup gần nhất: {0}"), t.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
            : T("Not backed up yet.", "Chưa backup lần nào.");
    }

    private async Task DoSync()
    {
        SetStatus(T("Backing up...", "Đang backup..."), true);
        var r = await _account.SyncAsync();
        UpdateSyncInfo();
        SetStatus(string.Format(T("Backed up {0} VMs. Also received from your other devices: {1} new, {2} updated, {3} moved to recycle bin.", "Đã backup {0} VM. Nhận thêm từ máy khác: {1} mới, {2} cập nhật, {3} vào thùng rác."),
            r.Total, r.Added, r.Updated, r.Deleted), true);
    }

    private async Task DoForcePull()
    {
        var r = await _account.ForcePullAsync();
        UpdateSyncInfo();
        SetStatus(string.Format(T("Restored the cloud backup: {0} VMs on this device ({1} moved to recycle bin).", "Đã khôi phục bản backup trên cloud: máy này có {0} VM ({1} vào thùng rác)."), r.Total, r.Deleted), true);
    }

    private async Task DoForcePush()
    {
        var r = await _account.ForcePushAsync();
        UpdateSyncInfo();
        SetStatus(string.Format(T("Backed up this device as the new cloud backup: {0} VMs.", "Đã backup máy này làm bản backup mới trên cloud: {0} VM."), r.Total, r.Deleted), true);
    }

    private void AddS3Link()
    {
        var b = new Button
        {
            Content = T("Extra backup to my own S3 storage...", "Backup thêm vào S3 riêng của tôi..."),
            Classes = { "toolbar" }, HorizontalAlignment = HorizontalAlignment.Left
        };
        b.Click += async (_, _) =>
        {
            await Dialogs.ShowAsync(new CloudBackupDialog(_settings, _settingsStore, _sessionStore));
            Show(_account.IsLoggedIn ? Mode.Account : Mode.Login);
        };
        _root.Children.Add(b);
    }
}
