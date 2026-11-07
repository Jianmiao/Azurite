Set-StrictMode -Version Latest

function Get-AzuriteSha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Write-AzuriteFlushedJson([string]$Path,$Value) {
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 30 -Compress))
 $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
 try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Read-AzuriteBoundedJson([string]$Path,[long]$MaxBytes=1048576) {
 $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
 if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -lt 2 -or $item.Length -gt $MaxBytes){throw 'Missing, linked or oversized JSON artifact.'}
 return [IO.File]::ReadAllText($Path)|ConvertFrom-Json -ErrorAction Stop
}
function Read-AzuriteStartupContract([string]$Path=(Join-Path $PSScriptRoot 'restricted-startup-contract.json')) {
 # This literal is the reviewed contract's digest, never calculated from the install.
 if((Get-AzuriteSha256 $Path) -cne '8B27AB740452DCA80E78335E4A7512242EBCFD65B7C67832A8A6104B1D3DE06F'){throw 'Unapproved startup contract digest.'}
 $contract=Read-AzuriteBoundedJson $Path
 if($contract.schema -ne 1 -or $contract.contractId -cne 'aa-2023.2.22f1-azurite-1.0.38-cli-nosound-v1'){throw 'Unapproved startup contract identity.'}
 return $contract
}
function Assert-AzuritePinnedInstall([string]$Install,$Contract) {
 if($Contract.schema -ne 1){throw 'Unknown contract schema.'}
 if(((Get-Item -LiteralPath $Install -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or @(Get-ChildItem -LiteralPath $Install -Force -Recurse -Attributes ReparsePoint).Count){throw 'Pinned install contains a reparse link.'}
 if((Get-Content -LiteralPath (Join-Path $Install 'ActiveProfile.txt') -Raw).Trim() -cne $Contract.profile){throw 'Wrong active profile.'}
 $profile=Read-AzuriteBoundedJson (Join-Path $Install ('profiles\'+$Contract.profile+'\modconfig.json'))
 if(@($profile.EnabledMods).Count -ne 1 -or $profile.EnabledMods[0].name -cne $Contract.modName -or $profile.EnabledMods[0].version -cne $Contract.modVersion){throw 'Unexpected enabled mod profile.'}
 foreach($pin in $Contract.files){
  if($pin.path -match '(^[\\/]|:|(^|[\\/])\.\.([\\/]|$))'){throw 'Invalid pinned relative path.'}
  $path=Join-Path $Install $pin.path
  $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
  if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or (Get-AzuriteSha256 $path) -cne $pin.sha256.ToUpperInvariant()){throw "Pinned input changed: $($pin.path)"}
 }
 foreach($scope in $Contract.exactDirectories){
  $spec=[pscustomobject]$scope
  $directory=[IO.Path]::GetFullPath((Join-Path $Install $scope.path))
  if(-not(Test-Path -LiteralPath $directory -PathType Container)){throw "Loader scope missing: $($scope.path)"}
  $recurse=$true;if($spec.PSObject.Properties['recurse']){$recurse=[bool]$spec.recurse}
  $files=@(Get-ChildItem -LiteralPath $directory -File -Force -Recurse:$recurse)
  if($spec.PSObject.Properties['extensions']){$files=@($files|Where-Object{$_.Extension -in $spec.extensions})}
  $actual=@($files|ForEach-Object{$_.FullName.Substring($directory.Length+1).Replace('\','/')}|Sort-Object)
  $expected=@($scope.files|Sort-Object)
  if(($actual -join '|') -cne ($expected -join '|')){throw "Loader/config scope drift: $($scope.path)"}
 }
}
function Assert-AzuriteEvidence($Contract) {
 foreach($proof in $Contract.evidence){if((Get-AzuriteSha256 $proof.path) -cne $proof.sha256.ToUpperInvariant()){throw "Static evidence changed: $($proof.path)"}}
}
function Read-AzuriteCliResponse([string]$Path,[ValidateSet('capabilities','schema')][string]$Command,[datetime]$StartedUtc) {
 $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
 if($item.CreationTimeUtc -lt $StartedUtc -or $item.LastWriteTimeUtc -lt $StartedUtc){throw 'Response predates owned native start.'}
 $r=Read-AzuriteBoundedJson $Path
 if($r -isnot [pscustomobject] -or $r.api_version -isnot [string] -or $r.api_version -cne '1' -or $r.ok -isnot [bool] -or -not $r.ok -or $r.data -isnot [pscustomobject] -or @($r.data.PSObject.Properties).Count -eq 0 -or $r.warnings -isnot [array]){throw 'Invalid native success envelope.'}
 $d=$r.data
 if($d.api_version -isnot [string] -or $d.api_version -cne '1'){throw 'Unexpected native data API version.'}
 if($Command -eq 'schema'){
  if($d.'$id' -cne 'aap://schema/api/1' -or $d.'$schema' -cne 'https://json-schema.org/draft/2020-12/schema' -or $d.'$defs' -isnot [pscustomobject] -or $d.tools -isnot [array] -or $d.tools.Count -lt 1 -or $d.cli_commands -isnot [pscustomobject] -or @($d.cli_commands.PSObject.Properties).Count -lt 1){throw 'Native schema shape missing.'}
  foreach($name in @('LineInput','LinePatch')){$def=$d.'$defs'.$name;if($def -isnot [pscustomobject] -or $def.type -cne 'object' -or $def.properties -isnot [pscustomobject] -or @($def.properties.PSObject.Properties).Count -lt 1){throw "Native schema definition missing: $name"}}
 } else {if($d.format_version -ne 2 -or $d.schema_uri -cne 'aap://schema/api/1' -or $d.commands -isnot [array] -or $d.commands.Count -lt 1){throw 'Native capabilities shape missing.'}}
 return [pscustomobject]@{Data=$d;Sha256=(Get-AzuriteSha256 $Path);Bytes=$item.Length;Path=$item.FullName}
}
function ConvertTo-AzuriteWindowsArgument([AllowEmptyString()][string]$Value) {
 # CRT/CommandLineToArgvW escaping: backslashes are doubled only before quotes/end quote.
 if($Value -notmatch '[\s"]' -and $Value.Length -gt 0){return $Value}
 $out=[Text.StringBuilder]::new();[void]$out.Append('"');$slashes=0
 foreach($c in $Value.ToCharArray()){
  if($c -eq '\'){$slashes++;continue}
  if($c -eq '"'){[void]$out.Append(('\' * ($slashes*2+1)));[void]$out.Append('"')}
  else{if($slashes){[void]$out.Append(('\' * $slashes))};[void]$out.Append($c)}
  $slashes=0
 }
 if($slashes){[void]$out.Append(('\' * ($slashes*2)))};[void]$out.Append('"');return $out.ToString()
}
function New-AzuriteProcessInfo([string]$FilePath,[string[]]$Arguments,[string]$WorkingDirectory,[pscredential]$Credential) {
 $p=[Diagnostics.ProcessStartInfo]::new();$p.FileName=$FilePath;$p.Arguments=($Arguments|ForEach-Object{ConvertTo-AzuriteWindowsArgument $_}) -join ' '
 $p.WorkingDirectory=$WorkingDirectory;$p.UseShellExecute=$false;$p.CreateNoWindow=$true;$p.WindowStyle='Hidden';$p.RedirectStandardOutput=$true;$p.RedirectStandardError=$true
 if($Credential){$p.UserName=$Credential.UserName;$p.Domain=$env:COMPUTERNAME;$p.Password=$Credential.Password;$p.LoadUserProfile=$true}
 # Do not even read Environment/EnvironmentVariables here. Windows builds credentialed env.
 return $p
}
function Set-AzuriteCliChildEnvironment([Diagnostics.ProcessStartInfo]$StartInfo,[string]$RequestDirectory) {
 if(-not [string]::IsNullOrEmpty($StartInfo.UserName)){throw 'Cannot override credentialed parent environment.'}
 foreach($name in @('UNITY_DISABLE_AUDIO','DOORSTOP_DISABLE','DOORSTOP_CONFIG_FILE','UNITY_BOOT_CONFIG','AZUREARCHIVE_CLI_DIRECTORY')){
  if(-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name,'Process'))){throw "Unexpected inherited startup override: $name"}
 }
 foreach($name in [Environment]::GetEnvironmentVariables('Process').Keys){if([string]$name -match '^(DOORSTOP_|CORECLR_|COR_|COMPlus_|DOTNET_STARTUP_HOOKS$|DOTNET_ADDITIONAL_DEPS$|DOTNET_SHARED_STORE$)' -and -not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable([string]$name,'Process'))){throw "Unexpected runtime override: $name"}}
 $StartInfo.EnvironmentVariables['AZUREARCHIVE_CLI_DIRECTORY']=$RequestDirectory
}
function Initialize-AzuriteRestrictedNative([string]$AssemblyPath) {
 if(-not ('Azurite.Restricted.ProcessPump' -as [type])){
  if($AssemblyPath){Add-Type -Path $AssemblyPath -ErrorAction Stop}
  else{Add-Type -Path (Join-Path $PSScriptRoot 'RestrictedCliNative.cs') -ErrorAction Stop}
 }
}
function New-AzuriteNetFrameworkHelper([string]$SourcePath,[string]$AssemblyPath) {
 # Controller-only preparation. Target bootstrap loads the resulting protected DLL,
 # never Add-Type source compilation (which could create an out-of-job csc child).
 Initialize-AzuriteRestrictedNative
 if(Test-Path -LiteralPath $AssemblyPath){throw 'Helper assembly path already exists.'}
 $compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $info=New-AzuriteProcessInfo -FilePath $compiler -Arguments @('/nologo','/target:library',('/out:'+$AssemblyPath),$SourcePath) -WorkingDirectory (Split-Path -Parent $AssemblyPath)
 $run=[Azurite.Restricted.ProcessPump]::Run($info,10000,$null,16384,@(),1048576)
 $status=Get-AzuriteProcessStatus $run 'Helper compiler'
 if($status.failed -or -not $run.DirectProcessExited -or -not(Test-Path -LiteralPath $AssemblyPath -PathType Leaf)){throw ('Controller netfx helper compilation failed: '+$status.reason+' '+$run.Stderr+' '+$run.Stdout)}
 return [pscustomobject]@{sourceSha256=(Get-AzuriteSha256 $SourcePath);assemblySha256=(Get-AzuriteSha256 $AssemblyPath);compilerPath=$compiler;compilerPid=$run.Pid}
}
function Get-AzuriteCanonicalProfilePath([string]$Path) {
 if([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:\\' -or $Path.Substring(2) -match '[:/\x00-\x1f"<>|?*]' -or $Path -match '(^|\\)\.{1,2}(\\|$)'){throw 'Profile path must be a canonical absolute local directory without traversal.'}
 $trimmed=$Path.TrimEnd('\');if($trimmed.Length -lt 4){throw 'A drive root cannot be a profile directory.'}
 foreach($part in $trimmed.Substring(3).Split('\')){if([string]::IsNullOrEmpty($part) -or $part -match '[ .]$'){throw 'Ambiguous profile path component.'}}
 $full=[IO.Path]::GetFullPath($trimmed)
 if(-not $full.Equals($trimmed,[StringComparison]::OrdinalIgnoreCase)){throw 'Noncanonical profile path.'}
 return $full
}
function Get-AzuriteTargetIdentityPaths($Identity,[string]$ExpectedSid,[string]$ControllerSid,[string]$ControllerProfile) {
 if($ExpectedSid -ceq $ControllerSid -or $Identity.identitySid -cne $ExpectedSid -or $Identity.isAdministrator -ne $false -or $Identity.hiveLoaded -ne $true -or @($Identity.unexpectedPrivileges).Count -ne 0){throw 'Dedicated low-privilege identity/profile proof failed.'}
 $paths=[ordered]@{}
 foreach($field in @('userProfile','environmentProfile','localAppData','localLow','environmentLocalAppData','environmentAppData','environmentTemp','environmentTmp')){$paths[$field]=Get-AzuriteCanonicalProfilePath $Identity.$field}
 $profile=$paths.userProfile;$controller=Get-AzuriteCanonicalProfilePath $ControllerProfile
 if($profile.Equals($controller,[StringComparison]::OrdinalIgnoreCase) -or $profile.StartsWith($controller+'\',[StringComparison]::OrdinalIgnoreCase) -or $controller.StartsWith($profile+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $paths.environmentProfile.Equals($profile,[StringComparison]::OrdinalIgnoreCase)){throw 'Target/controller profiles overlap or environment profile differs.'}
 foreach($field in @('localAppData','localLow','environmentLocalAppData','environmentAppData','environmentTemp','environmentTmp')){
  if(-not $paths[$field].StartsWith($profile+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Canonical target environment path escapes target profile.'}
 }
 if(-not $paths.environmentLocalAppData.Equals($paths.localAppData,[StringComparison]::OrdinalIgnoreCase)){throw 'LocalAppData environment/known-folder mismatch.'}
 return [pscustomobject]$paths
}
function Assert-AzuriteTargetIdentity($Identity,[string]$ExpectedSid,[string]$ControllerSid,[string]$ControllerProfile) {
 # Execute this filesystem proof under the target identity, before any AA child.
 $paths=Get-AzuriteTargetIdentityPaths $Identity $ExpectedSid $ControllerSid $ControllerProfile
 foreach($path in @($paths.PSObject.Properties.Value|Select-Object -Unique)){
  $cursor=[IO.Path]::GetPathRoot($path)
  foreach($part in @('')+@($path.Substring($cursor.Length).Split('\'))){
   if($part){$cursor=Join-Path $cursor $part}
   $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
   if(-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Target profile path has a non-directory or reparse ancestor.'}
  }
 }
 return [pscustomobject]@{schema=1;identitySid=$ExpectedSid;filesystemVerified=$true;method='canonical-local-directories-no-reparse-v1';checkedUtc=[datetime]::UtcNow.ToString('o');paths=$paths}
}
function Assert-AzuriteTargetIdentityRecord($Identity,$Proof,[string]$ExpectedSid,[string]$ControllerSid,[string]$ControllerProfile) {
 # Controller may lack target-profile access. Verify the correlated bootstrap proof,
 # not the target filesystem through a different Windows token.
 $paths=Get-AzuriteTargetIdentityPaths $Identity $ExpectedSid $ControllerSid $ControllerProfile
 if($Proof.schema -ne 1 -or $Proof.identitySid -cne $ExpectedSid -or $Proof.filesystemVerified -ne $true -or $Proof.method -cne 'canonical-local-directories-no-reparse-v1' -or @($Proof.paths.PSObject.Properties).Count -ne @($paths.PSObject.Properties).Count){throw 'Target filesystem proof missing or invalid.'}
 foreach($field in $paths.PSObject.Properties){if($Proof.paths.($field.Name) -cne $field.Value){throw 'Target filesystem proof paths differ from identity record.'}}
}
function Get-AzuriteProcessStatus($Run,[ValidateSet('Bootstrap','Native','Helper compiler')][string]$Stage='Native') {
 $hasError=-not [string]::IsNullOrEmpty([string]$Run.Error)
 $errorText=[string]$Run.Error
 $errorText=[regex]::Replace($errorText,'[\x00-\x1f\x7f]+',' ')
 $errorText=[regex]::Replace($errorText,'(?i)\b(password|passwd|pwd|encryptedPassword|credential)\s*[:=]\s*("[^"]*"|\S+)','$1=[redacted]')
 if($errorText.Length -gt 1024){$errorText=$errorText.Substring(0,1024)+' [truncated]'}
 if($hasError -and [string]::IsNullOrWhiteSpace($errorText)){$errorText='[nonempty error with no printable detail]'}
 $failed=$Run.TimedOut -or $Run.WindowViolation -or $Run.OutputLimitExceeded -or $Run.ExitCode -ne 0 -or $hasError
 $reason=if($failed){$Stage+' exit/deadline/window/log contract failed.'}else{''}
 if($errorText){$reason+=' Native startup/monitor error: '+$errorText}
 return [pscustomobject]@{failed=[bool]$failed;error=$errorText;reason=$reason;exitCode=$Run.ExitCode}
}
function Get-AzuriteOuterProcessStatus($Outer) {
 return Get-AzuriteProcessStatus $Outer Bootstrap
}
function Get-AzuriteNativeProcessStatus($Native) {
 return Get-AzuriteProcessStatus $Native Native
}
function Get-AzuriteNativeResultStatus($Result) {
 # Independently apply the same predicate to the bootstrap's serialized native result.
 return Get-AzuriteNativeProcessStatus ([pscustomobject]@{ExitCode=$Result.aaExitCode;TimedOut=$Result.timedOut;WindowViolation=$Result.windowViolation;OutputLimitExceeded=$Result.outputLimitExceeded;Error=$Result.processError})
}
function Assert-AzuriteCleanupConfirmed($Cleanup) {
 if($Cleanup.CleanupVerified -ne $true -or $Cleanup.DirectProcessExited -ne $true -or $Cleanup.JobDrained -ne $true -or $Cleanup.RemainingOwnedProcesses -ne 0 -or ($Cleanup.Pid -gt 0 -and $Cleanup.JobObserved -ne $true) -or ($Cleanup.Pid -eq 0 -and $Cleanup.NoProcessCreated -ne $true)){throw 'Owned cleanup is unverified; mutable evidence must remain untouched and the run stays quarantined.'}
}
function Assert-AzuriteCleanupGateClear([string]$LabRoot) {
 if(Test-Path -LiteralPath (Join-Path $LabRoot 'secrets\restricted-cli.pending.json')){throw 'Restricted CLI quarantine/pending marker exists; explicit reviewed recovery is required before another plan or native start.'}
}
function Start-AzuriteCleanupGate([string]$LabRoot,[string]$RunId,[string]$RunDirectory,[string]$ControllerSid) {
 Assert-AzuriteCleanupGateClear $LabRoot
 Assert-AzuritePrivateAcl -Path (Join-Path $LabRoot 'secrets') -ControllerSid $ControllerSid
 $path=Join-Path $LabRoot 'secrets\restricted-cli.pending.json'
 # Private parent DACL protects the marker even before its individual ACL is sealed.
 Write-AzuriteFlushedJson $path @{schema=1;runId=$RunId;runDirectory=$RunDirectory;controllerSid=$ControllerSid;state='cleanup-pending';createdUtc=[datetime]::UtcNow.ToString('o');recovery='Only explicit reviewed recovery may clear an unverified run.'}
 Set-AzuriteLabAcl -LabRoot $LabRoot -Path $path -ControllerSid $ControllerSid
 Assert-AzuritePrivateAcl -Path $path -ControllerSid $ControllerSid
 return $path
}
function Complete-AzuriteCleanupGate([string]$LabRoot,[string]$RunId,[string]$ControllerSid,$Cleanup) {
 Assert-AzuriteCleanupConfirmed $Cleanup
 if($Cleanup.RunId -cne $RunId){throw 'Owned cleanup proof belongs to a different run.'}
 $path=Join-Path $LabRoot 'secrets\restricted-cli.pending.json'
 Assert-AzuritePrivateAcl -Path $path -ControllerSid $ControllerSid
 $record=Read-AzuriteBoundedJson $path 16384
 if($record.schema -ne 1 -or $record.runId -cne $RunId -or $record.controllerSid -cne $ControllerSid -or $record.state -cne 'cleanup-pending'){throw 'Cleanup quarantine marker does not match this run.'}
 Remove-Item -LiteralPath $path -ErrorAction Stop
}
function Set-AzuriteRequestAcl([string]$LabRoot,[string]$Path,[string]$ControllerSid,[string]$TargetSid) {
 # Parent directory cannot delete/replace protected arguments.json. Only new files are writable.
 Set-AzuriteLabAcl -LabRoot $LabRoot -Path $Path -ControllerSid $ControllerSid -TestSid $TargetSid
 & icacls.exe $Path '/remove:g' ('*'+$TargetSid) | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Could not remove inherited-shape target grants from request root.'}
 & icacls.exe $Path '/grant:r' ('*'+$TargetSid+':(RX,WD)') | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Could not set request directory create-file rights.'}
 & icacls.exe $Path '/grant' '*S-1-3-0:(OI)(IO)(M)' | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Could not set request output-file inheritance.'}
 Assert-AzuriteRestrictedAcl -Path $Path -ControllerSid $ControllerSid -TargetSid $TargetSid -Role Request
}
function Assert-AzuriteRestrictedAcl([string]$Path,[string]$ControllerSid,[string]$TargetSid,[ValidateSet('Read','Request','Modify')][string]$Role='Read') {
 $acl=Get-Acl -LiteralPath $Path
 $trusted=@($ControllerSid,'S-1-5-18','S-1-5-32-544')
 if(-not $acl.AreAccessRulesProtected -or $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw 'Prepared path ownership/inheritance is unsafe.'}
 $targetCount=0;$creatorCount=0;$seen=@{}
 foreach($entry in $acl.Access){
  $sid=$entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
  if($entry.AccessControlType -ne 'Allow' -or $entry.IsInherited){throw 'Unexpected prepared ACL entry.'}
  $rights=[int]$entry.FileSystemRights -band (-bnot [int][Security.AccessControl.FileSystemRights]::Synchronize)
  if($sid -in $trusted){if(($entry.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl){throw 'Trusted ACL role lacks control.'};$seen[$sid]=$true;continue}
  if($sid -ceq 'S-1-3-0' -and $Role -eq 'Request'){
   $creatorCount++
   if($rights -ne [int][Security.AccessControl.FileSystemRights]::Modify -or $entry.InheritanceFlags -ne 'ObjectInherit' -or $entry.PropagationFlags -ne 'InheritOnly'){throw 'Unsafe creator-owned response-file ACL.'}
   continue
  }
  if($sid -cne $TargetSid){throw 'Prepared ACL grants unexpected identity.'}
  $targetCount++
  if($Role -eq 'Request'){
   if($rights -ne ([int][Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [int][Security.AccessControl.FileSystemRights]::CreateFiles) -or $entry.InheritanceFlags -ne 'None' -or $entry.PropagationFlags -ne 'None'){throw 'Unsafe request directory ACL.'}
  } else {
   $expected=if($Role -eq 'Modify'){[Security.AccessControl.FileSystemRights]::Modify}else{[Security.AccessControl.FileSystemRights]::ReadAndExecute}
   if($rights -ne [int]$expected -or $entry.PropagationFlags -ne 'None'){throw 'Unsafe prepared file rights.'}
  }
 }
 foreach($sid in $trusted){if(-not $seen.ContainsKey($sid)){throw 'Prepared ACL lacks trusted role.'}}
 if($targetCount -ne 1 -or ($Role -eq 'Request' -and $creatorCount -ne 1)){throw 'Prepared ACL target/creator roles missing.'}
}
function Limit-AzuriteRetainedArtifacts([string]$Directory,[long]$MaxFileBytes=4194304,[long]$MaxTotalBytes=16777216,[string[]]$ExcludeFile=@()) {
 # Only after the owned bootstrap has stopped. Never follow links or touch prepared inputs.
 if(((Get-Item -LiteralPath $Directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Cannot retain linked output directory.'}
 $total=0L
 foreach($file in @(Get-ChildItem -LiteralPath $Directory -Force -File | Sort-Object Name)){
  if($file.Name -in $ExcludeFile){continue}
  if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Cannot retain linked child output.'}
  $keep=[Math]::Max(0,[Math]::Min($MaxFileBytes,$MaxTotalBytes-$total))
  if($file.Length -gt $keep){
   $original=$file.Length;$s=[IO.FileStream]::new($file.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::None)
   try{$s.SetLength($keep);$s.Flush($true)}finally{$s.Dispose()}
   [pscustomobject]@{path=$file.FullName;originalBytes=$original;retainedBytes=$keep;reason='failed output exceeded retention bound'}
  }
  $total += [Math]::Min($file.Length,$keep)
 }
}
function Move-AzuritePreviousLoaderLog([string]$SourcePath,[string]$DestinationPath) {
 if(-not(Test-Path -LiteralPath $SourcePath)){return [pscustomobject]@{Present=$false;Path=$DestinationPath}}
 $item=Get-Item -LiteralPath $SourcePath -Force
 if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or (Test-Path -LiteralPath $DestinationPath)){throw 'Unsafe previous loader log path.'}
 $hash=Get-AzuriteSha256 $SourcePath;$length=$item.Length
 [IO.File]::Move($SourcePath,$DestinationPath)
 return [pscustomobject]@{Present=$true;Path=$DestinationPath;OriginalBytes=$length;OriginalSha256=$hash;LastWriteUtc=$item.LastWriteTimeUtc.ToString('o')}
}
function Copy-AzuriteFreshLoaderLog([string]$SourcePath,[string]$DestinationPath,[datetime]$StartedUtc) {
 if(-not(Test-Path -LiteralPath $SourcePath)){return [pscustomobject]@{Present=$false;Fresh=$false;Complete=$false;Path=$DestinationPath;Sha256=$null}}
 $item=Get-Item -LiteralPath $SourcePath -Force
 if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or (Test-Path -LiteralPath $DestinationPath)){throw 'Unsafe current loader log path.'}
 $length=$item.Length;$writeTime=$item.LastWriteTimeUtc;$fresh=$item.CreationTimeUtc -ge $StartedUtc -and $writeTime -ge $StartedUtc
 $source=[IO.FileStream]::new($SourcePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try {
  $dest=[IO.FileStream]::new($DestinationPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
  try{$buffer=New-Object byte[] 65536;$remaining=4194304L;while($remaining -gt 0){$read=$source.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining));if($read -eq 0){break};$dest.Write($buffer,0,$read);$remaining-=$read};$dest.Flush($true)}finally{$dest.Dispose()}
 } finally {$source.Dispose()}
 $after=Get-Item -LiteralPath $SourcePath
 return [pscustomobject]@{Present=$true;Fresh=$fresh;Complete=($length -le 4194304 -and $after.Length -eq $length -and $after.LastWriteTimeUtc -eq $writeTime);Path=$DestinationPath;SourcePath=$SourcePath;OriginalBytes=$length;RetainedBytes=(Get-Item $DestinationPath).Length;Sha256=(Get-AzuriteSha256 $DestinationPath);LastWriteUtc=$writeTime.ToString('o')}
}
function Get-AzuritePinDrift([string]$Install,$Contract) {
 if(((Get-Item -LiteralPath $Install -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or @(Get-ChildItem -LiteralPath $Install -Force -Recurse -Attributes ReparsePoint).Count){return [pscustomobject]@{path='<install reparse tree>';expectedSha256=$null;actualSha256=$null;reason='linked input tree; hashes deliberately not read'}}
 foreach($pin in $Contract.files){
  $actual=$null;$reason='changed'
  try{$path=Join-Path $Install $pin.path;$item=Get-Item -LiteralPath $path -Force -ErrorAction Stop;if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Unsafe input'};$actual=Get-AzuriteSha256 $path}catch{$reason='missing/unreadable/linked'}
  if($actual -cne $pin.sha256.ToUpperInvariant()){[pscustomobject]@{path=$pin.path;expectedSha256=$pin.sha256;actualSha256=$actual;reason=$reason}}
 }
}
Export-ModuleMember -Function Get-AzuriteSha256,Write-AzuriteFlushedJson,Read-AzuriteBoundedJson,Read-AzuriteStartupContract,Assert-AzuritePinnedInstall,Assert-AzuriteEvidence,Read-AzuriteCliResponse,New-AzuriteProcessInfo,Set-AzuriteCliChildEnvironment,Initialize-AzuriteRestrictedNative,New-AzuriteNetFrameworkHelper,Assert-AzuriteTargetIdentity,Assert-AzuriteTargetIdentityRecord,Get-AzuriteProcessStatus,Get-AzuriteOuterProcessStatus,Get-AzuriteNativeProcessStatus,Get-AzuriteNativeResultStatus,Assert-AzuriteCleanupConfirmed,Assert-AzuriteCleanupGateClear,Start-AzuriteCleanupGate,Complete-AzuriteCleanupGate,Set-AzuriteRequestAcl,Assert-AzuriteRestrictedAcl,Limit-AzuriteRetainedArtifacts,Move-AzuritePreviousLoaderLog,Copy-AzuriteFreshLoaderLog,Get-AzuritePinDrift
