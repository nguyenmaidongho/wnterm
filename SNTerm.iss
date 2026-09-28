; SN Term - Inno Setup Script
#define MyAppName "SN Term"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SN Term"
#define MyAppExeName "SNTerm.exe"

[Setup]
AppId={{D37F2C58-6E2A-4B2E-8E07-88C31D45A2F1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=.
OutputBaseFilename=SNTerm-Setup
SetupIconFile=src\SNTerm\Assets\app.ico
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
CloseApplicationsFilter=SNTerm.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình Desktop"; GroupDescription: "Biểu tượng bổ sung:"

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.snterm"; ValueType: string; ValueName: ""; ValueData: "SNTerm.SessionBackup"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\SNTerm.SessionBackup"; ValueType: string; ValueName: ""; ValueData: "SN Term Session File"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\SNTerm.SessionBackup\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\SNTerm.SessionBackup\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "Khởi chạy {#MyAppName}"; Flags: nowait postinstall skipifsilent
