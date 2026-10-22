; DeadLinks installer. Build with scripts\build-installer.ps1 (publishes a self-contained build to dist\stage first).
#define AppName "DeadLinks"
#define AppFullName "DeadLinks - Broken Shortcut & Dead Link Cleaner"
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define StageDir "..\dist\stage"

[Setup]
AppId={{6FB59CB2-B67F-5238-940C-CDDA3C2203E7}
AppName={#AppFullName}
AppVersion={#AppVersion}
AppPublisher=Md Anamul Hasan
AppPublisherURL=https://github.com/md-anamul-hasan/deadlinks
AppSupportURL=https://github.com/md-anamul-hasan/deadlinks/issues
DefaultDirName={autopf}\DeadLinks
DefaultGroupName=DeadLinks
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DeadLinks.exe
UninstallDisplayName={#AppFullName}
SetupIconFile=..\Assets/icon.ico
LicenseFile=..\LICENSE
OutputDir=..\dist\installer
OutputBaseFilename=DeadLinks-Setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\DeadLinks"; Filename: "{app}\DeadLinks.exe"
Name: "{autodesktop}\DeadLinks"; Filename: "{app}\DeadLinks.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DeadLinks.exe"; Description: "Launch DeadLinks"; Flags: nowait postinstall skipifsilent
