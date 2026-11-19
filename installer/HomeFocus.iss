; HomeFocus installer (Inno Setup 6). Per-user install, no admin rights needed.
; Build: ISCC.exe /DAppVersion=1.2.3 /DPublishDir=..\publish installer\HomeFocus.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppPublisher
  #define AppPublisher "HomeFocus"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{6F3C2B1E-8A4D-4C67-9E2F-4B1D7A9C5E10}
AppName=HomeFocus
AppVersion={#AppVersion}
AppVerName=HomeFocus {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\HomeFocus
DefaultGroupName=HomeFocus
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\HomeFocus.exe
UninstallDisplayName=HomeFocus
OutputDir=..\dist
OutputBaseFilename=HomeFocus-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
; English by default, Slovak when Windows uses Slovak (needs Inno Setup 6.5+).
Name: "sk"; MessagesFile: "Languages\Slovak.isl"

[CustomMessages]
en.Autostart=Start HomeFocus when I sign in to Windows
sk.Autostart=Spúšťať HomeFocus po prihlásení do Windows
en.LaunchNow=Launch HomeFocus
sk.LaunchNow=Spustiť HomeFocus

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\HomeFocus.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\HomeFocus"; Filename: "{app}\HomeFocus.exe"
Name: "{autodesktop}\HomeFocus"; Filename: "{app}\HomeFocus.exe"; Tasks: desktopicon

[Registry]
; Same entry the app's own "Start with Windows" toggle writes; removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HomeFocus"; \
  ValueData: """{app}\HomeFocus.exe"" --minimized"; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "HomeFocus"; \
  ValueType: none; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\HomeFocus.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

; Tracking data in %APPDATA%\HomeFocus is kept on uninstall on purpose.

[Code]
// The app hides to the tray on close, so stop it hard before files are replaced or removed.
procedure StopHomeFocus();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM HomeFocus.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopHomeFocus();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopHomeFocus();
  Result := True;
end;
