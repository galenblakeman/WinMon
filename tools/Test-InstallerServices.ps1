#requires -Version 7.4
# Compile the real Inno script, then execute its service orchestration with simulated OS boundaries.
# No service/driver installation, elevation, application launch or registry mutation occurs.
[CmdletBinding()]
param([string]$InnoCompiler = (Join-Path $PSScriptRoot '../artifacts/toolchain/innosetup-6.7.3/ISCC.exe'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root "artifacts/installer-services/$([guid]::NewGuid().ToString('N'))"
$packaging = Join-Path $run 'packaging'
New-Item -ItemType Directory -Path $packaging, (Join-Path $run 'publish'), (Join-Path $run 'src/WinMon/Assets') -Force | Out-Null
Copy-Item (Join-Path $root 'LICENSE') $run
Copy-Item (Join-Path $root 'src/WinMon/Assets/WinMon.ico') (Join-Path $run 'src/WinMon/Assets')
Set-Content (Join-Path $run 'publish/WinMon.exe') 'Compile-only fixture; never executed.'
Set-Content (Join-Path $run 'publish/WinMon.Cli.exe') 'Compile-only CLI fixture; never executed.'
Set-Content (Join-Path $run 'publish/WinMon.Mcp.exe') 'Compile-only MCP fixture; never executed.'
$pawn = Join-Path $run 'PawnIO_setup.exe'
Set-Content $pawn 'Compile-only PawnIO fixture; never executed.'
$hash = (Get-FileHash $pawn -Algorithm SHA256).Hash
$source = Get-Content (Join-Path $root 'packaging/WinMon.iss') -Raw
$script = Join-Path $packaging 'WinMon.iss'
Set-Content $script $source -Encoding utf8NoBOM
[xml]$strings = Get-Content (Join-Path $root 'src/WinMon/Resources/Strings.resx') -Raw
$task = [string](($strings.root.data | Where-Object name -ceq 'InstallerSensorServiceTask').value)
$failure = [string](($strings.root.data | Where-Object name -ceq 'InstallerSensorServiceSetupFailed').value)
$common = @('/DAppVersion=5.0.0', "/DPublishDirectory=$run/publish", '/DDesktopDirectoryConflict=Conflict',
    '/DDowngradeBlocked=Downgrade', "/DPawnIoInstaller=$pawn", "/DPawnIoSha256=$hash",
    "/DSensorServiceTask=$task", "/DSensorServiceSetupFailed=$failure")
foreach ($flags in @(@(0, 0), @(1, 0), @(0, 1), @(1, 1))) {
    $output = Join-Path $run "compile-$($flags[0])-$($flags[1])"
    & $InnoCompiler @common "/DReleaseDirectory=$output" "/DNetworkServiceSetup=$($flags[0])" "/DSensorServiceSetup=$($flags[1])" $script > (Join-Path $run "compile-$($flags[0])-$($flags[1]).log")
    if ($LASTEXITCODE -ne 0) { throw "Installer compile failed for service flags $flags. See $run" }
}

$stubs = @'
var
  SensorSelected, NetworkSelected, InstalledBefore, RegisteredAfter, LaunchSucceeds, BadHash: Boolean;
  PawnExitCode, NetworkExitCode, SensorExitCode, FailureMessages: Integer;
  Calls: String;

function TestTaskSelected(const Name: String): Boolean;
begin
  if Name = 'sensorservice' then Result := SensorSelected else Result := NetworkSelected;
end;

function TestPawnIoInstalled: Boolean;
begin
  Result := InstalledBefore or ((Pos('P', Calls) > 0) and RegisteredAfter);
end;

function TestHash(const Path: String): String;
begin
  if BadHash then Result := 'bad' else Result := GetSHA256OfFile(Path);
end;

function TestShellExec(const Verb, Filename, Params, WorkingDir: String; const ShowCmd: Integer;
  const Wait: TExecWait; var ErrorCode: Integer): Boolean;
begin
  if (Verb <> 'runas') or (Params <> '-install -silent') or (ShowCmd <> SW_HIDE) or
    (Wait <> ewWaitUntilTerminated) or (ExtractFileName(Filename) <> 'PawnIO_setup.exe') then
    RaiseException('Incorrect PawnIO invocation');
  Calls := Calls + 'P';
  ErrorCode := PawnExitCode;
  Result := LaunchSucceeds;
end;

function TestExec(const Filename, Params, WorkingDir: String; const ShowCmd: Integer;
  const Wait: TExecWait; var ErrorCode: Integer): Boolean;
begin
  if Params = '--install-network-service' then begin
    Calls := Calls + 'N'; ErrorCode := NetworkExitCode;
  end else if Params = '--install-sensor-service' then begin
    if not TestPawnIoInstalled then RaiseException('Sensor service ran without PawnIO');
    Calls := Calls + 'S'; ErrorCode := SensorExitCode;
  end else RaiseException('Unexpected application invocation');
  Result := True;
end;

function TestCustomMessage(const Name: String): String;
begin
  FailureMessages := FailureMessages + 1;
  Result := CustomMessage(Name);
end;
'@
$harness = $source.Replace('{app}', '{tmp}').Replace('WizardIsTaskSelected(', 'TestTaskSelected(').
    Replace('ExecAsOriginalUser(', 'TestExec(').Replace('if IsPawnIoInstalled then', 'if TestPawnIoInstalled then').
    Replace('and IsPawnIoInstalled;', 'and TestPawnIoInstalled;').Replace('GetSHA256OfFile(Path)', 'TestHash(Path)').
    Replace("ShellExec('runas'", "TestShellExec('runas'").Replace("Log(CustomMessage(", "Log(TestCustomMessage(")
$harness = $harness.Replace('[Code]', "[Code]`r`n$stubs")
$evidence = Join-Path $run 'orchestration-passed.txt'
$checks = @'

procedure Check(const Name, ExpectedCalls: String; ExpectedRestart: Boolean; ExpectedFailures: Integer);
begin
  CurStepChanged(ssPostInstall);
  if (Calls <> ExpectedCalls) or (NeedRestart <> ExpectedRestart) or
    (FailureMessages <> ExpectedFailures) then
    RaiseException(Name + ': calls=' + Calls + ', failures=' + IntToStr(FailureMessages));
  Log('PASS ' + Name);
end;

procedure Reset;
begin
  SensorSelected := True; NetworkSelected := False; InstalledBefore := False;
  RegisteredAfter := True; LaunchSucceeds := True; BadHash := False;
  PawnExitCode := 0; NetworkExitCode := 0; SensorExitCode := 0;
  FailureMessages := 0; Calls := ''; PawnIoRestartRequired := False;
end;

function InitializeSetup: Boolean;
begin
  Reset; Check('MissingPawnIoInstallsBeforeSensors', 'PS', False, 0);
  Reset; InstalledBefore := True; Check('ExistingPawnIoIsPreserved', 'S', False, 0);
  Reset; NetworkSelected := True; Check('BothServicesRunIndependently', 'NPS', False, 0);
  Reset; NetworkSelected := True; SensorSelected := False; Check('NetworkOnlyDoesNotInstallPawnIo', 'N', False, 0);
  Reset; SensorSelected := False; Check('UncheckedOptionsDoNothing', '', False, 0);
  Reset; LaunchSucceeds := False; PawnExitCode := 1223; Check('DeclinedPawnIoBlocksSensors', 'P', False, 1);
  Reset; PawnExitCode := 5; Check('PawnIoFailureBlocksSensors', 'P', False, 1);
  Reset; BadHash := True; Check('HashMismatchBlocksExecution', '', False, 1);
  Reset; RegisteredAfter := False; Check('MissingRegistrationBlocksSensors', 'P', False, 1);
  Reset; PawnExitCode := 3010; Check('RebootRequiredStillInstallsSensors', 'PS', True, 0);
  Reset; PawnExitCode := 3010; RegisteredAfter := False; Check('RebootWithoutRegistrationDefersSensors', 'P', True, 1);
  Reset; NetworkSelected := True; NetworkExitCode := 1; Check('NetworkFailureDoesNotSkipSensors', 'NPS', False, 1);
  Reset; SensorExitCode := 1; Check('SensorFailureIsReported', 'PS', False, 1);
  SaveStringToFile('__EVIDENCE__', '13 orchestration scenarios passed', False);
  Result := False; { Exit before any real installation starts. }
end;
'@
$harness += $checks.Replace('__EVIDENCE__', $evidence.Replace("'", "''"))
Set-Content $script $harness -Encoding utf8NoBOM
$harnessOutput = Join-Path $run 'harness'
& $InnoCompiler @common "/DReleaseDirectory=$harnessOutput" '/DNetworkServiceSetup=1' '/DSensorServiceSetup=1' $script > (Join-Path $run 'harness-compile.log')
if ($LASTEXITCODE -ne 0) { throw "Harness compile failed. See $run" }
$process = Start-Process (Join-Path $harnessOutput 'WinMon-5.0.0-Setup-x64.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$run/harness.log`"" -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Installer harness timed out.' }
if (!(Test-Path $evidence)) { throw "Installer orchestration failed. See $run/harness.log" }
Write-Host "PASS installer compilation (four combinations) and 13 orchestration scenarios: $run"
