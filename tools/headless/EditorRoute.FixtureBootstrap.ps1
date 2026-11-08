# Harmless process fixture only. Production has no fake executable/mode switch.
param([string]$RunId,[string]$Helper,[string]$Output,[ValidateSet('Complete','Timeout','NoHandshake','OutputLimit')][string]$Mode)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -DisableNameChecking
Initialize-AzuriteRestrictedNative -AssemblyPath $Helper
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent()
if($Mode -ceq 'NoHandshake'){$job.Terminate(0)}
$job.AnnounceAndAwaitController(([guid]$RunId).ToString('N'))
try{
 if($Mode -ceq 'OutputLimit'){[Console]::Out.Write(('x'*500000));[Console]::Out.Flush();Start-Sleep -Seconds 20}
 $shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
 $command=if($Mode -ceq 'Timeout'){'Start-Sleep -Seconds 30'}else{"[Console]::Out.Write('fixture-complete')"}
 $psi=New-AzuriteProcessInfo -FilePath $shell -Arguments @('-NoProfile','-NonInteractive','-Command',$command) -WorkingDirectory (Split-Path -Parent $Output)
 if($Mode -ceq 'Timeout'){
  $child=[Diagnostics.Process]::Start($psi)
  Write-AzuriteFlushedJson $Output @{fixtureOnly=$true;childPid=$child.Id;runId=$RunId}
  Start-Sleep -Seconds 30
 }else{
  $native=[Azurite.Restricted.ProcessPump]::Run($psi,5000,$job,4096,@(),1048576)
  $status=Get-AzuriteNativeProcessStatus $native
  if($status.failed -or $native.Stdout -cne 'fixture-complete'){throw 'Harmless fixture child failed.'}
  Write-AzuriteFlushedJson $Output @{fixtureOnly=$true;childPid=$native.Pid;runId=$RunId;output=$native.Stdout}
  $job.Terminate(0)
 }
}catch{[Console]::Error.WriteLine($_.Exception.Message);$job.Terminate(1)}
$job.Terminate(1)
