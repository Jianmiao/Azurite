# R4: real harmless zero-exit process + real denied-directory monitor exception.
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force
Initialize-AzuriteRestrictedNative
function Assert($Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"}}
$root=Join-Path $repo ('artifacts\restricted-cli-fix2\'+[guid]::NewGuid().ToString('N'))
$denied=Join-Path $root 'denied';[void][IO.Directory]::CreateDirectory($denied)
$empty=Join-Path $root 'empty';[void][IO.Directory]::CreateDirectory($empty)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $denied '/deny' ('*'+$sid+':(RD)') | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not establish isolated denied-directory fixture.'}
try {
 $run=$null
 for($attempt=0;$attempt -lt 8;$attempt++){
  $psi=New-AzuriteProcessInfo -FilePath (Join-Path ([Environment]::SystemDirectory) 'cmd.exe') -Arguments @('/d','/c','exit','0') -WorkingDirectory $root
  # Real successful checks let the tiny command finish before the genuine denied check.
  $watched=@($empty)*2048+@($denied)
  $candidate=[Azurite.Restricted.ProcessPump]::Run($psi,2000,$null,4096,$watched,1048576)
  if($candidate.ExitCode -eq 0 -and $candidate.Error -and $candidate.Error.Contains($denied) -and -not $candidate.TimedOut -and -not $candidate.WindowViolation -and -not $candidate.OutputLimitExceeded){$run=$candidate;break}
 }
 Assert ($null -ne $run) 'R4 could not reproduce actual zero exit plus monitor error'
 $status=Get-AzuriteNativeProcessStatus $run
 Write-AzuriteFlushedJson (Join-Path $root 'evidence.json') @{fixture='real cmd exit 0 plus actual denied watched-directory monitor error';pid=$run.Pid;exitCode=$run.ExitCode;error=$run.Error;timedOut=$run.TimedOut;windowViolation=$run.WindowViolation;outputLimitExceeded=$run.OutputLimitExceeded;productionStatus=$status}
 Assert $status.failed 'R4 native production predicate accepted exit 0 with monitor error'
 Assert ($status.error.Contains($denied) -and $status.reason.Contains($status.error)) 'R4 actionable sanitized monitor error was not retained'
 Assert ($run.ExitCode -eq 0) 'R4 actual child exit code was overwritten'
 $outer=Get-AzuriteOuterProcessStatus $run
 Assert $outer.failed 'R4 outer consumer accepted monitor error'
 Assert (Get-AzuriteProcessStatus $run 'Helper compiler').failed 'R4 helper compiler consumer predicate accepted monitor error'
 $recordPath=Join-Path $root 'native-record.json'
 Write-AzuriteFlushedJson $recordPath @{status='passed';aaExitCode=$run.ExitCode;timedOut=$run.TimedOut;windowViolation=$run.WindowViolation;outputLimitExceeded=$run.OutputLimitExceeded;processError=$status.error}
 $parentStatus=Get-AzuriteNativeResultStatus (Read-AzuriteBoundedJson $recordPath)
 Assert ($parentStatus.failed -and $parentStatus.exitCode -eq 0 -and $parentStatus.reason.Contains($status.error)) 'R4 controller accepted claimed passed native result with retained monitor error'
 $run.Error="fixture password=do-not-retain`nmonitor failed"
 $safe=Get-AzuriteNativeProcessStatus $run
 Assert ($safe.failed -and $safe.error -notmatch 'do-not-retain|[\x00-\x1f]' -and $safe.reason -match 'monitor failed') 'R4 native error lost failure state or was not sanitized'
 Write-Output ('PASS R4: real zero-exit monitor error rejected through production predicate, exit code preserved, sanitized detail retained; evidence '+$root)
} finally {& icacls.exe $denied '/remove:d' ('*'+$sid) | Out-Null}
