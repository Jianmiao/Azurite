#requires -Version 5.1
param([Parameter(Mandatory)][string]$ControlPath)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
foreach($name in @('Microsoft.PowerShell.Security','Microsoft.PowerShell.Management','Microsoft.PowerShell.Utility')){Import-Module (Join-Path $PSHOME ('Modules\'+$name+'\'+$name+'.psd1')) -Force -DisableNameChecking}
foreach($name in @('RestrictedCliCore.psm1','HeadlessCore.psm1','CurrentAccountCli.psm1','EditorRouteCore.psm1')){Import-Module (Join-Path $PSScriptRoot $name) -DisableNameChecking}
$control=Read-AzuriteBoundedJson $ControlPath 65536
$spec=Get-AzuriteEditorRouteSpec $control.runId
if($control.schema -ne 1 -or $control.scope -cnotin @('Positive','ProbeMissing','InvalidManifest') -or $control.labRoot -cne $spec.labRoot -or $control.runRoot -cne $spec.runRoot -or $control.jobId -cne $spec.jobId -or $PSScriptRoot -cne (Join-Path $spec.runRoot 'tooling') -or $ControlPath -cne (Join-Path $PSScriptRoot 'control.json')){throw 'Bootstrap is not bound to one fixed fresh lab run.'}
$expected=@(Get-AzuriteEditorRouteToolNames)+@('editor-route-contract.json','EditorRouteReviewedInputs.json','RestrictedCliNative.dll')
if((@($control.toolHashes.PSObject.Properties.Name|Sort-Object) -join '|') -cne (@($expected|Sort-Object) -join '|')){throw 'Staged source/helper pin set differs.'}
Assert-AzuriteCurrentPrivatePath $spec.runRoot $control.controllerSid
Assert-AzuriteCurrentPrivatePath $PSScriptRoot $control.controllerSid
Assert-AzuriteCurrentPrivatePath $ControlPath $control.controllerSid
foreach($pin in $control.toolHashes.PSObject.Properties){$path=Join-Path $PSScriptRoot $pin.Name;Assert-AzuriteCurrentPrivatePath $path $control.controllerSid;Assert-AzuritePreparedHash $path $pin.Value}
$contract=Read-AzuriteBoundedJson (Join-Path $PSScriptRoot 'editor-route-contract.json') 1048576
$inputs=Read-AzuriteBoundedJson (Join-Path $PSScriptRoot 'EditorRouteReviewedInputs.json') 65536
$expectedContract=New-AzuriteEditorRouteContract $inputs (Read-AzuriteStartupContract) $control.scope
if(($contract|ConvertTo-Json -Depth 30 -Compress) -cne ($expectedContract|ConvertTo-Json -Depth 30 -Compress) -or $control.contractSha256 -cne $control.toolHashes.'editor-route-contract.json'){throw 'Staged contract is not the exact reviewed derivative.'}
Initialize-AzuriteRestrictedNative -AssemblyPath (Join-Path $PSScriptRoot 'RestrictedCliNative.dll')
$job=[Azurite.Restricted.OwnedJob]::CreateAndJoinCurrent()
$exitCode=1
$result=[ordered]@{schema=1;identityMode='CurrentAccount';scope=$control.scope;runId=$spec.runId;jobId=$spec.jobId;bootstrapPid=$PID;controllerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;status='failed';jobOwned=$true;contractSha256=$control.contractSha256;manifestSha256=$control.manifestSha256;argumentsSha256=$control.argumentsSha256;noSoundConfigured=$false;acceptance=$contract.acceptance}
try{
 $job.AnnounceAndAwaitController($spec.jobId)
 $identityPath=Join-Path $spec.outputRoot 'identity.json'
 & (Join-Path $PSScriptRoot 'Identity-Preflight.ps1') -AzuriteRunId $spec.runId -AzuriteResultPath $identityPath
 $identity=Read-AzuriteBoundedJson $identityPath 32768
 $result.profilePathProof=Assert-AzuriteCurrentIdentity $identity $control.controllerSid $control.controllerProfile $control.currentLocalLow $control.currentEnvironment
 if($identity.status -cne 'passed' -or $identity.pid -ne $PID -or $identity.runId -cne $spec.runId -or @($identity.unexpectedPrivileges).Count){throw 'Current-account token/preflight is not the admitted owned bootstrap.'}
 $result.identitySha256=Get-AzuriteSha256 $identityPath
 Assert-AzuritePreparedHash (Join-Path $spec.runRoot 'shared-state\before.json') $control.sharedStateBeforeSha256
 Assert-AzuritePreparedHash (Join-Path $spec.runRoot 'shared-state\scope.json') $control.sharedStateScopeSha256
 Assert-AzuritePinnedInstall $spec.installRoot $contract
 Assert-AzuriteEvidence $contract
 Assert-AzuritePreparedHash $spec.manifestPath $control.manifestSha256
 Assert-AzuritePreparedHash $spec.settingsPath $control.seedSha256
 Assert-AzuritePreparedHash $spec.controlSeedPath $control.controlSeedSha256
 $argPath=Join-Path $spec.requestRoot 'arguments.json';Assert-AzuritePreparedHash $argPath $control.argumentsSha256
 $argsJson=Read-AzuriteBoundedJson $argPath 4096
 if(@($argsJson.PSObject.Properties).Count -ne 2 -or $argsJson.argv -isnot [array] -or $argsJson.argv.Count -ne 1 -or $argsJson.argv[0] -cne 'capabilities' -or $argsJson.cwd -cne (Join-Path $spec.runRoot 'workspace')){throw 'Business request is not the exact resource-free native transport.'}
 foreach($name in @('response.json')){if(Test-Path -LiteralPath (Join-Path $spec.requestRoot $name)){throw 'Native response exists before native start.'}}
 foreach($name in @('unity.log','editor-isolation-guardian.jsonl','editor-isolation-evidence.jsonl')){if(Test-Path -LiteralPath (Join-Path $spec.outputRoot $name)){throw 'Native evidence exists before native start.'}}
 if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'Another AzureArchive process exists.'}
 $remaining=[int]((ConvertTo-AzuriteUtcInstant $control.deadlineUtc)-[datetime]::UtcNow).TotalMilliseconds
 if($remaining -lt 1000){throw 'Shared deadline expired before native start.'}
 $psi=New-AzuriteProcessInfo -FilePath (Join-Path $spec.installRoot 'AzureArchive.exe') -Arguments $spec.nativeArguments -WorkingDirectory $spec.installRoot
 Set-AzuriteEditorRouteChildEnvironment $psi $spec
 $result.nativeArguments=$spec.nativeArguments;$result.noSoundConfigured=$true
 $result.noSoundProofSha256=@($contract.evidence|Where-Object {$_.path.EndsWith('STARTUP-CONTRACT.md')})[0].sha256
 $watched=@('output','request','settings','workspace','cache','persistent','discovery'|ForEach-Object {Join-Path $spec.runRoot $_})+@(Join-Path $spec.installRoot 'BepInEx\LogOutput.log')
 $native=[Azurite.Restricted.ProcessPump]::Run($psi,$remaining,$job,262144,$watched,4194304)
 $result.aaPid=$native.Pid;$result.aaStartedUtc=$native.StartedUtc.ToString('o');$result.aaCompletedUtc=[datetime]::UtcNow.ToString('o');$result.aaExitCode=$native.ExitCode;$result.timedOut=$native.TimedOut;$result.windowViolation=$native.WindowViolation;$result.outputLimitExceeded=$native.OutputLimitExceeded;$result.processError=$native.Error
 [IO.File]::WriteAllText((Join-Path $spec.outputRoot 'aa.stdout.log'),$native.Stdout);[IO.File]::WriteAllText((Join-Path $spec.outputRoot 'aa.stderr.log'),$native.Stderr)
 Assert-AzuriteEditorRouteNativeStatus $native $control.scope
 $result.nativeQuiescence=Wait-AzuriteEditorRouteQuiescence $job $native $spec.runId $control.deadlineUtc
 if(-not $result.nativeQuiescence.quiescent){throw ('Owned descendants did not quiesce before evidence validation: '+$result.nativeQuiescence.reason)}
 $guardianPath=Join-Path $spec.outputRoot 'editor-isolation-guardian.jsonl';$probePath=Join-Path $spec.outputRoot 'editor-isolation-evidence.jsonl'
 $guardian=@(Read-AzuriteEditorRouteJsonl $guardianPath);$probe=@();if(Test-Path -LiteralPath $probePath){$probe=@(Read-AzuriteEditorRouteJsonl $probePath)}
 $result.guardianSha256=Get-AzuriteSha256 $guardianPath;$result.probeSha256=if($probe.Count){Get-AzuriteSha256 $probePath}else{$null}
 $result.savedSha256=Get-AzuriteSha256 $spec.settingsPath
 if($control.scope -ceq 'Positive'){
  $response=Read-AzuriteCliResponse (Join-Path $spec.requestRoot 'response.json') capabilities $native.StartedUtc
  $result.responseSha256=$response.Sha256;$result.responseBytes=$response.Bytes
  $result.proof=Assert-AzuriteEditorRouteEvidence $probe $guardian $spec $control.manifestSha256 $native.Pid $native.StartedUtc $result.aaCompletedUtc $result.savedSha256
 }else{
  if(Test-Path -LiteralPath (Join-Path $spec.requestRoot 'response.json')){throw 'Negative guard run unexpectedly reached native capabilities response.'}
  $result.proof=Assert-AzuriteEditorRouteNegativeEvidence $probe $guardian $spec $control.manifestSha256 $native.Pid $native.StartedUtc $result.aaCompletedUtc $control.scope
 }
 foreach($name in @('aa.stdout.log','aa.stderr.log','unity.log')){$path=Join-Path $spec.outputRoot $name;if((Get-Item -LiteralPath $path).Length -gt 4194304){throw 'Native log exceeded its bound.'};$result[$name+'Sha256']=Get-AzuriteSha256 $path}
 Assert-AzuriteEditorRouteEffects ([pscustomobject]@{spec=$spec;control=$control})
 $result.status='passed';$exitCode=0
}catch{$result.error=$_.Exception.Message;$result.privateFailure=Get-AzuriteEditorRoutePrivateFailure $_ 'bootstrap-validation'}
finally{
 try{Write-AzuriteFlushedJson (Join-Path $spec.outputRoot 'bootstrap-result.json') $result;[Console]::Out.Flush();[Console]::Error.Flush()}
 finally{$job.Terminate([uint32]$exitCode)}
}
