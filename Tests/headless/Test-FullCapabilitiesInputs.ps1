[CmdletBinding()]
param([string]$ResultPath)

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repo 'tools\headless\New-AzuriteFullCapabilitiesInputs.ps1')
$passed=[Collections.Generic.List[string]]::new()
function Check($Value,[string]$Name){if(-not $Value){throw "FAIL $Name"};$passed.Add($Name);Write-Output "PASS $Name"}
function Pin([string]$Path){$full=[IO.Path]::GetFullPath((Join-Path $repo $Path));[pscustomobject]@{path=$full;sha256=(Get-InputsHash ([IO.File]::ReadAllBytes($full)))}}

$realRecordPath=Join-Path $repo 'artifacts\aa-fr-inputs\real-baseline-awaiting-harmless.input.json'
$real=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes($realRecordPath))
$noHarmless=New-AzuriteFullCapabilitiesInputs $real (Join-Path $repo 'artifacts\aa-fr-inputs\never-created')
Check (-not $noHarmless.inputReady -and $noHarmless.runId -ceq '2fc0eb21404d4efaaf527c07c295d147' -and @($noHarmless.failures|Where-Object {$_ -like '*harmless*'}).Count -eq 1 -and -not(Test-Path (Join-Path $repo 'artifacts\aa-fr-inputs\never-created'))) 'real-baseline-awaits-harmless-with-no-output'
$recordMissing=& (Join-Path $PSHOME $(if($PSVersionTable.PSVersion.Major -le 5){'powershell.exe'}else{'pwsh.exe'})) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\headless\New-AzuriteFullCapabilitiesInputs.ps1') -InputRecordPath $realRecordPath -OutputDirectory (Join-Path $repo 'artifacts\aa-fr-inputs\never-created')
$missingResult=($recordMissing -join [char]10)|ConvertFrom-Json
Check ($LASTEXITCODE -eq 0 -and $missingResult.inputReady -eq $false -and $missingResult.failures[0] -like '*harmless*') 'public-real-record-stays-dry-with-missing-proof'

$dir=Join-Path $repo ('artifacts\aa-fr-inputs\simulated-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($dir)
function WriteFixture([string]$Name,$Object){
 $path=Join-Path $dir $Name;$bytes=ConvertTo-InputsBytes $Object
 $stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
 return [pscustomobject]@{path=$path;sha256=Get-InputsHash $bytes}
}
function CloneFixture($Value){return ConvertFrom-InputsJson (ConvertTo-InputsBytes $Value)}
$run=$real.runId;$sid='S-1-5-21-222280291-4039000738-1914192245-1001';$profile='C:\fixture-profile';$localLow=$profile+'\AppData\LocalLow'
$environment=[pscustomobject]@{localAppData=$profile+'\AppData\Local';environmentProfile=$profile;environmentLocalAppData=$profile+'\AppData\Local';environmentAppData=$profile+'\AppData\Roaming';environmentTemp=$profile+'\AppData\Local\Temp';environmentTmp=$profile+'\AppData\Local\Temp'}
$profileIdentity=[pscustomobject]@{identitySid=$sid;isAdministrator=$false;hiveLoaded=$true;userProfile=$profile;environmentProfile=$profile;localAppData=$environment.localAppData;localLow=$localLow;environmentLocalAppData=$environment.environmentLocalAppData;environmentAppData=$environment.environmentAppData;environmentTemp=$environment.environmentTemp;environmentTmp=$environment.environmentTmp}
$proofPaths=[pscustomobject]@{userProfile=$profile;environmentProfile=$profile;localAppData=$environment.localAppData;localLow=$localLow;environmentLocalAppData=$environment.environmentLocalAppData;environmentAppData=$environment.environmentAppData;environmentTemp=$environment.environmentTemp;environmentTmp=$environment.environmentTmp}
$profileProof=[pscustomobject]@{schema=1;identityMode='CurrentAccount';identitySid=$sid;filesystemVerified=$true;method='current-account-shared-profile-v1';checkedUtc='2026-11-12T02:31:56Z';paths=$proofPaths;profileShared=$true;hiveShared=$true;accountIsolation=$false}
$identity=[pscustomobject]@{status='passed';identitySid=$sid;isAdministrator=$false;hiveLoaded=$true;token=[pscustomobject]@{user_sid=$sid;logon_sid='S-1-5-5-1-2';is_restricted=$false;elevated=$false;is_app_container=$false;users_enabled=$true};unexpectedPrivileges=@();controllerProfile=$profile;currentLocalLow=$localLow;currentEnvironment=$environment;profileIdentity=$profileIdentity;profileProof=$profileProof}
$identityPin=WriteFixture 'fake-original-identity.json' $identity
$daily='F:\AzureArchive_101'
$roots=@(
 [pscustomobject]@{label='LocalLowAA';path=$localLow+'\foxxlight\AzureArchive';kind='Directory';required=$false},
 [pscustomobject]@{label='DailyActiveProfile';path=$daily+'\ActiveProfile.txt';kind='File';required=$true},
 [pscustomobject]@{label='DailySelectedProfile';path=$daily+'\profiles\Default';kind='Directory';required=$true},
 [pscustomobject]@{label='DailyDoorstopConfig';path=$daily+'\doorstop_config.ini';kind='File';required=$true},
 [pscustomobject]@{label='DailyBepInExConfig';path=$daily+'\BepInEx\config\BepInEx.cfg';kind='File';required=$true}
)
$scope=[pscustomobject]@{schema=1;identitySid=$sid;registryPath='Software\foxxlight\AzureArchive';localLow=$localLow;dailyProfileName='Default';roots=$roots}
$scopePin=WriteFixture 'fake-shared-scope.json' $scope
Import-Module (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $repo 'tools\headless\CurrentAccountCli.psm1') -Force -DisableNameChecking
$module=Get-Module CurrentAccountCli;$specHash=& $module {param($s)Get-AzuriteStateSpecHash $s} $scope
$files=@(foreach($item in @(@('DailyActiveProfile/','Default'),@('DailyDoorstopConfig/','fixture-doorstop'),@('DailyBepInExConfig/','fixture-config'))){$bytes=[Text.UTF8Encoding]::new($false).GetBytes($item[1]);[pscustomobject]@{id=$item[0];bytes=$bytes.Length;sha256=Get-InputsHash $bytes;data=[Convert]::ToBase64String($bytes);attributes=32;lastWriteUtc='2026-11-12T02:30:00Z'}})
$before=[pscustomobject]@{schema=1;runId=$run;phase='Before';identitySid=$sid;specSha256=$specHash;capturedUtc='2026-11-12T02:31:56Z';roots=@([pscustomobject]@{label='LocalLowAA';path=$roots[0].path;kind='Directory';exists=$false},[pscustomobject]@{label='DailyActiveProfile';path=$roots[1].path;kind='File';exists=$true},[pscustomobject]@{label='DailySelectedProfile';path=$roots[2].path;kind='Directory';exists=$true},[pscustomobject]@{label='DailyDoorstopConfig';path=$roots[3].path;kind='File';exists=$true},[pscustomobject]@{label='DailyBepInExConfig';path=$roots[4].path;kind='File';exists=$true});directories=@('DailySelectedProfile/');files=$files;registry=[pscustomobject]@{exists=$false;keys=@();values=@()}}
$beforePin=WriteFixture 'fake-shared-before.json' $before
$harmless=[pscustomobject]@{schema=1;scope='actual-native-full-restricted-harmless-lifecycle';passed=$true;productionTargetExecuted=$false;desktopLeaseAcrossBootstrapExit=$true;actualSuspendedTokenAndJob=$true;pipeHandleListAndEOF=$true;cleanupVerified=$true;nativeSourceSha256=(Pin 'tools/headless/RestrictedCliFullRestricted.cs').sha256;sharedNativeSourceSha256=(Pin 'tools/headless/RestrictedCliNative.cs').sha256;testOnlySimulated=$true}
$harmlessPin=WriteFixture 'FAKE-harmless-not-approval.json' $harmless
$historicalWarm=Get-Content (Join-Path $repo 'artifacts\aa-fr-cache-prerequisites\warm-stamp-result.json') -Raw | ConvertFrom-Json
$runDirectory='F:\AzuriteLabs\Editor-20261108-normal\runs\restricted-'+$run
$preflight=[pscustomobject][ordered]@{schema=1;scope='fresh-full-capabilities-pretransition-observation';evidenceClass='simulated-fixture';runId=$run;labRoot='F:\AzuriteLabs\Editor-20261108-normal';installRoot='F:\AzuriteLabs\Editor-20261108-normal\install';observedUtc='2026-11-12T02:31:56Z';stageManifestSha256='0bddb4285f5cee0c79c3f2ab6f6ab8c346937ae8ff12b4f49f29a6f102bd33dd';cacheContractSha256='11476211f744e7009fe1bd21b7bc4b786d3f73c89a747f6ecab955e43754c5f4';historicalWarmResultSha256='ae20a85d28f254d14851deeb9d4ff0627bb7d821bd04154814c8fdfcca830fb6';actualEnvironment=[pscustomobject]@{BEPINEX_GAME_ASSEMBLY_PATH=[pscustomobject]@{present=$false;value=$null}};actualArgv=@('-batchmode','-nographics','-disableaudio','-logFile',($runDirectory+'\output\unity.log'));sidecar=[pscustomobject]@{environmentKey='AZUREARCHIVE_CLI_DIRECTORY';directory=$runDirectory+'\request';command='capabilities';cwd=$runDirectory+'\workspace'};warmInterop=[pscustomobject]@{computedStamp=$historicalWarm.computed_stamp;generatorVersion=$historicalWarm.generator_version;cpp2ilVersion=$historicalWarm.cpp2il_version;optionalRenameMapPresent=$false;orderedEvents=$historicalWarm.input_events;inputFiles=@($historicalWarm.input_files|ForEach-Object{[pscustomobject][ordered]@{relative_path=$_.relative_path;bytes=[long]$_.bytes;sha256=$_.sha256}})}}
$preflightPin=WriteFixture 'FAKE-fresh-preflight-not-production.json' $preflight
$native='artifacts\aa-fr-capabilities-preparation\bridge-fix1-20261111\freeze\tools\headless\RestrictedCliNative.dll'
$helper='artifacts\aa-fr-transition-preparation\integration-20261110-01\helper-build1\FullRestrictedStageFileSecurity.dll'
$build='artifacts\aa-fr-transition-preparation\integration-20261110-01\helper-build1\build.json'
$helperSource='artifacts\aa-fr-transition-preparation\integration-20261110-01\helper-build1\FullRestrictedStageFileSecurity.cs'
$tooling='artifacts\aa-fr-capabilities-preparation\bridge-fix1-20261111\freeze\tooling-manifest.json'
$sources=@(
 foreach($name in @('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1','FullRestrictedCapabilitiesPlan.psm1')){$pin=Pin ('tools\headless\'+$name);[pscustomobject]@{name=$name;path=$pin.path;sha256=$pin.sha256}}
 foreach($pair in @(@('RestrictedCliNative.dll',$native),@('FullRestrictedStageFileSecurity.dll',$helper),@('helper-build.json',$build),@('tooling-manifest.json',$tooling))){$pin=Pin $pair[1];[pscustomobject]@{name=$pair[0];path=$pin.path;sha256=$pin.sha256}}
)
$record=[pscustomobject]@{schema=1;scope='fixed-F-capabilities-preapproval-inputs';evidenceClass='simulated-fixture';runId=$run;baseline=$real.baseline;harmless=$harmlessPin;preflight=$preflightPin;shared=[pscustomobject]@{scope=$scopePin;before=$beforePin};originalIdentity=$identityPin;helperSource=(Pin $helperSource);sources=$sources;intentValidUntilUtc='2026-11-12T23:00:00Z';runtimeTimeoutSeconds=120}
$recordPin=WriteFixture 'simulated-input-record.json' $record
function Test-ReboundScope([string]$Suffix,[int]$RootIndex,[string]$Path,[string]$Needle){
 $changedScope=CloneFixture $scope;$changedBefore=CloneFixture $before
 $changedScope.roots[$RootIndex].path=$Path;$changedBefore.roots[$RootIndex].path=$Path
 $changedBefore.specSha256=& (Get-Module CurrentAccountCli) {param($s)Get-AzuriteStateSpecHash $s} $changedScope
 $scopeRef=WriteFixture ('scope-'+$Suffix+'.json') $changedScope
 $beforeRef=WriteFixture ('before-'+$Suffix+'.json') $changedBefore
 $changedRecord=CloneFixture $record;$changedRecord.shared.scope=$scopeRef;$changedRecord.shared.before=$beforeRef
 $checkResult=New-AzuriteFullCapabilitiesInputs $changedRecord (Join-Path $dir ('never-'+$Suffix))
 Check (-not $checkResult.inputReady -and @($checkResult.failures|Where-Object {$_ -like ('*'+$Needle+'*')}).Count -eq 1 -and -not(Test-Path (Join-Path $dir ('never-'+$Suffix)))) ('rehashed-wrong-shared-scope-'+$Suffix)
}
Test-ReboundScope 'wrong-bep' 4 'F:\AzureArchive_101\unrelated.txt' 'Shared exact label/path/kind/required differs: DailyBepInExConfig'
Test-ReboundScope 'wrong-profile' 2 'F:\AzureArchive_101\profiles\OtherProfile' 'Shared exact label/path/kind/required differs: DailySelectedProfile'
$badDescendant=CloneFixture $before;$badDescendant.files[2].id='DailyBepInExConfig/evil.txt'
$descendantPin=WriteFixture 'before-wrong-descendant.json' $badDescendant
$descendantRecord=CloneFixture $record;$descendantRecord.shared.before=$descendantPin
$descendantResult=New-AzuriteFullCapabilitiesInputs $descendantRecord (Join-Path $dir 'never-wrong-descendant')
Check (-not $descendantResult.inputReady -and @($descendantResult.failures|Where-Object {$_ -like '*Typed shared file ID/size/hash escapes its exact root*'}).Count -eq 1) 'typed-descendant-file-id-refused'
function Test-ReboundPreflight([string]$Suffix,[scriptblock]$Mutation,[string]$Needle){
 $changed=CloneFixture $preflight
 [void](& $Mutation $changed)
 $pin=WriteFixture ('preflight-'+$Suffix+'.json') $changed
 $changedRecord=CloneFixture $record;$changedRecord.preflight=$pin
 $checkResult=New-AzuriteFullCapabilitiesInputs $changedRecord (Join-Path $dir ('never-preflight-'+$Suffix))
 Check (-not $checkResult.inputReady -and @($checkResult.failures|Where-Object {$_ -like ('*'+$Needle+'*')}).Count -eq 1) ('rehashed-fresh-preflight-'+$Suffix)
}
Test-ReboundPreflight 'override' {param($x)$x.actualEnvironment.BEPINEX_GAME_ASSEMBLY_PATH.present=$true;$x.actualEnvironment.BEPINEX_GAME_ASSEMBLY_PATH.value='F:\unbound\GameAssembly.dll'} 'Actual BEPINEX_GAME_ASSEMBLY_PATH presence/value differs'
Test-ReboundPreflight 'unhollowed' {param($x)$x.actualArgv=@($x.actualArgv)+@('--unhollowed-path')} 'Actual selected capabilities argv/log path differs'
Test-ReboundPreflight 'warm-order' {param($x)$first=$x.warmInterop.orderedEvents[0];$x.warmInterop.orderedEvents[0]=$x.warmInterop.orderedEvents[1];$x.warmInterop.orderedEvents[1]=$first} 'Fresh ordered interop input event sequence differs'
Test-ReboundPreflight 'root' {param($x)$x.installRoot='F:\other\install'} 'Fresh preflight target/run/source pins differ'
Test-ReboundPreflight 'time' {param($x)$x.observedUtc='2026-11-12T02:31:55Z'} 'Fresh preflight observation precedes completed baseline'
$badChronology=CloneFixture $before;$badChronology.capturedUtc='2026-11-12T02:31:57Z'
$badChronologyPin=WriteFixture 'before-after-preflight.json' $badChronology
$badChronologyRecord=CloneFixture $record;$badChronologyRecord.shared.before=$badChronologyPin
$chronologyResult=New-AzuriteFullCapabilitiesInputs $badChronologyRecord (Join-Path $dir 'never-chronology')
Check (-not $chronologyResult.inputReady -and @($chronologyResult.failures|Where-Object {$_ -like '*Typed shared before snapshot is outside baseline/fresh preflight chronology*'}).Count -eq 1) 'typed-shared-before-after-fresh-preflight-refused'
$unknownRecord=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$unknownRecord|Add-Member -NotePropertyName approvalSha256 -NotePropertyValue ('a'*64)
$unknownPin=WriteFixture 'unknown-input-record.json' $unknownRecord
$unknownResult=& (Join-Path $PSHOME $(if($PSVersionTable.PSVersion.Major -le 5){'powershell.exe'}else{'pwsh.exe'})) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\headless\New-AzuriteFullCapabilitiesInputs.ps1') -InputRecordPath $unknownPin.path -OutputDirectory (Join-Path $dir 'unknown-never-output')
$parsedUnknown=($unknownResult -join [char]10)|ConvertFrom-Json
Check ($LASTEXITCODE -eq 0 -and -not $parsedUnknown.inputReady -and $parsedUnknown.failures[0] -like '*exact shape differs*' -and -not(Test-Path (Join-Path $dir 'unknown-never-output'))) 'public-unknown-record-refused-before-output'
$oversizeRecord=Join-Path $dir 'oversized-input-record.bin';$stream=[IO.FileStream]::new($oversizeRecord,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$stream.SetLength(131073)}finally{$stream.Dispose()}
$oldErrorPreference=$ErrorActionPreference;$ErrorActionPreference='Continue'
try{$oversizeOutput=& (Join-Path $PSHOME $(if($PSVersionTable.PSVersion.Major -le 5){'powershell.exe'}else{'pwsh.exe'})) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\headless\New-AzuriteFullCapabilitiesInputs.ps1') -InputRecordPath $oversizeRecord -OutputDirectory (Join-Path $dir 'oversize-never-output') 2>&1;$oversizeExit=$LASTEXITCODE}finally{$ErrorActionPreference=$oldErrorPreference}
Check ($oversizeExit -ne 0 -and (($oversizeOutput -join [char]10) -like '*exceeds 128 KiB*') -and -not(Test-Path (Join-Path $dir 'oversize-never-output'))) 'public-oversized-record-refused-before-read'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.evidenceClass='reviewed-real';$wrong.runId='11111111111111111111111111111111'
$relabel=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output')
Check (-not $relabel.inputReady -and @($relabel.failures|Where-Object {$_ -like '*exact-run baseline*'}).Count -eq 1) 'real-baseline-relabel-refused'
$out=Join-Path $dir 'dry-output'
$validated=New-AzuriteFullCapabilitiesInputs $record $out
if(-not $validated.inputReady){Write-Output ('VALIDATION_FAILURES '+($validated.failures -join ' | '))}
Check ($validated.inputReady -and $validated.evidenceClass -ceq 'simulated-fixture' -and $validated.productionEvidence -eq $false -and $validated.approvalPresent -eq $false) 'simulated-complete-inputs-remain-unapproved'
$intent=New-AzuriteFullCapabilitiesIntent $validated $out $record.runtimeTimeoutSeconds $record.intentValidUntilUtc
Check ($intent.inputReady -and $intent.planSha256 -cmatch '^[a-f0-9]{64}$' -and $intent.sourceManifestSha256 -cmatch '^[a-f0-9]{64}$' -and $intent.gateSha256 -cmatch '^[a-f0-9]{64}$' -and -not $intent.willApply -and -not $intent.willLaunch -and -not $intent.runtimeAllowed) 'forward-intent-is-dry'
$written=Write-AzuriteFullCapabilitiesInputs $intent $out
Check ($written.inputReady -and (Test-Path (Join-Path $out 'gate.json')) -and (Test-Path (Join-Path $out 'source-manifest.json')) -and -not (Test-Path (Join-Path $out 'approval.json'))) 'dry-output-contains-no-approval'
$beforeLinkOutput=Join-Path $dir 'reparse-parent-never-output'
$script:fakeOutputLinkParent=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts\aa-fr-inputs'))
function Get-Item {
 param([string]$LiteralPath,[switch]$Force,$ErrorAction)
 if([IO.Path]::GetFullPath($LiteralPath) -ceq $script:fakeOutputLinkParent){return [pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint;PSIsContainer=$true}}
 return Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force -ErrorAction SilentlyContinue
}
$linkError=$null
try{$null=Write-AzuriteFullCapabilitiesInputs $intent $beforeLinkOutput}catch{$linkError=$_.Exception.Message}finally{Remove-Item Function:\Get-Item -ErrorAction SilentlyContinue}
Check ($linkError -like '*Dry output ancestor is linked*' -and -not(Test-Path $beforeLinkOutput)) 'output-reparse-ancestor-refused-before-create'
$afterCreateOutput=Join-Path $dir 'reparse-after-create-output'
$script:fakeAfterCreatePath=[IO.Path]::GetFullPath($afterCreateOutput)
function Get-Item {
 param([string]$LiteralPath,[switch]$Force,$ErrorAction)
 if([IO.Path]::GetFullPath($LiteralPath) -ceq $script:fakeAfterCreatePath -and [IO.Directory]::Exists($script:fakeAfterCreatePath)){return [pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint;PSIsContainer=$true}}
 return Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force -ErrorAction SilentlyContinue
}
$afterError=$null
try{$null=Write-AzuriteFullCapabilitiesInputs $intent $afterCreateOutput}catch{$afterError=$_.Exception.Message}finally{Remove-Item Function:\Get-Item -ErrorAction SilentlyContinue}
Check ($afterError -like '*Dry output ancestor is linked*' -and (Test-Path $afterCreateOutput) -and @(Get-ChildItem -LiteralPath $afterCreateOutput -Force).Count -eq 0) 'output-reparse-after-create-refused-before-child-write'
$manifest=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes((Join-Path $out 'source-manifest.json')))
$control=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes((Join-Path $out 'control.json')))
$gate=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes((Join-Path $out 'gate.json')))
Check ($manifest.files.Count -eq 18 -and $gate.controlSha256 -ceq (Get-InputsHash ([IO.File]::ReadAllBytes((Join-Path $out 'control.json')))) -and $gate.transitionSourceManifestSha256 -ceq (Get-InputsHash ([IO.File]::ReadAllBytes((Join-Path $out 'source-manifest.json')))) -and $control.toolingManifestSha256 -ceq (Pin $tooling).sha256) 'exact-eighteen-source-forward-dag'
Check (-not $control.PSObject.Properties['gateSha256'] -and -not $gate.PSObject.Properties['reviewApprovalSha256'] -and $gate.harmlessLifecycleReceiptSha256 -ceq $harmlessPin.sha256) 'no-reverse-approval-or-receipt-cycle'
Check ($intent.files['expected-acl-manifest.json'].Length -lt 1048576 -and (ConvertFrom-InputsJson $intent.files['expected-acl-manifest.json']).objects.Count -gt 1200) 'finite-exact-acl-intent'
$changedBaseline=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes($real.baseline.identity.path))
$groupPath='F:\AzuriteLabs\Editor-20261108-normal\install\AzureArchive.exe'
$groupEntry=@($changedBaseline.objects|Where-Object {$_.path -ceq $groupPath})[0]
$groupEntry.sddl=[Security.AccessControl.RawSecurityDescriptor]::new($groupEntry.sddl.Replace('G:'+$sid+'D:','G:S-1-5-32-545D:')).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access')
Check (([Security.AccessControl.RawSecurityDescriptor]::new($groupEntry.sddl).Group.Value) -ceq 'S-1-5-32-545') 'simulated-existing-group-fixture-is-valid'
$changedBaselinePin=WriteFixture 'fake-existing-group-baseline.json' $changedBaseline
$changedCapture=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes($real.baseline.capture.path));$changedCapture.baselineSha256=$changedBaselinePin.sha256
$changedCapturePin=WriteFixture 'fake-existing-group-capture.json' $changedCapture
$groupRecord=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$groupRecord.baseline.identity=$changedBaselinePin;$groupRecord.baseline.capture=$changedCapturePin
$groupValidated=New-AzuriteFullCapabilitiesInputs $groupRecord (Join-Path $dir 'group-output')
if(-not $groupValidated.inputReady){Write-Output ('GROUP_FAILURES '+($groupValidated.failures -join ' | '))}
Check ($groupValidated.inputReady -and $groupValidated.productionEvidence -eq $false) 'simulated-existing-group-baseline-validates'
$groupIntent=New-AzuriteFullCapabilitiesIntent $groupValidated (Join-Path $dir 'group-output') $groupRecord.runtimeTimeoutSeconds $groupRecord.intentValidUntilUtc
$groupAcl=ConvertFrom-InputsJson $groupIntent.files['expected-acl-manifest.json']
$groupAclEntry=@($groupAcl.objects|Where-Object {$_.path -ceq $groupPath})[0]
Check (([Security.AccessControl.RawSecurityDescriptor]::new($groupAclEntry.sddl).Group.Value) -ceq 'S-1-5-32-545' -and $groupAclEntry.role -ceq 'install-file') 'existing-group-preserved-in-exact-acl-intent'
$writer=Join-Path $repo 'tools\headless\Prepare-AzuriteFullCapabilitiesRun.ps1'
$hostExecutable=if($PSVersionTable.PSVersion.Major -le 5){Join-Path $PSHOME 'powershell.exe'}else{Join-Path $PSHOME 'pwsh.exe'}
$writerOutput=@(& $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $writer -SourceManifestPath (Join-Path $out 'source-manifest.json') -ExpectedSourceManifestSha256 $intent.sourceManifestSha256 -ControlPath (Join-Path $out 'control.json') -ExpectedControlSha256 $intent.controlSha256 -GatePath (Join-Path $out 'gate.json') -ExpectedGateSha256 $intent.gateSha256 -ExpectedPlanSha256 $intent.planSha256)
$writerExit=$LASTEXITCODE
$writerPlan=$writerOutput -join [char]10 | ConvertFrom-Json
Check ($writerExit -eq 0 -and $writerPlan.mode -ceq 'planning-only' -and $writerPlan.runId -ceq $run -and $writerPlan.existingObjects -eq 1228 -and $writerPlan.aclTargets -eq 1252 -and $writerPlan.approvalPresent -eq $false -and -not $writerPlan.willMutate -and -not $writerPlan.willLaunch -and -not $writerPlan.runtimeAllowed) 'accepted-writer-public-plan-consumes-assembled-bytes'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.harmless.sha256=('0'*64);$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output');Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*Expected C SHA-256*'}).Count -eq 1) 'missing-reviewed-harmless-pin-refused'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.baseline.identity.sha256=('a'*64);$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output');Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*Pinned C input hash*'}).Count -eq 1) 'baseline-hash-drift-refused'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.runId=('a'*32);$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output');Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*Completed baseline*'}).Count -eq 1) 'baseline-run-replay-refused'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.harmless.path='F:\must-never-read.json';$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output');Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*C workspace*'}).Count -eq 1) 'F-input-refused-before-read'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.sources[0].sha256=('a'*64);$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output');Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*Pinned C input hash*'}).Count -eq 1) 'tool-source-drift-refused'
$copyCore=Join-Path $dir 'reviewed-core-copy.psm1';Copy-Item -LiteralPath (Join-Path $repo 'tools\headless\RestrictedCliCore.psm1') -Destination $copyCore
$wrong=CloneFixture $record;(@($wrong.sources|Where-Object {$_.name -ceq 'RestrictedCliCore.psm1'})[0]).path=$copyCore
$script:unexpectedImports=0
function Import-Module {param($Name,$Force,$DisableNameChecking)$script:unexpectedImports++;throw 'Unexpected module import before reviewed path check'}
try{$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-import-output')}finally{Remove-Item Function:\Import-Module -ErrorAction SilentlyContinue}
Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*Executing module path differs from reviewed C pin before import: RestrictedCliCore.psm1*'}).Count -eq 1 -and $script:unexpectedImports -eq 0) 'executing-module-copy-refused-before-any-import'
$wrongTooling=CloneFixture (ConvertFrom-InputsJson ([IO.File]::ReadAllBytes((Pin $tooling).path)))
$wrongTooling.build.canonicalBuildResultSha256=('a'*64)
$wrongToolingPin=WriteFixture 'wrong-nested-build-result-pin.json' $wrongTooling
$wrong=CloneFixture $record;$toolEntry=@($wrong.sources|Where-Object {$_.name -ceq 'tooling-manifest.json'})[0];$toolEntry.path=$wrongToolingPin.path;$toolEntry.sha256=$wrongToolingPin.sha256
$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-nested-build-output')
Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*Pinned C input hash/length differs*'}).Count -eq 1) 'nested-build-result-bounded-pinned-read-refuses-drift'
$wrong=ConvertFrom-InputsJson (ConvertTo-InputsBytes $record);$wrong.evidenceClass='reviewed-real';$r=New-AzuriteFullCapabilitiesInputs $wrong (Join-Path $dir 'never-output');Check (-not $r.inputReady -and @($r.failures|Where-Object {$_ -like '*simulated*'}).Count -eq 1) 'simulated-harmless-cannot-be-promoted-real'
$result=[pscustomobject]@{schema=1;scope='C-only simulated source-linked assembler tests';passed=$passed.Count;failed=0;powerShell=$PSVersionTable.PSVersion.ToString();fixtureDirectory=$dir;planSha256=$intent.planSha256;gateSha256=$intent.gateSha256;sourceManifestSha256=$intent.sourceManifestSha256;approvalPresent=$false;willApply=$false;willLaunch=$false;runtimeAllowed=$false}
if($ResultPath){[IO.File]::WriteAllText([IO.Path]::GetFullPath($ResultPath),($result|ConvertTo-Json -Depth 5 -Compress),[Text.UTF8Encoding]::new($false))}
$result|ConvertTo-Json -Depth 5 -Compress
