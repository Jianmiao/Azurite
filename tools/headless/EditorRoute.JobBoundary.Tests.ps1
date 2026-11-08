#requires -Version 7.0
# Investigation-only runner: system compiler and harmless owned managed children.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -DisableNameChecking
$root=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))) ('editor-route-boundary-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$helper=Join-Path $root 'RestrictedCliNative.dll';[void](New-AzuriteNetFrameworkHelper (Join-Path $PSScriptRoot 'RestrictedCliNative.cs') $helper)
$exe=Join-Path $root 'EditorRouteBoundaryFixture.exe'
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildInfo=New-AzuriteProcessInfo $compiler @('/nologo','/target:exe',('/reference:'+$helper),('/out:'+$exe),(Join-Path $PSScriptRoot 'EditorRoute.JobBoundaryFixture.cs.txt')) $root
$build=[Azurite.Restricted.ProcessPump]::Run($buildInfo,10000,$null,16384,@(),1048576)
Write-AzuriteFlushedJson (Join-Path $root 'build.json') $build
if((Get-AzuriteProcessStatus $build 'Helper compiler').failed){throw ('Fixture compilation failed: '+$build.Stderr+$build.Stdout)}
$id=[guid]::NewGuid().ToString('N');$info=New-AzuriteProcessInfo $exe @('bootstrap',$id,$root) $root
$outer=[Azurite.Restricted.ProcessPump]::RunBootstrap($info,60000,5000,$id,4096,@($root),1048576)
Write-AzuriteFlushedJson (Join-Path $root 'outer.json') $outer
Assert-AzuriteCleanupConfirmed $outer
if((Get-AzuriteOuterProcessStatus $outer).failed){throw ('Harmless boundary fixture failed; retained: '+$root)}
$events=@(Get-Content -LiteralPath (Join-Path $root 'boundary-observations.jsonl')|ForEach-Object {$_|ConvertFrom-Json})
$observations=@($events|Where-Object {$_.PSObject.Properties['memberCount']})
[pscustomobject]@{fixtureRoot=$root;immediatePlainCount=@($observations|Where-Object label -Like 'Plain*-immediate').Count;immediatePlainExtraCount=@($observations|Where-Object {$_.label -like 'Plain*-immediate' -and $_.memberCount -gt 1}).Count;heldHandleObservations=@($observations|Where-Object label -Like 'HoldExitedHandle*');heldHandleStatus=@($events|Where-Object label -EQ 'held-direct-handle-status');liveHelperObservations=@($observations|Where-Object label -Like 'LiveHelper*');cleanupVerified=$outer.CleanupVerified;remainingOwnedProcesses=$outer.RemainingOwnedProcesses}|ConvertTo-Json -Depth 8
