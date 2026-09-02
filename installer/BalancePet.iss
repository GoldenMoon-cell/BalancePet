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
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
CloseApplications=yes
RestartApplications=no
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
BeveledLabel=BalancePet
WelcomeLabel1=欢迎安装 BalancePet
WelcomeLabel2=安装向导将把 BalancePet {#AppVersion} 安装到你的电脑。%n%n可以选择仅当前用户安装，或为所有用户安装到受保护目录（需要管理员权限）。
SelectDirLabel3=请选择 BalancePet 的安装目录：
SelectDirBrowseLabel=点击“浏览”选择其他目录，文件夹名称可以自定义。
FinishedHeadingLabel=BalancePet 安装完成
FinishedLabel=BalancePet 已安装完成。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BalancePet"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\BalancePet"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 BalancePet"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function GetDefaultDir(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{autopf}\BalancePet')
  else
    Result := ExpandConstant('{localappdata}\Programs\BalancePet');
end;
