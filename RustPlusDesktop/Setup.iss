; =============================================
; RustPlusDesk Installer (Production)
; Fixes uninstall + supports upgrades
; =============================================

[Setup]
; 🔴 NEVER CHANGE THIS GUID AGAIN (identity of your app forever)
AppId={{7B4E7E6A-7D52-4F7B-AF4E-7A7D8F1C9B21}}

AppName=RustPlusDesk
AppVersion=3.5.3
AppPublisher=RustPlusDesk
AppPublisherURL=https://github.com/Pronwan/rustplus-desktop

DefaultDirName={autopf}\RustPlusDesk
DefaultGroupName=RustPlusDesk

; Fixes upgrades + broken previous installs
UsePreviousAppDir=yes
UsePreviousGroup=yes
CreateUninstallRegKey=yes

; Installer output
OutputDir=bin\Installer
OutputBaseFilename=RustPlusDesk-Setup

; Compression
Compression=lzma2/max
SolidCompression=yes

; UI / permissions
SetupIconFile=rustplus-desktop-icon.ico
UninstallDisplayIcon={app}\RustPlusDesk.exe
PrivilegesRequired=admin
WizardStyle=modern

; Optional but recommended
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes

; =============================================

[Tasks]
Name: "desktopicon"; \
Description: "{cm:CreateDesktopIcon}"; \
GroupDescription: "{cm:AdditionalIcons}"; \
Flags: unchecked

; =============================================

[Files]
; Main EXE (single-file publish)
Source: "bin\Installer\publish\RustPlusDesk.exe"; \
DestDir: "{app}"; Flags: ignoreversion

; Native runtime files
Source: "bin\Installer\publish\runtime\*"; \
DestDir: "{app}\runtime"; \
Flags: ignoreversion recursesubdirs createallsubdirs

; Icons
Source: "bin\Installer\publish\icons\*"; \
DestDir: "{app}\icons"; \
Flags: ignoreversion recursesubdirs createallsubdirs

; Data files
Source: "bin\Installer\publish\rust_items.json"; \
DestDir: "{app}"; Flags: ignoreversion

Source: "bin\Installer\publish\cash.wav"; \
DestDir: "{app}"; Flags: ignoreversion

; =============================================

[Icons]
Name: "{group}\RustPlusDesk"; Filename: "{app}\RustPlusDesk.exe"
Name: "{autodesktop}\RustPlusDesk"; Filename: "{app}\RustPlusDesk.exe"; Tasks: desktopicon

; =============================================

[Run]
Filename: "{app}\RustPlusDesk.exe"; \
Description: "{cm:LaunchProgram,RustPlusDesk}"; \
Flags: nowait postinstall skipifsilent

; =============================================

[UninstallDelete]
; Clean leftover runtime/data folders on uninstall
Type: filesandordirs; Name: "{app}\runtime"
Type: filesandordirs; Name: "{app}\icons"

; =============================================
[Code]

procedure DeleteOldBrokenUninstallers;
var
  Key: string;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\RustPlusDesk';

  RegDeleteKeyIncludingSubkeys(HKLM, Key);
  RegDeleteKeyIncludingSubkeys(HKCU, Key);

  Key := 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\RustPlusDesk';

  RegDeleteKeyIncludingSubkeys(HKLM, Key);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    DeleteOldBrokenUninstallers;
  end;
end;