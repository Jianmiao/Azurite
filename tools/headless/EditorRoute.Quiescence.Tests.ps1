#requires -Version 7.0
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'EditorRouteCore.psm1') -DisableNameChecking
$oraclePath=Join-Path $PSScriptRoot 'EditorRoute.QuiescenceAssertions.ps1'
if(Test-Path -LiteralPath $oraclePath){. $oraclePath}
try{
 # Reproduce the retained root record: positive initial budget, no query before
 # its shared deadline elapsed. This is a denial, never an acceptance shortcut.
 Assert-EditorRouteQuiescenceFixtureDenial ([pscustomobject]@{quiescent=$false;attemptCount=0;samples=@();reason='shared-deadline-exhausted';limitMilliseconds=47})
 Assert-EditorRouteQuiescenceFixtureDenial ([pscustomobject]@{quiescent=$false;attemptCount=1;samples=@([pscustomobject]@{queryError='InvalidOperationException'});reason='owned-membership-query-failed';limitMilliseconds=2000})
 $mutations=@(
  [pscustomobject]@{quiescent=$false;attemptCount=1;samples=@();reason='quiescence-budget-exhausted'},
  [pscustomobject]@{quiescent=$false;attemptCount=0;samples=@();reason='owned-membership-query-failed'},
  [pscustomobject]@{quiescent=$false;attemptCount=1;samples=@(1,2,3,4,5,6,7,8,9);reason='quiescence-budget-exhausted'},
  [pscustomobject]@{quiescent=$true;attemptCount=0;samples=@();reason='shared-deadline-exhausted'}
 )
 foreach($mutant in $mutations){$denied=$false;try{Assert-EditorRouteQuiescenceFixtureDenial $mutant}catch{$denied=$true};if(-not $denied){throw 'Fixture oracle silently accepted missing/excessive diagnostics or a mislabeled outcome.'}}
 Write-Output 'PASS fixture denial oracle distinguishes zero-attempt expiry and rejects missing diagnostics mutations'
}catch{Write-Output ('FAIL fixture denial oracle: '+$_.Exception.Message);exit 1}
$root=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))) ('editor-route-quiescence-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$helper=Join-Path $root 'RestrictedCliNative.dll';[void](New-AzuriteNetFrameworkHelper (Join-Path $PSScriptRoot 'RestrictedCliNative.cs') $helper)
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildInfo=New-AzuriteProcessInfo $compiler @('/nologo','/target:exe',('/reference:'+$helper),('/out:'+(Join-Path $root 'EditorRouteBoundaryFixture.exe')),(Join-Path $PSScriptRoot 'EditorRoute.JobBoundaryFixture.cs.txt')) $root
$build=[Azurite.Restricted.ProcessPump]::Run($buildInfo,10000,$null,16384,@(),1048576)
if((Get-AzuriteProcessStatus $build 'Helper compiler').failed){throw ('Fixture compile failed: '+$build.Stdout+$build.Stderr)}
$passed=1;$failed=0
try{
 $native=[pscustomobject]@{Pid=($PID+100000);DirectProcessExited=$true}
 $queryFailure=[pscustomobject]@{};$queryFailure|Add-Member -MemberType ScriptMethod -Name ProcessIds -Value {throw [InvalidOperationException]::new('injected exact membership query failure')}
 $q=Wait-AzuriteEditorRouteQuiescence $queryFailure $native ([guid]::NewGuid().ToString('D')) ([datetime]::UtcNow.AddSeconds(-1))
 if($q.quiescent -or $q.limitMilliseconds -ne 0 -or $q.attemptCount -ne 0 -or $q.reason -cne 'shared-deadline-exhausted'){throw 'Expired deadline admitted or queried a job.'}
 $passed++;Write-Output 'PASS exhausted deadline refuses admission before membership query'
 $q=Wait-AzuriteEditorRouteQuiescence $queryFailure $native ([guid]::NewGuid().ToString('D')) ([datetime]::UtcNow.AddSeconds(5))
 Write-AzuriteFlushedJson (Join-Path $root 'query-failure.json') $q
 if($q.quiescent -or $q.reason -cne 'owned-membership-query-failed' -or $q.queryFailure -cne 'InvalidOperationException' -or $q.samples.Count -ne 1){throw 'Exact membership query failure was hidden or not diagnosed.'}
 $passed++;Write-Output 'PASS membership query error is bounded diagnosed and rejected'
}catch{$failed++;Write-Output ('FAIL deadline/query controls: '+$_.Exception.Message)}
foreach($mode in @('PlainAbort','LiveHelper','PersistentHelper','DeadlineBound')){
 $id=[guid]::NewGuid().ToString('D');$shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
 $psi=New-AzuriteProcessInfo $shell @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'EditorRoute.QuiescenceFixture.ps1'),'-Root',$root,'-RunId',$id,'-Mode',$mode) $root
 $outer=Invoke-AzuriteEditorRouteOwnedBootstrap $psi $id ([datetime]::UtcNow.AddSeconds(45)) @()
 Write-AzuriteFlushedJson (Join-Path $root ($mode+'-outer.json')) $outer
 try{
  Assert-AzuriteCleanupConfirmed $outer
  if((Get-AzuriteOuterProcessStatus $outer).failed){$failure=Read-AzuriteBoundedJson (Join-Path $root ($mode+'-failure.json'));throw $failure.error}
  if($mode -cin @('PersistentHelper','DeadlineBound') -and @(Get-ChildItem -LiteralPath $root -Filter ($mode+'*.evidence-consumed')).Count){throw 'Evidence consumed before quiescence.'}
  $passed++;Write-Output ('PASS real owned boundary '+$mode)
 }catch{$failed++;Write-Output ('FAIL real owned boundary '+$mode+': '+$_.Exception.Message)}
}
Write-Output ('RESULT passed='+$passed+' failed='+$failed+'; fixtureRoot='+$root+'; AA not launched')
if($failed){exit 1}
