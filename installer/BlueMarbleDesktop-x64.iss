; Blue Marble Desktop - Inno Setup Script (x64)
; Produces a standard Windows installer for x64 systems

#define MyAppName "Blue Marble Desktop"
#define MyAppVersion "2.1.5"
#define MyAppPublisher "Blue Marble Desktop"
#define MyAppURL "https://github.com/Lushkies/blue-marble-desktop"
#define MyAppExeName "BlueMarbleDesktop.exe"

[Setup]
AppId={{D3A7F8E1-4B2C-4D9E-8A1F-6C3E5D7F9B2A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\assets\license.rtf
OutputDir=..\installer\output
OutputBaseFilename=BlueMarbleDesktop-{#MyAppVersion}-x64-setup
SetupIconFile=..\src\DesktopEarth\Resources\bluemarbledesktop.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; x64os (not x64compatible): ARM64 machines emulate x64 but should use the
; ARM64 installer, which bundles the Mesa3D OpenGL fallback
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Run Blue Marble Desktop when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
; Application and all runtime dependencies (self-contained, not single-file)
Source: "..\publish\x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

; Assets
Source: "..\assets\textures\*"; DestDir: "{app}\assets\textures"; Flags: ignoreversion recursesubdirs

; Icon
Source: "..\src\DesktopEarth\Resources\bluemarbledesktop.ico"; DestDir: "{app}\Resources"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\bluemarbledesktop.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\bluemarbledesktop.ico"; Tasks: desktopicon

[Registry]
; Run on startup (optional task)
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BlueMarbleDesktop"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
; Clear Windows icon cache so the new icon appears immediately
Filename: "ie4uinit.exe"; Parameters: "-show"; Flags: runhidden nowait
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill running instance before uninstall
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Ask if user wants to remove settings and data
    if MsgBox('Do you want to remove Blue Marble Desktop settings, cached images,' +
              ' favorites, and imported images?'#13#10 +
              '(Stored in ' + ExpandConstant('{userappdata}') + '\BlueMarbleDesktop)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\BlueMarbleDesktop'), True, True, True);
    end;
  end;
end;
