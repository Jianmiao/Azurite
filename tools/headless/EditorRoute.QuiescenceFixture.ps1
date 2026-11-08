# Harmless tests of the exact native Run -> owned membership boundary.
param([string]$Root,[string]$RunId,[ValidateSet('PlainAbort','LiveHelper','PersistentHelper','DeadlineBound')][string]$Mode)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
foreach($name in @('Microsoft.PowerShell.Security','Microsoft.PowerShell.Management','Microsoft.PowerShell.Utility')){Import-Module (Join-Path $PSHOME ('Modules\'+$name+'\'+$name+'.psd1')) -Force -DisableNameChecking}
Import-Module (Join-Path $PSScriptRoot 'EditorRouteCore.psm1') -DisableNameChecking
. (Join-Path $PSScriptRoot 'EditorRoute.QuiescenceAssertions.ps1')
Initialize-AzuriteRestrictedNative -AssemblyPath (Join-Path $Root 'RestrictedCliNative.dll')
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent();$job.AnnounceAndAwaitController(([guid]$RunId).ToString('N'));$exitCode=1
try{
 $records=[Collections.Generic.List[object]]::new();$count=if($Mode -ceq 'PlainAbort'){30}else{1}
 for($i=0;$i -lt $count;$i++){
  $childMode=if($Mode -ceq 'DeadlineBound'){'LiveHelper'}else{$Mode}
  $psi=New-AzuriteProcessInfo (Join-Path $Root 'EditorRouteBoundaryFixture.exe') @('child',$childMode,(Join-Path $Root ($Mode+$i+'.pid'))) $Root
  $native=[Azurite.Restricted.ProcessPump]::Run($psi,5000,$job,4096,@(),1048576)
  Assert-AzuriteEditorRouteNativeStatus $native ProbeMissing
  $deadline=[datetime]::UtcNow.AddMilliseconds($(if($Mode -ceq 'DeadlineBound'){150}else{5000}))
  $observed=Wait-AzuriteEditorRouteQuiescence $job $native $RunId $deadline
  $records.Add($observed)
  $expectPass=$Mode -cin @('PlainAbort','LiveHelper')
  if($observed.quiescent -ne $expectPass){throw 'Owned membership did not respect expected quiescence/timeout.'}
  if($expectPass -and ($observed.singletonObservations -lt 2 -or $observed.lastMemberIds.Count -ne 1 -or $observed.lastMemberIds[0] -ne $PID)){throw 'Quiescence did not preserve exact singleton requirement.'}
  if(-not $expectPass){Assert-EditorRouteQuiescenceFixtureDenial $observed}
  if($observed.samples.Count -gt 8 -or $observed.limitMilliseconds -gt 2000){throw 'Quiescence diagnostics/wait exceed bound.'}
  if($Mode -ceq 'DeadlineBound' -and $observed.elapsedMilliseconds -gt 1000){throw 'Shared deadline was extended by independent wait budget.'}
  if($observed.quiescent){[IO.File]::WriteAllText((Join-Path $Root ($Mode+$i+'.evidence-consumed')),'fixture acceptance only after observed singleton')}
 }
 Write-AzuriteFlushedJson (Join-Path $Root ($Mode+'-observations.json')) $records.ToArray()
 $exitCode=0
}catch{Write-AzuriteFlushedJson (Join-Path $Root ($Mode+'-failure.json')) @{error=$_.Exception.Message;records=$(if($records){$records.ToArray()}else{@()})}}
finally{[Console]::Out.Flush();[Console]::Error.Flush();$job.Terminate([uint32]$exitCode)}
