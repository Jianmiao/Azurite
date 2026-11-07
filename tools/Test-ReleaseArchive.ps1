#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$ExpectedVersion,
    [string]$CoreReferencePath,
    [string]$ExpectedSourceCommit,
    [string]$AAInstallPath,
    [string]$CecilPath,
    [switch]$RequireSidecar
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExpectedVersion) {
    $ExpectedVersion = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'assets/manifest.json') -Raw | ConvertFrom-Json).version_number
}
if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'ExpectedVersion must be a three-part numeric release version.' }
if (-not $CoreReferencePath) { $CoreReferencePath = Join-Path $repositoryRoot 'references/Azurite.Core.dll' }
$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
$resolvedCore = (Resolve-Path -LiteralPath $CoreReferencePath).Path
$prefix = "Azurite/$ExpectedVersion/"
$required = @('Azurite.dll', 'Azurite.Core.dll', 'manifest.json', 'icon.png', 'SHA256SUMS.txt')
$permitted = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($name in ($required + @('README.md', 'SOURCE.md', 'BUILD.json'))) { [void]$permitted.Add($name) }

function Get-ByteHash([byte[]]$Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Get-EntryBytes([IO.Compression.ZipArchiveEntry]$Entry) {
    # Limits avoid allocating unbounded data for an accidentally malformed ZIP.
    if ($Entry.Length -gt 16MB) { throw "Entry exceeds release size limit: $($Entry.FullName)" }
    $inputStream = $Entry.Open()
    $buffer = [IO.MemoryStream]::new()
    try {
        $inputStream.CopyTo($buffer)
        if ($buffer.Length -ne $Entry.Length) { throw "Entry length mismatch: $($Entry.FullName)" }
        return ,$buffer.ToArray()
    }
    finally { $inputStream.Dispose(); $buffer.Dispose() }
}

function Test-ManagedAssembly([byte[]]$Bytes, [string]$Name, [string]$Version) {
    $buffer = [IO.MemoryStream]::new($Bytes, $false)
    $pe = [Reflection.PortableExecutable.PEReader]::new($buffer)
    try {
        if (-not $pe.HasMetadata -or $null -eq $pe.PEHeaders.CorHeader) {
            throw "$Name is not a managed PE assembly."
        }
        if (($pe.PEHeaders.CoffHeader.Characteristics -band [Reflection.PortableExecutable.Characteristics]::Dll) -eq 0) {
            throw "$Name is not a PE DLL."
        }
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        if (-not $metadata.IsAssembly) { throw "$Name has no assembly definition." }
        $definition = $metadata.GetAssemblyDefinition()
        $actualName = $metadata.GetString($definition.Name)
        if ($actualName -cne $Name) { throw "Assembly identity mismatch: expected $Name, found $actualName." }
        if ($Version -and $definition.Version.ToString() -cne $Version) {
            throw "Assembly version mismatch: expected $Version, found $($definition.Version)."
        }
        # Inspect metadata without loading the plugin or any AA dependencies.
        return $definition.Version.ToString()
    }
    finally { $pe.Dispose(); $buffer.Dispose() }
}

$entries = [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
$paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
    if ($archive.Entries.Count -gt 12) { throw 'Unexpected release archive entry count.' }
    $totalLength = 0L
    foreach ($entry in $archive.Entries) {
        $path = $entry.FullName
        if (-not $paths.Add($path)) { throw "Duplicate or case-conflicting ZIP path: $path" }
        if ($path.Contains('\') -or $path.Contains(':') -or $path.StartsWith('/') -or
            ($path.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' })) {
            throw "Unsafe ZIP path: $path"
        }
        if ($path.EndsWith('/')) {
            if ($path -cne 'Azurite/' -and $path -cne $prefix) { throw "Unexpected ZIP directory: $path" }
            continue
        }
        if (-not $path.StartsWith($prefix, [StringComparison]::Ordinal)) { throw "File outside required root $prefix : $path" }
        $relative = $path.Substring($prefix.Length)
        if (-not $permitted.Contains($relative)) {
            throw "Unapproved payload file (host DLLs, user configuration and build folders are forbidden): $relative"
        }
        $totalLength += $entry.Length
        if ($totalLength -gt 32MB) { throw 'Release archive exceeds the total uncompressed size limit.' }
        $entries.Add($relative, (Get-EntryBytes $entry))
    }
}
finally { $archive.Dispose() }
foreach ($name in $required) {
    if (-not $entries.ContainsKey($name)) { throw "Required release file missing: $name" }
}

$utf8 = [Text.UTF8Encoding]::new($false, $true)
$manifest = $utf8.GetString($entries['manifest.json']) | ConvertFrom-Json
if ($manifest.name -cne 'Azurite' -or $manifest.version_number -cne $ExpectedVersion) {
    throw 'Manifest name/version does not match the archive release root.'
}
if (@($manifest.dependencies).Count -ne 0) { throw 'This standalone release must not declare undistributed dependencies.' }

$hashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$checksumText = $utf8.GetString($entries['SHA256SUMS.txt'])
foreach ($line in ($checksumText -split '\r?\n')) {
    if ($line.Length -eq 0) { continue }
    if ($line -notmatch '^(?<digest>[0-9A-Fa-f]{64})  (?<file>[^/\\]+)$') { throw 'Malformed SHA256SUMS.txt entry.' }
    $name = $Matches.file
    if ($name -ceq 'SHA256SUMS.txt' -or -not $entries.ContainsKey($name) -or -not $permitted.Contains($name)) {
        throw "Checksum refers to an unexpected file: $name"
    }
    if ($hashes.ContainsKey($name)) { throw "Duplicate checksum: $name" }
    $hashes.Add($name, $Matches.digest.ToLowerInvariant())
}
foreach ($name in $entries.Keys) {
    if ($name -ceq 'SHA256SUMS.txt') { continue }
    if (-not $hashes.ContainsKey($name)) { throw "Missing checksum: $name" }
    if ((Get-ByteHash $entries[$name]) -cne $hashes[$name]) { throw "SHA-256 mismatch: $name" }
}
if ($hashes.Count -ne $entries.Count - 1) { throw 'Checksum coverage does not match the complete payload.' }

$build = $null
$identity = $null
if ($entries.ContainsKey('BUILD.json')) {
    $build = $utf8.GetString($entries['BUILD.json']) | ConvertFrom-Json
    if ($build.schema -ne 1 -or $build.sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $build.sourceTree -cnotmatch '^[0-9a-f]{40}$' -or
        $build.sourceTreeClean -isnot [bool] -or -not $build.sourceTreeClean -or
        $build.version -cne $ExpectedVersion -or
        $build.candidateBuild -cnotmatch '^[a-z0-9][a-z0-9.-]*$' -or
        $build.pluginSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $build.coreSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $build.coreMode -cne 'original' -or $build.runtimeAcceptance -cne 'not-performed') {
        throw 'BUILD.json contains invalid candidate provenance or acceptance metadata.'
    }
    $hostReferences = @($build.hostReferences)
    if ($hostReferences.Count -eq 0) { throw 'BUILD.json has no host reference hashes.' }
    $hostPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($reference in $hostReferences) {
        if ($null -eq $reference -or $reference.path -cnotmatch '^[^:/\\]+(?:/[^:/\\]+)+$' -or
            ($reference.path -split '/' | Where-Object { $_ -in @('.', '..') }) -or
            $reference.sha256 -cnotmatch '^[0-9a-f]{64}$' -or -not $hostPaths.Add($reference.path)) {
            throw 'BUILD.json has invalid or duplicate host reference hashes.'
        }
    }
    if ($ExpectedSourceCommit -and $build.sourceCommit -cne $ExpectedSourceCommit) {
        throw 'BUILD.json sourceCommit does not match expected source commit.'
    }
    if ($build.pluginSha256 -cne (Get-ByteHash $entries['Azurite.dll']) -or
        $build.coreSha256 -cne (Get-ByteHash $entries['Azurite.Core.dll'])) {
        throw 'BUILD.json pluginSha256 or coreSha256 differs from archive payload.'
    }
    $identityArgs = @{
        PluginBytes = $entries['Azurite.dll']
        CoreBytes = $entries['Azurite.Core.dll']
        ManifestBytes = $entries['manifest.json']
    }
    if ($AAInstallPath) { $identityArgs.AAInstallPath = $AAInstallPath }
    if ($CecilPath) { $identityArgs.CecilPath = $CecilPath }
    $identity = & (Join-Path $PSScriptRoot 'Test-CandidateIdentity.ps1') @identityArgs
    if ($build.candidateBuild -cne $identity.CandidateBuild -or $build.version -cne $identity.Version -or
        $build.pluginSha256 -cne $identity.PluginSha256 -or $build.coreSha256 -cne $identity.CoreSha256) {
        throw 'BUILD.json version, candidateBuild, or binary hashes differ from validated identity.'
    }
} elseif ($ExpectedSourceCommit -or [version]$ExpectedVersion -ge [version]'1.0.40') {
    throw 'BUILD.json is required for provenance validation of 1.0.40 and later candidate archives.'
}

$pluginVersion = Test-ManagedAssembly $entries['Azurite.dll'] 'Azurite' "$ExpectedVersion.0"
$coreVersion = Test-ManagedAssembly $entries['Azurite.Core.dll'] 'Azurite.Core' ''
$expectedCoreHash = (Get-FileHash -LiteralPath $resolvedCore -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-ByteHash $entries['Azurite.Core.dll']) -cne $expectedCoreHash) {
    throw 'Azurite.Core.dll differs from the validated original Core reference.'
}
$pngSignature = [byte[]](137,80,78,71,13,10,26,10)
if ($entries['icon.png'].Length -lt 8) { throw 'Icon is not a PNG.' }
for ($index = 0; $index -lt 8; $index++) {
    if ($entries['icon.png'][$index] -ne $pngSignature[$index]) { throw 'Icon is not a PNG.' }
}

$archiveHash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
$sidecar = "$resolvedArchive.sha256"
if ($RequireSidecar -and -not (Test-Path -LiteralPath $sidecar -PathType Leaf)) { throw 'Archive checksum sidecar is missing.' }
if (Test-Path -LiteralPath $sidecar -PathType Leaf) {
    $expectedSidecar = "$archiveHash  $([IO.Path]::GetFileName($resolvedArchive))"
    if ([IO.File]::ReadAllText($sidecar).Trim() -cne $expectedSidecar) { throw 'Archive checksum sidecar does not match.' }
}
[pscustomobject]@{
    Archive = $resolvedArchive
    Version = $ExpectedVersion
    PluginAssemblyVersion = $pluginVersion
    CoreAssemblyVersion = $coreVersion
    PayloadFiles = $entries.Count
    SourceCommit = if ($build) { $build.sourceCommit } else { $null }
    SourceTree = if ($build) { $build.sourceTree } else { $null }
    CandidateBuild = if ($build) { $build.candidateBuild } else { $null }
    CoreMode = if ($build) { $build.coreMode } else { $null }
    SHA256 = $archiveHash
    Result = 'PASS'
}
