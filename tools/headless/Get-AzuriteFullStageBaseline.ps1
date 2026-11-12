[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunId,[switch]$Capture)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$script:BaselineRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:BaselineParent='F:\AzuriteLabs'
$script:BaselineLab='F:\AzuriteLabs\Editor-20261108-normal'
$script:BaselineOwner='S-1-5-21-222280291-4039000738-1914192245-1001'
$script:BaselineVolume='\\?\Volume{a7c77c39-d000-4faf-bbf8-cc8ca0c743cb}\'
$script:BaselineHelper='artifacts/aa-fr-transition-preparation/integration-20261110-01/helper-build1/FullRestrictedStageFileSecurity.dll'
$script:BaselineHelperSha='eef4c21387daef216ef7bcc7870b331e123d69e7be852498adcd07f253ef4c8f'
function Assert-AzuriteBaseline($Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Get-AzuriteBaselineHash([byte[]]$Bytes){$hash=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($hash.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}}
function Get-AzuriteBaselineTextHash([string]$Text){Get-AzuriteBaselineHash ([Text.UTF8Encoding]::new($false).GetBytes($Text))}
function ConvertFrom-AzuriteBaselineJson([byte[]]$Bytes){$text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes);if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){return ($text|ConvertFrom-Json -DateKind String)};return ($text|ConvertFrom-Json)}
function Read-AzuriteBaselineFrozen([string]$Relative,[string]$Expected){
 Assert-AzuriteBaseline ($Relative -match '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -and $Relative -notmatch '(^|/)\.\.(/|$)') 'Invalid frozen source relative path'
 $path=[IO.Path]::GetFullPath((Join-Path $script:BaselineRepo $Relative));Assert-AzuriteBaseline ($path.StartsWith($script:BaselineRepo+'\',[StringComparison]::Ordinal) -and $path.StartsWith('C:\',[StringComparison]::Ordinal)) 'Frozen source must remain in C workspace'
 $cursor=$path;while($cursor){$item=Get-Item -LiteralPath $cursor -Force;Assert-AzuriteBaseline (-not($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Frozen C source contains reparse ancestry';$cursor=Split-Path -Parent $cursor}
 $bytes=[IO.File]::ReadAllBytes($path);Assert-AzuriteBaseline ($bytes.Length -le 8388608 -and (Get-AzuriteBaselineHash $bytes) -ceq $Expected) ('Frozen C source pin differs: '+$Relative);return ,$bytes
}
function New-AzuriteStageBaselineScope([string]$Id){
 Assert-AzuriteBaseline ($Id -cmatch '^[a-f0-9]{32}$' -and $Id -cne ('0'*32)) 'RunId must be canonical nonzero lowercase 32-hex'
 $stage=ConvertFrom-AzuriteBaselineJson (Read-AzuriteBaselineFrozen 'artifacts/editor-normal-install-preparation/source-bound-manifest-v2.json' '0bddb4285f5cee0c79c3f2ab6f6ab8c346937ae8ff12b4f49f29a6f102bd33dd')
 $saved=ConvertFrom-AzuriteBaselineJson (Read-AzuriteBaselineFrozen 'artifacts/editor-normal-install-postverify/verification-08a369f8f1e54fc58da8e0d2f72bf607.json' '806f4524aa7e7025e490be9aab9b87225195090149d9ede9727796b7ad0bf26d')
 $helperBytes=Read-AzuriteBaselineFrozen $script:BaselineHelper $script:BaselineHelperSha
 Assert-AzuriteBaseline ($stage.labRoot -ceq $script:BaselineLab -and $stage.parentRoot -ceq $script:BaselineParent -and $stage.destinationVolumeGuid -ceq $script:BaselineVolume -and $stage.runtimeAllowed -eq $false -and $stage.entries.Count -eq 1062 -and $stage.directories.Count -eq 156 -and $saved.verifiedBytes -eq 3521516451) 'Frozen fixed-stage identity/count differs'
 $install=$script:BaselineLab+'\install';$objects=[Collections.Generic.List[object]]::new();$inventory=@{};$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($path in @($script:BaselineParent,$script:BaselineLab,$install)+@($stage.directories|ForEach-Object {$install+'\'+$_.Replace('/','\')})){
  Assert-AzuriteBaseline ($seen.Add($path)) 'Duplicate stage directory';$ancestor=$path -ceq $script:BaselineParent -or $path -ceq $script:BaselineLab
  $objects.Add([pscustomobject]@{path=$path;kind=$(if($ancestor){'AncestorDirectory'}else{'InstallDirectory'});directory=$true;bytes=0L;sha256=$null;aclSha256=$null})
  $inventory[$path]=[Collections.Generic.List[string]]::new()
 }
 $inventory[$script:BaselineParent].Add('.azurite-phase0-parent.json');$inventory[$script:BaselineParent].Add('Editor-20261108-normal')
 foreach($name in @('install','.phase0-staging','lab.marker.json')){$inventory[$script:BaselineLab].Add($name)}
 foreach($relative in $stage.directories){$path=$install+'\'+$relative.Replace('/','\');$inventory[(Split-Path -Parent $path)].Add([IO.Path]::GetFileName($path))}
 $savedIndex=@{};foreach($file in $saved.destinationFiles){$savedIndex[$file.path]=$file}
 $total=0L;foreach($file in $stage.entries){
  Assert-AzuriteBaseline ($file.path -notmatch '(^|/)\.\.(/|$)|[:\\\x00-\x1f]' -and $savedIndex.ContainsKey($file.path)) 'Unsafe or unbound manifest file'
  $path=$install+'\'+$file.path.Replace('/','\');Assert-AzuriteBaseline ($seen.Add($path)) 'Duplicate manifest file';$prior=$savedIndex[$file.path]
  Assert-AzuriteBaseline ($prior.sha256 -ceq $file.sha256 -and $prior.bytes -eq $file.bytes -and $prior.aclSha256 -ceq '17318e3e1dea3198a70e18d48bc75e610e04c8fd4a2eed8e46b09ec9aadf8da8') 'Saved file bytes/ACL pin differs'
  $objects.Add([pscustomobject]@{path=$path;kind='InstallFile';directory=$false;bytes=[long]$file.bytes;sha256=$file.sha256;aclSha256=$prior.aclSha256});$total+=[long]$file.bytes;$inventory[(Split-Path -Parent $path)].Add([IO.Path]::GetFileName($path))
 }
 $attempt=$script:BaselineLab+'\.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9'
 $anchors=[ordered]@{($script:BaselineParent+'\.azurite-phase0-parent.json')='8098538d54eadb7b6ee5cf07b514fb311a16ecdc97b80558fd30db565d9bd4fe';($script:BaselineLab+'\lab.marker.json')='8098538d54eadb7b6ee5cf07b514fb311a16ecdc97b80558fd30db565d9bd4fe';($script:BaselineLab+'\.phase0-staging\install-creation-seal.json')='abe2163a148bb825bd92edfd60a32a576bfa6dc583b7da24599617ae550ed454';($attempt+'\complete.json')='15506ff9993a2c51f3d508a880e3e2f3ec4eeae5417124b2fc7d42a2f2a90c28';($attempt+'\files.jsonl')='2fa532b2c2a483e8645b52d97bc2c916f0d94c438680271ae1f510952b8de1c2';($attempt+'\start.json')='c4e73c894ec326c1736bd9f05aaec4d993d1b4927217775d31ff86ce138ca008';($attempt+'\seal-complete.json')='371ff1df832daba64dd8206c49a4b941f026943399cee702b1cdfc9cfbaa246f'}
 foreach($path in $anchors.Keys){Assert-AzuriteBaseline ($seen.Add($path)) 'Duplicate historical anchor';$objects.Add([pscustomobject]@{path=$path;kind='HistoricalAnchor';directory=$false;bytes=$null;maximumBytes=1048576;sha256=$anchors[$path];aclSha256=$null})}
 Assert-AzuriteBaseline ($objects.Count -eq 1228 -and $total -eq 3521516451 -and $inventory.Count -eq 159) 'Exact historical collection scope differs'
 return [pscustomobject]@{schema=1;runId=$Id;labRoot=$script:BaselineLab;ownerSid=$script:BaselineOwner;volumeGuid=$script:BaselineVolume;objects=$objects.ToArray();inventory=$inventory;installBytes=$total;maximumAnchorBytes=7340032;bytesPerSecond=4194304;maximumWaitMilliseconds=50;maximumElapsedSeconds=3600;helperRelative=$script:BaselineHelper;helperSha256=$script:BaselineHelperSha;helperBytes=$helperBytes}
}
function Assert-AzuriteStageBaselineObject($Scope,$Entry,$Actual){
 Assert-AzuriteBaseline ($Actual.path -ceq $Entry.path -and $Actual.finalPath -ceq $Entry.path -and $Actual.volume -ceq $Scope.volumeGuid -and $Actual.owner -ceq $Scope.ownerSid -and $Actual.directory -eq $Entry.directory -and -not $Actual.reparse -and ($Entry.directory -or $Actual.links -eq 1)) ('Object identity/type/volume/owner/link differs: '+$Entry.path)
 Assert-AzuriteBaseline ($Actual.identity -cmatch '^[a-f0-9]{32}$' -and $Actual.identity -cne ('0'*32)) 'Invalid actual file identity'
 $sd=[Security.AccessControl.RawSecurityDescriptor]::new($Actual.sddl);Assert-AzuriteBaseline ($sd.Owner.Value -ceq $Scope.ownerSid -and $null -ne $sd.Group -and $sd.Group.Value -match '^S-1-' -and $null -ne $sd.DiscretionaryAcl) 'Missing actual full Owner/Group/Access descriptor'
 $ownerAccess=$sd.GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Access')
 if($Entry.kind -cin @('AncestorDirectory','InstallDirectory')){
  $wanted='O:'+$Scope.ownerSid+'D:P'+$(if($Entry.kind -ceq 'InstallDirectory'){'(D;;0x6;;;'+$Scope.ownerSid+')'}else{''})+'(A;OICI;FA;;;'+$Scope.ownerSid+')(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)'
  $wanted=[Security.AccessControl.RawSecurityDescriptor]::new($wanted).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Access');Assert-AzuriteBaseline ($ownerAccess -ceq $wanted) 'Frozen private directory/creation seal differs'
 }elseif($Entry.kind -ceq 'InstallFile'){Assert-AzuriteBaseline ((Get-AzuriteBaselineTextHash $ownerAccess) -ceq $Entry.aclSha256) 'Frozen inherited file DACL differs'}else{
  $seen=@{};foreach($ace in $sd.DiscretionaryAcl){Assert-AzuriteBaseline ($ace -is [Security.AccessControl.CommonAce] -and -not $ace.IsCallback -and $ace.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and $ace.AccessMask -eq 0x1f01ff -and [int]$ace.AceFlags -in @(0,16) -and $ace.SecurityIdentifier.Value -cin @($Scope.ownerSid,'S-1-5-18','S-1-5-32-544') -and -not $seen.ContainsKey($ace.SecurityIdentifier.Value)) 'Historical anchor is not controller/SYSTEM/admin private';$seen[$ace.SecurityIdentifier.Value]=$true};Assert-AzuriteBaseline ($seen.Count -eq 3) 'Historical anchor baseline rights missing'
 }
 if(-not $Entry.directory){Assert-AzuriteBaseline ($Actual.sha256 -ceq $Entry.sha256 -and ($null -eq $Entry.bytes -or $Actual.bytes -eq $Entry.bytes)) ('Frozen payload hash/length differs: '+$Entry.path);if($Entry.kind -ceq 'HistoricalAnchor'){Assert-AzuriteBaseline ($Actual.bytes -ge 1 -and $Actual.bytes -le $Entry.maximumBytes) 'Historical anchor size exceeds bound'}}
}
function Wait-AzuriteStageBaselineBudget([long]$ReadBytes,[scriptblock]$ElapsedMilliseconds,[scriptblock]$WaitMilliseconds,[scriptblock]$CheckNoAA){
 $requiredMilliseconds=1000.0*$ReadBytes/4194304.0;$checked=-1000.0
 while($true){$elapsed=[double](& $ElapsedMilliseconds);Assert-AzuriteBaseline ($elapsed -le 3600000) 'Baseline capture exceeded one-hour bound';if($elapsed-$checked -ge 1000){& $CheckNoAA;$checked=$elapsed};$deficit=$requiredMilliseconds-$elapsed;if($deficit -le 0){return};& $WaitMilliseconds ([int][Math]::Min(50,[Math]::Ceiling($deficit)))}
}
function New-AzuriteStageBaselineRecord($Scope,$Observations){
 return [pscustomobject][ordered]@{schema=1;scope='preapproval-stage-object-identities';runId=$Scope.runId;objects=@(foreach($item in $Observations){[pscustomobject][ordered]@{path=$item.path;identity=$item.identity;sddl=([Security.AccessControl.RawSecurityDescriptor]::new($item.sddl).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access'));bytes=[long]$item.bytes}})}
}
function Write-AzuriteBaselineNewJson([string]$Path,$Value){
 Assert-AzuriteBaselineCOutput $Path;$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 30 -Compress));$stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()};Assert-AzuriteBaseline ((Get-AzuriteBaselineHash ([IO.File]::ReadAllBytes($Path))) -ceq (Get-AzuriteBaselineHash $bytes)) 'C output readback differs';return Get-AzuriteBaselineHash $bytes
}
function Assert-AzuriteBaselineCOutput([string]$Path){
 Assert-AzuriteBaseline ($Path.StartsWith('C:\',[StringComparison]::Ordinal) -and [IO.Path]::GetFullPath($Path) -ceq $Path) 'Output must be exact C-only path'
 $cursor=Split-Path -Parent $Path;while($cursor){$item=Get-Item -LiteralPath $cursor -Force;Assert-AzuriteBaseline ($item.PSIsContainer -and -not($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'C output ancestry contains reparse/non-directory object';$cursor=Split-Path -Parent $cursor}
}
function Assert-AzuriteBaselineNoAA {Assert-AzuriteBaseline (@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count -eq 0) 'AzureArchive appeared; abort read-only capture without terminating it'}
function Lock-AzuriteBaselineAncestry($State,[string]$Path,[bool]$Directory){
 $cursor=if($Directory){$Path}else{Split-Path -Parent $Path};$chain=[Collections.Generic.List[string]]::new();while($cursor){$chain.Add($cursor);$cursor=Split-Path -Parent $cursor}
 for($i=$chain.Count-1;$i -ge 0;$i--){$path=$chain[$i];if(-not $State.handles.ContainsKey($path)){$State.handles[$path]=[Azurite.Stage.FileSecurity]::LockDirectory($path)}}
}
function Read-AzuriteBaselineDirectory($State,[string]$Path,[scriptblock]$VisitChild){
 Lock-AzuriteBaselineAncestry $State $Path $true
 foreach($child in [IO.Directory]::EnumerateFileSystemEntries($Path)){& $VisitChild $child}
}
function Assert-AzuriteBaselineInventory($Scope,$State,[scriptblock]$ReadDirectory={param($s,$p,$visit)Read-AzuriteBaselineDirectory $s $p $visit}){
 # Establish the historical P/L child sets before opening any install descendant.
 # L has neither runs nor secrets: any added pending ancestor is itself rejected.
 $roots=@('F:\AzuriteLabs',$Scope.labRoot)
 $orderedPaths=$roots+@($Scope.inventory.Keys|Where-Object {$_ -cnotin $roots}|Sort-Object -CaseSensitive)
 foreach($path in $orderedPaths){$names=[Collections.Generic.List[string]]::new();& $ReadDirectory $State $path {param($child)$names.Add([IO.Path]::GetFileName($child));Assert-AzuriteBaseline ($names.Count -le $Scope.inventory[$path].Count) ('Unexpected child in declared directory: '+$path)};$actual=@($names|Sort-Object -CaseSensitive);$expected=@($Scope.inventory[$path]|Sort-Object -CaseSensitive);Assert-AzuriteBaseline (($actual -join "`n") -ceq ($expected -join "`n")) ('Exact original child set/case differs: '+$path);if($State.timer.ElapsedMilliseconds-$State.lastAACheck -ge 1000){Assert-AzuriteBaselineNoAA;$State.lastAACheck=$State.timer.ElapsedMilliseconds}}
}
function Invoke-AzuriteStageBaselineCapture($Scope){
 Assert-AzuriteBaseline ($PSVersionTable.PSEdition -ceq 'Desktop' -and [IntPtr]::Size -eq 8) 'Capture requires fresh 64-bit Windows PowerShell'
 Assert-AzuriteBaseline ($null -eq ('Azurite.Stage.FileSecurity' -as [type])) 'Capture refuses previously loaded file/security helper types'
 $base=Join-Path $script:BaselineRepo 'artifacts\aa-fr-stage-baseline';Assert-AzuriteBaselineCOutput $base;[void][IO.Directory]::CreateDirectory($base);$output=Join-Path $base ('capture-'+$Scope.runId+'-'+[guid]::NewGuid().ToString('N'));Assert-AzuriteBaselineCOutput $output;Assert-AzuriteBaseline (-not [IO.Directory]::Exists($output)) 'Fresh C capture directory collision';[void][IO.Directory]::CreateDirectory($output)
 $null=Write-AzuriteBaselineNewJson (Join-Path $output 'intent.json') ([pscustomobject]@{schema=1;runId=$Scope.runId;scope='fixed-F-read-only-preapproval-baseline';helperSha256=$Scope.helperSha256;objects=1228;installBytes=$Scope.installBytes;bytesPerSecond=4194304;throttle='per-file burst then cumulative catchup; each wait at most 50ms';willWriteF=$false;willLaunch=$false;stageRuntimeAllowed=$false})
 [void][Reflection.Assembly]::Load($Scope.helperBytes)
 $state=@{handles=@{};timer=[Diagnostics.Stopwatch]::StartNew();lastAACheck=0L};$process=[Diagnostics.Process]::GetCurrentProcess();$oldPriority=$process.PriorityClass;$priorityChanged=$false;$mutex=$null;$held=$false;$release='not-acquired';$started=$null;$released=$null;$errorText=$null;$readBytes=0L;$observations=[Collections.Generic.List[object]]::new();$identities=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 try{
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent();try{Assert-AzuriteBaseline ($identity.User.Value -ceq $Scope.ownerSid -and -not [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Collector requires exact unelevated controller account'}finally{$identity.Dispose()}
  $process.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal;$priorityChanged=$true;Assert-AzuriteBaseline ($process.PriorityClass -eq [Diagnostics.ProcessPriorityClass]::BelowNormal) 'BelowNormal priority readback failed'
  Assert-AzuriteBaselineNoAA;$mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true;throw 'Abandoned global mutex requires explicit review; no capture'};Assert-AzuriteBaseline $held 'Another controller owns the global read lease';$started=[datetime]::UtcNow.ToString('o');Assert-AzuriteBaselineNoAA
  Assert-AzuriteBaselineInventory $Scope $state
  foreach($entry in $Scope.objects){
   Assert-AzuriteBaseline ($state.timer.Elapsed.TotalSeconds -lt $Scope.maximumElapsedSeconds) 'Baseline capture exceeded one-hour bound'
   if($state.timer.ElapsedMilliseconds-$state.lastAACheck -ge 1000 -or (-not $entry.directory -and $entry.bytes -ge 4194304)){Assert-AzuriteBaselineNoAA;$state.lastAACheck=$state.timer.ElapsedMilliseconds}
   Lock-AzuriteBaselineAncestry $state $entry.path $entry.directory
   $meta=Get-Item -LiteralPath $entry.path -Force;Assert-AzuriteBaseline (-not($meta.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $meta.PSIsContainer -eq $entry.directory) 'Object metadata type/link differs before read'
   if(-not $entry.directory){if($null -ne $entry.bytes){Assert-AzuriteBaseline ($meta.Length -eq $entry.bytes) 'File length differs before bounded payload read'}else{Assert-AzuriteBaseline ($meta.Length -le 1048576) 'Historical anchor exceeds pre-read size limit'}}
   $actual=[Azurite.Stage.FileSecurity]::Read($entry.path,($entry.kind -ceq 'HistoricalAnchor'));Assert-AzuriteStageBaselineObject $Scope $entry $actual;Assert-AzuriteBaseline ($identities.Add($actual.identity)) 'Two declared paths alias the same object identity'
   if(-not $entry.directory){$readBytes+=$actual.bytes;Wait-AzuriteStageBaselineBudget $readBytes {$state.timer.Elapsed.TotalMilliseconds} {param($milliseconds)[Threading.Thread]::Sleep($milliseconds)} {Assert-AzuriteBaselineNoAA;$state.lastAACheck=$state.timer.ElapsedMilliseconds}}
   if($entry.path.EndsWith('lab.marker.json') -or $entry.path.EndsWith('.azurite-phase0-parent.json')){$marker=ConvertFrom-AzuriteBaselineJson ([Convert]::FromBase64String($actual.content));Assert-AzuriteBaseline ($marker.runtimeAllowed -eq $false -and $marker.controllerSid -ceq $Scope.ownerSid -and $marker.labRoot -ceq $Scope.labRoot -and $marker.manifestSha256 -ceq '0bddb4285f5cee0c79c3f2ab6f6ab8c346937ae8ff12b4f49f29a6f102bd33dd') 'Historical marker semantics differ'}
   $observations.Add($actual)
  }
  Assert-AzuriteBaselineNoAA;Assert-AzuriteBaselineInventory $Scope $state;Assert-AzuriteBaseline ($observations.Count -eq 1228) 'Incomplete actual baseline observation set'
 }catch{$errorText=$_.Exception.ToString()}
 finally{
  foreach($handle in $state.handles.Values){try{$handle.Dispose()}catch{$errorText+='; directory handle close failed: '+$_.Exception.Message}}
  if($held){try{$mutex.ReleaseMutex();$release='released';$released=[datetime]::UtcNow.ToString('o')}catch{$release='release-failed';$errorText+='; mutex release failed: '+$_.Exception.Message}}
  if($mutex){$mutex.Dispose()};if($priorityChanged){try{$process.PriorityClass=$oldPriority}catch{$errorText+='; priority restore failed: '+$_.Exception.Message}};$state.timer.Stop();$process.Dispose()
 }
 $summary=[pscustomobject]@{schema=1;runId=$Scope.runId;scope='read-only-stage-baseline-capture';success=([string]::IsNullOrEmpty($errorText) -and $release -ceq 'released');observedUtc=[datetime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');lease=[pscustomobject]@{mutex='Global\AzuriteRestrictedCli-AA';heldFromUtc=$started;releasedUtc=$released;state=$release};observedObjects=$observations.Count;readBytes=$readBytes;elapsedSeconds=$state.timer.Elapsed.TotalSeconds;readBudgetBytesPerSecond=4194304;throttle='cumulative per-file catchup; individual helper file reads may burst';priority='BelowNormal during capture';rawDescriptorSections='Owner,Group,Access; SACL not read';groupEvidence='Actual preapproval observation; saved Owner/Access pins do not independently bind group';error=$errorText;willWriteF=$false;runtimeAllowed=$false;launchPermission=$false}
 $null=Write-AzuriteBaselineNewJson (Join-Path $output 'observations.json') $observations.ToArray()
 if($summary.success){$baseline=New-AzuriteStageBaselineRecord $Scope $observations.ToArray();$sha=Write-AzuriteBaselineNewJson (Join-Path $output 'identity-baseline.json') $baseline;$summary|Add-Member -NotePropertyName baselineSha256 -NotePropertyValue $sha}
 $null=Write-AzuriteBaselineNewJson (Join-Path $output 'capture-result.json') $summary
 $summary|Add-Member -NotePropertyName evidenceDirectory -NotePropertyValue $output
 if(-not $summary.success){throw ('Read-only baseline capture failed; retained C evidence: '+$output+'; '+$errorText)}
 return $summary
}
if($MyInvocation.InvocationName -ne '.'){
 $scope=New-AzuriteStageBaselineScope $RunId
 if($Capture){Invoke-AzuriteStageBaselineCapture $scope}else{[pscustomobject]@{schema=1;mode='planning-only';runId=$RunId;labRoot=$scope.labRoot;objectCount=$scope.objects.Count;installFiles=1062;sealedInstallDirectories=157;existingAncestors=2;historicalAnchors=7;inventoryCount=$scope.inventory.Count;installBytes=$scope.installBytes;bytesPerSecond=$scope.bytesPerSecond;maximumWaitMilliseconds=$scope.maximumWaitMilliseconds;helperSha256=$scope.helperSha256;willReadF=$false;willWriteF=$false;willLaunch=$false}}
}
