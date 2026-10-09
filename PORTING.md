# Port WPF → Avalonia (WNTerm.App)

Mục tiêu: bản Avalonia phải **giống hệt bản WPF về giao diện lẫn tính năng** (WPF nằm ở `src/WNTerm`, là nguồn tham chiếu để đọc, KHÔNG sửa).
Bản mới nằm ở `src/WNTerm.App` (thư viện UI dùng chung desktop + mobile) và dùng `src/WNTerm.Core` (logic, không UI).

## Quy ước

- Namespace: ViewModel giữ `WNTerm.ViewModels` (cùng tên lớp như WPF); View/Dialog: `WNTerm.App.Views`. Models/Services dùng lại từ Core.
- **Cấm** `System.Windows.*`, `Microsoft.Win32`, `MessageBox`, font `Segoe MDL2 Assets`/`Segoe UI Symbol` (không có trên Linux/Android). Icon dùng geometry trong `Themes/Icons.axaml`
  (`<Path Classes="ico" Data="{StaticResource IconCopy}"/>` nét viền, `Classes="fill"` nét đầy; thêm icon mới vào Icons.axaml nếu thiếu). Emoji cũng nên tránh, thay bằng Path.
- Màu: chỉ dùng `{DynamicResource ThemeXxx}` (cùng tên key như WPF, xem `Themes/Theme.axaml`); không hard-code màu trừ khi bản WPF cũng hard-code.
- Style: dùng sẵn `Themes/Styles.axaml` (Button mặc định, `Classes="primary"`, `toolbar`, `icon`, `ToggleButton.chip`, `TextBox`, `ComboBox`, `ListBox`...). Nếu control chưa có style hợp, thêm vào file style của riêng view (đừng sửa Styles.axaml chung — báo lại trong báo cáo).
- Chuỗi giao diện: key đã có thì `{DynamicResource Str_Xxx}` (xem `WNTerm.Core/Services/LocalizationManager.cs`). Chuỗi mới: dùng `LocalizationManager.Tr("English", "Tiếng Việt")` ở code-behind/VM (**KHÔNG sửa LocalizationManager.cs**).
- Binding: compiled bindings đang tắt (reflection binding bình thường). Dùng CommunityToolkit.Mvvm như bản WPF.
- Dispatcher/clipboard/chọn file: `WNTerm.App.Services.Ui` (`Ui.Run`, `Ui.RunAsync`, `Ui.Post`, `Ui.SetClipboardTextAsync`, `Ui.PickOpenFileAsync`, `Ui.PickSaveFileAsync`, `Ui.PickFolderAsync`, `Ui.OpenWithShell`).
- Hộp thoại **không dùng Window**: dùng overlay. Mỗi dialog là `class XxxDialog : DialogView<TResult>` (xem `Services/Dialogs.cs`); đặt `Title`, `DialogWidth`; đóng bằng `Close(result)`; hiện bằng `var r = await Dialogs.ShowAsync(dlg);`.
  WPF `ShowDialog() == true` ⇒ `await Dialogs.ShowAsync(dlg) == true` (TResult = `bool?`).
  Có sẵn: `Dialogs.MessageAsync`, `Dialogs.ConfirmAsync(title,msg)`, `Dialogs.PromptAsync(title,label,initial,isPassword)` (thay InputDialog/PasswordPromptDialog/MessageBox). `MessageBox.Show` đồng bộ → thành `await` ⇒ hàm chứa nó phải async.
- Layout phải co giãn được (điện thoại sau này): tránh Width cố định lớn, ưu tiên MinWidth/MaxWidth, Grid * / Auto.
- Mỗi file hoàn chỉnh khi ghi (đang có agent khác làm song song ở bản copy riêng).

## Kiểm thử trực quan (bắt buộc)
- Chạy thử bằng dữ liệu giả: `WNTERM_DATA_DIR`/`WNTERM_LOCAL_DIR` (KHÔNG đụng `%APPDATA%\WNTerm` thật — chứa VM production, không kết nối thử vào đó).
- Chụp cửa sổ bằng `PrintWindow` (không bấm chuột theo toạ độ màn hình, vì có thể trúng cửa sổ khác). Có thể thêm biến debug để tự mở dialog.
- `dotnet build src/WNTerm.Desktop` phải 0 lỗi.
