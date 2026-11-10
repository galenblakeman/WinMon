; Compiled by tools/New-Release.ps1 with Inno Setup 6.7.3.
#ifndef AppVersion
  #error AppVersion must be supplied
#endif
#ifndef PublishDirectory
  #error PublishDirectory must be supplied
#endif
#ifndef CliPublishDirectory
  #error CliPublishDirectory must be supplied
#endif
#ifndef CliPathTask
  #error CliPathTask must be supplied
#endif
#ifndef CliPathFailed
  #error CliPathFailed must be supplied
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory must be supplied
#endif
#define Repository "https://github.com/galenblakeman/WinMon"
#ifndef NetworkServiceSetup
  #define NetworkServiceSetup "0"
#endif

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
ChangesEnvironment=yes
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
Name: "clipath"; Description: "{#CliPathTask}"; Flags: unchecked
#if NetworkServiceSetup == "1"
Name: "networkservice"; Description: "Install and enable per-process network monitoring (requires administrator approval)"; Flags: unchecked checkedonce

[CustomMessages]
NetworkServiceSetupFailed=WinMon was installed, but network monitoring could not be enabled. You can try again in Settings > Network.
#endif

[Files]
Source: "{#PublishDirectory}\WinMon.exe"; DestDir: "{app}"
Source: "{#CliPublishDirectory}\WinMon.Cli.exe"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Separate from the app's opt-in WinMon.lnk, whose presence enables launch hotkeys.
Name: "{userprograms}\WinMon\WinMon"; Filename: "{app}\WinMon.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\WinMon"; Filename: "{app}\WinMon.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WinMon.exe"; Description: "Launch WinMon"; Flags: nowait postinstall skipifsilent

[Code]
function PathContains(const Value, Entry: String): Boolean;
var
  Item, Remaining: String;
  Separator: Integer;
begin
  Result := False;
  Remaining := Value;
  while Remaining <> '' do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Item := Remaining;
      Remaining := '';
    end
    else
    begin
      Item := Copy(Remaining, 1, Separator - 1);
      Delete(Remaining, 1, Separator);
    end;
    if CompareText(Trim(Item), Entry) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddCliPath;
var
  Current, Original, Directory: String;
begin
  Directory := ExpandConstant('{app}');
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then Current := '';
  if PathContains(Current, Directory) then Exit;
  Original := Current;
  if Current <> '' then Current := Current + ';';
  if not RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Current + Directory) then
    RaiseException('{#CliPathFailed}');
  if not RegWriteStringValue(HKCU, 'Software\WinMon', 'CliPathDirectory', Directory) then
  begin
    if not RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Original) then
      RaiseException('{#CliPathFailed}');
    RaiseException('{#CliPathFailed}');
  end;
end;

procedure RemoveCliPath;
var
  Current, Directory, Item, Remaining, Updated, Owned: String;
  Separator: Integer;
  First, Last: Boolean;
begin
  Directory := ExpandConstant('{app}');
  if not RegQueryStringValue(HKCU, 'Software\WinMon', 'CliPathDirectory', Owned) or
     (CompareText(Owned, Directory) <> 0) then Exit;
  if RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then
  begin
    if not PathContains(Current, Directory) then
    begin
      if not RegDeleteValue(HKCU, 'Software\WinMon', 'CliPathDirectory') then
        RaiseException('{#CliPathFailed}');
      Exit;
    end;
    Remaining := Current;
    Updated := '';
    First := True;
    repeat
    begin
      Separator := Pos(';', Remaining);
      if Separator = 0 then
      begin
        Item := Remaining;
        Remaining := '';
        Last := True;
      end
      else
      begin
        Item := Copy(Remaining, 1, Separator - 1);
        Delete(Remaining, 1, Separator);
        Last := False;
      end;
      if CompareText(Trim(Item), Directory) <> 0 then
      begin
        if not First then Updated := Updated + ';';
        Updated := Updated + Item;
        First := False;
      end;
    end;
    until Last;
    if not RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Updated) then
      RaiseException('{#CliPathFailed}');
  end;
  if not RegDeleteValue(HKCU, 'Software\WinMon', 'CliPathDirectory') then
    RaiseException('{#CliPathFailed}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
#if NetworkServiceSetup == "1"
var
  ResultCode: Integer;
  Succeeded: Boolean;
#endif
begin
  if CurStep <> ssPostInstall then Exit;
  if WizardIsTaskSelected('clipath') then AddCliPath;
#if NetworkServiceSetup == "1"
  if not WizardIsTaskSelected('networkservice') then Exit;
  Succeeded := False;
  try
    if ExecAsOriginalUser(ExpandConstant('{app}\WinMon.exe'), '--install-network-service',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Succeeded := ResultCode = 0;
  except
    Log(GetExceptionMessage);
  end;
  if not Succeeded then
  begin
    Log(CustomMessage('NetworkServiceSetupFailed'));
    if not WizardSilent then
      SuppressibleMsgBox(CustomMessage('NetworkServiceSetupFailed'), mbError, MB_OK, IDOK);
  end;
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, Executable, Shortcut: String;
  Shell, Link: Variant;
begin
  if CurUninstallStep <> usUninstall then Exit;
  RemoveCliPath;
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
