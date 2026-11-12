param([string]$ResultPath)
$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot '..\..\tools\headless\Get-AzuriteFullStageBaseline.ps1'
if(-not(Test-Path -LiteralPath $scriptPath)){throw 'ASSERT: exact stage baseline collector is missing'}
. $scriptPath -RunId ('1'*32)
$checks=[Collections.Generic.List[string]]::new()
function Check($ok,[string]$name){if(-not $ok){throw ('ASSERT: '+$name)};$checks.Add($name)}
$plan=New-AzuriteStageBaselineScope ('1'*32)
Check ($plan.objects.Count -eq 1228) 'exact 1228 historical objects'
Check (@($plan.objects|Where-Object kind -CEQ 'InstallFile').Count -eq 1062) 'exact 1062 install files'
Check (@($plan.objects|Where-Object kind -CEQ 'InstallDirectory').Count -eq 157) 'exact 157 sealed install directories'
Check (@($plan.objects|Where-Object kind -CEQ 'AncestorDirectory').Count -eq 2) 'only P and L existing ancestors'
Check (@($plan.objects|Where-Object kind -CEQ 'HistoricalAnchor').Count -eq 7) 'exact seven historical anchors'
Check ($plan.installBytes -eq 3521516451 -and $plan.bytesPerSecond -eq 4194304 -and $plan.maximumWaitMilliseconds -eq 50) 'frozen bytes and bounded read budget'
Check ($plan.inventory.Count -eq 159 -and $plan.inventory['F:\AzuriteLabs'].Count -eq 2 -and $plan.inventory['F:\AzuriteLabs\Editor-20261108-normal'].Count -eq 3) 'exact old root and install inventory'
Check (@($plan.objects|Where-Object {$_.path -like 'E:*' -or $_.path -like '*\runs*'}).Count -eq 0) 'no daily installation or new-run target'
foreach($bad in @(('0'*32),('A'*32),'../outside')){$rejected=$false;try{$null=New-AzuriteStageBaselineScope $bad}catch{$rejected=$true};Check $rejected ('invalid RunId refused '+$bad)}
$entry=@($plan.objects|Where-Object kind -CEQ 'InstallFile')[0]
$sid=$plan.ownerSid;$sddl='O:'+ $sid+'G:'+ $sid+'D:(A;ID;FA;;;'+$sid+')(A;ID;FA;;;SY)(A;ID;FA;;;BA)'
$sample=[pscustomobject]@{path=$entry.path;identity=('a'*32);volume=$plan.volumeGuid;owner=$sid;finalPath=$entry.path;directory=$false;reparse=$false;links=1;sddl=$sddl;bytes=$entry.bytes;sha256=$entry.sha256;rawDescriptor='AA=='}
Assert-AzuriteStageBaselineObject $plan $entry $sample
Check $true 'actual readback matching pins accepted'
foreach($bad in @('identity','volume','owner','finalPath','links','reparse','bytes','sha256','sddl')){$copy=$sample|ConvertTo-Json -Compress|ConvertFrom-Json;switch($bad){'identity'{$copy.identity='bad'};'volume'{$copy.volume='wrong'};'owner'{$copy.owner='foreign'};'finalPath'{$copy.finalPath='F:\alias'};'links'{$copy.links=2};'reparse'{$copy.reparse=$true};'bytes'{$copy.bytes++};'sha256'{$copy.sha256=('b'*64)};'sddl'{$copy.sddl=$sddl.Replace(';ID;',';;')}};$rejected=$false;try{Assert-AzuriteStageBaselineObject $plan $entry $copy}catch{$rejected=$true};Check $rejected ('reject actual readback drift '+$bad)}
$dir=@($plan.objects|Where-Object kind -CEQ 'InstallDirectory')[0];$actual=[pscustomobject]@{path=$dir.path;identity=('b'*32);volume=$plan.volumeGuid;owner=$sid;finalPath=$dir.path;directory=$true;reparse=$false;links=1;sddl=('O:'+ $sid+'G:'+ $sid+'D:P(D;;0x6;;;'+$sid+')(A;OICI;FA;;;'+$sid+')(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)');bytes=0;sha256=$null;rawDescriptor='AA=='}
Assert-AzuriteStageBaselineObject $plan $dir $actual;Check $true 'exact original directory seal accepted'
$actual.sddl=$actual.sddl.Replace('(D;;0x6;;;'+$sid+')','');$rejected=$false;try{Assert-AzuriteStageBaselineObject $plan $dir $actual}catch{$rejected=$true};Check $rejected 'missing original creation seal refused'
$clock=@{milliseconds=0.0;sleeps=[Collections.Generic.List[int]]::new();checks=0}
$wait={param($ms)$clock.sleeps.Add($ms);$clock.milliseconds+=$ms};$now={return $clock.milliseconds};$check={$clock.checks++}
Wait-AzuriteStageBaselineBudget 8388608 $now $wait $check
Check ($clock.milliseconds -ge 2000 -and @($clock.sleeps|Where-Object {$_ -gt 50 -or $_ -lt 1}).Count -eq 0) 'perfile burst catchup enforces 4MiB/s with at most 50ms waits'
Check ($clock.checks -ge 2) 'catchup waits retain periodic AA0 checks'
$clock.milliseconds=10000;$before=$clock.sleeps.Count;Wait-AzuriteStageBaselineBudget 8388608 $now $wait $check;Check ($before -eq $clock.sleeps.Count) 'already under read budget adds no sleep'
$rejected=$false;try{Wait-AzuriteStageBaselineBudget 8388608 {return 0} {param($ms)} {throw 'AA appeared'}}catch{$rejected=$_.Exception.Message -eq 'AA appeared'};Check $rejected 'AA0 appearance aborts catchup without termination'
$schema=New-AzuriteStageBaselineRecord $plan @($sample)
Check ($schema.schema -eq 1 -and $schema.scope -ceq 'preapproval-stage-object-identities' -and $schema.runId -ceq ('1'*32) -and $schema.objects[0].identity -ceq $sample.identity -and $schema.objects[0].sddl -ceq $sample.sddl -and $schema.objects[0].bytes -eq $sample.bytes) 'writer baseline schema preserves actual identity descriptor and length'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath($scriptPath),[ref]$tokens,[ref]$errors)
Check ($errors.Count -eq 0) 'collector parses without errors'
$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.InvokeMemberExpressionAst]},$true)|ForEach-Object {$_.Member.Value})
Check (@($calls|Where-Object {$_ -in @('SetDescriptor','WriteExisting','DeleteExisting','NewFile','NewDirectory')}).Count -eq 0) 'collector has no helper mutation method calls'
Check (@($calls|Where-Object {$_ -ceq 'Exists'}).Count -eq 1) 'collector never probes nested pending marker paths'
# Same inventory preflight as Capture; substitute only read-only directory I/O.
# No real path lookup, reparse creation, helper load, mutex, or process query.
foreach($scenario in @('valid','unknown-runs','unknown-secrets','reparse-parent','reparse-lab')){
 $fake=[pscustomobject]@{timer=[pscustomobject]@{ElapsedMilliseconds=0L};lastAACheck=0L;scenario=$scenario;events=[Collections.Generic.List[string]]::new();inventories=$plan.inventory}
 $readDirectory={param($state,$path,$visit)
  $state.events.Add('lock:'+ $path)
  if(($state.scenario -ceq 'reparse-parent' -and $path -ceq 'F:\AzuriteLabs') -or ($state.scenario -ceq 'reparse-lab' -and $path -ceq 'F:\AzuriteLabs\Editor-20261108-normal')){throw 'fake reparse ancestry rejected'}
  $state.events.Add('enumerate:'+ $path)
  foreach($child in $state.inventories[$path]){& $visit ($path+'\'+$child)}
  if($path -ceq 'F:\AzuriteLabs\Editor-20261108-normal'){
   if($state.scenario -ceq 'unknown-runs'){& $visit ($path+'\runs')}
   if($state.scenario -ceq 'unknown-secrets'){& $visit ($path+'\secrets')}
  }
 }
 $rejected=$false;try{Assert-AzuriteBaselineInventory $plan $fake $readDirectory}catch{$rejected=$true}
 if($scenario -ceq 'valid'){Check (-not $rejected -and $fake.events.Count -eq 318 -and $fake.events[0] -ceq 'lock:F:\AzuriteLabs' -and $fake.events[2] -ceq 'lock:F:\AzuriteLabs\Editor-20261108-normal') 'valid baseline verifies P then L and continues all declared inventories'}
 else{Check ($rejected -and @($fake.events|Where-Object {$_ -like '*\install*' -or $_ -like '*pending*' -or $_ -like '*\runs*' -or $_ -like '*\secrets*'}).Count -eq 0) ('unsafe ancestor refused before descendant or pending probe '+$scenario)}
}
$public=& $scriptPath -RunId ('2'*32)
Check ($public.mode -ceq 'planning-only' -and $public.willReadF -eq $false -and $public.willWriteF -eq $false -and $public.objectCount -eq 1228) 'default public entry is pure C-only planning'
$result=[pscustomobject]@{schema=1;scope='pure-scope-hash-policy-and-fake-clock-no-F-no-native';passed=$checks.Count;tests=$checks.ToArray();powerShell=$PSVersionTable.PSVersion.ToString();framework=[Environment]::Version.ToString();objectCount=$plan.objects.Count;inventoryCount=$plan.inventory.Count;installBytes=$plan.installBytes}
$result|ConvertTo-Json -Depth 6
if($ResultPath){[IO.File]::WriteAllText([IO.Path]::GetFullPath($ResultPath),($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))}
