#requires -Version 7.0
param([switch]$ProcessFixtures)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$passed=0; $failed=0
function Check([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
function Deny([scriptblock]$Body){$caught=$false;try{& $Body|Out-Null}catch{$caught=$true};Check $caught 'unsafe input was accepted'}
function Test([string]$Name,[scriptblock]$Body){try{& $Body;$script:passed++;Write-Output "PASS $Name"}catch{$script:failed++;Write-Output "FAIL $Name : $($_.Exception.Message)"}}
$module=Join-Path $PSScriptRoot 'EditorRouteCore.psm1'
Test 'controller module exists' {Check (Test-Path -LiteralPath $module) 'route controller is not implemented'}
if(-not(Test-Path -LiteralPath $module)){exit 1}
Import-Module $module -Force -DisableNameChecking
$id='44ea1a81-c812-4d69-ab38-3e66890b615c'
Test 'GUID run emits only capabilities and fixed native arguments' {
 $s=Get-AzuriteEditorRouteSpec $id
 Check ($s.runRoot -ceq ('E:\aamod\labs\Azurite-20261108-editor\runs\'+$id)) 'wrong lab run'
 Check (($s.nativeArguments -join '|') -ceq ('-batchmode|-nographics|-disableaudio|-logFile|'+$s.runRoot+'\output\unity.log')) 'wrong native flags'
 Check ($s.argv.Count -eq 1 -and $s.argv[0] -ceq 'capabilities') 'not resource-free capabilities'
 Check ($s.jobId -ceq '44ea1a81c8124d69ab383e66890b615c') 'GUID-D to owned-job correlation lost'
}
Test 'run ID alias or path injection rejected' {foreach($bad in @('44ea1a81c8124d69ab383e66890b615c','../x','44EA1A81-C812-4D69-AB38-3E66890B615C','')){Deny {Get-AzuriteEditorRouteSpec $bad}}}
Test 'all 26 settings are sanitized and internal audio remains unity gain' {
 $s=Get-AzuriteEditorRouteSpec $id;$seed=New-AzuriteEditorRouteSeed $s 'lab-read'
 Check (@($seed.Keys).Count -eq 26) 'incomplete native settings schema'
 foreach($key in @('masterVolume','voiceVolume','bgmVolume','soundEffectVolume')){Check ($seed[$key] -eq 1) 'internal PCM was muted'}
 foreach($key in @('stEnabled','autoSave','autoCompile','lobbyBgmEnabled','checkForUpdate','isAutoPlay')){Check ($seed[$key] -ceq $false) 'daily behavior enabled'}
 foreach($key in @('visitedFiles','favoriteBgmIds','favoriteSoundNames')){Check ($seed[$key].Count -eq 0) 'seed inherited daily list'}
 Check ($seed.username -ceq ('lab-read-'+$id)) 'missing real-read marker'
 Check ($seed.workspacePath -ceq ($s.runRoot+'\workspace') -and $seed.cachePath -ceq ($s.runRoot+'\cache')) 'seed path escaped'
}
Test 'manifest binds main and miss-control seeds separately' {
 $s=Get-AzuriteEditorRouteSpec $id;$m=New-AzuriteEditorRouteManifest $s ('A'*64) ('B'*64)
 Check ($m.Version -eq 1 -and $m.Mode -ceq 'route-selftest' -and $m.RunId -ceq $id) 'manifest identity invalid'
 Check ($m.SeedSha256 -ceq ('A'*64) -and $m.ControlSha256 -ceq ('B'*64)) 'seed binding missing'
 Check (@($m.Keys).Count -eq 8) 'unexpected manifest property'
 Deny {New-AzuriteEditorRouteManifest $s 'controller-json' ('B'*64)}
}
$fixture=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))) ('editor-route-fixture-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$spec=Get-AzuriteEditorRouteSpec $id
$manifestHash='A'*64;$nativePid=3210;$start=[datetime]::UtcNow.AddSeconds(-3);$end=$start.AddSeconds(2)
function Event([string]$Kind,$Data,[long]$Tick,[long]$Sequence){return [pscustomobject]@{sequence=$Sequence;utc=$start.AddMilliseconds($Tick).ToString('o');monotonicTicks=$Tick;runId=$id;pid=$nativePid;manifestSha256=$manifestHash;kind=$Kind;data=[pscustomobject]$Data}}
function Fixture {
 $s=$script:spec;$events=[Collections.Generic.List[object]]::new();$n=0
 $events.Add((Event 'install-enter' @{RunId=$id;Mode='route-selftest';manifestSha256=$manifestHash;startupOrderingProved=$false} 20 (++$n)))
 $methods=@(@('awake',100689258,0x7f5710),@('read',100689386,0x7f24e0),@('save',100689392,0x7f2610),@('write',100689268,0x7f6640),@('fallback',100689266,0x7f5dd0),@('cache',100689255,0x7f6a20),@('workspace',100689256,0x7f6c70),@('resources',100688737,0x7bffd0),@('discovery',100694828,0x90a330),@('offline',100695181,0x93c5b0),@('addressSet',100663883,0x2f6f900),@('addressClear',100663884,0x2f6edb0),@('addressEvaluate',100663885,0x2f6ee30))
 foreach($m in $methods){$events.Add((Event 'native-binding' @{Id=$m[0];Token=$m[1];Rva=$m[2];methodInfo=(1000000+$n*64);entry=(200000000+$m[2])} (20+$n) (++$n)))}
 $events.Add((Event 'hooks-registered' @{count=12;nativeInterceptionProved=$false;priorAwakeAbsenceProved=$false} 45 (++$n)))
 $events.Add((Event 'cli-awake-enter' @{owner=7000;cliActive=$true;nativeAwakeWillRun=$true;globalStartupOrderProved=$false} 60 (++$n)))
 $events.Add((Event 'native-read-dispatch' @{owner=7000;iterator=8000;missControl=$s.controlSeedPath} 70 (++$n)))
 $events.Add((Event 'native-read-prefix' @{owner=7000;iterator=8000;__1__state=0;actualSettingFilePath=$s.settingsPath;actualCachedWorkspace=($s.runRoot+'\workspace')} 80 (++$n)))
 $events.Add((Event 'native-original-read-completed' @{owner=7000;data=9000;yielded=$false;marker=('lab-read-'+$id)} 90 (++$n)))
 foreach($key in @('cache','workspace')){$events.Add((Event 'native-getter-routed' @{id=$key;route=($s.runRoot+'\'+$key)} (100+$n) (++$n)));$events.Add((Event 'native-readonly-getter-returned' @{id=$key;returned=($s.runRoot+'\'+$key);downstreamIo=$false;hitDelta=1} (100+$n) (++$n)))}
 $events.Add((Event 'native-getter-routed' @{id='discovery';route=($s.runRoot+'\discovery')} 140 (++$n)))
 $events.Add((Event 'native-discovery-getter-returned' @{returned=($s.runRoot+'\discovery');listenerStarted=$false} 150 (++$n)))
 $events.Add((Event 'addressables-reseeded-after-native-clear' @{persistent=($s.runRoot+'\persistent');originalClearObserved=$true} 160 (++$n)))
 $events.Add((Event 'native-addressables-property-returned' @{returned=($s.runRoot+'\persistent');catalogLoaded=$false} 170 (++$n)))
 $events.Add((Event 'native-write-admitted' @{owner=7000;target=$s.settingsPath} 180 (++$n)))
 $events.Add((Event 'native-original-write-returned' @{owner=7000} 190 (++$n)))
 $events.Add((Event 'route-selftest-complete' @{readHits=1;writeHits=1;saveEdge='native SaveTask.MoveNext -> native WriteUserSettings';savedSha256=('B'*64);readinessUntouched=$true;normalStartupProved=$false;catalogProved=$false} 200 (++$n)))
 $g=@((Event 'guardian-armed-before-chainloader' @{RunId=$id;Mode='route-selftest';manifestSha256=$manifestHash;executeMetadataToken=100663400;executeAssembly='BepInEx.Core'} 10 1),(Event 'all-routing-hooks-registered' @{manifestSha256=$manifestHash;routes=@('awake','read','write','fallback','cache','workspace','resources','discovery','offline','addressSet','addressClear','addressEvaluate');nativeHookHitsProved=$false} 40 2),(Event 'chainloader-return-admitted' @{manifestSha256=$manifestHash;nativeStartupMayResume=$true;normalReadinessProved=$false} 50 3))
 return [pscustomobject]@{probe=$events.ToArray();guardian=$g}
}
function Validate($F){Assert-AzuriteEditorRouteEvidence -Probe $F.probe -Guardian $F.guardian -Spec $spec -ManifestSha256 $manifestHash -NativePid $nativePid -StartedUtc $start -CompletedUtc $end -SavedSha256 ('B'*64)}
Test 'complete native original read/save and guardian proof is mandatory' {$f=Fixture;$r=Validate $f;Check ($r.nativeReadProved -and $r.nativeSaveProved -and -not $r.editorAccepted) 'wrong acceptance scope'}
Test 'registration and return code cannot replace native completion' {$f=Fixture;$f.probe=@($f.probe|Where-Object kind -in @('install-enter','native-binding','hooks-registered'));Deny {Validate $f}}
Test 'each stream is PID run and manifest bound' {
 foreach($field in @('pid','runId','manifestSha256')){$f=Fixture;$f.probe[0].$field=if($field -eq 'pid'){9999}else{'wrong'};Deny {Validate $f}}
 $f=Fixture;$f.guardian[0].PSObject.Properties.Remove('pid');Deny {Validate $f}
}
Test 'missing original marker native hit or saved digest fails' {
 $f=Fixture;($f.probe|Where-Object kind -eq 'native-original-read-completed').data.marker='miss-control-'+$id;Deny {Validate $f}
 $f=Fixture;$f.probe[-1].data.readHits=0;Deny {Validate $f}
 $f=Fixture;$f.probe[-1].data.savedSha256='C'*64;Deny {Validate $f}
 $f=Fixture;($f.probe|Where-Object kind -eq 'native-read-prefix').data.iterator=9999;Deny {Validate $f}
}
Test 'guardian must admit before genuine awake with complete routes' {
 $f=Fixture;$f.guardian[-1].monotonicTicks=300;Deny {Validate $f}
 $f=Fixture;$f.guardian[1].data.routes=@('read','write');Deny {Validate $f}
}
Test 'native getter miss never counts as containment proof' {$f=Fixture;($f.probe|Where-Object kind -eq 'native-readonly-getter-returned'|Select-Object -First 1).data.hitDelta=0;Deny {Validate $f}}
Test 'truncated reordered stale failure or unknown evidence is rejected' {
 $f=Fixture;$f.probe[2].sequence=99;Deny {Validate $f}
 $f=Fixture;$f.probe[2].utc=$start.AddDays(-1).ToString('o');Deny {Validate $f}
 $f=Fixture;$f.probe[2].kind='route-selftest-failed';Deny {Validate $f}
 $f=Fixture;$f.probe[2].kind='invented-proof';Deny {Validate $f}
}
Test 'bounded JSONL refuses partial or oversized evidence' {
 $p=Join-Path $fixture 'evidence.jsonl';[IO.File]::WriteAllText($p,((Fixture).probe|ForEach-Object {($_|ConvertTo-Json -Depth 8 -Compress)+"`n"}) -join '')
 $events=@(Read-AzuriteEditorRouteJsonl $p);Check ($events.Count -gt 25) 'complete evidence was not read'
 [IO.File]::AppendAllText($p,'{');Deny {Read-AzuriteEditorRouteJsonl $p}
 [IO.File]::WriteAllText($p,('x'*1048577));Deny {Read-AzuriteEditorRouteJsonl $p}
}
Test 'monitor failure cannot be hidden by success result' {
 $n=[pscustomobject]@{ExitCode=0;TimedOut=$false;WindowViolation=$false;OutputLimitExceeded=$false;Error=''}
 Assert-AzuriteEditorRouteNativeStatus $n Positive
 foreach($field in @('TimedOut','WindowViolation','OutputLimitExceeded')){$n.$field=$true;Deny {Assert-AzuriteEditorRouteNativeStatus $n Positive};$n.$field=$false}
 $n.Error='monitor failed';Deny {Assert-AzuriteEditorRouteNativeStatus $n Positive};$n.Error='';$n.ExitCode=86
 Deny {Assert-AzuriteEditorRouteNativeStatus $n Positive};Assert-AzuriteEditorRouteNativeStatus $n ProbeMissing
}
Test 'only explicit negative scope admits native guard exit 86' {
 $n=[pscustomobject]@{ExitCode=0;TimedOut=$false;WindowViolation=$false;OutputLimitExceeded=$false;Error=''}
 Deny {Assert-AzuriteEditorRouteNativeStatus $n ProbeMissing};$n.ExitCode=86;Assert-AzuriteEditorRouteNativeStatus $n InvalidManifest
}
Test 'review inputs cannot omit executable guardian or bootstrap pins' {
 Deny {New-AzuriteEditorRouteContract ([pscustomobject]@{schema=1}) ([pscustomobject]@{schema=1}) Positive}
}
Test 'PlanOnly cannot create a run or launch on unmet reviewed prerequisites' {
 $before=@(Get-ChildItem 'E:\aamod\labs\Azurite-20261108-editor' -Force|ForEach-Object Name)
 $plan=& (Join-Path $PSScriptRoot 'Invoke-AzuriteEditorRouteSelfTest.ps1') -PlanOnly
 Check ($plan.willLaunch -ceq $false -and $plan.willWrite -ceq $false) 'plan attempted work'
 $after=@(Get-ChildItem 'E:\aamod\labs\Azurite-20261108-editor' -Force|ForEach-Object Name)
 Check (($before -join '|') -ceq ($after -join '|')) 'plan wrote lab entries'
}
Test 'negative proof requires guardian-own abort before continuation' {
 $g=@((Event 'guardian-setup-failed' @{reason='invalid fixture JSON';nativeStartupMayResume=$false} 10 1))
 $r=Assert-AzuriteEditorRouteNegativeEvidence @() $g $spec $manifestHash $nativePid $start $end InvalidManifest
 Check ($r.expectedAbortProved -eq $true -and -not $r.editorAccepted) 'negative proof scope wrong'
 $g[0].data.nativeStartupMayResume=$true;Deny {Assert-AzuriteEditorRouteNegativeEvidence @() $g $spec $manifestHash $nativePid $start $end InvalidManifest}
}
Test 'outer controller correlates native guard evidence and rejects a substituted PID' {
 $s=Get-AzuriteEditorRouteSpec $id;$s.runRoot=$fixture;$s.outputRoot=Join-Path $fixture 'output';$s.requestRoot=Join-Path $fixture 'request';$s.settingsPath=Join-Path $fixture 'user_settings.json';$s.nativeArguments=@('-batchmode','-nographics','-disableaudio','-logFile',(Join-Path $s.outputRoot 'unity.log'))
 [void][IO.Directory]::CreateDirectory($s.outputRoot);[void][IO.Directory]::CreateDirectory($s.requestRoot)
 Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -DisableNameChecking
 $sid='S-1-5-21-1-2-3-1001';$paths=[ordered]@{userProfile='C:\fixture';environmentProfile='C:\fixture';localAppData='C:\fixture\AppData\Local';localLow='C:\fixture\AppData\LocalLow';environmentLocalAppData='C:\fixture\AppData\Local';environmentAppData='C:\fixture\AppData\Roaming';environmentTemp='C:\fixture\temp';environmentTmp='C:\fixture\temp'}
 $identity=[ordered]@{runId=$id;pid=1234;status='passed';identitySid=$sid;isAdministrator=$false;hiveLoaded=$true;unexpectedPrivileges=@()};foreach($name in $paths.Keys){$identity[$name]=$paths[$name]}
 $ip=Join-Path $s.outputRoot 'identity.json';Write-AzuriteFlushedJson $ip $identity
 $c=[pscustomobject]@{scope='InvalidManifest';runId=$id;jobId=$s.jobId;controllerSid=$sid;controllerProfile='C:\fixture';currentLocalLow=$paths.localLow;currentEnvironment=[pscustomobject]$paths;contractSha256=('C'*64);manifestSha256=$manifestHash;argumentsSha256=('D'*64)}
 $r=[pscustomobject]@{schema=1;identityMode='CurrentAccount';scope='InvalidManifest';runId=$id;jobId=$s.jobId;bootstrapPid=1234;controllerSid=$sid;status='passed';jobOwned=$true;contractSha256=('C'*64);manifestSha256=$manifestHash;argumentsSha256=('D'*64);noSoundConfigured=$true;noSoundProofSha256=('E'*64);acceptance=$s.acceptance;aaPid=$nativePid;aaStartedUtc=$start.ToString('o');aaCompletedUtc=$end.ToString('o');aaExitCode=86;timedOut=$false;windowViolation=$false;outputLimitExceeded=$false;processError='';nativeArguments=$s.nativeArguments;identitySha256=(Get-AzuriteSha256 $ip);profilePathProof=[pscustomobject]@{schema=1;identityMode='CurrentAccount';identitySid=$sid;filesystemVerified=$true;method='current-account-shared-profile-v1';checkedUtc=$start.AddMilliseconds(-100).ToString('o');paths=[pscustomobject]$paths;profileShared=$true;hiveShared=$true;accountIsolation=$false};guardianSha256='';probeSha256=$null;savedSha256=''}
 $g=Event 'guardian-setup-failed' @{reason='invalid fixture';nativeStartupMayResume=$false} 10 1
 $gp=Join-Path $s.outputRoot 'editor-isolation-guardian.jsonl';[IO.File]::WriteAllText($gp,($g|ConvertTo-Json -Depth 8 -Compress)+"`n");$r.guardianSha256=Get-AzuriteSha256 $gp
 [IO.File]::WriteAllText($s.settingsPath,'unchanged fixture seed');$r.savedSha256=Get-AzuriteSha256 $s.settingsPath
 foreach($name in @('aa.stdout.log','aa.stderr.log','unity.log')){$p=Join-Path $s.outputRoot $name;[IO.File]::WriteAllText($p,'harmless fixture');$r|Add-Member -NotePropertyName ($name+'Sha256') -NotePropertyValue (Get-AzuriteSha256 $p)}
 $outer=[pscustomobject]@{Pid=1234;RunId=$s.jobId;StartedUtc=$start.AddSeconds(-1);CleanupVerified=$true;DirectProcessExited=$true;JobDrained=$true;RemainingOwnedProcesses=0;JobObserved=$true;NoProcessCreated=$false}
 $c|Add-Member -NotePropertyName deadlineUtc -NotePropertyValue $end.AddMinutes(1).ToString('o')
 $q=[pscustomobject]@{schema=1;method='owned-job-singleton-v1';runId=$id;bootstrapPid=1234;bootstrapCreatedUtc=$start.AddMilliseconds(-500).ToString('o');nativePid=$nativePid;nativeDirectProcessExited=$true;startedUtc=$end.AddMilliseconds(1).ToString('o');completedUtc=$end.AddMilliseconds(40).ToString('o');deadlineUtc=$c.deadlineUtc;limitMilliseconds=2000;elapsedMilliseconds=39;attemptCount=2;singletonObservations=2;quiescent=$true;reason='two-exact-bootstrap-only-observations';queryFailure=$null;lastMemberIds=@(1234);samples=@([pscustomobject]@{utc=$end.AddMilliseconds(10).ToString('o');memberCount=1;memberIds=@(1234);queryError=$null},[pscustomobject]@{utc=$end.AddMilliseconds(35).ToString('o');memberCount=1;memberIds=@(1234);queryError=$null})}
 $r|Add-Member -NotePropertyName nativeQuiescence -NotePropertyValue $q
 $contract=[pscustomobject]@{acceptance=$s.acceptance;evidence=@([pscustomobject]@{path='C:\proof\STARTUP-CONTRACT.md';sha256=('E'*64)})}
 $run=[pscustomobject]@{spec=$s;control=$c}
 $v=Assert-AzuriteEditorRouteResult $run $outer $r $contract;Check $v.expectedAbortProved 'correlated guardian proof not accepted'
 $q.quiescent=$false;Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract};$q.quiescent=$true
 $q.samples[0].memberIds=@(1234,9999);Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract};$q.samples[0].memberIds=@(1234)
 $q.runId=[guid]::NewGuid().ToString('D');Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract};$q.runId=$id
 $q.completedUtc=$end.AddMinutes(2).ToString('o');Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract};$q.completedUtc=$end.AddMilliseconds(40).ToString('o')
 $q.limitMilliseconds='2000';Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract};$q.limitMilliseconds=2000
 $r.aaPid=9999;Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract}
 $r.aaPid=$nativePid;$r.scope='Positive';$c.scope='Positive';$r.aaExitCode=0
 $oldSpec=$script:spec;$script:spec=$s
 try{$positive=Fixture}finally{$script:spec=$oldSpec}
 [IO.File]::WriteAllText($s.settingsPath,(@{username=('lab-saved-'+$id);workspacePath=(Join-Path $s.runRoot 'workspace');cachePath=(Join-Path $s.runRoot 'cache')}|ConvertTo-Json -Compress))
 $r.savedSha256=Get-AzuriteSha256 $s.settingsPath;$positive.probe[-1].data.savedSha256=$r.savedSha256
 [IO.File]::WriteAllText($gp,(($positive.guardian|ForEach-Object {($_|ConvertTo-Json -Depth 8 -Compress)+"`n"}) -join ''));$r.guardianSha256=Get-AzuriteSha256 $gp
 $pp=Join-Path $s.outputRoot 'editor-isolation-evidence.jsonl';[IO.File]::WriteAllText($pp,(($positive.probe|ForEach-Object {($_|ConvertTo-Json -Depth 8 -Compress)+"`n"}) -join ''));$r.probeSha256=Get-AzuriteSha256 $pp
 $responsePath=Join-Path $s.requestRoot 'response.json';[IO.File]::WriteAllBytes($responsePath,[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\..\Tests\headless\fixtures\native-capabilities-20261108.json')))
 $r|Add-Member -NotePropertyName responseSha256 -NotePropertyValue (Get-AzuriteSha256 $responsePath);$r|Add-Member -NotePropertyName responseBytes -NotePropertyValue (Get-Item -LiteralPath $responsePath).Length
 $v=Assert-AzuriteEditorRouteResult $run $outer $r $contract;Check $v.nativeReadProved 'complete original-route and actual capabilities fixture rejected'
 [IO.File]::Move($responsePath,($responsePath+'.withheld'));Deny {Assert-AzuriteEditorRouteResult $run $outer $r $contract}
}
Test 'guardian absence wrong placement and unplanned probe copies fail before launch' {
 $install=Join-Path $fixture 'fake-install';[void][IO.Directory]::CreateDirectory($install)
 [void][IO.Directory]::CreateDirectory((Join-Path $install 'profiles\AzuriteAcceptance'))
 [IO.File]::WriteAllText((Join-Path $install 'ActiveProfile.txt'),'AzuriteAcceptance')
 [IO.File]::WriteAllText((Join-Path $install 'profiles\AzuriteAcceptance\modconfig.json'),'{"EnabledMods":[{"name":"Azurite","version":"1.0.38"}]}')
 $base=[pscustomobject]@{schema=1;contractId='aa-2023.2.22f1-azurite-1.0.38-cli-nosound-v2';labRoot='unused fixture';acceptance='fixture';commands=@('capabilities','schema');revisionNote='fixture';profile='AzuriteAcceptance';modName='Azurite';modVersion='1.0.38';files=@();exactDirectories=@([pscustomobject]@{path='BepInEx/patchers';files=@()},[pscustomobject]@{path='BepInEx/plugins';files=@()});evidence=@()}
 $toolPins=[ordered]@{};foreach($name in Get-AzuriteEditorRouteToolNames){$toolPins[$name]='A'*64}
 $bins=@();foreach($path in Get-AzuriteEditorRouteBinaryPaths){$full=Join-Path $install $path;[void][IO.Directory]::CreateDirectory((Split-Path -Parent $full));[IO.File]::WriteAllText($full,('NOT EXECUTABLE fixture '+$path));$bins+=@([pscustomobject]@{path=$path;sha256=(Get-AzuriteSha256 $full)})}
 $inputs=[pscustomobject]@{schema=1;admission='reviewed-cli-route-selftest-only';labRoot='E:\aamod\labs\Azurite-20261108-editor';baseContractSha256='5CED2B8185DFE43E94217F3B5D61F7B76D8DC5D70C817CABB50A098D46DC8362';toolPins=[pscustomobject]$toolPins;binaries=$bins;copyReceipt=[pscustomobject]@{path='copy';sha256=('B'*64)};harnessReceipt=[pscustomobject]@{path='harness';sha256=('C'*64)};guardReview=[pscustomobject]@{path='review';sha256=('D'*64)};orderingAudioProof=[pscustomobject]@{path='audio';sha256=('E'*64)}}
 $contract=New-AzuriteEditorRouteContract $inputs $base Positive;Assert-AzuritePinnedInstall $install $contract
 $guard=Join-Path $install $bins[0].path;$backup=$guard+'.retained';[IO.File]::Move($guard,$backup);Deny {Assert-AzuritePinnedInstall $install $contract};[IO.File]::Move($backup,$guard)
 $duplicate=Join-Path $install 'BepInEx\plugins\AzuriteEditorIsolation\Azurite.EditorIsolation.Guard.LabOnly.dll';[IO.File]::Copy($guard,$duplicate);Deny {Assert-AzuritePinnedInstall $install $contract};[IO.File]::Move($duplicate,(Join-Path $fixture 'retained-duplicate.dll'))
 $negative=New-AzuriteEditorRouteContract $inputs $base ProbeMissing;Deny {Assert-AzuritePinnedInstall $install $negative}
 [IO.File]::Move((Join-Path $install $bins[2].path),(Join-Path $fixture 'retained-probe.dll'));Assert-AzuritePinnedInstall $install $negative
}
Test 'shared typed-state policy accepts only the two known engine metadata changes' {
 Import-Module (Join-Path $PSScriptRoot 'CurrentAccountCli.psm1') -DisableNameChecking
 $statePath=Join-Path $fixture 'state';[void][IO.Directory]::CreateDirectory($statePath);[IO.File]::WriteAllText((Join-Path $statePath 'settings.json'),'fixture preferences')
 $stateSpec=[pscustomobject]@{schema=1;identitySid='S-1-5-21-1-2-3-1001';registryPath='Software\foxxlight\AzureArchive';roots=@([pscustomobject]@{label='LocalLowAA';path=$statePath;kind='Directory';required=$true})}
 function DecimalValue([string]$Name,[string]$Value){[pscustomobject]@{key='';name=$Name;kind='Binary';data=[Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($Value+[char]0))}}
 $registry=[pscustomobject]@{exists=$true;keys=@('');values=@((DecimalValue 'unity.player_session_count_h922449978' '90'),(DecimalValue 'unity.player_sessionid_h1351336811' '100'))}
 $before=New-AzuriteSharedSnapshot $stateSpec $registry $id Before;$after=New-AzuriteSharedSnapshot $stateSpec $registry $id After
 $after.registry.values=@((DecimalValue 'unity.player_session_count_h922449978' '91'),(DecimalValue 'unity.player_sessionid_h1351336811' '101'))
 $delta=Compare-AzuriteSharedSnapshots $before $after $stateSpec $id
 Check ($delta.accepted -and $delta.engineMetadata.Count -eq 2 -and -not $delta.restorationPerformed) 'known policy not preserved'
 $after.registry.values[0]=DecimalValue 'unity.player_session_count_h922449978' '93';Check (-not (Compare-AzuriteSharedSnapshots $before $after $stateSpec $id).accepted) 'multi-session delta allowed'
 [IO.File]::WriteAllText((Join-Path $statePath 'settings.json'),'changed preferences');$after=New-AzuriteSharedSnapshot $stateSpec $registry $id After
 Check (-not (Compare-AzuriteSharedSnapshots $before $after $stateSpec $id).accepted) 'application preference write allowed'
}
Test 'manifest and CLI environment are set on child only' {
 $before=[Environment]::GetEnvironmentVariable('AZURITE_EDITOR_ISOLATION_MANIFEST','Process');$cliBefore=[Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','Process')
 $psi=New-AzuriteProcessInfo -FilePath (Join-Path ([Environment]::SystemDirectory) 'cmd.exe') -Arguments @('/d','/c','exit','0') -WorkingDirectory $fixture
 $s=Get-AzuriteEditorRouteSpec $id;Set-AzuriteEditorRouteChildEnvironment $psi $s
 Check ($psi.EnvironmentVariables['AZURITE_EDITOR_ISOLATION_MANIFEST'] -ceq $s.manifestPath -and $psi.EnvironmentVariables['AZUREARCHIVE_CLI_DIRECTORY'] -ceq $s.requestRoot) 'child transport missing'
 Check ([Environment]::GetEnvironmentVariable('AZURITE_EDITOR_ISOLATION_MANIFEST','Process') -ceq $before -and [Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','Process') -ceq $cliBefore) 'controller/global environment was changed'
}
Test 'expired or unbounded deadline refuses even a harmless child start' {
 $psi=New-AzuriteProcessInfo -FilePath 'C:\not-started-fixture.exe' -Arguments @() -WorkingDirectory $fixture
 Deny {Invoke-AzuriteEditorRouteOwnedBootstrap $psi $id ([datetime]::UtcNow.AddMinutes(-1)) @($fixture)}
 Deny {Invoke-AzuriteEditorRouteOwnedBootstrap $psi $id ([datetime]::UtcNow.AddMinutes(10)) @($fixture)}
}
Test 'unexpected output and resource writes fail effect verification' {
 $root=Join-Path $fixture 'effects';[void][IO.Directory]::CreateDirectory($root)
 $s=Get-AzuriteEditorRouteSpec $id;$s.runRoot=$root;$s.outputRoot=Join-Path $root 'output';$s.requestRoot=Join-Path $root 'request';$s.settingsPath=Join-Path $root 'settings\user_settings.json';$s.controlSeedPath=Join-Path $root 'settings\miss_control.json';$s.manifestPath=Join-Path $root 'manifest.json'
 foreach($name in @('settings','output','request','workspace','cache','persistent','discovery')){[void][IO.Directory]::CreateDirectory((Join-Path $root $name))}
 foreach($path in @($s.settingsPath,$s.controlSeedPath,$s.manifestPath,(Join-Path $s.requestRoot 'arguments.json'))){[IO.File]::WriteAllText($path,'{}')}
 $c=[pscustomobject]@{scope='InvalidManifest';manifestSha256=(Get-AzuriteSha256 $s.manifestPath);controlSeedSha256=(Get-AzuriteSha256 $s.controlSeedPath);seedSha256=(Get-AzuriteSha256 $s.settingsPath);argumentsSha256=(Get-AzuriteSha256 (Join-Path $s.requestRoot 'arguments.json'));toolHashes=[pscustomobject]@{}}
 $run=[pscustomobject]@{spec=$s;control=$c};Assert-AzuriteEditorRouteEffects $run
 [IO.File]::WriteAllText((Join-Path $s.outputRoot 'unexpected.json'),'{}');Deny {Assert-AzuriteEditorRouteEffects $run}
 [IO.File]::Move((Join-Path $s.outputRoot 'unexpected.json'),(Join-Path $fixture 'unexpected-retained.json'))
 [IO.File]::WriteAllText((Join-Path $root 'cache\unexpected.bin'),'bad');Deny {Assert-AzuriteEditorRouteEffects $run}
}
if($ProcessFixtures){
 Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -DisableNameChecking
 $helper=Join-Path $fixture 'RestrictedCliNative.dll';[void](New-AzuriteNetFrameworkHelper (Join-Path $PSScriptRoot 'RestrictedCliNative.cs') $helper)
 foreach($mode in @('Complete','Timeout','NoHandshake','OutputLimit')){
  Test ('real harmless owned-process adapter '+$mode) {
   $pidFile=Join-Path $fixture ($mode+'.json');$childId=[guid]::NewGuid().ToString('D')
   $shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
   $psi=New-AzuriteProcessInfo -FilePath $shell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'EditorRoute.FixtureBootstrap.ps1'),'-RunId',$childId,'-Helper',$helper,'-Output',$pidFile,'-Mode',$mode) -WorkingDirectory $fixture
   $deadline=[datetime]::UtcNow.AddSeconds(12)
   $outer=Invoke-AzuriteEditorRouteOwnedBootstrap $psi $childId $deadline @($fixture)
   Write-AzuriteFlushedJson (Join-Path $fixture ($mode+'-outer.json')) $outer
   if($mode -ceq 'NoHandshake'){Deny {Assert-AzuriteCleanupConfirmed $outer}}else{Assert-AzuriteCleanupConfirmed $outer;Check ($outer.RemainingOwnedProcesses -eq 0 -and $outer.CleanupMilliseconds -le 5500) 'bounded full drain failed'}
   Check ($outer.RunId -ceq ([guid]$childId).ToString('N')) 'GUID mapping failed'
   if($mode -ceq 'Complete'){Check (-not(Get-AzuriteOuterProcessStatus $outer).failed) 'real complete run rejected';$record=Read-AzuriteBoundedJson $pidFile;Check ($record.output -ceq 'fixture-complete') 'real child output absent'}
   elseif($mode -ceq 'Timeout'){Check $outer.TimedOut 'deadline not enforced'}
   elseif($mode -ceq 'NoHandshake'){Check ((Get-AzuriteOuterProcessStatus $outer).failed) 'unacknowledged job admitted'}
   else{Check $outer.OutputLimitExceeded 'output overflow not enforced'}
   if(Test-Path -LiteralPath $pidFile){$record=Read-AzuriteBoundedJson $pidFile;Check ($null -eq (Get-Process -Id $record.childPid -ErrorAction SilentlyContinue)) 'owned fixture child survived drain'}
  }
 }
}
Write-Output "RESULT passed=$passed failed=$failed; fixture-only; AA/Unity not launched"
if($failed){exit 1}
