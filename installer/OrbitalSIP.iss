#define MyAppName "PROFFI - Phone"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "PROFFI - Phone"
#define MyAppURL "https://proffi.io"
#define MyAppExeName "OrbitalSIP.exe"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{A1B2C3D4-1234-5678-ABCD-000000000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=PROFFI-Setup-{#MyAppVersion}
SetupIconFile=proffi.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardImageFile=wizard-image.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763
; Run silently on startup (system tray app)
CloseApplications=force
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupentry"; Description: "Launch {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
; Main executable (self-contained, single file)
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Sound files
Source: "{#PublishDir}\sounds\*"; DestDir: "{app}\sounds"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Auto-start on Windows login
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupentry

; ── tel: / callto: / sip: protocol handler ───────────────────────────────────
; ProgId that knows how to open a tel-style link with our softphone.
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel"; \
  ValueType: string; ValueData: "URL:Tel Protocol"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel"; \
  ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel\DefaultIcon"; \
  ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel\shell\open\command"; \
  ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; Register the app with its URL capabilities so Windows offers it as an option
; for tel:/callto:/sip: links (Settings ▸ Default apps and the "Open with" picker).
Root: HKLM; Subkey: "Software\OrbitalSIP"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities"; \
  ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities"; \
  ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{#MyAppName} softphone"
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities\UrlAssociations"; \
  ValueType: string; ValueName: "tel"; ValueData: "OrbitalSIP.Tel"
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities\UrlAssociations"; \
  ValueType: string; ValueName: "callto"; ValueData: "OrbitalSIP.Tel"
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities\UrlAssociations"; \
  ValueType: string; ValueName: "sip"; ValueData: "OrbitalSIP.Tel"
Root: HKLM; Subkey: "Software\RegisteredApplications"; \
  ValueType: string; ValueName: "{#MyAppName}"; ValueData: "Software\OrbitalSIP\Capabilities"; \
  Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  // Kill any running OrbitalSIP process so the exe can be overwritten
  Exec('taskkill', '/F /IM OrbitalSIP.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
