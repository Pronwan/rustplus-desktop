; =============================================
; RustPlusDesk Installer (Production)
; Fixes uninstall + supports upgrades
; =============================================

#define MyAppName      "RustPlusDesk"
#define MyAppVersion   "4.1.4"
#define MyAppPublisher "Pronwan" 
#define MyAppURL       "https://github.com/Pronwan/rustplus-desktop"
#define MyAppExeName   "RustPlusDesk.exe"
; 🔴 ORIGINAL-ID 
#define MyAppId        "{{E8E0C4C1-2E2F-4D2D-9BE7-3B19F0C1ABCD}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Jawads Stabilitäts-Features
UsePreviousAppDir=yes
UsePreviousGroup=yes
CreateUninstallRegKey=yes
OutputDir=bin\Installer
OutputBaseFilename=RustPlusDesk-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes

; Deine Optik
SetupIconFile=rustplus-desktop-icon.ico
WizardImageFile=installer.png
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Jawads optimierte Dateiliste
Source: "bin\Installer\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Installer\publish\runtime\*";       DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "bin\Installer\publish\icons\*";         DestDir: "{app}\icons";   Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\runtime"
Type: filesandordirs; Name: "{app}\icons"

[Code]
// Jawads Aufräum-Logik (Sehr nützlich!)
procedure DeleteOldBrokenUninstallers;
var Key: string;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\RustPlusDesk';
  RegDeleteKeyIncludingSubkeys(HKLM, Key);
  RegDeleteKeyIncludingSubkeys(HKCU, Key);
  Key := 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\RustPlusDesk';
  RegDeleteKeyIncludingSubkeys(HKLM, Key);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then DeleteOldBrokenUninstallers;
end;