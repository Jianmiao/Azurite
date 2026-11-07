$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modulePath = Join-Path $repositoryRoot 'tools\headless\HeadlessCore.psm1'
$modulePath = [IO.Path]::GetFullPath($modulePath)
Import-Module -Name $modulePath -Force

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message. Expected '$Expected', got '$Actual'." }
}
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $threw = $false
    try { & $Action } catch { $threw = $true }
    if (-not $threw) { throw "Expected refusal: $Message" }
}

$logic = @(Get-AzuriteArguments -Mode Logic)
$graphics = @(Get-AzuriteArguments -Mode Graphics)
Assert-Equal '-batchmode|-nographics|-logFile|-' ($logic -join '|') 'Logic arguments'
Assert-Equal '-batchmode|-logFile|-' ($graphics -join '|') 'Graphics arguments'
Write-Output 'PASS: headless modes use distinct Unity arguments'

$fixtureRoot = Join-Path $repositoryRoot ('artifacts\headless-tests\' + [guid]::NewGuid().ToString('N'))
$labRoot = Join-Path $fixtureRoot 'lab'
$dailyRoot = Join-Path $fixtureRoot 'daily'
New-Item -ItemType Directory -Path $labRoot, $dailyRoot -Force | Out-Null
try {
    Assert-Throws { Assert-AzuriteLabPaths -LabRoot $labRoot -InstallPath $dailyRoot -DailyInstallPath $dailyRoot | Out-Null } 'daily install cannot be the lab install'
    Assert-Throws { Assert-AzuriteLabPaths -LabRoot 'relative\lab' -InstallPath $labRoot -DailyInstallPath $dailyRoot | Out-Null } 'relative lab path'
    $install = Join-Path $labRoot 'install'
    New-Item -ItemType Directory -Path $install | Out-Null
    Assert-Equal $install (Assert-AzuriteLabPaths -LabRoot $labRoot -InstallPath $install -DailyInstallPath $dailyRoot) 'isolated install path'
    Write-Output 'PASS: paths reject relative and daily-state overlap'

    $junction = Join-Path $labRoot 'linked-install'
    New-Item -ItemType Junction -Path $junction -Target $dailyRoot | Out-Null
    Assert-Throws { Assert-AzuriteLabPaths -LabRoot $labRoot -InstallPath $junction -DailyInstallPath $dailyRoot | Out-Null } 'reparse link to daily install'
    Remove-Item -LiteralPath $junction -Force
    Write-Output 'PASS: reparse link to daily state is refused'

    $pwsh = (Get-Command pwsh).Source
    $resultPath = Join-Path $labRoot 'result.json'
    $runId = [guid]::NewGuid().ToString('N')
    $good = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'Success') -WorkingDirectory $labRoot -RunId $runId -ResultPath $resultPath -TimeoutSeconds 10
    Assert-Equal $true $good.ResultValid 'fresh matching result'
    Assert-Equal $false $good.TimedOut 'successful child timeout status'
    Assert-Equal 0 $good.ExitCode 'successful child exit'
    if ($good.Stdout -notmatch 'fixture stdout' -or $good.Stderr -notmatch 'fixture stderr') { throw 'Both redirected streams must be captured.' }
    Write-Output 'PASS: fixture child writes a fresh PID/run-correlated result and both streams are captured'

    $flood = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'Flood') -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'flood.json') -TimeoutSeconds 10
    if (-not $flood.ResultValid -or $flood.Stdout.Length -lt 131072 -or $flood.Stderr.Length -lt 131072) { throw 'Concurrent stream drainage failed under pipe pressure.' }
    Write-Output 'PASS: both redirected streams drain under pipe pressure'

    $badRunId = [guid]::NewGuid().ToString('N')
    $bad = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'WrongPid') -WorkingDirectory $labRoot -RunId $badRunId -ResultPath (Join-Path $labRoot 'bad-result.json') -TimeoutSeconds 10
    Assert-Equal $false $bad.ResultValid 'wrong PID result refusal'
    Write-Output 'PASS: result with wrong PID is refused'

    $nativeExpected = @{ Mode = 'Logic'; Scenario = 'logic-smoke'; AccountSid = 'S-1-5-21-1-2-3-1001' }
    $nativeEnvironment = @{ AZURITE_MODE = 'Logic'; AZURITE_SCENARIO = 'logic-smoke'; AZURITE_ACCOUNT_SID = $nativeExpected.AccountSid }
    $native = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'NativeSuccess') -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'native.json') -TimeoutSeconds 10 -ChildEnvironment $nativeEnvironment -NativeExpectation $nativeExpected
    Assert-Equal $true $native.ResultValid 'named native checks'
    foreach ($case in @(@{ behavior = 'NativeMissingChecks'; path = 'missing-checks.json' }, @{ behavior = 'NativeWrongMode'; path = 'wrong-mode.json' }, @{ behavior = 'NativeStringPid'; path = 'string-pid.json' })) {
        $result = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', $case.behavior) -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot $case.path) -TimeoutSeconds 10 -ChildEnvironment $nativeEnvironment -NativeExpectation $nativeExpected
        Assert-Equal $false $result.ResultValid $case.behavior
    }
    Write-Output 'PASS: native result requires expected mode/scenario/SID and named passing checks'

    $graphicsExpected = @{ Mode = 'Graphics'; Scenario = 'graphics-frame'; AccountSid = $nativeExpected.AccountSid }
    $graphicsEnvironment = @{ AZURITE_MODE = 'Graphics'; AZURITE_SCENARIO = 'graphics-frame'; AZURITE_ACCOUNT_SID = $graphicsExpected.AccountSid }
    $graphics = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'NativeGraphics') -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'graphics.json') -TimeoutSeconds 10 -ChildEnvironment $graphicsEnvironment -NativeExpectation $graphicsExpected
    Assert-Equal $true $graphics.ResultValid 'nonempty Graphics frame artifact'
    $graphicsBad = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'NativeGraphicsBadHash') -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'graphics-bad.json') -TimeoutSeconds 10 -ChildEnvironment $graphicsEnvironment -NativeExpectation $graphicsExpected
    Assert-Equal $false $graphicsBad.ResultValid 'Graphics frame hash mismatch'
    $graphicsStale = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'NativeGraphicsStaleFrame') -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'graphics-stale.json') -TimeoutSeconds 10 -ChildEnvironment $graphicsEnvironment -NativeExpectation $graphicsExpected
    Assert-Equal $false $graphicsStale.ResultValid 'stale Graphics frame'
    foreach ($case in @(@{ behavior = 'NativeGraphicsTruncated'; path = 'graphics-truncated.json' }, @{ behavior = 'NativeGraphicsCorrupt'; path = 'graphics-corrupt.json' })) {
        $malformed = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', $case.behavior) -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot $case.path) -TimeoutSeconds 10 -ChildEnvironment $graphicsEnvironment -NativeExpectation $graphicsExpected
        Assert-Equal $false $malformed.ResultValid $case.behavior
    }
    Write-Output 'PASS: Graphics acceptance requires a real nonempty frame with matching size and hash'

    $fixturePassword = ConvertTo-SecureString -String 'fixture-only-value' -AsPlainText -Force
    $fakeCredential = [pscredential]::new('AzuriteTest', $fixturePassword)
    $credentialedPlan = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1')) -WorkingDirectory $labRoot -RunId 'fixture-credentialed' -ResultPath (Join-Path $labRoot 'credentialed-plan.json') -TimeoutSeconds 10 -Credential $fakeCredential -UseArgumentMetadata -PlanOnly
    Assert-Equal 'TargetProfile' $credentialedPlan.EnvironmentSource 'credentialed child environment source'
    Assert-Equal 0 $credentialedPlan.ExplicitEnvironmentCount 'credentialed child explicit environment count'
    if (($credentialedPlan.Arguments -join '|') -notmatch 'AzuriteRunId\|fixture-credentialed' -or
        ($credentialedPlan.Arguments -join '|') -notmatch 'AzuriteResultPath') { throw 'Credentialed child lacks public run metadata arguments.' }
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'credentialed-plan.json')) 'credentialed plan must not start child'
    Write-Output 'PASS: credentialed child plan uses target profile environment and public argument metadata'

    $unrelatedStart = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $unrelatedStart.UseShellExecute = $false
    $unrelatedStart.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'Sleep')) { [void]$unrelatedStart.ArgumentList.Add($argument) }
    $unrelated = [Diagnostics.Process]::Start($unrelatedStart)
    try {
        $timeoutRunId = [guid]::NewGuid().ToString('N')
        $timed = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'Sleep') -WorkingDirectory $labRoot -RunId $timeoutRunId -ResultPath (Join-Path $labRoot 'timeout-result.json') -TimeoutSeconds 1
        Assert-Equal $true $timed.TimedOut 'timeout status'
        Assert-Equal $false $timed.ResultValid 'timeout has no accepted result'
        if (Get-Process -Id $timed.Pid -ErrorAction SilentlyContinue) { throw 'Timed-out owned child remains alive.' }
        if ($unrelated.HasExited) { throw 'Timeout cleanup terminated an unrelated child.' }
    } finally {
        if (-not $unrelated.HasExited) { $unrelated.Kill(); [void]$unrelated.WaitForExit(5000) }
        $unrelated.Dispose()
    }
    Write-Output 'PASS: timeout kills only its owned child'

    $helperPidPath = Join-Path $labRoot 'orphan-helper.pid'
    $orphanWatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $orphan = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fixture-Child.ps1'), '-Behavior', 'OrphanPipe', '-HelperPidPath', $helperPidPath) -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'orphan-result.json') -TimeoutSeconds 2
        Assert-Equal $true $orphan.TimedOut 'inherited pipe timeout status'
        if ($orphanWatch.Elapsed.TotalSeconds -ge 5) { throw 'Inherited pipe timeout exceeded the bound.' }
        $helperPid = [int](Get-Content -LiteralPath $helperPidPath -Raw)
        if (-not (Get-Process -Id $helperPid -ErrorAction SilentlyContinue)) { throw 'Unrelated pipe holder was terminated.' }
    } finally {
        if (Test-Path -LiteralPath $helperPidPath -PathType Leaf) {
            $helperPid = [int](Get-Content -LiteralPath $helperPidPath -Raw)
            Stop-Process -Id $helperPid -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Output 'PASS: inherited output handle cannot defeat timeout or kill unrelated helper'

    $identityRunId = [guid]::NewGuid().ToString('N')
    $identity = Invoke-AzuriteChild -FilePath $pwsh -ArgumentList @('-NoProfile', '-NonInteractive', '-File', (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1')) -WorkingDirectory $labRoot -RunId $identityRunId -ResultPath (Join-Path $labRoot 'identity.json') -TimeoutSeconds 10 -UseArgumentMetadata
    Assert-Equal $true $identity.ResultValid 'identity child result'
    Assert-Equal ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) $identity.Result.identitySid 'identity child SID'
    Assert-Equal $false $identity.Result.isAdministrator 'fixture child privilege'
    if ([string]::IsNullOrWhiteSpace($identity.Result.localLow) -or
        -not $identity.Result.localLow.StartsWith($identity.Result.userProfile.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Child LocalLow must belong to its profile.'
    }
    Assert-Equal $true $identity.Result.hiveLoaded 'child HKCU hive'
    Assert-Equal $identity.Result.userProfile $identity.Result.environmentProfile 'child USERPROFILE environment'
    Assert-Equal $identity.Result.localAppData $identity.Result.environmentLocalAppData 'child LOCALAPPDATA environment'
    Write-Output 'PASS: harmless child reports actual SID, profile and privilege'

    $stagedFixture = Join-Path $labRoot 'tooling\Identity-Preflight.ps1'
    New-Item -ItemType Directory -Path (Split-Path -Parent $stagedFixture) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1') -Destination $stagedFixture
    $winPowerShell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $stagedIdentity = Invoke-AzuriteChild -FilePath $winPowerShell -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $stagedFixture) -WorkingDirectory $labRoot -RunId ([guid]::NewGuid().ToString('N')) -ResultPath (Join-Path $labRoot 'staged-identity.json') -TimeoutSeconds 10 -UseArgumentMetadata
    Assert-Equal $true $stagedIdentity.ResultValid "staged Windows PowerShell preflight exit=$($stagedIdentity.ExitCode) stderr=$($stagedIdentity.Stderr)"
    Assert-Equal 0 @($stagedIdentity.Result.unexpectedPrivileges).Count 'preflight unexpected token privileges'
    $fixtureControllerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Set-AzuriteLabAcl -LabRoot $labRoot -Path (Split-Path -Parent $stagedFixture) -ControllerSid $fixtureControllerSid
    Set-AzuriteLabAcl -LabRoot $labRoot -Path $stagedFixture -ControllerSid $fixtureControllerSid
    Write-Output 'PASS: staged preflight works with machine-wide Windows PowerShell and argument metadata'

    $setup = Join-Path $repositoryRoot 'tools\headless\Initialize-AzuriteTestAccount.ps1'
    $marker = [ordered]@{ schema = 1; purpose = 'AzuriteHeadlessLab'; labId = [guid]::NewGuid().ToString(); controllerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value; dailyInstallPath = $dailyRoot; createdUtc = [datetime]::UtcNow.ToString('o') }
    $marker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $labRoot 'lab.marker.json') -Encoding utf8
    $ownerDescription = Get-AzuriteAccountDescription -OwnerToken '01234567-89ab-cdef-0123-456789abcdef'
    $limit = (Get-Command New-LocalUser).Parameters['Description'].Attributes |
        Where-Object { $_ -is [System.Management.Automation.ValidateLengthAttribute] } | Select-Object -First 1
    if ($ownerDescription.Length -gt $limit.MaxLength) { throw 'Ownership description exceeds New-LocalUser metadata limit.' }
    New-LocalUser -Name 'AzuriteTestWhatIf' -NoPassword -Description $ownerDescription -WhatIf | Out-Null
    $usersGroup = Get-LocalGroup -SID 'S-1-5-32-545'
    Add-LocalGroupMember -Group $usersGroup.Name -Member $marker.controllerSid -WhatIf -ErrorAction Stop | Out-Null
    Write-Output 'PASS: manifest ownership description binds to real New-LocalUser WhatIf metadata'

    $fixtureSecret = ConvertTo-SecureString -String 'fixture-only-cryptographic-payload' -AsPlainText -Force
    $sealedForController = ConvertFrom-SecureString -SecureString $fixtureSecret
    $restoredSecret = ConvertTo-SecureString -String $sealedForController
    if ($null -eq $restoredSecret -or [string]::IsNullOrWhiteSpace($sealedForController)) { throw 'CurrentUser DPAPI did not restore a secure password.' }
    $restoredSecret.Dispose()
    $credentialDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sealedForController)))
    $committedManifest = [pscustomobject]@{ credentialSha256 = $credentialDigest }
    $credentialRecord = [pscustomobject]@{ schema = 1; labId = $marker.labId; controllerSid = $marker.controllerSid; accountSid = 'S-1-5-21-1-2-3-1001'; encryptedPassword = $sealedForController }
    Assert-Equal $true (Test-AzuriteCommittedCredential -Manifest $committedManifest -CredentialRecord $credentialRecord -LabId $marker.labId -ControllerSid $marker.controllerSid -AccountSid $credentialRecord.accountSid) 'committed credential reuse'
    $committedManifest.credentialSha256 = $null
    Assert-Equal $false (Test-AzuriteCommittedCredential -Manifest $committedManifest -CredentialRecord $credentialRecord -LabId $marker.labId -ControllerSid $marker.controllerSid -AccountSid $credentialRecord.accountSid) 'interrupted credential update cannot be reused'
    Write-Output 'PASS: CurrentUser DPAPI credential round-trips without outputting plaintext'

    $privateFixture = Join-Path $labRoot 'private-acl-fixture'
    New-Item -ItemType Directory -Path $privateFixture | Out-Null
    Set-AzuriteLabAcl -LabRoot $labRoot -Path $privateFixture -ControllerSid $marker.controllerSid
    Assert-AzuritePrivateAcl -Path $privateFixture -ControllerSid $marker.controllerSid
    $privateAcl = Get-Acl -LiteralPath $privateFixture
    Assert-Equal $true $privateAcl.AreAccessRulesProtected 'private fixture ACL inheritance'
    $foreignOwnerAcl = Get-Acl -LiteralPath $privateFixture
    $foreignOwnerAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-21-9-9-9-9'))
    Assert-Throws { Assert-AzuritePrivateAcl -Path $privateFixture -ControllerSid $marker.controllerSid -AclObject $foreignOwnerAcl } 'foreign owner of a private object'
    Write-Output 'PASS: private fixture ACL excludes inherited broad access'

    Remove-Item -LiteralPath $install -Recurse -Force
    $missingInstallPlan = & $setup -LabRoot $labRoot -PlanOnly
    Assert-Equal $false $missingInstallPlan.Ready 'missing installation plan readiness'
    if (($missingInstallPlan.PrerequisiteFailures -join ' ') -notmatch 'install') { throw 'Missing-install prerequisite is not reported.' }
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'account.manifest.json')) 'missing-install plan must not mutate'
    New-Item -ItemType Directory -Path $install -Force | Out-Null
    'fixture' | Set-Content -LiteralPath (Join-Path $install 'AzureArchive.exe')
    'AzuriteAcceptance' | Set-Content -LiteralPath (Join-Path $install 'ActiveProfile.txt')
    $profile = Join-Path $install 'profiles\AzuriteAcceptance'
    New-Item -ItemType Directory -Path (Join-Path $profile 'configs') -Force | Out-Null
    '{}' | Set-Content -LiteralPath (Join-Path $profile 'modconfig.json')
    Write-Output 'PASS: missing install is reported before any setup mutation'

    Assert-AzuriteLowPrivilegeGroups -AccountSid 'S-1-5-21-1-2-3-1001' -MembershipSids @('S-1-5-32-545')
    Assert-Throws { Assert-AzuriteLowPrivilegeGroups -AccountSid 'S-1-5-21-1-2-3-1001' -MembershipSids @('S-1-5-32-545', 'S-1-5-32-551') } 'Backup Operators membership'
    Write-Output 'PASS: nonadministrator privileged group drift is refused'

    $protectedChild = Join-Path $install 'protected-child'
    New-Item -ItemType Directory -Path $protectedChild | Out-Null
    & icacls.exe $protectedChild '/inheritance:r' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create protected ACL fixture.' }
    Assert-Throws { Assert-AzuriteAclTree -Path $install -ControllerSid $marker.controllerSid } 'protected descendant ACL'
    & icacls.exe $protectedChild '/inheritance:e' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore ACL fixture inheritance.' }
    Assert-AzuriteAclTree -Path $install -ControllerSid $marker.controllerSid
    Assert-AzuriteAclTree -Path $install -ControllerSid 'S-1-5-21-9-9-9-9' -TestSid $marker.controllerSid
    $rerunPrerequisites = Get-AzuriteSetupPrerequisites -LabRoot $labRoot -ControllerSid $marker.controllerSid -PreflightSource (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1')
    Assert-Equal $true $rerunPrerequisites.Ready "restored fixture readiness: $($rerunPrerequisites.Failures -join '; ')"
    & icacls.exe $install '/grant' '*S-1-1-0:(M)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create broad-write ACL fixture.' }
    $unsafePrerequisites = Get-AzuriteSetupPrerequisites -LabRoot $labRoot -ControllerSid $marker.controllerSid -PreflightSource (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1')
    Assert-Equal $false $unsafePrerequisites.Ready 'explicit broad write at install root'
    & icacls.exe $install '/remove:g' '*S-1-1-0' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore ACL fixture.' }
    $toolingFixture = Join-Path $labRoot 'tooling'
    & icacls.exe $toolingFixture '/grant' '*S-1-1-0:(M)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create tooling ACL conflict fixture.' }
    $toolingPrerequisites = Get-AzuriteSetupPrerequisites -LabRoot $labRoot -ControllerSid $marker.controllerSid -PreflightSource (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1')
    Assert-Equal $false $toolingPrerequisites.Ready 'tooling ACL conflict before account work'
    & icacls.exe $toolingFixture '/remove:g' '*S-1-1-0' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore tooling ACL fixture.' }
    & icacls.exe $toolingFixture '/grant' '*S-1-5-32-545:(M)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create test-account tooling write fixture.' }
    $writableTooling = Get-AzuriteSetupPrerequisites -LabRoot $labRoot -ControllerSid $marker.controllerSid -TestSid 'S-1-5-32-545' -PreflightSource (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1')
    Assert-Equal $false $writableTooling.Ready 'test account must not own writable tooling'
    & icacls.exe $toolingFixture '/remove:g' '*S-1-5-32-545' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore test-account tooling fixture.' }
    & icacls.exe $stagedFixture '/grant' '*S-1-5-32-545:(M)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create staged-script write fixture.' }
    $writableScript = Get-AzuriteSetupPrerequisites -LabRoot $labRoot -ControllerSid $marker.controllerSid -TestSid 'S-1-5-32-545' -PreflightSource (Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1')
    Assert-Equal $false $writableScript.Ready 'test account must not write staged preflight'
    & icacls.exe $stagedFixture '/remove:g' '*S-1-5-32-545' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore staged-script fixture.' }
    Write-Output 'PASS: protected descendant ACL is found and repaired fixture passes rerun'

    $ownerToken = '01234567-89ab-cdef-0123-456789abcdef'
    $fakeSid = 'S-1-5-21-1-2-3-1001'
    $accountState = [pscustomobject]@{ Account = $null; MembershipSids = @(); Creates = 0; Adds = 0; PasswordSets = 0; InterruptAdd = $true }
    $createAccount = { param($name, $description, $password) $accountState.Creates++; $accountState.Account = [pscustomobject]@{ Name = $name; Description = $description; Enabled = $true; SID = [pscustomobject]@{ Value = $fakeSid } }; return $accountState.Account }.GetNewClosure()
    $readGroups = { param($sid) return $accountState.MembershipSids }.GetNewClosure()
    $addUsers = { param($sid) $accountState.Adds++; if ($accountState.InterruptAdd) { throw 'fixture interruption before Users join' }; $accountState.MembershipSids = @('S-1-5-32-545') }.GetNewClosure()
    $resetPassword = { param($name, $password) $accountState.PasswordSets++ }.GetNewClosure()
    $fixturePassword = ConvertTo-SecureString -String 'fixture-only-password' -AsPlainText -Force
    Assert-Throws { Invoke-AzuriteAccountProvision -OwnerToken $ownerToken -Password $fixturePassword -CreateUser $createAccount -ReadMemberships $readGroups -AddUsersMember $addUsers -ResetPassword $resetPassword | Out-Null } 'interrupted Users join'
    Assert-Equal 1 $accountState.Creates 'new user creation before interruption'
    $accountState.InterruptAdd = $false
    $recovered = Invoke-AzuriteAccountProvision -ExistingAccount $accountState.Account -OwnerToken $ownerToken -Password $fixturePassword -CreateUser $createAccount -ReadMemberships $readGroups -AddUsersMember $addUsers -ResetPassword $resetPassword
    Assert-Equal $fakeSid $recovered.SID.Value 'recovered owned account SID'
    Assert-Equal 1 $accountState.Creates 'recovery must reuse owned user'
    Assert-Equal 2 $accountState.Adds 'recovery completes Users join'
    Assert-Equal 1 $accountState.PasswordSets 'recovery resets owned password'
    $accountState.MembershipSids = @('S-1-5-32-545', 'S-1-5-32-551')
    Assert-Throws { Invoke-AzuriteAccountProvision -ExistingAccount $accountState.Account -OwnerToken $ownerToken -Password $fixturePassword -CreateUser $createAccount -ReadMemberships $readGroups -AddUsersMember $addUsers -ResetPassword $resetPassword | Out-Null } 'privileged drift before password reset'
    Assert-Equal 1 $accountState.PasswordSets 'privileged drift must not reset password'
    $accountState.Account.Description = 'unowned'
    Assert-Throws { Invoke-AzuriteAccountProvision -ExistingAccount $accountState.Account -OwnerToken $ownerToken -Password $fixturePassword -CreateUser $createAccount -ReadMemberships $readGroups -AddUsersMember $addUsers -ResetPassword $resetPassword | Out-Null } 'unexpected existing account'
    $accountState.Account.Description = Get-AzuriteAccountDescription -OwnerToken $ownerToken
    $accountState.MembershipSids = @('S-1-5-32-545')
    $beforeNoOp = $accountState.PasswordSets
    [void](Invoke-AzuriteAccountProvision -ExistingAccount $accountState.Account -OwnerToken $ownerToken -Password $fixturePassword -CreateUser $createAccount -ReadMemberships $readGroups -AddUsersMember $addUsers -ResetPassword $resetPassword -ResetExistingPassword:$false)
    Assert-Equal $beforeNoOp $accountState.PasswordSets 'committed credential no-op must not reset password'
    Write-Output 'PASS: owned account creation, Users join, interrupted recovery and privileged refusal'

    $plan = & $setup -LabRoot $labRoot -PlanOnly
    Assert-Equal $true $plan.Ready 'single-step administrator plan readiness'
    Assert-Equal $true $plan.WouldJoinUsers 'new-user plan includes Users membership'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'account.manifest.json')) 'plan must not create manifest'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'secrets')) 'plan must not create secrets folder'
    Assert-Equal 'AzuriteTest' $plan.AccountName 'planned dedicated account'
    Write-Output 'PASS: single-step administrator plan is read-only'

    $currentPrincipal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Assert-Throws { & $setup -LabRoot $labRoot | Out-Null } 'nonadministrator account setup'
        Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'account.manifest.json')) 'nonadmin refusal must precede manifest creation'
        Write-Output 'PASS: nonadministrator setup refuses before mutation'
    }

    $runner = Join-Path $repositoryRoot 'tools\headless\Invoke-AzuriteHeadless.ps1'
    $runnerPlan = & $runner -LabRoot $labRoot -Mode Graphics -Scenario 'graphics-frame' -PlanOnly
    Assert-Equal '-batchmode|-logFile|-' ($runnerPlan.Arguments -join '|') 'runner graphics plan arguments'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'runs')) 'runner plan must not create run folder'
    Write-Output 'PASS: runner plan is read-only and preserves graphics mode arguments'
    Assert-Throws { & $runner -LabRoot $labRoot -Mode Logic -Scenario 'logic-smoke' | Out-Null } 'missing account/credential before launch'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $labRoot 'runs')) 'refusal must not create run folder'
    Write-Output 'PASS: runner refuses missing account/credential without starting a child'

} finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
