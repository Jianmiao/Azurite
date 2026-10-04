#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$ReadmePath,
    [string]$SourceNoticePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
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
        $checksumBytes = [Text.UTF8Encoding]::new($false).GetBytes(($hashLines -join "`n") + "`n")
        $checksumEntry = $archive.CreateEntry($prefix + 'SHA256SUMS.txt', [IO.Compression.CompressionLevel]::Optimal)
        $stream = $checksumEntry.Open()
        try { $stream.Write($checksumBytes, 0, $checksumBytes.Length) } finally { $stream.Dispose() }
    }
    finally { $archive.Dispose(); $outputStream.Dispose() }

    $validation = & (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') -ArchivePath $temporaryPath -ExpectedVersion $version
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
    & (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') -ArchivePath $archivePath -ExpectedVersion $version -RequireSidecar
}
finally {
    if ([IO.File]::Exists($temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
}
