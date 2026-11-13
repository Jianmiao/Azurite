#requires -Version 7.2
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$script=Join-Path $root 'tools/Test-Source.ps1'
function Assert([bool]$value,[string]$message){if(!$value){throw $message}}
$portable=& $script -Suite Portable -PlanOnly
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'AuthoringHostBridgeChecks')) 'Portable suite must exercise the production authoring host bridge.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'AuthoringHostContentChecks')) 'Portable suite must exercise native content projection through the production source.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'AuthoringHostSelectionChecks')) 'Portable suite must exercise the production native selection adapter.'
Assert ($portable.Jobs.Count -eq 28) 'Portable suite must include Core plus all 27 classified portable test projects.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'WheelSettingsControlTests')) 'Portable suite must exercise actual wheel control.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'AuthoringSpeakerShortcutTests')) 'Portable suite must exercise actual shortcut mutation.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'HostActivityAuthoringTests')) 'Portable suite must exercise actual host authoring admission.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'CardPackV2Tests')) 'Portable suite must exercise all five card-family compilation paths.'
Assert ([bool]($portable.Jobs | Where-Object Name -eq 'BoxSelectionTests')) 'Portable suite must exercise selection freshness.'
Assert (@($portable.Jobs | Where-Object Name -in @('CpuTests','DriverTests','SelectorTests','LoadingIntegrationTests')).Count -eq 0) 'Host-dependent tests leaked into portable suite.'
Assert (!$portable.LaunchesAA) 'Source validation must not launch AA.'
$failed=$false
try { & $script -Suite Host -PlanOnly | Out-Null } catch { $failed=$_.Exception.Message -match 'AAInstallPath' }
Assert $failed 'Missing host path must fail before running any job.'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('azurite-source-plan-'+[Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path (Join-Path $temporary 'aa/BepInEx/interop'),(Join-Path $temporary 'aa/BepInEx/core'),(Join-Path $temporary 'aave/src/VideoExport.Plugin') -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $temporary 'aa/BepInEx/interop/Assembly-CSharp.dll'),[byte[]]@())
    [IO.File]::WriteAllBytes((Join-Path $temporary 'aa/BepInEx/core/Mono.Cecil.dll'),[byte[]]@())
    [IO.File]::WriteAllText((Join-Path $temporary 'aave/src/VideoExport.Plugin/RenderControlV1.cs'),'fixture')
    $before=@(Get-ChildItem -LiteralPath $temporary -Recurse -Force).Count
    $hostPlan=& $script -Suite Host -AAInstallPath (Join-Path $temporary 'aa') -AaveSourceRoot (Join-Path $temporary 'aave') -PlanOnly
    Assert ($hostPlan.Jobs.Count -eq 6) 'Host suite must include plugin, four external tests, and actual DLL binding checks.'
    $cpu=@($hostPlan.Jobs | Where-Object Name -eq 'CpuTests')[0]
    Assert ([bool]($cpu.Arguments | Where-Object {$_ -like '-p:AaveRenderControlSource=*RenderControlV1.cs'})) 'CPU test must receive resolved production source explicitly.'
    Assert (@(Get-ChildItem -LiteralPath $temporary -Recurse -Force).Count -eq $before) 'PlanOnly mutated the fixture tree.'
} finally {
    $full=[IO.Path]::GetFullPath($temporary)
    if(!$full.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notlike 'azurite-source-plan-*'){throw 'Unsafe temporary cleanup path.'}
    if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
'PASS source suite planning and explicit dependency guards'
