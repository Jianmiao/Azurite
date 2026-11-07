param([string]$Module,[string]$AssemblyPath,[string]$RunId,[string]$Fake,[ValidateSet('Normal','Timeout','PipeLeak','NoHandshake','DirtyHandshake')][string]$Mode,[string]$PidPath)
$ErrorActionPreference='Stop'
Import-Module $Module -Force -DisableNameChecking
Initialize-AzuriteRestrictedNative -AssemblyPath $AssemblyPath
# Explicitly fixture-only current runner identity. Production bootstrap has no such mode.
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent()
if($Mode -eq 'NoHandshake'){$job.Terminate(1)}
if($Mode -eq 'DirtyHandshake'){
 $unexpected=[Diagnostics.Process]::Start((New-AzuriteProcessInfo -FilePath $Fake -Arguments @('leaf') -WorkingDirectory (Split-Path $PidPath)))
}
try{$job.AnnounceAndAwaitController($RunId)}catch{$job.Terminate(2)}
$behavior=if($Mode -eq 'PipeLeak'){'leak'}else{'tree'}
$psi=New-AzuriteProcessInfo -FilePath $Fake -Arguments @($behavior,$PidPath) -WorkingDirectory (Split-Path $PidPath)
if($Mode -eq 'Normal') {
 $child=[Diagnostics.Process]::Start($psi)
 $limit=[datetime]::UtcNow.AddSeconds(3)
 while(-not(Test-Path $PidPath) -and [datetime]::UtcNow -lt $limit){Start-Sleep -Milliseconds 20}
 [Console]::Out.WriteLine('fixture normal result flushed');[Console]::Out.Flush()
 $job.Terminate(0)
} elseif($Mode -eq 'Timeout') {
 $child=[Diagnostics.Process]::Start($psi)
 Start-Sleep -Seconds 60
} else {
 $run=[Azurite.Restricted.ProcessPump]::Run($psi,2000,$job,4096,@(),1048576)
 [Console]::Out.WriteLine('fixture deadline='+$run.TimedOut);[Console]::Out.Flush()
 $job.Terminate(124)
}
