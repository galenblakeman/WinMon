; Compiled by tools/New-Release.ps1 with Inno Setup 6.7.3.
#ifndef AppVersion
  #error AppVersion must be supplied
#endif
#ifndef PublishDirectory
  #error PublishDirectory must be supplied
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory must be supplied
#endif
#define Repository "https://github.com/galenblakeman/WinMon"

[Setup]
AppId={{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}
AppName=WinMon
AppVersion={#AppVersion}
AppPublisher=Galen Blakeman
AppPublisherURL={#Repository}
AppSupportURL={#Repository}/issues
AppUpdatesURL={#Repository}/releases
DefaultDirName={localappdata}\Programs\WinMon
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.14393
AppMutex=Local\WinMon.CpuMonitor
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\WinMon\Assets\WinMon.ico
UninstallDisplayIcon={app}\WinMon.exe
OutputDir={#ReleaseDirectory}
OutputBaseFilename=WinMon-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\WinMon.exe"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Separate from the app's opt-in WinMon.lnk, whose presence enables launch hotkeys.
Name: "{userprograms}\WinMon\WinMon"; Filename: "{app}\WinMon.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\WinMon"; Filename: "{app}\WinMon.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WinMon.exe"; Description: "Launch WinMon"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, Executable, Shortcut: String;
  Shell, Link: Variant;
begin
  if CurUninstallStep <> usUninstall then Exit;
  Executable := ExpandConstant('{app}\WinMon.exe');
  { A different portable/installed copy may own the shared startup entry. }
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WinMon', Command) then
    if CompareText(Command, '"' + Executable + '"') = 0 then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WinMon');
  Shortcut := ExpandConstant('{userprograms}\WinMon.lnk');
  if FileExists(Shortcut) then
  begin
    try
      Shell := CreateOleObject('WScript.Shell');
      Link := Shell.CreateShortcut(Shortcut);
      if CompareText(Link.TargetPath, Executable) = 0 then DeleteFile(Shortcut);
    except
      Log('Could not inspect the optional launch shortcut; leaving it in place.');
    end;
  end;
  { User preferences (including portable settings) are deliberately preserved. }
end;
