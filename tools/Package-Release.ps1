#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$ReadmePath,
    [string]$SourceNoticePath,
    [string]$SourceRoot,
    [string]$AAInstallPath,
    [string]$CecilPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $SourceRoot) { $SourceRoot = Split-Path -Parent $PSScriptRoot }
$repositoryRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourceRoot).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
function Get-CleanSourceCommit {
    $topLevel = & git -C $repositoryRoot rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $topLevel -or
        -not [IO.Path]::GetFullPath($topLevel.Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SourceRoot must be the root of a Git repository.'
    }
    $commit = (& git -C $repositoryRoot rev-parse --verify HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Source commit is missing or invalid.' }
    $changes = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify source tree status.' }
    if ($changes.Count -gt 0) { throw 'Release packaging requires a clean source tree; dirty or untracked files were found.' }
    $commit
}
$sourceCommit = Get-CleanSourceCommit
$sourceTree = (& git -C $repositoryRoot rev-parse 'HEAD^{tree}' 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceTree -cnotmatch '^[0-9a-f]{40}$') { throw 'Source tree is missing or invalid.' }
$manifestPath = Join-Path $repositoryRoot 'assets/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version_number
if ($manifest.name -cne 'Azurite' -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'assets/manifest.json must identify Azurite and a three-part numeric version.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'dist' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($outputRoot)
$archivePath = Join-Path $outputRoot "Azurite-$version.zip"
$sidecarPath = "$archivePath.sha256"
if ([IO.File]::Exists($archivePath) -or [IO.File]::Exists($sidecarPath)) {
    throw 'Refusing to overwrite an existing release archive or checksum. Select a new output directory.'
}

$files = [ordered]@{
    'Azurite.dll' = Join-Path $repositoryRoot 'Plugin/bin/Release/netcoreapp6.0/Azurite.dll'
    'Azurite.Core.dll' = Join-Path $repositoryRoot 'references/Azurite.Core.dll'
    'manifest.json' = $manifestPath
    'icon.png' = Join-Path $repositoryRoot 'assets/icon.png'
}
if (-not $ReadmePath) {
    $candidate = Join-Path $repositoryRoot 'README.md'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $ReadmePath = $candidate }
}
if (-not $SourceNoticePath) {
    $candidate = Join-Path $repositoryRoot 'docs/SOURCE.md'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $SourceNoticePath = $candidate }
}
if ($ReadmePath) { $files['README.md'] = $ReadmePath }
if ($SourceNoticePath) { $files['SOURCE.md'] = $SourceNoticePath }
foreach ($name in @($files.Keys)) {
    $path = (Resolve-Path -LiteralPath $files[$name]).Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing payload file: $name" }
    $files[$name] = $path
}
$identityArgs = @{
    PluginPath = $files['Azurite.dll']
    CorePath = $files['Azurite.Core.dll']
    ManifestPath = $files['manifest.json']
}
if ($AAInstallPath) { $identityArgs.AAInstallPath = $AAInstallPath }
if ($CecilPath) { $identityArgs.CecilPath = $CecilPath }
$identity = & (Join-Path $PSScriptRoot 'Test-CandidateIdentity.ps1') @identityArgs
$receiptPath = Join-Path $repositoryRoot 'artifacts/candidate-build-receipt.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw 'Candidate build receipt is missing. Run tools/Build-Candidate.ps1 from this clean commit first.'
}
try { $receipt = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json }
catch { throw "Candidate build receipt is invalid JSON: $($_.Exception.Message)" }
$requiredReceiptFields = @('schema', 'sourceCommit', 'sourceTree', 'sourceTreeClean', 'version', 'candidateBuild',
    'pluginSha256', 'coreSha256', 'coreMode', 'hostReferences')
foreach ($field in $requiredReceiptFields) {
    if ($field -notin $receipt.PSObject.Properties.Name) { throw "Candidate build receipt is missing $field." }
}
if ($receipt.schema -ne 1 -or $receipt.sourceTreeClean -isnot [bool] -or -not $receipt.sourceTreeClean -or
    $receipt.sourceCommit -cne $sourceCommit -or $receipt.sourceTree -cne $sourceTree) {
    throw 'Candidate build receipt sourceCommit/sourceTree is stale or invalid.'
}
if ($receipt.version -cne $identity.Version -or $receipt.candidateBuild -cne $identity.CandidateBuild -or
    $receipt.pluginSha256 -cne $identity.PluginSha256 -or $receipt.coreSha256 -cne $identity.CoreSha256 -or
    $receipt.coreMode -cne $identity.CoreMode) {
    throw 'Candidate build receipt version, candidateBuild, pluginSha256, or coreSha256 differs from current payload.'
}
$hostReferences = @($receipt.hostReferences)
if ($hostReferences.Count -eq 0) { throw 'Candidate build receipt has no host references.' }
$hostPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($reference in $hostReferences) {
    if ($null -eq $reference -or $reference.path -cnotmatch '^[^:/\\]+(?:/[^:/\\]+)+$' -or
        ($reference.path -split '/' | Where-Object { $_ -in @('.', '..') }) -or
        $reference.sha256 -cnotmatch '^[0-9a-f]{64}$' -or -not $hostPaths.Add($reference.path)) {
        throw 'Candidate build receipt has invalid or duplicate host reference hashes.'
    }
}
$buildRecord = [ordered]@{
    schema = 1
    sourceCommit = $sourceCommit
    sourceTree = $sourceTree
    sourceTreeClean = $true
    version = $identity.Version
    candidateBuild = $identity.CandidateBuild
    pluginSha256 = $identity.PluginSha256
    coreSha256 = $identity.CoreSha256
    coreMode = $identity.CoreMode
    hostReferences = $hostReferences
    runtimeAcceptance = 'not-performed'
}
$buildBytes = [Text.UTF8Encoding]::new($false).GetBytes(($buildRecord | ConvertTo-Json -Depth 4 -Compress) + "`n")

$temporaryPath = [IO.Path]::GetFullPath((Join-Path $outputRoot ('.azurite-' + [Guid]::NewGuid().ToString('N') + '.partial')))
# This exact workspace-local file is the only item eligible for cleanup.
if ([IO.Path]::GetDirectoryName($temporaryPath) -cne $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
    throw 'Temporary archive escaped the chosen output directory.'
}
$prefix = "Azurite/$version/"
try {
    $outputStream = [IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($outputStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $hashLines = [Collections.Generic.List[string]]::new()
        foreach ($name in $files.Keys) {
            $bytes = [IO.File]::ReadAllBytes($files[$name])
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            $hashLines.Add("$hash  $name")
            $entry = $archive.CreateEntry($prefix + $name, [IO.Compression.CompressionLevel]::Optimal)
            $stream = $entry.Open()
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
        $buildHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buildBytes)).ToLowerInvariant()
        $hashLines.Add("$buildHash  BUILD.json")
        $buildEntry = $archive.CreateEntry($prefix + 'BUILD.json', [IO.Compression.CompressionLevel]::Optimal)
        $stream = $buildEntry.Open()
        try { $stream.Write($buildBytes, 0, $buildBytes.Length) } finally { $stream.Dispose() }
        $checksumBytes = [Text.UTF8Encoding]::new($false).GetBytes(($hashLines -join "`n") + "`n")
        $checksumEntry = $archive.CreateEntry($prefix + 'SHA256SUMS.txt', [IO.Compression.CompressionLevel]::Optimal)
        $stream = $checksumEntry.Open()
        try { $stream.Write($checksumBytes, 0, $checksumBytes.Length) } finally { $stream.Dispose() }
    }
    finally { $archive.Dispose(); $outputStream.Dispose() }

    $validationArgs = @{ ArchivePath = $temporaryPath; ExpectedVersion = $version; ExpectedSourceCommit = $sourceCommit }
    if ($AAInstallPath) { $validationArgs.AAInstallPath = $AAInstallPath }
    if ($CecilPath) { $validationArgs.CecilPath = $CecilPath }
    $validation = & (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') @validationArgs
    if ((Get-CleanSourceCommit) -cne $sourceCommit -or
        (& git -C $repositoryRoot rev-parse 'HEAD^{tree}').Trim() -cne $sourceTree) {
        throw 'Source commit or tree changed during packaging.'
    }
    # File.Move without overwrite is still safe if another process published
    # the same filename after our initial existence check.
    [IO.File]::Move($temporaryPath, $archivePath)
    $checksum = "$($validation.SHA256)  $([IO.Path]::GetFileName($archivePath))`n"
    $sidecar = [IO.FileStream]::new($sidecarPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($checksum)
        $sidecar.Write($bytes, 0, $bytes.Length)
    }
    finally { $sidecar.Dispose() }
    $validationArgs.ArchivePath = $archivePath
    $validationArgs.RequireSidecar = $true
    & (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') @validationArgs
}
finally {
    if ([IO.File]::Exists($temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
}
