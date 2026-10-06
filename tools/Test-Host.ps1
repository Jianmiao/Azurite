param(
    [Parameter(Mandatory)][string]$AAInstallPath,
    [string]$Dotnet = 'C:\Program Files\dotnet\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$verificationRoot = Join-Path $sourceRoot 'artifacts\verification'
$pluginPath = Join-Path $sourceRoot 'Plugin\bin\Release\netcoreapp6.0\Azurite.dll'
$contractTests = Join-Path $verificationRoot 'InteropTests.dll'
$profileTests = Join-Path $verificationRoot 'HostProfile.Tests.dll'
foreach ($file in @($pluginPath, $contractTests, $profileTests)) {
    if (!(Test-Path -LiteralPath $file)) { throw 'Build the plugin and source-linked tests with Build-Offline.ps1 first.' }
}
$gameRoot = (Resolve-Path -LiteralPath $AAInstallPath).Path
$gameHash = (Get-FileHash -LiteralPath (Join-Path $gameRoot 'GameAssembly.dll') -Algorithm SHA256).Hash
$metadataHash = (Get-FileHash -LiteralPath (Join-Path $gameRoot 'AzureArchive_Data\il2cpp_data\Metadata\global-metadata.dat') -Algorithm SHA256).Hash
$interopHash = (Get-FileHash -LiteralPath (Join-Path $gameRoot 'BepInEx\interop\Assembly-CSharp.dll') -Algorithm SHA256).Hash
$unityHash = (Get-FileHash -LiteralPath (Join-Path $gameRoot 'BepInEx\interop\UnityEngine.CoreModule.dll') -Algorithm SHA256).Hash
# Load only the pure policy test assembly, never an AA/Unity/BepInEx assembly.
$profileAssembly = [Reflection.Assembly]::LoadFrom($profileTests)
$profileType = $profileAssembly.GetType('Azurite.HostProfile', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$portable = [bool]$profileType.GetMethod('IsPortableCandidate', $flags).Invoke($null, @($gameHash, $metadataHash))
$legacy = $profileType.GetMethod('Resolve', $flags).Invoke($null, @($gameHash, $interopHash, $unityHash))
$legacySupported = [bool]$legacy.GetType().GetProperty('Supported').GetValue($legacy)
$contractOutput = & $Dotnet $contractTests $pluginPath $gameRoot 2>&1
$contractExit = $LASTEXITCODE
$contractOutput | Set-Content -LiteralPath (Join-Path $verificationRoot 'HostBinding.log') -Encoding utf8
$result = [pscustomobject]@{
    InstallPath = $gameRoot
    NativeGameSha256 = $gameHash
    MetadataSha256 = $metadataHash
    PortableCandidate = $portable
    LegacyCandidate = $legacySupported
    BindingContractPassed = $contractExit -eq 0
    NativeHostAccepted = ($portable -and $contractExit -eq 0) -or $legacySupported
    StaticInspectionOnly = $true
    ContractReport = ($contractOutput -join [Environment]::NewLine)
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $verificationRoot 'HostCheck.json') -Encoding utf8
$result
