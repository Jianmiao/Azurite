Set-StrictMode -Version Latest
# No work occurs on import. All mutations belong to separately approved runtime calls.
$script:RetainedFullRuntimeLeases=[Collections.Generic.List[object]]::new()
function ConvertFrom-AzuriteFullJson([string]$Text) {
 $options=@{InputObject=$Text;ErrorAction='Stop'}
 if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$options.DateKind='String'}
 return ConvertFrom-Json @options
}
function Read-AzuriteFullBoundedJson([string]$Path,[long]$MaxBytes=1048576) {
 $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
 if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -lt 2 -or $item.Length -gt $MaxBytes){throw 'Missing, linked or oversized full-mode JSON.'}
 $bytes=[IO.File]::ReadAllBytes($Path);if($bytes.Length -gt $MaxBytes){throw 'Full JSON grew past its bound.'}
 return ConvertFrom-AzuriteFullJson ([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
}
function ConvertTo-AzuriteFullFileIdentity([string]$Identity) {
 if($Identity -cmatch '^([a-f0-9]{8}):([a-f0-9]{16})$'){return $matches[1]+'00000000'+$matches[2]}
 if($Identity -cmatch '^[a-f0-9]{8}00000000[a-f0-9]{16}$'){return $Identity}
 throw 'Full file identity must be exact volume32/file64 or canonical padded 32-hex.'
}
function Get-AzuriteFullLogEvidence([string]$Path) {
 $record=[Azurite.Restricted.FullFileEvidence]::Read($Path);$record['identity']=ConvertTo-AzuriteFullFileIdentity $record['identity'];return $record
}
function Read-AzuriteFullApproval([string]$Path,[string]$ExpectedSha256) {
 $item=Get-Item -LiteralPath $Path -Force
 if($ExpectedSha256 -cnotmatch '^[a-f0-9]{64}$' -or $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -lt 2 -or $item.Length -gt 16384 -or (Get-AzuriteSha256 $Path).ToLowerInvariant() -cne $ExpectedSha256){throw 'Positive approval bytes differ from external digest or bound.'}
 $bytes=[IO.File]::ReadAllBytes($Path);return [pscustomobject]@{bytes=$bytes.Length;content=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)}
}
function Read-AzuriteFullSourceManifest([string]$Path,$Gate,$Plan,$Control) {
 $full=Assert-AzuriteFullPath $Path
 if(-not $full.StartsWith('C:\',[StringComparison]::Ordinal) -or (Get-AzuriteSha256 $full).ToLowerInvariant() -cne $Gate.transitionSourceManifestSha256){throw 'Runtime controller source manifest is not the approved C input.'}
 $manifest=Read-AzuriteFullBoundedJson $full 1048576
 $names=@('facts.json','plan.json','expected-acl-manifest.json','identity-baseline.json','scope.json','before.json','FullRestrictedStageFileSecurity.dll','helper-build.json','RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1','FullRestrictedCapabilitiesPlan.psm1','tooling-manifest.json')
 if($manifest.schema -ne 1 -or $manifest.scope -cne 'controller-full-capabilities-preapproval-sources' -or $manifest.runId -cne $Plan.RunId -or @($manifest.files).Count -ne $names.Count){throw 'Runtime source manifest shape differs.'}
 $map=@{};foreach($entry in $manifest.files){$p=Assert-AzuriteFullPath $entry.path;if($entry.name -cnotin $names -or $map.ContainsKey($entry.name) -or -not $p.StartsWith('C:\',[StringComparison]::Ordinal)){throw 'Unexpected controller source entry.'};$item=Get-Item -LiteralPath $p -Force;if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $entry.bytes -lt 1 -or $entry.bytes -gt 67108864 -or $item.Length -ne $entry.bytes -or (Get-AzuriteSha256 $p).ToLowerInvariant() -cne $entry.sha256){throw 'Controller source bytes differ.'};$map[$entry.name]=$entry}
 foreach($name in Get-AzuriteFullToolNames){if($map[$name].sha256 -cne $Control.toolHashes.$name){throw 'Controller source/tooling binding differs.'}}
 foreach($name in @('FullRestrictedCapabilitiesPlan.psm1','FullRestrictedCapabilitiesRuntime.psm1')){if((Get-AzuriteSha256 (Join-Path $PSScriptRoot $name)).ToLowerInvariant() -cne $map[$name].sha256){throw 'Executed controller module differs from approved source.'}}
 if($map['FullRestrictedStageFileSecurity.dll'].sha256 -cne $Gate.transitionHelperSha256 -or $map['tooling-manifest.json'].sha256 -cne $Gate.toolingManifestSha256 -or $map['expected-acl-manifest.json'].sha256 -cne $Gate.expectedAclManifestSha256 -or $map['before.json'].sha256 -cne $Control.sharedStateBeforeSha256){throw 'Controller helper/state/ACL digest differs from approved intent.'}
 $build=Read-AzuriteFullBoundedJson $map['helper-build.json'].path 131072
 if($build.schema -ne 1 -or $build.scope -cne 'controller-file-security-helper-build' -or $build.exitCode -ne 0 -or $build.assemblySha256 -cne $Gate.transitionHelperSha256 -or $build.sourceSha256 -cnotmatch '^[a-f0-9]{64}$' -or $build.moduleSha256 -cne (Get-AzuriteSha256 (Join-Path $PSScriptRoot 'FullRestrictedStageTransition.psm1')).ToLowerInvariant()){throw 'Controller metadata helper provenance differs.'}
 return $map
}
function Initialize-AzuriteFullPinnedHelpers([string]$ToolDirectory,$Control,$SourceMap) {
 $native=Join-Path $ToolDirectory 'RestrictedCliNative.dll';$type='Azurite.Restricted.ProcessPump' -as [type]
 if($type){if((Get-AzuriteSha256 $type.Assembly.Location).ToLowerInvariant() -cne $Control.toolHashes.'RestrictedCliNative.dll' -or -not $type.GetMethod('RunBootstrapFullRestricted')){throw 'A different native helper type is already loaded.'}}else{Add-Type -Path $native}
 $security=$SourceMap['FullRestrictedStageFileSecurity.dll'];$loaded='Azurite.Stage.FileSecurity' -as [type]
 if($loaded){if((Get-AzuriteSha256 $loaded.Assembly.Location).ToLowerInvariant() -cne $security.sha256){throw 'A different metadata helper type is already loaded.'}}else{Add-Type -Path $security.path}
}
function Assert-AzuriteFullPendingRecord($Record,$Plan,$Control,$Proof,[string]$State,[bool]$LiveLeaseHeld) {
 if(-not $LiveLeaseHeld -or $Proof.preparationLeaseReleased -ne $true -or $Proof.requiresFreshRuntimeLease -ne $true){throw 'Fresh held runtime lease and successful preparation release are required.'}
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId)
 if($State -cnotin @('prepared-awaiting-runtime-lease','runtime-pending') -or $Record.schema -ne 1 -or $Record.scope -cne 'full-capabilities-global-pending' -or $Record.runId -cne $Plan.RunId -or $Record.planSha256 -cne $Plan.PlanSha256 -or $Record.controllerSid -cne $Control.controllerSid -or $Record.runDirectory -cne $run -or $Record.state -cne $State){throw 'Global pending state belongs to another or uncertain run; explicit recovery is required.'}
}
function Get-AzuriteFullPending([string]$LabRoot) {
 if($LabRoot -cne 'F:\AzuriteLabs\Editor-20261108-normal'){throw 'Full pending boundary differs.'}
 return Join-Path $LabRoot 'runs\full-capabilities.pending.json'
}
function Read-AzuriteFullPending($Plan,$Control,$Proof,[string]$State,[bool]$LiveLeaseHeld) {
 Assert-AzuriteCleanupGateClear $Plan.LabRoot
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId)
 if(Test-Path -LiteralPath (Join-Path $run 'history\transition-quarantine.json')){throw 'Unresolved physical transition quarantine blocks runtime.'}
 $path=Get-AzuriteFullPending $Plan.LabRoot;Assert-AzuritePrivateAcl $path $Control.controllerSid
 $evidence=[Azurite.Stage.FileSecurity]::Read($path,$true)
 if($evidence.directory -or $evidence.bytes -lt 2 -or $evidence.bytes -gt 16384 -or $evidence.owner -cne $Control.controllerSid -or $evidence.volume -cne $Proof.volumeGuid){throw 'Global pending object identity/size/owner/volume differs.'}
 $record=ConvertFrom-AzuriteFullJson ([Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($evidence.content)))
 Assert-AzuriteFullPendingRecord $record $Plan $Control $Proof $State $LiveLeaseHeld
 return [pscustomobject]@{path=$path;record=$record;evidence=$evidence}
}
function Start-AzuriteFullRuntimePending($Plan,$Control,$Proof,[bool]$LiveLeaseHeld) {
 $pending=Read-AzuriteFullPending $Plan $Control $Proof 'prepared-awaiting-runtime-lease' $LiveLeaseHeld
 $record=$pending.record;$record.state='runtime-pending';$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($record|ConvertTo-Json -Depth 10 -Compress))
 [Azurite.Stage.FileSecurity]::WriteExisting($pending.path,$pending.evidence.identity,$pending.evidence.sddl,$pending.evidence.sha256,$bytes)
 $after=Read-AzuriteFullPending $Plan $Control $Proof 'runtime-pending' $LiveLeaseHeld
 if($after.evidence.identity -cne $pending.evidence.identity){throw 'Pending update changed object identity.'}
 return $after
}
function Assert-AzuriteFullCleanupAcceptance($Plan,$Cleanup,$Comparison) {
 Assert-AzuriteCleanupConfirmed $Cleanup;Assert-AzuriteFullZeroSharedDelta $Comparison
 if($Cleanup.RunId -cne $Plan.RunId -or $Comparison.runId -cne $Plan.RunId -or $Cleanup.FullJobObserverClosed -ne $true -or $Cleanup.FullJobObserverRetained -ne $false -or $Cleanup.DesktopLeaseClosed -ne $true -or $Cleanup.DesktopLeaseRetained -ne $false -or $Cleanup.StreamCaptureComplete -ne $true){throw 'Full ownership release is not verified.'}
}
function Complete-AzuriteFullRuntimePending($Plan,$Control,$Proof,$Cleanup,$Comparison,[bool]$LiveLeaseHeld) {
 Assert-AzuriteFullCleanupAcceptance $Plan $Cleanup $Comparison
 $pending=Read-AzuriteFullPending $Plan $Control $Proof 'runtime-pending' $LiveLeaseHeld
 [Azurite.Stage.FileSecurity]::DeleteExisting($pending.path,$pending.evidence.identity,$pending.evidence.sddl,$pending.evidence.sha256)
 if(Test-Path -LiteralPath $pending.path){throw 'Global pending deletion is unverified.'}
}
function Invoke-AzuriteFullLeaseFinalizer([scriptblock]$Backend,$State) {
 if($State.Held){
  $State.ReleaseAttempted=$true;$releasedNow=$false
  try{$null=& $Backend Release $State;$State.ReleaseSucceeded=$true;$State.Held=$false;$releasedNow=$true}
  catch{
   $State.ReleaseError=$_.Exception.ToString();$State.OverallPassed=$false
   try{$null=& $Backend Retain $State;$State.OwnerRetained=$true}catch{$State.RetentionError=$_.Exception.ToString()}
   if($State.Held -eq $true){try{$State.Compensation=& $Backend RestorePendingCreateNew $State}catch{$State.CompensationError=$_.Exception.ToString()}}
  }
  if($releasedNow){
   $State.DisposeAttempted=$true
   try{$null=& $Backend Dispose $State;$State.DisposeSucceeded=$true}
   catch{
    $State.DisposeError=$_.Exception.ToString();$State.OverallPassed=$false
    # Retaining an object reference after Release does not retain exclusion.
    try{$null=& $Backend Retain $State;$State.OwnerRetained=$true}catch{$State.RetentionError=$_.Exception.ToString()}
   }
  }
 }
 try{$null=& $Backend Persist $State}catch{$State.EvidenceError=$_.Exception.ToString();$State.OverallPassed=$false}
 return $State
}
function Invoke-AzuriteFullPendingCompensation([scriptblock]$Backend,$State) {
 if($State.Held -ne $true){throw 'Pending compensation requires established held runtime exclusion.'}
 $path=Get-AzuriteFullPending $State.Plan.LabRoot
 if(& $Backend Exists $State $null){return [pscustomobject]@{disposition='existing-marker-retained';overwritten=$false;path=$path}}
 if(-not $State.PendingEvidence){throw 'Approved pending identity/descriptor unavailable for CreateNew compensation.'}
 $record=$State.PendingEvidence.record|ConvertTo-Json -Depth 10|ConvertFrom-Json;$record.state='runtime-mutex-release-failed';$record|Add-Member releaseError $State.ReleaseError
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($record|ConvertTo-Json -Depth 12 -Compress))
 $null=& $Backend CreateNew $State ([pscustomobject]@{path=$path;bytes=$bytes;sddl=$State.PendingEvidence.evidence.sddl})
 return [pscustomobject]@{disposition='failure-marker-created-new';overwritten=$false;path=$path}
}
function New-AzuriteFullLeaseBackend {
 return {param($Action,$State)
  switch($Action){
   'Release' {$State.Mutex.ReleaseMutex();return}
   'Dispose' {$State.Mutex.Dispose();return}
   'Retain' {$script:RetainedFullRuntimeLeases.Add([pscustomobject]@{mutex=$State.Mutex;runId=$State.Plan.RunId;controllerSid=$State.Control.controllerSid;leaseHeld=$State.Held;releaseError=$State.ReleaseError;disposeError=$State.DisposeError});return}
   'RestorePendingCreateNew' {
    return Invoke-AzuriteFullPendingCompensation {param($Action,$State,$Request)if($Action -ceq 'Exists'){return Test-Path -LiteralPath (Get-AzuriteFullPending $State.Plan.LabRoot)};if($Action -cne 'CreateNew'){throw 'Unexpected compensation operation.'};[Azurite.Stage.FileSecurity]::NewFile($Request.path,$Request.bytes,$Request.sddl)} $State
   }
   'Persist' {
    $record=[ordered]@{schema=1;scope='full-runtime-mutex-release';runId=$State.Plan.RunId;releaseAttempted=$State.ReleaseAttempted;releaseSucceeded=$State.ReleaseSucceeded;releaseError=$State.ReleaseError;disposeAttempted=$State.DisposeAttempted;disposeSucceeded=$State.DisposeSucceeded;disposeError=$State.DisposeError;leaseHeld=$State.Held;ownerRetained=$State.OwnerRetained;retentionError=$State.RetentionError;pendingClearedBeforeRelease=$State.PendingCleared;compensation=$State.Compensation;compensationError=$State.CompensationError;primaryError=$State.PrimaryError;overallPassed=$State.OverallPassed}
    Write-AzuriteFlushedJson (Join-Path $State.EvidenceDirectory 'runtime-lease.json') $record;return
   }
   default {throw 'Unknown full lease finalizer action.'}
  }
 }
}
function Assert-AzuriteFullOriginalIdentity($Identity,$Control,$Facts) {
 if($Identity.status -cne 'passed' -or $Identity.identitySid -cne $Control.controllerSid -or $Identity.isAdministrator -ne $false -or $Identity.hiveLoaded -ne $true -or $Identity.token.logon_sid -cne $Facts.acl.actualLogonSid -or $Identity.token.user_sid -cne $Control.controllerSid -or $Identity.token.is_restricted -ne $false -or $Identity.token.elevated -ne $false -or $Identity.token.is_app_container -ne $false -or $Identity.token.users_enabled -ne $true -or @($Identity.unexpectedPrivileges).Count -ne 0){throw 'Fresh original identity/Logon/privilege facts differ.'}
}
function Get-AzuriteFullRuntimeBudget($Control,[int]$RequestedSeconds,[long]$ElapsedMilliseconds,[datetime]$NowUtc) {
 if(($Control.runtimeTimeoutSeconds -isnot [int] -and $Control.runtimeTimeoutSeconds -isnot [long]) -or $Control.runtimeTimeoutSeconds -lt 10 -or $Control.runtimeTimeoutSeconds -gt 300 -or $RequestedSeconds -ne $Control.runtimeTimeoutSeconds -or $ElapsedMilliseconds -lt 0 -or $Control.PSObject.Properties.Name -ccontains 'deadlineUtc'){throw 'Pinned bounded runtime duration differs from public timeout or legacy absolute deadline remains.'}
 $expires=ConvertTo-AzuriteUtcInstant $Control.intentValidUntilUtc
 if($NowUtc.Kind -ne [DateTimeKind]::Utc -or $expires -le $NowUtc){throw 'Runtime intent expired before launch.'}
 $remaining=[long]$Control.runtimeTimeoutSeconds*1000-$ElapsedMilliseconds
 $remaining=[Math]::Min($remaining,[long][Math]::Floor(($expires-$NowUtc).TotalMilliseconds))
 if($remaining -lt 1 -or $remaining -gt 300000){throw 'Fresh monotonic runtime budget exhausted or oversized.'}
 return [int]$remaining
}
function Assert-AzuriteFullZeroSharedDelta($Comparison) {
 if(-not $Comparison -or $Comparison.schema -ne 1 -or $Comparison.runId -cnotmatch '^[a-f0-9]{32}$' -or $Comparison.accepted -ne $true -or $Comparison.userPreferencesUnchanged -ne $true -or $Comparison.finalStateUnchanged -ne $true -or $Comparison.registryChanged -ne $false -or @($Comparison.unexpected).Count -ne 0 -or @($Comparison.deltas).Count -ne 0 -or @($Comparison.engineMetadata).Count -ne 0 -or $Comparison.restorationPerformed -ne $false){throw 'Full restriction requires zero shared delta, including legacy engine session values.'}
}
function Assert-AzuritePrecreatedLogEvidence($Before,$After,[string]$RunId,[int]$Pid,[datetime]$StartedUtc,[datetime]$EndedUtc,[bool]$CleanupVerified) {
 if(-not $CleanupVerified -or $Pid -le 0 -or $Before.runId -cne $RunId -or $After.runId -cne $RunId -or $Before.bytes -ne 0 -or $Before.sha256 -cne 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855' -or $Before.identity -cne $After.identity -or $Before.path -cne $After.path -or $After.bytes -le 0 -or $After.bytes -gt 4194304 -or $After.sha256 -ceq $Before.sha256){throw 'Precreated log identity/empty baseline/content/drain proof failed.'}
 $write=ConvertTo-AzuriteUtcInstant $After.lastWriteUtc
 if($write -lt $StartedUtc -or $write -gt $EndedUtc.AddSeconds(2) -or $After.controllerCorrelatedPid -ne $Pid -or $After.correlation -cne 'owned-controller-native-time-window'){throw 'Precreated log lacks bounded outer-controller correlation.'}
 # No claim of an embedded native PID/run marker is made.
}
function Assert-AzuriteFullEnvironment($Environment) {
 foreach($key in @($Environment.Keys)){if([string]$key -match '^(BEPINEX_GAME_ASSEMBLY_PATH$|DOORSTOP_|CORECLR_|COR_|COMPlus_|DOTNET_STARTUP_HOOKS$|DOTNET_ADDITIONAL_DEPS$|DOTNET_SHARED_STORE$|UNITY_DISABLE_AUDIO$|UNITY_BOOT_CONFIG$)' -and -not [string]::IsNullOrEmpty([string]$Environment[$key])){throw "Unbound startup override in full-restricted branch: $key"}}
}
function Assert-AzuriteFullRuntimeGate($Gate,$Plan,$ApprovalContent,[string]$RunId,[string]$GateSha256) {
 if(-not $Gate -or $Gate.schema -ne 2 -or $Gate.scope -cne 'preapproval-full-restricted-capabilities-intent' -or $Gate.runId -cne $RunId -or $Gate.planSha256 -cne $Plan.PlanSha256 -or $Gate.contractSha256 -cne $Plan.ContractSha256 -or $GateSha256 -cnotmatch '^[a-f0-9]{64}$' -or $ApprovalContent.controlGateSha256 -cne $GateSha256 -or $ApprovalContent.toolingManifestSha256 -cne $Gate.toolingManifestSha256 -or $Gate.stageRuntimeAllowed -ne $false -or $Gate.operation -cne 'capabilities'){throw 'Missing exact forward-only approved runtime intent.'}
 foreach($name in @('controlSha256','toolingManifestSha256','expectedAclManifestSha256','harmlessLifecycleReceiptSha256','transitionSourceManifestSha256','transitionHelperSha256')){if($Gate.$name -cnotmatch '^[a-f0-9]{64}$'){throw "Missing gate pin: $name"}}
 foreach($name in @('reviewApprovalSha256','transitionReceiptSha256','aa0Observed','evidence')){if($Gate.PSObject.Properties.Name -ccontains $name){throw 'Preapproval gate must not contain approval/posttransition cycles or a planned AA0 claim.'}}
 $now=[datetime]::UtcNow;if($now -lt (ConvertTo-AzuriteUtcInstant $ApprovalContent.validFromUtc) -or $now -gt (ConvertTo-AzuriteUtcInstant $ApprovalContent.validUntilUtc)){throw 'Positive approval is outside its live validity interval.'}
}
function Get-AzuriteFullToolNames {return @('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1')}
function Assert-AzuriteFullToolManifest($Manifest,$Control) {
 $names=Get-AzuriteFullToolNames
 if($Manifest.schema -ne 1 -or $Manifest.scope -cne 'full-restricted-precompiled-tooling' -or @($Manifest.files).Count -ne $names.Count -or @($Control.toolHashes.PSObject.Properties).Count -ne $names.Count){throw 'Full tooling manifest shape/count differs.'}
 $seen=@{};foreach($file in $Manifest.files){if($file.name -cnotin $names -or $seen.ContainsKey($file.name) -or $file.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $Control.toolHashes.($file.name) -cne $file.sha256){throw 'Full tooling file set or control pin differs.'};$seen[$file.name]=$true}
 $build=$Manifest.build
 if($build.target -cne 'library' -or $build.platform -cne 'x64' -or $build.exitCode -ne 0 -or @($build.sources).Count -ne 2 -or $build.sources[0].name -cne 'RestrictedCliNative.cs' -or $build.sources[1].name -cne 'RestrictedCliFullRestricted.cs' -or $build.assemblySha256 -cne $Control.toolHashes.'RestrictedCliNative.dll' -or @($build.references).Count -ne 1 -or $build.references[0].name -cne 'System.Web.Extensions.dll'){throw 'Full helper compile provenance differs.'}
 foreach($source in $build.sources){if($source.sha256 -cne $Control.toolHashes.($source.name)){throw 'Helper source digest differs.'}}
 if($build.compilerSha256 -cnotmatch '^[a-f0-9]{64}$' -or $build.references[0].sha256 -cnotmatch '^[a-f0-9]{64}$'){throw 'Compiler/reference provenance is unpinned.'}
}
function Assert-AzuriteFullPreparedTooling([string]$ToolDirectory,$Control,[string]$ControlPath) {
 foreach($path in @($ToolDirectory,$ControlPath)){Assert-AzuritePrivateAcl $path $Control.controllerSid;if((Get-Acl -LiteralPath $path).GetOwner([Security.Principal.SecurityIdentifier]).Value -cne $Control.controllerSid){throw 'Prepared tooling owner differs from controller.'}}
 $manifestPath=Join-Path $ToolDirectory 'tooling-manifest.json';Assert-AzuritePrivateAcl $manifestPath $Control.controllerSid
 if((Get-AzuriteSha256 $manifestPath).ToLowerInvariant() -cne $Control.toolingManifestSha256){throw 'Tooling manifest digest differs from control.'}
 $manifest=Read-AzuriteFullBoundedJson $manifestPath 131072;Assert-AzuriteFullToolManifest $manifest $Control
 $framework=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319'
 if((Get-AzuriteSha256 (Join-Path $framework 'csc.exe')).ToLowerInvariant() -cne $manifest.build.compilerSha256 -or (Get-AzuriteSha256 (Join-Path $framework 'System.Web.Extensions.dll')).ToLowerInvariant() -cne $manifest.build.references[0].sha256){throw 'Framework compiler/reference provenance differs from reviewed build.'}
 foreach($file in $manifest.files){$path=Join-Path $ToolDirectory $file.name;Assert-AzuritePrivateAcl $path $Control.controllerSid;if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse tooling refused.'};if((Get-AzuriteSha256 $path).ToLowerInvariant() -cne $file.sha256){throw 'Prepared source/assembly hash differs.'}}
}
function Assert-AzuriteFullAclPolicy([string]$Sddl,[string]$OwnerSid,[string]$LogonSid,[string]$Role,[string]$Mask) {
 $sd=[Security.AccessControl.RawSecurityDescriptor]::new($Sddl)
 if($sd.Owner.Value -cne $OwnerSid -or -not($sd.ControlFlags -band [Security.AccessControl.ControlFlags]::DiscretionaryAclProtected) -or -not $sd.DiscretionaryAcl){throw 'Full object requires exact owner and protected nonnull DACL.'}
 $directory=$Role -cin @('install-directory','run-directory','request-directory','private-directory','ancestor-directory')
 $trusted=@($OwnerSid,'S-1-5-18','S-1-5-32-544');$baseSeen=@{};$principalSeen=0;$sealSeen=0;$inheritSeen=0
 foreach($ace in $sd.DiscretionaryAcl){
  if($ace -isnot [Security.AccessControl.CommonAce] -or $ace.IsCallback){throw 'Unsupported ACE in finite full descriptor.'}
  $sid=$ace.SecurityIdentifier.Value;$flags=[int]$ace.AceFlags;$rights=$ace.AccessMask
  if($ace.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessDenied){
   if($Role -cne 'install-directory' -or $sid -cne $OwnerSid -or $rights -ne 6 -or $flags -ne 0 -or $sealSeen){throw 'Unexpected creation deny or deny scope.'};$sealSeen++;continue
  }
  if($ace.AceQualifier -ne [Security.AccessControl.AceQualifier]::AccessAllowed){throw 'Unsupported ACL qualifier.'}
  if($sid -cin $trusted){if($baseSeen.ContainsKey($sid) -or $rights -ne 0x1f01ff -or $flags -ne $(if($directory){3}else{0})){throw 'Private controller/SYSTEM/admin baseline changed.'};$baseSeen[$sid]=$true;continue}
  if($sid -cne $LogonSid -or $Role -cin @('private-file','private-directory')){throw 'Unexpected selected/shared principal grant.'}
  if($Role -ceq 'request-directory' -and $flags -eq 9 -and $rights -eq 0x13019f){if($inheritSeen){throw 'Duplicate inherited response grant.'};$inheritSeen++;continue}
  if($flags -ne 0 -or $rights -ne [Convert]::ToInt32($Mask.Substring(2),16) -or $principalSeen){throw 'Selected-principal access differs from exact noninheriting mask.'};$principalSeen++
 }
 if($baseSeen.Count -ne 3 -or $sealSeen -ne $(if($Role -ceq 'install-directory'){1}else{0}) -or $principalSeen -ne $(if($Role -cin @('private-file','private-directory')){0}else{1}) -or $inheritSeen -ne $(if($Role -ceq 'request-directory'){1}else{0})){throw 'Required baseline/seal/selected-principal ACE missing.'}
}
function Get-AzuriteFullExpectedRoles($Plan,$Control) {
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId);$install=Join-Path $Plan.LabRoot 'install';$roles=@{}
 foreach($entry in $Plan.Grants.files){$roles[(Join-Path $install $entry.path.Replace('/','\'))]=@('install-file',$entry.mask)}
 foreach($entry in $Plan.Grants.directories){if($entry.path -cmatch '^F:\\'){if($entry.path -cnotin @('F:\AzuriteLabs',$Plan.LabRoot)){throw 'Unexpected absolute ancestor grant.'};$path=$entry.path;$role='ancestor-directory'}else{$path=if($entry.path -ceq '.'){$install}else{Join-Path $install $entry.path.Replace('/','\')};$role='install-directory'};$roles[$path]=@($role,$entry.mask)}
 foreach($entry in $Plan.MutableLeaves){$path=if($entry.role -ceq 'FreshRunUnityLog'){Join-Path $run 'output\unity.log'}else{Join-Path $install $entry.path.Replace('/','\')};$roles[$path]=@('install-file','0x12019F')}
 foreach($path in @((Join-Path $Plan.LabRoot 'runs'),$run,(Join-Path $run 'output'),(Join-Path $run 'workspace'))){$roles[$path]=@('run-directory','0x1200A9')}
 $roles[(Join-Path $run 'request')]=@('request-directory','0x1200AB');$roles[(Join-Path $run 'request\arguments.json')]=@('arguments-file','0x120089')
 foreach($relative in @('tooling','history','history\transition','history\post-run','shared-state')){$roles[(Join-Path $run $relative)]=@('private-directory','0x0')}
 foreach($name in (Get-AzuriteFullToolNames)+@('control.json','tooling-manifest.json','expected-acl-manifest.json')){$roles[(Join-Path $run ('tooling\'+$name))]=@('private-file','0x0')}
 foreach($relative in @('history\transition\before-images-raw.json','history\before-images.json','history\acl-before-images.json','history\precreated-log-baselines.json','shared-state\scope.json','shared-state\before.json')){$roles[(Join-Path $run $relative)]=@('private-file','0x0')}
 $roles[(Join-Path $Plan.LabRoot 'runs\full-capabilities.pending.json')]=@('private-file','0x0')
 return $roles
}
function Assert-AzuriteFullExpectedAclManifest($Manifest,$Plan,$Control,[string]$LogonSid) {
 $history=@($Control.privateHistoryPaths);if($history.Count -ne 2 -or $history[0].path -cne 'transition' -or $history[0].kind -cne 'Directory' -or $history[1].path -cne 'transition/before-images-raw.json' -or $history[1].kind -cne 'File'){throw 'Control must declare only the exact private history extension.'}
 $roles=Get-AzuriteFullExpectedRoles $Plan $Control
 if($Manifest.schema -ne 1 -or $Manifest.scope -cne 'preapproval-exact-acl-intent' -or $Manifest.runId -cne $Plan.RunId -or $Manifest.planSha256 -cne $Plan.PlanSha256 -or $Manifest.ownerSid -cne $Control.controllerSid -or $Manifest.logonSid -cne $LogonSid -or @($Manifest.objects).Count -ne $roles.Count){throw 'Exact finite ACL manifest shape/coverage differs.'}
 $seen=@{};foreach($entry in $Manifest.objects){if(-not $roles.ContainsKey($entry.path) -or $seen.ContainsKey($entry.path)){throw 'Unexpected or duplicate ACL path.'};$seen[$entry.path]=$true;$role=$roles[$entry.path];if($entry.role -cne $role[0] -or $entry.mask -cne $role[1]){throw 'ACL path role/mask differs from planner.'};Assert-AzuriteFullAclPolicy $entry.sddl $Control.controllerSid $LogonSid $entry.role $entry.mask}
}
function Assert-AzuriteFullLiveAclManifest($Plan,$Control,$Facts) {
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId);$path=Join-Path $run 'tooling\expected-acl-manifest.json'
 if((Get-AzuriteSha256 $path).ToLowerInvariant() -cne $Control.expectedAclManifestSha256){throw 'Expected ACL intent digest differs.'}
 $manifest=Read-AzuriteFullBoundedJson $path 4194304;Assert-AzuriteFullExpectedAclManifest $manifest $Plan $Control $Facts.acl.actualLogonSid
 $volumes=@(Get-CimInstance Win32_Volume -Filter "DriveLetter='F:'");if($volumes.Count -ne 1 -or $volumes[0].DeviceID -cne $Facts.volumeGuid){throw 'Live destination volume differs from approval.'}
 # Finite manifest only; no recursive inventory or dependency expansion.
 $parents=@{};$observed=@{};foreach($entry in $manifest.objects){
  $item=[Azurite.Stage.FileSecurity]::Read($entry.path,$false);$observed[$entry.path]=$item
  $wantDirectory=$entry.role -clike '*directory';if($item.directory -ne $wantDirectory -or $item.reparse -or $item.owner -cne $Control.controllerSid -or $item.volume -cne $Facts.volumeGuid -or (-not $item.directory -and $item.links -ne 1)){throw 'Live object type/link/owner/volume differs.'}
  $actual=$item.sddl
  $wanted=[Security.AccessControl.RawSecurityDescriptor]::new($entry.sddl).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access')
  if($actual -cne $wanted){throw "Live ACL differs from reviewed exact descriptor: $($entry.path)"}
  $cursor=Split-Path -Parent $entry.path;while($cursor -and -not $parents.ContainsKey($cursor)){$parents[$cursor]=$true;$ancestor=Get-Item -LiteralPath $cursor -Force;if(-not $ancestor.PSIsContainer -or ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse/missing ACL ancestor refused.'};$cursor=Split-Path -Parent $cursor}
 }
 return $observed
}
function Get-AzuriteFullExpectedInventory($Plan,$Control,[ValidateSet('Prepared','BeforeNative','AfterNative','Complete')][string]$Phase) {
 $roles=Get-AzuriteFullExpectedRoles $Plan $Control;$expected=@{}
 foreach($path in $roles.Keys){if($roles[$path][0] -clike '*directory'){$expected[$path]=[Collections.Generic.List[string]]::new()}}
 foreach($path in $roles.Keys){$parent=Split-Path -Parent $path;if($expected.ContainsKey($parent)){$expected[$parent].Add([IO.Path]::GetFileName($path))}}
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId)
 $expected['F:\AzuriteLabs'].Add('.azurite-phase0-parent.json');$expected[$Plan.LabRoot].Add('lab.marker.json');$expected[$Plan.LabRoot].Add('.phase0-staging')
 if($Phase -cne 'Prepared'){foreach($name in @('prelaunch-current.json','prelaunch-delta.json')){$expected[(Join-Path $run 'shared-state')].Add($name)}}
 if($Phase -cin @('AfterNative','Complete')){
  foreach($name in @('bootstrap.stdout.log','bootstrap.stderr.log')){$expected[$run].Add($name)}
  foreach($name in @('identity.json','bootstrap-result.json','aa.stdout.log','aa.stderr.log')){$expected[(Join-Path $run 'output')].Add($name)}
  foreach($name in @('unity.log','loader.log')){$expected[(Join-Path $run 'history\post-run')].Add($name)}
  $expected[(Join-Path $run 'request')].Add('response.json')
 }
 if($Phase -ceq 'Complete'){$expected[$run].Add('controller-result.json');foreach($name in @('after.json','delta.json')){$expected[(Join-Path $run 'shared-state')].Add($name)}}
 return $expected
}
function Assert-AzuriteFullInventoryNames([string[]]$Actual,[string[]]$Expected) {
 if($Actual.Count -gt 2048 -or $Actual.Count -ne $Expected.Count -or (($Actual|Sort-Object -CaseSensitive) -join [char]10) -cne (($Expected|Sort-Object -CaseSensitive) -join [char]10)){throw 'Exact bounded inventory names/count/case differ.'}
}
function Assert-AzuriteFullLiveInventories($Plan,$Control,[string]$Phase) {
 $expected=Get-AzuriteFullExpectedInventory $Plan $Control $Phase
 foreach($path in $expected.Keys){$names=@([IO.Directory]::EnumerateFileSystemEntries($path)|ForEach-Object{[IO.Path]::GetFileName($_)});Assert-AzuriteFullInventoryNames $names $expected[$path].ToArray()}
}
function Assert-AzuriteFullStageAnchors($Contract,$Facts,$SourceMap) {
 $root=$Contract.labRoot;$attempt=Join-Path $root '.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9'
 $pins=@{'F:\AzuriteLabs\.azurite-phase0-parent.json'=$Contract.stage.parentMarkerSha256;(Join-Path $root 'lab.marker.json')=$Contract.stage.rootMarkerSha256;(Join-Path $root '.phase0-staging\install-creation-seal.json')=$Contract.stage.sealPolicySha256;(Join-Path $attempt 'complete.json')=$Contract.stage.completionSha256;(Join-Path $attempt 'files.jsonl')=$Contract.stage.journalSha256;(Join-Path $attempt 'start.json')=$Contract.stage.startReceiptSha256;(Join-Path $attempt 'seal-complete.json')=$Contract.stage.sealCompletionSha256}
 $baseline=Read-AzuriteFullBoundedJson $SourceMap['identity-baseline.json'].path 4194304;$objects=@{};foreach($entry in $baseline.objects){if($objects.ContainsKey($entry.path)){throw 'Duplicate original identity baseline.'};$objects[$entry.path]=$entry}
 foreach($path in $pins.Keys){$actual=[Azurite.Stage.FileSecurity]::Read($path,($path -clike '*marker.json'));if(-not $objects.ContainsKey($path) -or $actual.identity -cne $objects[$path].identity -or $actual.owner -cne $Facts.ownerSid -or $actual.volume -cne $Facts.volumeGuid -or $actual.sha256 -cne $pins[$path]){throw 'Historical phase-0 object identity or bytes changed.'}
  if($path -clike '*marker.json'){$marker=ConvertFrom-AzuriteFullJson ([Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($actual.content)));if($marker.runtimeAllowed -ne $false -or $marker.controllerSid -cne $Facts.controllerSid -or $marker.labRoot -cne $root -or $marker.manifestSha256 -cne $Facts.stage.manifestSha256){throw 'Stage marker identity/runtime permission changed.'}}
 }
 return $objects
}
function Assert-AzuriteFullWarmInputs($Contract) {
 Assert-AzuriteFullEnvironment ([Environment]::GetEnvironmentVariables())
 $proofPath=Join-Path $PSScriptRoot '..\..\artifacts\aa-fr-cache-prerequisites\warm-stamp-result.json'
 Assert-AzuritePreparedHash $proofPath $Contract.cachePrerequisites.warmResultSha256
 $warm=Read-AzuriteFullBoundedJson $proofPath 1048576;$root=$Contract.installRoot
 if(Test-Path -LiteralPath (Join-Path $root 'BepInEx\DeobfuscationMap.csv.gz')){throw 'Unapproved optional rename map appeared.'}
 $actual=@([IO.Directory]::EnumerateFiles((Join-Path $root 'BepInEx\unity-libs'),'*.dll',[IO.SearchOption]::TopDirectoryOnly)|ForEach-Object{[IO.Path]::GetFileName($_)})
 $expected=@($warm.input_events|Where-Object{$_.kind -ceq 'utf8_string' -and $_.value.EndsWith('.dll')}|ForEach-Object value)
 if($actual.Count -ne 72 -or ($actual -join [char]10) -cne ($expected -join [char]10)){throw 'Warm interop enumeration order changed.'}
}
function Assert-AzuriteFullPreparedIdentities($Plan,$Facts,$Observed,$Original,[bool]$After) {
 $install=Join-Path $Plan.LabRoot 'install'
 foreach($entry in @($Plan.Grants.files)+@($Plan.Grants.directories)){$path=if($entry.path -cmatch '^F:\\'){$entry.path}elseif($entry.path -ceq '.'){$install}else{Join-Path $install $entry.path.Replace('/','\')};if(-not $Original.ContainsKey($path) -or -not $Observed.ContainsKey($path)){throw 'Original identity baseline omits a declared install/ancestor object.'}}
 foreach($path in $Observed.Keys){if($Original.ContainsKey($path) -and $Observed[$path].identity -cne $Original[$path].identity){throw "Original object replaced: $path"}}
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId);$install=Join-Path $Plan.LabRoot 'install'
 $cache=Join-Path $install $Facts.cache.modTheLeaf.Replace('/','\')
 if($Observed[$cache].identity -cne $Facts.transition.cacheLeafFileIdentity -or $Observed[(Join-Path $run 'output\unity.log')].identity -cne $Facts.transition.unityLogFileIdentity){throw 'New leaf identity differs from transition receipt.'}
 if(-not $After -and ($Observed[$cache].sha256 -cne $Facts.cache.seedSha256 -or $Observed[$cache].bytes -ne 4 -or $Observed[(Join-Path $install 'BepInEx\cache\chainloader_typeloader.dat')].sha256 -cne $Facts.cache.chainloaderSha256)){throw 'Prepared exact cache seed/existing cache bytes changed before AA.'}
 foreach($leaf in $Plan.MutableLeaves){$path=if($leaf.role -ceq 'FreshRunUnityLog'){Join-Path $run 'output\unity.log'}else{Join-Path $install $leaf.path.Replace('/','\')};if($Observed[$path].bytes -gt 4194304){throw 'Named mutable leaf exceeds the output bound.'}}
}
function Copy-AzuriteFullPostDrainLog([string]$Source,[string]$Destination,$Expected) {
 if((Test-Path -LiteralPath $Destination) -or $Expected.bytes -lt 1 -or $Expected.bytes -gt 4194304){throw 'Post-drain history must be new and bounded.'}
 $input=$null;$output=$null
 try{
  $input=[IO.FileStream]::new($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  if($input.Length -ne $Expected.bytes){throw 'Log length changed before immutable capture.'}
  $output=[IO.FileStream]::new($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  $buffer=New-Object byte[] 65536;$total=0L
  while(($read=$input.Read($buffer,0,$buffer.Length)) -gt 0){$total+=$read;if($total -gt 4194304){throw 'Post-drain log exceeds bound.'};$output.Write($buffer,0,$read)}
  $output.Flush($true);$output.Dispose();$output=$null
  $after=Get-AzuriteFullLogEvidence $Source
  if($after['identity'] -cne $Expected.identity -or $after['sha256'] -cne $Expected.sha256 -or $total -ne $Expected.bytes -or (Get-AzuriteSha256 $Destination).ToLowerInvariant() -cne $Expected.sha256){throw 'Log identity/bytes changed while capturing immutable history.'}
  (Get-Item -LiteralPath $Destination).IsReadOnly=$true
  return [pscustomobject]@{path=$Destination;bytes=$total;sha256=$Expected.sha256;sourceIdentity=$Expected.identity;capturedAfterVerifiedDrain=$true}
 }finally{if($output){$output.Dispose()};if($input){$input.Dispose()}}
}
function Write-AzuriteFullCapturedText([string]$Path,[AllowEmptyString()][string]$Text) {
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text);if($bytes.Length -gt 1048576){throw 'Captured stream text exceeds UTF-8 file bound.'}
 $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
 try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Assert-AzuriteFullRequestInventory([string[]]$Names,[long[]]$Sizes) {
 if($Names.Count -ne $Sizes.Count -or $Names.Count -gt 8 -or @($Names|Where-Object{$_ -ceq 'arguments.json'}).Count -ne 1 -or @($Names|Where-Object{$_ -ceq 'response.json'}).Count -ne 1){throw 'Request inventory/count mismatch.'}
 for($i=0;$i -lt $Names.Count;$i++){if($Names[$i] -cnotin @('arguments.json','response.json') -or $Sizes[$i] -lt 1 -or $Sizes[$i] -gt 1048576){throw 'Unexpected response/temp leaf after drain; retain evidence.'}}
}
function Read-AzuriteFullPreparedControl([string]$ControlPath,$Gate,$Plan,$Proof) {
 $expected=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId+'\tooling\control.json')
 if($ControlPath -cne $expected){throw 'Full control path differs from exact admitted run.'}
 $control=Read-AzuriteFullBoundedJson $ControlPath 131072
 Assert-AzuriteFullControlBinding $control $Gate $Plan $Proof
 if((Get-AzuriteSha256 $ControlPath).ToLowerInvariant() -cne $Gate.controlSha256){throw 'Gate does not bind prepared control bytes.'}
 return $control
}
function Assert-AzuriteFullControlBinding($control,$Gate,$Plan,$Proof) {
 $names=@('schema','identityMode','runId','command','labRoot','planSha256','contractSha256','controllerSid','targetSid','controllerProfile','currentLocalLow','currentEnvironment','argumentsSha256','sharedStateBeforeSha256','toolingManifestSha256','expectedAclManifestSha256','privateHistoryPaths','toolHashes','runtimeTimeoutSeconds','intentValidUntilUtc')
 if(@($control.PSObject.Properties).Count -ne $names.Count -or @($control.PSObject.Properties.Name|Where-Object{$_ -cnotin $names}).Count){throw 'Exact full control field set differs.'}
 if($control.schema -ne 2 -or $control.identityMode -cne 'CurrentAccountFullRestrictedCapabilities' -or $control.runId -cne $Plan.RunId -or $control.command -cne 'capabilities' -or $control.labRoot -cne $Plan.LabRoot -or $control.planSha256 -cne $Plan.PlanSha256 -or $control.contractSha256 -cne $Plan.ContractSha256 -or $control.controllerSid -cne $Proof.controllerSid -or $control.targetSid -cne $Proof.controllerSid){throw 'Prepared full bootstrap control mismatch.'}
 if($control.toolingManifestSha256 -cne $Gate.toolingManifestSha256 -or $control.expectedAclManifestSha256 -cne $Gate.expectedAclManifestSha256){throw 'Control intent manifest bindings differ.'}
 foreach($field in @('gateSha256','reviewApprovalSha256','transitionReceiptSha256','admissionSha256')){if($control.PSObject.Properties.Name -ccontains $field){throw 'Control must not introduce a reverse approval/postreceipt hash link.'}}
 foreach($field in @('argumentsSha256','sharedStateBeforeSha256')){if($control.$field -cnotmatch '^[a-f0-9]{64}$'){throw 'Control transport/state bytes must be pinned.'}}
 $null=Get-AzuriteFullRuntimeBudget $control $control.runtimeTimeoutSeconds 0 ([datetime]::UtcNow)
}
function Assert-AzuriteFullReceiptBindings($Gate,$Plan,$Proof,$Control) {
 $path=Assert-AzuriteFullPath $Gate.harmlessLifecycleReceiptPath
 if((Get-AzuriteSha256 $path).ToLowerInvariant() -cne $Gate.harmlessLifecycleReceiptSha256){throw 'Reviewed harmless lifecycle receipt bytes differ.'}
 $receipt=Read-AzuriteFullBoundedJson $path 1048576
 if($receipt.schema -ne 1 -or $receipt.scope -cne 'actual-native-full-restricted-harmless-lifecycle' -or $receipt.passed -ne $true -or $receipt.productionTargetExecuted -ne $false -or $receipt.desktopLeaseAcrossBootstrapExit -ne $true -or $receipt.actualSuspendedTokenAndJob -ne $true -or $receipt.pipeHandleListAndEOF -ne $true -or $receipt.cleanupVerified -ne $true -or $receipt.nativeSourceSha256 -cne $Control.toolHashes.'RestrictedCliFullRestricted.cs' -or $receipt.sharedNativeSourceSha256 -cne $Control.toolHashes.'RestrictedCliNative.cs'){throw 'Same-source actual harmless lifecycle proof is incomplete.'}
 $history=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId+'\history')
 foreach($entry in @(@('before-images.json',$Proof.beforeImageInventoryHash),@('acl-before-images.json',$Proof.aclBeforeImageInventoryHash))){$file=Join-Path $history $entry[0];Assert-AzuritePrivateAcl $file $Proof.controllerSid;if((Get-AzuriteSha256 $file).ToLowerInvariant() -cne $entry[1]){throw 'Actual before-image inventory differs from posttransition proof.'}}
}
function Assert-AzuriteFullEnvelope($Contract,$Plan,[bool]$After,$Observed) {
 foreach($pin in $Plan.Grants.files){
  $path=Join-Path $Contract.installRoot $pin.path;$mutable=@($Plan.MutableLeaves|Where-Object{$_.path -ceq $pin.path})
  if($mutable.Count){if($pin.path -cin @('BepInEx/config/BepInEx.cfg','profiles/AzuriteAcceptance/configs/halocue.azurite.cfg')){$expected=$pin.sha256}else{if($Observed[$path].bytes -gt 4194304){throw 'Named mutable leaf exceeds bound.'};continue}}
  else{$expected=$pin.sha256}
  if(-not $Observed.ContainsKey($path) -or $Observed[$path].sha256 -cne ([string]$expected).ToLowerInvariant()){throw "Full immutable/final-config pin differs: $($pin.path)"}
 }
}
function Invoke-AzuriteFullCapabilitiesPreparedRun($Contract,$Plan,$Facts,$Gate,[string]$GateSha256,$Admission,$EvidenceBundle,$Approval,[string]$ExpectedApprovalSha256,[string]$TransitionSourceManifestPath,[string]$ToolDirectory,[int]$RuntimeTimeoutSeconds=120) {
 $evidenceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\aa-fr-capabilities-runtime'))
 if(-not $evidenceRoot.StartsWith('C:\',[StringComparison]::Ordinal)){throw 'Runtime controller evidence must stay in its C workspace.'}
 $evidence=Join-Path $evidenceRoot ([guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($evidence)
 $state=@{EvidenceDirectory=$evidence;Plan=$Plan;Control=$null;Mutex=$null;MutexCreated=$false;Held=$false;ReleaseAttempted=$false;ReleaseSucceeded=$false;ReleaseError=$null;DisposeAttempted=$false;DisposeSucceeded=$false;DisposeError=$null;OwnerRetained=$false;RetentionError=$null;Compensation=$null;CompensationError=$null;EvidenceError=$null;PrimaryError=$null;OverallPassed=$false;PendingCleared=$false;PendingEvidence=$null;Result=$null}
 $call=@{};foreach($key in $PSBoundParameters.Keys){$call[$key]=$PSBoundParameters[$key]};$call.RuntimeTimeoutSeconds=$RuntimeTimeoutSeconds;$call.Execution=$state
 try{$state.Result=Invoke-AzuriteFullCapabilitiesPreparedRunCore @call;$state.OverallPassed=($state.OverallPassed -and $state.Result.passed -eq $true -and $state.ReleaseSucceeded -and -not $state.ReleaseError -and -not $state.DisposeError -and -not $state.EvidenceError -and -not $state.PrimaryError)}
 catch{$state.PrimaryError=$_.Exception.ToString();$state.OverallPassed=$false}
 finally{
  $final=[ordered]@{schema=1;scope='full-runtime-final-outcome';runId=$Plan.RunId;passed=$state.OverallPassed;primaryError=$state.PrimaryError;runtimeLeaseReleased=$state.ReleaseSucceeded;releaseError=$state.ReleaseError;runtimeMutexDisposed=$state.DisposeSucceeded;disposeError=$state.DisposeError;leaseHeld=$state.Held;ownerRetained=$state.OwnerRetained;compensation=$state.Compensation;compensationError=$state.CompensationError;pendingCleared=$state.PendingCleared;result=$state.Result;runtimeAllowed=$false}
  Write-AzuriteFlushedJson (Join-Path $evidence 'controller-final.json') $final
 }
 if(-not $state.OverallPassed){throw ('Full capabilities refused/failed; final C evidence: '+$evidence+'; '+$state.PrimaryError)}
 return [pscustomobject]$final
}
function Invoke-AzuriteFullCapabilitiesPreparedRunCore($Contract,$Plan,$Facts,$Gate,[string]$GateSha256,$Admission,$EvidenceBundle,$Approval,[string]$ExpectedApprovalSha256,[string]$TransitionSourceManifestPath,[string]$ToolDirectory,[int]$RuntimeTimeoutSeconds,$Execution) {
 # No transition is applied here. An external reviewed controller transition must
 # already have produced all exact run objects/receipts; missing evidence refuses.
 if(-not $Plan.Ready -or $Plan.RuntimeAllowed -ne $false -or $Plan.WillLaunch -ne $false){throw 'Malformed dry planner result.'}
 $checked=Assert-AzuriteFullCapabilitiesAdmission -Contract $Contract -Plan $Plan -Facts $Facts -Admission $Admission -EvidenceBundle $EvidenceBundle -Approval $Approval -ExpectedApprovalSha256 $ExpectedApprovalSha256
 if(-not $checked.Valid){throw ('Independent runtime admission validation failed: '+($checked.Failures -join '; '))}
 $proof=$checked.Proof;$approvalContent=ConvertFrom-AzuriteFullJson $Approval.content;Assert-AzuriteFullRuntimeGate $Gate $Plan $approvalContent $Plan.RunId $GateSha256
 $run=Join-Path $Plan.LabRoot ('runs\restricted-'+$Plan.RunId);$controlPath=Join-Path $run 'tooling\control.json';$control=Read-AzuriteFullPreparedControl $controlPath $Gate $Plan $proof
 $Execution.Control=$control
 Assert-AzuriteFullPreparedTooling (Join-Path $run 'tooling') $control $controlPath
 Assert-AzuriteFullReceiptBindings $Gate $Plan $proof $control
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 if($sid -cne $proof.controllerSid -or ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Full capabilities requires exact unelevated original controller.'}
 $mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');$locked=$false;$ancestryLocks=[Collections.Generic.List[object]]::new()
 $Execution.Mutex=$mutex;$Execution.MutexCreated=$true
 try{
  try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true};if(-not $locked){throw 'Existing global restricted-CLI lock is held.'}
  $Execution.Held=$true
  $control=Read-AzuriteFullPreparedControl $controlPath $Gate $Plan $proof
  Assert-AzuriteFullRuntimeGate $Gate $Plan $approvalContent $Plan.RunId $GateSha256
  $sources=Read-AzuriteFullSourceManifest $TransitionSourceManifestPath $Gate $Plan $control
  Assert-AzuriteFullPreparedTooling (Join-Path $run 'tooling') $control $controlPath
  Assert-AzuriteFullReceiptBindings $Gate $Plan $proof $control
  Initialize-AzuriteFullPinnedHelpers (Join-Path $run 'tooling') $control $sources
  $roles=Get-AzuriteFullExpectedRoles $Plan $control;$lockPaths=@{};foreach($path in $roles.Keys){$cursor=if($roles[$path][0] -clike '*directory'){$path}else{Split-Path -Parent $path};while($cursor -and $cursor -cne 'F:\'){$lockPaths[$cursor]=$true;$cursor=Split-Path -Parent $cursor}}
  foreach($path in @($lockPaths.Keys|Sort-Object {$_.Length})){$ancestryLocks.Add([Azurite.Stage.FileSecurity]::LockDirectory($path))}
  $null=Read-AzuriteFullPending $Plan $control $proof 'prepared-awaiting-runtime-lease' $locked
  $currentIdentity=[pscustomobject]([Azurite.Restricted.FullBootstrapIdentity]::Read($Plan.RunId));Assert-AzuriteFullOriginalIdentity $currentIdentity $control $Facts
  $null=Assert-AzuriteCurrentIdentity $currentIdentity $control.controllerSid $control.controllerProfile $control.currentLocalLow $control.currentEnvironment
  Assert-AzuriteCleanupGateClear $Plan.LabRoot
  if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count){throw 'AA0 gate failed immediately before bootstrap.'}
  $observed=Assert-AzuriteFullLiveAclManifest $Plan $control $Facts;$original=Assert-AzuriteFullStageAnchors $Contract $Facts $sources
  Assert-AzuriteFullPreparedIdentities $Plan $Facts $observed $original $false
  Assert-AzuriteFullLiveInventories $Plan $control Prepared;Assert-AzuriteFullWarmInputs $Contract
  Assert-AzuriteFullEnvelope $Contract $Plan $false $observed
  foreach($entry in $control.toolHashes.PSObject.Properties){$source=Join-Path $ToolDirectory $entry.Name;$copy=Join-Path $run ('tooling\'+$entry.Name);if((Get-AzuriteSha256 $copy).ToLowerInvariant() -cne ([string]$entry.Value).ToLowerInvariant()){throw 'Prepared helper/tooling copy differs.'};if($entry.Name -cne 'RestrictedCliNative.dll' -and (Get-AzuriteSha256 $source).ToLowerInvariant() -cne ([string]$entry.Value).ToLowerInvariant()){throw 'Prepared source differs from reviewed controller tooling.'}}
  $output=Join-Path $run 'output';$request=Join-Path $run 'request';$stateDir=Join-Path $run 'shared-state';$scope=Read-AzuriteFullBoundedJson (Join-Path $stateDir 'scope.json');$before=Read-AzuriteFullBoundedJson (Join-Path $stateDir 'before.json') 67108864
  Assert-AzuritePreparedHash (Join-Path $stateDir 'before.json') $control.sharedStateBeforeSha256
  Assert-AzuritePreparedHash (Join-Path $stateDir 'scope.json') $sources['scope.json'].sha256
  $prelaunch=New-AzuriteSharedSnapshot $scope (Read-AzuritePlayerPrefsSnapshot) $Plan.RunId After
  $prelaunchDelta=Compare-AzuriteSharedSnapshots $before $prelaunch $scope $Plan.RunId;Assert-AzuriteFullZeroSharedDelta $prelaunchDelta
  $logBaselines=Read-AzuriteFullBoundedJson (Join-Path $run 'history\precreated-log-baselines.json') 32768
  Assert-AzuritePrivateAcl (Join-Path $run 'history\precreated-log-baselines.json') $sid
  if($logBaselines.runId -cne $Plan.RunId -or @($logBaselines.logs).Count -ne 2 -or @($logBaselines.logs|Where-Object{$_.path -ceq (Join-Path $run 'output\unity.log') -and $_.identity -ceq $Facts.transition.unityLogFileIdentity}).Count -ne 1){throw 'Precreated log baseline must match the actual transition identity.'}
  $beforeImages=Read-AzuriteFullBoundedJson (Join-Path $run 'history\before-images.json') 1048576
  $loaderBefore=@($beforeImages|Where-Object{$_.path -ceq 'BepInEx/LogOutput.log'})
  $loaderBaseline=@($logBaselines.logs|Where-Object{$_.path -ceq (Join-Path $Contract.installRoot 'BepInEx\LogOutput.log')})
  if($loaderBefore.Count -ne 1 -or $loaderBaseline.Count -ne 1 -or $loaderBefore[0].fileIdentity -cne $loaderBaseline[0].identity){throw 'Loader log identity differs from the hash-bound before-image inventory.'}
  foreach($baseline in $logBaselines.logs){$now=Get-AzuriteFullLogEvidence $baseline.path;if($now['identity'] -cne $baseline.identity -or $now['bytes'] -ne 0 -or $now['sha256'] -cne $baseline.sha256){throw 'Precreated log baseline changed before start.'}}
  $null=Get-AzuriteFullRuntimeBudget $control $RuntimeTimeoutSeconds 0 ([datetime]::UtcNow)
  if((ConvertTo-AzuriteUtcInstant $control.intentValidUntilUtc) -gt (ConvertTo-AzuriteUtcInstant $approvalContent.validUntilUtc)){throw 'Control intent outlasts positive approval.'}
  $pending=Start-AzuriteFullRuntimePending $Plan $control $proof $locked;$quarantine=$pending.path
  $Execution.PendingEvidence=$pending
  Write-AzuriteFlushedJson (Join-Path $stateDir 'prelaunch-current.json') $prelaunch;Write-AzuriteFlushedJson (Join-Path $stateDir 'prelaunch-delta.json') $prelaunchDelta
  Assert-AzuriteFullLiveInventories $Plan $control BeforeNative
  if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count){throw 'Fresh final AA0 gate failed.'}
  $shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
  $psi=New-AzuriteProcessInfo $shell @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $run 'tooling\RestrictedCliBootstrap.ps1'),'-ControlPath',$controlPath,'-ReviewedControlSha256',$Gate.controlSha256) $run
  $remaining=Get-AzuriteFullRuntimeBudget $control $RuntimeTimeoutSeconds 0 ([datetime]::UtcNow)
  $outer=[Azurite.Restricted.ProcessPump]::RunBootstrapFullRestricted($psi,$remaining,$Plan.RunId,$Gate.controlSha256,262144,@($output,$request),4194304)
  Write-AzuriteFullCapturedText (Join-Path $run 'bootstrap.stdout.log') $outer.Stdout;Write-AzuriteFullCapturedText (Join-Path $run 'bootstrap.stderr.log') $outer.Stderr
  $passed=$false;$reason=$null;$delta=$null;$native=$null;$logs=@();$stateVerified=$false
  try{
   Assert-AzuriteCleanupConfirmed $outer;$status=Get-AzuriteOuterProcessStatus $outer;if($status.failed){throw $status.reason}
   $native=Read-AzuriteFullBoundedJson (Join-Path $output 'bootstrap-result.json') 1048576
   if($native.schema -ne 2 -or $native.status -cne 'passed' -or $native.runId -cne $Plan.RunId -or $native.bootstrapPid -ne $outer.Pid -or $native.command -cne 'capabilities' -or $native.targetSid -cne $sid -or $native.planSha256 -cne $Plan.PlanSha256 -or $native.contractSha256 -cne $Plan.ContractSha256 -or $native.aaExitCode -ne 0 -or $native.restrictedNative.NativeHandlesClosed -ne $true -or $native.restrictedNative.StreamsComplete -ne $true -or $native.restrictedNative.Evidence.actualTokenVerified -ne $true -or $native.restrictedNative.Evidence.exactOwnedJobVerified -ne $true){throw 'Full bootstrap correlation/native evidence failed.'}
   $started=ConvertTo-AzuriteUtcInstant $native.aaStartedUtc;$ended=[datetime]::UtcNow
   $responsePath=Join-Path $request 'response.json';$responseObject=[Azurite.Stage.FileSecurity]::Read($responsePath,$false)
   if($responseObject.bytes -gt 1048576 -or $responseObject.links -ne 1 -or $responseObject.directory -or $responseObject.reparse -or $responseObject.owner -cne $sid -or $responseObject.volume -cne $Facts.volumeGuid){throw 'Response object escaped its exact bounded identity/volume role.'}
   $response=Read-AzuriteCliResponse $responsePath capabilities $started;if($response.Sha256 -cne $native.responseSha256 -or $response.Sha256.ToLowerInvariant() -cne $responseObject.sha256){throw 'Capability response changed after bootstrap.'}
   $items=@(Get-ChildItem -LiteralPath $request -Force);if(@($items|Where-Object{$_.PSIsContainer}).Count){throw 'Unexpected request directory.'};Assert-AzuriteFullRequestInventory @($items.Name) @($items.Length)
   Assert-AzuritePreparedHash (Join-Path $request 'arguments.json') $control.argumentsSha256
   foreach($baseline in $logBaselines.logs){$current=[pscustomobject](Get-AzuriteFullLogEvidence $baseline.path);$current|Add-Member runId $Plan.RunId;$current|Add-Member controllerCorrelatedPid $native.aaPid;$current|Add-Member correlation 'owned-controller-native-time-window';Assert-AzuritePrecreatedLogEvidence $baseline $current $Plan.RunId $native.aaPid $started $ended $true;$name=if($baseline.path -ceq (Join-Path $run 'output\unity.log')){'unity.log'}else{'loader.log'};$copy=Copy-AzuriteFullPostDrainLog $baseline.path (Join-Path $run ('history\post-run\'+$name)) $current;$current|Add-Member immutableCapture $copy;$logs+=@($current)}
   $finalObserved=Assert-AzuriteFullLiveAclManifest $Plan $control $Facts;Assert-AzuriteFullPreparedIdentities $Plan $Facts $finalObserved $original $true
   $null=Assert-AzuriteFullStageAnchors $Contract $Facts $sources;Assert-AzuriteFullLiveInventories $Plan $control AfterNative;Assert-AzuriteFullEnvelope $Contract $Plan $true $finalObserved;$passed=$true
  }catch{$reason=$_.Exception.ToString()}
  if($outer.CleanupVerified){try{$after=New-AzuriteSharedSnapshot $scope (Read-AzuritePlayerPrefsSnapshot) $Plan.RunId After;Write-AzuriteFlushedJson (Join-Path $stateDir 'after.json') $after;$delta=Compare-AzuriteSharedSnapshots $before $after $scope $Plan.RunId;Write-AzuriteFlushedJson (Join-Path $stateDir 'delta.json') $delta;Assert-AzuriteFullZeroSharedDelta $delta;$stateVerified=$true}catch{$passed=$false;$reason+=' Zero-shared-state verification failed: '+$_.Exception.Message}}
  $result=[ordered]@{schema=2;identityMode='CurrentAccountFullRestrictedCapabilities';runId=$Plan.RunId;passed=$passed;reason=$reason;aaAdmission=$false;outer=$outer;native=$native;precreatedLogs=$logs;zeroSharedDelta=$stateVerified;quarantinePath=$quarantine;restorationPerformed=$false}
  $Execution.Result=[pscustomobject]$result;$Execution.OverallPassed=$passed
  Write-AzuriteFlushedJson (Join-Path $run 'controller-result.json') $result
  if($outer.CleanupVerified -and $stateVerified -and $passed){Assert-AzuriteFullLiveInventories $Plan $control Complete;Complete-AzuriteFullRuntimePending $Plan $control $proof $outer $delta $locked;$Execution.PendingCleared=$true}
  if(-not $passed){throw "Full capabilities failed; evidence and quarantine retained: $run"};return [pscustomobject]$result
 }catch{$Execution.PrimaryError=$_.Exception.ToString();$Execution.OverallPassed=$false;throw}
 finally{
  foreach($handle in $ancestryLocks){try{$handle.Dispose()}catch{$Execution.PrimaryError+=' Directory-lock release: '+$_.Exception.ToString();$Execution.OverallPassed=$false}}
  if(-not $locked){try{$mutex.Dispose()}catch{$Execution.PrimaryError+=' Unacquired mutex close: '+$_.Exception.ToString();$Execution.OverallPassed=$false}}
  $null=Invoke-AzuriteFullLeaseFinalizer (New-AzuriteFullLeaseBackend) $Execution
  if($Execution.ReleaseError -or $Execution.DisposeError -or $Execution.EvidenceError){throw ('Runtime lease/evidence completion failed: '+$Execution.PrimaryError+' '+$Execution.ReleaseError+' '+$Execution.DisposeError+' '+$Execution.CompensationError+' '+$Execution.EvidenceError)}
 }
}
function Invoke-AzuriteFullRestrictedBootstrap($Control,[string]$ToolDirectory,[string]$ControlPath,[string]$ReviewedControlSha256,[Diagnostics.Stopwatch]$BootstrapClock) {
 # Called only by the existing bootstrap's explicit schema-2 branch.
 $run=Join-Path $Control.labRoot ('runs\restricted-'+$Control.runId)
 if($Control.identityMode -cne 'CurrentAccountFullRestrictedCapabilities' -or $Control.command -cne 'capabilities' -or $Control.labRoot -cne 'F:\AzuriteLabs\Editor-20261108-normal' -or $ToolDirectory -cne (Join-Path $run 'tooling') -or [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -cne $Control.controllerSid){throw 'Invalid fixed F/full-restricted bootstrap identity.'}
 if($ReviewedControlSha256 -cnotmatch '^[a-f0-9]{64}$' -or $ControlPath -cne (Join-Path $ToolDirectory 'control.json') -or (Get-AzuriteSha256 $ControlPath).ToLowerInvariant() -cne $ReviewedControlSha256){throw 'Full bootstrap control pin differs.'}
 Assert-AzuriteFullPreparedTooling $ToolDirectory $Control $ControlPath
 Add-Type -Path (Join-Path $ToolDirectory 'RestrictedCliNative.dll')
 $job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent();$exit=1;$output=Join-Path $run 'output';$workspace=Join-Path $run 'workspace';$request=Join-Path $run 'request'
 $r=[ordered]@{schema=2;identityMode=$Control.identityMode;runId=$Control.runId;command='capabilities';status='failed';bootstrapPid=$PID;targetSid=$Control.controllerSid;contractSha256=$Control.contractSha256;planSha256=$Control.planSha256;argumentsSha256=$Control.argumentsSha256;aaAdmission=$false;jobOwned=$true}
 try{
  if(-not $BootstrapClock -or -not $BootstrapClock.IsRunning){throw 'Fresh bootstrap monotonic clock is missing.'}
  $ackRemaining=Get-AzuriteFullRuntimeBudget $Control $Control.runtimeTimeoutSeconds $BootstrapClock.ElapsedMilliseconds ([datetime]::UtcNow)
  $job.AnnounceAndAwaitControllerFull($Control.runId,$ackRemaining)
  $identityFile=Join-Path $output 'identity.json';$captured=[Azurite.Restricted.FullBootstrapIdentity]::Read($Control.runId);Write-AzuriteFlushedJson $identityFile $captured
  $identity=Read-AzuriteFullBoundedJson $identityFile 32768;$r.profilePathProof=Assert-AzuriteCurrentIdentity $identity $Control.controllerSid $Control.controllerProfile $Control.currentLocalLow $Control.currentEnvironment
  Assert-AzuritePreparedHash (Join-Path $run 'shared-state\before.json') $Control.sharedStateBeforeSha256
  $envMap=@{};foreach($key in [Environment]::GetEnvironmentVariables('Process').Keys){$envMap[[string]$key]=[Environment]::GetEnvironmentVariable([string]$key,'Process')};Assert-AzuriteFullEnvironment $envMap
  $log=Join-Path $output 'unity.log';if(-not(Test-Path -LiteralPath $log -PathType Leaf) -or (Get-Item -LiteralPath $log).Length -ne 0 -or (Test-Path -LiteralPath (Join-Path $request 'response.json'))){throw 'Full branch requires exact empty precreated Unity log and absent response.'}
  if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length -ne 0){throw 'Another AA process exists.'}
  $remaining=Get-AzuriteFullRuntimeBudget $Control $Control.runtimeTimeoutSeconds $BootstrapClock.ElapsedMilliseconds ([datetime]::UtcNow)
  $argv=@('-batchmode','-nographics','-disableaudio','-logFile',$log);$psi=New-AzuriteProcessInfo 'F:\AzuriteLabs\Editor-20261108-normal\install\AzureArchive.exe' $argv 'F:\AzuriteLabs\Editor-20261108-normal\install'
  Set-AzuriteCliChildEnvironment $psi $request
  $full=[Azurite.Restricted.ProcessPump]::RunFullRestrictedCapabilities($psi,$remaining,$job,$Control.runId,$run,$Control.argumentsSha256,262144,@($output,$request,'F:\AzuriteLabs\Editor-20261108-normal\install\BepInEx\LogOutput.log'),4194304)
  $native=$full.Process;$r.restrictedNative=$full;$r.aaPid=$native.Pid;$r.aaStartedUtc=$native.StartedUtc.ToString('o');$r.aaExitCode=$native.ExitCode;$r.timedOut=$native.TimedOut;$r.windowViolation=$native.WindowViolation;$r.nativeArguments=$argv
  Write-AzuriteFullCapturedText (Join-Path $output 'aa.stdout.log') $native.Stdout;Write-AzuriteFullCapturedText (Join-Path $output 'aa.stderr.log') $native.Stderr
  $status=Get-AzuriteNativeProcessStatus $native;if($status.failed -or -not $full.StreamsComplete -or -not $full.NativeHandlesClosed -or -not $full.DesktopLeaseAcknowledged -or -not $native.CleanupVerified){throw ('Full native spawn/monitor/cleanup failed: '+$status.reason)}
  $response=Read-AzuriteCliResponse (Join-Path $request 'response.json') capabilities $native.StartedUtc;$r.responseSha256=$response.Sha256;$r.responseBytes=$response.Bytes;$r.status='passed';$exit=0
 }catch{$r.error=$_.Exception.ToString()}finally{try{Write-AzuriteFlushedJson (Join-Path $output 'bootstrap-result.json') $r;[Console]::Out.Flush();[Console]::Error.Flush()}finally{$job.TerminateFull([uint32]$exit)}}
}
Export-ModuleMember -Function Assert-AzuriteFullZeroSharedDelta,Assert-AzuritePrecreatedLogEvidence,Assert-AzuriteFullEnvironment,Assert-AzuriteFullRuntimeGate,Assert-AzuriteFullRequestInventory,Read-AzuriteFullPreparedControl,Invoke-AzuriteFullRestrictedBootstrap,Invoke-AzuriteFullCapabilitiesPreparedRun,ConvertTo-AzuriteFullFileIdentity,Get-AzuriteFullToolNames,Assert-AzuriteFullToolManifest,Assert-AzuriteFullAclPolicy,Get-AzuriteFullExpectedRoles,Assert-AzuriteFullExpectedAclManifest,Read-AzuriteFullApproval,Assert-AzuriteFullPendingRecord,Assert-AzuriteFullCleanupAcceptance,Get-AzuriteFullRuntimeBudget,Invoke-AzuriteFullLeaseFinalizer,Invoke-AzuriteFullPendingCompensation,Get-AzuriteFullExpectedInventory,Assert-AzuriteFullInventoryNames,Assert-AzuriteFullOriginalIdentity,Assert-AzuriteFullControlBinding,Read-AzuriteFullBoundedJson,ConvertFrom-AzuriteFullJson
