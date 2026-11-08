# Current-account mode fixtures: no AA, account/logon or daily-state writes.
param([switch]$CheckProductionPlan)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Assert($Condition,[string]$Message){if(-not $Condition){throw "FAIL: $Message"}}
function Refuses([scriptblock]$Action,[string]$Message){$failed=$false;try{& $Action|Out-Null}catch{$failed=$true};Assert $failed $Message}
$module=Join-Path $repo 'tools\headless\CurrentAccountCli.psm1'
Assert (Test-Path $module) 'explicit current-account mode helpers are missing'
Import-Module (Join-Path $repo 'tools\headless\HeadlessCore.psm1') -Force
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force
Import-Module $module -Force
$root=Join-Path $repo ('artifacts\current-account-cli-tests\'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$profile=Join-Path $root 'profile'
foreach($p in @('AppData\Local','AppData\LocalLow','AppData\Roaming','AppData\Local\Temp')){[void][IO.Directory]::CreateDirectory((Join-Path $profile $p))}
$identity=[pscustomobject]@{identitySid=$sid;isAdministrator=$false;hiveLoaded=$true;unexpectedPrivileges=@('SeDebugPrivilege');userProfile=$profile;environmentProfile=$profile;localAppData=(Join-Path $profile 'AppData\Local');localLow=(Join-Path $profile 'AppData\LocalLow');environmentLocalAppData=(Join-Path $profile 'AppData\Local');environmentAppData=(Join-Path $profile 'AppData\Roaming');environmentTemp=(Join-Path $profile 'AppData\Local\Temp');environmentTmp=(Join-Path $profile 'AppData\Local\Temp')}
$expectedEnvironment=$identity|ConvertTo-Json -Depth 6|ConvertFrom-Json
$proof=Assert-AzuriteCurrentIdentity $identity $sid $profile $identity.localLow $expectedEnvironment
Assert ($proof.identityMode -ceq 'CurrentAccount' -and $proof.hiveShared -and $proof.profileShared) 'same SID is accepted with honest shared-state labels'
Refuses {Assert-AzuriteCurrentIdentity $identity 'S-1-5-21-1-2-3-1009' $profile $identity.localLow $expectedEnvironment} 'wrong actual SID refused'
$identity.isAdministrator=$true;Refuses {Assert-AzuriteCurrentIdentity $identity $sid $profile $identity.localLow $expectedEnvironment} 'elevated current token refused';$identity.isAdministrator=$false
$routedTemp=Join-Path $root 'actual-controller-temp';[void][IO.Directory]::CreateDirectory($routedTemp)
$identity.environmentTemp=$routedTemp;$expectedEnvironment.environmentTemp=$routedTemp
[void](Assert-AzuriteCurrentIdentity $identity $sid $profile $identity.localLow $expectedEnvironment)
$identity.environmentTemp=Join-Path $profile 'AppData\Local\Temp'
Refuses {Assert-AzuriteCurrentIdentity $identity $sid $profile $identity.localLow $expectedEnvironment} 'current child cannot silently change the inherited environment'
$expectedEnvironment.environmentTemp=$identity.environmentTemp
Write-Output 'PASS: current SID and true profile accepted; wrong SID/elevation refused without SAM-membership gate'

$stateRoot=Join-Path $root 'shared-files';[void][IO.Directory]::CreateDirectory($stateRoot)
[IO.File]::WriteAllText((Join-Path $stateRoot 'settings.json'),'{"fixture":"unchanged"}')
$spec=[pscustomobject]@{schema=1;identitySid=$sid;registryPath='Software\foxxlight\AzureArchive';roots=@([pscustomobject]@{label='LocalLowAA';path=$stateRoot;kind='Directory';required=$true})}
function B64([string]$s){[Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($s+[char]0))}
$registry=[pscustomobject]@{exists=$true;keys=@('');values=@([pscustomobject]@{key='';name='Language';kind='String';data=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('fixture'))},[pscustomobject]@{key='';name='unity.player_session_count_h922449978';kind='Binary';data=(B64 '999')},[pscustomobject]@{key='';name='unity.player_sessionid_h1351336811';kind='Binary';data=(B64 '123456789012345678')})}
$id=[guid]::NewGuid().ToString('N');$before=New-AzuriteSharedSnapshot $spec $registry $id Before
Assert-AzuriteSharedSnapshot $before $spec $id Before
Refuses {Assert-AzuriteSharedSnapshot $null $spec $id Before} 'absent state evidence refused'
$after=New-AzuriteSharedSnapshot $spec $registry $id After
$same=Compare-AzuriteSharedSnapshots $before $after $spec $id
Assert ($same.accepted -and -not $same.registryChanged -and $same.userPreferencesUnchanged) 'exact shared state preserved'
$after.registry.values[1].data=B64 '1000';$after.registry.values[2].data=B64 '9876543210123456789'
$metadata=Compare-AzuriteSharedSnapshots $before $after $spec $id
Assert ($metadata.accepted -and $metadata.registryChanged -and $metadata.engineMetadata.Count -eq 2 -and $metadata.userPreferencesUnchanged) 'only exact binary ASCII-decimal session changes classified separately'
Assert ($metadata.engineMetadata[0].before -and $metadata.engineMetadata[0].after) 'raw typed metadata deltas retained privately'
$after.registry.values[1].data=[Convert]::ToBase64String([byte[]]@(232,3,0,0))
Assert (-not (Compare-AzuriteSharedSnapshots $before $after $spec $id).accepted) 'little-endian counter is not misclassified as decimal'
$after=New-AzuriteSharedSnapshot $spec $registry $id After;$after.registry.values[1].data=B64 '1002'
Assert (-not (Compare-AzuriteSharedSnapshots $before $after $spec $id).accepted) 'counter must increment exactly one'
$after=New-AzuriteSharedSnapshot $spec $registry $id After;$after.registry.values[2].kind='String';$after.registry.values[2].data=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('fixture'))
Assert (-not (Compare-AzuriteSharedSnapshots $before $after $spec $id).accepted) 'engine metadata type change refused'
$after=New-AzuriteSharedSnapshot $spec $registry $id After;$after.registry.values=@($after.registry.values[0],$after.registry.values[1])
Assert (-not (Compare-AzuriteSharedSnapshots $before $after $spec $id).accepted) 'registry deletion refused'
$after=New-AzuriteSharedSnapshot $spec $registry $id After;$after.registry.values+=@([pscustomobject]@{key='';name='unity.cloud_userid';kind='Binary';data=(B64 '11')})
Assert (-not (Compare-AzuriteSharedSnapshots $before $after $spec $id).accepted) 'no unity wildcard exception for added values'
[IO.File]::WriteAllText((Join-Path $stateRoot 'new.json'),'{}')
$after=New-AzuriteSharedSnapshot $spec $registry $id After
Assert (-not (Compare-AzuriteSharedSnapshots $before $after $spec $id).accepted) 'unexpected file addition refused'
Remove-Item (Join-Path $stateRoot 'new.json')
$bad=$before|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.files[0].sha256='0'*64
Refuses {Assert-AzuriteSharedSnapshot $bad $spec $id Before} 'malformed backup/hash evidence refused'
$bad=$before|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.roots[0].path=Join-Path $root 'different-root'
Refuses {Assert-AzuriteSharedSnapshot $bad $spec $id Before} 'snapshot cannot substitute a different protected root'
$bad=$before|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.registry.values[0].key='uncaptured-subkey'
Refuses {Assert-AzuriteSharedSnapshot $bad $spec $id Before} 'registry values cannot refer to an uncaptured key'
Write-Output 'PASS: bounded typed snapshots; exact ASCII/NUL metadata policy; unexpected file/registry/type/add/delete changes refused'

$lab=Join-Path $root 'lab';$install=Join-Path $lab 'install';[void][IO.Directory]::CreateDirectory($install)
foreach($d in @('BepInEx\plugins','profiles\AzuriteAcceptance\configs')){[void][IO.Directory]::CreateDirectory((Join-Path $install $d))}
[IO.File]::WriteAllText((Join-Path $install 'ActiveProfile.txt'),'AzuriteAcceptance')
[IO.File]::WriteAllText((Join-Path $install 'AzureArchive.exe'),'NOT EXECUTABLE: harmless pinned fixture')
[IO.File]::WriteAllText((Join-Path $install 'profiles\AzuriteAcceptance\modconfig.json'),'{"EnabledMods":[{"name":"Azurite","version":"1.0.38"}]}')
$pins=@();foreach($p in @('ActiveProfile.txt','AzureArchive.exe','profiles\AzuriteAcceptance\modconfig.json')){$pins+=@{path=$p;sha256=(Get-FileHash (Join-Path $install $p)).Hash}}
$contract=[pscustomobject]@{schema=1;labRoot=$lab;profile='AzuriteAcceptance';modName='Azurite';modVersion='1.0.38';files=$pins;exactDirectories=@(@{path='BepInEx/plugins';files=@()});evidence=@();acceptance='native CLI query only; audio/GPU/Editor unverified'}
@{schema=1;purpose='AzuriteHeadlessLab';labId=[guid]::NewGuid().ToString();controllerSid=$sid;dailyInstallPath=(Join-Path $root 'daily')}|ConvertTo-Json|Set-Content (Join-Path $lab 'lab.marker.json')
$context=[pscustomobject]@{sid=$sid;isAdministrator=$false;profile=$profile}
$planSnapshot=New-AzuriteSharedSnapshot $spec $registry 'plan' Plan
$treeBefore=@(Get-ChildItem $lab -Force -Recurse|ForEach-Object{$_.FullName}) -join '|'
$plan=Get-AzuriteCurrentPrerequisites $lab $contract $context $spec $planSnapshot
$treeAfter=@(Get-ChildItem $lab -Force -Recurse|ForEach-Object{$_.FullName}) -join '|'
Assert ($plan.Ready -and -not $plan.WillLaunch -and -not $plan.RequiresCredentials -and $treeBefore -ceq $treeAfter) 'current PlanOnly ready without manifests/accounts or writes'
Assert (-not(Test-Path (Join-Path $lab 'account.manifest.json'))) 'fixture has no dedicated account manifest'
$prepared=Join-Path $lab 'prepared.json';[IO.File]::WriteAllText($prepared,'{"argv":["capabilities"]}');$hash=Get-AzuriteSha256 $prepared
Assert-AzuritePreparedHash $prepared $hash
[IO.File]::WriteAllText($prepared,'{"argv":["schema"]}')
Refuses {Assert-AzuritePreparedHash $prepared $hash} 'same-user prepared request changes refused'
Write-Output 'PASS: ready no-write current-mode plan without credentials; same-user prepared hash drift refused'
$fixtureSource=Join-Path $PSScriptRoot 'CurrentAccount-FixtureBootstrap.ps1'
Assert (Test-Path $fixtureSource) 'same-account harmless bootstrap fixture is missing'
$owned=Join-Path $root 'owned-run';[void][IO.Directory]::CreateDirectory($owned)
Set-AzuriteRunPathAccess $root $owned $sid $sid CurrentAccount Read
$tooling=Join-Path $owned 'tooling';$output=Join-Path $owned 'output'
foreach($dir in @($tooling,$output)){[void][IO.Directory]::CreateDirectory($dir);Set-AzuriteRunPathAccess $root $dir $sid $sid CurrentAccount Modify}
$toolHashes=@{}
foreach($name in @('HeadlessCore.psm1','RestrictedCliCore.psm1','CurrentAccountCli.psm1','RestrictedCliNative.cs','Identity-Preflight.ps1')){$source=Join-Path $repo ('tools\headless\'+$name);$dest=Join-Path $tooling $name;Copy-Item $source $dest;Set-AzuriteRunPathAccess $root $dest $sid $sid CurrentAccount Read;$toolHashes[$name]=Get-AzuriteSha256 $dest}
$dest=Join-Path $tooling 'CurrentAccount-FixtureBootstrap.ps1';Copy-Item $fixtureSource $dest;Set-AzuriteRunPathAccess $root $dest $sid $sid CurrentAccount Read;$toolHashes['CurrentAccount-FixtureBootstrap.ps1']=Get-AzuriteSha256 $dest
$assembly=Join-Path $tooling 'RestrictedCliNative.dll';[void](New-AzuriteNetFrameworkHelper (Join-Path $tooling 'RestrictedCliNative.cs') $assembly);Set-AzuriteRunPathAccess $root $assembly $sid $sid CurrentAccount Read;$toolHashes['RestrictedCliNative.dll']=Get-AzuriteSha256 $assembly
$runId=[guid]::NewGuid().ToString('N');$actualProfile=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile);$actualLow=[Azurite.Restricted.KnownFolders]::LocalLow()
$envBaseline=[pscustomobject]@{localAppData=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData);environmentProfile=$env:USERPROFILE;environmentLocalAppData=$env:LOCALAPPDATA;environmentAppData=$env:APPDATA;environmentTemp=$env:TEMP;environmentTmp=$env:TMP}
$control=Join-Path $tooling 'fixture-control.json';Write-AzuriteFlushedJson $control @{runId=$runId;sid=$sid;profile=$actualProfile;localLow=$actualLow;environment=$envBaseline;hashes=$toolHashes;output=$output}
Set-AzuriteRunPathAccess $root $control $sid $sid CurrentAccount Read
$shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$psi=New-AzuriteProcessInfo -FilePath $shell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$dest,'-ControlPath',$control) -WorkingDirectory $owned
Assert ([string]::IsNullOrEmpty($psi.UserName) -and -not $psi.LoadUserProfile) 'current launch must not use alternate logon/profile loading'
$run=[Azurite.Restricted.ProcessPump]::RunBootstrap($psi,15000,5000,$runId,16384,@($output),1048576)
Write-AzuriteFlushedJson (Join-Path $owned 'controller-fixture.json') $run
Assert (-not (Get-AzuriteOuterProcessStatus $run).failed -and $run.CleanupVerified -and $run.JobDrained) 'same-account harmless bootstrap failed identity/ACL/drain'
$proof=Read-AzuriteBoundedJson (Join-Path $output 'current-result.json')
Assert ($proof.sid -ceq $sid -and $proof.identityMode -ceq 'CurrentAccount' -and $proof.hiveShared -and -not $proof.accountIsolation) 'real same-account child did not prove shared actual profile/HKCU'
foreach($name in $toolHashes.Keys){Assert-AzuritePreparedHash (Join-Path $tooling $name) $toolHashes[$name]}
Write-Output 'PASS: actual same-user PS5.1 harmless bootstrap, private/read-only tooling, unchanged inherited environment, correlated job drain'
$secret=Join-Path $owned 'secrets';[void][IO.Directory]::CreateDirectory($secret);Set-AzuriteLabAcl -LabRoot $owned -Path $secret -ControllerSid $sid
[void](Start-AzuriteCleanupGate $owned $runId $owned $sid)
$guardBefore=New-AzuriteSharedSnapshot $spec $registry $runId Before;$guardAfter=New-AzuriteSharedSnapshot $spec $registry $runId After
$guardAfter.registry.values[0].data=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('changed-user-preference'))
$guardDelta=Compare-AzuriteSharedSnapshots $guardBefore $guardAfter $spec $runId
Assert ([bool](Get-Command Complete-AzuriteCurrentCleanupGate -ErrorAction SilentlyContinue)) 'current-mode state preservation closure gate is missing'
Refuses {Complete-AzuriteCurrentCleanupGate $owned $runId $sid $run $guardDelta} 'unexpected user preference change must keep quarantine despite drained child'
Refuses {Assert-AzuriteCleanupGateClear $owned} 'shared-state drift quarantine was cleared'
$guardAfter=New-AzuriteSharedSnapshot $spec $registry $runId After;$guardAfter.registry.values[1].data=B64 '1000';$guardAfter.registry.values[2].data=B64 '9999999999999999999'
$guardDelta=Compare-AzuriteSharedSnapshots $guardBefore $guardAfter $spec $runId
Complete-AzuriteCurrentCleanupGate $owned $runId $sid $run $guardDelta
Assert-AzuriteCleanupGateClear $owned
Assert ($guardDelta.registryChanged -and -not $guardDelta.finalStateUnchanged -and -not $guardDelta.restorationPerformed) 'accepted engine metadata must remain explicitly changed, never restored'
Write-Output 'PASS: shared-state drift retains quarantine; exact metadata-only change closes preservation without restoring shared registry'
if($CheckProductionPlan){
 $realLab='E:\aamod\labs\Azurite-20261108-stable'
 $beforeFiles=@(Get-ChildItem -LiteralPath $realLab -Force -Recurse|ForEach-Object{$_.FullName}) -join '|'
 function global:Get-LocalUser {throw 'CURRENT MODE REACHED ACCOUNT HELPER'}
 try{$realPlan=& (Join-Path $repo 'tools\headless\Invoke-AzuriteRestrictedCli.ps1') -LabRoot $realLab -UseCurrentAccount -Command capabilities -PlanOnly}finally{Remove-Item Function:\Get-LocalUser}
 $afterFiles=@(Get-ChildItem -LiteralPath $realLab -Force -Recurse|ForEach-Object{$_.FullName}) -join '|'
 Assert ($beforeFiles -ceq $afterFiles) 'production current-account PlanOnly changed filesystem inventory'
 Assert (($realPlan.Failures -join '|') -notmatch 'account manifest|DPAPI|credential|ACCOUNT HELPER') 'current mode reached a superseded account/credential prerequisite'
 Assert ($realPlan.Ready -and -not $realPlan.WillLaunch -and -not $realPlan.RequiresCredentials) 'production current-account PlanOnly did not become ready'
 Write-Output 'PASS: real marked-lab current PlanOnly ready with no account helper, credential prerequisite or filesystem write'
}
Write-Output ('PASS current-account fixtures; '+$root)
