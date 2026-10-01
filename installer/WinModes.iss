; WinModes installer (Inno Setup 6 or 7).
; Built by tools/package.ps1, which passes the version and the folder to package:
;   ISCC /DAppVersion=0.3.0 /DSourceDir=..\artifacts\WinModes /DOutputDir=..\artifacts installer\WinModes.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\WinModes"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "WinModes"
#define AppExe "WinModes.exe"
#define AppUrl "https://github.com/LinkPhoenix/winmodes"

[Setup]
; Never change AppId: it is how an update finds the previous install.
AppId={{6E0B5C0E-6F0B-4B7B-9C39-5A1D0C2B7E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=LinkPhoenix
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
; Program Files: the protected list and the elevated helper cannot be changed without administrator rights.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE.md
SetupIconFile=..\src\WinModes.App\Assets\winmodes.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=WinModes-v{#AppVersion}-setup-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; Modes are edited from the unelevated app, so users may write here. The protected list in data\ stays admin-only
; and is enforced by the elevated helper whatever a profile says.
Name: "{app}\profiles"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Close the app, then undo the active mode so no service is left changed after the uninstall.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "CloseWinModes"
Filename: "{app}\WinModes.Elevated.exe"; Parameters: "revert"; Flags: runhidden waituntilterminated; RunOnceId: "RevertActiveMode"
; Then undo what the Optimize and Services pages changed machine-wide: service start types, policy values, scheduled tasks.
Filename: "{app}\WinModes.Elevated.exe"; Parameters: "change :restore :untweak"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreServices"
; Remove the opt-in task that starts the helper without a prompt.
Filename: "{app}\WinModes.Elevated.exe"; Parameters: "task remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveSilentSwitchTask"

[Registry]
; "Start with Windows" is written by the app; remove it with the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "WinModes"; Flags: dontcreatekey uninsdeletevalue noerror
