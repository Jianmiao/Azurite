#requires -Version 7.0
param(
 [Parameter(Mandatory)][string]$LabRoot,
 [Parameter(Mandatory)][ValidateSet('capabilities','schema')][string]$Command,
 [ValidateRange(10,300)][int]$TimeoutSeconds=120,
 [switch]$PlanOnly
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'HeadlessCore.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -Force
$lab=Assert-AzuriteFullPath $LabRoot
$controllerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$controllerProfile=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
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
try {
 if(-not(Test-Path -LiteralPath $systemShell -PathType Leaf)){throw 'System Windows PowerShell 5.1 is unavailable.'}
 [void](Assert-AzuriteFullPath $systemShell)
 if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count){throw 'Another AzureArchive process exists.'}
 foreach($dir in @('runs','workspace','user-data')){if(-not(Test-Path -LiteralPath (Join-Path $lab $dir) -PathType Container)){throw "Prepared lab directory is missing: $dir"}}
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
$plan=[pscustomobject]@{Command=$Command;LabRoot=$lab;Executable=(Join-Path $install 'AzureArchive.exe');UnityArguments=@('-batchmode','-nographics','-disableaudio','-logFile','<run>\output\unity.log');BusinessArgv=@($Command);Transport='AZUREARCHIVE_CLI_DIRECTORY + arguments.json';ContractSha256=$contractHash;Ready=($failures.Count -eq 0);Failures=$failures.ToArray();WillLaunch=$false;Acceptance=$contract.acceptance}
if($PlanOnly){return $plan}
if(-not $plan.Ready){throw ('Restricted CLI prerequisites failed. No process started: '+($failures -join '; '))}

$mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');$locked=$false;$credential=$null;$password=$null
try {
 try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
 if(-not $locked){throw 'Another restricted AA query holds the global run lock.'}
 Assert-AzuriteCleanupGateClear $lab
 if(@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'").Count){throw 'AzureArchive appeared before staging.'}
 $runId=[guid]::NewGuid().ToString('N');$runDir=Join-Path $lab ('runs\restricted-'+$runId)
 [void](New-Item -ItemType Directory -Path $runDir -ErrorAction Stop)
 Set-AzuriteLabAcl -LabRoot $lab -Path $runDir -ControllerSid $controllerSid -TestSid $account.SID.Value
 Assert-AzuriteRestrictedAcl $runDir $controllerSid $account.SID.Value
 $toolDir=Join-Path $runDir 'tooling';$requestDir=Join-Path $runDir 'request';$outputDir=Join-Path $runDir 'output';$workspace=Join-Path $runDir 'workspace'
 foreach($dir in @($toolDir,$requestDir,$outputDir,$workspace)){
  [void](New-Item -ItemType Directory -Path $dir)
  $rights=if($dir -in @($outputDir,$workspace)){'Modify'}else{'ReadAndExecute'}
  Set-AzuriteLabAcl -LabRoot $lab -Path $dir -ControllerSid $controllerSid -TestSid $account.SID.Value -TestRights $rights
 }
 Set-AzuriteRequestAcl $lab $requestDir $controllerSid $account.SID.Value
 $toolHashes=@{}
 foreach($name in @('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliBootstrap.ps1','Identity-Preflight.ps1','restricted-startup-contract.json')){
  $src=Join-Path $PSScriptRoot $name;$dest=Join-Path $toolDir $name
  Copy-Item -LiteralPath $src -Destination $dest -ErrorAction Stop
  Set-AzuriteLabAcl -LabRoot $lab -Path $dest -ControllerSid $controllerSid -TestSid $account.SID.Value
  Assert-AzuriteRestrictedAcl $dest $controllerSid $account.SID.Value
  $hash=Get-AzuriteSha256 $src;if((Get-AzuriteSha256 $dest) -cne $hash){throw 'Staged tooling hash mismatch.'};$toolHashes[$name]=$hash
 }
 $nativeAssembly=Join-Path $toolDir 'RestrictedCliNative.dll'
 $helperBuild=New-AzuriteNetFrameworkHelper (Join-Path $toolDir 'RestrictedCliNative.cs') $nativeAssembly
 Set-AzuriteLabAcl -LabRoot $lab -Path $nativeAssembly -ControllerSid $controllerSid -TestSid $account.SID.Value
 Assert-AzuriteRestrictedAcl $nativeAssembly $controllerSid $account.SID.Value
 $toolHashes['RestrictedCliNative.dll']=$helperBuild.assemblySha256
 $argumentsPath=Join-Path $requestDir 'arguments.json'
 Write-AzuriteFlushedJson $argumentsPath @{argv=@($Command);cwd=$workspace}
 Set-AzuriteLabAcl -LabRoot $lab -Path $argumentsPath -ControllerSid $controllerSid -TestSid $account.SID.Value
 Assert-AzuriteRestrictedAcl $argumentsPath $controllerSid $account.SID.Value
 $control=[ordered]@{schema=1;runId=$runId;command=$Command;labRoot=$lab;runDirectory=$runDir;targetSid=$account.SID.Value;controllerSid=$controllerSid;controllerProfile=$controllerProfile;contractSha256=$contractHash;toolHashes=$toolHashes;argumentsSha256=(Get-AzuriteSha256 $argumentsPath);deadlineUtc=[datetime]::UtcNow.AddSeconds($TimeoutSeconds).ToString('o')}
 $controlPath=Join-Path $toolDir 'control.json';Write-AzuriteFlushedJson $controlPath $control
 Set-AzuriteLabAcl -LabRoot $lab -Path $controlPath -ControllerSid $controllerSid -TestSid $account.SID.Value
 Assert-AzuriteRestrictedAcl $controlPath $controllerSid $account.SID.Value
 $password=ConvertTo-SecureString $credentialRecord.encryptedPassword -ErrorAction Stop;$credential=[pscredential]::new('AzuriteTest',$password)
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
 $remaining=[int]([datetime]::Parse($control.deadlineUtc).ToUniversalTime()-[datetime]::UtcNow).TotalMilliseconds
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
  if($result.schema -ne 1 -or $result.runId -cne $runId -or $result.bootstrapPid -ne $outer.Pid -or $result.targetSid -cne $account.SID.Value -or $result.command -cne $Command -or $result.contractSha256 -cne $contractHash -or $result.argumentsSha256 -cne $control.argumentsSha256 -or $result.status -cne 'passed' -or $result.aaExitCode -ne 0 -or $result.aaPid -isnot [long] -and $result.aaPid -isnot [int] -or $result.aaPid -le 0 -or $result.jobOwned -ne $true -or $result.noSoundConfigured -ne $true -or $result.acceptance -cne $contract.acceptance){throw 'Correlated bootstrap identity/evidence fields are invalid.'}
  $identity=Read-AzuriteBoundedJson (Join-Path $outputDir 'identity.json') 32768
  if((Get-AzuriteSha256 (Join-Path $outputDir 'identity.json')) -cne $result.identitySha256){throw 'Identity record changed after target validation.'}
  Assert-AzuriteTargetIdentityRecord $identity $result.profilePathProof $account.SID.Value $controllerSid $controllerProfile
  if($identity.pid -ne $outer.Pid -or $identity.runId -cne $runId){throw 'Identity proof is not this bootstrap.'}
  $proof=@($contract.evidence|Where-Object{$_.path.EndsWith('STARTUP-CONTRACT.md')})[0].sha256
  if($result.noSoundProofSha256 -cne $proof){throw 'NoSound configuration evidence differs.'}
  $responsePath=Join-Path $requestDir 'response.json'
  if($result.responsePath -cne $responsePath -or $result.unityLogPath -cne (Join-Path $outputDir 'unity.log')){throw 'Child artifact paths are not this run.'}
  $nativeStarted=[datetime]::Parse($result.aaStartedUtc).ToUniversalTime()
  $identityChecked=[datetime]::Parse($result.profilePathProof.checkedUtc).ToUniversalTime()
  if($identityChecked -lt $outer.StartedUtc -or $identityChecked -gt $nativeStarted){throw 'Target filesystem proof is outside pre-native bootstrap interval.'}
  if($nativeStarted -lt $outer.StartedUtc -or $nativeStarted -gt [datetime]::UtcNow){throw 'Native creation time is outside the owned bootstrap run.'}
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
 $drift=@();$trimmed=@()
 if($outer.CleanupVerified){
  $drift=@(Get-AzuritePinDrift $install $contract)
  if($drift.Count){$passed=$false;$reason+=' Pinned inputs drifted after the run; manual review required.'}
  if(-not $passed){$trimmed+=@(Limit-AzuriteRetainedArtifacts $outputDir);$trimmed+=@(Limit-AzuriteRetainedArtifacts -Directory $requestDir -MaxFileBytes 1048576 -ExcludeFile @('arguments.json'))}
 }
 $cleanup=[ordered]@{runId=$outer.RunId;verified=$outer.CleanupVerified;jobObserved=$outer.JobObserved;directProcessExited=$outer.DirectProcessExited;jobDrained=$outer.JobDrained;remainingOwnedProcesses=$outer.RemainingOwnedProcesses;noProcessCreated=$outer.NoProcessCreated;graceLimitMilliseconds=5000;elapsedMilliseconds=$outer.CleanupMilliseconds;error=$outer.CleanupError;quarantinePath=$quarantinePath;requiresExplicitRecovery=(-not $outer.CleanupVerified)}
 $summary=[ordered]@{schema=1;runId=$runId;command=$Command;runDirectory=$runDir;bootstrapPid=$outer.Pid;bootstrapExitCode=$outer.ExitCode;bootstrapProcess=$outerStatus;cleanup=$cleanup;helperBuild=$helperBuild;targetSid=$account.SID.Value;passed=$passed;reason=$reason;timedOut=$outer.TimedOut;windowViolation=$outer.WindowViolation;contractSha256=$contractHash;acceptance=$contract.acceptance;previousLoaderLog=$previousLog;previousLogTruncation=$previousTruncation;loaderLog=$loaderEvidence;pinnedInputDrift=$drift;truncatedArtifacts=$trimmed;native=$result}
 Write-AzuriteFlushedJson (Join-Path $runDir 'controller-result.json') $summary
 if($outer.CleanupVerified){Complete-AzuriteCleanupGate $lab $runId $controllerSid $outer}
 if(-not $passed){throw "Restricted native query failed: $reason; evidence: $runDir"}
 return [pscustomobject]$summary
} finally {if($password){$password.Dispose()};if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
