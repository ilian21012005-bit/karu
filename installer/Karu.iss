; Installateur Windows Karu (Inno Setup 6)
; Compile via tools/build-installer.ps1 ou GitHub Actions.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MyAppName "Karu"
#define MyAppPublisher "Karu"
#define MyAppURL "https://github.com/ilian21012005-bit/clip-buffer"
#define MyAppExeName "Karu.exe"

[Setup]
AppId={{A7C3E8F1-4B2D-4E9A-9C1F-6D8B2A0E5F73}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\third_party\ffmpeg\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename=Karu-Setup-{#AppVersion}
SetupIconFile=..\src\ClipBuffer.App\Assets\karu.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
InfoBeforeFile=welcome-fr.txt
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer une icône sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: checkedonce
Name: "startupicon"; Description: "Lancer Karu avec Windows (raccourci Démarrage)"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
