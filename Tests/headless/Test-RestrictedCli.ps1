# Harmless fixtures only. This suite never invokes AzureArchive.exe or provisions an account.
param([switch]$SkipProcessFixtures)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$module = Join-Path $repo 'tools\headless\RestrictedCliCore.psm1'
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw "FAIL: $Message" } }
function Refuses([scriptblock]$Action, [string]$Message) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Assert $failed $Message }
Assert (Test-Path -LiteralPath $module) 'restricted contract validator is not implemented'
Import-Module $module -Force
$root = Join-Path $repo ('artifacts\restricted-cli-tests\' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$started=[datetime]::UtcNow.AddSeconds(-1)
$response=Join-Path $root 'response.json'
$capFixture=Join-Path $PSScriptRoot 'fixtures\native-capabilities-20261108.json'
Assert ((Get-AzuriteSha256 $capFixture) -ceq 'A12FC49612372CB6A9F5AB56B86EFEC8F438C585F0BF3E1FAD033F66A787401F') 'native capabilities fixture bytes are bound to the first response'
$cap=[IO.File]::ReadAllText($capFixture)
$schema='{"api_version":"1","ok":true,"data":{"api_version":"1","$id":"aap://schema/api/1","$schema":"https://json-schema.org/draft/2020-12/schema","$defs":{"LineInput":{"type":"object","properties":{"text":{"type":"string"}}},"LinePatch":{"type":"object","properties":{"text":{"type":"string"}}}},"tools":[{"name":"fixture"}],"cli_commands":{"fixture":{}}},"warnings":[]}'
[IO.File]::WriteAllText($response,$cap)
$valid=Read-AzuriteCliResponse -Path $response -Command capabilities -StartedUtc $started
Assert ($valid.Bytes -gt 20 -and $valid.Sha256 -match '^[A-F0-9]{64}$') 'fresh native query envelope accepted with raw hash'
[IO.File]::WriteAllText($response,$schema)
Assert ((Read-AzuriteCliResponse -Path $response -Command schema -StartedUtc $started).Data.'$defs'.LineInput.type -ceq 'object') 'real schema definitions accepted'
foreach($bad in @('{}','{"status":"passed","checks":[{"name":"native","passed":true}]}','{"api_version":"1","ok":true,"data":{},"warnings":[]}','{"api_version":"1","ok":"true","data":{"a":1},"warnings":[]}','{"api_version":"1","ok":false,"data":{"a":1},"warnings":[]}', '{broken')) {
 [IO.File]::WriteAllText($response,$bad); Refuses { Read-AzuriteCliResponse -Path $response -Command capabilities -StartedUtc $started } 'malformed, failed or synthetic response refused'
}
[IO.File]::WriteAllText($response,$cap)
Refuses { Read-AzuriteCliResponse -Path $response -Command schema -StartedUtc $started } 'capability data cannot satisfy schema'
[IO.File]::WriteAllText($response,$schema.Replace('"properties":{"text":{"type":"string"}}','"properties":{}'))
Refuses { Read-AzuriteCliResponse -Path $response -Command schema -StartedUtc $started } 'empty definition bodies refused'
[IO.File]::WriteAllText($response,('x' * 1048577))
Refuses { Read-AzuriteCliResponse -Path $response -Command schema -StartedUtc $started } 'oversized response refused before parsing'
[IO.File]::WriteAllText($response,$cap); (Get-Item $response).LastWriteTimeUtc=$started.AddDays(-1)
Refuses { Read-AzuriteCliResponse -Path $response -Command capabilities -StartedUtc $started } 'stale response refused'
Refuses { Read-AzuriteCliResponse -Path (Join-Path $root 'absent.json') -Command capabilities -StartedUtc $started } 'missing response refused'
Write-Output 'PASS: fresh meaningful native envelopes; stale/malformed/oversize/missing/failed/synthetic rejection'
[IO.File]::WriteAllText($response,'{"api_version":"1","ok":true,"data":{"api_version":"1","format_version":2,"schema_uri":"aap://schema/api/1","commands":["schema","capabilities"]},"warnings":[]}')
Refuses { Read-AzuriteCliResponse -Path $response -Command capabilities -StartedUtc $started } 'old synthetic top-level commands is not native capabilities'

$install=Join-Path $root 'install'; [void][IO.Directory]::CreateDirectory($install)
foreach($d in @('BepInEx\plugins','BepInEx\patchers','profiles\AzuriteAcceptance\configs','mods\Azurite\1.0.38','AzureArchive_Data\Plugins\x86_64')) { [void][IO.Directory]::CreateDirectory((Join-Path $install $d)) }
[IO.File]::WriteAllText((Join-Path $install 'ActiveProfile.txt'),'AzuriteAcceptance')
[IO.File]::WriteAllText((Join-Path $install 'AzureArchive.exe'),'harmless fixture, never executable')
[IO.File]::WriteAllText((Join-Path $install 'profiles\AzuriteAcceptance\modconfig.json'),'{"EnabledMods":[{"name":"Azurite","version":"1.0.38"}]}')
$pins=@(); foreach($file in @('ActiveProfile.txt','AzureArchive.exe','profiles\AzuriteAcceptance\modconfig.json')) { $pins+=@{path=$file;sha256=(Get-FileHash (Join-Path $install $file)).Hash} }
$contract=[pscustomobject]@{schema=1;contractId='fixture-static';files=$pins;exactDirectories=@(@{path='BepInEx\plugins';files=@()},@{path='BepInEx\patchers';files=@()});profile='AzuriteAcceptance';modName='Azurite';modVersion='1.0.38'}
Assert-AzuritePinnedInstall -Install $install -Contract $contract
[IO.File]::WriteAllText((Join-Path $install 'BepInEx\plugins\extra.dll'),'x')
Refuses { Assert-AzuritePinnedInstall -Install $install -Contract $contract } 'extra ordinary plugin refused'
Remove-Item -LiteralPath (Join-Path $install 'BepInEx\plugins\extra.dll')
[IO.File]::WriteAllText((Join-Path $install 'AzureArchive.exe'),'changed')
Refuses { Assert-AzuritePinnedInstall -Install $install -Contract $contract } 'changed AA hash refused'
[IO.File]::WriteAllText((Join-Path $install 'ActiveProfile.txt'),'other')
Refuses { Assert-AzuritePinnedInstall -Install $install -Contract $contract } 'wrong profile refused'
$pinFile=Join-Path $root 'unapproved-contract.json'; [IO.File]::WriteAllText($pinFile,'{}')
Refuses { Read-AzuriteStartupContract -Path $pinFile } 'unapproved contract refused'
Write-Output 'PASS: pinned install rejects binary/profile/plugin drift and unapproved contracts'

$profileFixture=Join-Path $root 'identity-target'
foreach($suffix in @('AppData\Local','AppData\LocalLow','AppData\Roaming','AppData\Local\Temp')){[void][IO.Directory]::CreateDirectory((Join-Path $profileFixture $suffix))}
$identity=[pscustomobject]@{identitySid='S-1-5-21-1-2-3-1002';isAdministrator=$false;hiveLoaded=$true;unexpectedPrivileges=@();userProfile=$profileFixture;localAppData=(Join-Path $profileFixture 'AppData\Local');localLow=(Join-Path $profileFixture 'AppData\LocalLow');environmentProfile=$profileFixture;environmentLocalAppData=(Join-Path $profileFixture 'AppData\Local');environmentAppData=(Join-Path $profileFixture 'AppData\Roaming');environmentTemp=(Join-Path $profileFixture 'AppData\Local\Temp');environmentTmp=(Join-Path $profileFixture 'AppData\Local\Temp')}
[void](Assert-AzuriteTargetIdentity $identity $identity.identitySid 'S-1-5-21-1-2-3-1001' 'C:\Users\Controller')
$identity.environmentTemp='C:\Users\Controller\AppData\Local\Temp'
Refuses { Assert-AzuriteTargetIdentity $identity $identity.identitySid 'S-1-5-21-1-2-3-1001' 'C:\Users\Controller' } 'controller-derived environment refused'
Refuses { Assert-AzuriteTargetIdentity $identity $identity.identitySid $identity.identitySid 'C:\Users\Controller' } 'controller identity refused'
Write-Output 'PASS: target identity rejects controller SID and profile environment contamination'

Import-Module (Join-Path $repo 'tools\headless\HeadlessCore.psm1') -Force
$aclLab=Join-Path $root 'acl-lab';[void][IO.Directory]::CreateDirectory($aclLab)
$controller=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;$fakeSid='S-1-5-32-546' # Built-in Guests: structural ACL fixture, not a logon identity.
Set-AzuriteLabAcl -LabRoot $aclLab -Path $aclLab -ControllerSid $controller -TestSid $fakeSid
$requestAcl=Join-Path $aclLab 'request';[void][IO.Directory]::CreateDirectory($requestAcl)
Set-AzuriteRequestAcl $aclLab $requestAcl $controller $fakeSid
$prepared=Join-Path $requestAcl 'arguments.json';[IO.File]::WriteAllText($prepared,'{}')
Set-AzuriteLabAcl -LabRoot $aclLab -Path $prepared -ControllerSid $controller -TestSid $fakeSid
Assert-AzuriteRestrictedAcl $prepared $controller $fakeSid
Assert-AzuriteRestrictedAcl $requestAcl $controller $fakeSid Request
$nativeTemp=Join-Path $requestAcl 'response.unique.tmp';$nativeFinal=Join-Path $requestAcl 'response.json'
[IO.File]::WriteAllText($nativeTemp,'one');[IO.File]::AppendAllText($nativeTemp,'two');[IO.File]::Move($nativeTemp,$nativeFinal)
Assert ([IO.File]::ReadAllText($nativeFinal) -ceq 'onetwo') 'native Finish create/reopen/atomic-rename lifecycle works (fixture creator)'
[IO.File]::Delete($nativeFinal)
& icacls.exe $prepared '/grant' '*S-1-1-0:W' | Out-Null
Refuses { Assert-AzuriteRestrictedAcl $prepared $controller $fakeSid } 'broad prepared-file write grant refused'
Set-AzuriteLabAcl -LabRoot $aclLab -Path $prepared -ControllerSid $controller -TestSid $fakeSid
Write-Output 'PASS: protected request/file ACLs separate file creation from prepared request replacement (structural fixture)'
$logs=Join-Path $root 'bounded-logs';[void][IO.Directory]::CreateDirectory($logs);$huge=Join-Path $logs 'unity.log'
[IO.File]::WriteAllText($huge,('x'*8192))
Assert ([bool](Get-Command Limit-AzuriteRetainedArtifacts -ErrorAction SilentlyContinue)) 'retained oversized artifacts need a bounded cleanup implementation'
$trims=@(Limit-AzuriteRetainedArtifacts -Directory $logs -MaxFileBytes 4096 -MaxTotalBytes 8192)
Assert ((Get-Item $huge).Length -eq 4096 -and $trims.Count -eq 1 -and $trims[0].originalBytes -eq 8192) 'failed oversized log retention is capped and recorded'
$loader=Join-Path $root 'LogOutput.log';$oldLoader=Join-Path $root 'previous-LogOutput.log';$newLoader=Join-Path $root 'captured-LogOutput.log'
[IO.File]::WriteAllText($loader,'old loader evidence')
Assert ([bool](Get-Command Move-AzuritePreviousLoaderLog -ErrorAction SilentlyContinue)) 'loader evidence needs old/new separation'
$old=Move-AzuritePreviousLoaderLog $loader $oldLoader
Assert (-not(Test-Path $loader) -and (Test-Path $oldLoader) -and $old.Present) 'old loader log archived out of native path before start'
$missing=Copy-AzuriteFreshLoaderLog $loader $newLoader $started
Assert (-not $missing.Present -and -not(Test-Path $newLoader)) 'missing new loader log cannot use previous log'
[IO.File]::WriteAllText($loader,'new loader evidence')
$fresh=Copy-AzuriteFreshLoaderLog $loader $newLoader $started
Assert ($fresh.Present -and $fresh.Fresh -and $fresh.Complete -and $fresh.Sha256 -ceq (Get-FileHash $newLoader).Hash) 'fresh loader log captured with exact hash'
Remove-Item $newLoader;(Get-Item $loader).LastWriteTimeUtc=$started.AddDays(-1)
$stale=Copy-AzuriteFreshLoaderLog $loader $newLoader $started
Assert (-not $stale.Fresh) 'stale loader evidence rejected'
Assert ([bool](Get-Command Get-AzuritePinDrift -ErrorAction SilentlyContinue)) 'pinned dependency changes need recorded drift'
$drift=@(Get-AzuritePinDrift $install $contract)
Assert ($drift.Count -eq 2 -and @($drift|Where-Object path -eq 'AzureArchive.exe').Count -eq 1) 'all changed fixed inputs reported without resealing'
Write-Output 'PASS: old/new loader evidence separation, freshness and pinned dependency drift reporting'

$lab=Join-Path $root 'unprepared-lab';[void][IO.Directory]::CreateDirectory($lab)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
@{schema=1;purpose='AzuriteHeadlessLab';labId=[guid]::NewGuid().ToString();controllerSid=$sid;dailyInstallPath=(Join-Path $root 'daily')}|ConvertTo-Json|Set-Content (Join-Path $lab 'lab.marker.json')
$before=@(Get-ChildItem $lab -Force -Recurse | ForEach-Object {$_.FullName}) -join '|'
$plan=& (Join-Path $repo 'tools\headless\Invoke-AzuriteRestrictedCli.ps1') -LabRoot $lab -Command capabilities -PlanOnly
$after=@(Get-ChildItem $lab -Force -Recurse | ForEach-Object {$_.FullName}) -join '|'
Assert ($before -ceq $after -and -not $plan.WillLaunch -and -not $plan.Ready -and $plan.Failures.Count -gt 0) 'PlanOnly reports missing prerequisites without writes'
Assert (($plan.Failures -join '|') -match 'account manifest is missing' -and ($plan.Failures -join '|') -match 'DPAPI credential is missing') 'PlanOnly distinguishes missing identity and credential prerequisites'
Refuses { & (Join-Path $repo 'tools\headless\Invoke-AzuriteRestrictedCli.ps1') -LabRoot $lab -Command capabilities } 'unprepared identity/credential cannot start'
Assert (-not (Test-Path (Join-Path $lab 'runs'))) 'refused run created no process artifacts'
Write-Output 'PASS: PlanOnly and missing identity/credentials fail without writes/start'

if (-not $SkipProcessFixtures) {
 Initialize-AzuriteRestrictedNative
 $shell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
 $fixture=Join-Path $PSScriptRoot 'Restricted-FixtureBootstrap.ps1'
 $nativeAssembly=Join-Path $root 'RestrictedCliNative.dll'
 [void](New-AzuriteNetFrameworkHelper (Join-Path $repo 'tools\headless\RestrictedCliNative.cs') $nativeAssembly)
 $expected=@('','space value','quote"value','trail\','two\\"slashes','C:\white space\ending\')
 $argFile=Join-Path $root 'arg-input.json'; [IO.File]::WriteAllText($argFile,($expected|ConvertTo-Json -Compress))
 $csc=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $fake=Join-Path $root 'RestrictedFake.exe'
 $source=Join-Path $root 'Restricted-FixtureConsole.cs';Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Restricted-FixtureConsole.cs.txt') -Destination $source
 & $csc /nologo /target:exe ("/out:$fake") $source
 Assert ($LASTEXITCODE -eq 0) 'harmless fixture compilation'
 $replacement=Join-Path $requestAcl 'replacement.tmp';[IO.File]::WriteAllText($replacement,'replacement')
 Set-AzuriteLabAcl -LabRoot $aclLab -Path $replacement -ControllerSid $controller -TestSid $fakeSid -TestRights Modify
 $aclRun=[Azurite.Restricted.ProcessPump]::Run((New-AzuriteProcessInfo -FilePath $fake -Arguments @('acl',$prepared,$replacement) -WorkingDirectory $root),5000,$null,4096,@(),1048576)
 Assert (-not (Get-AzuriteProcessStatus $aclRun).failed -and $aclRun.Stdout -match 'acl-blocks=3' -and [IO.File]::ReadAllText($prepared) -ceq '{}') 'restricted fixture token cannot truncate/delete/replace prepared request'
 $psi=New-AzuriteProcessInfo -FilePath $fake -Arguments (@('echo')+$expected) -WorkingDirectory $root
 $echo=[Azurite.Restricted.ProcessPump]::Run($psi,10000,$null,4096,@(),1048576)
 Assert (-not (Get-AzuriteProcessStatus $echo).failed) 'argument fixture exited without monitor errors'
 $actual=@($echo.Stdout.Trim() -split '\|' | ForEach-Object {[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_))})
 Assert (($actual|ConvertTo-Json -Compress) -ceq ($expected|ConvertTo-Json -Compress)) 'native argv whitespace/quotes/backslashes/empty fidelity'
 $controllerEnv=[Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','Process')
 $userEnv=[Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','User')
 $machineEnv=[Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','Machine')
 $psi=New-AzuriteProcessInfo -FilePath $fake -Arguments @('env') -WorkingDirectory $root
 Set-AzuriteCliChildEnvironment -StartInfo $psi -RequestDirectory $root
 $envResult=[Azurite.Restricted.ProcessPump]::Run($psi,10000,$null,4096,@(),1048576)
 Assert (-not (Get-AzuriteProcessStatus $envResult).failed) 'environment fixture exited without monitor errors'
 Assert ($envResult.Stdout.Trim() -ceq $root) 'only child receives sidecar environment'
 Assert ([Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','Process') -ceq $controllerEnv) 'controller process environment unchanged'
 Assert ([Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','User') -ceq $userEnv -and [Environment]::GetEnvironmentVariable('AZUREARCHIVE_CLI_DIRECTORY','Machine') -ceq $machineEnv) 'registry environments unchanged'
 $unrelated=[Diagnostics.Process]::Start((New-AzuriteProcessInfo -FilePath $fake -Arguments @('leaf') -WorkingDirectory $root))
 try {
  foreach($mode in @('Normal','Timeout','PipeLeak')) {
   $pidPath=Join-Path $root ($mode+'.json')
   $fixtureRunId=[guid]::NewGuid().ToString('N')
   $psi=New-AzuriteProcessInfo -FilePath $shell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$fixture,'-Module',$module,'-AssemblyPath',$nativeAssembly,'-RunId',$fixtureRunId,'-Fake',$fake,'-Mode',$mode,'-PidPath',$pidPath) -WorkingDirectory $root
   $watch=[Diagnostics.Stopwatch]::StartNew()
   $run=[Azurite.Restricted.ProcessPump]::RunBootstrap($psi,6500,5000,$fixtureRunId,8192,@(),1048576)
   Assert ($watch.Elapsed.TotalSeconds -lt 12.5) 'one runtime deadline plus bounded cleanup grace bounds process and inherited pipes'
   Assert (Test-Path $pidPath) 'fixture captured owned descendant identities'
   $pids=Get-Content $pidPath -Raw|ConvertFrom-Json
   foreach($owned in @($pids.pids)) { Assert (-not (Get-Process -Id $owned -ErrorAction SilentlyContinue)) 'job-owned child and grandchild are dead' }
   Assert ([bool]$run.CleanupVerified) 'R1 return boundary has no authoritative owned-job drain proof'
   Assert ($run.JobObserved -and $run.DirectProcessExited -and $run.JobDrained -and $run.RemainingOwnedProcesses -eq 0) 'R1 kernel job/direct handle quiescence missing at return'
   Assert (-not $unrelated.HasExited) 'unrelated process survives owned cleanup'
   if($mode -eq 'Normal') { Assert (-not (Get-AzuriteOuterProcessStatus $run).failed) 'flushed normal bootstrap result and intended exit code without monitor errors' }
   else { Assert ($run.TimedOut -or $run.ExitCode -eq 124) 'timeout or inherited-pipe deadline reported' }
   if($mode -eq 'PipeLeak'){Assert ($run.Stdout -match 'fixture deadline=True') 'inherited handles actually exercise pipe deadline after child exit'}
   $lastVerified=$run
  }
  foreach($mode in @('NoHandshake','DirtyHandshake')){
   $id=[guid]::NewGuid().ToString('N');$pidPath=Join-Path $root ($mode+'.json')
   $psi=New-AzuriteProcessInfo -FilePath $shell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$fixture,'-Module',$module,'-AssemblyPath',$nativeAssembly,'-RunId',$id,'-Fake',$fake,'-Mode',$mode,'-PidPath',$pidPath) -WorkingDirectory $root
   $bad=[Azurite.Restricted.ProcessPump]::RunBootstrap($psi,3500,5000,$id,8192,@(),1048576)
   Assert (-not $bad.CleanupVerified -and -not $bad.JobObserved -and $bad.DirectProcessExited) 'R1 uncertain/mixed pre-child job handshake must remain cleanup-unverified'
   if($mode -eq 'DirtyHandshake'){Assert ($bad.Error -match 'exactly the pre-child bootstrap') 'R1 non-bootstrap pre-ACK member was not refused'}
   $gateLab=Join-Path $root ('quarantine-'+$mode);[void][IO.Directory]::CreateDirectory((Join-Path $gateLab 'secrets'))
   Set-AzuriteLabAcl -LabRoot $gateLab -Path (Join-Path $gateLab 'secrets') -ControllerSid $controller
   [void](Start-AzuriteCleanupGate $gateLab $id $root $controller)
   $mutable=Join-Path $gateLab 'mutable.log';[IO.File]::WriteAllText($mutable,('x'*8192));$beforeHash=Get-AzuriteSha256 $mutable
   Refuses {Assert-AzuriteCleanupConfirmed $bad;Limit-AzuriteRetainedArtifacts -Directory $gateLab -MaxFileBytes 16} 'R1 unverified cleanup must not trim mutable evidence'
   Assert ((Get-AzuriteSha256 $mutable) -ceq $beforeHash) 'R1 mutable evidence changed before quiescence'
   Refuses {Complete-AzuriteCleanupGate $gateLab $id $controller $bad} 'R1 unverified quarantine was auto-cleared'
   Refuses {Assert-AzuriteCleanupGateClear $gateLab} 'R1 next native run was not blocked'
   $quarantinedPlan=& (Join-Path $repo 'tools\headless\Invoke-AzuriteRestrictedCli.ps1') -LabRoot $gateLab -Command capabilities -PlanOnly
   Assert (($quarantinedPlan.Failures -join '|') -match 'quarantine/pending marker exists' -and -not $quarantinedPlan.Ready) 'R1 pending quarantine did not block production PlanOnly'
   Assert (-not $unrelated.HasExited) 'R1 rejected handshake killed an unrelated process'
  }
  $clearLab=Join-Path $root 'verified-gate';[void][IO.Directory]::CreateDirectory((Join-Path $clearLab 'secrets'))
  Set-AzuriteLabAcl -LabRoot $clearLab -Path (Join-Path $clearLab 'secrets') -ControllerSid $controller
  [void](Start-AzuriteCleanupGate $clearLab $lastVerified.RunId $root $controller)
  Refuses {Complete-AzuriteCleanupGate $clearLab ([guid]::NewGuid().ToString('N')) $controller $lastVerified} 'R1 wrong run could clear the pending gate'
  Complete-AzuriteCleanupGate $clearLab $lastVerified.RunId $controller $lastVerified
  Assert-AzuriteCleanupGateClear $clearLab
  Write-Output 'PASS R1: pre-child job handoff, authoritative return-boundary drain, no-ACK refusal and persistent quarantine protect mutable evidence and next starts'
 } finally { if(-not $unrelated.HasExited){$unrelated.Kill()};$unrelated.Dispose() }
 Write-Output 'PASS: restricted-token prepared input refusal; native argv, child-only environment, owned nested cleanup, unrelated survival, total pipe deadline (fixture identity only)'
}
Write-Output ('PASS: restricted CLI offline suite; fixtures retained at '+$root)
