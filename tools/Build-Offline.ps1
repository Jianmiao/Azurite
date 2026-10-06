param(
    [string]$AAInstallPath = 'E:\AzureArchive1.0fix3',
    [string]$CompilerDirectory = 'E:\AzureArchive_decompiled\mod-comparison-20261003\build-tools\roslyn-4.8.0\tasks\netcore\bincore',
    [string]$RuntimeDirectory = 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.32',
    [string]$Dotnet = 'C:\Program Files\dotnet\dotnet.exe',
    [switch]$TestsOnly
)
$ErrorActionPreference = 'Stop'
$buildRoot = Split-Path -Parent $PSScriptRoot
$buildOutput = Join-Path $buildRoot 'artifacts\verification'
New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null
$compilerPath = Join-Path $CompilerDirectory 'csc.dll'
$runtimeRefs = @(Get-ChildItem -LiteralPath $RuntimeDirectory -Filter '*.dll' | Where-Object {
    $_.Name -notlike '*Native*' -and ($_.Name -like 'System*' -or $_.Name -in @('netstandard.dll','mscorlib.dll','Microsoft.CSharp.dll'))
} | ForEach-Object { '/reference:' + $_.FullName })
$globalsPath = Join-Path $buildOutput 'GlobalUsings.cs'
[IO.File]::WriteAllText($globalsPath, 'global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;')
function Compile([string]$Name, [string]$Output, [string[]]$Sources, [string[]]$References, [switch]$Executable) {
    $arguments = @('/nologo','/noconfig','/nostdlib+','/langversion:latest','/nullable:annotations','/unsafe+','/optimize+',('/target:' + $(if ($Executable) { 'exe' } else { 'library' })),('/out:' + $Output))
    $messages = & $Dotnet $compilerPath @arguments @runtimeRefs @References @Sources 2>&1
    $compilerExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $buildOutput ($Name + '.build.log')), ($messages -join [Environment]::NewLine))
    if ($compilerExit -ne 0) { $messages; throw "Compilation failed: $Name" }
    if ($messages) { $messages }
    if ($Executable) {
        [IO.File]::WriteAllText([IO.Path]::ChangeExtension($Output, 'runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"},"rollForward":"LatestPatch"}}')
        $result = & $Dotnet $Output 2>&1
        $testExit = $LASTEXITCODE
        $result | Set-Content -LiteralPath (Join-Path $buildOutput ($Name + '.log')) -Encoding utf8
        $result | Select-Object -Last 2
        if ($testExit -ne 0) { throw "Tests failed: $Name" }
    } else { Write-Output "Built $Name" }
}
$corePath = Join-Path $buildRoot 'references\Azurite.Core.dll'
Copy-Item -LiteralPath $corePath -Destination $buildOutput -Force
$cecilPath = Join-Path $AAInstallPath 'BepInEx\core\Mono.Cecil.dll'
Copy-Item -LiteralPath $cecilPath -Destination $buildOutput -Force
if (!$TestsOnly) {
    $pluginDir = Join-Path $buildRoot 'Plugin\bin\Release\netcoreapp6.0'
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    [xml]$pluginProject = Get-Content -LiteralPath (Join-Path $buildRoot 'Plugin\Azurite.csproj') -Raw
    $pluginRefs = @(foreach ($reference in $pluginProject.SelectNodes('/Project/ItemGroup/Reference')) {
        $hint = ([string]$reference.HintPath).Replace('$(AAInstallPath)', $AAInstallPath)
        if (![IO.Path]::IsPathRooted($hint)) { $hint = Join-Path (Join-Path $buildRoot 'Plugin') $hint }
        '/reference:' + [IO.Path]::GetFullPath($hint)
    })
    $pluginSources = @(Get-ChildItem -LiteralPath (Join-Path $buildRoot 'Plugin\Azurite') -Filter '*.cs' | ForEach-Object FullName)
    $removedSources = @($pluginProject.SelectNodes('/Project/ItemGroup/Compile[@Remove]') | ForEach-Object {
        [IO.Path]::GetFullPath((Join-Path (Join-Path $buildRoot 'Plugin') $_.Remove))
    })
    $pluginSources = @($pluginSources | Where-Object { $_ -notin $removedSources })
    $pluginSources += @((Join-Path $buildRoot 'Plugin\CompilerAttributes.cs'),(Join-Path $buildRoot 'Plugin\Properties\AssemblyInfo.cs'))
    Compile 'Azurite' (Join-Path $pluginDir 'Azurite.dll') $pluginSources $pluginRefs
    Copy-Item -LiteralPath $corePath -Destination $pluginDir -Force
}
foreach ($projectPath in @(Get-ChildItem -LiteralPath $buildRoot -Directory | Where-Object { $_.Name -eq 'Tests' -or $_.Name.EndsWith('Tests') } | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' })) {
    $projectDir = Split-Path -Parent $projectPath.FullName
    [xml]$projectXml = Get-Content -LiteralPath $projectPath.FullName -Raw
    $testSources = @($globalsPath) + @(Get-ChildItem -LiteralPath $projectDir -File -Filter '*.cs' | ForEach-Object FullName)
    foreach ($include in $projectXml.SelectNodes('/Project/ItemGroup/Compile[@Include]')) {
        $sourceInclude = ([string]$include.Include).Replace('$(ProgressiveControllerSource)', '../Plugin/Azurite/ProgressiveEditorLoading.cs')
        $testSources += [IO.Path]::GetFullPath((Join-Path $projectDir $sourceInclude))
    }
    $testRefs = @()
    foreach ($reference in $projectXml.SelectNodes('/Project/ItemGroup/Reference')) {
        if ($reference.HintPath) { $testRefs += '/reference:' + [IO.Path]::GetFullPath((Join-Path $projectDir $reference.HintPath)) }
    }
    if ($projectXml.SelectNodes('/Project/ItemGroup/PackageReference[@Include="Mono.Cecil"]').Count -gt 0) { $testRefs += '/reference:' + $cecilPath }
    $testName = [IO.Path]::GetFileNameWithoutExtension($projectPath.Name)
    Compile $testName (Join-Path $buildOutput ($testName + '.dll')) $testSources $testRefs -Executable
}
