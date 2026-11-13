#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Portable','Host','All')][string]$Suite='Portable',
    [string]$AAInstallPath,
    [string]$AaveSourceRoot,
    [switch]$PlanOnly
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$portable=@('Tests/HostProfile.Tests.csproj','ScrollTests/ScrollTests.csproj','PreviewTests/PreviewTests.csproj',
 'PreviewLeaseTests/PreviewLeaseTests.csproj','ObserverTests/ObserverTests.csproj','AnimationTests/AnimationTests.csproj',
 'RowReuseTests/RowReuseTests.csproj','BloomTests/BloomTests.csproj','InteropTests/InteropTests.csproj',
 'CadenceTests/CadenceTests.csproj','CharacterTests/CharacterTests.csproj','ProgressiveTests/ProgressiveTests.csproj',
 'RowCacheTests/RowCacheTests.csproj','VirtualizationTests/VirtualizationTests.csproj',
 'BackgroundGalleryTests/BackgroundGalleryTests.csproj','WheelTuningTests/WheelTuningTests.csproj',
 'WheelSettingsControlTests/WheelSettingsControlTests.csproj','AuthoringSpeakerShortcutTests/AuthoringSpeakerShortcutTests.csproj',
 'HostActivityAuthoringTests/HostActivityAuthoringTests.csproj',
 'BoxSelectionTests/BoxSelectionTests.csproj','BatchEditingTests/BatchEditingTests.csproj',
 'PresetCardPackTests/PresetCardPackTests.csproj','CardPackIntegrationTests/CardPackIntegrationTests.csproj',
 'CardPackV2Tests/CardPackV2Tests.csproj',
 'AuthoringHostBridgeChecks/AuthoringHostBridgeChecks.csproj',
 'AuthoringHostContentChecks/AuthoringHostContentChecks.csproj',
 'AuthoringHostSelectionChecks/AuthoringHostSelectionChecks.csproj',
 'AuthoringHostResourceChecks/AuthoringHostResourceChecks.csproj',
 'AuthoringHostViewChecks/AuthoringHostViewChecks.csproj')
$hostProjects=@('DriverTests/DriverTests.csproj','SelectorTests/SelectorTests.csproj',
 'LoadingIntegrationTests/LoadingIntegrationTests.csproj','CpuTests/CpuTests.csproj')
$classified=@($portable)+@($hostProjects)
$discovered=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {
    $_.Name.EndsWith('Tests',[StringComparison]::Ordinal) -or $_.Name.EndsWith('Checks',[StringComparison]::Ordinal)
} | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.csproj' | ForEach-Object {[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')}
})
$unclassified=@($discovered | Where-Object {$_ -notin $classified})
if($unclassified.Count){throw ('Classify new test projects as Portable or Host before validation: '+($unclassified -join ', '))}
$jobs=[Collections.Generic.List[object]]::new()
function Add-Job([string]$Name,[string[]]$Arguments){$jobs.Add([pscustomobject]@{Name=$Name;Arguments=$Arguments})}
if($Suite -in @('Portable','All')){
    Add-Job 'Core' @('build',(Join-Path $root 'Core/Azurite.Core.csproj'),'-c','Release','--nologo','-maxcpucount:2')
    foreach($project in $portable){
        $name=($project -split '/')[0]
        Add-Job $name @('run','--project',(Join-Path $root $project),'-c','Release')
    }
}
if($Suite -in @('Host','All')){
    if(!$AAInstallPath -or !(Test-Path -LiteralPath (Join-Path $AAInstallPath 'BepInEx/interop/Assembly-CSharp.dll') -PathType Leaf)){
        throw 'Host suite requires -AAInstallPath with current generated Assembly-CSharp.dll.'
    }
    $aa=(Resolve-Path -LiteralPath $AAInstallPath).Path
    if(!(Test-Path -LiteralPath (Join-Path $aa 'BepInEx/core/Mono.Cecil.dll') -PathType Leaf)){throw 'Host suite requires Mono.Cecil.dll from the same AA installation.'}
    if(!$AaveSourceRoot -or !(Test-Path -LiteralPath (Join-Path $AaveSourceRoot 'src/VideoExport.Plugin/RenderControlV1.cs') -PathType Leaf)){
        throw 'Host suite requires -AaveSourceRoot containing production src/VideoExport.Plugin/RenderControlV1.cs.'
    }
    $renderControl=(Resolve-Path -LiteralPath (Join-Path $AaveSourceRoot 'src/VideoExport.Plugin/RenderControlV1.cs')).Path
    Add-Job 'Plugin' @('build',(Join-Path $root 'Plugin/Azurite.csproj'),'-c','Release','--nologo','-maxcpucount:2',('-p:AAInstallPath='+$aa))
    Add-Job 'DriverTests' @('run','--project',(Join-Path $root 'DriverTests/DriverTests.csproj'),'-c','Release',('-p:AAInstallPath='+$aa))
    Add-Job 'SelectorTests' @('run','--project',(Join-Path $root 'SelectorTests/SelectorTests.csproj'),'-c','Release',('-p:AAInstallPath='+$aa),'--',$aa)
    Add-Job 'LoadingIntegrationTests' @('run','--project',(Join-Path $root 'LoadingIntegrationTests/LoadingIntegrationTests.csproj'),'-c','Release','--',(Join-Path $aa 'BepInEx/interop/Assembly-CSharp.dll'))
    Add-Job 'CpuTests' @('run','--project',(Join-Path $root 'CpuTests/CpuTests.csproj'),'-c','Release',('-p:AaveRenderControlSource='+$renderControl))
    Add-Job 'HostBinding' @('run','--project',(Join-Path $root 'InteropTests/InteropTests.csproj'),'-c','Release','--',(Join-Path $root 'Plugin/bin/Release/netcoreapp6.0/Azurite.dll'),$aa)
}
$plan=[pscustomobject]@{Suite=$Suite;LaunchesAA=$false;Jobs=@($jobs);HostPrerequisitesRequired=($Suite -ne 'Portable')}
if($PlanOnly){return $plan}
foreach($job in $jobs){
    $projectIndex=[Array]::IndexOf($job.Arguments,'--project')
    $project=if($projectIndex -ge 0){$job.Arguments[$projectIndex+1]}else{$job.Arguments[1]}
    if(!(Test-Path -LiteralPath $project -PathType Leaf)){throw ('Missing test/build project: '+$project)}
}
$output=Join-Path $root ('artifacts/source-validation-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $output | Out-Null
$results=[Collections.Generic.List[object]]::new()
foreach($job in $jobs){
    $log=Join-Path $output ($job.Name+'.log')
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $arguments=$job.Arguments
    & dotnet @arguments *> $log
    $code=$LASTEXITCODE
    $clock.Stop()
    $results.Add([pscustomobject]@{Name=$job.Name;ExitCode=$code;Seconds=[Math]::Round($clock.Elapsed.TotalSeconds,2);Log=$log})
    Write-Host ($job.Name+': '+$(if($code -eq 0){'PASS'}else{'FAIL'})+' ('+$clock.Elapsed.TotalSeconds.ToString('F1')+'s)')
    if($code -ne 0){Get-Content -LiteralPath $log -Tail 12 | Write-Host}
}
$failed=@($results|Where-Object ExitCode -ne 0).Count
$report=[ordered]@{schema=1;suite=$Suite;launchesAA=$false;runtimeAcceptance='not performed';passed=$results.Count-$failed;failed=$failed;results=@($results)}
$report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
Write-Output ('Evidence: '+$output)
if($failed){throw "$failed source validation job(s) failed; see preserved logs."}
