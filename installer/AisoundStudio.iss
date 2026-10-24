#ifndef PublishDir
  #error PublishDir must point to the clean self-contained publish directory
#endif

[Setup]
AppId={{61B3069C-9D4E-4B20-83F6-C3D41800C74C}
AppName=声屿 · Aisound Studio
AppVersion=1.0.0
AppPublisher=AisoundStudio contributors
DefaultDirName={localappdata}\Programs\AisoundStudio
DisableDirPage=yes
DefaultGroupName=声屿 · Aisound Studio
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
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
Uninstallable=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\声屿 · Aisound Studio"; Filename: "{app}\AisoundStudio.exe"
Name: "{autodesktop}\声屿 · Aisound Studio"; Filename: "{app}\AisoundStudio.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\AisoundStudio.exe"; Description: "启动声屿 · Aisound Studio"; Flags: postinstall nowait skipifsilent
