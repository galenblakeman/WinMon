# Shared by Verify.ps1 and its dependency-free self-check.
function Get-VerificationProjects([string]$Root) {
    $projects = @(Get-ChildItem (Join-Path $Root 'tests') -Recurse -Filter *.Tests.csproj -File | Sort-Object FullName)
    if (!$projects.Count) { throw 'No test projects found.' }
    $solution = Get-Content (Join-Path $Root 'WinMon.sln') -Raw
    foreach ($project in $projects) {
        $relative = [IO.Path]::GetRelativePath($Root, $project.FullName).Replace('/', '\')
        $entry = '(?m)^Project\("[^"]+"\) = "[^"]+", "' + [regex]::Escape($relative) + '", "'
        if ($solution -notmatch $entry) { throw "Test project is missing from WinMon.sln: $relative" }
    }
    return $projects
}

function Get-TestSummary([string]$Path, [bool]$Store = $false) {
    if (!(Test-Path -LiteralPath $Path)) { return $null }
    [xml]$report = Get-Content -LiteralPath $Path -Raw
    $counters = $report.TestRun.ResultSummary.Counters
    if ($counters -isnot [System.Xml.XmlElement]) { return $null }
    foreach ($name in @('total', 'executed', 'passed', 'failed')) {
        if ($counters.GetAttribute($name) -notmatch '^\d+$') { return $null }
    }
    $expectedSkips = @{
        'MonCore.Tests/Fixture_Write' = 'Writes the format fixture into the source tree.'
        'WinMon.NetworkService.Tests/Benchmark_FlowAggregationWritesComparableMeasurements' = 'Records bounded endpoint-flow aggregation overhead for the performance history'
        'WinMon.NetworkService.Tests/Collector_AccountsForControlledWorkerAndStopsItsSession' = 'Runs a bounded elevated ETW experiment on an approved Windows test machine'
        'WinMon.NetworkSetup.Tests/ElevatedSecureFactories_ProtectNewStagingChildrenAndLockImmediately' = 'Requires elevation to assign the protected Administrators owner.'
        'WinMon.NetworkSetup.Tests/RecoveryValidatesProtectedTreeBeforeAcceptingMissingFiles' = 'Requires elevation to assign protected owners and permissions.'
        'WinMon.Mcp.Tests/MeasurePublishedServer' = 'Measures a published server for about four minutes; requires dotnet-counters, dotnet-gcdump and WINMON_MCP_TEST_EXE.'
        'WinMon.Tests/AvailableFreeSpace_ReadsNetworkShareWhenAvailable' = 'No administrative share is available to read through UNC.'
        'MonCore.Windows.Tests/VerifyPublisher_ExtractsAuthenticodeCertificateBeforeRejectingAnotherPublisher' = 'The signed .NET host is not installed at the standard path.'
        'WinMon.Cli.Tests/DevelopmentOrStoreBuildCannotLaunchSetupWithoutSignedBundle' = 'Missing-bundle check is only applicable to builds without setup assets.'
    }
    if (!$Store) {
        $expectedSkips['WinMon.Cli.Tests/StoreBuild_RejectsBeforeContactingGitHub'] = 'This assertion only applies to the Store build.'
    }
    else {
        foreach ($name in @('Check_Cancellation_ReturnsFailureWithoutMutation', 'Check_NewSignedCliOffer_ReportsItWithoutWritingFiles',
                'Check_RejectsRedirectOutsideGitHubOrigins', 'Check_RejectsTamperedSignatureAndProductMismatch',
                'Check_StaleRelease_ReportsCurrentVersion', 'Install_CorruptPayload_DoesNotLaunchAndCleansStaging',
                'Install_InstalledCopy_VerifiesAndLaunchesMatchingInstaller', 'Install_OversizedChunkedPayload_DoesNotLaunchOrRetainIt',
                'Install_PortableCopy_VerifiesMatchingArchiveWithoutLaunchingInstaller', 'Install_StalledPayload_TimesOutWithoutLaunching')) {
            $expectedSkips["WinMon.Cli.Tests/$name"] = 'GitHub updates are disabled for Microsoft Store builds.'
        }
    }
    $project = [IO.Path]::GetFileNameWithoutExtension($Path)
    $tests = @($report.TestRun.Results.UnitTestResult | Where-Object { $null -ne $_ })
    $notExecuted = @($tests | Where-Object outcome -eq 'NotExecuted')
    $unexpected = @($tests | Where-Object { $_.outcome -notin @('Passed', 'Failed', 'NotExecuted') })
    $reasons = @($notExecuted | ForEach-Object {
        $reason = $_.Output.ErrorInfo.Message
        if (!$reason) { $reason = $_.Output.StdOut }
        $key = "$project/$($_.testName)"
        if (!$expectedSkips.ContainsKey($key) -or $expectedSkips[$key] -cne $reason) { $unexpected += $_ }
        "$($_.testName): $reason"
    }) -join ' | '
    $executed = @($tests | Where-Object { $_.outcome -in @('Passed', 'Failed') }).Count
    $passed = @($tests | Where-Object outcome -eq 'Passed').Count
    $failed = @($tests | Where-Object outcome -eq 'Failed').Count
    [pscustomobject]@{
        Passed = $report.TestRun.ResultSummary.outcome -in @('Completed', 'Passed') -and
            $failed -eq 0 -and $unexpected.Count -eq 0 -and
            $passed -eq [int]$counters.passed -and $failed -eq [int]$counters.failed -and
            $executed -eq [int]$counters.executed -and $tests.Count -eq [int]$counters.total
        Executed = $executed
        Detail = "passed=$passed failed=$failed not-executed=$($notExecuted.Count) unexpected=$($unexpected.Count)$(if ($reasons) { "; $reasons" })"
    }
}
