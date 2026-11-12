; Compiled by tools/New-Release.ps1 with Inno Setup 6.7.3.
#ifndef AppVersion
  #error AppVersion must be supplied
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
#ifndef CliLegacyPathTask
  #error CliLegacyPathTask must be supplied
#endif
#ifndef CliDirectoryConflict
  #error CliDirectoryConflict must be supplied
#endif
#ifndef DowngradeBlocked
  #error DowngradeBlocked must be supplied
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory must be supplied
#endif

[Setup]
AppId={{8C69DAA2-DA6F-4952-BBE8-8AD1FDC6E2B9}
AppName=WinMon CLI
AppVersion={#AppVersion}
AppPublisher=Galen Blakeman
AppPublisherURL=https://github.com/galenblakeman/WinMon
AppSupportURL=https://github.com/galenblakeman/WinMon/issues
AppUpdatesURL=https://github.com/galenblakeman/WinMon/releases
DefaultDirName={localappdata}\Programs\WinMon CLI
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.14393
ChangesEnvironment=yes
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\WinMon\Assets\WinMon.ico
UninstallDisplayIcon={app}\WinMon.Cli.exe
OutputDir={#ReleaseDirectory}
OutputBaseFilename=WinMon-Cli-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "clipath"; Description: "{#CliPathTask}"; Flags: unchecked
Name: "legacypath"; Description: "{#CliLegacyPathTask}"; Flags: unchecked

[Files]
Source: "{#CliPublishDirectory}\WinMon.Cli.exe"; DestDir: "{app}"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

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

function ConflictsWithRegisteredProduct(const Directory: String): Boolean;
var Installed: String;
begin
  Result := False;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1', 'InstallLocation', Installed) and SameOrNested(Directory, Installed) then begin Result := True; Exit; end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A78FCFC-CCEE-4D67-9FFB-CA1DA6E1DDE0}_is1', 'InstallLocation', Installed) and SameOrNested(Directory, Installed) then begin Result := True; Exit; end;
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
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C69DAA2-DA6F-4952-BBE8-8AD1FDC6E2B9}_is1', 'DisplayVersion', Existing) then Exit;
  Candidate := '{#AppVersion}';
  for Index := 1 to 3 do begin
    OldPart := NextVersionPart(Existing); NewPart := NextVersionPart(Candidate);
    if (OldPart < 0) or (NewPart < 0) then Exit;
    if NewPart < OldPart then begin Result := True; Exit; end;
    if NewPart > OldPart then Exit;
  end;
end;

function PathContains(const Value, Entry: String): Boolean;
var Item, Remaining: String; Separator: Integer;
begin
  Result := False; Remaining := Value;
  while Remaining <> '' do begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then begin Item := Remaining; Remaining := ''; end
    else begin Item := Copy(Remaining, 1, Separator - 1); Delete(Remaining, 1, Separator); end;
    if CompareText(Trim(Item), Entry) = 0 then begin Result := True; Exit; end;
  end;
end;

procedure RemovePath(const ValueName, Directory: String);
var Current, Item, Remaining, Updated, Owned: String; Separator: Integer; First, Last: Boolean;
begin
  if not RegQueryStringValue(HKCU, 'Software\WinMon', ValueName, Owned) or (CompareText(Owned, Directory) <> 0) then Exit;
  if RegQueryStringValue(HKCU, 'Environment', 'Path', Current) and PathContains(Current, Directory) then begin
    Remaining := Current; Updated := ''; First := True;
    repeat
      Separator := Pos(';', Remaining);
      if Separator = 0 then begin Item := Remaining; Remaining := ''; Last := True; end
      else begin Item := Copy(Remaining, 1, Separator - 1); Delete(Remaining, 1, Separator); Last := False; end;
      if CompareText(Trim(Item), Directory) <> 0 then begin if not First then Updated := Updated + ';'; Updated := Updated + Item; First := False; end;
    until Last;
    if not RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Updated) then RaiseException('{#CliPathFailed}');
  end;
  if not RegDeleteValue(HKCU, 'Software\WinMon', ValueName) then RaiseException('{#CliPathFailed}');
end;

procedure AddCliPath;
var Current, Original, Directory: String;
begin
  Directory := ExpandConstant('{app}'); if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then Current := '';
  if PathContains(Current, Directory) then Exit;
  Original := Current; if Current <> '' then Current := Current + ';';
  if not RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Current + Directory) then RaiseException('{#CliPathFailed}');
  if not RegWriteStringValue(HKCU, 'Software\WinMon', 'CliStandalonePathDirectory', Directory) then begin
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Original); RaiseException('{#CliPathFailed}');
  end;
end;

function HasConflictingFiles(const Directory: String): Boolean;
begin
  Result := HasReparsePoint(Directory) or ConflictsWithRegisteredProduct(Directory) or FileExists(AddBackslash(Directory) + 'WinMon.exe') or FileExists(AddBackslash(Directory) + 'WinMon.Mcp.exe');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := (CurPageID <> wpSelectDir) or not HasConflictingFiles(WizardDirValue);
  if not Result then SuppressibleMsgBox('{#CliDirectoryConflict}', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if HasConflictingFiles(WizardDirValue) then Result := '{#CliDirectoryConflict}';
  if (Result = '') and IsDowngrade then Result := '{#DowngradeBlocked}';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var LegacyDirectory, DesktopDirectory: String;
begin
  if CurStep <> ssPostInstall then Exit;
  if WizardIsTaskSelected('clipath') then AddCliPath;
  if WizardIsTaskSelected('legacypath') and RegQueryStringValue(HKCU, 'Software\WinMon', 'CliPathDirectory', LegacyDirectory) and
    RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1', 'InstallLocation', DesktopDirectory) and
    (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(LegacyDirectory)), RemoveBackslashUnlessRoot(ExpandFileName(DesktopDirectory))) = 0) then RemovePath('CliPathDirectory', LegacyDirectory);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemovePath('CliStandalonePathDirectory', ExpandConstant('{app}'));
end;
