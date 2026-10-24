#ifndef PublishDir
  #error PublishDir must point to the clean self-contained publish directory
#endif

[Setup]
AppId={{61B3069C-9D4E-4B20-83F6-C3D41800C74C}
AppName={cm:AppName}
AppVersion=1.0.1
AppPublisher=AisoundStudio contributors
DefaultDirName={localappdata}\Programs\AisoundStudio
DisableDirPage=no
DefaultGroupName={cm:AppName}
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
DisableProgramGroupPage=yes
OutputDir={#PublishDir}\..
OutputBaseFilename=AisoundStudio-Setup
SetupIconFile=..\source\app.ico
UninstallDisplayIcon={app}\AisoundStudio.exe
LicenseFile=..\LICENSE
WizardStyle=modern
ShowLanguageDialog=yes
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.AppName=Aisound Studio
chinesesimplified.AppName=声屿 · Aisound Studio
english.ShortcutGroup=Shortcuts:
chinesesimplified.ShortcutGroup=快捷方式：
english.DesktopIcon=Create a desktop shortcut
chinesesimplified.DesktopIcon=创建桌面快捷方式
english.LaunchApp=Launch Aisound Studio
chinesesimplified.LaunchApp=启动声屿 · Aisound Studio

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{cm:AppName}"; Filename: "{app}\AisoundStudio.exe"
Name: "{autodesktop}\{cm:AppName}"; Filename: "{app}\AisoundStudio.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:ShortcutGroup}"; Flags: unchecked

[Run]
Filename: "{app}\AisoundStudio.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent

