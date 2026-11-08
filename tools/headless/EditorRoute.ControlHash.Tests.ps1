#requires -Version 7.0
# Runs the actual production stager/effect verifier against workspace-only paths.
# Only the fixed lab location and shared-state readers are replaced in this test's
# module session; production launch/admission code is never called.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'EditorRouteCore.psm1') -DisableNameChecking
Initialize-AzuriteRestrictedNative
$root=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))) ('editor-route-controlhash-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;Set-AzuriteLabAcl $root $root $sid
$lab=Join-Path $root 'lab';$state=Join-Path $root 'mock-shared-state'
foreach($path in @($lab,$state)){[void][IO.Directory]::CreateDirectory($path);Set-AzuriteLabAcl $root $path $sid}
[IO.File]::WriteAllText((Join-Path $state 'fixture.json'),'{"fixture":true}')
$pins=[ordered]@{};foreach($name in Get-AzuriteEditorRouteToolNames){$pins[$name]=Get-AzuriteSha256 (Join-Path $PSScriptRoot $name)}
$context=[pscustomobject]@{sid=$sid;profile='C:\fixture-profile';environment=[pscustomobject]@{fixtureOnly=$true};registeredLow=[Azurite.Restricted.KnownFolders]::LocalLow();dailyInstall='C:\unused-fixture-daily';contract=[pscustomobject]@{toolPins=[pscustomobject]$pins;fixtureOnly=$true}}
$module=Get-Module EditorRouteCore
$result=& $module {
 param($FixtureLab,$FixtureState,$Context)
 $oldLab=$script:EditorLab;$script:EditorLab=$FixtureLab;$script:ControlHashFixtureState=$FixtureState
 function script:New-AzuriteSharedStateSpec([string]$LocalLow,[string]$DailyInstall,[string]$IdentitySid){return [pscustomobject]@{schema=1;identitySid=$IdentitySid;registryPath='Software\foxxlight\AzureArchive';roots=@([pscustomobject]@{label='LocalLowAA';path=$script:ControlHashFixtureState;kind='Directory';required=$true})}}
 function script:Read-AzuritePlayerPrefsSnapshot {return [pscustomobject]@{exists=$false;keys=@();values=@()}}
 try{
  $run=New-AzuriteEditorRouteRun $Context ProbeMissing 120
  $path=Join-Path $run.spec.runRoot 'tooling\control.json'
  $readback=Read-AzuriteBoundedJson $path
  $observation=[ordered]@{fixtureOnly=$true;runType=$run.GetType().FullName;runObjectCount=@($run).Count;controlExpected=$run.controlSha256;expectedType=$run.controlSha256.GetType().FullName;expectedLength=([string]$run.controlSha256).Length;controlActual=Get-AzuriteSha256 $path;controlPath=$path;expectedMatchesActual=($run.controlSha256 -ceq (Get-AzuriteSha256 $path));runtimeToolHashType=$run.control.toolHashes.GetType().FullName;runtimeToolHashPropertyNames=@($run.control.toolHashes.PSObject.Properties.Name);serializedToolHashType=$readback.toolHashes.GetType().FullName;serializedToolHashPropertyNames=@($readback.toolHashes.PSObject.Properties.Name);directControlHashPassed=$false;effectsPassed=$false;error=$null;scriptStack=$null;privateFailure=$null;mutations=@()}
  Assert-AzuritePreparedHash $path $run.controlSha256;$observation.directControlHashPassed=$true
  try{Assert-AzuriteEditorRouteEffects $run;$observation.effectsPassed=$true}catch{$observation.error=$_.Exception.Message;$observation.scriptStack=$_.ScriptStackTrace;$observation.privateFailure=Get-AzuriteEditorRoutePrivateFailure $_ 'fixture-effects'}
  if($observation.effectsPassed){
   $expectedNames=@(Get-AzuriteEditorRouteToolNames)+@('editor-route-contract.json','EditorRouteReviewedInputs.json','RestrictedCliNative.dll')
   if(@($run.control.toolHashes.PSObject.Properties).Count -ne 13 -or ((@($run.control.toolHashes.PSObject.Properties.Name|Sort-Object) -join '|') -cne (@($expectedNames|Sort-Object) -join '|'))){throw 'Actual stager did not return exactly the thirteen named pin entries.'}
   foreach($pin in $run.control.toolHashes.PSObject.Properties){if($pin.Value -cne $readback.toolHashes.($pin.Name)){throw 'In-memory pin identity differs from serialized control.'}}
   $mutationCases=@($run.control.toolHashes.PSObject.Properties|ForEach-Object {@{name=('tooling:'+$_.Name);path=(Join-Path $run.spec.runRoot ('tooling\'+$_.Name));expected=$_.Value}})+@(@{name='outer-launch-control';path=$path;expected=$run.controlSha256})
   foreach($test in $mutationCases){
    $bytes=[IO.File]::ReadAllBytes($test.path)
    try{
     [IO.File]::AppendAllText($test.path,"`n# harmless fixture mutation")
     $mutantFailure=$null;try{Assert-AzuriteEditorRouteEffects $run}catch{$mutantFailure=Get-AzuriteEditorRoutePrivateFailure $_ 'fixture-mutated-input'}
     if(-not $mutantFailure -or $mutantFailure.preparedInputMismatch.check -cne $test.name -or $mutantFailure.preparedInputMismatch.path -cne $test.path -or $mutantFailure.preparedInputMismatch.expectedSha256 -cne $test.expected -or $mutantFailure.preparedInputMismatch.actualSha256 -cne (Get-AzuriteSha256 $test.path) -or -not $mutantFailure.scriptStack){throw 'Changed prepared file was accepted or exact private mismatch diagnostics were lost.'}
     $observation.mutations+=@($mutantFailure)
    }finally{[IO.File]::WriteAllBytes($test.path,$bytes)}
    Assert-AzuriteEditorRouteEffects $run
   }
  }
  return [pscustomobject]$observation
 }finally{$script:EditorLab=$oldLab;Remove-Item Function:\New-AzuriteSharedStateSpec -ErrorAction SilentlyContinue;Remove-Item Function:\Read-AzuritePlayerPrefsSnapshot -ErrorAction SilentlyContinue}
} $lab $state $context
Write-AzuriteFlushedJson (Join-Path $root 'stager-observation.json') $result
$result|Select-Object fixtureOnly,runType,controlExpected,controlActual,expectedMatchesActual,runtimeToolHashType,directControlHashPassed,effectsPassed,@{n='pinCount';e={$_.runtimeToolHashPropertyNames.Count}},@{n='mutationRejections';e={$_.mutations.Count}},@{n='mutationChecks';e={@($_.mutations|ForEach-Object {$_.preparedInputMismatch.check})}},error,privateFailure|ConvertTo-Json -Depth 8
if(-not $result.effectsPassed){Write-Output 'FAIL freshly staged unchanged control must pass exact production effect checks';exit 1}
Write-Output 'PASS freshly staged unchanged control passes exact production effect checks'
