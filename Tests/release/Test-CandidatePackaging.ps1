#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CecilPath,
    [Parameter(Mandatory)][string]$LegacyArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$identityScript = Join-Path $root 'tools/Test-CandidateIdentity.ps1'
$archiveScript = Join-Path $root 'tools/Test-ReleaseArchive.ps1'
$packageScript = Join-Path $root 'tools/Package-Release.ps1'
$originalCore = Join-Path $root 'references/Azurite.Core.dll'
$cecil = (Resolve-Path -LiteralPath $CecilPath).Path
if (-not (Test-Path -LiteralPath $cecil -PathType Leaf)) { throw 'CecilPath must be a file.' }
$oldArchive = (Resolve-Path -LiteralPath $LegacyArchivePath).Path
if (-not (Test-Path -LiteralPath $oldArchive -PathType Leaf)) { throw 'LegacyArchivePath must be a file.' }
if ((Get-FileHash -LiteralPath $oldArchive -Algorithm SHA256).Hash -cne '71808F0775AED02CB11DDC67FB5D6F9201E25634006390C372CBDD810C062DD5') {
    throw 'LegacyArchivePath must identify the immutable validated 1.0.38 archive.'
}
Add-Type -Path $cecil

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Fails([scriptblock]$Action, [string]$Pattern) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $_.Exception.Message -match $Pattern }
    Assert $failed "Expected failure matching '$Pattern'."
}
function Edit-Plugin([string]$Path, [scriptblock]$Mutation) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        & $Mutation $assembly
        $replacement = "$Path.mutated"
        $assembly.Write($replacement)
    }
    finally { $assembly.Dispose() }
    Move-Item -LiteralPath $replacement -Destination $Path -Force
}
function Set-ZipEntries([string]$Path, [hashtable]$Updates) {
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($name in $Updates.Keys) {
            $entry = $archive.GetEntry($name)
            if ($entry) { $entry.Delete() }
            if ($null -eq $Updates[$name]) { continue }
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $stream = $entry.Open()
            try {
                $bytes = [byte[]]$Updates[$name]
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
function Get-ZipBytes([string]$Path, [string]$Name) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($Name)
        if (-not $entry) { throw "Missing fixture ZIP entry: $Name" }
        $stream = $entry.Open()
        $buffer = [IO.MemoryStream]::new()
        try { $stream.CopyTo($buffer); return ,$buffer.ToArray() }
        finally { $stream.Dispose(); $buffer.Dispose() }
    }
    finally { $archive.Dispose() }
}
function Update-ZipChecksums([string]$Path, [string]$Prefix) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $lines = foreach ($entry in $archive.Entries) {
            if ($entry.FullName -ceq ($Prefix + 'SHA256SUMS.txt')) { continue }
            $stream = $entry.Open()
            $buffer = [IO.MemoryStream]::new()
            try {
                $stream.CopyTo($buffer)
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buffer.ToArray())).ToLowerInvariant()
                "$hash  $([IO.Path]::GetFileName($entry.FullName))"
            }
            finally { $stream.Dispose(); $buffer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    Set-ZipEntries $Path @{ ($Prefix + 'SHA256SUMS.txt') = [Text.UTF8Encoding]::new($false).GetBytes(($lines -join "`n") + "`n") }
}
function Write-FixtureReceipt([string]$Path, $Identity, [string]$Commit, [string]$Tree, [string]$PluginHash) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    $record = [ordered]@{
        schema = 1
        sourceCommit = $Commit
        sourceTree = $Tree
        sourceTreeClean = $true
        version = $Identity.Version
        candidateBuild = $Identity.CandidateBuild
        pluginSha256 = $PluginHash
        coreSha256 = $Identity.CoreSha256
        coreMode = $Identity.CoreMode
        hostReferences = @(@{ path = 'BepInEx/core/Mono.Cecil.dll'; sha256 = (Get-FileHash -LiteralPath $cecil -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
    [IO.File]::WriteAllText($Path, (($record | ConvertTo-Json -Depth 5) + "`n"), [Text.UTF8Encoding]::new($false))
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('azurite-candidate-packaging-' + [Guid]::NewGuid().ToString('N'))
try {
    $fixture = Join-Path $temporary 'source'
    foreach ($directory in @('assets', 'references', 'Plugin/bin/Release/netcoreapp6.0')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $fixture $directory))
    }
    $plugin = Join-Path $fixture 'Plugin/bin/Release/netcoreapp6.0/Azurite.dll'
    $core = Join-Path $fixture 'references/Azurite.Core.dll'
    $manifest = Join-Path $fixture 'assets/manifest.json'
    $originalPlugin = Join-Path $temporary 'original-1.0.38.dll'
    [IO.File]::WriteAllBytes($originalPlugin, (Get-ZipBytes $oldArchive 'Azurite/1.0.38/Azurite.dll'))
    Copy-Item -LiteralPath $originalPlugin -Destination $plugin
    Copy-Item -LiteralPath $originalCore -Destination $core
    [IO.File]::WriteAllText($manifest, '{"name":"Azurite","version_number":"1.0.38","dependencies":[]}')
    Copy-Item -LiteralPath (Join-Path $root 'assets/icon.png') -Destination (Join-Path $fixture 'assets/icon.png')
    [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), "/artifacts/`n")

    $identity = & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest -CecilPath $cecil
    Assert ($identity.Version -ceq '1.0.38') 'Candidate identity version differs from fixture manifest.'
    Assert ($identity.CandidateBuild -ceq 'wheel-settings-layout-2') 'Candidate build marker differs from fixture binary.'
    Assert-Fails { & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest } 'Supply AAInstallPath or CecilPath'
    Write-Output 'PASS candidate binary identity'

    $identityCases = @(
        @{ Name = 'assembly version'; Pattern = 'assembly version'; Mutation = {
            param($assembly) $assembly.Name.Version = [Version]'1.0.37.0'
        } },
        @{ Name = 'file version'; Pattern = 'file version'; Mutation = {
            param($assembly)
            $attribute = @($assembly.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'System.Reflection.AssemblyFileVersionAttribute' })[0]
            $attribute.ConstructorArguments[0] = [Mono.Cecil.CustomAttributeArgument]::new($assembly.MainModule.TypeSystem.String, '1.0.37.0')
        } },
        @{ Name = 'informational version'; Pattern = 'informational version'; Mutation = {
            param($assembly)
            $attribute = @($assembly.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'System.Reflection.AssemblyInformationalVersionAttribute' })[0]
            $attribute.ConstructorArguments[0] = [Mono.Cecil.CustomAttributeArgument]::new($assembly.MainModule.TypeSystem.String, '1.0.38-candidate.other')
        } },
        @{ Name = 'missing informational metadata'; Pattern = 'AssemblyInformationalVersionAttribute'; Mutation = {
            param($assembly)
            $attribute = @($assembly.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'System.Reflection.AssemblyInformationalVersionAttribute' })[0]
            [void]$assembly.CustomAttributes.Remove($attribute)
        } },
        @{ Name = 'BepInPlugin version'; Pattern = 'BepInPlugin'; Mutation = {
            param($assembly)
            $type = @($assembly.MainModule.Types | Where-Object FullName -eq 'Azurite.Plugin')[0]
            $attribute = @($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'BepInEx.BepInPlugin' })[0]
            $attribute.ConstructorArguments[2] = [Mono.Cecil.CustomAttributeArgument]::new($assembly.MainModule.TypeSystem.String, '1.0.37')
        } },
        @{ Name = 'Plugin.Version'; Pattern = 'Plugin.Version'; Mutation = {
            param($assembly)
            $type = @($assembly.MainModule.Types | Where-Object FullName -eq 'Azurite.Plugin')[0]
            @($type.Fields | Where-Object Name -eq 'Version')[0].Constant = '1.0.37'
        } },
        @{ Name = 'CandidateBuild'; Pattern = 'candidate build marker'; Mutation = {
            param($assembly)
            $type = @($assembly.MainModule.Types | Where-Object FullName -eq 'Azurite.Plugin')[0]
            @($type.Fields | Where-Object Name -eq 'CandidateBuild')[0].Constant = 'other'
        } }
    )
    foreach ($case in $identityCases) {
        Copy-Item -LiteralPath $originalPlugin -Destination $plugin -Force
        Edit-Plugin $plugin $case.Mutation
        Assert-Fails { & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest -CecilPath $cecil } $case.Pattern
    }
    Copy-Item -LiteralPath $originalPlugin -Destination $plugin -Force
    [IO.File]::WriteAllBytes($plugin, [byte[]](1, 2, 3, 4))
    Assert-Fails { & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest -CecilPath $cecil } 'managed assembly metadata'
    Copy-Item -LiteralPath $originalPlugin -Destination $plugin -Force
    $coreBytes = [IO.File]::ReadAllBytes($core)
    $coreBytes[0] = $coreBytes[0] -bxor 1
    [IO.File]::WriteAllBytes($core, $coreBytes)
    Assert-Fails { & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest -CecilPath $cecil } 'validated original Core'
    Copy-Item -LiteralPath $originalCore -Destination $core -Force
    [IO.File]::WriteAllText($manifest, '{"name":"Azurite","version_number":"1.0.37","dependencies":[]}')
    Assert-Fails { & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest -CecilPath $cecil } 'assembly version'
    [IO.File]::WriteAllText($manifest, '{"name":"Azurite","version_number":"1.0.38","dependencies":[]}')
    Write-Output ('PASS identity rejects {0} metadata and payload mismatches' -f ($identityCases.Count + 3))

    $legacy = & $archiveScript -ArchivePath $oldArchive -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore
    Assert ($legacy.Result -ceq 'PASS') 'Existing 1.0.38 archive was rejected.'
    Write-Output 'PASS legacy archive without BUILD.json'

    & git -C $fixture init -q
    & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid add .
    & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit -qm fixture
    if ($LASTEXITCODE -ne 0) { throw 'Fixture Git commit failed.' }
    $commit = (& git -C $fixture rev-parse HEAD).Trim()
    $tree = (& git -C $fixture rev-parse 'HEAD^{tree}').Trim()
    $receipt = Join-Path $fixture 'artifacts/candidate-build-receipt.json'
    $output = Join-Path $temporary 'packages'
    [void][IO.Directory]::CreateDirectory($output)
    $dirty = Join-Path $fixture 'dirty.txt'
    [IO.File]::WriteAllText($dirty, 'uncommitted')
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'dirty|clean'
    Assert (-not (Test-Path -LiteralPath (Join-Path $output 'Azurite-1.0.38.zip'))) 'Dirty source published an archive.'
    Remove-Item -LiteralPath $dirty
    Write-Output 'PASS dirty source refused before publication'

    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'build receipt'
    Assert (-not (Test-Path -LiteralPath (Join-Path $output 'Azurite-1.0.38.zip'))) 'Missing receipt published an archive.'
    Write-FixtureReceipt $receipt $identity ('0' * 40) $tree $identity.PluginSha256
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'build receipt|sourceCommit'
    Write-FixtureReceipt $receipt $identity $commit $tree ('0' * 64)
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'build receipt|pluginSha256'
    Write-FixtureReceipt $receipt $identity $commit ('0' * 40) $identity.PluginSha256
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'build receipt|sourceTree'
    Write-FixtureReceipt $receipt $identity $commit $tree $identity.PluginSha256
    $wrongCore = [IO.File]::ReadAllText($receipt) | ConvertFrom-Json
    $wrongCore.coreSha256 = '0' * 64
    [IO.File]::WriteAllText($receipt, ($wrongCore | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'build receipt|coreSha256'
    Write-FixtureReceipt $receipt $identity $commit $tree $identity.PluginSha256
    Write-Output 'PASS absent, stale commit/tree, and stale plugin/Core receipts refused'

    $package = & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil
    $zip = Join-Path $output 'Azurite-1.0.38.zip'
    $prefix = 'Azurite/1.0.38/'
    Assert ($package.Result -ceq 'PASS') 'Clean package did not validate.'
    Assert (Test-Path -LiteralPath "$zip.sha256") 'Archive sidecar is missing.'
    $build = [Text.UTF8Encoding]::new($false, $true).GetString((Get-ZipBytes $zip ($prefix + 'BUILD.json'))) | ConvertFrom-Json
    Assert ($build.schema -eq 1 -and $build.sourceCommit -ceq $commit -and $build.sourceTreeClean -eq $true) 'BUILD.json source provenance differs from fixture commit.'
    Assert ($build.version -ceq '1.0.38' -and $build.candidateBuild -ceq 'wheel-settings-layout-2') 'BUILD.json candidate version differs from binary.'
    Assert ($build.coreMode -ceq 'original' -and $build.runtimeAcceptance -ceq 'not-performed') 'BUILD.json overstates validation.'
    Assert ($build.pluginSha256 -ceq $identity.PluginSha256 -and $build.coreSha256 -ceq $identity.CoreSha256) 'BUILD.json payload hashes differ from copied binaries.'
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil } 'overwrite'
    Write-Output 'PASS clean package provenance, checksums, and overwrite refusal'

    $tampered = Join-Path $temporary 'tampered.zip'
    Copy-Item -LiteralPath $zip -Destination $tampered
    $build.pluginSha256 = '0' * 64
    Set-ZipEntries $tampered @{ ($prefix + 'BUILD.json') = [Text.UTF8Encoding]::new($false).GetBytes(($build | ConvertTo-Json -Depth 4)) }
    Update-ZipChecksums $tampered $prefix
    Assert-Fails { & $archiveScript -ArchivePath $tampered -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore -CecilPath $cecil } 'BUILD.json|pluginSha256'

    Copy-Item -LiteralPath $zip -Destination $tampered -Force
    $build = [Text.UTF8Encoding]::new($false, $true).GetString((Get-ZipBytes $tampered ($prefix + 'BUILD.json'))) | ConvertFrom-Json
    $build.sourceCommit = 'not-a-commit'
    Set-ZipEntries $tampered @{ ($prefix + 'BUILD.json') = [Text.UTF8Encoding]::new($false).GetBytes(($build | ConvertTo-Json -Depth 4)) }
    Update-ZipChecksums $tampered $prefix
    Assert-Fails { & $archiveScript -ArchivePath $tampered -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore -CecilPath $cecil } 'BUILD.json'
    Assert-Fails { & $archiveScript -ArchivePath $zip -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore -CecilPath $cecil -ExpectedSourceCommit ('0' * 40) } 'sourceCommit'

    Copy-Item -LiteralPath $zip -Destination $tampered -Force
    $checksums = [Text.UTF8Encoding]::new($false).GetString((Get-ZipBytes $tampered ($prefix + 'SHA256SUMS.txt')))
    $checksums = (($checksums -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '  BUILD\.json$' }) -join "`n") + "`n"
    Set-ZipEntries $tampered @{ ($prefix + 'SHA256SUMS.txt') = [Text.UTF8Encoding]::new($false).GetBytes($checksums) }
    Assert-Fails { & $archiveScript -ArchivePath $tampered -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore -CecilPath $cecil } 'Missing checksum: BUILD.json'

    Copy-Item -LiteralPath $zip -Destination $tampered -Force
    $pluginPayload = Join-Path $temporary 'mutated-plugin.dll'
    [IO.File]::WriteAllBytes($pluginPayload, (Get-ZipBytes $tampered ($prefix + 'Azurite.dll')))
    Edit-Plugin $pluginPayload { param($assembly) $assembly.Name.Version = [Version]'1.0.37.0' }
    Set-ZipEntries $tampered @{ ($prefix + 'Azurite.dll') = [IO.File]::ReadAllBytes($pluginPayload) }
    $build = [Text.UTF8Encoding]::new($false, $true).GetString((Get-ZipBytes $tampered ($prefix + 'BUILD.json'))) | ConvertFrom-Json
    $build.pluginSha256 = (Get-FileHash -LiteralPath $pluginPayload -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-ZipEntries $tampered @{ ($prefix + 'BUILD.json') = [Text.UTF8Encoding]::new($false).GetBytes(($build | ConvertTo-Json -Depth 4)) }
    Update-ZipChecksums $tampered $prefix
    Assert-Fails { & $archiveScript -ArchivePath $tampered -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore -CecilPath $cecil } 'assembly version'

    Copy-Item -LiteralPath $zip -Destination $tampered -Force
    Set-ZipEntries $tampered @{ ($prefix + 'manifest.json') = [Text.UTF8Encoding]::new($false).GetBytes('{"name":"Azurite","version_number":"1.0.37","dependencies":[]}') }
    Update-ZipChecksums $tampered $prefix
    Assert-Fails { & $archiveScript -ArchivePath $tampered -ExpectedVersion '1.0.38' -CoreReferencePath $originalCore -CecilPath $cecil } 'Manifest name/version'
    Write-Output 'PASS archive rejects tampered BUILD.json, checksum coverage, binary metadata, and version'

    [IO.File]::WriteAllText((Join-Path $fixture 'source-only.txt'), 'behavior fix without rebuild')
    & git -C $fixture add source-only.txt
    & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit -qm source-only-change
    if ($LASTEXITCODE -ne 0) { throw 'Source-only fixture Git commit failed.' }
    $staleOutput = Join-Path $temporary 'stale-packages'
    [void][IO.Directory]::CreateDirectory($staleOutput)
    Assert-Fails { & $packageScript -SourceRoot $fixture -OutputDirectory $staleOutput -CecilPath $cecil } 'build receipt|sourceCommit'
    Assert (-not (Test-Path -LiteralPath (Join-Path $staleOutput 'Azurite-1.0.38.zip'))) 'Source-only commit published stale binary.'
    Write-Output 'PASS source-only commit invalidates build receipt'

    Edit-Plugin $plugin {
        param($assembly)
        $assembly.Name.Version = [Version]'1.0.40.0'
        foreach ($name in @('AssemblyFileVersionAttribute', 'AssemblyInformationalVersionAttribute')) {
            $attribute = @($assembly.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq "System.Reflection.$name" })[0]
            $value = if ($name -ceq 'AssemblyFileVersionAttribute') { '1.0.40.0' } else { '1.0.40-candidate.wheel-settings-layout-2' }
            $attribute.ConstructorArguments[0] = [Mono.Cecil.CustomAttributeArgument]::new($assembly.MainModule.TypeSystem.String, $value)
        }
        $type = @($assembly.MainModule.Types | Where-Object FullName -eq 'Azurite.Plugin')[0]
        $bep = @($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'BepInEx.BepInPlugin' })[0]
        $bep.ConstructorArguments[2] = [Mono.Cecil.CustomAttributeArgument]::new($assembly.MainModule.TypeSystem.String, '1.0.40')
        @($type.Fields | Where-Object Name -eq 'Version')[0].Constant = '1.0.40'
    }
    [IO.File]::WriteAllText($manifest, '{"name":"Azurite","version_number":"1.0.40","dependencies":[]}')
    & git -C $fixture add .
    & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit -qm candidate-1.0.40
    if ($LASTEXITCODE -ne 0) { throw '1.0.40 fixture Git commit failed.' }
    $futureIdentity = & $identityScript -PluginPath $plugin -CorePath $core -ManifestPath $manifest -CecilPath $cecil
    Write-FixtureReceipt $receipt $futureIdentity (& git -C $fixture rev-parse HEAD).Trim() (& git -C $fixture rev-parse 'HEAD^{tree}').Trim() $futureIdentity.PluginSha256
    $future = & $packageScript -SourceRoot $fixture -OutputDirectory $output -CecilPath $cecil
    Assert ($future.Result -ceq 'PASS') '1.0.40 fixture package did not validate.'
    $futureZip = Join-Path $output 'Azurite-1.0.40.zip'
    $futurePrefix = 'Azurite/1.0.40/'
    Copy-Item -LiteralPath $futureZip -Destination $tampered -Force
    Set-ZipEntries $tampered @{ ($futurePrefix + 'BUILD.json') = $null }
    Update-ZipChecksums $tampered $futurePrefix
    Assert-Fails { & $archiveScript -ArchivePath $tampered -ExpectedVersion '1.0.40' -CoreReferencePath $originalCore -CecilPath $cecil } 'BUILD.json is required'
    Write-Output 'PASS 1.0.40 archive requires BUILD.json'
}
finally {
    $full = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($full) -notlike 'azurite-candidate-packaging-*') {
        throw 'Unsafe temporary cleanup path.'
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
