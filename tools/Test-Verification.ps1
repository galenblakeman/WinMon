#requires -Version 7.4
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestResults.ps1')

function Assert-Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "Verification self-check failed: $Message" }
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$directory = Join-Path $temporaryRoot ('WinMon.Verification-' + [guid]::NewGuid().ToString('N'))
New-Item $directory -ItemType Directory | Out-Null
try {
    $path = Join-Path $directory 'MonCore.Tests.trx'
    Assert-Check ($null -eq (Get-TestSummary $path)) 'A missing report must fail.'
    function Write-Report([string]$Results, [int]$Passed, [int]$Failed = 0, [int]$Skipped = 0, [string]$Outcome = 'Completed') {
        Set-Content $path @"
<TestRun><Results>$Results</Results><ResultSummary outcome="$Outcome"><Counters total="$($Passed + $Failed + $Skipped)" executed="$($Passed + $Failed)" passed="$Passed" failed="$Failed" /></ResultSummary></TestRun>
"@
    }
    $pass = '<UnitTestResult testName="Works" outcome="Passed" />'
    Write-Report $pass 1
    Assert-Check ((Get-TestSummary $path).Passed) 'A passing report must pass.'
    Write-Report '<UnitTestResult testName="Broken" outcome="Failed" />' 0 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'A failed assertion must fail.'
    Write-Report $pass 1 -Outcome 'Aborted'
    Assert-Check (!(Get-TestSummary $path).Passed) 'An aborted run must fail even with passing counters.'
    Write-Report $pass 2
    Assert-Check (!(Get-TestSummary $path).Passed) 'Missing test results must fail.'
    $skip = '<UnitTestResult testName="Fixture_Write" outcome="NotExecuted"><Output><ErrorInfo><Message>Writes the format fixture into the source tree.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    $summary = Get-TestSummary $path
    Assert-Check ($summary.Passed -and $summary.Executed -eq 1 -and $summary.Detail.Contains('not-executed=1')) 'Known Explicit skips must be reported from individual results.'
    Write-Report ($pass + $skip.Replace('Fixture_Write', 'NewlyIgnoredTest')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'A new ignored test must fail.'
    Write-Report ($pass + $skip.Replace('source tree.', 'unexpected reason.')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'An altered skip reason must fail.'
    Write-Report $skip 0 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Executed -eq 0) 'Skip-only reports must not count as executed tests.'
    Write-Report '' 0
    Assert-Check ((Get-TestSummary $path).Executed -eq 0) 'Empty filtered projects must not count as executed tests.'
    Set-Content $path '<TestRun />'
    Assert-Check ($null -eq (Get-TestSummary $path)) 'A report without counters must fail.'
    Set-Content $path '<TestRun><ResultSummary outcome="Completed"><Counters /></ResultSummary></TestRun>'
    Assert-Check ($null -eq (Get-TestSummary $path)) 'Incomplete counters must fail.'
    Set-Content $path '<TestRun>'
    $rejected = $false
    try { $null = Get-TestSummary $path } catch { $rejected = $true }
    Assert-Check $rejected 'Malformed XML must fail.'

    $path = Join-Path $directory 'WinMon.Cli.Tests.trx'
    $skip = '<UnitTestResult testName="StoreBuild_RejectsBeforeContactingGitHub" outcome="NotExecuted"><Output><ErrorInfo><Message>This assertion only applies to the Store build.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Passed) 'Desktop must allow the Store-only test skip.'
    Assert-Check (!(Get-TestSummary $path $true).Passed) 'Store must execute its Store-only test.'

    $path = Join-Path $directory 'WinMon.NetworkSetup.Tests.trx'
    $skip = '<UnitTestResult testName="Install_CommitsProtectedFilesPolicyAndRegistration" outcome="NotExecuted"><Output><ErrorInfo><Message>OneTimeSetUp: Runs isolated protected-folder setup transactions with simulated OS service boundaries.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Passed) 'Fixture-level Explicit reasons must match the adapter OneTimeSetUp prefix.'
    Write-Report ($pass + $skip.Replace('OneTimeSetUp: ', '')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'Changed fixture skip reasons must still fail.'

    $skip = '<UnitTestResult testName="InstalledWinMonConfigurationIsAcceptedWithoutTakingOwnershipOfItsQueryHandle" outcome="NotExecuted"><Output><ErrorInfo><Message>Requires the installed WinMon network service and permission to query its configuration.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Passed) 'The installed-service query prerequisite must be reported explicitly.'
    Write-Report ($pass + $skip.Replace('permission to query its configuration.', 'an unexpected prerequisite.')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'A changed installed-service query prerequisite must fail.'

    $skip = '<UnitTestResult testName="InstalledRegistrationCaptureReturnsIndependentAllowedValuesWithoutChangingTheKey" outcome="NotExecuted"><Output><ErrorInfo><Message>Requires the existing trusted WinMon network-service uninstall registration and read permission.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Passed) 'The installed-registration prerequisite must be reported explicitly.'
    Write-Report ($pass + $skip.Replace('and read permission.', 'and an unexpected prerequisite.')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'A changed installed-registration prerequisite must fail.'

    foreach ($case in @('TrustedInstalledTreeIsCanonicalUniqueAndUnchanged',
            'InstalledInventoryMatchesProtectedFileSetWhenStrict', 'InstalledInventoryMatchesProtectedFileSetWhenRepairIsAllowed')) {
        $reason = 'Requires the protected WinMon network installation and read permission.'
        if ($case -ne 'TrustedInstalledTreeIsCanonicalUniqueAndUnchanged') { $reason = 'OneTimeSetUp: ' + $reason }
        $skip = "<UnitTestResult testName=`"$case`" outcome=`"NotExecuted`"><Output><ErrorInfo><Message>$reason</Message></ErrorInfo></Output></UnitTestResult>"
        Write-Report ($pass + $skip) 1 -Skipped 1
        Assert-Check ((Get-TestSummary $path).Passed) 'Installed filesystem prerequisites must match their exact case names and reasons.'
        Write-Report ($pass + $skip.Replace('and read permission.', 'and an unexpected prerequisite.')) 1 -Skipped 1
        Assert-Check (!(Get-TestSummary $path).Passed) 'Changed installed filesystem prerequisites must fail.'
    }

    $path = Join-Path $directory 'WinMon.Tests.trx'
    $skip = '<UnitTestResult testName="AvailableNativeDriveDiscoveryPreservesOrderNamesLabelsAndReturnsFreshChoices" outcome="NotExecuted"><Output><ErrorInfo><Message>Requires stable native logical drives with readable metadata and at least one ready drive.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Passed) 'The native-drive prerequisite must be reported explicitly.'
    Write-Report ($pass + $skip.Replace('readable metadata', 'an unexpected environment')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'A changed native-drive prerequisite must fail.'

    $path = Join-Path $directory 'MonCore.Windows.Tests.trx'
    $skip = '<UnitTestResult testName="InstalledServicePipeIdentityAndScmPidAgreeWithoutProtocolRequests" outcome="NotExecuted"><Output><ErrorInfo><Message>Requires the running protected WinMon network service and pipe access.</Message></ErrorInfo></Output></UnitTestResult>'
    Write-Report ($pass + $skip) 1 -Skipped 1
    Assert-Check ((Get-TestSummary $path).Passed) 'The native pipe identity prerequisite must match the exact test and reason.'
    Write-Report ($pass + $skip.Replace('and pipe access.', 'and an unexpected prerequisite.')) 1 -Skipped 1
    Assert-Check (!(Get-TestSummary $path).Passed) 'A changed native pipe identity prerequisite must fail.'

    New-Item (Join-Path $directory 'tests/First.Tests') -ItemType Directory -Force | Out-Null
    Set-Content (Join-Path $directory 'tests/First.Tests/First.Tests.csproj') '<Project />'
    Set-Content (Join-Path $directory 'WinMon.sln') 'Project("{test}") = "First.Tests", "tests\First.Tests\First.Tests.csproj", "{first}"'
    Assert-Check (@(Get-VerificationProjects $directory).Count -eq 1) 'Every discovered test project must be selected.'
    New-Item (Join-Path $directory 'tests/Second.Tests') -ItemType Directory | Out-Null
    Set-Content (Join-Path $directory 'tests/Second.Tests/Second.Tests.csproj') '<Project />'
    $rejected = $false
    try { $null = Get-VerificationProjects $directory } catch { $rejected = $true }
    Assert-Check $rejected 'A test project omitted from the solution must fail.'
    Add-Content (Join-Path $directory 'WinMon.sln') '# "tests\Second.Tests\Second.Tests.csproj"'
    $rejected = $false
    try { $null = Get-VerificationProjects $directory } catch { $rejected = $true }
    Assert-Check $rejected 'A path mentioned in a comment must not count as a solution project.'
    Add-Content (Join-Path $directory 'WinMon.sln') 'Project("{test}") = "Second.Tests", "tests\Second.Tests\Second.Tests.csproj", "{second}"'
    Assert-Check (@(Get-VerificationProjects $directory).Count -eq 2) 'New test projects must join verification automatically.'
    Write-Host 'PASS verification self-check'
}
finally {
    if (![IO.Path]::GetFullPath($directory).StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a self-check directory outside the temporary root.'
    }
    Remove-Item -LiteralPath $directory -Recurse -Force
}
