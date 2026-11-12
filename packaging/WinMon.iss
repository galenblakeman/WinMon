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
#ifndef DesktopDirectoryConflict
  #error DesktopDirectoryConflict must be supplied
#endif
#ifndef DowngradeBlocked
  #error DowngradeBlocked must be supplied
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
ChangesEnvironment=no
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
#if NetworkServiceSetup == "1"
Name: "networkservice"; Description: "Install and enable per-process network monitoring (requires administrator approval)"; Flags: unchecked checkedonce

[CustomMessages]
NetworkServiceSetupFailed=WinMon was installed, but network monitoring could not be enabled. You can try again in Settings > Network.
#endif

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
const FileAttributeReparsePoint = $400;
const InvalidFileAttributes = $FFFFFFFF;
function GetFileAttributes(Path: String): Cardinal; external 'GetFileAttributesW@kernel32.dll stdcall';

function SameOrNested(const First, Second: String): Boolean;
var Left, Right: String;
begin
  Left := AddBackslash(ExpandFileName(First)); Right := AddBackslash(ExpandFileName(Second));
  Result := (CompareText(Left, Right) = 0) or (CompareText(Copy(Left, 1, Length(Right)), Right) = 0) or (CompareText(Copy(Right, 1, Length(Left)), Left) = 0);
end;

function HasReparsePoint(const Directory: String): Boolean;
var Current, Parent: String; Attributes: Cardinal;
begin
  Result := False; Current := ExpandFileName(Directory);
  while Current <> '' do begin
    Attributes := GetFileAttributes(Current);
    if (Attributes <> InvalidFileAttributes) and ((Attributes and FileAttributeReparsePoint) <> 0) then begin Result := True; Exit; end;
    Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Current));
    if CompareText(Parent, Current) = 0 then Exit;
    Current := Parent;
  end;
end;

function HasConflictingDirectory(const Directory: String): Boolean;
var Installed, DesktopDirectory: String; LegacyCompanions: Boolean;
begin
  Result := HasReparsePoint(Directory);
  if Result then Exit;
  LegacyCompanions := FileExists(AddBackslash(Directory) + 'WinMon.Cli.exe') or FileExists(AddBackslash(Directory) + 'WinMon.Mcp.exe');
  if LegacyCompanions and (not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1', 'InstallLocation', DesktopDirectory) or (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(Directory)), RemoveBackslashUnlessRoot(ExpandFileName(DesktopDirectory))) <> 0)) then begin Result := True; Exit; end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C69DAA2-DA6F-4952-BBE8-8AD1FDC6E2B9}_is1', 'InstallLocation', Installed) and SameOrNested(Directory, Installed) then begin Result := True; Exit; end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A78FCFC-CCEE-4D67-9FFB-CA1DA6E1DDE0}_is1', 'InstallLocation', Installed) and SameOrNested(Directory, Installed) then Result := True;
end;

function NextVersionPart(var Value: String): Integer;
var Separator: Integer; Part: String;
begin
  Separator := Pos('.', Value);
  if Separator = 0 then begin Part := Value; Value := ''; end
  else begin Part := Copy(Value, 1, Separator - 1); Delete(Value, 1, Separator); end;
  Result := StrToIntDef(Part, -1);
end;

function IsDowngrade: Boolean;
var Existing, Candidate: String; Index: Integer; OldPart, NewPart: Integer;
begin
  Result := False;
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1', 'DisplayVersion', Existing) then Exit;
  Candidate := '{#AppVersion}';
  for Index := 1 to 3 do begin
    OldPart := NextVersionPart(Existing); NewPart := NextVersionPart(Candidate);
    if (OldPart < 0) or (NewPart < 0) then Exit;
    if NewPart < OldPart then begin Result := True; Exit; end;
    if NewPart > OldPart then Exit;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := (CurPageID <> wpSelectDir) or not HasConflictingDirectory(WizardDirValue);
  if not Result then SuppressibleMsgBox('{#DesktopDirectoryConflict}', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if HasConflictingDirectory(WizardDirValue) then Result := '{#DesktopDirectoryConflict}';
  if (Result = '') and IsDowngrade then Result := '{#DowngradeBlocked}';
end;

procedure CurStepChanged(CurStep: TSetupStep);
#if NetworkServiceSetup == "1"
var
  ResultCode: Integer;
  Succeeded: Boolean;
#endif
begin
  if CurStep <> ssPostInstall then Exit;
  if FileExists(ExpandConstant('{app}\WinMon.Cli.exe')) or FileExists(ExpandConstant('{app}\WinMon.Mcp.exe')) then
    Log('Legacy bundled CLI or MCP executable preserved; install standalone tools before removing these copies.');
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

procedure RemoveLegacyCliPath;
var Owned, Directory, Current, Remaining, Item, Updated: String; Separator: Integer; First, Last: Boolean;
begin
  Directory := ExpandConstant('{app}');
  if not RegQueryStringValue(HKCU, 'Software\WinMon', 'CliPathDirectory', Owned) or
    (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(Owned)), RemoveBackslashUnlessRoot(ExpandFileName(Directory))) <> 0) then Exit;
  if RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then begin
    Remaining := Current; Updated := ''; First := True;
    repeat
      Separator := Pos(';', Remaining);
      if Separator = 0 then begin Item := Remaining; Remaining := ''; Last := True; end
      else begin Item := Copy(Remaining, 1, Separator - 1); Delete(Remaining, 1, Separator); Last := False; end;
      if CompareText(Trim(Item), Owned) <> 0 then begin
        if not First then Updated := Updated + ';';
        Updated := Updated + Item; First := False;
      end;
    until Last;
    if (Updated <> Current) and not RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Updated) then begin
      Log('Could not remove the legacy installer-owned CLI PATH entry; leaving its ownership record for manual cleanup.');
      Exit;
    end;
  end;
  if not RegDeleteValue(HKCU, 'Software\WinMon', 'CliPathDirectory') then
    Log('Could not remove the legacy CLI PATH ownership record.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, Executable, Shortcut: String;
  Shell, Link: Variant;
begin
  if CurUninstallStep <> usUninstall then Exit;
  RemoveLegacyCliPath;
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
