#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AAInstallPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$aa = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $AAInstallPath).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $aa -PathType Container)) { throw 'AAInstallPath must be an existing directory.' }
$projectPath = Join-Path $repositoryRoot 'Plugin/Azurite.csproj'
$manifestPath = Join-Path $repositoryRoot 'assets/manifest.json'
$pluginPath = Join-Path $repositoryRoot 'Plugin/bin/Release/netcoreapp6.0/Azurite.dll'
$corePath = Join-Path $repositoryRoot 'references/Azurite.Core.dll'
$receiptPath = Join-Path $repositoryRoot 'artifacts/candidate-build-receipt.json'

function Get-CleanSourceIdentity {
    $topLevel = & git -C $repositoryRoot rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $topLevel -or
        -not [IO.Path]::GetFullPath($topLevel.Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Build source must be the root of a Git repository.'
    }
    $commit = (& git -C $repositoryRoot rev-parse --verify HEAD 2>$null).Trim()
    $tree = (& git -C $repositoryRoot rev-parse 'HEAD^{tree}' 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$' -or $tree -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Source commit or tree is missing or invalid.'
    }
    $changes = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify source tree status.' }
    if ($changes.Count -gt 0) { throw 'Candidate build requires a clean source tree; dirty or untracked files were found.' }
    [pscustomobject]@{ Commit = $commit; Tree = $tree }
}

function Get-HostReferences {
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $references = [Collections.Generic.List[object]]::new()
    foreach ($reference in $project.SelectNodes('/Project/ItemGroup/Reference')) {
        $hint = [string]$reference.HintPath
        if (-not $hint.StartsWith('$(AAInstallPath)', [StringComparison]::Ordinal)) { continue }
        $relative = $hint.Substring('$(AAInstallPath)'.Length).TrimStart([char[]]@('\', '/'))
        if (-not $relative -or ($relative -split '[\\/]' | Where-Object { $_ -in @('', '.', '..') })) {
            throw "Unsafe host reference HintPath: $hint"
        }
        $path = [IO.Path]::GetFullPath((Join-Path $aa $relative))
        if (-not $path.StartsWith($aa + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing or outside-AA host reference: $relative"
        }
        $references.Add([ordered]@{
            path = $relative.Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    if ($references.Count -eq 0) { throw 'Project declares no AA host references.' }
    @($references | Sort-Object { $_.path })
}

$before = Get-CleanSourceIdentity
$hostReferences = @(Get-HostReferences)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($receiptPath))
if ([IO.File]::Exists($receiptPath)) { Remove-Item -LiteralPath $receiptPath -Force }
if ([IO.File]::Exists($pluginPath)) { Remove-Item -LiteralPath $pluginPath -Force }

$buildOutput = & dotnet build $projectPath -c Release --no-incremental --disable-build-servers "-p:AAInstallPath=$aa" 2>&1
if ($LASTEXITCODE -ne 0) {
    $buildOutput | ForEach-Object { Write-Output $_ }
    throw 'Candidate plugin rebuild failed; no valid build receipt was written.'
}
if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) { throw 'Build completed without producing Azurite.dll.' }
$identity = & (Join-Path $PSScriptRoot 'Test-CandidateIdentity.ps1') -PluginPath $pluginPath -CorePath $corePath -ManifestPath $manifestPath -AAInstallPath $aa
$after = Get-CleanSourceIdentity
if ($after.Commit -cne $before.Commit -or $after.Tree -cne $before.Tree) {
    throw 'Source commit or tree changed during candidate build.'
}
$afterHostReferences = @(Get-HostReferences)
if (($afterHostReferences | ConvertTo-Json -Depth 4 -Compress) -cne ($hostReferences | ConvertTo-Json -Depth 4 -Compress)) {
    throw 'AA host reference hashes changed during candidate build.'
}

$record = [ordered]@{
    schema = 1
    sourceCommit = $before.Commit
    sourceTree = $before.Tree
    sourceTreeClean = $true
    version = $identity.Version
    candidateBuild = $identity.CandidateBuild
    pluginSha256 = $identity.PluginSha256
    coreSha256 = $identity.CoreSha256
    coreMode = $identity.CoreMode
    hostReferences = $hostReferences
}
$temporaryReceipt = "$receiptPath.$([Guid]::NewGuid().ToString('N')).partial"
try {
    [IO.File]::WriteAllText($temporaryReceipt, (($record | ConvertTo-Json -Depth 6) + "`n"), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryReceipt, $receiptPath)
}
finally {
    if ([IO.File]::Exists($temporaryReceipt)) { Remove-Item -LiteralPath $temporaryReceipt -Force }
}
[pscustomobject]@{
    Receipt = $receiptPath
    SourceCommit = $before.Commit
    SourceTree = $before.Tree
    Version = $identity.Version
    CandidateBuild = $identity.CandidateBuild
    PluginSha256 = $identity.PluginSha256
    CoreSha256 = $identity.CoreSha256
    HostReferences = $hostReferences.Count
    Result = 'PASS'
}
