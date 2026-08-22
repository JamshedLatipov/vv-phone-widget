#define MyAppName "PROFFI - Phone"
; Percent-encoded MyAppName, for the ms-settings deep link in [Run]. Keep in step
; with MyAppName: it has to match the RegisteredApplications entry byte for byte
; or Settings just ignores it. There is no URL-encode in the preprocessor.
#define MyAppNameUrl "PROFFI%20-%20Phone"
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
; Three separate registrations are needed and they do three different jobs.
; Only the second one makes a link actually open; the third is what puts us in
; the Settings UI. Having the first and third without the second — which is what
; this installer used to do — registers a handler nobody can reach.
;
;  1. The ProgId. The object a UrlAssociation is allowed to point at, and the
;     command Windows runs once the user has picked us for a scheme.
;  2. The scheme keys (tel, callto). What ShellExecute resolves when something
;     opens a tel: link. Microsoft, on the URL Protocol value: "Without this key,
;     the handler application will not launch."
;     https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa767914(v=vs.85)
;  3. Capabilities + RegisteredApplications. What lists us under Settings ▸
;     Default apps. ApplicationDescription is required there — an app without one
;     is not shown at all.
;     https://learn.microsoft.com/en-us/windows/win32/shell/default-programs
;
; What an installer may NOT do is make itself the default. Windows blocks that on
; purpose — "Registry-based changes are not supported for apps", enforced by the
; UCPD.sys filter driver — so we register as a candidate and let the user choose.
; https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform

; 1. ProgId that knows how to open a tel-style link with our softphone.
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel"; \
  ValueType: string; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel"; \
  ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel\DefaultIcon"; \
  ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\OrbitalSIP.Tel\shell\open\command"; \
  ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; 2. The scheme keys themselves. createvalueifdoesntexist means we fill in a
; scheme nobody handles yet and keep our hands off one that is already spoken
; for. A per-user handler is safe from us either way: HKCU\Software\Classes wins
; over HKLM\Software\Classes wherever the two collide.
; Uninstall is handled in [Code], not by a flag — these keys are shared, and
; "delete on uninstall" would take a successor's registration down with ours.
;
; sip is deliberately absent here. A sip: URI is as often "join this conference"
; as it is "call this number" — Zoom, Teams and friends register it for meeting
; joins — and we cannot tell the two apart from the URI. Claiming it by default
; would hijack meeting links. We still declare sip under Capabilities below, so a
; user who wants us to answer sip: can pick us in Settings; we just refuse to
; take it without being asked.
Root: HKLM; Subkey: "Software\Classes\tel"; \
  ValueType: string; ValueData: "URL:Tel Protocol"; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "Software\Classes\tel"; \
  ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "Software\Classes\tel\DefaultIcon"; \
  ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "Software\Classes\tel\shell\open\command"; \
  ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: createvalueifdoesntexist

Root: HKLM; Subkey: "Software\Classes\callto"; \
  ValueType: string; ValueData: "URL:Callto Protocol"; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "Software\Classes\callto"; \
  ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "Software\Classes\callto\DefaultIcon"; \
  ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "Software\Classes\callto\shell\open\command"; \
  ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: createvalueifdoesntexist

; 3. Register the app with its URL capabilities so Windows offers it as an option
; for tel:/callto:/sip: links (Settings ▸ Default apps ▸ "Choose defaults by link
; type"). ApplicationIcon is what every shipping app sets here and what the
; Settings list draws next to the name; it is not in Microsoft's documented list
; of Capabilities values, so treat it as cosmetic rather than load-bearing.
Root: HKLM; Subkey: "Software\OrbitalSIP"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities"; \
  ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities"; \
  ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{#MyAppName} softphone"
Root: HKLM; Subkey: "Software\OrbitalSIP\Capabilities"; \
  ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
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
; Opening this page is the only supported way to become the default for tel:.
; The registeredAppMachine argument is the name we wrote to RegisteredApplications
; and it preselects us in the list; if Windows cannot match it the page still
; opens, just without the selection. Off by default and skipped in silent
; installs so unattended deployments never pop a window.
; https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings
Filename: "ms-settings:defaultapps?registeredAppMachine={#MyAppNameUrl}"; \
  Description: "Choose {#MyAppName} for phone links (opens Windows Settings)"; \
  Flags: shellexec nowait runasoriginaluser postinstall skipifsilent unchecked

[Code]

const
  SHCNE_ASSOCCHANGED = $08000000;
  SHCNF_IDLIST       = $0000;

// Windows caches association data; without this the new registration may not
// show up in Settings until the user logs out. Microsoft calls the notification
// "required to ensure the proper functioning of system defaults".
// https://learn.microsoft.com/en-us/windows/win32/shell/default-programs
procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1, dwItem2: Cardinal);
  external 'SHChangeNotify@shell32.dll stdcall';

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  // Kill any running OrbitalSIP process so the exe can be overwritten
  Exec('taskkill', '/F /IM OrbitalSIP.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;

// Hand a scheme back, but only while it is still ours. Another program may have
// claimed tel: since we were installed, and overwriting our command with theirs
// is exactly the case where deleting the key would break a working handler that
// has nothing to do with us.
procedure ReleaseScheme(const Scheme: String);
var
  Key, Cmd, OurExe: String;
begin
  Key := 'Software\Classes\' + Scheme;
  OurExe := LowerCase(ExpandConstant('{app}\{#MyAppExeName}'));

  if not RegQueryStringValue(HKLM, Key + '\shell\open\command', '', Cmd) then
    Exit;
  if Pos(OurExe, LowerCase(Cmd)) = 0 then
    Exit;

  RegDeleteKeyIncludingSubkeys(HKLM, Key + '\shell');
  RegDeleteKeyIncludingSubkeys(HKLM, Key + '\DefaultIcon');
  RegDeleteValue(HKLM, Key, 'URL Protocol');
  RegDeleteValue(HKLM, Key, '');
  // Leaves the key alone if anyone else has since added anything under it.
  RegDeleteKeyIfEmpty(HKLM, Key);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  ReleaseScheme('tel');
  ReleaseScheme('callto');
  SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;
