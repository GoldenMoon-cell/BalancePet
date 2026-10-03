#ifndef AppVersion
  #error AppVersion must be supplied with /DAppVersion=<version>
#endif

#ifndef SourceDir
  #error SourceDir must be supplied with /DSourceDir=<published-directory>
#endif

#ifndef OutputDir
  #define OutputDir "..\\dist"
#endif

#define AppName "BalancePet"
#define AppExeName "BalancePet.Wpf.exe"

[Setup]
AppId={{B6777BA6-3A1A-4C53-9FC0-1B6F0B569F74}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=GoldenMoon-cell
AppPublisherURL=https://github.com/GoldenMoon-cell/BalancePet
AppSupportURL=https://github.com/GoldenMoon-cell/BalancePet/issues
AppUpdatesURL=https://github.com/GoldenMoon-cell/BalancePet/releases
DefaultDirName={code:GetDefaultDir}
UsePreviousAppDir=yes
AppendDefaultDirName=no
DisableDirPage=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=BalancePet-{#AppVersion}-Setup
SetupIconFile={#SourceDir}\assets\balance-pet.ico
UninstallDisplayIcon={app}\assets\balance-pet.ico
UninstallDisplayName={#AppName}
ArchitecturesAllowed=x64compatible
; Windows 10 1809. Without this the wizard defaults to 6.1sp1 — Windows 7 SP1 — and
; installs a program that cannot start, because a self-contained .NET 8 desktop app needs
; Windows 10 1607 at the earliest. 1809 is the floor this program's own code is written
; against: below it there is no window material at all (the DWM calls the material needs
; begin at 17763), so an older system would get a working but plain window. Windows 11
; satisfies this: it reports 10.0.22000 and later.
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
CloseApplications=yes
RestartApplications=no
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.InstallScopeHint=You can install for the current user or use administrator permissions for a shared, protected location.
english.DesktopShortcut=Create a desktop shortcut
english.AdditionalTasks=Additional options:
english.LaunchBalancePet=Launch BalancePet
chinesesimplified.InstallScopeHint=可以选择仅当前用户安装，或为所有用户安装到受保护目录（需要管理员权限）。
chinesesimplified.DesktopShortcut=创建桌面快捷方式
chinesesimplified.AdditionalTasks=附加选项：
chinesesimplified.LaunchBalancePet=启动 BalancePet

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BalancePet"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\BalancePet"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchBalancePet}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + CustomMessage('InstallScopeHint');
end;

function GetDefaultDir(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{autopf}\BalancePet')
  else
    Result := ExpandConstant('{localappdata}\Programs\BalancePet');
end;
