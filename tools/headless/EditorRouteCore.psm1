Set-StrictMode -Version Latest
foreach($name in @('HeadlessCore.psm1','RestrictedCliCore.psm1','CurrentAccountCli.psm1')){Import-Module (Join-Path $PSScriptRoot $name) -Global -DisableNameChecking}
$script:EditorLab='E:\aamod\labs\Azurite-20261108-editor'
$script:EditorAcceptance='CLI-active native route selftest only; normal Editor/Graphical/PCM acceptance NOT proved'

function Get-AzuriteEditorRouteSpec([string]$RunId){
 if($RunId -cnotmatch '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$' -or ([guid]$RunId).ToString('D') -cne $RunId){throw 'A fresh canonical GUID-D run identity is required.'}
 $root=Join-Path $script:EditorLab ('runs\'+$RunId)
 return [pscustomobject]@{runId=$RunId;jobId=([guid]$RunId).ToString('N');labRoot=$script:EditorLab;runRoot=$root;installRoot=(Join-Path $script:EditorLab 'install');manifestPath=(Join-Path $root 'manifest.json');requestRoot=(Join-Path $root 'request');outputRoot=(Join-Path $root 'output');settingsPath=(Join-Path $root 'settings\user_settings.json');controlSeedPath=(Join-Path $root 'settings\miss_control.json');argv=@('capabilities');nativeArguments=@('-batchmode','-nographics','-disableaudio','-logFile',(Join-Path $root 'output\unity.log'));acceptance=$script:EditorAcceptance}
}
function New-AzuriteEditorRouteSeed($Spec,[ValidateSet('lab-read','miss-control')][string]$Marker){
 return [ordered]@{stEnabled=$false;catalogUrlOverride='';masterVolume=1;voiceVolume=1;bgmVolume=1;soundEffectVolume=1;lastProfileIndex=0;username=($Marker+'-'+$Spec.runId);goldText='0';diamondText='0';staminaText='0';autoSave=$false;autoCompile=$false;lobbyBgmEnabled=$false;autoDelay=0;fpsTier=0;checkForUpdate=$false;isAutoPlay=$false;fontIndex=0;visitedFiles=@();favoriteBgmIds=@();favoriteSoundNames=@();completeResStructVer=0;customSource='';cachePath=(Join-Path $Spec.runRoot 'cache');workspacePath=(Join-Path $Spec.runRoot 'workspace')}
}
function New-AzuriteEditorRouteManifest($Spec,[string]$SeedSha256,[string]$ControlSha256){
 foreach($hash in @($SeedSha256,$ControlSha256)){if($hash -cnotmatch '^[A-F0-9]{64}$'){throw 'Manifest hashes must identify the two complete native seeds.'}}
 return [ordered]@{Version=1;RunId=$Spec.runId;Mode='route-selftest';LabRoot=$Spec.labRoot;RunRoot=$Spec.runRoot;InstallRoot=$Spec.installRoot;SeedSha256=$SeedSha256;ControlSha256=$ControlSha256}
}
function Assert-AzuriteEditorRouteValue($Actual,$Expected,[string]$Label){
 if($Expected -is [bool] -and $Actual -isnot [bool]){throw ('Boolean evidence type differs: '+$Label)}
 if(($Expected -is [int] -or $Expected -is [long]) -and ($Actual -isnot [int] -and $Actual -isnot [long])){throw ('Integer evidence type differs: '+$Label)}
 if($Actual -cne $Expected){throw ('Evidence mismatch: '+$Label)}
}
function Assert-AzuriteEditorRouteStream([object[]]$Events,$Spec,[string]$ManifestSha256,[int]$NativePid,$StartedUtc,$CompletedUtc,[string[]]$Kinds){
 if($Events.Count -lt 1 -or $Events.Count -gt 128 -or $NativePid -le 0){throw 'Missing or excessive correlated evidence stream.'}
 $start=ConvertTo-AzuriteUtcInstant $StartedUtc;$end=ConvertTo-AzuriteUtcInstant $CompletedUtc
 $sequence=0L;$lastTick=-1L;$lastTime=$start
 foreach($event in $Events){
  if(@($event.PSObject.Properties).Count -ne 8){throw 'Evidence envelope differs from the exact frozen eight-field schema.'}
  foreach($field in @('sequence','utc','monotonicTicks','runId','pid','manifestSha256','kind','data')){if($field -cnotin @($event.PSObject.Properties.Name)){throw ('Evidence field missing or wrong case: '+$field)}}
  Assert-AzuriteEditorRouteValue $event.sequence (++$sequence) 'sequence'
  Assert-AzuriteEditorRouteValue $event.pid $NativePid 'native PID'
  Assert-AzuriteEditorRouteValue $event.runId $Spec.runId 'run ID'
  if($event.manifestSha256 -isnot [string] -or -not $event.manifestSha256.Equals($ManifestSha256,[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest identity differs across evidence.'}
  if($event.monotonicTicks -isnot [int] -and $event.monotonicTicks -isnot [long]){throw 'Invalid monotonic timestamp type.'}
  $time=ConvertTo-AzuriteUtcInstant $event.utc
  if($time -lt $start -or $time -gt $end -or $time -lt $lastTime -or $event.monotonicTicks -le $lastTick){throw 'Evidence timestamps are stale or reordered.'}
  if($event.kind -cnotin $Kinds){throw 'Failure or unknown event cannot be silently ignored.'}
  $lastTime=$time;$lastTick=$event.monotonicTicks
 }
}
function Get-AzuriteEditorRouteOne([object[]]$Events,[string]$Kind){$matches=@($Events|Where-Object {$_.kind -ceq $Kind});if($matches.Count -ne 1){throw ('Missing or duplicate proof: '+$Kind)};return $matches[0]}
function Assert-AzuriteEditorRouteEvidence {
 param([object[]]$Probe,[object[]]$Guardian,$Spec,[string]$ManifestSha256,[int]$NativePid,$StartedUtc,$CompletedUtc,[string]$SavedSha256)
 $probeKinds=@('install-enter','native-binding','hooks-registered','cli-awake-enter','native-read-dispatch','native-read-prefix','native-original-read-completed','native-getter-routed','native-readonly-getter-returned','native-discovery-getter-returned','addressables-reseeded-after-native-clear','native-addressables-property-returned','native-write-admitted','native-original-write-returned','route-selftest-complete')
 $guardianKinds=@('guardian-armed-before-chainloader','all-routing-hooks-registered','chainloader-return-admitted')
 Assert-AzuriteEditorRouteStream $Probe $Spec $ManifestSha256 $NativePid $StartedUtc $CompletedUtc $probeKinds
 Assert-AzuriteEditorRouteStream $Guardian $Spec $ManifestSha256 $NativePid $StartedUtc $CompletedUtc $guardianKinds
 $armed=Get-AzuriteEditorRouteOne $Guardian 'guardian-armed-before-chainloader';$registered=Get-AzuriteEditorRouteOne $Guardian 'all-routing-hooks-registered';$admitted=Get-AzuriteEditorRouteOne $Guardian 'chainloader-return-admitted'
 $install=Get-AzuriteEditorRouteOne $Probe 'install-enter';$hooks=Get-AzuriteEditorRouteOne $Probe 'hooks-registered';$awake=Get-AzuriteEditorRouteOne $Probe 'cli-awake-enter'
 foreach($item in @($armed,$install)){Assert-AzuriteEditorRouteValue $item.data.RunId $Spec.runId 'initial run ID';Assert-AzuriteEditorRouteValue $item.data.Mode 'route-selftest' 'mode'}
 foreach($item in @($armed,$registered,$admitted,$install)){if(-not $item.data.manifestSha256.Equals($ManifestSha256,[StringComparison]::OrdinalIgnoreCase)){throw 'Handshake manifest hash mismatch.'}}
 $routes=@('awake','read','write','fallback','cache','workspace','resources','discovery','offline','addressSet','addressClear','addressEvaluate')
 if((@($registered.data.routes|Sort-Object) -join '|') -cne (@($routes|Sort-Object) -join '|')){throw 'The guardian did not acknowledge all exact routes.'}
 Assert-AzuriteEditorRouteValue $registered.data.nativeHookHitsProved $false 'registration does not prove native hit'
 Assert-AzuriteEditorRouteValue $hooks.data.count 12 'route registration count'
 Assert-AzuriteEditorRouteValue $hooks.data.nativeInterceptionProved $false 'registration proof scope'
 Assert-AzuriteEditorRouteValue $admitted.data.nativeStartupMayResume $true 'guardian continuation'
 Assert-AzuriteEditorRouteValue $awake.data.cliActive $true 'real CLI active'
 Assert-AzuriteEditorRouteValue $awake.data.nativeAwakeWillRun $true 'original native Awake'
 if(-not ($armed.monotonicTicks -lt $install.monotonicTicks -and $install.monotonicTicks -lt $registered.monotonicTicks -and $registered.monotonicTicks -lt $hooks.monotonicTicks -and $hooks.monotonicTicks -lt $admitted.monotonicTicks -and $admitted.monotonicTicks -lt $awake.monotonicTicks)){throw 'Guardian/installation/first-Awake order mismatch.'}
 $bindings=@($Probe|Where-Object kind -ceq 'native-binding')
 $methods=@(@('awake',100689258,0x7f5710),@('read',100689386,0x7f24e0),@('save',100689392,0x7f2610),@('write',100689268,0x7f6640),@('fallback',100689266,0x7f5dd0),@('cache',100689255,0x7f6a20),@('workspace',100689256,0x7f6c70),@('resources',100688737,0x7bffd0),@('discovery',100694828,0x90a330),@('offline',100695181,0x93c5b0),@('addressSet',100663883,0x2f6f900),@('addressClear',100663884,0x2f6edb0),@('addressEvaluate',100663885,0x2f6ee30))
 if($bindings.Count -ne 13){throw 'Native binding evidence incomplete.'}
 foreach($method in $methods){$entry=@($bindings|Where-Object {$_.data.Id -ceq $method[0]});if($entry.Count -ne 1){throw 'Missing or duplicate native entry binding.'};Assert-AzuriteEditorRouteValue $entry[0].data.Token $method[1] 'native token';Assert-AzuriteEditorRouteValue $entry[0].data.Rva $method[2] 'native RVA';if($entry[0].data.methodInfo -le 0 -or $entry[0].data.entry -le 0 -or $entry[0].monotonicTicks -ge $registered.monotonicTicks){throw 'Invalid native entry binding or registration order.'}}
 $ordered=@('cli-awake-enter','native-read-dispatch','native-read-prefix','native-original-read-completed','native-discovery-getter-returned','addressables-reseeded-after-native-clear','native-addressables-property-returned','native-write-admitted','native-original-write-returned','route-selftest-complete')
 $last=-1L;foreach($kind in $ordered){$event=Get-AzuriteEditorRouteOne $Probe $kind;if($event.monotonicTicks -le $last){throw 'Native original/read/write evidence is out of order.'};$last=$event.monotonicTicks}
 $dispatch=Get-AzuriteEditorRouteOne $Probe 'native-read-dispatch';$read=Get-AzuriteEditorRouteOne $Probe 'native-read-prefix';$original=Get-AzuriteEditorRouteOne $Probe 'native-original-read-completed';$write=Get-AzuriteEditorRouteOne $Probe 'native-write-admitted';$written=Get-AzuriteEditorRouteOne $Probe 'native-original-write-returned';$complete=Get-AzuriteEditorRouteOne $Probe 'route-selftest-complete'
 if($awake.data.owner -le 0 -or $dispatch.data.iterator -le 0 -or $original.data.data -le 0){throw 'Native object identity proof missing.'}
 foreach($event in @($dispatch,$read,$original,$write,$written)){Assert-AzuriteEditorRouteValue $event.data.owner $awake.data.owner 'same native owner'}
 Assert-AzuriteEditorRouteValue $read.data.iterator $dispatch.data.iterator 'same native iterator'
 Assert-AzuriteEditorRouteValue $read.data.__1__state 0 'native read state'
 Assert-AzuriteEditorRouteValue $dispatch.data.missControl $Spec.controlSeedPath 'safe native miss seed'
 Assert-AzuriteEditorRouteValue $read.data.actualSettingFilePath $Spec.settingsPath 'native owner route'
 Assert-AzuriteEditorRouteValue $read.data.actualCachedWorkspace (Join-Path $Spec.runRoot 'workspace') 'native workspace field'
 Assert-AzuriteEditorRouteValue $original.data.yielded $false 'native read original completion'
 Assert-AzuriteEditorRouteValue $original.data.marker ('lab-read-'+$Spec.runId) 'native original main marker'
 Assert-AzuriteEditorRouteValue $write.data.target $Spec.settingsPath 'native original write target'
 $routed=@($Probe|Where-Object kind -ceq 'native-getter-routed');$returned=@($Probe|Where-Object kind -ceq 'native-readonly-getter-returned')
 if($routed.Count -ne 3 -or $returned.Count -ne 2){throw 'Native getter evidence incomplete.'}
 foreach($id in @('cache','workspace','discovery')){
  $hit=@($routed|Where-Object {$_.data.id -ceq $id});if($hit.Count -ne 1){throw 'Native getter hit missing/duplicate.'}
  Assert-AzuriteEditorRouteValue $hit[0].data.route (Join-Path $Spec.runRoot $id) 'native getter contained route'
  $back=if($id -eq 'discovery'){Get-AzuriteEditorRouteOne $Probe 'native-discovery-getter-returned'}else{$items=@($returned|Where-Object {$_.data.id -ceq $id});if($items.Count -ne 1){throw 'Getter original result missing/duplicate.'};$items[0]}
  Assert-AzuriteEditorRouteValue $back.data.returned (Join-Path $Spec.runRoot $id) 'native getter result'
  if($hit[0].monotonicTicks -le $original.monotonicTicks -or $hit[0].monotonicTicks -ge $back.monotonicTicks -or $back.monotonicTicks -ge $write.monotonicTicks){throw 'Native getter proof order differs.'}
  if($id -eq 'discovery'){Assert-AzuriteEditorRouteValue $back.data.listenerStarted $false 'no listener'}else{Assert-AzuriteEditorRouteValue $back.data.hitDelta 1 'native getter hit';Assert-AzuriteEditorRouteValue $back.data.downstreamIo $false 'no downstream getter I/O'}
 }
 $clear=Get-AzuriteEditorRouteOne $Probe 'addressables-reseeded-after-native-clear';$address=Get-AzuriteEditorRouteOne $Probe 'native-addressables-property-returned'
 Assert-AzuriteEditorRouteValue $clear.data.persistent (Join-Path $Spec.runRoot 'persistent') 'Addressables native clear/reseed'
 Assert-AzuriteEditorRouteValue $clear.data.originalClearObserved $true 'original native dictionary clear'
 Assert-AzuriteEditorRouteValue $address.data.returned (Join-Path $Spec.runRoot 'persistent') 'Addressables native result'
 Assert-AzuriteEditorRouteValue $address.data.catalogLoaded $false 'no catalog'
 Assert-AzuriteEditorRouteValue $complete.data.readHits 1 'native read hit';Assert-AzuriteEditorRouteValue $complete.data.writeHits 1 'native write hit'
 Assert-AzuriteEditorRouteValue $complete.data.saveEdge 'native SaveTask.MoveNext -> native WriteUserSettings' 'native save caller edge'
 if(-not $complete.data.savedSha256.Equals($SavedSha256,[StringComparison]::OrdinalIgnoreCase)){throw 'Original saved file differs from completion proof.'}
 Assert-AzuriteEditorRouteValue $complete.data.readinessUntouched $true 'unchanged readiness';Assert-AzuriteEditorRouteValue $complete.data.normalStartupProved $false 'no normal startup acceptance';Assert-AzuriteEditorRouteValue $complete.data.catalogProved $false 'no catalog acceptance'
 if($Probe[-1].kind -cne 'route-selftest-complete' -or $Guardian[-1].kind -cne 'chainloader-return-admitted'){throw 'Evidence streams did not finish at their exact terminal events.'}
 return [pscustomobject]@{nativeReadProved=$true;nativeSaveProved=$true;guardianComplete=$true;probeEventCount=$Probe.Count;guardianEventCount=$Guardian.Count;editorAccepted=$false;graphicalAccepted=$false;acceptance=$script:EditorAcceptance}
}
function Read-AzuriteEditorRouteJsonl([string]$Path){
 $file=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
 if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $file.Length -lt 2 -or $file.Length -gt 1048576){throw 'Evidence file is missing, linked or exceeds its bound.'}
 $raw=[IO.File]::ReadAllText($Path)
 if(-not $raw.EndsWith("`n")){throw 'Evidence stream ends with an unflushed or truncated event.'}
 $lines=@($raw.Split([char]10)|Where-Object {$_.Trim().Length});if($lines.Count -gt 128){throw 'Evidence event count exceeds its bound.'}
 foreach($line in $lines){if($line.Length -gt 32768){throw 'Evidence event exceeds its bound.'};$line|ConvertFrom-Json -ErrorAction Stop}
}
function Assert-AzuriteEditorRouteNativeStatus($Native,[ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope){
 $status=Get-AzuriteNativeProcessStatus $Native
 foreach($name in @('TimedOut','WindowViolation','OutputLimitExceeded')){Assert-AzuriteEditorRouteValue $Native.$name $false ('monitor '+$name)}
 if(-not [string]::IsNullOrEmpty([string]$Native.Error)){throw 'Native process monitoring failed.'}
 $expected=if($Scope -ceq 'Positive'){0}else{86}
 Assert-AzuriteEditorRouteValue $Native.ExitCode $expected 'native exit code'
}
function Get-AzuriteEditorRouteToolNames {
 return @('EditorRouteCore.psm1','EditorRouteBootstrap.ps1','Invoke-AzuriteEditorRouteSelfTest.ps1','EditorRoutePrepareContract.ps1','RestrictedCliCore.psm1','RestrictedCliNative.cs','Identity-Preflight.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','restricted-startup-contract.json')
}
function Get-AzuriteEditorRouteBinaryPaths {
 return @('BepInEx/patchers/AzuriteEditorIsolation/Azurite.EditorIsolation.Guard.LabOnly.dll','BepInEx/patchers/AzuriteEditorIsolation/Azurite.EditorIsolation.Policy.LabOnly.dll','BepInEx/plugins/AzuriteEditorIsolation/Azurite.EditorIsolation.Probe.LabOnly.dll')
}
function Assert-AzuriteEditorRouteConfigDescriptor($Config){
 if(-not $Config -or $Config.baselineSha256 -cne 'F2E0F930EFDA6A7FA292E872760A27CDC9BB1D232CD82D719446622C4DC6EF06' -or $Config.expectedSha256 -cne 'A0DA59BFA4590A1C3A2B75FF8FF8EE078EF192783606994FF2D1772C358CDBE6'){throw 'InvalidManifest requires the exact reviewed baseline/derived config pair.'}
 $hashes=@{receipt='45C110175D0E260769A868FE015B254C65092F01A335FA359F54077EBE328337';report='3B37F82EF3BF866F6A190BC46AD7B0CA38071259336366310AF3D5076A6FFC11';derived='A0DA59BFA4590A1C3A2B75FF8FF8EE078EF192783606994FF2D1772C358CDBE6'}
 foreach($name in $hashes.Keys){if($Config.$name.sha256 -cne $hashes[$name] -or [string]::IsNullOrWhiteSpace($Config.$name.path)){throw 'Derived config receipt/report/artifact is not the frozen review set.'}}
}
function Assert-AzuriteEditorRouteConfigDerivation($Config){
 Assert-AzuriteEditorRouteConfigDescriptor $Config
 $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'));$task=Join-Path $repo '.superpowers\sdd\azurite-authoring-20261108'
 $paths=@{receipt=(Join-Path $repo 'artifacts\early-abort-config\early-abort-config-receipt.json');report=(Join-Path $task 'early-abort-config-audit.md');derived=(Join-Path $repo 'artifacts\early-abort-config\BepInEx.early-abort.expected.cfg')}
 foreach($name in $paths.Keys){if($Config.$name.path -cne $paths[$name]){throw 'Independent config proof escaped its fixed source path; live config cannot substitute.'};Assert-AzuritePreparedHash $paths[$name] $Config.$name.sha256}
 $receipt=Read-AzuriteBoundedJson $Config.receipt.path 32768
 if($receipt.schema -ne 1 -or $receipt.disposition -cne 'derived-exact-invalid-manifest-config-only' -or $receipt.baseline.sha256.ToUpperInvariant() -cne $Config.baselineSha256 -or $receipt.derivedExpected.sha256.ToUpperInvariant() -cne $Config.expectedSha256 -or $receipt.derivedExpected.path -cne $Config.derived.path){throw 'Config derivation identity does not match the fixed scope.'}
 Assert-AzuriteEditorRouteValue $receipt.semantics.uniqueTripleCount 27 'unchanged config setting count'
 Assert-AzuriteEditorRouteValue $receipt.semantics.orderedTriplesIdentical $true 'unchanged ordered settings'
 Assert-AzuriteEditorRouteValue $receipt.semantics.earlyBoundCount 23 'early binding count'
 Assert-AzuriteEditorRouteValue $receipt.semantics.lateOrphanCount 4 'later binding count'
 foreach($name in @('sameObjectSave','freshConfigObject','freshManagedProcess','all27BindingsReconstructBaselineExactly')){Assert-AzuriteEditorRouteValue $receipt.fixedPoint.$name $true 'derived serialization fixed point'}
 foreach($name in @('requiredPreSha256','requiredPostSha256')){
  if($receipt.scopeRule.InvalidManifest.$name.ToUpperInvariant() -cne $Config.expectedSha256 -or $receipt.scopeRule.PositiveAndProbeMissing.$name.ToUpperInvariant() -cne $Config.baselineSha256){throw 'Scope-specific exact config rules differ from the root ruling.'}
 }
 Assert-AzuriteEditorRouteValue $receipt.scopeRule.variantsOrSemanticTolerance $false 'no config variant tolerance'
 foreach($name in @('aaOrUnityLoaded','nativeRuntimeReplayed','nativeAcceptanceClaimed','labInstallWritten','quarantineCleared')){Assert-AzuriteEditorRouteValue $receipt.limits.$name $false 'source-only derivation scope'}
}
function New-AzuriteEditorRouteContract($Inputs,$BaseContract,[ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope){
 if($Inputs.schema -ne 1 -or $Inputs.admission -cne 'reviewed-cli-route-selftest-only' -or $Inputs.labRoot -cne $script:EditorLab -or $Inputs.baseContractSha256 -cne '5CED2B8185DFE43E94217F3B5D61F7B76D8DC5D70C817CABB50A098D46DC8362'){throw 'Reviewed Editor route admission inputs are absent or invalid.'}
 if($BaseContract.schema -ne 1 -or $BaseContract.contractId -cne 'aa-2023.2.22f1-azurite-1.0.38-cli-nosound-v2'){throw 'Frozen source contract differs.'}
 $tools=Get-AzuriteEditorRouteToolNames
 if((@($Inputs.toolPins.PSObject.Properties.Name|Sort-Object) -join '|') -cne (@($tools|Sort-Object) -join '|')){throw 'Exact reviewed controller/tool source pins are required.'}
 foreach($pin in $Inputs.toolPins.PSObject.Properties){if($pin.Value -cnotmatch '^[A-F0-9]{64}$'){throw 'Invalid reviewed source digest.'}}
 $paths=Get-AzuriteEditorRouteBinaryPaths
 if((@($Inputs.binaries.path|Sort-Object) -join '|') -cne (@($paths|Sort-Object) -join '|')){throw 'Exactly the three reviewed guardian/policy/probe artifacts are required.'}
 foreach($pin in $Inputs.binaries){if($pin.sha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Invalid reviewed harness artifact digest.'}}
 foreach($name in @('copyReceipt','harnessReceipt','guardReview','orderingAudioProof')){if($Inputs.$name.sha256 -cnotmatch '^[A-F0-9]{64}$'){throw ('Missing reviewed receipt: '+$name)}}
 $contract=$BaseContract|ConvertTo-Json -Depth 30|ConvertFrom-Json
 $contract.labRoot=$script:EditorLab;$contract.contractId='editor-cli-route-selftest-v1-'+$Scope;$contract.acceptance=$script:EditorAcceptance
 $contract.commands=@('capabilities')
 $contract.revisionNote='Explicit reviewed derivative of frozen CLI NoSound v2 plus three lab-only isolation artifacts; scope-specific probe absence or malformed manifest is fixed by the controller. No live pin adoption.'
 $additional=@($Inputs.binaries|Where-Object {$Scope -cne 'ProbeMissing' -or $_.path -cne $paths[2]})
 $contract.files=@($contract.files)+$additional
 foreach($directory in $contract.exactDirectories){
  if($directory.path -ceq 'BepInEx/patchers'){$directory.files=@($directory.files)+@('AzuriteEditorIsolation/Azurite.EditorIsolation.Guard.LabOnly.dll','AzuriteEditorIsolation/Azurite.EditorIsolation.Policy.LabOnly.dll')}
  if($directory.path -ceq 'BepInEx/plugins'){$directory.files=if($Scope -ceq 'ProbeMissing'){@()}else{@('AzuriteEditorIsolation/Azurite.EditorIsolation.Probe.LabOnly.dll')}}
 }
 $contract.evidence=@($contract.evidence)+@($Inputs.copyReceipt,$Inputs.harnessReceipt,$Inputs.guardReview,$Inputs.orderingAudioProof)
 if($Scope -ceq 'InvalidManifest'){
  $config=$Inputs.invalidManifestConfig;Assert-AzuriteEditorRouteConfigDescriptor $config
  $pins=@($contract.files|Where-Object path -CEQ 'BepInEx/config/BepInEx.cfg')
  if($pins.Count -ne 1 -or $pins[0].sha256 -cne $config.baselineSha256){throw 'Expected pristine base config pin is missing, duplicated or changed.'}
  # One exact pre/post pin for this scope. No live hashing, transition set or
  # acceptance of both variants; ordinary scopes retain every original base pin.
  $pins[0].sha256=$config.expectedSha256
  $contract.evidence+=@($config.receipt,$config.report,$config.derived)
  $contract.revisionNote+=' InvalidManifest alone uses the independently derived exact early-abort config pin before and after execution.'
 }
 $contract|Add-Member -NotePropertyName routeScope -NotePropertyValue $Scope
 $contract|Add-Member -NotePropertyName toolPins -NotePropertyValue $Inputs.toolPins
 $contract|Add-Member -NotePropertyName probeBinaries -NotePropertyValue $Inputs.binaries
 $contract|Add-Member -NotePropertyName nativeEvidenceSchema -NotePropertyValue 'runId-pid-manifest-v2'
 return $contract
}
function Read-AzuriteEditorRouteInputs([ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope='Positive'){
 $path=Join-Path $PSScriptRoot 'EditorRouteReviewedInputs.json'
 $inputs=Read-AzuriteBoundedJson $path 65536
 # This reviewed input file is a deliberate source artifact. Preparation only
 # validates its expected hashes; it never fills them from the live install.
 $base=Read-AzuriteStartupContract
 [void](New-AzuriteEditorRouteContract $inputs $base $Scope)
 $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
 $task=Join-Path $repo '.superpowers\sdd\azurite-authoring-20261108'
 $expected=@{copyReceipt=(Join-Path $script:EditorLab 'program-copy-manifest.json');harnessReceipt=(Join-Path $task 'editor-isolation-probe-fix1-hashes.json');guardReview=(Join-Path $task 'editor-isolation-native-source-guard-review.json');orderingAudioProof=(Join-Path $task 'normal-start-order-audio-proof.md')}
 foreach($name in $expected.Keys){if($inputs.$name.path -cne $expected[$name]){throw 'Review receipt escaped its fixed source boundary.'};Assert-AzuritePreparedHash $expected[$name] $inputs.$name.sha256}
 $copy=Read-AzuriteBoundedJson $inputs.copyReceipt.path 1048576
 if($copy.destination -cne (Join-Path $script:EditorLab 'install') -or $copy.runtimeStarted -ne $false -or @($copy.mismatches).Count -ne 0 -or $copy.files -ne @($copy.entries).Count){throw 'Copied-host receipt is incomplete or mismatched.'}
 $review=Read-AzuriteBoundedJson $inputs.guardReview.path 32768
 if($review.schema -ne 1 -or $review.disposition -cne 'approved-cli-route-selftest' -or $review.harnessReceiptSha256 -cne $inputs.harnessReceipt.sha256 -or $review.openBlockingFindings -ne 0){throw 'Native source/guardian review does not admit these exact artifacts.'}
 $harness=Read-AzuriteBoundedJson $inputs.harnessReceipt.path 131072
 foreach($pin in $inputs.binaries){$name=[IO.Path]::GetFileName($pin.path);$matched=@($harness.binaries|Where-Object {[IO.Path]::GetFileName($_.path) -ceq $name -and $_.sha256.ToUpperInvariant() -ceq $pin.sha256});if($matched.Count -ne 1){throw 'Artifact is not the reviewed frozen harness binary.'}}
 foreach($pin in $inputs.toolPins.PSObject.Properties){Assert-AzuritePreparedHash (Join-Path $PSScriptRoot $pin.Name) $pin.Value}
 if($Scope -ceq 'InvalidManifest'){Assert-AzuriteEditorRouteConfigDerivation $inputs.invalidManifestConfig}
 return $inputs
}
function Read-AzuriteEditorRouteContract([ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope){
 $inputs=Read-AzuriteEditorRouteInputs $Scope
 $expected=New-AzuriteEditorRouteContract $inputs (Read-AzuriteStartupContract) $Scope
 $path=Join-Path $script:EditorLab ('contracts\editor-route-'+$Scope+'.json')
 $actual=Read-AzuriteBoundedJson $path 1048576
 if(($actual|ConvertTo-Json -Depth 30 -Compress) -cne ($expected|ConvertTo-Json -Depth 30 -Compress)){throw 'Prepared contract differs from its deterministic reviewed inputs.'}
 return $actual
}
function Assert-AzuriteEditorRouteNegativeEvidence([object[]]$Probe,[object[]]$Guardian,$Spec,[string]$ManifestSha256,[int]$NativePid,$StartedUtc,$CompletedUtc,[ValidateSet('ProbeMissing','InvalidManifest')][string]$Scope){
 if($Probe.Count -ne 0){throw 'Negative gate unexpectedly entered the normal probe.'}
 $kinds=if($Scope -ceq 'InvalidManifest'){@('guardian-setup-failed')}else{@('guardian-armed-before-chainloader','missing-or-incomplete-probe','chainloader-exception-or-missing-probe')}
 Assert-AzuriteEditorRouteStream $Guardian $Spec $ManifestSha256 $NativePid $StartedUtc $CompletedUtc $kinds
 if($Scope -ceq 'InvalidManifest'){
  if($Guardian.Count -ne 1 -or $Guardian[0].kind -cne 'guardian-setup-failed'){throw 'Invalid manifest did not fail at initial guardian setup.'}
 }else{
  if($Guardian.Count -ne 2 -or $Guardian[0].kind -cne 'guardian-armed-before-chainloader' -or $Guardian[1].kind -cnotin @('missing-or-incomplete-probe','chainloader-exception-or-missing-probe')){throw 'Missing probe did not fail at mandatory chainloader continuation.'}
  Assert-AzuriteEditorRouteValue $Guardian[0].data.RunId $Spec.runId 'negative guard RunId'
  Assert-AzuriteEditorRouteValue $Guardian[0].data.Mode 'route-selftest' 'negative CLI guard mode'
 }
 Assert-AzuriteEditorRouteValue $Guardian[-1].data.nativeStartupMayResume $false 'guardian own-abort before native continuation'
 if([string]::IsNullOrWhiteSpace([string]$Guardian[-1].data.reason)){throw 'Guardian failure disposition is absent.'}
 return [pscustomobject]@{expectedAbortProved=$true;guardianEventCount=$Guardian.Count;probeEventCount=0;nativeReadProved=$false;nativeSaveProved=$false;editorAccepted=$false;graphicalAccepted=$false;acceptance=$script:EditorAcceptance}
}
function Set-AzuriteEditorRouteChildEnvironment([Diagnostics.ProcessStartInfo]$StartInfo,$Spec){
 if(-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('AZURITE_EDITOR_ISOLATION_MANIFEST','Process'))){throw 'Inherited editor isolation manifest is forbidden.'}
 Set-AzuriteCliChildEnvironment $StartInfo $Spec.requestRoot
 $StartInfo.EnvironmentVariables['AZURITE_EDITOR_ISOLATION_MANIFEST']=$Spec.manifestPath
}
function Get-AzuriteEditorRouteContext([ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope){
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$sid=$identity.User.Value
 $principal=[Security.Principal.WindowsPrincipal]::new($identity)
 if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Unelevated current-account token required.'}
 $profile=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
 Assert-AzuriteCleanupGateClear $script:EditorLab
 Assert-AzuritePrivateAcl $script:EditorLab $sid
 $marker=Read-AzuriteBoundedJson (Join-Path $script:EditorLab 'lab.marker.json') 16384
 if($marker.schema -ne 1 -or $marker.purpose -cne 'AzuriteHeadlessLab' -or $marker.controllerSid -cne $sid -or $marker.dailyInstallPath -cne 'F:\AzureArchive_101'){throw 'Lab ownership/daily-source marker differs.'}
 [void](Assert-AzuriteLabPaths $script:EditorLab (Join-Path $script:EditorLab 'install') $marker.dailyInstallPath)
 $contract=Read-AzuriteEditorRouteContract $Scope
 Assert-AzuriteNoReparseTree $script:EditorLab
 Assert-AzuritePinnedInstall (Join-Path $script:EditorLab 'install') $contract
 Assert-AzuriteEvidence $contract
 if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length -ne 0){throw 'Another AzureArchive process exists.'}
 foreach($dir in @('runs','secrets','contracts')){$path=Join-Path $script:EditorLab $dir;if(Test-Path -LiteralPath $path){Assert-AzuriteCurrentPrivatePath $path $sid}}
 $environment=[pscustomobject]@{localAppData=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData);environmentProfile=$env:USERPROFILE;environmentLocalAppData=$env:LOCALAPPDATA;environmentAppData=$env:APPDATA;environmentTemp=$env:TEMP;environmentTmp=$env:TMP}
 $registeredLow=Get-AzuriteRegisteredLocalLow $profile
 $stateSpec=New-AzuriteSharedStateSpec $registeredLow.path $marker.dailyInstallPath $sid
 # Plan snapshots stay in memory. No helper compilation, ACL, directory or log writes.
 $snapshot=New-AzuriteSharedSnapshot $stateSpec (Read-AzuritePlayerPrefsSnapshot) 'plan' Plan
 return [pscustomobject]@{sid=$sid;profile=$profile;environment=$environment;registeredLow=$registeredLow.path;dailyInstall=$marker.dailyInstallPath;contract=$contract;runtimeAllowed=($marker.PSObject.Properties['runtimeAllowed'] -and $marker.runtimeAllowed -is [bool] -and $marker.runtimeAllowed -eq $true);stateSpec=$stateSpec}
}
function New-AzuriteEditorRouteRun($Context,[ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope,[int]$TimeoutSeconds){
 $spec=Get-AzuriteEditorRouteSpec ([guid]::NewGuid().ToString('D'));$sid=$Context.sid
 foreach($name in @('runs','secrets')){$path=Join-Path $spec.labRoot $name;if(-not(Test-Path -LiteralPath $path)){[void][IO.Directory]::CreateDirectory($path);Set-AzuriteLabAcl $spec.labRoot $path $sid}else{Assert-AzuriteCurrentPrivatePath $path $sid}}
 if(Test-Path -LiteralPath $spec.runRoot){throw 'Fresh run directory unexpectedly exists.'}
 [void][IO.Directory]::CreateDirectory($spec.runRoot);Set-AzuriteLabAcl $spec.labRoot $spec.runRoot $sid
 foreach($name in @('settings','workspace','cache','persistent','discovery','output','request','tooling','shared-state','previous-log')){$path=Join-Path $spec.runRoot $name;[void][IO.Directory]::CreateDirectory($path);Set-AzuriteLabAcl $spec.labRoot $path $sid}
 Write-AzuriteFlushedJson $spec.settingsPath (New-AzuriteEditorRouteSeed $spec 'lab-read')
 Write-AzuriteFlushedJson $spec.controlSeedPath (New-AzuriteEditorRouteSeed $spec 'miss-control')
 $manifest=New-AzuriteEditorRouteManifest $spec (Get-AzuriteSha256 $spec.settingsPath) (Get-AzuriteSha256 $spec.controlSeedPath)
 if($Scope -ceq 'InvalidManifest'){[IO.File]::WriteAllBytes($spec.manifestPath,[byte[]]@(123))}else{Write-AzuriteFlushedJson $spec.manifestPath $manifest}
 $argumentsPath=Join-Path $spec.requestRoot 'arguments.json';Write-AzuriteFlushedJson $argumentsPath @{argv=@('capabilities');cwd=(Join-Path $spec.runRoot 'workspace')}
 $tooling=Join-Path $spec.runRoot 'tooling';$toolHashes=[ordered]@{}
 foreach($name in (Get-AzuriteEditorRouteToolNames)){$source=Join-Path $PSScriptRoot $name;$target=Join-Path $tooling $name;Assert-AzuritePreparedHash $source $Context.contract.toolPins.$name;Copy-Item -LiteralPath $source -Destination $target;Assert-AzuritePreparedHash $target $Context.contract.toolPins.$name;$toolHashes[$name]=$Context.contract.toolPins.$name}
 $contractPath=Join-Path $tooling 'editor-route-contract.json';Write-AzuriteFlushedJson $contractPath $Context.contract
 $inputsPath=Join-Path $tooling 'EditorRouteReviewedInputs.json';Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorRouteReviewedInputs.json') -Destination $inputsPath
 $toolHashes['editor-route-contract.json']=Get-AzuriteSha256 $contractPath;$toolHashes['EditorRouteReviewedInputs.json']=Get-AzuriteSha256 $inputsPath
 $helperPath=Join-Path $tooling 'RestrictedCliNative.dll';$build=New-AzuriteNetFrameworkHelper (Join-Path $tooling 'RestrictedCliNative.cs') $helperPath
 $toolHashes['RestrictedCliNative.dll']=$build.assemblySha256
 $actualLow=[Azurite.Restricted.KnownFolders]::LocalLow()
 if(-not $actualLow.Equals($Context.registeredLow,[StringComparison]::OrdinalIgnoreCase)){throw 'Native KnownFolder does not match actual registered profile.'}
 $stateSpec=New-AzuriteSharedStateSpec $actualLow $Context.dailyInstall $sid
 $stateBefore=New-AzuriteSharedSnapshot $stateSpec (Read-AzuritePlayerPrefsSnapshot) $spec.runId Before
 $stateDir=Join-Path $spec.runRoot 'shared-state';Write-AzuriteFlushedJson (Join-Path $stateDir 'scope.json') $stateSpec;Write-AzuriteFlushedJson (Join-Path $stateDir 'before.json') $stateBefore
 # Match the serialized control's property shape at this DTO boundary. A shallow
 # outer cast leaves OrderedDictionary metadata visible through PSObject.Properties.
 $control=[ordered]@{schema=1;scope=$Scope;runId=$spec.runId;jobId=$spec.jobId;labRoot=$spec.labRoot;runRoot=$spec.runRoot;controllerSid=$sid;controllerProfile=$Context.profile;currentLocalLow=$actualLow;currentEnvironment=$Context.environment;manifestSha256=(Get-AzuriteSha256 $spec.manifestPath);seedSha256=$manifest.SeedSha256;controlSeedSha256=$manifest.ControlSha256;argumentsSha256=(Get-AzuriteSha256 $argumentsPath);contractSha256=$toolHashes['editor-route-contract.json'];toolHashes=[pscustomobject]$toolHashes;sharedStateBeforeSha256=(Get-AzuriteSha256 (Join-Path $stateDir 'before.json'));sharedStateScopeSha256=(Get-AzuriteSha256 (Join-Path $stateDir 'scope.json'));deadlineUtc=[datetime]::UtcNow.AddSeconds($TimeoutSeconds).ToString('o');helperBuild=$build}
 Write-AzuriteFlushedJson (Join-Path $tooling 'control.json') $control
 foreach($file in Get-ChildItem -LiteralPath $spec.runRoot -Recurse -File){Set-AzuriteLabAcl $spec.labRoot $file.FullName $sid}
 return [pscustomobject]@{spec=$spec;control=[pscustomobject]$control;controlSha256=(Get-AzuriteSha256 (Join-Path $tooling 'control.json'));stateSpec=$stateSpec;stateBefore=$stateBefore}
}
function Assert-AzuriteEditorRoutePreparedFile([string]$Path,$ExpectedHash,[string]$CheckName){
 try{Assert-AzuritePreparedHash $Path $ExpectedHash}
 catch{
  $cause=$_.Exception;$actual=$null;$actualError=$null
  try{if(Test-Path -LiteralPath $Path -PathType Leaf){$actual=Get-AzuriteSha256 $Path}else{$actualError='FileAbsentOrNotLeaf'}}catch{$actualError=$_.Exception.GetType().Name}
  $detail=[pscustomobject]@{check=$CheckName;path=$Path;expectedSha256=[string]$ExpectedHash;expectedValueType=$(if($null -eq $ExpectedHash){'null'}else{$ExpectedHash.GetType().FullName});actualSha256=$actual;actualReadError=$actualError}
  $failure=[InvalidOperationException]::new(('Prepared input verification failed at '+$CheckName+'.'),$cause)
  $failure.Data['EditorRoutePreparedInputMismatch']=$detail
  throw $failure
 }
}
function Get-AzuriteEditorRoutePrivateFailure($Record,[string]$Stage){
 $exception=$Record.Exception;$mismatch=$null
 for($i=0;$exception -and $i -lt 8;$i++){
  if($exception.Data.Contains('EditorRoutePreparedInputMismatch')){$mismatch=$exception.Data['EditorRoutePreparedInputMismatch'];break}
  $exception=$exception.InnerException
 }
 return [pscustomobject]@{stage=$Stage;exceptionType=$Record.Exception.GetType().FullName;message=$Record.Exception.Message;scriptStack=$Record.ScriptStackTrace;preparedInputMismatch=$mismatch}
}
function Assert-AzuriteEditorRouteEffects($Run){
 $spec=$Run.spec;$control=$Run.control
 Assert-AzuriteNoReparseTree $spec.runRoot
 if($Run.PSObject.Properties['controlSha256']){Assert-AzuriteEditorRoutePreparedFile (Join-Path $spec.runRoot 'tooling\control.json') $Run.controlSha256 'outer-launch-control'}
 Assert-AzuriteEditorRoutePreparedFile $spec.manifestPath $control.manifestSha256 'manifest'
 Assert-AzuriteEditorRoutePreparedFile $spec.controlSeedPath $control.controlSeedSha256 'miss-control-seed'
 Assert-AzuriteEditorRoutePreparedFile (Join-Path $spec.requestRoot 'arguments.json') $control.argumentsSha256 'capabilities-arguments'
 foreach($name in @('workspace','cache','persistent','discovery')){if(@(Get-ChildItem -LiteralPath (Join-Path $spec.runRoot $name) -Force).Count){throw 'Resource-free selftest produced unexpected application writes.'}}
 foreach($item in Get-ChildItem -LiteralPath (Join-Path $spec.runRoot 'settings') -Force){if($item.PSIsContainer -or $item.Name -cnotmatch '^(user_settings\.json|miss_control\.json|user_settings\.json\.bk\.[123])$'){throw 'Unexpected settings file or sidecar.'}}
 if($control.scope -cne 'Positive'){Assert-AzuriteEditorRoutePreparedFile $spec.settingsPath $control.seedSha256 'main-seed-negative';if(@(Get-ChildItem -LiteralPath (Join-Path $spec.runRoot 'settings') -Force).Count -ne 2){throw 'Negative control wrote settings sidecars.'}}
 $outputNames=@('identity.json','aa.stdout.log','aa.stderr.log','unity.log','editor-isolation-guardian.jsonl','editor-isolation-evidence.jsonl','bootstrap-result.json','bepinex.LogOutput.log')
 foreach($item in Get-ChildItem -LiteralPath $spec.outputRoot -Force){if($item.PSIsContainer -or $item.Name -cnotin $outputNames -or $item.Length -gt 4194304){throw 'Unexpected or excessive native output.'}}
 foreach($item in Get-ChildItem -LiteralPath $spec.requestRoot -Force){if($item.PSIsContainer -or $item.Name -cnotin @('arguments.json','response.json') -or $item.Length -gt 1048576){throw 'Unexpected or excessive CLI transport output.'}}
 foreach($pin in $control.toolHashes.PSObject.Properties){Assert-AzuriteEditorRoutePreparedFile (Join-Path $spec.runRoot ('tooling\'+$pin.Name)) $pin.Value ('tooling:'+$pin.Name)}
}
function Get-AzuriteEditorRouteMembershipSample($Job){
 # Identity/liveness details are diagnostic only. No member is excluded from
 # admission because its PID cannot be queried or appears to have exited.
 $ids=@($Job.ProcessIds())
 if($ids.Count -gt 32){throw 'Owned membership exceeds the bounded diagnostic/admission limit.'}
 $members=[Collections.Generic.List[object]]::new()
 foreach($id in @($ids|Select-Object -First 8)){
  $state='unknown';$created=$null;$name=$null;$queryError=$null;$process=$null
  try{
   $process=[Diagnostics.Process]::GetProcessById($id)
   $state=if($process.HasExited){'exited'}else{'live'}
   $created=$process.StartTime.ToUniversalTime().ToString('o')
   $name=$process.ProcessName;if($name.Length -gt 64){$name=$name.Substring(0,64)}
  }catch{$state='unavailable';$queryError=$_.Exception.GetBaseException().GetType().Name}
  finally{if($process){$process.Dispose()}}
  $members.Add([pscustomobject]@{pid=$id;state=$state;createdUtc=$created;name=$name;queryError=$queryError})
 }
 return [pscustomobject]@{utc=[datetime]::UtcNow.ToString('o');memberCount=$ids.Count;memberIds=@($ids);members=$members.ToArray();detailsTruncated=($ids.Count -gt 8);queryError=$null}
}
function Wait-AzuriteEditorRouteQuiescence($Job,$Native,[string]$RunId,$DeadlineUtc){
 [void](Get-AzuriteEditorRouteSpec $RunId)
 Assert-AzuriteEditorRouteValue $Native.DirectProcessExited $true 'direct native process exit before quiescence'
 if(($Native.Pid -isnot [int] -and $Native.Pid -isnot [long]) -or $Native.Pid -le 0 -or $Native.Pid -eq $PID){throw 'Invalid direct native PID at quiescence boundary.'}
 $deadline=ConvertTo-AzuriteUtcInstant $DeadlineUtc;$started=[datetime]::UtcNow
 $available=[math]::Floor(($deadline-$started).TotalMilliseconds)
 $limit=[int][math]::Max(0,[math]::Min(2000,$available))
 $watch=[Diagnostics.Stopwatch]::StartNew();$samples=[Collections.Generic.List[object]]::new()
 $attempts=0;$singletons=0;$quiescent=$false;$reason='shared-deadline-exhausted';$lastIds=@();$queryFailure=$null
 $self=[Diagnostics.Process]::GetCurrentProcess()
 try{$created=$self.StartTime.ToUniversalTime().ToString('o')}finally{$self.Dispose()}
 while($limit -gt 0 -and $watch.ElapsedMilliseconds -lt $limit -and [datetime]::UtcNow -lt $deadline){
  $attempts++
  try{$sample=Get-AzuriteEditorRouteMembershipSample $Job}
  catch{$queryFailure=$_.Exception.GetBaseException().GetType().Name;$sample=[pscustomobject]@{utc=[datetime]::UtcNow.ToString('o');memberCount=-1;memberIds=@();members=@();detailsTruncated=$false;queryError=$queryFailure}}
  # Retain first four and last four observations, never unbounded polling logs.
  if($samples.Count -eq 8){$samples.RemoveAt(4)};$samples.Add($sample)
  if($queryFailure){$reason='owned-membership-query-failed';break}
  $lastIds=@($sample.memberIds)
  if($watch.ElapsedMilliseconds -ge $limit -or [datetime]::UtcNow -ge $deadline){$reason='quiescence-budget-exhausted';break}
  if($lastIds.Count -eq 1 -and $lastIds[0] -eq $PID){$singletons++}else{$singletons=0}
  if($singletons -ge 2){$quiescent=$true;$reason='two-exact-bootstrap-only-observations';break}
  $reason='quiescence-budget-exhausted'
  $sleep=[int][math]::Min(20,[math]::Min(($limit-$watch.ElapsedMilliseconds),[math]::Floor(($deadline-[datetime]::UtcNow).TotalMilliseconds)))
  if($sleep -gt 0){[Threading.Thread]::Sleep($sleep)}
 }
 return [pscustomobject]@{schema=1;method='owned-job-singleton-v1';runId=$RunId;bootstrapPid=$PID;bootstrapCreatedUtc=$created;nativePid=$Native.Pid;nativeDirectProcessExited=$true;startedUtc=$started.ToString('o');completedUtc=[datetime]::UtcNow.ToString('o');deadlineUtc=$deadline.ToString('o');limitMilliseconds=$limit;elapsedMilliseconds=$watch.ElapsedMilliseconds;attemptCount=$attempts;singletonObservations=$singletons;quiescent=$quiescent;reason=$reason;queryFailure=$queryFailure;lastMemberIds=@($lastIds);samples=$samples.ToArray()}
}
function Assert-AzuriteEditorRouteQuiescence($Observation,[string]$RunId,[int]$BootstrapPid,[int]$NativePid,$NativeCompletedUtc,$DeadlineUtc){
 if(-not $Observation){throw 'Bounded owned membership proof is absent.'}
 foreach($pair in @(@($Observation.schema,1),@($Observation.method,'owned-job-singleton-v1'),@($Observation.runId,$RunId),@($Observation.bootstrapPid,$BootstrapPid),@($Observation.nativePid,$NativePid),@($Observation.nativeDirectProcessExited,$true),@($Observation.quiescent,$true),@($Observation.singletonObservations,2))){Assert-AzuriteEditorRouteValue $pair[0] $pair[1] 'owned quiescence correlation'}
 foreach($name in @('limitMilliseconds','elapsedMilliseconds','attemptCount')){if($Observation.$name -isnot [int] -and $Observation.$name -isnot [long]){throw 'Owned quiescence counters require integer evidence.'}}
 $started=ConvertTo-AzuriteUtcInstant $Observation.startedUtc;$completed=ConvertTo-AzuriteUtcInstant $Observation.completedUtc;$deadline=ConvertTo-AzuriteUtcInstant $DeadlineUtc
 if($Observation.queryFailure -or $Observation.reason -cne 'two-exact-bootstrap-only-observations' -or $Observation.limitMilliseconds -lt 1 -or $Observation.limitMilliseconds -gt 2000 -or $Observation.elapsedMilliseconds -lt 0 -or $Observation.elapsedMilliseconds -ge $Observation.limitMilliseconds -or $Observation.attemptCount -lt 2 -or $started -lt (ConvertTo-AzuriteUtcInstant $NativeCompletedUtc) -or $completed -lt $started -or $completed -gt $deadline -or (ConvertTo-AzuriteUtcInstant $Observation.deadlineUtc) -ne $deadline){throw 'Owned membership proof is failed, stale or outside shared deadline.'}
 if(@($Observation.lastMemberIds).Count -ne 1 -or $Observation.lastMemberIds[0] -ne $BootstrapPid -or @($Observation.samples).Count -lt 2 -or @($Observation.samples).Count -gt 8){throw 'Owned membership proof omitted the exact singleton boundary.'}
 $previous=$null
 foreach($sample in @($Observation.samples|Select-Object -Last 2)){
  $time=ConvertTo-AzuriteUtcInstant $sample.utc
  if($sample.queryError -or $sample.memberCount -ne 1 -or @($sample.memberIds).Count -ne 1 -or $sample.memberIds[0] -ne $BootstrapPid -or $time -lt $started -or $time -gt $completed -or ($previous -and $time -le $previous)){throw 'Two fresh exact-bootstrap-only observations are required.'}
  $previous=$time
 }
}
function Assert-AzuriteEditorRouteResult($Run,$Outer,$Result,$Contract){
 $spec=$Run.spec;$control=$Run.control
 Assert-AzuriteCleanupConfirmed $Outer
 foreach($pair in @(@($Result.schema,1),@($Result.identityMode,'CurrentAccount'),@($Result.scope,$control.scope),@($Result.runId,$spec.runId),@($Result.jobId,$spec.jobId),@($Outer.RunId,$spec.jobId),@($Result.bootstrapPid,$Outer.Pid),@($Result.controllerSid,$control.controllerSid),@($Result.status,'passed'),@($Result.jobOwned,$true),@($Result.contractSha256,$control.contractSha256),@($Result.manifestSha256,$control.manifestSha256),@($Result.argumentsSha256,$control.argumentsSha256),@($Result.noSoundConfigured,$true),@($Result.acceptance,$Contract.acceptance))){Assert-AzuriteEditorRouteValue $pair[0] $pair[1] 'bootstrap/native binding'}
 if(($Result.aaPid -isnot [int] -and $Result.aaPid -isnot [long]) -or $Result.aaPid -le 0 -or $Result.aaPid -eq $Outer.Pid){throw 'Native PID is absent or is the bootstrap itself.'}
 $native=[pscustomobject]@{ExitCode=$Result.aaExitCode;TimedOut=$Result.timedOut;WindowViolation=$Result.windowViolation;OutputLimitExceeded=$Result.outputLimitExceeded;Error=$Result.processError}
 Assert-AzuriteEditorRouteNativeStatus $native $control.scope
 $window=Get-AzuriteNativeTimeWindow $Result $Outer.StartedUtc ([datetime]::UtcNow)
 $end=ConvertTo-AzuriteUtcInstant $Result.aaCompletedUtc
 if($end -lt $window.nativeStartedUtc -or $end -gt [datetime]::UtcNow){throw 'Native completion time is outside the observed process lifetime.'}
 Assert-AzuriteEditorRouteQuiescence $Result.nativeQuiescence $spec.runId $Outer.Pid $Result.aaPid $end $control.deadlineUtc
 if(($Result.nativeArguments -join '|') -cne ($spec.nativeArguments -join '|')){throw 'Native NoSound startup argv differs from fixed contract.'}
 $proofs=@($Contract.evidence|Where-Object {$_.path.EndsWith('STARTUP-CONTRACT.md')})
 if($proofs.Count -ne 1 -or $Result.noSoundProofSha256 -cne $proofs[0].sha256){throw 'Native NoSound configuration proof is unbound.'}
 $identityPath=Join-Path $spec.outputRoot 'identity.json';Assert-AzuritePreparedHash $identityPath $Result.identitySha256
 $identity=Read-AzuriteBoundedJson $identityPath 32768
 Assert-AzuriteCurrentIdentityRecord $identity $Result.profilePathProof $control.controllerSid $control.controllerProfile $control.currentLocalLow $control.currentEnvironment
 if($identity.runId -cne $spec.runId -or $identity.pid -ne $Outer.Pid -or $identity.status -cne 'passed' -or @($identity.unexpectedPrivileges).Count){throw 'Identity proof is not the unelevated owned bootstrap.'}
 foreach($name in @('aa.stdout.log','aa.stderr.log','unity.log')){$path=Join-Path $spec.outputRoot $name;if((Get-Item -LiteralPath $path).Length -gt 4194304){throw 'Native log is excessive.'};Assert-AzuritePreparedHash $path $Result.($name+'Sha256')}
 $guardianPath=Join-Path $spec.outputRoot 'editor-isolation-guardian.jsonl';$probePath=Join-Path $spec.outputRoot 'editor-isolation-evidence.jsonl'
 Assert-AzuritePreparedHash $guardianPath $Result.guardianSha256
 $guardian=@(Read-AzuriteEditorRouteJsonl $guardianPath);$probe=@()
 if(Test-Path -LiteralPath $probePath){Assert-AzuritePreparedHash $probePath $Result.probeSha256;$probe=@(Read-AzuriteEditorRouteJsonl $probePath)}elseif($Result.probeSha256){throw 'Claimed probe evidence is missing.'}
 Assert-AzuritePreparedHash $spec.settingsPath $Result.savedSha256
 if($control.scope -ceq 'Positive'){
  $response=Read-AzuriteCliResponse (Join-Path $spec.requestRoot 'response.json') capabilities $window.nativeStartedUtc
  if($response.Sha256 -cne $Result.responseSha256 -or $response.Bytes -ne $Result.responseBytes){throw 'Actual native capabilities response was changed or substituted.'}
  $saved=Read-AzuriteBoundedJson $spec.settingsPath 65536
  if($saved.username -cne ('lab-saved-'+$spec.runId) -or $saved.workspacePath -cne (Join-Path $spec.runRoot 'workspace') -or $saved.cachePath -cne (Join-Path $spec.runRoot 'cache')){throw 'Native saved-file marker/path proof differs.'}
  return Assert-AzuriteEditorRouteEvidence $probe $guardian $spec $control.manifestSha256 $Result.aaPid $window.nativeStartedUtc $end $Result.savedSha256
 }
 if(Test-Path -LiteralPath (Join-Path $spec.requestRoot 'response.json')){throw 'Negative control reached a native CLI response after the guard boundary.'}
 return Assert-AzuriteEditorRouteNegativeEvidence $probe $guardian $spec $control.manifestSha256 $Result.aaPid $window.nativeStartedUtc $end $control.scope
}
function Invoke-AzuriteEditorRouteOwnedBootstrap([Diagnostics.ProcessStartInfo]$StartInfo,[string]$RunId,$DeadlineUtc,[string[]]$WatchedPaths){
 $spec=Get-AzuriteEditorRouteSpec $RunId
 $remaining=[int]((ConvertTo-AzuriteUtcInstant $DeadlineUtc)-[datetime]::UtcNow).TotalMilliseconds
 if($remaining -lt 1000 -or $remaining -gt 300000){throw 'Shared launch deadline is expired or exceeds the bounded contract.'}
 return [Azurite.Restricted.ProcessPump]::RunBootstrap($StartInfo,$remaining,5000,$spec.jobId,262144,$WatchedPaths,4194304)
}
function Assert-AzuriteEditorRouteLoaderEvidence($Loader,$Run,$Outer,$Native,$Proof){
 # Proof must be the freshly returned independent native/guardian validation,
 # never the bootstrap's own serialized claim or an early-abort label alone.
 $spec=$Run.spec;$control=$Run.control;$scope=$control.scope
 if($scope -cnotin @('Positive','ProbeMissing','InvalidManifest') -or -not $Proof){throw 'Loader evidence requires a known scope and independently validated native proof.'}
 Assert-AzuriteCleanupConfirmed $Outer
 foreach($pair in @(@($Outer.RunId,$spec.jobId),@($Native.bootstrapPid,$Outer.Pid),@($Native.scope,$scope),@($Native.status,'passed'),@($Native.runId,$spec.runId),@($Native.jobId,$spec.jobId),@($Native.manifestSha256,$control.manifestSha256),@($Native.argumentsSha256,$control.argumentsSha256),@($Native.noSoundConfigured,$true),@($Proof.editorAccepted,$false),@($Proof.graphicalAccepted,$false))){Assert-AzuriteEditorRouteValue $pair[0] $pair[1] 'loader/native proof binding'}
 if(($Native.aaPid -isnot [int] -and $Native.aaPid -isnot [long]) -or $Native.aaPid -le 0 -or $Native.aaPid -eq $Outer.Pid){throw 'Loader proof has no distinct native PID.'}
 Assert-AzuriteEditorRouteNativeStatus ([pscustomobject]@{ExitCode=$Native.aaExitCode;TimedOut=$Native.timedOut;WindowViolation=$Native.windowViolation;OutputLimitExceeded=$Native.outputLimitExceeded;Error=$Native.processError}) $scope
 $expectedArgv=@('-batchmode','-nographics','-disableaudio','-logFile',(Join-Path $spec.outputRoot 'unity.log'))
 if(($Native.nativeArguments -join '|') -cne ($expectedArgv -join '|')){throw 'Loader exception cannot change fixed native startup flags.'}
 if($scope -ceq 'Positive'){
  foreach($name in @('nativeReadProved','nativeSaveProved','guardianComplete')){Assert-AzuriteEditorRouteValue $Proof.$name $true 'positive native proof before loader admission'}
 }else{
  Assert-AzuriteEditorRouteValue $Proof.expectedAbortProved $true 'guardian own-abort proof'
  Assert-AzuriteEditorRouteValue $Proof.guardianEventCount $(if($scope -ceq 'InvalidManifest'){1}else{2}) 'exact negative guardian boundary'
  Assert-AzuriteEditorRouteValue $Proof.probeEventCount 0 'no probe continuation after negative guardian boundary'
 }
 foreach($name in @('Present','Fresh','Complete')){if($Loader.$name -isnot [bool]){throw 'Loader status requires typed Boolean evidence.'}}
 $destination=Join-Path $spec.outputRoot 'bepinex.LogOutput.log';$source=Join-Path $spec.installRoot 'BepInEx\LogOutput.log'
 if($Loader.Path -cne $destination){throw 'Copied loader evidence belongs to a different run.'}
 if(-not $Loader.Present){
  if($scope -cne 'InvalidManifest' -or $Loader.Fresh -or $Loader.Complete -or $Loader.Sha256 -or (Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $source)){throw 'Absent loader content is admitted only for the proved InvalidManifest early abort.'}
  return [pscustomobject]@{scope=$scope;accepted=$true;earlyAbortExceptionUsed=$true;disposition='correlated-invalid-manifest-abort-loader-absent';present=$false;retainedBytes=0}
 }
 if(-not $Loader.Fresh -or -not $Loader.Complete -or $Loader.SourcePath -cne $source){throw 'Existing loader evidence must remain fresh, complete and from the pinned source.'}
 foreach($name in @('OriginalBytes','RetainedBytes')){if(($Loader.$name -isnot [int] -and $Loader.$name -isnot [long]) -or $Loader.$name -lt 0 -or $Loader.$name -gt 4194304){throw 'Loader byte evidence is invalid or excessive.'}}
 if($Loader.OriginalBytes -ne $Loader.RetainedBytes -or (Get-Item -LiteralPath $destination -Force).Length -ne $Loader.RetainedBytes){throw 'Loader evidence was truncated or its byte count changed.'}
 Assert-AzuriteEditorRoutePreparedFile $destination $Loader.Sha256 'loader-log-copy'
 $empty=$Loader.OriginalBytes -eq 0
 if($empty -and $scope -cne 'InvalidManifest'){throw 'Positive and ProbeMissing require nonempty fresh complete loader evidence.'}
 return [pscustomobject]@{scope=$scope;accepted=$true;earlyAbortExceptionUsed=$empty;disposition=$(if($empty){'correlated-invalid-manifest-abort-loader-empty'}else{'fresh-complete-nonempty-loader'});present=$true;retainedBytes=$Loader.RetainedBytes;sha256=$Loader.Sha256}
}
function Invoke-AzuriteEditorRouteController {
 param([ValidateSet('Positive','ProbeMissing','InvalidManifest')][string]$Scope='Positive',[ValidateRange(10,300)][int]$TimeoutSeconds=120,[switch]$PlanOnly,[switch]$PrepareOnly)
 $context=$null;$failure=$null
 try{$context=Get-AzuriteEditorRouteContext $Scope}catch{$failure=$_.Exception.GetType().Name}
 if($PlanOnly){return [pscustomobject]@{scope=$Scope;ready=($null -ne $context);nativeStartAllowed=($null -ne $context -and $context.runtimeAllowed);willLaunch=$false;willWrite=$false;failureCount=[int]($null -ne $failure);failureType=$failure;acceptance=$script:EditorAcceptance}}
 if(-not $context){throw 'Editor route prerequisites failed; no run was staged or native process started. Review the fixed inputs, pins and quarantine.'}
 if(-not $PrepareOnly -and -not $context.runtimeAllowed){throw 'Lab marker still forbids native start; source review and explicit lab admission are required.'}
 $mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');$locked=$false
 try{
  try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
  if(-not $locked){throw 'Another AA laboratory run holds the shared lock.'}
  Assert-AzuriteCleanupGateClear $script:EditorLab
  if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'AzureArchive appeared before preparation.'}
  $run=New-AzuriteEditorRouteRun $context $Scope $TimeoutSeconds;$spec=$run.spec;$control=$run.control
  if($PrepareOnly){return [pscustomobject]@{runId=$spec.runId;scope=$Scope;prepared=$true;willLaunch=$false;reusable=$false;acceptance=$script:EditorAcceptance}}
  $quarantine=Start-AzuriteCleanupGate $spec.labRoot $spec.jobId $spec.runRoot $context.sid
  $outer=$null;$passed=$false;$proof=$null;$stateComparison=$null;$errorType='';$reason='';$result=$null;$loader=$null;$loaderAdmission=$null
  $privateFailures=[Collections.Generic.List[object]]::new();$validationStage='owned-launch-and-cleanup'
  try{
   if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'AzureArchive appeared before owned launch.'}
   $loaderLog=Join-Path $spec.installRoot 'BepInEx\LogOutput.log'
   $previous=Move-AzuritePreviousLoaderLog $loaderLog (Join-Path $spec.runRoot 'previous-log\LogOutput.log')
   if($previous.Present){Set-AzuriteLabAcl $spec.labRoot $previous.Path $context.sid}
   $shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
   $psi=New-AzuriteProcessInfo -FilePath $shell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $spec.runRoot 'tooling\EditorRouteBootstrap.ps1'),'-ControlPath',(Join-Path $spec.runRoot 'tooling\control.json')) -WorkingDirectory $spec.runRoot
   $remaining=[int]((ConvertTo-AzuriteUtcInstant $control.deadlineUtc)-[datetime]::UtcNow).TotalMilliseconds
   if($remaining -lt 1000){throw 'Shared deadline expired during preparation.'}
   $watched=@('output','request','settings','workspace','cache','persistent','discovery'|ForEach-Object {Join-Path $spec.runRoot $_})
   $outer=Invoke-AzuriteEditorRouteOwnedBootstrap $psi $spec.runId $control.deadlineUtc $watched
   # Nothing mutable is copied, read for final acceptance or trimmed before drain.
   Assert-AzuriteCleanupConfirmed $outer
   [IO.File]::WriteAllText((Join-Path $spec.runRoot 'bootstrap.stdout.log'),$outer.Stdout);[IO.File]::WriteAllText((Join-Path $spec.runRoot 'bootstrap.stderr.log'),$outer.Stderr)
   $outerStatus=Get-AzuriteOuterProcessStatus $outer;if($outerStatus.failed){throw 'Owned bootstrap process/monitor failed.'}
   $validationStage='fresh-loader-evidence'
   $loader=Copy-AzuriteFreshLoaderLog $loaderLog (Join-Path $spec.outputRoot 'bepinex.LogOutput.log') $outer.StartedUtc
   $validationStage='prepared-inputs-and-effects'
   Assert-AzuriteEditorRouteEffects $run
   $validationStage='native-result-and-proof'
   $result=Read-AzuriteBoundedJson (Join-Path $spec.outputRoot 'bootstrap-result.json') 65536
   $proof=Assert-AzuriteEditorRouteResult $run $outer $result $context.contract
   $validationStage='scope-loader-evidence'
   $loaderAdmission=Assert-AzuriteEditorRouteLoaderEvidence $loader $run $outer $result $proof
   if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'Another AzureArchive exists after the owned job drained.'}
   $validationStage='pinned-install'
   Assert-AzuritePinnedInstall $spec.installRoot $context.contract
   $passed=$true
  }catch{$errorType=$_.Exception.GetType().Name;$reason=$_.Exception.Message;$privateFailures.Add((Get-AzuriteEditorRoutePrivateFailure $_ $validationStage))}
  $stateVerified=$false
  if($outer -and $outer.CleanupVerified){
   try{
    Assert-AzuriteCleanupConfirmed $outer
    $stateDir=Join-Path $spec.runRoot 'shared-state'
    Assert-AzuritePreparedHash (Join-Path $stateDir 'before.json') $control.sharedStateBeforeSha256
    Assert-AzuritePreparedHash (Join-Path $stateDir 'scope.json') $control.sharedStateScopeSha256
    $after=New-AzuriteSharedSnapshot $run.stateSpec (Read-AzuritePlayerPrefsSnapshot) $spec.runId After
    Write-AzuriteFlushedJson (Join-Path $stateDir 'after.json') $after
    $stateComparison=Compare-AzuriteSharedSnapshots $run.stateBefore $after $run.stateSpec $spec.runId
    Write-AzuriteFlushedJson (Join-Path $stateDir 'delta.json') $stateComparison
    if(-not $stateComparison.accepted -or -not $stateComparison.userPreferencesUnchanged -or @($stateComparison.unexpected).Count){throw 'Shared user state changed unexpectedly.'}
    Assert-AzuritePinnedInstall $spec.installRoot $context.contract
    $stateVerified=$true
   }catch{$passed=$false;$reason+=' Shared-state/pin verification failed: '+$_.Exception.Message;$errorType=$_.Exception.GetType().Name;$privateFailures.Add((Get-AzuriteEditorRoutePrivateFailure $_ 'shared-state-and-pins'))}
  }else{$passed=$false;$reason+=' Owned-job drain is unverified; evidence must stay untouched.'}
  $safe=[ordered]@{schema=1;runId=$spec.runId;scope=$Scope;passed=$passed;cleanupVerified=[bool]($outer -and $outer.CleanupVerified);sharedStateVerified=$stateVerified;unexpectedStateDeltaCount=$(if($stateComparison){@($stateComparison.unexpected).Count}else{-1});engineMetadataDeltaCount=$(if($stateComparison){@($stateComparison.engineMetadata).Count}else{-1});guardianEventCount=$(if($proof){$proof.guardianEventCount}else{0});probeEventCount=$(if($proof){$proof.probeEventCount}else{0});quarantined=(-not ($passed -and $stateVerified));restorationPerformed=$false;editorAccepted=$false;graphicalAccepted=$false;acceptance=$script:EditorAcceptance}
  # Private diagnostics include paths/errors. Only counts/status leave this API.
  if($outer -and $outer.CleanupVerified){Write-AzuriteFlushedJson (Join-Path $spec.runRoot 'controller-result.json') @{summary=$safe;errorType=$errorType;reason=$reason;native=$result;cleanup=$outer;control=$control;expectedControlSha256=$run.controlSha256;loaderCapture=$loader;loaderAdmission=$loaderAdmission;privateFailures=$privateFailures.ToArray()}}
  else{
   # The separate private controller area is outside all native-writable watched
   # directories. Persist bounded diagnostics without reading/trimming the run.
   $diagnostic=Join-Path $spec.labRoot ('secrets\editor-route-'+$spec.jobId+'-unverified.json')
   Write-AzuriteFlushedJson $diagnostic @{summary=$safe;errorType=$errorType;reason=$reason;cleanup=$outer;expectedControlSha256=$run.controlSha256;privateFailures=$privateFailures.ToArray();runEvidenceUntouched=$true}
   Set-AzuriteLabAcl $spec.labRoot $diagnostic $context.sid
  }
  if($passed -and $stateVerified){Complete-AzuriteCleanupGate $spec.labRoot $spec.jobId $context.sid $outer}
  return [pscustomobject]$safe
 }finally{if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
Export-ModuleMember -Function *-AzuriteEditorRoute*
