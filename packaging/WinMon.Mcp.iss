; Compiled by tools/New-Release.ps1 with Inno Setup 6.7.3.
#ifndef AppVersion
  #error AppVersion must be supplied
#endif
#ifndef McpPublishDirectory
  #error McpPublishDirectory must be supplied
#endif
#ifndef McpCodexTask
  #error McpCodexTask must be supplied
#endif
#ifndef McpClaudeTask
  #error McpClaudeTask must be supplied
#endif
#ifndef McpRegistrationHelp
  #error McpRegistrationHelp must be supplied
#endif
#ifndef McpRegistrationFailed
  #error McpRegistrationFailed must be supplied
#endif
#ifndef McpCleanupSkipped
  #error McpCleanupSkipped must be supplied
#endif
#ifndef McpDirectoryConflict
  #error McpDirectoryConflict must be supplied
#endif
#ifndef DowngradeBlocked
  #error DowngradeBlocked must be supplied
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory must be supplied
#endif

[Setup]
AppId={{9A78FCFC-CCEE-4D67-9FFB-CA1DA6E1DDE0}
AppName=WinMon MCP
AppVersion={#AppVersion}
AppPublisher=Galen Blakeman
AppPublisherURL=https://github.com/galenblakeman/WinMon
AppSupportURL=https://github.com/galenblakeman/WinMon/issues
AppUpdatesURL=https://github.com/galenblakeman/WinMon/releases
DefaultDirName={localappdata}\Programs\WinMon MCP
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.14393
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\WinMon\Assets\WinMon.ico
UninstallDisplayIcon={app}\WinMon.Mcp.exe
OutputDir={#ReleaseDirectory}
OutputBaseFilename=WinMon-Mcp-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "codex"; Description: "{#McpCodexTask}"; GroupDescription: "{#McpRegistrationHelp}"; Flags: unchecked checkedonce
Name: "claude"; Description: "{#McpClaudeTask}"; Flags: unchecked checkedonce

[Files]
Source: "{#McpPublishDirectory}\WinMon.Mcp.exe"; DestDir: "{app}"
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
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C69DAA2-DA6F-4952-BBE8-8AD1FDC6E2B9}_is1', 'InstallLocation', Installed) and SameOrNested(Directory, Installed) then begin Result := True; Exit; end;
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
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A78FCFC-CCEE-4D67-9FFB-CA1DA6E1DDE0}_is1', 'DisplayVersion', Existing) then Exit;
  Candidate := '{#AppVersion}';
  for Index := 1 to 3 do begin
    OldPart := NextVersionPart(Existing); NewPart := NextVersionPart(Candidate);
    if (OldPart < 0) or (NewPart < 0) then Exit;
    if NewPart < OldPart then begin Result := True; Exit; end;
    if NewPart > OldPart then Exit;
  end;
end;

function HasConflictingFiles(const Directory: String): Boolean;
begin
  Result := HasReparsePoint(Directory) or ConflictsWithRegisteredProduct(Directory) or FileExists(AddBackslash(Directory) + 'WinMon.exe') or FileExists(AddBackslash(Directory) + 'WinMon.Cli.exe');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := (CurPageID <> wpSelectDir) or not HasConflictingFiles(WizardDirValue);
  if not Result then SuppressibleMsgBox('{#McpDirectoryConflict}', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if HasConflictingFiles(WizardDirValue) then Result := '{#McpDirectoryConflict}';
  if (Result = '') and IsDowngrade then Result := '{#DowngradeBlocked}';
end;

procedure RegisterClient(const Client: String);
var ResultCode: Integer; Message: String; Succeeded: Boolean;
begin
  ResultCode := -1; Succeeded := False;
  try
    Succeeded := ExecAsOriginalUser(ExpandConstant('{app}\WinMon.Mcp.exe'), '--installer-registration ' + Client +
        ' --install-path "' + ExpandConstant('{app}\WinMon.Mcp.exe') + '"',
        ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  except
    Log('WinMon MCP registration raised an exception for ' + Client + ': ' + GetExceptionMessage);
  end;
  if not Succeeded then begin
    Log('WinMon MCP registration failed for ' + Client + ' (exit ' + IntToStr(ResultCode) + ').');
    Log('Inspect any existing entry first: ' + Client + ' mcp get winmon');
    if Client = 'codex' then
      Log('Manual registration: codex mcp add winmon -- "' + ExpandConstant('{app}\WinMon.Mcp.exe') + '"')
    else
      Log('Manual user registration: claude mcp add --scope user --transport stdio winmon -- "' + ExpandConstant('{app}\WinMon.Mcp.exe') + '"');
    Log('Preserve existing custom settings before changing a conflicting entry; restart client sessions after registration.');
    if not WizardSilent then begin
      Message := '{#McpRegistrationFailed}';
      StringChangeEx(Message, '{0}', Client, True);
      SuppressibleMsgBox(Message, mbError, MB_OK, IDOK);
    end;
  end;
end;

procedure UnregisterClient(const Client: String);
var ResultCode: Integer; Succeeded: Boolean; Message: String;
begin
  if IsAdmin then begin
    Log('WinMon MCP registration cleanup skipped for ' + Client + ' because uninstall is elevated.');
    Log('Inspect any remaining entry: ' + Client + ' mcp get winmon');
    Log('Remove only entries pointing to this installation; see the MCP migration guide for scoped removal commands.');
    if not UninstallSilent then begin
      Message := '{#McpCleanupSkipped}';
      StringChangeEx(Message, '{0}', Client, True);
      SuppressibleMsgBox(Message, mbInformation, MB_OK, IDOK);
    end;
    Exit;
  end;
  ResultCode := -1; Succeeded := False;
  try
    Succeeded := Exec(ExpandConstant('{app}\WinMon.Mcp.exe'), '--installer-registration ' + Client +
        ' --install-path "' + ExpandConstant('{app}\WinMon.Mcp.exe') + '" --unregister',
        ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  except
    Log('WinMon MCP registration cleanup raised an exception for ' + Client + ': ' + GetExceptionMessage);
  end;
  if not Succeeded then begin
    Log('WinMon MCP registration cleanup was skipped for ' + Client + ' (exit ' + IntToStr(ResultCode) + ').');
    Log('Inspect any remaining entry: ' + Client + ' mcp get winmon');
    if Client = 'codex' then Log('Remove only a stale entry pointing to this installation: codex mcp remove winmon')
    else Log('Remove only a stale user entry pointing to this installation: claude mcp remove --scope user winmon');
    if not UninstallSilent then begin
      Message := '{#McpCleanupSkipped}';
      StringChangeEx(Message, '{0}', Client, True);
      SuppressibleMsgBox(Message, mbInformation, MB_OK, IDOK);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  if WizardIsTaskSelected('codex') then RegisterClient('codex');
  if WizardIsTaskSelected('claude') then RegisterClient('claude');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then Exit;
  UnregisterClient('codex');
  UnregisterClient('claude');
end;
