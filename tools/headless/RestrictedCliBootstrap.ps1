#requires -Version 5.1
param([Parameter(Mandatory)][string]$ControlPath)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -Force -DisableNameChecking
$control=Read-AzuriteBoundedJson $ControlPath 65536
$contract=Read-AzuriteStartupContract
if($control.schema -ne 1 -or $control.command -cnotin @('capabilities','schema') -or $control.runId -cnotmatch '^[a-f0-9]{32}$' -or $control.labRoot -cne $contract.labRoot){throw 'Invalid restricted bootstrap control.'}
$runDir=Join-Path $control.labRoot ('runs\restricted-'+$control.runId)
if($control.runDirectory -cne $runDir -or $PSScriptRoot -cne (Join-Path $runDir 'tooling') -or $ControlPath -cne (Join-Path $PSScriptRoot 'control.json')){throw 'Bootstrap paths do not belong to this run.'}
Assert-AzuriteRestrictedAcl $runDir $control.controllerSid $control.targetSid
Assert-AzuriteRestrictedAcl $PSScriptRoot $control.controllerSid $control.targetSid
Assert-AzuriteRestrictedAcl $ControlPath $control.controllerSid $control.targetSid
foreach($entry in $control.toolHashes.PSObject.Properties){
 if($entry.Name -cnotin @('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','Identity-Preflight.ps1','restricted-startup-contract.json')){throw 'Unexpected bootstrap file.'}
 $path=Join-Path $PSScriptRoot $entry.Name;Assert-AzuriteRestrictedAcl $path $control.controllerSid $control.targetSid
 if((Get-AzuriteSha256 $path) -cne $entry.Value){throw 'Bootstrap tooling hash differs.'}
}
if((Get-AzuriteSha256 (Join-Path $PSScriptRoot 'restricted-startup-contract.json')) -cne $control.contractSha256){throw 'Bootstrap startup contract differs.'}
if(@($control.toolHashes.PSObject.Properties).Count -ne 6 -or -not $control.toolHashes.PSObject.Properties['RestrictedCliNative.dll']){throw 'Controller-built helper assembly pin is missing.'}
Initialize-AzuriteRestrictedNative -AssemblyPath (Join-Path $PSScriptRoot 'RestrictedCliNative.dll')
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent()
$outputDir=Join-Path $runDir 'output';$requestDir=Join-Path $runDir 'request';$workspace=Join-Path $runDir 'workspace';$exitCode=1
$result=[ordered]@{schema=1;runId=$control.runId;command=$control.command;bootstrapPid=$PID;targetSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;status='failed';contractSha256=$control.contractSha256;argumentsSha256=$control.argumentsSha256;jobOwned=$true;acceptance=$contract.acceptance;noSoundConfigured=$false}
try {
 $job.AnnounceAndAwaitController($control.runId)
 Assert-AzuriteRestrictedAcl $outputDir $control.controllerSid $control.targetSid Modify
 Assert-AzuriteRestrictedAcl $workspace $control.controllerSid $control.targetSid Modify
 Assert-AzuriteRestrictedAcl $requestDir $control.controllerSid $control.targetSid Request
 $identityPath=Join-Path $outputDir 'identity.json'
 & (Join-Path $PSScriptRoot 'Identity-Preflight.ps1') -AzuriteRunId $control.runId -AzuriteResultPath $identityPath
 $identity=Read-AzuriteBoundedJson $identityPath 32768
 $result.profilePathProof=Assert-AzuriteTargetIdentity $identity $control.targetSid $control.controllerSid $control.controllerProfile
 $result.identitySha256=Get-AzuriteSha256 $identityPath
 $name=[Security.Principal.WindowsIdentity]::GetCurrent().Name.Split('\')[-1]
 if($name -cne 'AzuriteTest'){throw 'Bootstrap is not the dedicated account.'}
 $install=Join-Path $control.labRoot 'install';Assert-AzuritePinnedInstall $install $contract
 $argPath=Join-Path $requestDir 'arguments.json';Assert-AzuriteRestrictedAcl $argPath $control.controllerSid $control.targetSid
 if((Get-AzuriteSha256 $argPath) -cne $control.argumentsSha256){throw 'Prepared business argv changed.'}
 $request=Read-AzuriteBoundedJson $argPath 4096
 if(@($request.PSObject.Properties).Count -ne 2 -or $request.argv -isnot [array] -or $request.argv.Count -ne 1 -or $request.argv[0] -cne $control.command -or $request.cwd -cne $workspace){throw 'Unsupported business argv.'}
 $responsePath=Join-Path $requestDir 'response.json';$unityLog=Join-Path $outputDir 'unity.log'
 if(Test-Path $responsePath){throw 'Response already exists before owned native start.'}
 if(Test-Path $unityLog){throw 'Unity log already exists before owned native start.'}
 if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length -ne 0){throw 'Another AzureArchive process exists.'}
 $remaining=[int]([datetime]::Parse($control.deadlineUtc).ToUniversalTime()-[datetime]::UtcNow).TotalMilliseconds
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
