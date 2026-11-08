#requires -Version 7.0
# Files and decision fixtures only; no AA/Unity or installed-file writes.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'EditorRouteCore.psm1') -DisableNameChecking
$root=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))) ('editor-route-loader-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root);Set-AzuriteLabAcl $root $root ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
$passed=0;$failed=0
function Check([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Deny([scriptblock]$Action){$denied=$false;try{&$Action|Out-Null}catch{$denied=$true};Check $denied 'invalid loader exception was accepted'}
function Test([string]$Name,[scriptblock]$Action){try{&$Action;$script:passed++;Write-Output ('PASS '+$Name)}catch{$script:failed++;Write-Output ('FAIL '+$Name+': '+$_.Exception.Message)}}
function Fixture([string]$Scope,[string]$Kind){
 $id=[guid]::NewGuid().ToString('D');$s=Get-AzuriteEditorRouteSpec $id;$case=Join-Path $root $id
 $s.runRoot=$case;$s.outputRoot=Join-Path $case 'output';$s.installRoot=Join-Path $case 'install';$s.nativeArguments=@('-batchmode','-nographics','-disableaudio','-logFile',(Join-Path $s.outputRoot 'unity.log'))
 foreach($p in @($s.outputRoot,(Join-Path $s.installRoot 'BepInEx'))){[void][IO.Directory]::CreateDirectory($p)}
 $source=Join-Path $s.installRoot 'BepInEx\LogOutput.log';$dest=Join-Path $s.outputRoot 'bepinex.LogOutput.log';$start=[datetime]::UtcNow.AddSeconds(-1)
 if($Kind -cne 'Missing'){
  if($Kind -ceq 'Oversize'){[IO.File]::WriteAllBytes($source,[byte[]]::new(4194305))}
  else{[IO.File]::WriteAllText($source,$(if($Kind -cin @('Empty','StaleEmpty')){''}else{'fresh harmless loader evidence'}))}
  if($Kind -cin @('Stale','StaleEmpty')){[IO.File]::SetLastWriteTimeUtc($source,$start.AddMinutes(-2))}
 }
 $loader=Copy-AzuriteFreshLoaderLog $source $dest $start
 $control=[pscustomobject]@{scope=$Scope;manifestSha256=('A'*64);argumentsSha256=('B'*64)}
 $native=[pscustomobject]@{scope=$Scope;status='passed';runId=$id;jobId=$s.jobId;bootstrapPid=111;aaPid=222;aaExitCode=$(if($Scope -ceq 'Positive'){0}else{86});noSoundConfigured=$true;timedOut=$false;windowViolation=$false;outputLimitExceeded=$false;processError='';nativeArguments=$s.nativeArguments;manifestSha256=('A'*64);argumentsSha256=('B'*64)}
 $proof=if($Scope -ceq 'Positive'){[pscustomobject]@{nativeReadProved=$true;nativeSaveProved=$true;guardianComplete=$true;guardianEventCount=3;probeEventCount=33;editorAccepted=$false;graphicalAccepted=$false}}else{[pscustomobject]@{expectedAbortProved=$true;guardianEventCount=$(if($Scope -ceq 'InvalidManifest'){1}else{2});probeEventCount=0;editorAccepted=$false;graphicalAccepted=$false}}
 $outer=[pscustomobject]@{Pid=111;RunId=$s.jobId;CleanupVerified=$true;DirectProcessExited=$true;JobDrained=$true;RemainingOwnedProcesses=0;JobObserved=$true;NoProcessCreated=$false}
 return [pscustomobject]@{loader=$loader;run=[pscustomobject]@{spec=$s;control=$control};native=$native;proof=$proof;outer=$outer}
}
function Validate($F){Assert-AzuriteEditorRouteLoaderEvidence $F.loader $F.run $F.outer $F.native $F.proof}
Test 'fresh complete nonempty log remains required and accepted for ordinary scopes' {foreach($scope in @('Positive','ProbeMissing')){$r=Validate (Fixture $scope Full);Check (-not $r.earlyAbortExceptionUsed) 'ordinary scope used exception'}}
Test 'positive and missing-probe scopes reject missing or empty logs' {foreach($scope in @('Positive','ProbeMissing')){foreach($kind in @('Missing','Empty')){Deny {Validate (Fixture $scope $kind)}}}}
Test 'proved InvalidManifest abort alone admits absent or fresh empty loader content' {foreach($kind in @('Missing','Empty')){$r=Validate (Fixture InvalidManifest $kind);Check $r.earlyAbortExceptionUsed 'proved early abort was not admitted'}}
Test 'InvalidManifest normal fresh log needs no exception' {$r=Validate (Fixture InvalidManifest Full);Check (-not $r.earlyAbortExceptionUsed) 'normal fresh log incorrectly labeled absent/empty'}
Test 'stale empty stale nonempty and oversized logs are never waived' {foreach($scope in @('Positive','ProbeMissing','InvalidManifest')){foreach($kind in @('StaleEmpty','Stale','Oversize')){Deny {Validate (Fixture $scope $kind)}}}}
Test 'claimed complete but truncated copy is rejected' {$f=Fixture InvalidManifest Full;$f.loader.RetainedBytes=1;Deny {Validate $f}}
Test 'missing or incomplete guardian proof cannot grant early-log exception' {
 $f=Fixture InvalidManifest Missing;$f.proof=$null;Deny {Validate $f}
 foreach($field in @('expectedAbortProved','guardianEventCount','probeEventCount')){$f=Fixture InvalidManifest Missing;$f.proof.$field=$(if($field -ceq 'expectedAbortProved'){$false}else{9});Deny {Validate $f}}
}
Test 'native abort code and monitor success remain mandatory' {
 foreach($field in @('aaExitCode','timedOut','windowViolation','outputLimitExceeded','processError','noSoundConfigured')){$f=Fixture InvalidManifest Missing;$f.native.$field=$(if($field -ceq 'aaExitCode'){0}elseif($field -ceq 'processError'){'monitor failed'}elseif($field -ceq 'noSoundConfigured'){$false}else{$true});Deny {Validate $f}}
}
Test 'fixed native flags cannot be omitted or extended' {$f=Fixture InvalidManifest Missing;$f.native.nativeArguments=@('-batchmode');Deny {Validate $f};$f=Fixture InvalidManifest Empty;$f.native.nativeArguments+=@('-extra');Deny {Validate $f}}
Test 'native run manifest and request bindings remain exact' {foreach($field in @('scope','status','runId','jobId','manifestSha256','argumentsSha256')){$f=Fixture InvalidManifest Missing;$f.native.$field='wrong';Deny {Validate $f}}}
Test 'full owned cleanup cannot be replaced by an early-abort label' {foreach($field in @('CleanupVerified','JobDrained','DirectProcessExited','JobObserved')){$f=Fixture InvalidManifest Missing;$f.outer.$field=$false;Deny {Validate $f}};$f=Fixture InvalidManifest Missing;$f.outer.RemainingOwnedProcesses=1;Deny {Validate $f}}
Test 'copied loader metadata belongs to this exact run and preserves typed status' {$f=Fixture InvalidManifest Empty;$f.loader.Path=Join-Path $root 'wrong.log';Deny {Validate $f};$f=Fixture InvalidManifest Empty;$f.loader.Fresh='true';Deny {Validate $f}}
Write-Output ('RESULT passed='+$passed+' failed='+$failed+'; fixtureRoot='+$root+'; no AA')
if($failed){exit 1}
