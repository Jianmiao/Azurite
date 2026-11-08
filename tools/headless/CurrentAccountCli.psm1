Set-StrictMode -Version Latest

function Get-AzuriteStateBytesHash([byte[]]$Bytes) {
 $sha=[Security.Cryptography.SHA256]::Create()
 try{return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-','')}finally{$sha.Dispose()}
}
function Get-AzuriteCurrentIdentityPaths($Identity,[string]$ExpectedSid,[string]$ExpectedProfile,[string]$ExpectedLocalLow,$ExpectedEnvironment) {
 if($Identity.identitySid -cne $ExpectedSid -or $Identity.isAdministrator -ne $false -or $Identity.hiveLoaded -ne $true){throw 'Current-account SID, unelevated token or loaded HKCU proof failed.'}
 $paths=[ordered]@{}
 foreach($name in @('userProfile','environmentProfile','localAppData','localLow','environmentLocalAppData','environmentAppData','environmentTemp','environmentTmp')){$paths[$name]=Get-AzuriteCanonicalProfilePath $Identity.$name}
 $profile=Get-AzuriteCanonicalProfilePath $ExpectedProfile;$low=Get-AzuriteCanonicalProfilePath $ExpectedLocalLow
 if(-not $paths.userProfile.Equals($profile,[StringComparison]::OrdinalIgnoreCase) -or -not $paths.environmentProfile.Equals($profile,[StringComparison]::OrdinalIgnoreCase) -or -not $paths.localLow.Equals($low,[StringComparison]::OrdinalIgnoreCase)){throw 'Current-account true profile/LocalLow differs from the controller.'}
 if(-not $ExpectedEnvironment){throw 'Current controller environment baseline is missing.'}
 foreach($name in @('localAppData','environmentProfile','environmentLocalAppData','environmentAppData','environmentTemp','environmentTmp')){if(-not $paths[$name].Equals((Get-AzuriteCanonicalProfilePath $ExpectedEnvironment.$name),[StringComparison]::OrdinalIgnoreCase)){throw 'Current child inherited environment differs from the actual controller baseline.'}}
 if(-not $paths.localAppData.Equals($paths.environmentLocalAppData,[StringComparison]::OrdinalIgnoreCase)){throw 'Current LocalAppData environment differs from KnownFolder.'}
 return [pscustomobject]$paths
}
function Assert-AzuriteCurrentIdentity($Identity,[string]$ExpectedSid,[string]$ExpectedProfile,[string]$ExpectedLocalLow,$ExpectedEnvironment) {
 $paths=Get-AzuriteCurrentIdentityPaths $Identity $ExpectedSid $ExpectedProfile $ExpectedLocalLow $ExpectedEnvironment
 foreach($path in @($paths.PSObject.Properties.Value|Select-Object -Unique)){
  $cursor=[IO.Path]::GetPathRoot($path)
  foreach($part in @('')+@($path.Substring($cursor.Length).Split('\'))){if($part){$cursor=Join-Path $cursor $part};$item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop;if(-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Current profile path contains a missing/reparse/non-directory ancestor.'}}
 }
 # A filtered token is allowed even when its human owner belongs to Administrators.
 return [pscustomobject]@{schema=1;identityMode='CurrentAccount';identitySid=$ExpectedSid;filesystemVerified=$true;method='current-account-shared-profile-v1';checkedUtc=[datetime]::UtcNow.ToString('o');paths=$paths;profileShared=$true;hiveShared=$true;accountIsolation=$false}
}
function Assert-AzuriteCurrentIdentityRecord($Identity,$Proof,[string]$ExpectedSid,[string]$ExpectedProfile,[string]$ExpectedLocalLow,$ExpectedEnvironment) {
 $paths=Get-AzuriteCurrentIdentityPaths $Identity $ExpectedSid $ExpectedProfile $ExpectedLocalLow $ExpectedEnvironment
 if($Proof.schema -ne 1 -or $Proof.identityMode -cne 'CurrentAccount' -or $Proof.identitySid -cne $ExpectedSid -or $Proof.filesystemVerified -ne $true -or $Proof.method -cne 'current-account-shared-profile-v1' -or $Proof.hiveShared -ne $true -or $Proof.accountIsolation -ne $false){throw 'Current-account shared-profile proof is invalid.'}
 foreach($field in $paths.PSObject.Properties){if($Proof.paths.($field.Name) -cne $field.Value){throw 'Current-account identity proof path mismatch.'}}
}
function Assert-AzuriteCurrentPrivatePath([string]$Path,[string]$ControllerSid) {
 Assert-AzuritePrivateAcl -Path $Path -ControllerSid $ControllerSid
}
function Set-AzuriteRunPathAccess([string]$LabRoot,[string]$Path,[string]$ControllerSid,[string]$TargetSid,[string]$IdentityMode,[ValidateSet('Read','Modify','Request')][string]$Role='Read') {
 if($IdentityMode -ceq 'CurrentAccount'){
  Set-AzuriteLabAcl -LabRoot $LabRoot -Path $Path -ControllerSid $ControllerSid
  Assert-AzuriteCurrentPrivatePath $Path $ControllerSid
  if($Role -eq 'Read' -and (Test-Path -LiteralPath $Path -PathType Leaf)){(Get-Item -LiteralPath $Path).IsReadOnly=$true}
 }else{
  if($Role -eq 'Request'){Set-AzuriteRequestAcl $LabRoot $Path $ControllerSid $TargetSid}
  else{$rights=if($Role -eq 'Modify'){'Modify'}else{'ReadAndExecute'};Set-AzuriteLabAcl -LabRoot $LabRoot -Path $Path -ControllerSid $ControllerSid -TestSid $TargetSid -TestRights $rights}
  Assert-AzuriteRestrictedAcl $Path $ControllerSid $TargetSid $Role
 }
}
function Assert-AzuriteRunPathAccess([string]$Path,[string]$ControllerSid,[string]$TargetSid,[string]$IdentityMode,[ValidateSet('Read','Modify','Request')][string]$Role='Read') {
 if($IdentityMode -ceq 'CurrentAccount'){Assert-AzuriteCurrentPrivatePath $Path $ControllerSid}else{Assert-AzuriteRestrictedAcl $Path $ControllerSid $TargetSid $Role}
}
function Assert-AzuritePreparedHash([string]$Path,[string]$ExpectedHash) {
 if($ExpectedHash -cnotmatch '^[A-F0-9]{64}$' -or (Get-AzuriteSha256 $Path) -cne $ExpectedHash){throw 'Immutable prepared input changed; same-account access is not an ACL security boundary.'}
}
function Get-AzuriteRegisteredLocalLow([string]$Profile) {
 $name='{A520A1A4-1780-4FF6-BD18-167343C5AF16}'
 $user=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders')
 try{if($user -and $name -in $user.GetValueNames()){$value=$user.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($value -isnot [string]){throw 'Registered LocalLow override has unsupported type.'};return [pscustomobject]@{path=(Get-AzuriteCanonicalProfilePath ([Environment]::ExpandEnvironmentVariables($value)));source='registered per-user KnownFolder override';nativeVerificationRequired=$true}}}finally{if($user){$user.Dispose()}}
 $key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FolderDescriptions\'+$name)
 try{if(-not $key -or $key.GetValue('Name') -cne 'LocalAppDataLow' -or $key.GetValue('RelativePath') -cne 'AppData\LocalLow' -or $key.GetValue('ParentFolder')){throw 'LocalLow registered definition is not the reviewed profile-relative definition.'};return [pscustomobject]@{path=(Join-Path (Get-AzuriteCanonicalProfilePath $Profile) 'AppData\LocalLow');source='registered profile-relative KnownFolder definition';nativeVerificationRequired=$true}}finally{if($key){$key.Dispose()}}
}
function New-AzuriteSharedStateSpec([string]$LocalLow,[string]$DailyInstall,[string]$IdentitySid) {
 $low=Assert-AzuriteFullPath $LocalLow;$daily=Assert-AzuriteFullPath $DailyInstall
 $active=Join-Path $daily 'ActiveProfile.txt';$item=Get-Item -LiteralPath $active -ErrorAction Stop
 if($item.Length -gt 4096){throw 'Daily profile selection exceeds bounded scope.'}
 $name=[IO.File]::ReadAllText($active).Trim()
 if([string]::IsNullOrWhiteSpace($name) -or $name -in @('.','..') -or $name -match '[\\/:*?"<>|\x00-\x1f]'){throw 'Daily selected profile is not a simple actual profile name.'}
 return [pscustomobject]@{schema=1;identitySid=$IdentitySid;registryPath='Software\foxxlight\AzureArchive';localLow=$low;dailyProfileName=$name;roots=@(
  [pscustomobject]@{label='LocalLowAA';path=(Join-Path $low 'foxxlight\AzureArchive');kind='Directory';required=$false},
  [pscustomobject]@{label='DailyActiveProfile';path=$active;kind='File';required=$true},
  [pscustomobject]@{label='DailySelectedProfile';path=(Join-Path $daily ('profiles\'+$name));kind='Directory';required=$true},
  [pscustomobject]@{label='DailyDoorstopConfig';path=(Join-Path $daily 'doorstop_config.ini');kind='File';required=$true},
  [pscustomobject]@{label='DailyBepInExConfig';path=(Join-Path $daily 'BepInEx\config\BepInEx.cfg');kind='File';required=$true}
 )}
}
function Get-AzuriteStateSpecHash($Spec){return Get-AzuriteStateBytesHash ([Text.Encoding]::UTF8.GetBytes(($Spec|ConvertTo-Json -Depth 8 -Compress)))}
function Read-AzuritePlayerPrefsSnapshot {
 $base=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\foxxlight\AzureArchive',$false)
 if(-not $base){return [pscustomobject]@{exists=$false;keys=@();values=@()}}
 $keys=[Collections.Generic.List[string]]::new();$values=[Collections.Generic.List[object]]::new();$queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue('');$bytes=0L
 try{
  while($queue.Count){
   $relative=$queue.Dequeue();$key=if($relative){$base.OpenSubKey($relative,$false)}else{$base}
   if(-not $key){throw 'PlayerPrefs key disappeared during capture.'}
   try{
    $keys.Add($relative);if($keys.Count -gt 128){throw 'PlayerPrefs subtree exceeds key bound.'}
    foreach($name in @($key.GetValueNames()|Sort-Object)){
     $kind=$key.GetValueKind($name);$value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
     $raw=switch($kind.ToString()){
      'Binary' {[byte[]]$value;break};'None' {[byte[]]$value;break}
      'DWord' {[BitConverter]::GetBytes([int]$value);break};'QWord' {[BitConverter]::GetBytes([long]$value);break}
      'String' {[Text.Encoding]::Unicode.GetBytes([string]$value);break};'ExpandString' {[Text.Encoding]::Unicode.GetBytes([string]$value);break}
      'MultiString' {[Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($value) -Compress));break}
      default {throw 'Unsupported PlayerPrefs registry value type.'}
     }
     $raw=[byte[]]@($raw);$bytes+=$raw.Length
     if($raw.Length -gt 8388608 -or $bytes -gt 8388608 -or $values.Count -ge 2048){throw 'PlayerPrefs data exceeds bounded backup scope.'}
     $values.Add([pscustomobject]@{key=$relative;name=$name;kind=$kind.ToString();data=[Convert]::ToBase64String($raw)})
    }
    foreach($child in @($key.GetSubKeyNames()|Sort-Object)){if(($relative -split '\\').Count -gt 12){throw 'PlayerPrefs nesting exceeds scope.'};$queue.Enqueue($(if($relative){$relative+'\'+$child}else{$child}))}
   }finally{if($relative){$key.Dispose()}}
  }
 }finally{$base.Dispose()}
 return [pscustomobject]@{exists=$true;keys=$keys.ToArray();values=$values.ToArray()}
}
function New-AzuriteSharedSnapshot($Spec,$Registry,[string]$RunId,[ValidateSet('Plan','Before','After')][string]$Phase) {
 if($Spec.schema -ne 1 -or $Spec.registryPath -cne 'Software\foxxlight\AzureArchive' -or @($Spec.roots).Count -lt 1 -or @($Spec.roots).Count -gt 8){throw 'Shared state scope is missing or unsupported.'}
 $files=[Collections.Generic.List[object]]::new();$directories=[Collections.Generic.List[string]]::new();$roots=[Collections.Generic.List[object]]::new();$total=0L
 foreach($root in $Spec.roots){
  if($root.label -cnotmatch '^[A-Za-z][A-Za-z0-9]{0,39}$' -or $root.kind -cnotin @('File','Directory')){throw 'Invalid shared-state root description.'}
  $path=Assert-AzuriteFullPath $root.path;$exists=Test-Path -LiteralPath $path
  if(-not $exists -and $root.required -and $Phase -ne 'After'){throw 'A required shared-state root is absent.'}
  $roots.Add([pscustomobject]@{label=$root.label;path=$path;kind=$root.kind;exists=[bool]$exists})
  if(-not $exists){continue}
  $queue=[Collections.Generic.Queue[object]]::new();$queue.Enqueue([pscustomobject]@{path=$path;relative='';depth=0})
  while($queue.Count){
   $next=$queue.Dequeue();$item=Get-Item -LiteralPath $next.path -Force -ErrorAction Stop
   if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Shared-state reparse input is forbidden.'}
   if($item.PSIsContainer){
    if($root.kind -eq 'File' -or $next.depth -gt 12 -or $directories.Count -ge 512){throw 'Shared-state directory scope exceeds bounds.'}
    $directories.Add($root.label+'/'+$next.relative)
    foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($item.FullName)){
     if($queue.Count+$files.Count+$directories.Count -gt 1536){throw 'Shared-state entry count exceeds bounds.'}
     $relative=if($next.relative){$next.relative+'/'+[IO.Path]::GetFileName($entry)}else{[IO.Path]::GetFileName($entry)}
     $queue.Enqueue([pscustomobject]@{path=$entry;relative=$relative;depth=$next.depth+1})
    }
   }else{
    if(($root.kind -eq 'Directory' -and $next.depth -eq 0) -or $files.Count -ge 512 -or $item.Length -gt 33554432 -or $total+$item.Length -gt 67108864){throw 'Shared-state files exceed bounded backup scope.'}
    $length=$item.Length;$writeTime=$item.LastWriteTimeUtc;$raw=[IO.File]::ReadAllBytes($item.FullName);$after=Get-Item -LiteralPath $item.FullName -Force
    if($raw.Length -ne $length -or $after.Length -ne $length -or $after.LastWriteTimeUtc -ne $writeTime){throw 'Shared-state file changed during capture.'}
    $total+=$raw.Length;$files.Add([pscustomobject]@{id=$root.label+'/'+$next.relative;bytes=$raw.Length;sha256=(Get-AzuriteStateBytesHash $raw);data=[Convert]::ToBase64String($raw);attributes=[int]$item.Attributes;lastWriteUtc=$writeTime.ToString('o')})
   }
  }
 }
 $registryCopy=$Registry|ConvertTo-Json -Depth 12 -Compress|ConvertFrom-Json
 $snapshot=[pscustomobject]@{schema=1;runId=$RunId;phase=$Phase;identitySid=$Spec.identitySid;specSha256=(Get-AzuriteStateSpecHash $Spec);capturedUtc=[datetime]::UtcNow.ToString('o');roots=$roots.ToArray();directories=@($directories.ToArray()|Sort-Object);files=@($files.ToArray()|Sort-Object id);registry=$registryCopy}
 Assert-AzuriteSharedSnapshot $snapshot $Spec $RunId $Phase
 if($Phase -ne 'After' -and $Spec.PSObject.Properties['dailyProfileName']){
  $selection=@($snapshot.files|Where-Object id -CEQ 'DailyActiveProfile/')[0]
  $text=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($selection.data)).TrimStart([char]0xfeff).Trim()
  if($text -cne $Spec.dailyProfileName){throw 'Daily selected profile changed during scope capture.'}
 }
 return $snapshot
}
function Assert-AzuriteSharedSnapshot($Snapshot,$Spec,[string]$RunId,[string]$Phase) {
 if(-not $Snapshot -or $Snapshot.schema -ne 1 -or $Snapshot.runId -cne $RunId -or $Snapshot.phase -cne $Phase -or $Snapshot.identitySid -cne $Spec.identitySid -or $Snapshot.specSha256 -cne (Get-AzuriteStateSpecHash $Spec) -or @($Snapshot.roots).Count -ne @($Spec.roots).Count -or @($Snapshot.files).Count -gt 512 -or @($Snapshot.directories).Count -gt 512){throw 'Absent, stale or malformed shared-state snapshot.'}
 [void](ConvertTo-AzuriteUtcInstant $Snapshot.capturedUtc)
 $rootMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 for($i=0;$i -lt @($Spec.roots).Count;$i++){
  $expected=$Spec.roots[$i];$actual=$Snapshot.roots[$i]
  if($actual.label -cne $expected.label -or $actual.path -cne (Assert-AzuriteFullPath $expected.path) -or $actual.kind -cne $expected.kind -or $actual.exists -isnot [bool] -or $rootMap.ContainsKey($actual.label) -or ($expected.required -and -not $actual.exists -and $Phase -cne 'After')){throw 'Shared-state snapshot root differs from its explicit scope.'}
  $rootMap.Add($actual.label,$actual)
 }
 $dirSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($directory in $Snapshot.directories){$parts=$directory -split '/',2;if($parts.Count -ne 2 -or -not $rootMap.ContainsKey($parts[0]) -or $rootMap[$parts[0]].kind -cne 'Directory' -or -not $rootMap[$parts[0]].exists -or $directory -match '(^|/)\.\.(/|$)' -or -not $dirSet.Add($directory)){throw 'Shared-state directory evidence is invalid.'}}
 $total=0L;$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($file in $Snapshot.files){$parts=$file.id -split '/',2;if($parts.Count -ne 2 -or -not $rootMap.ContainsKey($parts[0]) -or -not $rootMap[$parts[0]].exists -or $file.id -match '(^|/)\.\.(/|$)' -or ($rootMap[$parts[0]].kind -ceq 'File' -and $parts[1] -cne '')){throw 'Shared-state file is outside its captured root.'};[void](ConvertTo-AzuriteUtcInstant $file.lastWriteUtc);$raw=[Convert]::FromBase64String($file.data);if(-not $ids.Add($file.id) -or $raw.Length -ne $file.bytes -or $raw.Length -gt 33554432 -or (Get-AzuriteStateBytesHash $raw) -cne $file.sha256){throw 'Shared-state file backup/hash is invalid.'};$total+=$raw.Length}
 foreach($r in $Snapshot.roots){if($r.exists -and (($r.kind -ceq 'File' -and -not $ids.Contains($r.label+'/')) -or ($r.kind -ceq 'Directory' -and -not $dirSet.Contains($r.label+'/')))){throw 'Snapshot omits an existing explicit root.'}}
 $r=$Snapshot.registry
 if(-not $r -or $r.exists -isnot [bool] -or @($r.keys).Count -gt 128 -or @($r.values).Count -gt 2048 -or (-not $r.exists -and (@($r.keys).Count -or @($r.values).Count))){throw 'Typed PlayerPrefs snapshot is missing or invalid.'}
 $keySet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($key in $r.keys){if($key -isnot [string] -or $key.Contains([char]0) -or -not $keySet.Add($key)){throw 'Typed registry key evidence is invalid.'}}
 if($r.exists -and -not $keySet.Contains('')){throw 'Typed registry root evidence is absent.'}
 $valueIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$registryBytes=0L
 foreach($v in $r.values){if($v.kind -cnotin @('Binary','None','DWord','QWord','String','ExpandString','MultiString') -or $v.data -isnot [string] -or $v.name -isnot [string] -or $v.key -isnot [string] -or -not $keySet.Contains($v.key) -or $v.name.Contains([char]0) -or -not $valueIds.Add($v.key+[char]0+$v.name)){throw 'Typed registry evidence is invalid.'};$bytes=[Convert]::FromBase64String($v.data);$registryBytes+=$bytes.Length;if(($v.kind -ceq 'DWord' -and $bytes.Length -ne 4) -or ($v.kind -ceq 'QWord' -and $bytes.Length -ne 8) -or ($v.kind -cin @('String','ExpandString') -and $bytes.Length%2 -ne 0)){throw 'Typed registry data has invalid byte length.'}}
 if($registryBytes -gt 8388608 -or $total+$registryBytes -gt 67108864){throw 'Shared-state backup exceeds 64 MiB total bound.'}
}
function Get-AzuriteDecimalBinary($Value) {
 if(-not $Value -or $Value.kind -cne 'Binary'){return $null}
 $b=[Convert]::FromBase64String($Value.data)
 if($b.Length -lt 2 -or $b.Length -gt 65 -or $b[$b.Length-1] -ne 0){return $null}
 for($i=0;$i -lt $b.Length-1;$i++){if($b[$i] -lt 48 -or $b[$i] -gt 57){return $null}}
 $text=[Text.Encoding]::ASCII.GetString($b,0,$b.Length-1).TrimStart('0');if(-not $text){return '0'};return $text
}
function Get-AzuriteDecimalSuccessor([string]$Value) {
 $chars=$Value.ToCharArray();$carry=$true
 for($i=$chars.Length-1;$i -ge 0 -and $carry;$i--){if($chars[$i] -eq '9'){$chars[$i]='0'}else{$chars[$i]=[char]([int]$chars[$i]+1);$carry=$false}}
 $result=-join $chars;if($carry){$result='1'+$result};return $result
}
function Compare-AzuriteSharedSnapshots($Before,$After,$Spec,[string]$RunId) {
 Assert-AzuriteSharedSnapshot $Before $Spec $RunId Before;Assert-AzuriteSharedSnapshot $After $Spec $RunId After
 if((ConvertTo-AzuriteUtcInstant $After.capturedUtc) -lt (ConvertTo-AzuriteUtcInstant $Before.capturedUtc)){throw 'Shared-state snapshot chronology is invalid.'}
 $unexpected=[Collections.Generic.List[object]]::new();$metadata=[Collections.Generic.List[object]]::new();$deltas=[Collections.Generic.List[object]]::new();$registryChanged=$false
 if(($Before.roots|ConvertTo-Json -Compress) -cne ($After.roots|ConvertTo-Json -Compress) -or ($Before.directories -join [char]0) -cne ($After.directories -join [char]0)){$change=[pscustomobject]@{category='filesystem-structure';before=$Before.roots;after=$After.roots};$unexpected.Add($change);$deltas.Add($change)}
 $old=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal);$new=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($f in $Before.files){$old[$f.id]=$f};foreach($f in $After.files){$new[$f.id]=$f}
 foreach($id in @(@($old.Keys)+@($new.Keys)|Sort-Object -Unique)){
  $a=if($old.ContainsKey($id)){$old[$id]}else{$null};$b=if($new.ContainsKey($id)){$new[$id]}else{$null}
  if(-not $a -or -not $b -or $a.sha256 -cne $b.sha256 -or $a.attributes -ne $b.attributes -or (ConvertTo-AzuriteUtcInstant $a.lastWriteUtc).Ticks -ne (ConvertTo-AzuriteUtcInstant $b.lastWriteUtc).Ticks){$change=[pscustomobject]@{category='file';id=$id;before=$(if($a){@{sha256=$a.sha256;bytes=$a.bytes;lastWriteUtc=$a.lastWriteUtc}}else{$null});after=$(if($b){@{sha256=$b.sha256;bytes=$b.bytes;lastWriteUtc=$b.lastWriteUtc}}else{$null});rawBackup='before.json/after.json'};$unexpected.Add($change);$deltas.Add($change)}
 }
 if($Before.registry.exists -ne $After.registry.exists -or ($Before.registry.keys -join [char]0) -cne ($After.registry.keys -join [char]0)){$registryChanged=$true;$change=[pscustomobject]@{category='registry-structure';before=$Before.registry.keys;after=$After.registry.keys};$unexpected.Add($change);$deltas.Add($change)}
 $old.Clear();$new.Clear();foreach($v in $Before.registry.values){$old[$v.key+[char]0+$v.name]=$v};foreach($v in $After.registry.values){$new[$v.key+[char]0+$v.name]=$v}
 foreach($id in @(@($old.Keys)+@($new.Keys)|Sort-Object -Unique)){
  $a=if($old.ContainsKey($id)){$old[$id]}else{$null};$b=if($new.ContainsKey($id)){$new[$id]}else{$null}
  if($a -and $b -and $a.kind -ceq $b.kind -and $a.data -ceq $b.data){continue}
  $registryChanged=$true;$allowed=$false;$name=if($a){$a.name}else{$b.name}
  if($a -and $b -and $a.key -ceq '' -and $b.key -ceq '' -and $name -cin @('unity.player_session_count_h922449978','unity.player_sessionid_h1351336811')){
   $av=Get-AzuriteDecimalBinary $a;$bv=Get-AzuriteDecimalBinary $b
   if($null -ne $av -and $null -ne $bv){$allowed=($name -ceq 'unity.player_sessionid_h1351336811') -or $bv -ceq (Get-AzuriteDecimalSuccessor $av)}
  }
  $change=[pscustomobject]@{category=$(if($allowed){'native-engine-session-metadata'}else{'unexpected-registry'});name=$name;before=$a;after=$b;automaticallyRestored=$false}
  $deltas.Add($change);if($allowed){$metadata.Add($change)}else{$unexpected.Add($change)}
 }
 return [pscustomobject]@{schema=1;runId=$RunId;accepted=($unexpected.Count -eq 0);userPreferencesUnchanged=($unexpected.Count -eq 0);registryChanged=$registryChanged;finalStateUnchanged=($deltas.Count -eq 0);engineMetadata=$metadata.ToArray();unexpected=$unexpected.ToArray();deltas=$deltas.ToArray();restorationPerformed=$false}
}
function Complete-AzuriteCurrentCleanupGate([string]$LabRoot,[string]$RunId,[string]$ControllerSid,$Cleanup,$StateComparison) {
 if(-not $StateComparison -or $StateComparison.schema -ne 1 -or $StateComparison.runId -cne $RunId -or $StateComparison.accepted -ne $true -or $StateComparison.userPreferencesUnchanged -ne $true -or @($StateComparison.unexpected).Count -ne 0 -or $StateComparison.restorationPerformed -ne $false){throw 'Shared-state preservation is unverified or changed unexpectedly; quarantine must remain.'}
 Complete-AzuriteCleanupGate $LabRoot $RunId $ControllerSid $Cleanup
}
function Get-AzuriteCurrentPrerequisites([string]$LabRoot,$Contract,$Context,$Spec,$Snapshot) {
 $failures=[Collections.Generic.List[string]]::new()
 try{
  if($Context.sid -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or $Context.isAdministrator -ne $false){throw 'Current token must be the unelevated controller identity.'}
  Assert-AzuriteCleanupGateClear $LabRoot
  $marker=Read-AzuriteBoundedJson (Join-Path $LabRoot 'lab.marker.json') 16384;$id=[guid]::Empty
  if($marker.schema -ne 1 -or $marker.purpose -cne 'AzuriteHeadlessLab' -or $marker.controllerSid -cne $Context.sid -or -not [guid]::TryParse($marker.labId,[ref]$id) -or -not $LabRoot.Equals($Contract.labRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Current-account marked lab identity differs.'}
  [void](Assert-AzuriteLabPaths $LabRoot (Join-Path $LabRoot 'install') $marker.dailyInstallPath)
  Assert-AzuriteNoReparseTree $LabRoot;Assert-AzuritePinnedInstall (Join-Path $LabRoot 'install') $Contract;Assert-AzuriteEvidence $Contract
  foreach($name in @('runs','secrets')){$path=Join-Path $LabRoot $name;if(Test-Path -LiteralPath $path){Assert-AzuriteCurrentPrivatePath $path $Context.sid}}
  if([Diagnostics.Process]::GetProcessesByName('AzureArchive').Length){throw 'Another AzureArchive process exists.'}
  Assert-AzuriteSharedSnapshot $Snapshot $Spec 'plan' Plan
 }catch{$failures.Add($_.Exception.Message)}
 return [pscustomobject]@{IdentityMode='CurrentAccount';Ready=($failures.Count -eq 0);WillLaunch=$false;RequiresCredentials=$false;RequiresAccountSetup=$false;AccountIsolation=$false;SharedHKCU=$true;SharedProfile=$true;NativeKnownFolderVerificationRequired=$true;WillPrepareLabDirectories=@('runs','secrets');Failures=$failures.ToArray();Acceptance=$Contract.acceptance}
}
Export-ModuleMember -Function Assert-AzuriteCurrentIdentity,Assert-AzuriteCurrentIdentityRecord,Assert-AzuriteCurrentPrivatePath,Set-AzuriteRunPathAccess,Assert-AzuriteRunPathAccess,Assert-AzuritePreparedHash,Get-AzuriteRegisteredLocalLow,New-AzuriteSharedStateSpec,Read-AzuritePlayerPrefsSnapshot,New-AzuriteSharedSnapshot,Assert-AzuriteSharedSnapshot,Compare-AzuriteSharedSnapshots,Complete-AzuriteCurrentCleanupGate,Get-AzuriteCurrentPrerequisites
