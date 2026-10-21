; Candidate scaffold only: compile and validate in a clean Windows account before distribution.
; Inno Setup 6+; pass /DSourceDir=<publish directory> if using a custom build output.
#ifndef SourceDir
  #define SourceDir "..\..\outputs\OrbLink-win-x64-v0.3.0"
#endif
[Setup]
AppId={{5B4B47DC-7EE1-4CA9-9AB5-2C9934FB02A8}
AppName=OrbLink
AppVersion=0.3.0
AppVerName=OrbLink 0.3.0 beta
DefaultDirName={localappdata}\Programs\OrbLink
DefaultGroupName=OrbLink
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\outputs\installer-output
OutputBaseFilename=OrbLink-Setup-x64-v0.3.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\OrbLink.exe
LicenseFile=..\..\LICENSE
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked
Name: "autostart"; Description: "Iniciar OrbLink com o Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\OrbLink.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LEIA-ME.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\CHECKLIST-TESTES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\docs\licenses\*"; DestDir: "{app}\docs\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\OrbLink"; Filename: "{app}\OrbLink.exe"
Name: "{autodesktop}\OrbLink"; Filename: "{app}\OrbLink.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OrbLink"; ValueData: """{app}\OrbLink.exe"""; Tasks: autostart

[Run]
Filename: "{app}\OrbLink.exe"; Description: "Abrir OrbLink"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OrbLink', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\OrbLink.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OrbLink');
end;
; Never delete %LOCALAPPDATA%\OrbLink on install/update/uninstall.
; No firewall rule, certificate generation, signing bypass or registry-wide cleanup.
