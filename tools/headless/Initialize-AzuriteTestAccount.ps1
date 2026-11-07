param(
    [Parameter(Mandatory)][string]$LabRoot,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'HeadlessCore.psm1') -Force
$lab = Assert-AzuriteFullPath $LabRoot
$markerPath = Join-Path $lab 'lab.marker.json'
if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Lab marker is missing.' }
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$controllerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if ($marker.schema -ne 1 -or $marker.purpose -cne 'AzuriteHeadlessLab' -or
    -not ([guid]::TryParse($marker.labId, [ref]([guid]::Empty))) -or
    $marker.controllerSid -cne $controllerSid) { throw 'Lab marker is not owned by the controller identity.' }
[void](Assert-AzuriteLabPaths -LabRoot $lab -InstallPath (Join-Path $lab 'install') -DailyInstallPath $marker.dailyInstallPath)
Assert-AzuriteNoReparseTree $lab
$account = Get-LocalUser -Name 'AzuriteTest' -ErrorAction SilentlyContinue
$preflightSource = Join-Path $PSScriptRoot 'Identity-Preflight.ps1'
$prerequisites = Get-AzuriteSetupPrerequisites -LabRoot $lab -ControllerSid $controllerSid -PreflightSource $preflightSource -TestSid $(if ($account) { $account.SID.Value })
$sourceHash = if (Test-Path -LiteralPath $preflightSource -PathType Leaf) { (Get-FileHash -LiteralPath $preflightSource -Algorithm SHA256).Hash } else { $null }
$manifestPath = Join-Path $lab 'account.manifest.json'
$secretDirectory = Join-Path $lab 'secrets'
$credentialPath = Join-Path $secretDirectory 'controller.credential.json'
$toolingDirectory = Join-Path $lab 'tooling'
$stagedPreflight = Join-Path $toolingDirectory 'Identity-Preflight.ps1'
$manifest = $null
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    Assert-AzuritePrivateAcl -Path $manifestPath -ControllerSid $controllerSid
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or $manifest.labId -cne $marker.labId -or
        $manifest.controllerSid -cne $controllerSid -or $manifest.accountName -cne 'AzuriteTest' -or
        -not ([guid]::TryParse($manifest.ownerToken, [ref]([guid]::Empty))) -or
        ($manifest.preflightSha256 -and $manifest.preflightSha256 -cne $sourceHash)) {
        throw 'Account manifest does not belong to this reviewed lab.'
    }
}
if ($account) {
    if (-not $manifest -or -not $account.Enabled -or
        $account.Description -cne (Get-AzuriteAccountDescription -OwnerToken $manifest.ownerToken) -or
        ($manifest.accountSid -and $manifest.accountSid -cne $account.SID.Value)) {
        throw 'Existing AzuriteTest account is not manifest-owned.'
    }
    $membershipSids = @(Get-AzuriteLocalMembershipSids -AccountSid $account.SID.Value)
    if (@($membershipSids | Where-Object { $_ -cne 'S-1-5-32-545' }).Count -gt 0 -or $membershipSids.Count -gt 1) {
        throw 'Existing AzuriteTest has unexpected local group membership.'
    }
} elseif ($manifest -and $manifest.accountSid) { throw 'Manifest refers to a deleted account; manual review is required.' }
if (Test-Path -LiteralPath $secretDirectory -PathType Container) {
    Assert-AzuritePrivateAcl -Path $secretDirectory -ControllerSid $controllerSid
}
$credentialValid = $false
if (Test-Path -LiteralPath $credentialPath -PathType Leaf) {
    Assert-AzuritePrivateAcl -Path $credentialPath -ControllerSid $controllerSid
    if ($account) {
        try {
            $record = Get-Content -LiteralPath $credentialPath -Raw | ConvertFrom-Json
            $credentialValid = Test-AzuriteCommittedCredential -Manifest $manifest -CredentialRecord $record -LabId $marker.labId -ControllerSid $controllerSid -AccountSid $account.SID.Value
        } catch { $credentialValid = $false }
    }
}
if ((Test-Path -LiteralPath $stagedPreflight -PathType Leaf) -and $sourceHash -and
    (Get-FileHash -LiteralPath $stagedPreflight -Algorithm SHA256).Hash -cne $sourceHash) {
    $prerequisites.Failures += "Staged preflight differs from reviewed source: $stagedPreflight"
    $prerequisites.Ready = $false
}
$ownerToken = if ($manifest) { $manifest.ownerToken } else { [guid]::NewGuid().ToString() }
$description = Get-AzuriteAccountDescription -OwnerToken $ownerToken
$descriptionLimit = (Get-Command New-LocalUser).Parameters['Description'].Attributes |
    Where-Object { $_ -is [System.Management.Automation.ValidateLengthAttribute] } | Select-Object -First 1
if (-not $descriptionLimit -or $description.Length -gt $descriptionLimit.MaxLength) { throw 'Ownership description exceeds local-user command limit.' }
$plan = [pscustomobject]@{
    LabRoot = $lab; AccountName = 'AzuriteTest'; ControllerSid = $controllerSid
    Ready = $prerequisites.Ready; PrerequisiteFailures = $prerequisites.Failures
    ExistingOwnedAccount = [bool]$account; WouldCreateAccount = -not [bool]$account
    WouldJoinUsers = -not $account -or $membershipSids.Count -eq 0
    WouldRefreshCredential = -not $credentialValid; RequiresAdministrator = $true
    CredentialPath = $credentialPath; StagedPreflight = $stagedPreflight
    SystemPowerShell = $prerequisites.SystemPowerShell
}
if ($PlanOnly) { return $plan }
if (-not $prerequisites.Ready) { throw "Lab prerequisites failed before mutation: $($prerequisites.Failures -join '; ')" }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run from an already elevated shell under the controller SID. No UAC prompt is triggered.'
}
if (-not $account) { New-LocalUser -Name 'AzuriteTest' -NoPassword -Description $description -WhatIf -ErrorAction Stop | Out-Null }

$password = $null
if ($credentialValid) {
    $password = ConvertTo-SecureString -String $record.encryptedPassword -ErrorAction Stop
} else {
    $random = [byte[]]::new(44)
    [Security.Cryptography.RandomNumberGenerator]::Fill($random)
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%'
    $password = [Security.SecureString]::new()
    foreach ($character in 'Aa9!'.ToCharArray()) { $password.AppendChar($character) }
    foreach ($byte in $random) { $password.AppendChar($alphabet[[int]($byte % $alphabet.Length)]) }
    $password.MakeReadOnly()
    [Array]::Clear($random, 0, $random.Length)
}
try {
    Set-AzuriteLabAcl -LabRoot $lab -Path $lab -ControllerSid $controllerSid
    if (-not (Test-Path -LiteralPath $secretDirectory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $secretDirectory -ErrorAction Stop)
    }
    Set-AzuriteLabAcl -LabRoot $lab -Path $secretDirectory -ControllerSid $controllerSid
    if (-not $manifest) {
        $manifest = [ordered]@{ schema = 1; labId = $marker.labId; controllerSid = $controllerSid
            accountName = 'AzuriteTest'; ownerToken = $ownerToken; accountSid = $null
            preflightSha256 = $sourceHash; credentialSha256 = $null }
        Write-AzuritePrivateJson -LabRoot $lab -Path $manifestPath -Value $manifest -ControllerSid $controllerSid
    }
    if (-not $credentialValid -and $account) {
        if ($manifest -is [Collections.IDictionary]) { $manifest['credentialSha256'] = $null }
        else { $manifest | Add-Member -NotePropertyName credentialSha256 -NotePropertyValue $null -Force }
        Write-AzuritePrivateJson -LabRoot $lab -Path $manifestPath -Value $manifest -ControllerSid $controllerSid
    }
    $createUser = { param($name, $desc, $secret) New-LocalUser -Name $name -Password $secret -Description $desc -AccountNeverExpires -PasswordNeverExpires -ErrorAction Stop }
    $readMemberships = { param($sid) Get-AzuriteLocalMembershipSids -AccountSid $sid }
    $addUsersMember = { param($sid) $users = Get-LocalGroup -SID 'S-1-5-32-545' -ErrorAction Stop; Add-LocalGroupMember -Group $users.Name -Member $sid -ErrorAction Stop }
    $resetPassword = { param($name, $secret) Set-LocalUser -Name $name -Password $secret -ErrorAction Stop }
    $account = Invoke-AzuriteAccountProvision -ExistingAccount $account -RecordedSid $manifest.accountSid -OwnerToken $ownerToken -Password $password -CreateUser $createUser -ReadMemberships $readMemberships -AddUsersMember $addUsersMember -ResetPassword $resetPassword -ResetExistingPassword:$plan.WouldRefreshCredential
    $manifest.accountSid = $account.SID.Value
    if (-not $manifest.preflightSha256) { $manifest.preflightSha256 = $sourceHash }
    Write-AzuritePrivateJson -LabRoot $lab -Path $manifestPath -Value $manifest -ControllerSid $controllerSid
    Set-AzuriteLabAcl -LabRoot $lab -Path $lab -ControllerSid $controllerSid -TestSid $account.SID.Value
    foreach ($directory in @('install', 'workspace', 'user-data', 'runs')) {
        $path = Join-Path $lab $directory
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { [void](New-Item -ItemType Directory -Path $path -ErrorAction Stop) }
        Set-AzuriteLabAcl -LabRoot $lab -Path $path -ControllerSid $controllerSid -TestSid $account.SID.Value -TestRights Modify
    }
    if (-not (Test-Path -LiteralPath $toolingDirectory -PathType Container)) { [void](New-Item -ItemType Directory -Path $toolingDirectory -ErrorAction Stop) }
    Set-AzuriteLabAcl -LabRoot $lab -Path $toolingDirectory -ControllerSid $controllerSid -TestSid $account.SID.Value
    Copy-Item -LiteralPath $preflightSource -Destination $stagedPreflight -Force
    Set-AzuriteLabAcl -LabRoot $lab -Path $stagedPreflight -ControllerSid $controllerSid -TestSid $account.SID.Value
    if ((Get-FileHash -LiteralPath $stagedPreflight -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Staged preflight hash mismatch.' }
    if (-not $credentialValid) {
        $sealed = ConvertFrom-SecureString -SecureString $password
        $credential = [ordered]@{ schema = 1; labId = $marker.labId; controllerSid = $controllerSid
            accountSid = $account.SID.Value; encryptedPassword = $sealed }
        Write-AzuritePrivateJson -LabRoot $lab -Path $credentialPath -Value $credential -ControllerSid $controllerSid
        $committedDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sealed)))
        if ($manifest -is [Collections.IDictionary]) { $manifest['credentialSha256'] = $committedDigest }
        else { $manifest | Add-Member -NotePropertyName credentialSha256 -NotePropertyValue $committedDigest -Force }
        Write-AzuritePrivateJson -LabRoot $lab -Path $manifestPath -Value $manifest -ControllerSid $controllerSid
    }
    return [pscustomobject]@{ LabRoot = $lab; AccountName = 'AzuriteTest'; AccountSid = $account.SID.Value
        CredentialPath = $credentialPath; StagedPreflight = $stagedPreflight
        CredentialRefreshed = -not $credentialValid; Prepared = $true }
} finally { $password.Dispose() }
