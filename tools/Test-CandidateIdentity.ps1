#requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Files')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Files')][string]$PluginPath,
    [Parameter(Mandatory, ParameterSetName = 'Files')][string]$CorePath,
    [Parameter(Mandatory, ParameterSetName = 'Files')][string]$ManifestPath,
    [Parameter(Mandatory, ParameterSetName = 'Bytes')][byte[]]$PluginBytes,
    [Parameter(Mandatory, ParameterSetName = 'Bytes')][byte[]]$CoreBytes,
    [Parameter(Mandatory, ParameterSetName = 'Bytes')][byte[]]$ManifestBytes,
    [string]$AAInstallPath,
    [string]$CecilPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$originalCoreSha256 = '76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5'

if ($AAInstallPath -and $CecilPath) { throw 'Specify either AAInstallPath or CecilPath, not both.' }
if ($AAInstallPath) { $CecilPath = Join-Path $AAInstallPath 'BepInEx/core/Mono.Cecil.dll' }
if (-not $CecilPath) { throw 'Supply AAInstallPath or CecilPath explicitly for metadata validation.' }
$resolvedCecil = (Resolve-Path -LiteralPath $CecilPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedCecil -PathType Leaf)) { throw 'Mono.Cecil.dll must be a file.' }
$loadedCecil = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -ceq 'Mono.Cecil' })
if ($loadedCecil.Count -gt 0) {
    if ($loadedCecil.Count -ne 1 -or $loadedCecil[0].Location -cne $resolvedCecil) {
        throw 'A different Mono.Cecil assembly is already loaded; start a new PowerShell process.'
    }
} else {
    Add-Type -Path $resolvedCecil
}

if ($PSCmdlet.ParameterSetName -ceq 'Files') {
    $PluginBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $PluginPath).Path)
    $CoreBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $CorePath).Path)
    $ManifestBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ManifestPath).Path)
}
function Get-ByteHash([byte[]]$Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}
function Get-AttributeString($Attributes, [string]$TypeName, [int]$ArgumentIndex) {
    $matches = @($Attributes | Where-Object { $_.AttributeType.FullName -ceq $TypeName })
    if ($matches.Count -ne 1 -or $matches[0].ConstructorArguments.Count -le $ArgumentIndex -or
        $matches[0].ConstructorArguments[$ArgumentIndex].Value -isnot [string]) {
        throw "Missing or ambiguous $TypeName metadata."
    }
    [string]$matches[0].ConstructorArguments[$ArgumentIndex].Value
}
function Get-PluginConstant($Type, [string]$Name) {
    $matches = @($Type.Fields | Where-Object { $_.Name -ceq $Name })
    if ($matches.Count -ne 1 -or -not $matches[0].IsLiteral -or -not $matches[0].HasConstant -or
        $matches[0].FieldType.FullName -cne 'System.String' -or $matches[0].Constant -isnot [string]) {
        throw "Missing or invalid Azurite.Plugin.$Name string constant."
    }
    [string]$matches[0].Constant
}

$manifest = [Text.UTF8Encoding]::new($false, $true).GetString($ManifestBytes) | ConvertFrom-Json
$version = [string]$manifest.version_number
if ($manifest.name -cne 'Azurite' -or $version -cnotmatch '^\d+\.\d+\.\d+$') {
    throw 'Manifest must identify Azurite and a three-part numeric version.'
}
$coreHash = Get-ByteHash $CoreBytes
if ($coreHash -cne $originalCoreSha256) {
    throw "Azurite.Core.dll differs from the validated original Core: $coreHash"
}

$inputStream = [IO.MemoryStream]::new($PluginBytes, $false)
try {
    try { $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputStream) }
    catch { throw "Azurite.dll has no readable managed assembly metadata: $($_.Exception.Message)" }
    try {
        if ($assembly.Name.Name -cne 'Azurite' -or $assembly.MainModule.Kind -ne [Mono.Cecil.ModuleKind]::Dll) {
            throw 'Azurite.dll has the wrong assembly name or module kind.'
        }
        if ($assembly.Name.Version.ToString() -cne "$version.0") {
            throw "Plugin assembly version does not match manifest $version."
        }
        $fileVersion = Get-AttributeString $assembly.CustomAttributes 'System.Reflection.AssemblyFileVersionAttribute' 0
        if ($fileVersion -cne "$version.0") { throw 'Plugin file version does not match manifest.' }
        $informationalVersion = Get-AttributeString $assembly.CustomAttributes 'System.Reflection.AssemblyInformationalVersionAttribute' 0
        $pluginTypes = @($assembly.MainModule.Types | Where-Object { $_.FullName -ceq 'Azurite.Plugin' })
        if ($pluginTypes.Count -ne 1) { throw 'Azurite.Plugin type metadata is missing or ambiguous.' }
        $pluginType = $pluginTypes[0]
        $bepId = Get-AttributeString $pluginType.CustomAttributes 'BepInEx.BepInPlugin' 0
        $bepVersion = Get-AttributeString $pluginType.CustomAttributes 'BepInEx.BepInPlugin' 2
        if ($bepId -cne 'halocue.azurite' -or $bepVersion -cne $version) {
            throw 'BepInPlugin identity/version does not match manifest.'
        }
        $constantVersion = Get-PluginConstant $pluginType 'Version'
        if ($constantVersion -cne $version) { throw 'Plugin.Version constant does not match manifest.' }
        $candidateBuild = Get-PluginConstant $pluginType 'CandidateBuild'
        if ($candidateBuild -cnotmatch '^[a-z0-9][a-z0-9.-]*$') { throw 'Invalid Plugin.CandidateBuild marker.' }
        if ($informationalVersion -cne "$version-candidate.$candidateBuild") {
            throw 'Plugin informational version does not match candidate build marker.'
        }
    }
    finally { $assembly.Dispose() }
}
finally { $inputStream.Dispose() }

[pscustomobject]@{
    Version = $version
    CandidateBuild = $candidateBuild
    PluginSha256 = Get-ByteHash $PluginBytes
    CoreSha256 = $coreHash
    CoreMode = 'original'
    InformationalVersion = $informationalVersion
    Result = 'PASS'
}
