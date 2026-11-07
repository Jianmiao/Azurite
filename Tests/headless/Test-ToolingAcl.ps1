$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repositoryRoot 'tools\headless\HeadlessCore.psm1') -Force
$fixtureRoot = Join-Path $repositoryRoot ('artifacts\headless-tests\' + [guid]::NewGuid().ToString('N'))
$lab = Join-Path $fixtureRoot 'lab'
$install = Join-Path $lab 'install'
$profile = Join-Path $install 'profiles\AzuriteAcceptance'
$tooling = Join-Path $lab 'tooling'
$preflightSource = Join-Path $repositoryRoot 'tools\headless\Identity-Preflight.ps1'
$staged = Join-Path $tooling 'Identity-Preflight.ps1'
$controllerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
try {
    New-Item -ItemType Directory -Path (Join-Path $profile 'configs'), $tooling -Force | Out-Null
    'fixture' | Set-Content -LiteralPath (Join-Path $install 'AzureArchive.exe')
    'AzuriteAcceptance' | Set-Content -LiteralPath (Join-Path $install 'ActiveProfile.txt')
    '{}' | Set-Content -LiteralPath (Join-Path $profile 'modconfig.json')
    Copy-Item -LiteralPath $preflightSource -Destination $staged
    Set-AzuriteLabAcl -LabRoot $lab -Path $tooling -ControllerSid $controllerSid
    Set-AzuriteLabAcl -LabRoot $lab -Path $staged -ControllerSid $controllerSid
    $baseline = Get-AzuriteSetupPrerequisites -LabRoot $lab -ControllerSid $controllerSid -PreflightSource $preflightSource
    if (-not $baseline.Ready) { throw "Baseline fixture is invalid: $($baseline.Failures -join '; ')" }
    foreach ($target in @($tooling, $staged)) {
        & icacls.exe $target '/grant' '*S-1-5-32-545:(M)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not create fixture ACE: $target" }
        $result = Get-AzuriteSetupPrerequisites -LabRoot $lab -ControllerSid $controllerSid -TestSid 'S-1-5-32-545' -PreflightSource $preflightSource
        if ($result.Ready -or ($result.Failures -join ' ') -notmatch [regex]::Escape($target)) {
            throw "Writable test-principal tooling was accepted: $target"
        }
        & icacls.exe $target '/remove:g' '*S-1-5-32-545' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not restore fixture ACE: $target" }
    }
    foreach ($target in @($tooling, $staged)) {
        Set-AzuriteLabAcl -LabRoot $lab -Path $target -ControllerSid $controllerSid -TestSid 'S-1-5-32-545'
    }
    $readOnly = Get-AzuriteSetupPrerequisites -LabRoot $lab -ControllerSid $controllerSid -TestSid 'S-1-5-32-545' -PreflightSource $preflightSource
    if (-not $readOnly.Ready) { throw "Production-generated test-principal RX was refused: $($readOnly.Failures -join '; ')" }
    $transition = Join-Path $lab 'acl-transition'
    New-Item -ItemType Directory -Path $transition | Out-Null
    Set-AzuriteLabAcl -LabRoot $lab -Path $transition -ControllerSid $controllerSid
    Set-AzuriteLabAcl -LabRoot $lab -Path $transition -ControllerSid $controllerSid -TestSid 'S-1-5-32-545'
    Set-AzuriteLabAcl -LabRoot $lab -Path $transition -ControllerSid $controllerSid
    Assert-AzuritePrivateAcl -Path $transition -ControllerSid $controllerSid
    Set-AzuriteLabAcl -LabRoot $lab -Path $transition -ControllerSid $controllerSid -TestSid 'S-1-5-32-545'
    $entries = @((Get-Acl -LiteralPath $transition).Access | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique)
    $expected = @(@($controllerSid, 'S-1-5-18', 'S-1-5-32-544', 'S-1-5-32-545') | Sort-Object -Unique)
    if (($entries -join '|') -cne ($expected -join '|')) { throw "ACL transition retained extra grants: $($entries -join ', ')" }
    Write-Output 'PASS: tooling directory and staged script reject test-principal Modify before account work'
    Write-Output 'PASS: production RX tooling and private/RX rerun transitions remain valid'
} finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
