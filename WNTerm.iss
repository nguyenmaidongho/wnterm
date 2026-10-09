; WN Term - Inno Setup Script
#define MyAppName "WN Term"
#define MyAppVersion GetVersionNumbersString("publish\desktop-win-x64\WNTerm.exe")
#define MyAppPublisher "WN Term"
#define MyAppExeName "WNTerm.exe"

[Setup]
AppId={{D37F2C58-6E2A-4B2E-8E07-88C31D45A2F1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableDirPage=no
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
OutputDir=../release
OutputBaseFilename=WNTerm-Setup
SetupIconFile=src\WNTerm.Desktop\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
CloseApplications=yes
CloseApplicationsFilter=WNTerm.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình Desktop"; GroupDescription: "Biểu tượng bổ sung:"

[Files]
Source: "publish\desktop-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.wnterm"; ValueType: string; ValueName: ""; ValueData: "WNTerm.SessionBackup"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\WNTerm.SessionBackup"; ValueType: string; ValueName: ""; ValueData: "WN Term Session File"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\WNTerm.SessionBackup\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\WNTerm.SessionBackup\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "Khởi chạy {#MyAppName}"; Flags: nowait postinstall skipifsilent
; Cập nhật ngay trong ứng dụng: app tải bộ cài, chạy với /SILENT /update=1 → cài xong tự mở lại app.
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: IsInAppUpdate

[Code]
function IsInAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:update|0}') = '1';
end;
