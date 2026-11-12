#requires -Version 5.1
param([Parameter(Mandatory)][string]$ControlPath,[string]$ReviewedControlSha256)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$fullBootstrapClock=[Diagnostics.Stopwatch]::StartNew()
# Current-account launch inherits the controller's PSModulePath. Resolve these
# built-ins from this machine-wide PS5.1 host, without rewriting its environment.
foreach($name in @('Microsoft.PowerShell.Security','Microsoft.PowerShell.Management','Microsoft.PowerShell.Utility')){Import-Module (Join-Path $PSHOME ('Modules\'+$name+'\'+$name+'.psd1')) -Force -DisableNameChecking}
# The full-mode control is pinned before loading any copied run module.
if($ReviewedControlSha256){
 if($ReviewedControlSha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-Item -LiteralPath $ControlPath).Length -gt 131072 -or (Get-FileHash -LiteralPath $ControlPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $ReviewedControlSha256){throw 'Reviewed full control digest/size mismatch.'}
 $fullRaw=[IO.File]::ReadAllText($ControlPath)|ConvertFrom-Json
 if($fullRaw.schema -ne 2 -or $fullRaw.identityMode -cne 'CurrentAccountFullRestrictedCapabilities' -or $fullRaw.runId -cnotmatch '^[a-f0-9]{32}$' -or $fullRaw.command -cne 'capabilities' -or $fullRaw.labRoot -cne 'F:\AzuriteLabs\Editor-20261108-normal'){throw 'Fixed full bootstrap control identity mismatch.'}
 $fullRun=Join-Path $fullRaw.labRoot ('runs\restricted-'+$fullRaw.runId)
 if($PSScriptRoot -cne (Join-Path $fullRun 'tooling') -or $ControlPath -cne (Join-Path $PSScriptRoot 'control.json')){throw 'Full control/tooling paths differ from admitted run.'}
 $fullTools=@('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1')
 if(@($fullRaw.toolHashes.PSObject.Properties).Count -ne $fullTools.Count){throw 'Exact full tool hash set is required.'}
 foreach($entry in $fullRaw.toolHashes.PSObject.Properties){if($entry.Name -cnotin $fullTools -or [string]$entry.Value -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $entry.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.Value){throw 'Full tooling bytes differ before module import.'}}
}
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'HeadlessCore.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'CurrentAccountCli.psm1') -Force -DisableNameChecking
$control=Read-AzuriteBoundedJson $ControlPath 65536
if($control.schema -eq 2 -and $control.identityMode -ceq 'CurrentAccountFullRestrictedCapabilities'){
 if(-not $ReviewedControlSha256){throw 'Full bootstrap requires an independently passed reviewed control hash.'}
 Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesRuntime.psm1') -Force -DisableNameChecking
 Invoke-AzuriteFullRestrictedBootstrap -Control $control -ToolDirectory $PSScriptRoot -ControlPath $ControlPath -ReviewedControlSha256 $ReviewedControlSha256 -BootstrapClock $fullBootstrapClock
 throw 'Full bootstrap returned without terminating its owned Job.'
}
$contract=Read-AzuriteStartupContract
if($control.schema -ne 1 -or $control.identityMode -cnotin @('CurrentAccount','DedicatedAccount') -or $control.command -cnotin @('capabilities','schema') -or $control.runId -cnotmatch '^[a-f0-9]{32}$' -or $control.labRoot -cne $contract.labRoot -or ($control.identityMode -ceq 'CurrentAccount' -and $control.targetSid -cne $control.controllerSid)){throw 'Invalid restricted bootstrap control.'}
$runDir=Join-Path $control.labRoot ('runs\restricted-'+$control.runId)
if($control.runDirectory -cne $runDir -or $PSScriptRoot -cne (Join-Path $runDir 'tooling') -or $ControlPath -cne (Join-Path $PSScriptRoot 'control.json')){throw 'Bootstrap paths do not belong to this run.'}
Assert-AzuriteRunPathAccess $runDir $control.controllerSid $control.targetSid $control.identityMode
Assert-AzuriteRunPathAccess $PSScriptRoot $control.controllerSid $control.targetSid $control.identityMode
Assert-AzuriteRunPathAccess $ControlPath $control.controllerSid $control.targetSid $control.identityMode
foreach($entry in $control.toolHashes.PSObject.Properties){
 if($entry.Name -cnotin @('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','Identity-Preflight.ps1','restricted-startup-contract.json','CurrentAccountCli.psm1','HeadlessCore.psm1')){throw 'Unexpected bootstrap file.'}
 $path=Join-Path $PSScriptRoot $entry.Name;Assert-AzuriteRunPathAccess $path $control.controllerSid $control.targetSid $control.identityMode
 if((Get-AzuriteSha256 $path) -cne $entry.Value){throw 'Bootstrap tooling hash differs.'}
}
if((Get-AzuriteSha256 (Join-Path $PSScriptRoot 'restricted-startup-contract.json')) -cne $control.contractSha256){throw 'Bootstrap startup contract differs.'}
if(@($control.toolHashes.PSObject.Properties).Count -ne 8 -or -not $control.toolHashes.PSObject.Properties['RestrictedCliNative.dll']){throw 'Controller-built helper assembly pin is missing.'}
Initialize-AzuriteRestrictedNative -AssemblyPath (Join-Path $PSScriptRoot 'RestrictedCliNative.dll')
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent()
$outputDir=Join-Path $runDir 'output';$requestDir=Join-Path $runDir 'request';$workspace=Join-Path $runDir 'workspace';$exitCode=1
$result=[ordered]@{schema=1;identityMode=$control.identityMode;runId=$control.runId;command=$control.command;bootstrapPid=$PID;targetSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;status='failed';contractSha256=$control.contractSha256;argumentsSha256=$control.argumentsSha256;jobOwned=$true;acceptance=$contract.acceptance;noSoundConfigured=$false}
try {
 $job.AnnounceAndAwaitController($control.runId)
 Assert-AzuriteRunPathAccess $outputDir $control.controllerSid $control.targetSid $control.identityMode Modify
 Assert-AzuriteRunPathAccess $workspace $control.controllerSid $control.targetSid $control.identityMode Modify
 Assert-AzuriteRunPathAccess $requestDir $control.controllerSid $control.targetSid $control.identityMode Request
 $identityPath=Join-Path $outputDir 'identity.json'
 & (Join-Path $PSScriptRoot 'Identity-Preflight.ps1') -AzuriteRunId $control.runId -AzuriteResultPath $identityPath
 $identity=Read-AzuriteBoundedJson $identityPath 32768
 if($control.identityMode -ceq 'CurrentAccount'){
  $result.profilePathProof=Assert-AzuriteCurrentIdentity $identity $control.controllerSid $control.controllerProfile $control.currentLocalLow $control.currentEnvironment
  Assert-AzuritePreparedHash (Join-Path $runDir 'shared-state\before.json') $control.sharedStateBeforeSha256
 }else{$result.profilePathProof=Assert-AzuriteTargetIdentity $identity $control.targetSid $control.controllerSid $control.controllerProfile}
 $result.identitySha256=Get-AzuriteSha256 $identityPath
 $name=[Security.Principal.WindowsIdentity]::GetCurrent().Name.Split('\')[-1]
 if($control.identityMode -ceq 'DedicatedAccount' -and $name -cne 'AzuriteTest'){throw 'Bootstrap is not the dedicated account.'}
 $install=Join-Path $control.labRoot 'install';Assert-AzuritePinnedInstall $install $contract
 $argPath=Join-Path $requestDir 'arguments.json';Assert-AzuriteRunPathAccess $argPath $control.controllerSid $control.targetSid $control.identityMode
 if((Get-AzuriteSha256 $argPath) -cne $control.argumentsSha256){throw 'Prepared business argv changed.'}
 $request=Read-AzuriteBoundedJson $argPath 4096
 if(@($request.PSObject.Properties).Count -ne 2 -or $request.argv -isnot [array] -or $request.argv.Count -ne 1 -or $request.argv[0] -cne $control.command -or $request.cwd -cne $workspace){throw 'Unsupported business argv.'}
 $responsePath=Join-Path $requestDir 'response.json';$unityLog=Join-Path $outputDir 'unity.log'
 if(Test-Path $responsePath){throw 'Response already exists before owned native start.'}
 if(Test-Path $unityLog){throw 'Unity log already exists before owned native start.'}
 if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length -ne 0){throw 'Another AzureArchive process exists.'}
 $remaining=[int]((ConvertTo-AzuriteUtcInstant $control.deadlineUtc)-[datetime]::UtcNow).TotalMilliseconds
 if($remaining -lt 1000){throw 'Shared deadline expired before native start.'}
 $argv=@('-batchmode','-nographics','-disableaudio','-logFile',$unityLog)
 $psi=New-AzuriteProcessInfo -FilePath (Join-Path $install 'AzureArchive.exe') -Arguments $argv -WorkingDirectory $install
 Set-AzuriteCliChildEnvironment -StartInfo $psi -RequestDirectory $requestDir
 $result.noSoundConfigured=$true;$result.noSoundProofSha256=@($contract.evidence|Where-Object{$_.path.EndsWith('STARTUP-CONTRACT.md')})[0].sha256
 $result.nativeArguments=$argv
 $native=[Azurite.Restricted.ProcessPump]::Run($psi,$remaining,$job,262144,@($outputDir,$requestDir,(Join-Path $install 'BepInEx\LogOutput.log')),4194304)
 $nativeStatus=Get-AzuriteNativeProcessStatus $native
 $result.aaPid=$native.Pid;$result.aaStartedUtc=$native.StartedUtc.ToString('o');$result.aaExitCode=$native.ExitCode;$result.timedOut=$native.TimedOut;$result.windowViolation=$native.WindowViolation;$result.outputLimitExceeded=$native.OutputLimitExceeded;$result.processError=$nativeStatus.error
 [IO.File]::WriteAllText((Join-Path $outputDir 'aa.stdout.log'),$native.Stdout);[IO.File]::WriteAllText((Join-Path $outputDir 'aa.stderr.log'),$native.Stderr)
 if($nativeStatus.failed){throw $nativeStatus.reason}
 $response=Read-AzuriteCliResponse $responsePath $control.command $native.StartedUtc
 if(-not(Test-Path $unityLog -PathType Leaf)){throw 'Fresh native Unity log is missing.'}
 $result.responsePath=$responsePath;$result.responseSha256=$response.Sha256;$result.responseBytes=$response.Bytes;$result.unityLogPath=$unityLog
 $result.stdoutSha256=Get-AzuriteSha256 (Join-Path $outputDir 'aa.stdout.log');$result.stderrSha256=Get-AzuriteSha256 (Join-Path $outputDir 'aa.stderr.log');$result.unityLogSha256=Get-AzuriteSha256 $unityLog
 $result.status='passed';$exitCode=0
} catch {$result.error=$_.Exception.Message}
finally {
 try {Write-AzuriteFlushedJson (Join-Path $outputDir 'bootstrap-result.json') $result;[Console]::Out.Flush();[Console]::Error.Flush()}
 finally {$job.Terminate([uint32]$exitCode)}
}
