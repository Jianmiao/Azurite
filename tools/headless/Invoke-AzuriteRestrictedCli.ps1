#requires -Version 7.0
param(
 [Parameter(Mandatory)][string]$LabRoot,
 [Parameter(Mandatory)][ValidateSet('capabilities','schema')][string]$Command,
 [ValidateRange(10,300)][int]$TimeoutSeconds=120,
 [switch]$UseCurrentAccount,
 [switch]$PlanOnly,
 [switch]$FullRestrictedCapabilities,
 [string]$FullRunId,
 [string]$FullPlanFactsPath,
 [string]$FullAdmissionPath,
 [string]$FullRuntimeGatePath,
 [string]$FullEvidenceBundlePath,
 [string]$FullApprovalPath,
 [string]$FullExpectedApprovalSha256,
 [string]$FullExpectedRuntimeGateSha256,
 [string]$FullTransitionSourceManifestPath
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'HeadlessCore.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CurrentAccountCli.psm1') -Force
if($FullRestrictedCapabilities){
 Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesPlan.psm1') -Force
 Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesRuntime.psm1') -Force
 if(-not $UseCurrentAccount -or $Command -cne 'capabilities' -or $LabRoot -cne 'F:\AzuriteLabs\Editor-20261108-normal'){throw 'Full restriction is only the fixed F current-account capabilities branch.'}
 if(-not $FullRunId){if($PlanOnly){$FullRunId='0'*32}else{throw 'A separately admitted fresh full-restriction RunId is required.'}}
 $fullContract=Read-AzuriteFullCapabilitiesContract
 $cache=Read-AzuriteFullBoundedJson (Join-Path $PSScriptRoot '..\..\artifacts\aa-fr-cache-prerequisites\cache-prerequisites-contract.json') 131072
 $facts=if($FullPlanFactsPath){Read-AzuriteFullBoundedJson $FullPlanFactsPath 1048576}else{$null}
 $admission=if($FullAdmissionPath){Read-AzuriteFullBoundedJson $FullAdmissionPath 1048576}else{$null}
 $bundle=if($FullEvidenceBundlePath){Read-AzuriteFullBoundedJson $FullEvidenceBundlePath 1048576}else{$null}
 $approval=if($FullApprovalPath){Read-AzuriteFullApproval $FullApprovalPath $FullExpectedApprovalSha256}else{$null}
 $planArgs=@{Contract=$fullContract;CachePrerequisites=$cache;RunId=$FullRunId;Facts=$facts;Admission=$admission}
 foreach($required in @('EvidenceBundle','Approval','ExpectedApprovalSha256')){if(-not(Get-Command New-AzuriteFullCapabilitiesPlan).Parameters.ContainsKey($required)){throw 'Reviewed forward-only planner interface is required.'}}
 $planArgs.EvidenceBundle=$bundle;$planArgs.Approval=$approval;$planArgs.ExpectedApprovalSha256=$FullExpectedApprovalSha256
 $fullPlan=New-AzuriteFullCapabilitiesPlan @planArgs
 if($PlanOnly){return $fullPlan}
 if(-not $fullPlan.Ready -or -not $FullRuntimeGatePath){throw 'Full capabilities preparation is not runtime admission; exact transition, focused review and harmless lifecycle gate remain required.'}
 if($FullExpectedRuntimeGateSha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-AzuriteSha256 $FullRuntimeGatePath).ToLowerInvariant() -cne $FullExpectedRuntimeGateSha256){throw 'Runtime gate must match an externally supplied reviewed digest.'}
 $gate=Read-AzuriteFullBoundedJson $FullRuntimeGatePath 1048576
 return Invoke-AzuriteFullCapabilitiesPreparedRun -Contract $fullContract -Plan $fullPlan -Facts $facts -Gate $gate -GateSha256 $FullExpectedRuntimeGateSha256 -Admission $admission -EvidenceBundle $bundle -Approval $approval -ExpectedApprovalSha256 $FullExpectedApprovalSha256 -TransitionSourceManifestPath $FullTransitionSourceManifestPath -ToolDirectory $PSScriptRoot -RuntimeTimeoutSeconds $TimeoutSeconds
}
$lab=Assert-AzuriteFullPath $LabRoot
$controllerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$controllerProfile=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$identityMode=if($UseCurrentAccount){'CurrentAccount'}else{'DedicatedAccount'}
$currentPrincipal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$currentContext=[pscustomobject]@{sid=$controllerSid;profile=$controllerProfile;isAdministrator=$currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}
$currentEnvironment=[pscustomobject]@{localAppData=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData);environmentProfile=$env:USERPROFILE;environmentLocalAppData=$env:LOCALAPPDATA;environmentAppData=$env:APPDATA;environmentTemp=$env:TEMP;environmentTmp=$env:TMP}
$contract=Read-AzuriteStartupContract
$contractHash=Get-AzuriteSha256 (Join-Path $PSScriptRoot 'restricted-startup-contract.json')
$install=Join-Path $lab 'install'
$manifestPath=Join-Path $lab 'account.manifest.json';$credentialPath=Join-Path $lab 'secrets\controller.credential.json'
$systemShell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$failures=[Collections.Generic.List[string]]::new();$marker=$null;$manifest=$null;$account=$null;$credentialRecord=$null
try {Assert-AzuriteCleanupGateClear $lab}catch{$failures.Add($_.Exception.Message)}
if($failures.Count){
 if($PlanOnly){return [pscustomobject]@{Command=$Command;LabRoot=$lab;ContractSha256=$contractHash;Ready=$false;Failures=$failures.ToArray();WillLaunch=$false;Quarantined=$true;Acceptance=$contract.acceptance}}
 throw ('Restricted CLI quarantine blocks preparation: '+($failures -join '; '))
}
try {
 $marker=Read-AzuriteBoundedJson (Join-Path $lab 'lab.marker.json') 16384
 $labId=[guid]::Empty
 if($marker.schema -ne 1 -or $marker.purpose -cne 'AzuriteHeadlessLab' -or $marker.controllerSid -cne $controllerSid -or -not [guid]::TryParse($marker.labId,[ref]$labId)){throw 'Lab marker does not identify this controller and lab.'}
 [void](Assert-AzuriteLabPaths -LabRoot $lab -InstallPath $install -DailyInstallPath $marker.dailyInstallPath)
 if(-not $lab.Equals($contract.labRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Lab root is outside the pinned startup contract.'}
 Assert-AzuriteNoReparseTree $lab
 Assert-AzuritePinnedInstall $install $contract
 Assert-AzuriteEvidence $contract
} catch {$failures.Add($_.Exception.Message)}
if($UseCurrentAccount){
 try {
  if($currentContext.isAdministrator){throw 'Current-account mode requires the unelevated current token.'}
  $registeredLow=Get-AzuriteRegisteredLocalLow $controllerProfile
  $stateSpec=New-AzuriteSharedStateSpec $registeredLow.path $marker.dailyInstallPath $controllerSid
  $planSnapshot=New-AzuriteSharedSnapshot $stateSpec (Read-AzuritePlayerPrefsSnapshot) 'plan' Plan
  $currentPlan=Get-AzuriteCurrentPrerequisites $lab $contract $currentContext $stateSpec $planSnapshot
  foreach($failure in $currentPlan.Failures){$failures.Add($failure)}
  $planSnapshot=$null
 } catch {$failures.Add('Current-account identity or bounded shared-state preflight failed: '+$_.Exception.GetType().Name)}
}else{
try {
 if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){throw 'Dedicated account manifest is missing; manual reviewed account setup remains required.'}
 Assert-AzuritePrivateAcl -Path $manifestPath -ControllerSid $controllerSid
 $manifest=Read-AzuriteBoundedJson $manifestPath 16384
 if(-not $marker -or $manifest.schema -ne 1 -or $manifest.labId -cne $marker.labId -or $manifest.controllerSid -cne $controllerSid -or $manifest.accountName -cne 'AzuriteTest' -or $manifest.accountSid -ceq $controllerSid -or $manifest.accountSid -notmatch '^S-1-5-21-'){throw 'Dedicated account manifest identity is invalid.'}
 $account=Get-LocalUser -Name 'AzuriteTest' -ErrorAction Stop
 if(-not $account.Enabled -or $account.SID.Value -cne $manifest.accountSid -or $account.Description -cne (Get-AzuriteAccountDescription $manifest.ownerToken)){throw 'Dedicated account identity differs from owned manifest.'}
 Assert-AzuriteLowPrivilegeGroups -AccountSid $account.SID.Value
} catch {$failures.Add($_.Exception.Message)}
try {
 if(-not(Test-Path -LiteralPath $credentialPath -PathType Leaf)){throw 'Private DPAPI credential is missing; no child can start.'}
 Assert-AzuritePrivateAcl -Path (Join-Path $lab 'secrets') -ControllerSid $controllerSid
 Assert-AzuritePrivateAcl -Path $credentialPath -ControllerSid $controllerSid
 $credentialRecord=Read-AzuriteBoundedJson $credentialPath 16384
 if(-not $manifest -or -not $account -or -not(Test-AzuriteCommittedCredential -Manifest $manifest -CredentialRecord $credentialRecord -LabId $marker.labId -ControllerSid $controllerSid -AccountSid $account.SID.Value)){throw 'DPAPI credential digest or ownership is uncommitted/invalid.'}
} catch {$failures.Add($_.Exception.Message)}
}
try {
 if(-not(Test-Path -LiteralPath $systemShell -PathType Leaf)){throw 'System Windows PowerShell 5.1 is unavailable.'}
 [void](Assert-AzuriteFullPath $systemShell)
 if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count){throw 'Another AzureArchive process exists.'}
 if(-not $UseCurrentAccount){foreach($dir in @('runs','workspace','user-data')){if(-not(Test-Path -LiteralPath (Join-Path $lab $dir) -PathType Container)){throw "Prepared lab directory is missing: $dir"}}}
 if($account){
  Assert-AzuriteRestrictedAcl $lab $controllerSid $account.SID.Value
  Assert-AzuriteRestrictedAcl (Join-Path $lab 'runs') $controllerSid $account.SID.Value Modify
  # Reuse existing descendant checks only on shared mutable roots. Restricted runs
  # deliberately contain protected tooling descendants, unlike the generic runner.
  foreach($dir in @('install','workspace','user-data')){
   $path=Join-Path $lab $dir;Assert-AzuriteRestrictedAcl $path $controllerSid $account.SID.Value Modify
   Assert-AzuriteAclTree -Path $path -ControllerSid $controllerSid -TestSid $account.SID.Value
  }
  if((Get-AzuriteSha256 (Join-Path $PSScriptRoot 'Identity-Preflight.ps1')) -cne $manifest.preflightSha256){throw 'Reviewed identity helper differs from account setup pin.'}
 }
} catch {$failures.Add($_.Exception.Message)}
$plan=[pscustomobject]@{Command=$Command;IdentityMode=$identityMode;RequiresCredentials=(-not $UseCurrentAccount);RequiresAccountSetup=(-not $UseCurrentAccount);SharedHKCU=[bool]$UseCurrentAccount;AccountIsolation=(-not $UseCurrentAccount);NativeKnownFolderVerificationRequired=[bool]$UseCurrentAccount;WillPrepareLabDirectories=$(if($UseCurrentAccount){@('runs','secrets')}else{@()});LabRoot=$lab;Executable=(Join-Path $install 'AzureArchive.exe');UnityArguments=@('-batchmode','-nographics','-disableaudio','-logFile','<run>\output\unity.log');BusinessArgv=@($Command);Transport='AZUREARCHIVE_CLI_DIRECTORY + arguments.json';ContractSha256=$contractHash;Ready=($failures.Count -eq 0);Failures=$failures.ToArray();WillLaunch=$false;Acceptance=$contract.acceptance}
if($PlanOnly){return $plan}
if(-not $plan.Ready){throw ('Restricted CLI prerequisites failed. No process started: '+($failures -join '; '))}
$targetSid=if($UseCurrentAccount){$controllerSid}else{$account.SID.Value}

$mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');$locked=$false;$credential=$null;$password=$null
try {
 try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
 if(-not $locked){throw 'Another restricted AA query holds the global run lock.'}
 Assert-AzuriteCleanupGateClear $lab
 if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'").Count){throw 'AzureArchive appeared before staging.'}
 if($UseCurrentAccount){
  foreach($name in @('runs','secrets')){$path=Join-Path $lab $name;if(-not(Test-Path -LiteralPath $path)){[void](New-Item -ItemType Directory -Path $path);Set-AzuriteLabAcl -LabRoot $lab -Path $path -ControllerSid $controllerSid}else{Assert-AzuriteCurrentPrivatePath $path $controllerSid}}
 }
 $runId=[guid]::NewGuid().ToString('N');$runDir=Join-Path $lab ('runs\restricted-'+$runId)
 [void](New-Item -ItemType Directory -Path $runDir -ErrorAction Stop)
 Set-AzuriteRunPathAccess $lab $runDir $controllerSid $targetSid $identityMode Read
 $toolDir=Join-Path $runDir 'tooling';$requestDir=Join-Path $runDir 'request';$outputDir=Join-Path $runDir 'output';$workspace=Join-Path $runDir 'workspace'
 foreach($dir in @($toolDir,$requestDir,$outputDir,$workspace)){
  [void](New-Item -ItemType Directory -Path $dir)
  $rights=if($dir -in @($outputDir,$workspace)){'Modify'}else{'Read'}
  Set-AzuriteRunPathAccess $lab $dir $controllerSid $targetSid $identityMode $rights
 }
 Set-AzuriteRunPathAccess $lab $requestDir $controllerSid $targetSid $identityMode Request
 $toolHashes=@{}
 foreach($name in @('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliBootstrap.ps1','Identity-Preflight.ps1','restricted-startup-contract.json','CurrentAccountCli.psm1','HeadlessCore.psm1')){
  $src=Join-Path $PSScriptRoot $name;$dest=Join-Path $toolDir $name
  Copy-Item -LiteralPath $src -Destination $dest -ErrorAction Stop
  Set-AzuriteRunPathAccess $lab $dest $controllerSid $targetSid $identityMode Read
  $hash=Get-AzuriteSha256 $src;if((Get-AzuriteSha256 $dest) -cne $hash){throw 'Staged tooling hash mismatch.'};$toolHashes[$name]=$hash
 }
 $nativeAssembly=Join-Path $toolDir 'RestrictedCliNative.dll'
 $helperBuild=New-AzuriteNetFrameworkHelper (Join-Path $toolDir 'RestrictedCliNative.cs') $nativeAssembly
 Set-AzuriteRunPathAccess $lab $nativeAssembly $controllerSid $targetSid $identityMode Read
 $toolHashes['RestrictedCliNative.dll']=$helperBuild.assemblySha256
 $argumentsPath=Join-Path $requestDir 'arguments.json'
 Write-AzuriteFlushedJson $argumentsPath @{argv=@($Command);cwd=$workspace}
 Set-AzuriteRunPathAccess $lab $argumentsPath $controllerSid $targetSid $identityMode Read
 $stateDirectory=$null;$stateBefore=$null;$stateBeforeHash=$null;$actualLow=$null
 if($UseCurrentAccount){
  $actualLow=[Azurite.Restricted.KnownFolders]::LocalLow()
  if(-not $actualLow.Equals($registeredLow.path,[StringComparison]::OrdinalIgnoreCase)){throw 'Actual KnownFolder LocalLow differs from the prospective registered path.'}
  $stateDirectory=Join-Path $runDir 'shared-state';[void](New-Item -ItemType Directory -Path $stateDirectory);Set-AzuriteLabAcl -LabRoot $lab -Path $stateDirectory -ControllerSid $controllerSid
  $stateSpec=New-AzuriteSharedStateSpec $actualLow $marker.dailyInstallPath $controllerSid
  $stateBefore=New-AzuriteSharedSnapshot $stateSpec (Read-AzuritePlayerPrefsSnapshot) $runId Before
  Write-AzuriteFlushedJson (Join-Path $stateDirectory 'before.json') $stateBefore
  $stateBeforeHash=Get-AzuriteSha256 (Join-Path $stateDirectory 'before.json')
  Write-AzuriteFlushedJson (Join-Path $stateDirectory 'scope.json') $stateSpec
 }
 $control=[ordered]@{schema=1;identityMode=$identityMode;runId=$runId;command=$Command;labRoot=$lab;runDirectory=$runDir;targetSid=$targetSid;controllerSid=$controllerSid;controllerProfile=$controllerProfile;currentLocalLow=$actualLow;currentEnvironment=$currentEnvironment;sharedStateBeforeSha256=$stateBeforeHash;contractSha256=$contractHash;toolHashes=$toolHashes;argumentsSha256=(Get-AzuriteSha256 $argumentsPath);deadlineUtc=[datetime]::UtcNow.AddSeconds($TimeoutSeconds).ToString('o')}
 $controlPath=Join-Path $toolDir 'control.json';Write-AzuriteFlushedJson $controlPath $control
 Set-AzuriteRunPathAccess $lab $controlPath $controllerSid $targetSid $identityMode Read
 if(-not $UseCurrentAccount){$password=ConvertTo-SecureString $credentialRecord.encryptedPassword -ErrorAction Stop;$credential=[pscredential]::new('AzuriteTest',$password)}
 Initialize-AzuriteRestrictedNative
 # Loader config stays pinned. Separate the existing isolated-install log from this run.
 if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'").Count){throw 'AzureArchive appeared before log archival.'}
 $previousDir=Join-Path $runDir 'previous-log';[void](New-Item -ItemType Directory -Path $previousDir)
 Set-AzuriteLabAcl -LabRoot $lab -Path $previousDir -ControllerSid $controllerSid
 $loaderLog=Join-Path $install 'BepInEx\LogOutput.log'
 $previousLog=Move-AzuritePreviousLoaderLog $loaderLog (Join-Path $previousDir 'LogOutput.log')
 if($previousLog.Present){Set-AzuriteLabAcl -LabRoot $lab -Path $previousLog.Path -ControllerSid $controllerSid}
 $previousTruncation=@(Limit-AzuriteRetainedArtifacts $previousDir)
 $psi=New-AzuriteProcessInfo -FilePath $systemShell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $toolDir 'RestrictedCliBootstrap.ps1'),'-ControlPath',$controlPath) -WorkingDirectory $runDir -Credential $credential
 # Credentialed launch: Environment remains untouched. Only bootstrap changes its AA child's env.
 $remaining=[int]((ConvertTo-AzuriteUtcInstant $control.deadlineUtc)-[datetime]::UtcNow).TotalMilliseconds
 if($remaining -lt 1000){throw 'Preparation exhausted the shared deadline.'}
 $quarantinePath=Start-AzuriteCleanupGate $lab $runId $runDir $controllerSid
 $outer=[Azurite.Restricted.ProcessPump]::RunBootstrap($psi,$remaining,5000,$runId,262144,@($outputDir,$requestDir),4194304)
 $outerStatus=Get-AzuriteOuterProcessStatus $outer
 [IO.File]::WriteAllText((Join-Path $runDir 'bootstrap.stdout.log'),$outer.Stdout)
 [IO.File]::WriteAllText((Join-Path $runDir 'bootstrap.stderr.log'),$outer.Stderr)
 $passed=$false;$reason='';$result=$null;$loaderEvidence=$null
 try {
  Assert-AzuriteCleanupConfirmed $outer
  Assert-AzuriteNoReparseTree $lab
  $loaderEvidence=Copy-AzuriteFreshLoaderLog $loaderLog (Join-Path $outputDir 'bepinex.LogOutput.log') $outer.StartedUtc
  foreach($name in $toolHashes.Keys){if((Get-AzuriteSha256 (Join-Path $toolDir $name)) -cne $toolHashes[$name]){throw 'Prepared tooling changed during child run.'}}
  if((Get-AzuriteSha256 $argumentsPath) -cne $control.argumentsSha256){throw 'Prepared request changed.'}
  if($outerStatus.failed){throw $outerStatus.reason}
  if(-not $loaderEvidence.Present -or -not $loaderEvidence.Fresh -or -not $loaderEvidence.Complete){throw 'Fresh complete loader log is missing or excessive; old log cannot substitute.'}
  $result=Read-AzuriteBoundedJson (Join-Path $outputDir 'bootstrap-result.json') 65536
  $nativeStatus=Get-AzuriteNativeResultStatus $result
  $result.processError=$nativeStatus.error
  if($nativeStatus.failed){throw $nativeStatus.reason}
  if($result.schema -ne 1 -or $result.identityMode -cne $identityMode -or $result.runId -cne $runId -or $result.bootstrapPid -ne $outer.Pid -or $result.targetSid -cne $targetSid -or $result.command -cne $Command -or $result.contractSha256 -cne $contractHash -or $result.argumentsSha256 -cne $control.argumentsSha256 -or $result.status -cne 'passed' -or $result.aaExitCode -ne 0 -or $result.aaPid -isnot [long] -and $result.aaPid -isnot [int] -or $result.aaPid -le 0 -or $result.jobOwned -ne $true -or $result.noSoundConfigured -ne $true -or $result.acceptance -cne $contract.acceptance){throw 'Correlated bootstrap identity/evidence fields are invalid.'}
  $identity=Read-AzuriteBoundedJson (Join-Path $outputDir 'identity.json') 32768
  if((Get-AzuriteSha256 (Join-Path $outputDir 'identity.json')) -cne $result.identitySha256){throw 'Identity record changed after target validation.'}
  if($UseCurrentAccount){Assert-AzuriteCurrentIdentityRecord $identity $result.profilePathProof $controllerSid $controllerProfile $actualLow $currentEnvironment}
  else{Assert-AzuriteTargetIdentityRecord $identity $result.profilePathProof $targetSid $controllerSid $controllerProfile}
  if($identity.pid -ne $outer.Pid -or $identity.runId -cne $runId){throw 'Identity proof is not this bootstrap.'}
  $proof=@($contract.evidence|Where-Object{$_.path.EndsWith('STARTUP-CONTRACT.md')})[0].sha256
  if($result.noSoundProofSha256 -cne $proof){throw 'NoSound configuration evidence differs.'}
  $responsePath=Join-Path $requestDir 'response.json'
  if($result.responsePath -cne $responsePath -or $result.unityLogPath -cne (Join-Path $outputDir 'unity.log')){throw 'Child artifact paths are not this run.'}
  $timeWindow=Get-AzuriteNativeTimeWindow $result $outer.StartedUtc ([datetime]::UtcNow)
  $nativeStarted=$timeWindow.nativeStartedUtc
  if(($result.nativeArguments -join '|') -cne (@('-batchmode','-nographics','-disableaudio','-logFile',(Join-Path $outputDir 'unity.log')) -join '|')){throw 'Native startup arguments differ from the fixed NoSound contract.'}
  $response=Read-AzuriteCliResponse -Path $responsePath -Command $Command -StartedUtc $nativeStarted
  if($response.Sha256 -cne $result.responseSha256 -or $response.Bytes -ne $result.responseBytes){throw 'Native raw response changed after validation.'}
  foreach($entry in @(@{name='aa.stdout.log';sha=$result.stdoutSha256},@{name='aa.stderr.log';sha=$result.stderrSha256},@{name='unity.log';sha=$result.unityLogSha256})){
   $path=Join-Path $outputDir $entry.name;$item=Get-Item -LiteralPath $path
   if($item.Length -gt 4194304 -or (Get-AzuriteSha256 $path) -cne $entry.sha){throw 'Native logs are missing, excessive or changed.'}
  }
  Assert-AzuritePinnedInstall $install $contract
  $passed=$true
 } catch {$reason=$_.Exception.Message}
 if($outerStatus.error -and -not $reason.Contains($outerStatus.error)){$reason+=' '+$outerStatus.reason}
 $drift=@();$trimmed=@();$stateCanRelease=-not $UseCurrentAccount
 $stateSummary=[ordered]@{guarded=[bool]$UseCurrentAccount;verified=$false;accepted=$false;profileShared=[bool]$UseCurrentAccount;hkcuShared=[bool]$UseCurrentAccount;restorationPerformed=$false}
 if($outer.CleanupVerified){
  if($UseCurrentAccount){
   try{
    Assert-AzuriteCleanupConfirmed $outer
    Assert-AzuritePreparedHash (Join-Path $stateDirectory 'before.json') $stateBeforeHash
    $stateAfter=New-AzuriteSharedSnapshot $stateSpec (Read-AzuritePlayerPrefsSnapshot) $runId After
    Write-AzuriteFlushedJson (Join-Path $stateDirectory 'after.json') $stateAfter
    $stateDelta=Compare-AzuriteSharedSnapshots $stateBefore $stateAfter $stateSpec $runId
    Write-AzuriteFlushedJson (Join-Path $stateDirectory 'delta.json') $stateDelta
    $stateCanRelease=$stateDelta.accepted
    $stateSummary=[ordered]@{guarded=$true;verified=$true;accepted=$stateDelta.accepted;profileShared=$true;hkcuShared=$true;userPreferencesUnchanged=$stateDelta.userPreferencesUnchanged;sharedRegistryChanged=$stateDelta.registryChanged;finalStateUnchanged=$stateDelta.finalStateUnchanged;engineMetadataChanged=@($stateDelta.engineMetadata|ForEach-Object{$_.name});unexpectedDeltaCount=$stateDelta.unexpected.Count;restorationPerformed=$false;evidenceDirectory=$stateDirectory;beforeSha256=$stateBeforeHash;afterSha256=(Get-AzuriteSha256 (Join-Path $stateDirectory 'after.json'));deltaSha256=(Get-AzuriteSha256 (Join-Path $stateDirectory 'delta.json'))}
    if(-not $stateCanRelease){$passed=$false;$reason+=' Shared-state preservation failed; private raw evidence retained and run quarantined.'}
   }catch{
    $passed=$false;$stateCanRelease=$false;$reason+=' Shared-state snapshot/comparison is unverified; run quarantined.'
    Write-AzuriteFlushedJson (Join-Path $stateDirectory 'state-failure.json') @{error=$_.Exception.Message;restorationPerformed=$false}
   }
  }
  $drift=@(Get-AzuritePinDrift $install $contract)
  if($drift.Count){$passed=$false;if($UseCurrentAccount){$stateCanRelease=$false};$reason+=' Pinned inputs drifted after the run; manual review required.'}
  if(-not $passed){$trimmed+=@(Limit-AzuriteRetainedArtifacts $outputDir);$trimmed+=@(Limit-AzuriteRetainedArtifacts -Directory $requestDir -MaxFileBytes 1048576 -ExcludeFile @('arguments.json'))}
 }
 $cleanup=[ordered]@{runId=$outer.RunId;verified=$outer.CleanupVerified;jobObserved=$outer.JobObserved;directProcessExited=$outer.DirectProcessExited;jobDrained=$outer.JobDrained;remainingOwnedProcesses=$outer.RemainingOwnedProcesses;noProcessCreated=$outer.NoProcessCreated;graceLimitMilliseconds=5000;elapsedMilliseconds=$outer.CleanupMilliseconds;error=$outer.CleanupError;quarantinePath=$quarantinePath;requiresExplicitRecovery=(-not ($outer.CleanupVerified -and $stateCanRelease))}
 $summary=[ordered]@{schema=1;identityMode=$identityMode;accountIsolation=(-not $UseCurrentAccount);sharedState=$stateSummary;runId=$runId;command=$Command;runDirectory=$runDir;bootstrapPid=$outer.Pid;bootstrapExitCode=$outer.ExitCode;bootstrapProcess=$outerStatus;cleanup=$cleanup;helperBuild=$helperBuild;targetSid=$targetSid;passed=$passed;reason=$reason;timedOut=$outer.TimedOut;windowViolation=$outer.WindowViolation;contractSha256=$contractHash;acceptance=$contract.acceptance;previousLoaderLog=$previousLog;previousLogTruncation=$previousTruncation;loaderLog=$loaderEvidence;pinnedInputDrift=$drift;truncatedArtifacts=$trimmed;native=$result}
 Write-AzuriteFlushedJson (Join-Path $runDir 'controller-result.json') $summary
 if($outer.CleanupVerified -and $stateCanRelease){
  if($UseCurrentAccount){Complete-AzuriteCurrentCleanupGate $lab $runId $controllerSid $outer $stateDelta}
  else{Complete-AzuriteCleanupGate $lab $runId $controllerSid $outer}
 }
 if(-not $passed){throw "Restricted native query failed: $reason; evidence: $runDir"}
 return [pscustomobject]$summary
} finally {if($password){$password.Dispose()};if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
