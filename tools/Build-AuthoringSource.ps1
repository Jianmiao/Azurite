#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AAInstallPath, [Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
$aa=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $AAInstallPath).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
$output=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$artifactRoot=Join-Path $root 'artifacts'
if(!$output.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'OutputDirectory must be a new directory under this repository artifacts/; never use an installation or stable output directory.'
}
if(Test-Path -LiteralPath $output){throw 'OutputDirectory already exists; previous outputs and failures are never overwritten.'}
if(!(Test-Path -LiteralPath (Join-Path $aa 'BepInEx/interop/Assembly-CSharp.dll') -PathType Leaf)){throw 'AAInstallPath must provide the real generated interop assemblies.'}
[void][IO.Directory]::CreateDirectory($output)
$artifacts=Join-Path $output 'build'
$pluginProject=Join-Path $root 'Plugin/Azurite.csproj'
$coreProject=Join-Path $root 'Core/Azurite.Core.csproj'
$originalCore=Join-Path $root 'references/Azurite.Core.dll'
$properties=@('-p:AzuriteCoreMode=AuthoringSource','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",'-p:Configuration=Release',"-p:AAInstallPath=$aa")
function Assert([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Hash([string]$Path){(Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()}
function Evaluate([string]$Project){
    $result=& dotnet msbuild $Project -nologo @properties '-getProperty:TargetPath,AssemblyName' '-getItem:Compile,Reference,ProjectReference' 2>&1
    Assert ($LASTEXITCODE -eq 0) ('MSBuild evaluation failed: '+($result -join "`n"))
    $result -join "`n" | ConvertFrom-Json
}
function FilePins([string[]]$Paths){
    @(foreach($path in ($Paths|Sort-Object -Unique)){
        $full=[IO.Path]::GetFullPath($path)
        Assert (Test-Path -LiteralPath $full -PathType Leaf) "Missing build input: $full"
        [ordered]@{path=$full;sha256=(Hash $full)}
    })
}
function Json($Value){ConvertTo-Json -InputObject $Value -Depth 8 -Compress}
try{
    $originalBefore=Hash $originalCore
    Assert ($originalBefore -ceq '76a46c4a896dfbdffd66c2be85e9a0e07d2d0523faaef0841763d997c65df1a5') 'Original Core reference identity is not the preserved baseline.'
    $plugin=Evaluate $pluginProject; $core=Evaluate $coreProject
    Assert ($plugin.Properties.AssemblyName -ceq 'Azurite.AuthoringSource') 'Experimental assembly identity missing.'
    Assert (@($plugin.Items.ProjectReference).Count -eq 1 -and [IO.Path]::GetFullPath($plugin.Items.ProjectReference[0].FullPath) -ceq $coreProject) 'Source Core project reference was not selected.'
    Assert (@($plugin.Items.Reference|Where-Object Identity -eq 'Azurite.Core').Count -eq 0) 'Original Core reference leaked into authoring mode.'
    $sources=@($pluginProject,$coreProject,$PSCommandPath)+@($plugin.Items.Compile.FullPath)+@($core.Items.Compile.FullPath)
    # SDK-generated sources live in this fresh output and are not pre-existing inputs.
    $sources=@($sources|Where-Object{![IO.Path]::GetFullPath($_).StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)})
    $sourceBefore=@(FilePins $sources)
    $hostPaths=@($plugin.Items.Reference|Where-Object{$_.HintPath -and ([string]$_.HintPath).StartsWith($aa+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)}|ForEach-Object HintPath)
    Assert ($hostPaths.Count -gt 0) 'No real host reference paths were evaluated.'
    $hostBefore=@(FilePins $hostPaths)
    $sdk=(& dotnet --version).Trim(); Assert ($LASTEXITCODE -eq 0) 'Cannot identify SDK.'
    $buildOutput=& dotnet build $pluginProject -c Release --no-incremental --disable-build-servers --nologo --artifacts-path $artifacts "-p:AAInstallPath=$aa" '-p:AzuriteCoreMode=AuthoringSource' 2>&1
    $buildCode=$LASTEXITCODE
    $buildOutput|Set-Content -LiteralPath (Join-Path $output 'build.log') -Encoding utf8
    Assert ($buildCode -eq 0) "Authoring source build failed ($buildCode); build.log is retained."
    $pluginPath=[IO.Path]::GetFullPath($plugin.Properties.TargetPath)
    $coreBuiltPath=[IO.Path]::GetFullPath($core.Properties.TargetPath)
    $coreCopyPath=Join-Path ([IO.Path]::GetDirectoryName($pluginPath)) 'Azurite.Core.dll'
    foreach($path in @($pluginPath,$coreBuiltPath,$coreCopyPath)){
        Assert ($path.StartsWith($artifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $path -PathType Leaf)) "Missing or escaped build output: $path"
    }
    Assert ((Hash $coreBuiltPath) -ceq (Hash $coreCopyPath)) 'Plugin output Core differs from this source build Core.'
    Assert ((Hash $originalCore) -ceq $originalBefore) 'Preserved original Core changed during build.'
    Assert ((Json @(FilePins $sources)) -ceq (Json $sourceBefore)) 'Source inputs changed during build; pair is not certified.'
    Assert ((Json @(FilePins $hostPaths)) -ceq (Json $hostBefore)) 'Host inputs changed during build; pair is not certified.'
    $cecil=Join-Path $aa 'BepInEx/core/Mono.Cecil.dll'
    $loaded=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object{$_.GetName().Name -ceq 'Mono.Cecil'})
    Assert ($loaded.Count -eq 0 -or ($loaded.Count -eq 1 -and $loaded[0].Location -ceq $cecil)) 'A different metadata reader is already loaded.'
    if($loaded.Count -eq 0){Add-Type -Path $cecil}
    $pluginAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($pluginPath)
    $coreAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($coreCopyPath)
    try{
        Assert ($pluginAssembly.Name.Name -ceq 'Azurite.AuthoringSource') 'Wrong experimental assembly name.'
        Assert (@($pluginAssembly.MainModule.Types|Where-Object FullName -eq 'Azurite.AuthoringCardPackPanel').Count -eq 1) 'Actual Panel was not compiled.'
        Assert (@($coreAssembly.MainModule.Types|Where-Object FullName -eq 'Azurite.Core.CardPackV2Workflow').Count -eq 1) 'Built Core lacks the required workflow API.'
        $reference=@($pluginAssembly.MainModule.AssemblyReferences|Where-Object Name -eq 'Azurite.Core')
        Assert ($reference.Count -eq 1 -and $reference[0].FullName -ceq $coreAssembly.Name.FullName) 'Plugin/Core assembly identity mismatch.'
        $pluginIdentity=$pluginAssembly.Name.FullName; $coreIdentity=$coreAssembly.Name.FullName
    }finally{$pluginAssembly.Dispose();$coreAssembly.Dispose()}
    $depsPath=[IO.Path]::ChangeExtension($pluginPath,'deps.json')
    $deps=Get-Content -LiteralPath $depsPath -Raw|ConvertFrom-Json
    $coreDependencies=@($deps.libraries.PSObject.Properties|Where-Object Name -like 'Azurite.Core/*')
    Assert ($coreDependencies.Count -eq 1 -and $coreDependencies[0].Value.type -ceq 'project') 'Dependency manifest does not identify source-built Core.'
    $record=[ordered]@{schema=1;coreMode='AuthoringSource';releaseEligible=$false;runtimeAcceptance='not-performed';launchesAA=$false;
        sdk=$sdk;pluginIdentity=$pluginIdentity;coreIdentity=$coreIdentity;pluginPath=$pluginPath;corePath=$coreCopyPath;coreBuildPath=$coreBuiltPath;
        pluginSha256=(Hash $pluginPath);coreSha256=(Hash $coreCopyPath);depsSha256=(Hash $depsPath);originalCoreSha256=$originalBefore;
        sourceFiles=$sourceBefore;hostReferences=$hostBefore}
    $partial=Join-Path $output 'authoring-source-build.partial'
    [IO.File]::WriteAllText($partial,($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    $receipt=Join-Path $output 'authoring-source-build.json'
    [IO.File]::Move($partial,$receipt)
    [pscustomobject]@{Result='PASS';Receipt=$receipt;Plugin=$pluginPath;Core=$coreCopyPath;ReleaseEligible=$false;LaunchesAA=$false}
}catch{
    [ordered]@{result='FAIL';message=$_.Exception.Message;launchesAA=$false;runtimeAcceptance='not-performed'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'failure.json') -Encoding utf8
    throw
}
