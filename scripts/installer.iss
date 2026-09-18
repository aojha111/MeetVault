; ============================================================================
; MeetVault installer script (Inno Setup 6)
; Input:  dist\portable\MeetVault\  (produced by scripts\publish.cmd)
; Output: dist\MeetVault-<version>-setup-win-x64.exe
;
; Installs into {localappdata}\Programs\MeetVault (per-user, no admin needed).
; The app downloads + SHA-256-verifies its AI models on first run (Model
; Manager); no weights ship in this installer.
; ============================================================================

#define MyAppName "MeetVault"
#define MyAppVersion GetEnv("MEETVAULT_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "MeetVault"
#define MyAppURL "https://github.com/meetvault"
#define MyAppExeName "MeetVault.exe"

[Setup]
AppId={{7C1B6E3A-52D4-4B7E-9C58-MEETVAULT1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=MeetVault-{#MyAppVersion}-setup-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\portable\MeetVault\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\MeetVault CLI"; Filename: "{app}\mvault.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Keep nothing: the vault lives under {app} (portable-style layout).
Type: filesandordirs; Name: "{app}"
