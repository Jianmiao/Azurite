param([Parameter(Mandatory)][string]$RepoRoot)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $RepoRoot 'tools/headless/RestrictedCliCore.psm1') -Force
Import-Module (Join-Path $RepoRoot 'tools/headless/HeadlessCore.psm1') -Force
Import-Module (Join-Path $RepoRoot 'tools/headless/FullRestrictedCapabilitiesRuntime.psm1') -Force
$script:count=0
function Check($Value,[string]$Why){$script:count++;if(-not $Value){throw $Why}}
function Refuse([scriptblock]$Action,[string]$Why){$failed=$false;try{& $Action}catch{$failed=$true};Check $failed $Why}
$preserved=ConvertFrom-AzuriteFullJson '{"observedUtc":"2026-11-10T00:00:00.1234567Z"}';Check ($preserved.observedUtc -is [string] -and $preserved.observedUtc -ceq '2026-11-10T00:00:00.1234567Z') 'newmode canonical dates stay strings acrossPS5and7'
$zero=[pscustomobject]@{schema=1;runId=('a'*32);accepted=$true;userPreferencesUnchanged=$true;finalStateUnchanged=$true;registryChanged=$false;unexpected=@();deltas=@();engineMetadata=@();restorationPerformed=$false}
Assert-AzuriteFullZeroSharedDelta $zero
foreach($field in @('registryChanged','restorationPerformed')){$bad=$zero|ConvertTo-Json|ConvertFrom-Json;$bad.$field=$true;Refuse {Assert-AzuriteFullZeroSharedDelta $bad} "shared $field refused"}
$session=$zero|ConvertTo-Json|ConvertFrom-Json;$session.engineMetadata=@(@{name='unity.player_session_count_h922449978'});$session.deltas=$session.engineMetadata;$session.finalStateUnchanged=$false;$session.registryChanged=$true
Refuse {Assert-AzuriteFullZeroSharedDelta $session} 'legacy session exception cannot satisfy new zero delta'
Assert-AzuriteFullEnvironment @{}
foreach($name in @('BEPINEX_GAME_ASSEMBLY_PATH','DOORSTOP_CONFIG_FILE','CORECLR_PROFILER','COMPlus_Test','UNITY_BOOT_CONFIG')){$envMap=@{};$envMap[$name]='unexpected';Refuse {Assert-AzuriteFullEnvironment $envMap} "override $name refused"}
$run='a'*32;$start=[datetime]::Parse('2026-11-09T00:00:00Z').ToUniversalTime();$end=$start.AddSeconds(2)
$before=[pscustomobject]@{runId=$run;path='F:\fixture\unity.log';identity='vol:file';bytes=0;sha256='e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'}
$after=[pscustomobject]@{runId=$run;path=$before.path;identity='vol:file';bytes=20;sha256=('b'*64);lastWriteUtc=$start.AddSeconds(1).ToString('o');controllerCorrelatedPid=123;correlation='owned-controller-native-time-window'}
Assert-AzuritePrecreatedLogEvidence $before $after $run 123 $start $end $true
foreach($field in @('identity','runId','correlation')){$bad=$after|ConvertTo-Json|ConvertFrom-Json;$bad.$field='wrong';Refuse {Assert-AzuritePrecreatedLogEvidence $before $bad $run 123 $start $end $true} "log $field refused"}
Refuse {Assert-AzuritePrecreatedLogEvidence $before $after $run 123 $start $end $false} 'undrained log not accepted'
$old=$after|ConvertTo-Json|ConvertFrom-Json;$old.lastWriteUtc=$start.AddDays(-1).ToString('o');Refuse {Assert-AzuritePrecreatedLogEvidence $before $old $run 123 $start $end $true} 'stale precreated log rejected'
Assert-AzuriteFullRequestInventory @('arguments.json','response.json') @(30,100)
Refuse {Assert-AzuriteFullRequestInventory @('arguments.json','response.json','unexpected.tmp') @(30,100,1)} 'unexpected residual leaf rejected'
Refuse {Assert-AzuriteFullRequestInventory @('arguments.json','response.json') @(30,1048577)} 'output bound retained'
Refuse {Assert-AzuriteFullRuntimeGate $null $null $null $run} 'no runtime admission from dry plan'
$gate=[pscustomobject]@{schema=2;scope='preapproval-full-restricted-capabilities-intent';runId=$run;planSha256=('c'*64);contractSha256=('d'*64);toolingManifestSha256=('e'*64);controlSha256=('f'*64);expectedAclManifestSha256=('1'*64);harmlessLifecycleReceiptSha256=('2'*64);transitionSourceManifestSha256=('3'*64);transitionHelperSha256=('4'*64);stageRuntimeAllowed=$false;operation='capabilities'}
$plan=[pscustomobject]@{RunId=$run;LabRoot='F:\AzuriteLabs\Editor-20261108-normal';PlanSha256=$gate.planSha256;ContractSha256=$gate.contractSha256}
$approval=[pscustomobject]@{controlGateSha256=('5'*64);toolingManifestSha256=$gate.toolingManifestSha256;validFromUtc=[datetime]::UtcNow.AddHours(-1).ToString('yyyy-MM-ddTHH:mm:ssZ');validUntilUtc=[datetime]::UtcNow.AddHours(1).ToString('yyyy-MM-ddTHH:mm:ssZ')}
Assert-AzuriteFullRuntimeGate $gate $plan $approval $run ('5'*64);Check $true 'forward-only runtime gate accepted'
foreach($field in @('operation','runId','planSha256','contractSha256','controlSha256','transitionHelperSha256')){$bad=$gate|ConvertTo-Json|ConvertFrom-Json;$bad.$field='wrong';Refuse {Assert-AzuriteFullRuntimeGate $bad $plan $approval $run ('5'*64)} "gate $field refused"}
foreach($field in @('reviewApprovalSha256','transitionReceiptSha256','aa0Observed')){$bad=$gate|ConvertTo-Json|ConvertFrom-Json;$bad|Add-Member $field ('6'*64);Refuse {Assert-AzuriteFullRuntimeGate $bad $plan $approval $run ('5'*64)} "reverse/future gate field refused: $field"}
Refuse {Assert-AzuriteFullRuntimeGate $gate $plan $approval $run ('7'*64)} 'external gate digest cannot be adopted'
$expired=$approval|ConvertTo-Json|ConvertFrom-Json;$expired.validUntilUtc=[datetime]::UtcNow.AddHours(-1).ToString('o');Refuse {Assert-AzuriteFullRuntimeGate $gate $plan $expired $run ('5'*64)} 'expired positive approval'
$clock=[datetime]::Parse('2026-11-10T00:10:00Z').ToUniversalTime();$timing=[pscustomobject]@{runtimeTimeoutSeconds=120;intentValidUntilUtc='2026-11-10T01:00:00Z'}
Check ((Get-AzuriteFullRuntimeBudget $timing 120 0 $clock) -eq 120000) 'delayed preparation within intent gets fresh120second runtime'
Check ((Get-AzuriteFullRuntimeBudget $timing 120 15000 $clock) -eq 105000) 'bootstrap elapsed time deducted'
Refuse {Get-AzuriteFullRuntimeBudget $timing 121 0 $clock} 'public timeout mismatch denied'
Refuse {Get-AzuriteFullRuntimeBudget $timing 120 120001 $clock} 'monotonic deadline exhausted'
Refuse {Get-AzuriteFullRuntimeBudget $timing 120 0 $clock.AddHours(2)} 'expired preparation intent cannot run'
$tooLong=[pscustomobject]@{runtimeTimeoutSeconds=301;intentValidUntilUtc=$timing.intentValidUntilUtc};Refuse {Get-AzuriteFullRuntimeBudget $tooLong 301 0 $clock} 'preparation validity cannot extend runtime past300seconds'
Check ((ConvertTo-AzuriteFullFileIdentity '12345678:abcdef0123456789') -ceq '1234567800000000abcdef0123456789') 'native file identity canonical padding'
Refuse {ConvertTo-AzuriteFullFileIdentity '12345678:ABCDEF0123456789'} 'noncanonical file identity denied'
$sid='S-1-5-21-1-2-3-1001';$logon='S-1-5-5-1-2'
$private='O:'+ $sid+'G:SYD:P(A;;FA;;;'+$sid+')(A;;FA;;;SY)(A;;FA;;;BA)'
$directory='O:'+ $sid+'G:SYD:P(A;OICI;FA;;;'+$sid+')(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)'
$mutable=$private+'(A;;0x12019F;;;'+$logon+')';Assert-AzuriteFullAclPolicy $mutable $sid $logon 'install-file' '0x12019F';Check $true 'exact file-only mutable grant accepted'
$request=$directory+'(A;;0x1200AB;;;'+$logon+')(A;OIIO;0x13019F;;;'+$logon+')';Assert-AzuriteFullAclPolicy $request $sid $logon 'request-directory' '0x1200AB';Check $true 'parent request and inheritedresponse rename rights accepted'
$sealed=$directory.Replace('D:P','D:P(D;;0x6;;;'+$sid+')')+'(A;;0x1200A9;;;'+$logon+')';Assert-AzuriteFullAclPolicy $sealed $sid $logon 'install-directory' '0x1200A9';Check $true 'install creation seal preserved'
foreach($bad in @($mutable.Replace('0x12019F','0x1301FF'),$mutable.Replace('D:P','D:'),($mutable+'(A;;FA;;;WD)'),$mutable.Replace($logon,'S-1-5-5-9-9'))){Refuse {Assert-AzuriteFullAclPolicy $bad $sid $logon 'install-file' '0x12019F'} 'broad inherited/untrusted/wrongselected ACL denied'}
Refuse {Assert-AzuriteFullAclPolicy ($request.Replace('0x1200AB','0x1200EF')) $sid $logon 'request-directory' '0x1200AB'} 'parent deletechild/create-directory forbidden'
Refuse {Assert-AzuriteFullAclPolicy ($sealed.Replace('(D;;0x6;;;'+$sid+')','')) $sid $logon 'install-directory' '0x1200A9'} 'missing creation seal refused'
Assert-AzuriteFullAclPolicy $private $sid $logon 'private-file' '0x0';Refuse {Assert-AzuriteFullAclPolicy $mutable $sid $logon 'private-file' '0x0'} 'child grant to control/tool/history forbidden'
$hashes=[ordered]@{};$files=@();foreach($name in Get-AzuriteFullToolNames){$hashes[$name]='a'*64;$files+=@([pscustomobject]@{name=$name;sha256=('a'*64)})}
$control=[pscustomobject]@{controllerSid=$sid;toolHashes=[pscustomobject]$hashes;privateHistoryPaths=@([pscustomobject]@{path='transition';kind='Directory'},[pscustomobject]@{path='transition/before-images-raw.json';kind='File'})}
$manifest=[pscustomobject]@{schema=1;scope='full-restricted-precompiled-tooling';files=$files;build=[pscustomobject]@{target='library';platform='x64';exitCode=0;sources=@([pscustomobject]@{name='RestrictedCliNative.cs';sha256=('a'*64)},[pscustomobject]@{name='RestrictedCliFullRestricted.cs';sha256=('a'*64)});assemblySha256=('a'*64);compilerSha256=('b'*64);references=@([pscustomobject]@{name='System.Web.Extensions.dll';sha256=('c'*64)})}}
Assert-AzuriteFullToolManifest $manifest $control;Check $true 'exact two-source precompiled helper manifest accepted'
$bad=$manifest|ConvertTo-Json -Depth 10|ConvertFrom-Json;$bad.files[0].name='control.json';Refuse {Assert-AzuriteFullToolManifest $bad $control} 'tool manifest excludes control selfreference'
$bad=$manifest|ConvertTo-Json -Depth 10|ConvertFrom-Json;$bad.build.sources[1].sha256='d'*64;Refuse {Assert-AzuriteFullToolManifest $bad $control} 'helper source provenance mismatch'
$bad=$manifest|ConvertTo-Json -Depth 10|ConvertFrom-Json;$bad.build.platform='AnyCPU';Refuse {Assert-AzuriteFullToolManifest $bad $control} 'wrong helper build platform'
$rolePlan=$plan|ConvertTo-Json|ConvertFrom-Json
$rolePlan|Add-Member Grants ([pscustomobject]@{files=@([pscustomobject]@{path='BepInEx/config/BepInEx.cfg';mask='0x12019F'},[pscustomobject]@{path='BepInEx/cache/chainloader_typeloader.dat';mask='0x12019F'});directories=@('F:\AzuriteLabs',$plan.LabRoot,'.','BepInEx','BepInEx/config','BepInEx/cache'|ForEach-Object{[pscustomobject]@{path=$_;mask='0x1200A9'}})})
$rolePlan|Add-Member MutableLeaves @([pscustomobject]@{path='output/unity.log';role='FreshRunUnityLog'},[pscustomobject]@{path='BepInEx/cache/new.dat';role='MissingModTheCache'})
$roles=Get-AzuriteFullExpectedRoles $rolePlan $control
Check ($roles['F:\AzuriteLabs'][0] -ceq 'ancestor-directory' -and $roles[$plan.LabRoot][0] -ceq 'ancestor-directory') 'absoluteP/L ancestor paths remain outside install'
Check ($roles.ContainsKey($plan.LabRoot+'\runs\full-capabilities.pending.json') -and -not $roles.ContainsKey($plan.LabRoot+'\secrets')) 'global pending exact role without newsecrets'
$objects=@(foreach($path in $roles.Keys){$role=$roles[$path][0];$mask=$roles[$path][1];$sddl=if($role -clike '*directory'){$directory}else{$private};if($role -ceq 'install-directory'){$sddl=$sddl.Replace('D:P','D:P(D;;0x6;;;'+$sid+')')};if($role -cnotin @('private-file','private-directory')){$sddl+='(A;;'+$mask+';;;'+$logon+')'};if($role -ceq 'request-directory'){$sddl+='(A;OIIO;0x13019F;;;'+$logon+')'};[pscustomobject]@{path=$path;role=$role;mask=$mask;sddl=$sddl}})
$aclManifest=[pscustomobject]@{schema=1;scope='preapproval-exact-acl-intent';runId=$run;planSha256=$plan.PlanSha256;ownerSid=$sid;logonSid=$logon;objects=$objects}
Assert-AzuriteFullExpectedAclManifest $aclManifest $rolePlan $control $logon;Check $true 'complete finite ACL intent accepted'
$bad=$aclManifest|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.objects=@($bad.objects|Select-Object -Skip 1);Refuse {Assert-AzuriteFullExpectedAclManifest $bad $rolePlan $control $logon} 'missing exact ACL object denied'
$badControl=$control|ConvertTo-Json -Depth 12|ConvertFrom-Json;$badControl.privateHistoryPaths[1].path='../escape.json';Refuse {Assert-AzuriteFullExpectedAclManifest $aclManifest $rolePlan $badControl $logon} 'private history escape denied'
$inventory=Get-AzuriteFullExpectedInventory $rolePlan $control Prepared
Assert-AzuriteFullInventoryNames $inventory['F:\AzuriteLabs'].ToArray() @('.azurite-phase0-parent.json','Editor-20261108-normal');Check $true 'prepared root inventory preserves originalroot and one runs delta'
Check (-not $inventory.ContainsKey('F:\') -and $inventory[($plan.LabRoot+'\runs\restricted-'+$run+'\workspace')].Count -eq 0) 'inventory stays finite and workspaceempty'
$completeInventory=Get-AzuriteFullExpectedInventory $rolePlan $control Complete
Check ($completeInventory[($plan.LabRoot+'\runs\restricted-'+$run+'\request')].Count -eq 2 -and $completeInventory[($plan.LabRoot+'\runs\restricted-'+$run+'\history\post-run')].Count -eq 2) 'postrun request and immutable logs have exact declared names'
$fullControl=[pscustomobject]@{schema=2;identityMode='CurrentAccountFullRestrictedCapabilities';runId=$run;command='capabilities';labRoot=$plan.LabRoot;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;controllerSid=$sid;targetSid=$sid;controllerProfile='C:\fixture-profile';currentLocalLow='C:\fixture-profile\LocalLow';currentEnvironment=[pscustomobject]@{};argumentsSha256=('a'*64);sharedStateBeforeSha256=('b'*64);toolingManifestSha256=$gate.toolingManifestSha256;expectedAclManifestSha256=$gate.expectedAclManifestSha256;privateHistoryPaths=$control.privateHistoryPaths;toolHashes=$control.toolHashes;runtimeTimeoutSeconds=120;intentValidUntilUtc=[datetime]::UtcNow.AddMinutes(10).ToString('o')}
Assert-AzuriteFullControlBinding $fullControl $gate $plan ([pscustomobject]@{controllerSid=$sid});Check $true 'forward-only exact20field control accepted'
$bad=$fullControl|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad|Add-Member gateSha256 ('d'*64);Refuse {Assert-AzuriteFullControlBinding $bad $gate $plan ([pscustomobject]@{controllerSid=$sid})} 'control-to-gate cycle denied'
$bad=$fullControl|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.command='schema';Refuse {Assert-AzuriteFullControlBinding $bad $gate $plan ([pscustomobject]@{controllerSid=$sid})} 'schema command cannot use fullcontrol'
$identity=[pscustomobject]@{status='passed';identitySid=$sid;isAdministrator=$false;hiveLoaded=$true;unexpectedPrivileges=@();token=[pscustomobject]@{logon_sid=$logon;user_sid=$sid;is_restricted=$false;elevated=$false;is_app_container=$false;users_enabled=$true}}
$identityFacts=[pscustomobject]@{acl=[pscustomobject]@{actualLogonSid=$logon}}
Assert-AzuriteFullOriginalIdentity $identity $control $identityFacts;Check $true 'same original current token facts accepted'
$bad=$identity|ConvertTo-Json -Depth 8|ConvertFrom-Json;$bad.token.logon_sid='S-1-5-5-9-9';Refuse {Assert-AzuriteFullOriginalIdentity $bad $control $identityFacts} 'actualLogon change rejected beforeAA'
$proof=[pscustomobject]@{preparationLeaseReleased=$true;requiresFreshRuntimeLease=$true}
$pending=[pscustomobject]@{schema=1;scope='full-capabilities-global-pending';runId=$run;planSha256=$plan.PlanSha256;controllerSid=$sid;runDirectory=$plan.LabRoot+'\runs\restricted-'+$run;state='prepared-awaiting-runtime-lease'}
Assert-AzuriteFullPendingRecord $pending $plan $control $proof 'prepared-awaiting-runtime-lease' $true;Check $true 'matching prepared run requires fresh held runtime lease'
Refuse {Assert-AzuriteFullPendingRecord $pending $plan $control $proof 'prepared-awaiting-runtime-lease' $false} 'static preparation receipt is not live runtime exclusion'
foreach($field in @('runId','state','planSha256')){$bad=$pending|ConvertTo-Json|ConvertFrom-Json;$bad.$field='wrong';Refuse {Assert-AzuriteFullPendingRecord $bad $plan $control $proof 'prepared-awaiting-runtime-lease' $true} "global pending mismatch $field blocks every run"}
$cleanup=[pscustomobject]@{RunId=$run;CleanupVerified=$true;DirectProcessExited=$true;JobDrained=$true;RemainingOwnedProcesses=0;Pid=12;JobObserved=$true;NoProcessCreated=$false;FullJobObserverClosed=$true;FullJobObserverRetained=$false;DesktopLeaseClosed=$true;DesktopLeaseRetained=$false;StreamCaptureComplete=$true}
Assert-AzuriteFullCleanupAcceptance $plan $cleanup $zero;Check $true 'complete owned cleanup and zero shared delta permits pending release'
$wrongDelta=$zero|ConvertTo-Json|ConvertFrom-Json;$wrongDelta.runId='b'*32;Refuse {Assert-AzuriteFullCleanupAcceptance $plan $cleanup $wrongDelta} 'different shared-state run cannot clear pending'
foreach($field in @('StreamCaptureComplete','FullJobObserverClosed','DesktopLeaseClosed')){$bad=$cleanup|ConvertTo-Json|ConvertFrom-Json;$bad.$field=$false;Refuse {Assert-AzuriteFullCleanupAcceptance $plan $bad $zero} "failed owner/stream close blocks release: $field"}
Assert-AzuriteFullInventoryNames @('a','b') @('b','a');Refuse {Assert-AzuriteFullInventoryNames @('a','b','extra') @('a','b')} 'unknown sibling rejected';Refuse {Assert-AzuriteFullInventoryNames @('A','b') @('a','b')} 'case alias rejected'
foreach($mode in @('success','release-failed','create-failed','already-exists')){
 $state=@{Held=$true;ReleaseAttempted=$false;ReleaseSucceeded=$false;ReleaseError=$null;DisposeAttempted=$false;DisposeSucceeded=$false;DisposeError=$null;OverallPassed=$true;OwnerRetained=$false;RetentionError=$null;Compensation=$null;CompensationError=$null;EvidenceError=$null;Plan=$plan;PendingEvidence=[pscustomobject]@{record=$pending;evidence=[pscustomobject]@{sddl=$private}};Calls=[Collections.Generic.List[string]]::new();Mode=$mode;Creates=0;Persisted=$false}
 $backend={param($action,$s)
  $s.Calls.Add($action)
  switch($action){
   Release {if($s.Mode -cne 'success'){throw 'primary ReleaseMutex injected failure'}}
   Dispose {}
   Retain {}
   RestorePendingCreateNew {Invoke-AzuriteFullPendingCompensation {param($operation,$v,$request)if($operation -ceq 'Exists'){return $v.Mode -ceq 'already-exists'};if($operation -cne 'CreateNew'){throw 'must never overwrite'};$v.Creates++;if($v.Mode -ceq 'create-failed'){throw 'secondary CreateNew injected failure'}} $s}
   Persist {$s.Persisted=$true}
  }
 }
 $done=Invoke-AzuriteFullLeaseFinalizer $backend $state
 if($mode -ceq 'success'){Check ($done.OverallPassed -and $done.ReleaseSucceeded -and $done.DisposeAttempted -and $done.DisposeSucceeded -and $null -eq $done.DisposeError -and -not $done.Held -and $done.Creates -eq 0 -and $done.Persisted) 'normal verified cleanup plus release/dispose completes without manual gate'}
 else{Check (-not $done.OverallPassed -and $done.OwnerRetained -and $done.ReleaseError.Contains('primary') -and $done.Persisted -and -not $done.Calls.Contains('Dispose')) "release failure preserves owner and nooverallpass: $mode"}
 if($mode -ceq 'create-failed'){Check ($done.CompensationError.Contains('secondary') -and $done.Creates -eq 1) 'primary and secondary failures retained'}
 if($mode -ceq 'already-exists'){Check ($done.Creates -eq 0 -and $done.Compensation.disposition -ceq 'existing-marker-retained') 'existing pending never overwritten'}
 if($mode -ceq 'release-failed'){Check ($done.Creates -eq 1 -and $done.Compensation.overwritten -eq $false) 'failure marker restoration CreateNew only'}
}
$leaseRegressionFailures=[Collections.Generic.List[string]]::new()
function LeaseRegression($Value,[string]$Why){$script:count++;if(-not $Value){$leaseRegressionFailures.Add($Why)}}
foreach($mode in @('dispose-failed','release-cleared-then-failed')){
 $state=@{Held=$true;PendingCleared=$true;ReleaseAttempted=$false;ReleaseSucceeded=$false;ReleaseError=$null;DisposeAttempted=$false;DisposeSucceeded=$false;DisposeError=$null;OverallPassed=$true;OwnerRetained=$false;RetentionError=$null;Compensation=$null;CompensationError=$null;EvidenceError=$null;Plan=$plan;PendingEvidence=[pscustomobject]@{record=$pending;evidence=[pscustomobject]@{sddl=$private}};Calls=[Collections.Generic.List[string]]::new();Mode=$mode;Creates=0;Persisted=$false}
 $backend={param($action,$s)
  $s.Calls.Add($action)
  switch($action){
   Release {if($s.Mode -ceq 'release-cleared-then-failed'){$s.Held=$false;throw 'release callback failed after clearing held state'}}
   Dispose {throw 'post-release Dispose injected failure'}
   Retain {}
   RestorePendingCreateNew {$s.Creates++;[pscustomobject]@{disposition='unexpected-unheld-marker-write'}}
   Persist {$s.Persisted=$true}
  }
 }
 $done=Invoke-AzuriteFullLeaseFinalizer $backend $state
 LeaseRegression (-not $done.Held -and -not $done.OverallPassed -and $done.OwnerRetained -and $done.Persisted) ("unheld failure retains only local reference/C evidence: "+$mode)
 LeaseRegression ($done.Creates -eq 0 -and -not $done.Calls.Contains('RestorePendingCreateNew')) ("no pending compensation after ownership ended: "+$mode)
 if($mode -ceq 'dispose-failed'){
  LeaseRegression ($done.ReleaseSucceeded -and $null -eq $done.ReleaseError) 'successful Release stays truthful after Dispose throws'
  LeaseRegression ($done.DisposeAttempted -and -not $done.DisposeSucceeded -and $null -ne $done.DisposeError -and $done.DisposeError.Contains('post-release Dispose')) 'Dispose failure has its separate explicit outcome'
 }else{
  LeaseRegression (-not $done.ReleaseSucceeded -and $null -ne $done.ReleaseError -and $done.ReleaseError.Contains('after clearing held') -and -not $done.Calls.Contains('Dispose')) 'partial Release callback error does not manufacture confirmed release/disposal'
 }
}
$noLease=@{Held=$false;Plan=$plan;Calls=0;ReleaseError='fixture';PendingEvidence=[pscustomobject]@{record=$pending;evidence=[pscustomobject]@{sddl=$private}}}
$guardRefused=$false;try{Invoke-AzuriteFullPendingCompensation {param($operation,$s,$request)$s.Calls++;if($operation -ceq 'Exists'){return $false}} $noLease|Out-Null}catch{$guardRefused=$true}
LeaseRegression ($guardRefused -and $noLease.Calls -eq 0) 'compensation helper rejects unheld state before any filesystem backend operation'
[pscustomobject]@{scope='post-release ownership regression controls';failures=$leaseRegressionFailures.ToArray()}|ConvertTo-Json -Compress
if($leaseRegressionFailures.Count){throw ('Lease-finalization regression controls failed: '+($leaseRegressionFailures -join '; '))}
foreach($name in @('RestrictedCliBootstrap.ps1','Invoke-AzuriteRestrictedCli.ps1','FullRestrictedCapabilitiesRuntime.psm1')){$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $RepoRoot ('tools/headless/'+$name)),[ref]$t,[ref]$e);Check ($e.Count -eq 0) "parse $name"}
[pscustomobject]@{schema=1;mode='pure runtime predicates and PS5.1 parser; no native stage APIs';passed=$true;checks=$script:count}|ConvertTo-Json -Compress
