#ifndef AppVersion
  #define AppVersion "0.2.1"
#endif
#ifndef AppIdentity
  #define AppIdentity "PowerPal.Windows"
#endif
[Setup]
AppId={#AppIdentity}
AppName=PowerPal
AppVersion={#AppVersion}
AppPublisher=Chris DiBona
AppPublisherURL=https://github.com/cdibona/PowerPal
DefaultDirName={localappdata}\Programs\PowerPal
DefaultGroupName=PowerPal
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=PowerPal-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\PowerPal.exe
SetupIconFile=..\assets\PowerPal.ico
LicenseFile=..\LICENSE
DisableProgramGroupPage=yes
UsePreviousTasks=yes
[Files]
Source: "..\bin\v{#AppVersion}\PowerPal.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
[Tasks]
Name: "startup"; Description: "Start PowerPal in the tray when I sign in"; Flags: checkedonce
[Icons]
Name: "{group}\PowerPal"; Filename: "{app}\PowerPal.exe"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PowerPal"; ValueData: """{app}\PowerPal.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue; Check: ShouldSetStartup
[Run]
Filename: "{app}\PowerPal.exe"; Parameters: "{code:LaunchParameters}"; Flags: nowait; Check: ShouldLaunch
[Code]
function HasParameter(Value: String): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then Result := True;
end;
function IsPowerPalUpdate: Boolean;
begin
  Result := HasParameter('/POWERPALUPDATE');
end;
function LaunchParameters(Param: String): String;
begin
  if IsPowerPalUpdate then Result := '--background'
  else Result := '--show';
end;
function ShouldSetStartup: Boolean;
begin
  Result := (not IsPowerPalUpdate) or RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PowerPal');
end;
function ShouldLaunch: Boolean;
begin
  Result := not HasParameter('/NORUN');
end;
function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then MsgBox('PowerPal needs .NET Framework 4.8 or later. Install it from Microsoft, then run this installer again.', mbError, MB_OK);
end;
