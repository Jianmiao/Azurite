param(
    [Parameter(Mandatory)][string]$LabRoot,
    [Parameter(Mandatory)][ValidateSet('Logic', 'Graphics')][string]$Mode,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$')][string]$Scenario,
    [ValidateRange(1, 3600)][int]$TimeoutSeconds = 180,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'HeadlessCore.psm1') -Force
$lab = Assert-AzuriteFullPath $LabRoot
$markerPath = Join-Path $lab 'lab.marker.json'
if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Lab marker is missing.' }
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
if ($marker.schema -ne 1 -or $marker.purpose -cne 'AzuriteHeadlessLab' -or
    $marker.controllerSid -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) {
    throw 'Lab marker is invalid for the controller.'
}
$install = Join-Path $lab 'install'
[void](Assert-AzuriteLabPaths -LabRoot $lab -InstallPath $install -DailyInstallPath $marker.dailyInstallPath)
Assert-AzuriteNoReparseTree $lab
$scenarioContract = Get-AzuriteScenarioContract -Mode $Mode -Scenario $Scenario
$arguments = @(Get-AzuriteArguments -Mode $Mode)
$executable = Join-Path $install 'AzureArchive.exe'
$profileName = 'AzuriteAcceptance'
$activeProfile = Join-Path $install 'ActiveProfile.txt'
$profileConfig = Join-Path $install ('profiles\' + $profileName + '\modconfig.json')
$profileConfigs = Join-Path $install ('profiles\' + $profileName + '\configs')
$stagedPreflight = Join-Path $lab 'tooling\Identity-Preflight.ps1'
$systemPowerShell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$manifestPath = Join-Path $lab 'account.manifest.json'
$credentialPath = Join-Path $lab 'secrets\controller.credential.json'
$plan = [pscustomobject]@{
    LabRoot = $lab; Executable = $executable; Mode = $Mode; Scenario = $Scenario; Arguments = $arguments
    ActiveProfile = $profileName; RequiredChecks = $scenarioContract.RequiredChecks
    RequiresFrame = $scenarioContract.RequiresFrame
    CredentialPresent = (Test-Path -LiteralPath $credentialPath -PathType Leaf)
    AccountManifestPresent = (Test-Path -LiteralPath $manifestPath -PathType Leaf)
    AudioIsolationVerified = $false
    TimeoutSeconds = $TimeoutSeconds; WillLaunch = $false
}
if ($PlanOnly) { return $plan }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
    throw 'Dedicated account manifest or DPAPI credential is absent. No process was started.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.labId -cne $marker.labId -or
    $manifest.controllerSid -cne $marker.controllerSid -or $manifest.accountName -cne 'AzuriteTest' -or
    $manifest.accountSid -notmatch '^S-1-' -or $manifest.preflightSha256 -cnotmatch '^[0-9A-Fa-f]{64}$') { throw 'Account manifest is invalid.' }
$account = Get-LocalUser -Name 'AzuriteTest' -ErrorAction SilentlyContinue
if (-not $account -or -not $account.Enabled -or $account.SID.Value -cne $manifest.accountSid -or
    $account.Description -cne (Get-AzuriteAccountDescription -OwnerToken $manifest.ownerToken)) {
    throw 'Dedicated account identity is absent or differs from the manifest.'
}
Assert-AzuriteLowPrivilegeGroups -AccountSid $account.SID.Value
$credentialRecord = Get-Content -LiteralPath $credentialPath -Raw | ConvertFrom-Json
if ($credentialRecord.schema -ne 1 -or $credentialRecord.labId -cne $marker.labId -or
    $credentialRecord.controllerSid -cne $marker.controllerSid -or
    $credentialRecord.accountSid -cne $account.SID.Value -or [string]::IsNullOrWhiteSpace($credentialRecord.encryptedPassword)) {
    throw 'Credential metadata does not match the dedicated account and controller.'
}
Assert-AzuritePrivateAcl -Path $credentialPath -ControllerSid $marker.controllerSid
$password = ConvertTo-SecureString -String $credentialRecord.encryptedPassword
$credential = [pscredential]::new('AzuriteTest', $password)
if (-not (Test-Path -LiteralPath $systemPowerShell -PathType Leaf) -or
    -not (Test-Path -LiteralPath $stagedPreflight -PathType Leaf) -or
    (Get-FileHash -LiteralPath $stagedPreflight -Algorithm SHA256).Hash -cne $manifest.preflightSha256) {
    throw 'Machine-wide PowerShell or reviewed lab preflight is missing or changed.'
}
$stagedAcl = Get-Acl -LiteralPath $stagedPreflight
if (-not $stagedAcl.AreAccessRulesProtected -or
    -not @($stagedAcl.Access | Where-Object {
        $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -ceq $account.SID.Value -and
        $_.AccessControlType -eq 'Allow' -and
        ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::ReadAndExecute) -eq [Security.AccessControl.FileSystemRights]::ReadAndExecute -and
        ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -eq 0
    }).Count) { throw 'Dedicated account cannot read the staged preflight safely.' }
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Isolated AA executable is missing.' }
if (-not (Test-Path -LiteralPath $activeProfile -PathType Leaf) -or
    ((Get-Content -LiteralPath $activeProfile -Raw).Trim() -cne $profileName) -or
    -not (Test-Path -LiteralPath $profileConfig -PathType Leaf) -or
    -not (Test-Path -LiteralPath $profileConfigs -PathType Container)) { throw 'Native AzuriteAcceptance profile is missing or inactive.' }
if (@(Get-CimInstance -ClassName Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count -gt 0) {
    throw 'Another AzureArchive process exists; refusing a headless launch.'
}
$mutex = [Threading.Mutex]::new($false, 'Local\AzuriteHeadless-AA')
$hasMutex = $false
try {
    $hasMutex = $mutex.WaitOne(0)
    if (-not $hasMutex) { throw 'Another headless AA run is active.' }
    if (@(Get-CimInstance -ClassName Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count -gt 0) {
        throw 'Another AzureArchive process appeared before launch.'
    }
    $runId = [guid]::NewGuid().ToString('N')
    $runDirectory = Join-Path $lab ('runs\' + $runId)
    [void](New-Item -ItemType Directory -Path $runDirectory -ErrorAction Stop)
    $preflightResultPath = Join-Path $runDirectory 'preflight.json'
    $preflight = Invoke-AzuriteChild -FilePath $systemPowerShell -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $stagedPreflight) -WorkingDirectory $runDirectory -RunId ('preflight-' + $runId) -ResultPath $preflightResultPath -TimeoutSeconds 15 -Credential $credential -UseArgumentMetadata
    Assert-AzuriteNoReparseTree $runDirectory
    $preflight.Stdout | Set-Content -LiteralPath (Join-Path $runDirectory 'preflight.stdout.log') -Encoding utf8
    $preflight.Stderr | Set-Content -LiteralPath (Join-Path $runDirectory 'preflight.stderr.log') -Encoding utf8
    if (-not $preflight.ResultValid -or $preflight.Result.identitySid -cne $account.SID.Value -or
        $preflight.Result.isAdministrator -ne $false -or
        [string]::IsNullOrWhiteSpace($preflight.Result.userProfile) -or
        [string]::IsNullOrWhiteSpace($preflight.Result.localAppData) -or
        $preflight.Result.userProfile.Equals($env:USERPROFILE, [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.localAppData.StartsWith($preflight.Result.userProfile.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.localLow.StartsWith($preflight.Result.userProfile.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.environmentProfile.Equals($preflight.Result.userProfile, [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.environmentLocalAppData.Equals($preflight.Result.localAppData, [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.environmentAppData.StartsWith($preflight.Result.userProfile.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.environmentTemp.StartsWith($preflight.Result.userProfile.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $preflight.Result.environmentTmp.StartsWith($preflight.Result.userProfile.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $preflight.Result.hiveLoaded -ne $true -or @($preflight.Result.unexpectedPrivileges).Count -ne 0) {
        throw 'Harmless child did not prove dedicated SID, Windows profile, LocalLow and HKCU isolation.'
    }
    foreach ($knownPath in @($preflight.Result.userProfile, $preflight.Result.localAppData, $preflight.Result.localLow)) {
        [void](Assert-AzuriteFullPath $knownPath)
    }
    if ($preflight.Result.audioIsolationVerified -ne $true) {
        throw 'AA launch is disabled: OS session audio isolation has no verified implementation yet.'
    }
    if (@(Get-CimInstance -ClassName Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count -gt 0) {
        throw 'Another AzureArchive process appeared after preflight.'
    }
    $resultPath = Join-Path $runDirectory 'result.json'
    $nativeExpectation = @{ Mode = $Mode; Scenario = $Scenario; AccountSid = $account.SID.Value }
    $run = Invoke-AzuriteChild -FilePath $executable -ArgumentList $arguments -WorkingDirectory $install -RunId $runId -ResultPath $resultPath -TimeoutSeconds $TimeoutSeconds -Credential $credential -UseArgumentMetadata -NativeExpectation $nativeExpectation
    Assert-AzuriteNoReparseTree $runDirectory
    $run.Stdout | Set-Content -LiteralPath (Join-Path $runDirectory 'stdout.log') -Encoding utf8
    $run.Stderr | Set-Content -LiteralPath (Join-Path $runDirectory 'stderr.log') -Encoding utf8
    $summary = [pscustomobject]@{ RunId = $runId; Mode = $Mode; Pid = $run.Pid; ExitCode = $run.ExitCode
        TimedOut = $run.TimedOut; WindowViolation = $run.WindowViolation; ResultValid = $run.ResultValid
        RunDirectory = $runDirectory; StartedUtc = $run.StartedUtc }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'run-summary.json') -Encoding utf8
    if ($run.TimedOut -or $run.WindowViolation -or -not $run.ResultValid) { throw "Headless AA run failed; inspect $runDirectory" }
    return $summary
} finally {
    if ($hasMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
