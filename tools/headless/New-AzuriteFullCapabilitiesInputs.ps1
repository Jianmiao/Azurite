[CmdletBinding()]
param([string]$InputRecordPath,[string]$OutputDirectory)

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$script:InputsRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:InputsOwner='S-1-5-21-222280291-4039000738-1914192245-1001'
$script:InputsLab='F:\AzuriteLabs\Editor-20261108-normal'
$script:InputsVolume='\\?\Volume{a7c77c39-d000-4faf-bbf8-cc8ca0c743cb}\'
$script:InputsSources=@('RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1','FullRestrictedCapabilitiesPlan.psm1','FullRestrictedStageFileSecurity.dll','helper-build.json','tooling-manifest.json')
$script:InputsManifest=@('facts.json','plan.json','expected-acl-manifest.json','identity-baseline.json','scope.json','before.json','FullRestrictedStageFileSecurity.dll','helper-build.json','RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1','FullRestrictedCapabilitiesPlan.psm1','tooling-manifest.json')

function Get-InputsHash([byte[]]$Bytes){$sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}
function ConvertTo-InputsBytes($Value){
 $json=ConvertTo-Json -InputObject $Value -Depth 90 -Compress
 foreach($pair in @(@('\u003c','<'),@('\u003e','>'),@('\u0026','&'),@('\u0027',"'"))){$json=$json.Replace($pair[0],$pair[1])}
 return [Text.UTF8Encoding]::new($false).GetBytes($json)
}
function Sort-InputsOrdinal([string[]]$Values){$copy=[string[]]@($Values);[Array]::Sort($copy,[StringComparer]::Ordinal);return ,$copy}
function ConvertFrom-InputsJson([byte[]]$Bytes){$text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes);$p=@{InputObject=$text;ErrorAction='Stop'};if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$p.DateKind='String'};return ConvertFrom-Json @p}
function Assert-Inputs($Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Assert-InputsShape($Value,[string[]]$Names,[string]$Label){
 Assert-Inputs ($Value -is [pscustomobject]) ($Label+' must be an object')
 $actual=@($Value.PSObject.Properties|ForEach-Object Name)
 Assert-Inputs ($actual.Count -eq $Names.Count -and @($actual|Where-Object {$_ -cnotin $Names}).Count -eq 0 -and @($Names|Where-Object {$_ -cnotin $actual}).Count -eq 0) ($Label+' exact shape differs')
}
function Assert-InputsAcyclic($Value,[string[]]$Forbidden,[string]$Label){
 foreach($name in @($Value.PSObject.Properties|ForEach-Object Name)){Assert-Inputs ($name -cnotin $Forbidden) ($Label+' contains reverse approval/receipt dependency: '+$name)}
}
function Assert-InputsCPath([string]$Path){
 Assert-Inputs (-not [string]::IsNullOrWhiteSpace($Path)) 'C input path is missing'
 $full=[IO.Path]::GetFullPath($Path)
 Assert-Inputs ($full.StartsWith($script:InputsRepo+'\', [StringComparison]::OrdinalIgnoreCase) -and $full.StartsWith('C:\',[StringComparison]::OrdinalIgnoreCase)) 'Input must be inside the C workspace'
 $cursor=$full;while($cursor -and $cursor.StartsWith($script:InputsRepo,[StringComparison]::OrdinalIgnoreCase)){$item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop;Assert-Inputs (-not($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'C input reparse ancestry refused';$cursor=Split-Path -Parent $cursor}
 return $full
}
function Assert-InputsOutputPath([string]$Path,[bool]$MustBeAbsent){
 $root=[IO.Path]::GetFullPath((Join-Path $script:InputsRepo 'artifacts\aa-fr-inputs'))+'\'
 $full=[IO.Path]::GetFullPath($Path)
 Assert-Inputs ($full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -and $full.StartsWith('C:\',[StringComparison]::OrdinalIgnoreCase)) 'Dry output must be under C aa-fr-inputs'
 if($MustBeAbsent){Assert-Inputs (-not [IO.Directory]::Exists($full) -and -not [IO.File]::Exists($full)) 'Dry output directory must be fresh'}
 $cursor=$full
 while($cursor){
  $item=Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
  if($item){Assert-Inputs (-not($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $item.PSIsContainer) ('Dry output ancestor is linked or not a directory: '+$cursor)}
  $parent=Split-Path -Parent $cursor
  if([string]::IsNullOrEmpty($parent) -or $parent -ceq $cursor){break}
  $cursor=$parent
 }
 return $full
}
function Read-InputsPinned($Pin,[long]$Maximum){
 Assert-InputsShape $Pin @('path','sha256') 'C pin'
 Assert-Inputs ($Pin.sha256 -cmatch '^[0-9a-f]{64}$' -and $Pin.sha256 -cne ('0'*64)) 'Expected C SHA-256 must be independently supplied'
 $path=Assert-InputsCPath $Pin.path;$item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 Assert-Inputs (-not $item.PSIsContainer -and $item.Length -ge 2 -and $item.Length -le $Maximum) 'Pinned C input is missing or oversized'
 $bytes=[IO.File]::ReadAllBytes($path)
 Assert-Inputs ($bytes.Length -eq $item.Length -and (Get-InputsHash $bytes) -ceq $Pin.sha256) 'Pinned C input hash/length differs'
 return [pscustomobject]@{path=$path;bytes=$bytes;sha256=$Pin.sha256;length=$bytes.Length}
}
function Assert-InputsBaseline($Baseline,$Capture,[string]$RunId,$Plan,[string]$BaselineSha){
 Assert-InputsShape $Baseline @('schema','scope','runId','objects') 'Identity baseline'
 Assert-Inputs ($Baseline.schema -eq 1 -and $Baseline.scope -ceq 'preapproval-stage-object-identities' -and $Baseline.runId -ceq $RunId -and $Baseline.objects -is [array] -and $Baseline.objects.Count -eq 1228) 'Completed baseline scope/count differs'
 Assert-Inputs ($Capture.schema -eq 1 -and $Capture.scope -ceq 'read-only-stage-baseline-capture' -and $Capture.runId -ceq $RunId -and $Capture.success -eq $true -and $Capture.observedObjects -eq 1228 -and $Capture.baselineSha256 -ceq $BaselineSha -and $Capture.lease.state -ceq 'released' -and $Capture.runtimeAllowed -eq $false -and $Capture.launchPermission -eq $false -and [string]::IsNullOrEmpty($Capture.error)) 'Real completed baseline capture receipt missing or failed'
 $captureTime=[DateTimeOffset]::Parse($Capture.observedUtc,[Globalization.CultureInfo]::InvariantCulture)
 $leaseStart=[DateTimeOffset]::Parse($Capture.lease.heldFromUtc,[Globalization.CultureInfo]::InvariantCulture)
 $leaseEnd=[DateTimeOffset]::Parse($Capture.lease.releasedUtc,[Globalization.CultureInfo]::InvariantCulture)
 Assert-Inputs ($Capture.lease.mutex -ceq 'Global\AzuriteRestrictedCli-AA' -and $leaseStart -le $leaseEnd -and $leaseEnd -le $captureTime.AddSeconds(2) -and $Capture.priority -ceq 'BelowNormal during capture' -and $Capture.rawDescriptorSections -ceq 'Owner,Group,Access; SACL not read') 'Completed read-only baseline lease/priority/descriptor evidence differs'
 $expected=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $install=$Plan.LabRoot+'\install'
 foreach($row in @($Plan.Grants.files)+@($Plan.Grants.directories)){
  $path=if($row.path -cmatch '^F:\\'){$row.path}elseif($row.path -ceq '.'){$install}else{$install+'\'+$row.path.Replace('/','\')}
  [void]$expected.Add($path)
 }
 $attempt=Join-Path $Plan.LabRoot '.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9'
 $anchors=@(
  (Join-Path 'F:\AzuriteLabs' '.azurite-phase0-parent.json'),
  (Join-Path $Plan.LabRoot 'lab.marker.json'),
  (Join-Path $Plan.LabRoot '.phase0-staging\install-creation-seal.json'),
  (Join-Path $attempt 'complete.json'),
  (Join-Path $attempt 'files.jsonl'),
  (Join-Path $attempt 'start.json'),
  (Join-Path $attempt 'seal-complete.json')
 )
 foreach($path in $anchors){[void]$expected.Add($path)}
 Assert-Inputs ($expected.Count -eq 1228) ('Expected baseline path count differs: '+$expected.Count)
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($object in $Baseline.objects){
  Assert-InputsShape $object @('path','identity','sddl','bytes') 'Baseline object'
  Assert-Inputs ($expected.Contains($object.path) -and $seen.Add($object.path) -and $object.identity -cmatch '^[a-f0-9]{32}$' -and $ids.Add($object.identity) -and ($object.bytes -is [int] -or $object.bytes -is [long]) -and $object.bytes -ge 0) 'Baseline object path/identity/bytes differs'
  $sd=[Security.AccessControl.RawSecurityDescriptor]::new($object.sddl)
  Assert-Inputs ($sd.Owner.Value -ceq $script:InputsOwner -and $object.sddl -ceq $sd.GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access')) 'Baseline Owner/Group/Access descriptor is not canonical'
 }
 Assert-Inputs ($seen.Count -eq 1228) 'Baseline path coverage incomplete'
 $stagePin=[pscustomobject]@{path=(Join-Path $script:InputsRepo 'artifacts\editor-normal-install-preparation\source-bound-manifest-v2.json');sha256=$Plan.ReadEnvelope.manifestSha256}
 $stage=ConvertFrom-InputsJson (Read-InputsPinned $stagePin 1048576).bytes
 $pins=@{};foreach($entry in $stage.entries){$pins[$install+'\'+$entry.path.Replace('/','\')]=[long]$entry.bytes}
 foreach($object in $Baseline.objects){if($pins.ContainsKey($object.path)){Assert-Inputs ($object.bytes -eq $pins[$object.path]) ('Baseline staged file size differs: '+$object.path)}}
}
function Assert-InputsHarmless($Receipt,$Sources,[string]$EvidenceClass){
 Assert-Inputs ($EvidenceClass -cne 'reviewed-real' -or -not $Receipt.PSObject.Properties['testOnlySimulated']) 'Simulated harmless receipt cannot be promoted to reviewed-real'
 Assert-Inputs ($Receipt.schema -eq 1 -and $Receipt.scope -ceq 'actual-native-full-restricted-harmless-lifecycle' -and $Receipt.passed -eq $true -and $Receipt.productionTargetExecuted -eq $false -and $Receipt.desktopLeaseAcrossBootstrapExit -eq $true -and $Receipt.actualSuspendedTokenAndJob -eq $true -and $Receipt.pipeHandleListAndEOF -eq $true -and $Receipt.cleanupVerified -eq $true -and $Receipt.nativeSourceSha256 -ceq $Sources['RestrictedCliFullRestricted.cs'].sha256 -and $Receipt.sharedNativeSourceSha256 -ceq $Sources['RestrictedCliNative.cs'].sha256) 'Separately reviewed same-source harmless lifecycle receipt is incomplete'
}
function Assert-InputsPreflight($Preflight,$Historical,$Contract,$Capture,$InputRecord,[string]$PreflightSha){
 Assert-InputsShape $Preflight @('schema','scope','evidenceClass','runId','labRoot','installRoot','observedUtc','stageManifestSha256','cacheContractSha256','historicalWarmResultSha256','actualEnvironment','actualArgv','sidecar','warmInterop') 'Fresh preflight'
 Assert-Inputs ($Preflight.schema -eq 1 -and $Preflight.scope -ceq 'fresh-full-capabilities-pretransition-observation' -and $Preflight.evidenceClass -ceq $InputRecord.evidenceClass -and $Preflight.runId -ceq $InputRecord.runId -and $Preflight.labRoot -ceq $Contract.labRoot -and $Preflight.installRoot -ceq $Contract.installRoot -and $Preflight.stageManifestSha256 -ceq $Contract.stage.manifestSha256 -and $Preflight.cacheContractSha256 -ceq $Contract.cachePrerequisites.contractSha256 -and $Preflight.historicalWarmResultSha256 -ceq $Contract.cachePrerequisites.warmResultSha256) 'Fresh preflight target/run/source pins differ'
 Assert-Inputs ($Preflight.observedUtc -is [string] -and $Preflight.observedUtc -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') 'Fresh preflight observation time is not canonical UTC'
 $observed=[DateTimeOffset]::Parse($Preflight.observedUtc,[Globalization.CultureInfo]::InvariantCulture)
 $baseline=[DateTimeOffset]::Parse($Capture.observedUtc,[Globalization.CultureInfo]::InvariantCulture)
 Assert-Inputs ($observed -ge $baseline) 'Fresh preflight observation precedes completed baseline'
 Assert-InputsShape $Preflight.actualEnvironment @('BEPINEX_GAME_ASSEMBLY_PATH') 'Fresh environment observation'
 $override=$Preflight.actualEnvironment.BEPINEX_GAME_ASSEMBLY_PATH
 Assert-InputsShape $override @('present','value') 'GameAssembly override observation'
 Assert-Inputs ($override.present -is [bool] -and $override.present -eq $false -and $null -eq $override.value) 'Actual BEPINEX_GAME_ASSEMBLY_PATH presence/value differs'
 $run=$Contract.labRoot+'\runs\restricted-'+$InputRecord.runId
 $expectedArgv=@('-batchmode','-nographics','-disableaudio','-logFile',($run+'\output\unity.log'))
 Assert-Inputs ($Preflight.actualArgv -is [array] -and $Preflight.actualArgv.Count -eq 5 -and ($Preflight.actualArgv -join [char]10) -ceq ($expectedArgv -join [char]10)) 'Actual selected capabilities argv/log path differs'
 Assert-InputsShape $Preflight.sidecar @('environmentKey','directory','command','cwd') 'Fresh sidecar observation'
 Assert-Inputs ($Preflight.sidecar.environmentKey -ceq 'AZUREARCHIVE_CLI_DIRECTORY' -and $Preflight.sidecar.directory -ceq ($run+'\request') -and $Preflight.sidecar.command -ceq 'capabilities' -and $Preflight.sidecar.cwd -ceq ($run+'\workspace')) 'Actual capabilities sidecar selection differs'
 Assert-InputsShape $Preflight.warmInterop @('computedStamp','generatorVersion','cpp2ilVersion','optionalRenameMapPresent','orderedEvents','inputFiles') 'Fresh warm-order observation'
 $warm=$Preflight.warmInterop
 Assert-Inputs ($warm.computedStamp -ceq $Contract.cachePrerequisites.stamp -and $warm.generatorVersion -ceq $Contract.cachePrerequisites.generatorVersion -and $warm.cpp2ilVersion -ceq $Contract.cachePrerequisites.cpp2ilVersion -and $warm.optionalRenameMapPresent -is [bool] -and $warm.optionalRenameMapPresent -eq $false -and $warm.orderedEvents -is [array] -and $warm.orderedEvents.Count -eq 147 -and $warm.inputFiles -is [array] -and $warm.inputFiles.Count -eq 73) 'Fresh warm-order versions/count/stamp differ'
 $expectedEvents=ConvertTo-InputsBytes $Historical.input_events
 $actualEvents=ConvertTo-InputsBytes $warm.orderedEvents
 Assert-Inputs ((Get-InputsHash $actualEvents) -ceq (Get-InputsHash $expectedEvents)) 'Fresh ordered interop input event sequence differs from historical source-linked proof'
 $files=@(foreach($entry in $Historical.input_files){[pscustomobject][ordered]@{relative_path=$entry.relative_path;bytes=[long]$entry.bytes;sha256=$entry.sha256}})
 Assert-Inputs ((Get-InputsHash (ConvertTo-InputsBytes $warm.inputFiles)) -ceq (Get-InputsHash (ConvertTo-InputsBytes $files))) 'Fresh ordered interop input file hashes/sizes differ from historical source-linked proof'
 Assert-Inputs ($PreflightSha -cmatch '^[a-f0-9]{64}$') 'Fresh preflight digest is malformed'
}
function Assert-InputsShared($Scope,$Before,$Identity,[string]$RunId){
 Assert-Inputs ($Identity.status -ceq 'passed' -and $Identity.identitySid -ceq $script:InputsOwner -and $Identity.isAdministrator -eq $false -and $Identity.hiveLoaded -eq $true -and $Identity.token.user_sid -ceq $script:InputsOwner -and $Identity.token.logon_sid -cmatch '^S-1-5-5-\d+-\d+$' -and $Identity.token.is_restricted -eq $false -and $Identity.token.elevated -eq $false -and $Identity.token.is_app_container -eq $false -and $Identity.token.users_enabled -eq $true -and @($Identity.unexpectedPrivileges).Count -eq 0) 'Actual original controller identity/Logon receipt is incomplete'
 Assert-Inputs ($Identity.controllerProfile -is [string] -and $Identity.currentLocalLow -is [string] -and $Identity.currentEnvironment -is [pscustomobject]) 'Original true profile/KnownFolder/environment evidence missing'
 Import-Module (Join-Path $PSScriptRoot 'RestrictedCliCore.psm1') -Force -DisableNameChecking
 Import-Module (Join-Path $PSScriptRoot 'CurrentAccountCli.psm1') -Force -DisableNameChecking
 $m=Get-Module CurrentAccountCli
 $paths=& $m {param($i,$sid,$profile,$low,$environment)Get-AzuriteCurrentIdentityPaths $i $sid $profile $low $environment} $Identity.profileIdentity $script:InputsOwner $Identity.controllerProfile $Identity.currentLocalLow $Identity.currentEnvironment
 Assert-Inputs ($Identity.profileProof.schema -eq 1 -and $Identity.profileProof.identityMode -ceq 'CurrentAccount' -and $Identity.profileProof.identitySid -ceq $script:InputsOwner -and $Identity.profileProof.filesystemVerified -eq $true -and $Identity.profileProof.hiveShared -eq $true -and $Identity.profileProof.accountIsolation -eq $false) 'Current-account profile/KnownFolder proof differs'
 foreach($entry in $paths.PSObject.Properties){Assert-Inputs ($Identity.profileProof.paths.($entry.Name) -ceq $entry.Value) ('Current-account profile path proof differs: '+$entry.Name)}
 Assert-Inputs ($paths.localLow -ceq $Identity.currentLocalLow -and $paths.userProfile -ceq $Identity.controllerProfile) 'True current profile/LocalLow differs from supplied controller fields'
 Assert-Inputs ($Scope.identitySid -ceq $script:InputsOwner -and $Scope.registryPath -ceq 'Software\foxxlight\AzureArchive' -and $Before.identitySid -ceq $script:InputsOwner -and $Before.phase -ceq 'Before') 'Shared scope and typed before-state identity differ'
 $specHash=& $m {param($s)Get-AzuriteStateSpecHash $s} $Scope
 Assert-Inputs ($Scope.schema -eq 1 -and $Before.schema -eq 1 -and $Before.runId -ceq $RunId -and $Before.specSha256 -ceq $specHash -and @($Before.roots).Count -eq @($Scope.roots).Count -and @($Before.files).Count -le 512 -and @($Before.directories).Count -le 512 -and $Before.capturedUtc -is [string]) 'Typed shared snapshot scope/hash/count differs'
 [void][DateTimeOffset]::Parse($Before.capturedUtc,[Globalization.CultureInfo]::InvariantCulture)
 Assert-Inputs ($Scope.dailyProfileName -is [string] -and $Scope.dailyProfileName -cmatch '^[^\\/:*?"<>|\x00-\x1f .][^\\/:*?"<>|\x00-\x1f]*$' -and $Scope.dailyProfileName -cnotin @('.','..') -and $Scope.dailyProfileName -notmatch '[ .]$') 'Daily selected profile name is not a simple canonical component'
 $daily='F:\AzureArchive_101'
 $expectedRoots=@(
  [pscustomobject]@{label='LocalLowAA';path=$Identity.currentLocalLow+'\foxxlight\AzureArchive';kind='Directory';required=$false},
  [pscustomobject]@{label='DailyActiveProfile';path=$daily+'\ActiveProfile.txt';kind='File';required=$true},
  [pscustomobject]@{label='DailySelectedProfile';path=$daily+'\profiles\'+$Scope.dailyProfileName;kind='Directory';required=$true},
  [pscustomobject]@{label='DailyDoorstopConfig';path=$daily+'\doorstop_config.ini';kind='File';required=$true},
  [pscustomobject]@{label='DailyBepInExConfig';path=$daily+'\BepInEx\config\BepInEx.cfg';kind='File';required=$true}
 )
 Assert-Inputs (@($Scope.roots).Count -eq $expectedRoots.Count) 'Shared scope must contain five exact roots'
 $rootSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 for($i=0;$i -lt @($Scope.roots).Count;$i++){
  $wanted=$Scope.roots[$i];$actual=$Before.roots[$i]
  $fixed=$expectedRoots[$i]
  Assert-Inputs ($wanted.label -ceq $fixed.label -and $wanted.path -ceq $fixed.path -and $wanted.kind -ceq $fixed.kind -and $wanted.required -is [bool] -and $wanted.required -eq $fixed.required) ('Shared exact label/path/kind/required differs: '+$fixed.label)
  Assert-Inputs ($rootSeen.Add($wanted.label) -and $actual.label -ceq $wanted.label -and $actual.path -ceq $wanted.path -and $actual.kind -ceq $wanted.kind -and $actual.exists -is [bool] -and ($actual.exists -or -not $wanted.required)) ('Typed shared root differs: '+$wanted.label)
  $null=Get-AzuriteCanonicalProfilePath $wanted.path
 }
 $dirSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($directory in $Before.directories){
  $parts=$directory -split '/',2
  $root=@($Before.roots|Where-Object {$_.label -ceq $parts[0]})
  Assert-Inputs ($directory -is [string] -and $parts.Count -eq 2 -and $root.Count -eq 1 -and $root[0].exists -and $root[0].kind -ceq 'Directory' -and $directory -notmatch '(^|/)\.\.?(/|$)' -and $directory -notmatch '[\\:\x00-\x1f]' -and $dirSeen.Add($directory)) 'Typed shared directory ID escapes its exact root'
 }
 $fileSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$total=0L
 foreach($file in $Before.files){
  $parts=$file.id -split '/',2
  $root=@($Before.roots|Where-Object {$_.label -ceq $parts[0]})
  Assert-Inputs ($parts.Count -eq 2 -and $root.Count -eq 1 -and $root[0].exists -and ($root[0].kind -cne 'File' -or $parts[1] -ceq '') -and $file.id -notmatch '(^|/)\.\.?(/|$)' -and $file.id -notmatch '[\\:\x00-\x1f]' -and $fileSeen.Add($file.id) -and $file.bytes -ge 0 -and $file.bytes -le 33554432 -and $file.data -is [string] -and $file.sha256 -cmatch '^[A-Fa-f0-9]{64}$') 'Typed shared file ID/size/hash escapes its exact root'
  $raw=[Convert]::FromBase64String($file.data);$total+=$raw.Length
  Assert-Inputs ($raw.Length -eq $file.bytes -and (Get-InputsHash $raw) -ceq $file.sha256.ToLowerInvariant() -and $total -le 67108864) 'Typed shared file bytes differ from saved SHA-256'
  [void][DateTimeOffset]::Parse($file.lastWriteUtc,[Globalization.CultureInfo]::InvariantCulture)
 }
 foreach($root in $Before.roots){if($root.exists){$id=$root.label+'/';Assert-Inputs ($(if($root.kind -ceq 'File'){$fileSeen.Contains($id)}else{$dirSeen.Contains($id)})) 'Existing shared root lacks its typed captured item'}}
 $registry=$Before.registry
 Assert-Inputs ($registry.exists -is [bool] -and @($registry.keys).Count -le 128 -and @($registry.values).Count -le 2048) 'Typed registry snapshot count/shape differs'
 if($registry.exists){Assert-Inputs (@($registry.keys) -ccontains '') 'Typed registry root key missing'}else{Assert-Inputs (@($registry.keys).Count -eq 0 -and @($registry.values).Count -eq 0) 'Absent typed registry has content'}
 $valueSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$registryBytes=0L
 foreach($value in $registry.values){Assert-Inputs ($value.kind -cin @('Binary','None','DWord','QWord','String','ExpandString','MultiString') -and $value.data -is [string] -and $value.key -cin @($registry.keys) -and $value.name -is [string] -and $valueSeen.Add($value.key+[char]0+$value.name)) 'Typed registry value metadata differs';$raw=[Convert]::FromBase64String($value.data);$registryBytes+=$raw.Length;Assert-Inputs ($registryBytes -le 8388608 -and (($value.kind -cne 'DWord') -or $raw.Length -eq 4) -and (($value.kind -cne 'QWord') -or $raw.Length -eq 8) -and (($value.kind -cnotin @('String','ExpandString')) -or $raw.Length%2 -eq 0)) 'Typed registry value bytes exceed type/bound'}
 Assert-Inputs ($total+$registryBytes -le 67108864) 'Typed shared file+registry bytes exceed bound'
 $selection=@($Before.files|Where-Object {$_.id -ceq 'DailyActiveProfile/'})
 Assert-Inputs ($selection.Count -eq 1 -and $Scope.dailyProfileName -is [string]) 'Daily selected profile evidence missing'
 $selected=[Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($selection[0].data)).TrimStart([char]0xfeff).Trim()
 Assert-Inputs ($selected -ceq $Scope.dailyProfileName -and $Scope.roots[2].path -ceq ($daily+'\profiles\'+$selected)) 'Captured ActiveProfile differs from selected profile directory'
}
function Assert-InputsFactsFromBaseline($Baseline,$Facts,$Contract,$Identity){
 $index=@{};foreach($entry in $Baseline.objects){$index[$entry.path]=$entry}
 $install=$Contract.installRoot
 $chain=$install+'\BepInEx\cache\chainloader_typeloader.dat'
 $mod=$install+'\'+$Contract.cachePrerequisites.modTheLeaf.Replace('/','\')
 $parent=$install+'\BepInEx\cache'
 Assert-Inputs ($index.ContainsKey($chain) -and -not $index.ContainsKey($mod) -and $index.ContainsKey($parent)) 'Exact existing chainloader/missing ModThe cache baseline differs'
 $sd=[Security.AccessControl.RawSecurityDescriptor]::new($index[$parent].sddl)
 $denies=@($sd.DiscretionaryAcl|Where-Object {$_.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessDenied -and $_.SecurityIdentifier.Value -ceq $script:InputsOwner -and $_.AccessMask -eq 6})
 Assert-Inputs ($denies.Count -eq 1 -and $Facts.cache.modTheLeafAbsent -eq $true -and $Facts.acl.cacheParentCreationDenied -eq $true) 'Exact cache parent creation seal/absence differs'
 $profile=$install+'\ActiveProfile.txt'
 $config=$install+'\profiles\AzuriteAcceptance\modconfig.json'
 Assert-Inputs ($index.ContainsKey($profile) -and $index.ContainsKey($config) -and $Facts.profile -ceq 'AzuriteAcceptance' -and $Facts.modVersion -ceq '1.0.38') 'Pinned selected profile baseline differs'
 Assert-Inputs ($Identity.token.logon_sid -ceq $Facts.acl.actualLogonSid) 'Actual Logon SID differs from pretransition Facts'
}
function New-AzuriteFullCapabilitiesInputs {
 [CmdletBinding()]param([Parameter(Mandatory)]$InputRecord,[Parameter(Mandatory)][string]$OutputDirectory)
 $failures=[Collections.Generic.List[string]]::new()
 try {
  Assert-InputsShape $InputRecord @('schema','scope','evidenceClass','runId','baseline','harmless','preflight','shared','originalIdentity','helperSource','sources','intentValidUntilUtc','runtimeTimeoutSeconds') 'Input record'
  Assert-Inputs ($InputRecord.schema -eq 1 -and $InputRecord.scope -ceq 'fixed-F-capabilities-preapproval-inputs' -and $InputRecord.evidenceClass -cin @('simulated-fixture','reviewed-real')) 'Input record scope/evidence class differs'
  Assert-Inputs ($InputRecord.runId -cmatch '^[a-f0-9]{32}$' -and $InputRecord.runId -cne ('0'*32)) 'RunId must be nonzero canonical lowercase 32-hex'
  Assert-Inputs ($InputRecord.intentValidUntilUtc -is [string] -and $InputRecord.intentValidUntilUtc -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$' -and ($InputRecord.runtimeTimeoutSeconds -is [int] -or $InputRecord.runtimeTimeoutSeconds -is [long]) -and $InputRecord.runtimeTimeoutSeconds -ge 10 -and $InputRecord.runtimeTimeoutSeconds -le 300) 'Exact intent expiry/runtime duration missing'
  $null=[DateTimeOffset]::Parse($InputRecord.intentValidUntilUtc,[Globalization.CultureInfo]::InvariantCulture)
  foreach($name in @('baseline','harmless','preflight','shared','originalIdentity','helperSource')){Assert-Inputs ($null -ne $InputRecord.$name) ('Missing separately pinned '+$name+' evidence')}
  Assert-InputsShape $InputRecord.baseline @('identity','capture') 'Baseline pins';Assert-InputsShape $InputRecord.shared @('scope','before') 'Shared pins'
  $baselineBytes=Read-InputsPinned $InputRecord.baseline.identity 4194304;$captureBytes=Read-InputsPinned $InputRecord.baseline.capture 131072
  Assert-Inputs ($InputRecord.evidenceClass -cne 'reviewed-real' -or ($InputRecord.runId -ceq '2fc0eb21404d4efaaf527c07c295d147' -and $baselineBytes.sha256 -ceq 'afed6f91b9060eabe5f4e2b83a78556c6c9b7a817cb1a5d23b7551070fcc11ed')) 'Reviewed-real input must bind the completed exact-run baseline'
  $baseline=ConvertFrom-InputsJson $baselineBytes.bytes;$capture=ConvertFrom-InputsJson $captureBytes.bytes
  Assert-Inputs ($capture.schema -eq 1 -and $capture.scope -ceq 'read-only-stage-baseline-capture' -and $capture.runId -ceq $InputRecord.runId -and $capture.success -eq $true -and $capture.observedObjects -eq 1228 -and $capture.baselineSha256 -ceq $baselineBytes.sha256 -and $capture.lease.state -ceq 'released' -and $capture.runtimeAllowed -eq $false -and $capture.launchPermission -eq $false -and [string]::IsNullOrEmpty($capture.error)) 'Real completed baseline capture receipt missing or failed'
  $harmlessBytes=Read-InputsPinned $InputRecord.harmless 1048576
  $preflightBytes=Read-InputsPinned $InputRecord.preflight 1048576
  $scopeBytes=Read-InputsPinned $InputRecord.shared.scope 1048576;$beforeBytes=Read-InputsPinned $InputRecord.shared.before 67108864;$identityBytes=Read-InputsPinned $InputRecord.originalIdentity 131072;$helperSourceBytes=Read-InputsPinned $InputRecord.helperSource 1048576
  $harmless=ConvertFrom-InputsJson $harmlessBytes.bytes;$preflight=ConvertFrom-InputsJson $preflightBytes.bytes
  $scope=ConvertFrom-InputsJson $scopeBytes.bytes;$before=ConvertFrom-InputsJson $beforeBytes.bytes;$identity=ConvertFrom-InputsJson $identityBytes.bytes
  Assert-Inputs (@($InputRecord.sources).Count -eq $script:InputsSources.Count) 'Exact accepted source pin count differs'
  $sources=@{}
  foreach($entry in $InputRecord.sources){Assert-InputsShape $entry @('name','path','sha256') 'Accepted source pin';Assert-Inputs ($entry.name -cin $script:InputsSources -and -not $sources.ContainsKey($entry.name)) 'Unknown/duplicate reviewed source name';$sources[$entry.name]=Read-InputsPinned ([pscustomobject]@{path=$entry.path;sha256=$entry.sha256}) 8388608}
  foreach($name in $script:InputsSources){Assert-Inputs ($sources.ContainsKey($name)) ('Missing reviewed source: '+$name)}
  foreach($name in @('RestrictedCliCore.psm1','CurrentAccountCli.psm1','FullRestrictedCapabilitiesPlan.psm1','FullRestrictedCapabilitiesRuntime.psm1')){
   $executing=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $name))
   Assert-Inputs ($sources[$name].path -ceq $executing) ('Executing module path differs from reviewed C pin before import: '+$name)
   $verified=Read-InputsPinned ([pscustomobject]@{path=$executing;sha256=$sources[$name].sha256}) 8388608
   Assert-Inputs ($verified.sha256 -ceq $sources[$name].sha256 -and $verified.length -eq $sources[$name].length) ('Executing module bytes differ before import: '+$name)
  }
  Assert-InputsHarmless $harmless $sources $InputRecord.evidenceClass
  Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesPlan.psm1') -Force -DisableNameChecking
  Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesRuntime.psm1') -Force -DisableNameChecking
  $contract=Read-AzuriteFullCapabilitiesContract
  $cachePin=[pscustomobject]@{path=(Join-Path $script:InputsRepo 'artifacts\aa-fr-cache-prerequisites\cache-prerequisites-contract.json');sha256=$contract.cachePrerequisites.contractSha256}
  $cache=ConvertFrom-InputsJson (Read-InputsPinned $cachePin 1048576).bytes
  $historicalWarmPin=[pscustomobject]@{path=(Join-Path $script:InputsRepo 'artifacts\aa-fr-cache-prerequisites\warm-stamp-result.json');sha256=$contract.cachePrerequisites.warmResultSha256}
  $historicalWarm=ConvertFrom-InputsJson (Read-InputsPinned $historicalWarmPin 1048576).bytes
  Assert-InputsPreflight $preflight $historicalWarm $contract $capture $InputRecord $preflightBytes.sha256
  $tooling=ConvertFrom-InputsJson $sources['tooling-manifest.json'].bytes;$toolNames=Get-AzuriteFullToolNames
  $actualToolNames=@($tooling.files|ForEach-Object name)
  Assert-Inputs ($tooling.schema -eq 1 -and $tooling.scope -ceq 'full-restricted-precompiled-tooling' -and @($tooling.files).Count -eq 8 -and ($actualToolNames -join ',') -ceq ($toolNames -join ',')) 'Reviewed tooling manifest names/order differ'
  foreach($entry in $tooling.files){Assert-Inputs ($sources[$entry.name].sha256 -ceq $entry.sha256) ('Reviewed tooling pin differs: '+$entry.name)}
  Assert-Inputs ($tooling.build.exitCode -eq 0 -and $tooling.build.assemblySha256 -ceq $sources['RestrictedCliNative.dll'].sha256 -and $tooling.build.sources[0].sha256 -ceq $sources['RestrictedCliNative.cs'].sha256 -and $tooling.build.sources[1].sha256 -ceq $sources['RestrictedCliFullRestricted.cs'].sha256 -and $tooling.build.canonicalBuildResultSha256 -cmatch '^[a-f0-9]{64}$' -and $tooling.build.sourceArchiveSha256 -cmatch '^[a-f0-9]{64}$') 'Reviewed native helper provenance differs'
  Assert-Inputs ($tooling.build.canonicalBuildResultPath -is [string] -and $tooling.build.canonicalBuildResultPath -cmatch '^\.superpowers/sdd/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/result\.json$') 'Canonical reviewed native build result relative path differs'
  $nativeResultPath=[IO.Path]::GetFullPath((Join-Path $script:InputsRepo $tooling.build.canonicalBuildResultPath))
  $nativeResultBytes=Read-InputsPinned ([pscustomobject]@{path=$nativeResultPath;sha256=$tooling.build.canonicalBuildResultSha256}) 131072
  $nativeResult=ConvertFrom-InputsJson $nativeResultBytes.bytes
  Assert-Inputs ($nativeResult.dllSha256 -ceq $tooling.build.assemblySha256 -and $nativeResult.sourceArchiveSha256 -ceq $tooling.build.sourceArchiveSha256 -and $nativeResult.aaExecuted -eq $false -and $nativeResult.nativeFixtureExecuted -eq $false -and $nativeResult.stageWrites -eq $false) 'Accepted native build result differs from pinned source/assembly'
  $helperBuild=ConvertFrom-InputsJson $sources['helper-build.json'].bytes
  $transitionModule=Read-InputsPinned ([pscustomobject]@{path=(Join-Path $PSScriptRoot 'FullRestrictedStageTransition.psm1');sha256=$helperBuild.moduleSha256}) 1048576
  Assert-Inputs ($helperBuild.schema -eq 1 -and $helperBuild.scope -ceq 'controller-file-security-helper-build' -and $helperBuild.exitCode -eq 0 -and $helperBuild.assemblySha256 -ceq $sources['FullRestrictedStageFileSecurity.dll'].sha256 -and $helperBuild.sourceSha256 -ceq $helperSourceBytes.sha256 -and $helperBuild.moduleSha256 -ceq $transitionModule.sha256) 'Reviewed file/security helper build binding differs'
  Assert-Inputs ($helperBuild.compilerSha256 -ceq $tooling.build.compilerSha256 -and $helperBuild.target -ceq 'library' -and $helperBuild.platform -ceq 'x64' -and @($tooling.build.references).Count -eq 1 -and $tooling.build.references[0].name -ceq 'System.Web.Extensions.dll' -and $tooling.build.references[0].sha256 -cmatch '^[a-f0-9]{64}$') 'Exact compiler/reference provenance differs'
  Assert-InputsShared $scope $before $identity $InputRecord.runId
  $sharedTime=[DateTimeOffset]::Parse($before.capturedUtc,[Globalization.CultureInfo]::InvariantCulture)
  $freshTime=[DateTimeOffset]::Parse($preflight.observedUtc,[Globalization.CultureInfo]::InvariantCulture)
  Assert-Inputs ($sharedTime -ge [DateTimeOffset]::Parse($capture.observedUtc,[Globalization.CultureInfo]::InvariantCulture) -and $sharedTime -le $freshTime) 'Typed shared before snapshot is outside baseline/fresh preflight chronology'
  $facts=[pscustomobject][ordered]@{schema=1;runId=$InputRecord.runId;labRoot=$contract.labRoot;executable=$contract.executable;operation='capabilities';profile=$contract.profile;modName=$contract.modName;modVersion=$contract.modVersion;volumeGuid=$contract.stage.destinationVolumeGuid;ownerSid=$contract.ownerSid;controllerSid=$contract.ownerSid;stage=$contract.stage;inputs=[pscustomobject]@{};cache=[pscustomobject]@{chainloaderSha256=$contract.cachePrerequisites.chainloaderSha256;modTheLeaf=$contract.cachePrerequisites.modTheLeaf;modTheLeafAbsent=$true;seedSha256=$contract.cachePrerequisites.seedSha256;stamp=$preflight.warmInterop.computedStamp;orderedInputEventCount=$preflight.warmInterop.orderedEvents.Count;unityDllCount=72;generatorVersion=$preflight.warmInterop.generatorVersion;cpp2ilVersion=$preflight.warmInterop.cpp2ilVersion;optionalRenameMapPresent=$preflight.warmInterop.optionalRenameMapPresent;orderedInputsMatchRecorded=((Get-InputsHash (ConvertTo-InputsBytes $preflight.warmInterop.orderedEvents)) -ceq (Get-InputsHash (ConvertTo-InputsBytes $historicalWarm.input_events)))};environment=[pscustomobject]@{gameAssemblyOverride=$preflight.actualEnvironment.BEPINEX_GAME_ASSEMBLY_PATH.value;unhollowedPathArgument=($preflight.actualArgv -ccontains '--unhollowed-path')};acl=[pscustomobject]@{stageSealPolicySha256=$contract.stage.sealPolicySha256;stageSealsIntact=$true;cacheParentCreationDenied=$true;sharedGrantDelta='None';actualLogonSid=$identity.token.logon_sid};preTransition=[pscustomobject]@{phase='pretransition';runId=$InputRecord.runId;cacheLeaf=$contract.cachePrerequisites.modTheLeaf;cacheLeafExists=$false;unityLogAbsent=$true;seedSha256=$contract.cachePrerequisites.seedSha256;parentSealIntact=$true;runtimeAllowed=$false;observedUtc=$preflight.observedUtc};transition=$null}
  $module=Get-Module FullRestrictedCapabilitiesPlan;$envelope=& $module {param($c,$f,$r)(New-BasePlan $c $f $r).ReadEnvelope} $contract $facts $InputRecord.runId
  $facts.inputs=[pscustomobject]@{stageFilePinDigest=$envelope.stageFilePinDigest;immutableFilePinDigest=$envelope.immutableFilePinDigest;directoryPinDigest=$envelope.directoryPinDigest;semanticPinCount=451;stageFileCount=1062;stageDirectoryCount=156;stageBytes=[long]3521516451;cacheContractSha256=$contract.cachePrerequisites.contractSha256;cacheReportSha256=$contract.cachePrerequisites.reportSha256;cacheIndexSha256=$contract.cachePrerequisites.indexSha256;warmResultSha256=$contract.cachePrerequisites.warmResultSha256}
  $plan=New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $InputRecord.runId -Facts $facts
  Assert-Inputs ($plan.PreparationReady -and -not $plan.Ready -and -not $plan.RuntimeAllowed -and -not $plan.WillLaunch -and $null -eq $facts.transition) ('Dry planner rejected preapproval facts: '+($plan.Failures -join '; '))
  Assert-InputsBaseline $baseline $capture $InputRecord.runId $plan $baselineBytes.sha256
  Assert-InputsFactsFromBaseline $baseline $facts $contract $identity
  return [pscustomobject]@{schema=1;scope='fixed-F-capabilities-preapproval-input-assembly';evidenceClass=$InputRecord.evidenceClass;runId=$InputRecord.runId;inputReady=$true;productionEvidence=$false;approvalPresent=$false;willApply=$false;willLaunch=$false;runtimeAllowed=$false;plan=$plan;facts=$facts;sources=$sources;tooling=$tooling;baseline=$baseline;baselinePin=$baselineBytes;scopePin=$scopeBytes;beforePin=$beforeBytes;harmlessPin=$harmlessBytes;preflightPin=$preflightBytes;identity=$identity;helperBuild=$helperBuild}
 }catch{$failures.Add($_.Exception.Message);return [pscustomobject]@{schema=1;scope='fixed-F-capabilities-preapproval-input-assembly';evidenceClass=$(if($InputRecord -and $InputRecord.PSObject.Properties['evidenceClass']){$InputRecord.evidenceClass}else{$null});runId=$(if($InputRecord -and $InputRecord.PSObject.Properties['runId']){$InputRecord.runId}else{$null});inputReady=$false;productionEvidence=$false;approvalPresent=$false;willApply=$false;willLaunch=$false;runtimeAllowed=$false;failures=$failures.ToArray();files=$null}}
}
function New-AzuriteFullCapabilitiesIntent {
 [CmdletBinding()]param([Parameter(Mandatory)]$Validated,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][int]$RuntimeTimeoutSeconds,[Parameter(Mandatory)][string]$IntentValidUntilUtc)
 Assert-Inputs ($Validated.inputReady -eq $true -and $Validated.approvalPresent -eq $false) 'Only complete preapproval dry inputs can be serialized'
 $out=Assert-InputsOutputPath $OutputDirectory $true
 $plan=$Validated.plan;$facts=$Validated.facts;$sources=$Validated.sources;$identity=$Validated.identity;$tooling=$Validated.tooling
 Assert-InputsAcyclic $facts @('approvalSha256','transitionReceiptSha256','gateSha256') 'Preapproval facts'
 $run=$plan.LabRoot+'\runs\restricted-'+$plan.RunId
 $toolHashes=[ordered]@{};foreach($entry in $tooling.files){$toolHashes[$entry.name]=$entry.sha256}
 $args=[pscustomobject][ordered]@{argv=@('capabilities');cwd=$run+'\workspace'}
 $control=[pscustomobject][ordered]@{
  schema=2;identityMode='CurrentAccountFullRestrictedCapabilities';runId=$plan.RunId;command='capabilities';labRoot=$plan.LabRoot;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256
  controllerSid=$script:InputsOwner;targetSid=$script:InputsOwner;controllerProfile=$identity.controllerProfile;currentLocalLow=$identity.currentLocalLow;currentEnvironment=$identity.currentEnvironment
  argumentsSha256=(Get-InputsHash (ConvertTo-InputsBytes $args));sharedStateBeforeSha256=$Validated.beforePin.sha256;toolingManifestSha256=$sources['tooling-manifest.json'].sha256;expectedAclManifestSha256=$null
  toolHashes=[pscustomobject]$toolHashes;privateHistoryPaths=@([pscustomobject]@{path='transition';kind='Directory'},[pscustomobject]@{path='transition/before-images-raw.json';kind='File'})
  runtimeTimeoutSeconds=$RuntimeTimeoutSeconds;intentValidUntilUtc=$IntentValidUntilUtc
 }
 Assert-AzuriteFullToolManifest $tooling $control
 Assert-InputsAcyclic $tooling @('controlSha256','gateSha256','approvalSha256','reviewApprovalSha256','transitionReceiptSha256') 'Tooling manifest'
 $roles=Get-AzuriteFullExpectedRoles $plan $control
 $baselineMap=@{};foreach($entry in $Validated.baseline.objects){Assert-Inputs (-not $baselineMap.ContainsKey($entry.path)) 'Duplicate verified baseline path';$baselineMap[$entry.path]=$entry}
 $aclObjects=@(foreach($path in (Sort-InputsOrdinal ([string[]]@($roles.Keys)))){
  $role=$roles[$path][0];$mask=$roles[$path][1];$directory=$role.EndsWith('directory')
  if($baselineMap.ContainsKey($path)){$baselineGroup=[Security.AccessControl.RawSecurityDescriptor]::new($baselineMap[$path].sddl).Group.Value}
  else{$baselineGroup=$script:InputsOwner}
  $sddl='O:'+$script:InputsOwner+'G:'+$baselineGroup+'D:P'
  if($role -ceq 'install-directory'){$sddl+='(D;;0x6;;;'+$script:InputsOwner+')'}
  foreach($who in @($script:InputsOwner,'SY','BA')){$sddl+='(A;'+$(if($directory){'OICI'}else{''})+';FA;;;'+$who+')'}
  if(-not $role.StartsWith('private')){$sddl+='(A;;'+$mask+';;;'+$identity.token.logon_sid+')'}
  if($role -ceq 'request-directory'){$sddl+='(A;OIIO;0x13019f;;;'+$identity.token.logon_sid+')'}
  $canonical=[Security.AccessControl.RawSecurityDescriptor]::new($sddl).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access')
  if($baselineMap.ContainsKey($path)){Assert-Inputs (([Security.AccessControl.RawSecurityDescriptor]::new($canonical).Group.Value) -ceq $baselineGroup) ('Existing ACL intent group differs from pinned baseline: '+$path)}
  [pscustomobject]@{path=$path;role=$role;mask=$mask;sddl=$canonical}
 })
 $acl=[pscustomobject][ordered]@{schema=1;scope='preapproval-exact-acl-intent';runId=$plan.RunId;planSha256=$plan.PlanSha256;ownerSid=$script:InputsOwner;logonSid=$identity.token.logon_sid;objects=$aclObjects}
 Assert-AzuriteFullExpectedAclManifest $acl $plan $control $identity.token.logon_sid
 $aclBytes=ConvertTo-InputsBytes $acl;Assert-Inputs ($aclBytes.Length -le 1048576) 'Exact ACL intent exceeds writer bound'
 $control.expectedAclManifestSha256=Get-InputsHash $aclBytes
 $generated=[ordered]@{'facts.json'=ConvertTo-InputsBytes $facts;'plan.json'=ConvertTo-InputsBytes $plan;'expected-acl-manifest.json'=$aclBytes;'identity-baseline.json'=$Validated.baselinePin.bytes;'scope.json'=$Validated.scopePin.bytes;'before.json'=$Validated.beforePin.bytes}
 $rows=@(foreach($name in $script:InputsManifest){
  if($generated.Contains($name)){$bytes=$generated[$name];[pscustomobject]@{name=$name;path=[IO.Path]::GetFullPath((Join-Path $out $name));sha256=Get-InputsHash $bytes;bytes=$bytes.Length}}
  else{$pin=$sources[$name];[pscustomobject]@{name=$name;path=$pin.path;sha256=$pin.sha256;bytes=$pin.length}}
 })
 $actualNames=@($rows|ForEach-Object name|Sort-Object -CaseSensitive)
 $expectedNames=@($script:InputsManifest|Sort-Object -CaseSensitive)
 Assert-Inputs (($actualNames -join ',') -ceq ($expectedNames -join ',')) 'Exact 18-source names differ'
 $sourceManifest=[pscustomobject][ordered]@{schema=1;scope='controller-full-capabilities-preapproval-sources';runId=$plan.RunId;files=$rows}
 $sourceBytes=ConvertTo-InputsBytes $sourceManifest;$controlBytes=ConvertTo-InputsBytes $control
 Assert-Inputs ($rows.Count -eq 18 -and $sourceBytes.Length -le 1048576 -and $controlBytes.Length -le 131072) 'Forward 18-source/control bound differs'
 $gate=[pscustomobject][ordered]@{schema=2;scope='preapproval-full-restricted-capabilities-intent';runId=$plan.RunId;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;controlSha256=(Get-InputsHash $controlBytes);toolingManifestSha256=$sources['tooling-manifest.json'].sha256;expectedAclManifestSha256=$control.expectedAclManifestSha256;harmlessLifecycleReceiptPath=$Validated.harmlessPin.path;harmlessLifecycleReceiptSha256=$Validated.harmlessPin.sha256;freshPreflightPath=$Validated.preflightPin.path;freshPreflightSha256=$Validated.preflightPin.sha256;transitionSourceManifestSha256=(Get-InputsHash $sourceBytes);transitionHelperSha256=$sources['FullRestrictedStageFileSecurity.dll'].sha256;stageRuntimeAllowed=$false;operation='capabilities'}
 Assert-InputsAcyclic $sourceManifest @('controlSha256','gateSha256','approvalSha256','reviewApprovalSha256','transitionReceiptSha256') 'Source manifest'
 Assert-InputsAcyclic $control @('gateSha256','approvalSha256','reviewApprovalSha256','transitionReceiptSha256','admissionSha256') 'Control'
 Assert-InputsAcyclic $gate @('gateSha256','approvalSha256','reviewApprovalSha256','transitionReceiptSha256','admissionSha256','aa0Observed') 'Gate'
 $gateBytes=ConvertTo-InputsBytes $gate;Assert-Inputs ($gateBytes.Length -le 131072) 'Forward gate exceeds bound'
 $generated['source-manifest.json']=$sourceBytes;$generated['control.json']=$controlBytes;$generated['gate.json']=$gateBytes
 return [pscustomobject]@{schema=1;scope='fixed-F-capabilities-preapproval-input-assembly';evidenceClass=$Validated.evidenceClass;runId=$plan.RunId;inputReady=$true;productionEvidence=$false;reviewRequired=$true;approvalPresent=$false;willApply=$false;willLaunch=$false;runtimeAllowed=$false;planSha256=$plan.PlanSha256;sourceManifestSha256=Get-InputsHash $sourceBytes;controlSha256=Get-InputsHash $controlBytes;gateSha256=Get-InputsHash $gateBytes;toolingManifestSha256=$sources['tooling-manifest.json'].sha256;harmlessReceiptSha256=$Validated.harmlessPin.sha256;baselineSha256=$Validated.baselinePin.sha256;sharedBeforeSha256=$Validated.beforePin.sha256;files=$generated}
}
function Write-AzuriteFullCapabilitiesInputs($Result,[string]$OutputDirectory){
 Assert-Inputs ($Result.inputReady -eq $true -and $Result.files -is [System.Collections.Specialized.OrderedDictionary]) 'No complete dry inputs to write'
 $path=Assert-InputsOutputPath $OutputDirectory $true
 [void][IO.Directory]::CreateDirectory($path)
 $null=Assert-InputsOutputPath $path $false
 $saved=@{}
 foreach($entry in $Result.files.GetEnumerator()){
  $null=Assert-InputsOutputPath $path $false
  $dest=Join-Path $path $entry.Key;$bytes=[byte[]]$entry.Value
  $stream=[IO.FileStream]::new($dest,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
  Assert-Inputs ((Get-InputsHash ([IO.File]::ReadAllBytes($dest))) -ceq (Get-InputsHash $bytes)) 'C dry output readback differs'
  $saved[$entry.Key]=Get-InputsHash $bytes
 }
 $manifest=ConvertFrom-InputsJson ([IO.File]::ReadAllBytes((Join-Path $path 'source-manifest.json')))
 foreach($entry in $manifest.files){if($saved.ContainsKey($entry.name)){Assert-Inputs ($saved[$entry.name] -ceq $entry.sha256 -and (Get-Item -LiteralPath (Join-Path $path $entry.name)).Length -eq $entry.bytes) ('Source-manifest generated file readback differs: '+$entry.name)}}
 Assert-Inputs ($saved['source-manifest.json'] -ceq $Result.sourceManifestSha256 -and $saved['control.json'] -ceq $Result.controlSha256 -and $saved['gate.json'] -ceq $Result.gateSha256) 'Forward source/control/gate readback differs'
 return [pscustomobject]@{schema=1;scope=$Result.scope;evidenceClass=$Result.evidenceClass;runId=$Result.runId;inputReady=$true;approvalPresent=$false;willApply=$false;willLaunch=$false;runtimeAllowed=$false;outputDirectory=$path;planSha256=$Result.planSha256;sourceManifestSha256=$Result.sourceManifestSha256;controlSha256=$Result.controlSha256;gateSha256=$Result.gateSha256}
}
if($MyInvocation.InvocationName -ne '.'){
 if([string]::IsNullOrWhiteSpace($InputRecordPath)){[pscustomobject]@{schema=1;scope='fixed-F-capabilities-preapproval-input-assembly';inputReady=$false;approvalPresent=$false;willApply=$false;willLaunch=$false;runtimeAllowed=$false;failures=@('Completed baseline, reviewed harmless lifecycle receipt, typed shared snapshot, original identity and external source pins are required.')}|ConvertTo-Json -Compress;return}
 $path=Assert-InputsCPath $InputRecordPath;$item=Get-Item -LiteralPath $path -Force
 Assert-Inputs (-not $item.PSIsContainer -and $item.Length -ge 2 -and $item.Length -le 131072) 'Input record is empty or exceeds 128 KiB'
 $recordBytes=[IO.File]::ReadAllBytes($path)
 Assert-Inputs ($recordBytes.Length -eq $item.Length -and $recordBytes.Length -le 131072) 'Input record grew or changed during bounded read'
 $record=ConvertFrom-InputsJson $recordBytes
 $validated=New-AzuriteFullCapabilitiesInputs $record $OutputDirectory
 if(-not $validated.inputReady){$validated|ConvertTo-Json -Depth 8 -Compress;return}
 $intent=New-AzuriteFullCapabilitiesIntent $validated $OutputDirectory $record.runtimeTimeoutSeconds $record.intentValidUntilUtc
 Write-AzuriteFullCapabilitiesInputs $intent $OutputDirectory|ConvertTo-Json -Depth 8 -Compress
}
