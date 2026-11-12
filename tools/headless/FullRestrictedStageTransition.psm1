Set-StrictMode -Version Latest
# Import is inert. Only the explicit Apply entry constructs the physical backend.
# Tests call the private coordinator in module scope. No public backend/target override exists.
$script:TransitionRoot='F:\AzuriteLabs\Editor-20261108-normal'
$script:TransitionVolume='\\?\Volume{a7c77c39-d000-4faf-bbf8-cc8ca0c743cb}\'
$script:TransitionOwner='S-1-5-21-222280291-4039000738-1914192245-1001'
$script:TransitionRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
function Get-TransitionHash([byte[]]$Bytes){$s=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($s.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$s.Dispose()}}
function Get-TransitionTextHash([string]$Text){Get-TransitionHash ([Text.UTF8Encoding]::new($false).GetBytes($Text))}
function ConvertTo-TransitionJson($Value){ConvertTo-Json -InputObject $Value -Depth 90 -Compress}
function Assert-Transition($Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Invoke-TransitionOp($Backend,$State,[string]$Action,[hashtable]$Arguments=@{}){if(-not $Arguments.ContainsKey('path')){$Arguments.path=$null};if(-not $Arguments.ContainsKey('name')){$Arguments.name=$null};& $Backend $Action $Arguments $State}
function Assert-TransitionObject($Object,$Spec,[string]$Path,[bool]$Directory){
 Assert-Transition ($null -ne $Object) "Missing declared object: $Path"
 Assert-Transition ($Object.path -ceq $Path -and $Object.finalPath -ceq $Path -and $Object.directory -eq $Directory -and -not $Object.reparse) "Canonical identity/type/reparse mismatch: $Path"
 Assert-Transition ($Object.owner -ceq $Spec.OwnerSid -and $Object.volume -ceq $Spec.Volume -and ($Directory -or $Object.links -eq 1)) "Owner/volume/link mismatch: $Path"
 Assert-Transition ($Object.identity -cmatch '^[a-f0-9]{32}$') "Unusable object identity: $Path"
}
function Assert-TransitionSame($Before,$After,[bool]$Content=$true){
 foreach($key in @('path','identity','volume','owner','directory','links','reparse','finalPath','sddl')){Assert-Transition ($Before.$key -ceq $After.$key) "Original object drift before mutation ($key): $($Before.path)"}
 if($Content -and -not $Before.directory){Assert-Transition ($Before.bytes -eq $After.bytes -and $Before.sha256 -ceq $After.sha256) "Original bytes changed: $($Before.path)"}
}
function Remove-TransitionCacheDeny([string]$Sddl,[string]$Owner){
 $sd=[Security.AccessControl.RawSecurityDescriptor]::new($Sddl);$found=-1
 for($i=0;$i -lt $sd.DiscretionaryAcl.Count;$i++){$a=$sd.DiscretionaryAcl[$i];if($a -is [Security.AccessControl.CommonAce] -and $a.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessDenied -and $a.SecurityIdentifier.Value -ceq $Owner -and $a.AccessMask -eq 6 -and [int]$a.AceFlags -eq 0){Assert-Transition ($found -lt 0) 'Duplicate cache creation deny';$found=$i}}
 Assert-Transition ($found -ge 0) 'Exact cache parent creation deny missing';$sd.DiscretionaryAcl.RemoveAce($found)
 $sd.GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access')
}
function Invoke-TransitionMutation($Backend,$State,$Spec,[string]$Action,[hashtable]$Arguments,$Before){
 # Every mutation is intent-journaled and verified against its own original/absence.
 $current=Invoke-TransitionOp $Backend $State Observe @{path=$Arguments.path;captureBytes=($Action -ceq 'WriteBytes')}
 if($null -eq $Before){Assert-Transition ($null -eq $current) "Refusing an existing new target: $($Arguments.path)"}else{Assert-TransitionSame $Before $current}
 Invoke-TransitionOp $Backend $State Journal @{name='mutation-intent';value=[pscustomobject]@{action=$Action;path=$Arguments.path;before=$current;expectedSddl=$Arguments.sddl;expectedBytes=$(if($Arguments.ContainsKey('bytes')){$Arguments.bytes.Length}else{$null});expectedSha256=$(if($Arguments.ContainsKey('bytes')){Get-TransitionHash $Arguments.bytes}else{$null})}}
 if($Before){$Arguments.expectedIdentity=$Before.identity;$Arguments.expectedSddl=$Before.sddl;$Arguments.expectedSha256=$Before.sha256}else{$Arguments.expectedIdentity=$null;$Arguments.expectedSddl=$null;$Arguments.expectedSha256=$null}
 Invoke-TransitionOp $Backend $State $Action $Arguments
 $after=Invoke-TransitionOp $Backend $State Observe @{path=$Arguments.path;captureBytes=$false}
 $directory=if($Action -ceq 'CreateDirectory'){$true}elseif($Before){[bool]$Before.directory}else{$false}
 Assert-TransitionObject $after $Spec $Arguments.path $directory
 if($Before){Assert-Transition ($after.identity -ceq $Before.identity) "Identity changed during mutation: $($Arguments.path)"}
 if($Action -cin @('SetDescriptor','CreateDirectory','CreateFile')){Assert-Transition ($after.sddl -ceq $Arguments.sddl) "Planned descriptor readback differs: $($Arguments.path)"}
 if($Action -cin @('WriteBytes','CreateFile')){Assert-Transition ($after.bytes -eq $Arguments.bytes.Length -and $after.sha256 -ceq (Get-TransitionHash $Arguments.bytes)) "Content readback differs: $($Arguments.path)"}
 Invoke-TransitionOp $Backend $State Journal @{name='mutation-readback';value=[pscustomobject]@{action=$Action;object=$after}}
 return $after
}
function Invoke-TransitionCoordinator($Spec,$Backend,$State){
 $lease=$null;$errorText=$null;$success=$false;$quarantined=$false;$sealRestored=$false;$cacheOriginal=$null;$unsealed=$null
 $snapshots=[Collections.Generic.List[object]]::new();$created=[Collections.Generic.List[object]]::new();$current=@{};$grants=[Collections.Generic.List[object]]::new();$observations=$null
 try{
  $lease=Invoke-TransitionOp $Backend $State Acquire
  Assert-Transition ($lease.ownerSid -ceq $Spec.OwnerSid -and $lease.logonSid -ceq $Spec.LogonSid -and $lease.volume -ceq $Spec.Volume) 'Lease current-token owner/logon/volume binding differs'
  $identities=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach($entry in $Spec.Existing){$o=Invoke-TransitionOp $Backend $State Observe @{path=$entry.path;captureBytes=$entry.captureBytes};Assert-TransitionObject $o $Spec $entry.path $entry.directory;Assert-Transition ($identities.Add($o.identity)) 'Existing object identity aliases another declared object';Assert-Transition ($o.sddl -ceq $entry.baselineSddl) "Baseline descriptor changed: $($entry.path)";if($entry.PSObject.Properties['identity']){Assert-Transition ($o.identity -ceq $entry.identity) "Preapproval identity changed: $($entry.path)"};if(-not $entry.directory){Assert-Transition ($o.sha256 -ceq $entry.sha256 -and $o.bytes -eq $entry.bytes) "Pinned original input changed: $($entry.path)"};$snapshots.Add($o);$current[$entry.path]=$o}
  $grantSet=@{};foreach($g in $Spec.Grants){Assert-Transition (-not $grantSet.ContainsKey($g.path)) 'Duplicate planned ACL target';$grantSet[$g.path]=$g}
  foreach($e in $Spec.Existing){if($Spec.ImmutablePaths -ccontains $e.path -and -not $grantSet.ContainsKey($e.path)){continue};Assert-Transition ($grantSet.ContainsKey($e.path)) "Missing planned original descriptor coverage: $($e.path)"}
  $newPaths=@($Spec.Directories)+@($Spec.Files|ForEach-Object path)+@($Spec.CacheFile)
  Assert-Transition (@($newPaths|Select-Object -Unique).Count -eq $newPaths.Count) 'Duplicate finite creation target'
  foreach($path in $newPaths){Assert-Transition ($null -eq (Invoke-TransitionOp $Backend $State Observe @{path=$path;captureBytes=$false})) "Unexpected existing new target: $path"}
  Invoke-TransitionOp $Backend $State VerifyInventories @{phase='initial';created=@()}
  $environment=Invoke-TransitionOp $Backend $State ValidateEnvironment
  Assert-Transition ($environment.aaCount -eq 0 -and $environment.markerRuntimeAllowed -eq $false) 'AA or historical marker gate failed'
  Invoke-TransitionOp $Backend $State Persist @{name='before-images-raw';value=[pscustomobject]@{schema=1;runId=$Spec.RunId;descriptorSections='Owner,Group,Access; SACL not read';objects=$snapshots.ToArray();absent=$newPaths}}
  foreach($path in $Spec.Directories){$o=Invoke-TransitionMutation $Backend $State $Spec CreateDirectory @{path=$path;sddl=$Spec.PrivateDirectorySddl} $null;$created.Add($o);$current[$path]=$o;if($Spec.PSObject.Properties['PendingPath'] -and $path -ceq ($Spec.Root+'\runs')){$file=@($Spec.Files|Where-Object path -CEQ $Spec.PendingPath)[0];$o=Invoke-TransitionMutation $Backend $State $Spec CreateFile @{path=$file.path;bytes=$file.bytes;sddl=$Spec.PrivateFileSddl} $null;$created.Add($o);$current[$file.path]=$o}}
  # A controller-private immutable history copy must be durably read back before
  # the first original payload or descriptor mutation. C journal survives early failures.
  Invoke-TransitionOp $Backend $State Persist @{name='before-images-raw-history';value=[pscustomobject]@{schema=1;runId=$Spec.RunId;descriptorSections='Owner,Group,Access; SACL not read';objects=$snapshots.ToArray();absent=$newPaths}}
  foreach($file in $Spec.Files){if($file.PSObject.Properties['early'] -and $file.early -and -not $current.ContainsKey($file.path)){$bytes=Get-TransitionEvidenceBytes $file $Spec $snapshots.ToArray() $current;$file.bytes=$bytes;$o=Invoke-TransitionMutation $Backend $State $Spec CreateFile @{path=$file.path;bytes=$bytes;sddl=$Spec.PrivateFileSddl} $null;$created.Add($o);$current[$file.path]=$o}}
  $cacheOriginal=$current[$Spec.CacheParent];$unsealed=Remove-TransitionCacheDeny $cacheOriginal.sddl $Spec.OwnerSid
  $current[$Spec.CacheParent]=Invoke-TransitionMutation $Backend $State $Spec SetDescriptor @{path=$Spec.CacheParent;sddl=$unsealed} $cacheOriginal
  try{$o=Invoke-TransitionMutation $Backend $State $Spec CreateFile @{path=$Spec.CacheFile;bytes=$Spec.Seed;sddl=$Spec.PrivateFileSddl} $null;$created.Add($o);$current[$Spec.CacheFile]=$o}
  finally{
   $now=Invoke-TransitionOp $Backend $State Observe @{path=$Spec.CacheParent;captureBytes=$false}
   Assert-Transition ($now.identity -ceq $cacheOriginal.identity -and $now.sddl -ceq $unsealed) 'Cache parent changed; exact owned reseal is unsafe'
   $current[$Spec.CacheParent]=Invoke-TransitionMutation $Backend $State $Spec SetDescriptor @{path=$Spec.CacheParent;sddl=$cacheOriginal.sddl} $now
   $sealRestored=$true
  }
  $restored=$current[$Spec.CacheParent]
  $current[$Spec.Loader]=Invoke-TransitionMutation $Backend $State $Spec WriteBytes @{path=$Spec.Loader;bytes=[byte[]]@();sddl=$current[$Spec.Loader].sddl} $current[$Spec.Loader]
  foreach($file in $Spec.Files){if($current.ContainsKey($file.path)){continue};if($file.PSObject.Properties['kind'] -and $file.kind -cne 'static'){$file.bytes=Get-TransitionEvidenceBytes $file $Spec $snapshots.ToArray() $current};$o=Invoke-TransitionMutation $Backend $State $Spec CreateFile @{path=$file.path;bytes=$file.bytes;sddl=$Spec.PrivateFileSddl} $null;$created.Add($o);$current[$file.path]=$o}
  foreach($grant in $Spec.Grants){Assert-Transition ($current.ContainsKey($grant.path)) "Grant has no before/creation identity: $($grant.path)";$o=Invoke-TransitionMutation $Backend $State $Spec SetDescriptor @{path=$grant.path;sddl=$grant.sddl} $current[$grant.path];$current[$grant.path]=$o;$grants.Add($o)}
  foreach($entry in $Spec.Existing){$now=Invoke-TransitionOp $Backend $State Observe @{path=$entry.path;captureBytes=$false};Assert-TransitionSame $current[$entry.path] $now;if($entry.path -cne $Spec.Loader -and -not $entry.directory){Assert-Transition ($now.sha256 -ceq $entry.sha256 -and $now.bytes -eq $entry.bytes) 'Unexpected original payload delta'}}
  foreach($file in @($Spec.Files)+@([pscustomobject]@{path=$Spec.CacheFile;bytes=$Spec.Seed})){$now=Invoke-TransitionOp $Backend $State Observe @{path=$file.path;captureBytes=$false};Assert-TransitionSame $current[$file.path] $now;Assert-Transition ($now.sha256 -ceq (Get-TransitionHash $file.bytes)) 'Final created content differs'}
  foreach($path in $Spec.Directories){$now=Invoke-TransitionOp $Backend $State Observe @{path=$path;captureBytes=$false};Assert-TransitionSame $current[$path] $now}
  Invoke-TransitionOp $Backend $State VerifyInventories @{phase='prepared';created=$created.ToArray()}
  $finalEnvironment=Invoke-TransitionOp $Backend $State ValidateEnvironment
  Assert-Transition ((ConvertTo-TransitionJson $environment) -ceq (ConvertTo-TransitionJson $finalEnvironment)) 'Final environment/warm binding drift'
  if($Spec.PSObject.Properties['PendingPath']){$pending=Get-TransitionJsonBytes (@($Spec.Files|Where-Object path -CEQ $Spec.PendingPath)[0].bytes);$pending.state='prepared-awaiting-runtime-lease';$bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $pending));$current[$Spec.PendingPath]=Invoke-TransitionMutation $Backend $State $Spec WriteBytes @{path=$Spec.PendingPath;bytes=$bytes;sddl=$current[$Spec.PendingPath].sddl} $current[$Spec.PendingPath]}
  $observations=[pscustomobject]@{schema=1;runId=$Spec.RunId;lease=$lease;leaseStateAtObservation='held-exclusive';snapshots=$snapshots.ToArray();created=$created.ToArray();grantReadbacks=$grants.ToArray();cacheParentBefore=$cacheOriginal;cacheParentRestored=$restored;cacheLeaf=$current[$Spec.CacheFile];loader=$current[$Spec.Loader];arguments=$current[$Spec.ProtectedArguments];environment=$finalEnvironment;objects=@($current.Values)}
  Invoke-TransitionOp $Backend $State Persist @{name='observations';value=$observations}
  $success=$true
 }catch{
  $errorText=$_.Exception.Message
  # Only the one recorded unseal may be recovered. There is no whole-tree undo.
  if($cacheOriginal -and -not $sealRestored){try{$now=Invoke-TransitionOp $Backend $State Observe @{path=$Spec.CacheParent;captureBytes=$false};if($now.identity -ceq $cacheOriginal.identity -and $now.sddl -ceq $unsealed){$null=Invoke-TransitionMutation $Backend $State $Spec SetDescriptor @{path=$Spec.CacheParent;sddl=$cacheOriginal.sddl} $now;$sealRestored=$true}elseif($now.identity -ceq $cacheOriginal.identity -and $now.sddl -ceq $cacheOriginal.sddl){$sealRestored=$true}}catch{$errorText+='; reseal: '+$_.Exception.Message}}
  try{Invoke-TransitionOp $Backend $State Quarantine @{value=[pscustomobject]@{runId=$Spec.RunId;error=$errorText;sealRestored=$sealRestored;created=$created.ToArray();stage='prelaunch-no-child-created'}};$quarantined=$true}catch{$errorText+='; quarantine persistence failed: '+$_.Exception.Message}
 }finally{
  $releaseState='not-acquired';if($lease){try{Invoke-TransitionOp $Backend $State Release @{value=$lease};$releaseState='released'}catch{$success=$false;$releaseState='release-failed';$errorText+='; lease release failed: '+$_.Exception.Message;try{Invoke-TransitionOp $Backend $State Quarantine @{value=[pscustomobject]@{runId=$Spec.RunId;error=$errorText;stage='lease-release-uncertain'}};$quarantined=$true}catch{$errorText+='; quarantine failed: '+$_.Exception.Message}}}
 }
 return [pscustomobject]@{Success=$success;Prepared=$success;ReadyForLaunch=$false;WillLaunch=$false;RuntimeAllowed=$false;LeaseState=$releaseState;Quarantined=$quarantined;SealRestored=$sealRestored;Error=$errorText;Observations=$observations}
}

# File/security only. This string is compiled in a separate C-only preparation
# step; Apply verifies and loads the frozen output, and never compiles a helper.
# No token creation, process creation, registry, Job, desktop or account API.
$script:TransitionNativeSource=@'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
namespace Azurite.Stage {
 public sealed class ObjectEvidence {
  public string path,identity,volume,owner,finalPath,sddl,ownerAccessSddl,rawDescriptor,sha256,content;
  public bool directory,reparse; public uint links; public long bytes;
 }
 public static class FileSecurity {
  [StructLayout(LayoutKind.Sequential)] struct FT {public uint Low,High;}
  [StructLayout(LayoutKind.Sequential)] struct FI {public uint Attr; public FT Creation,Access,Write; public uint Volume,SizeHigh,SizeLow,Links,IdHigh,IdLow;}
  [StructLayout(LayoutKind.Sequential)] struct SA {public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
  [StructLayout(LayoutKind.Sequential)] struct Disposition {[MarshalAs(UnmanagedType.U1)]public bool DeleteFile;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string p,uint access,uint share,IntPtr sa,uint create,uint flags,IntPtr template);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateFileW")] static extern SafeFileHandle CreateFileSecure(string p,uint access,uint share,ref SA sa,uint create,uint flags,IntPtr template);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateDirectoryW(string p,ref SA sa);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h,out FI info);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle h,int type,ref Disposition info,uint length);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandleW(SafeFileHandle h,StringBuilder b,uint size,uint flags);
  [DllImport("advapi32.dll")] static extern uint GetSecurityInfo(SafeFileHandle h,uint type,uint info,out IntPtr o,out IntPtr g,out IntPtr d,out IntPtr s,out IntPtr sd);
  [DllImport("advapi32.dll")] static extern uint GetSecurityDescriptorLength(IntPtr sd);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetKernelObjectSecurity(SafeFileHandle h,uint info,byte[] sd);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
  static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
  static string Hash(byte[] b){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
  static string Id(FI i){return i.Volume.ToString("x8")+"00000000"+i.IdHigh.ToString("x8")+i.IdLow.ToString("x8");}
  static SafeFileHandle Open(string p,uint rights){var h=CreateFileW(p,rights,1,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(h.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),p);return h;}
  static byte[] Sd(SafeFileHandle h){IntPtr o,g,d,s,p;uint e=GetSecurityInfo(h,1,7,out o,out g,out d,out s,out p);if(e!=0)throw new Win32Exception((int)e);try{var b=new byte[GetSecurityDescriptorLength(p)];Marshal.Copy(p,b,0,b.Length);return b;}finally{LocalFree(p);}}
  static byte[] Binary(string s){var sd=new RawSecurityDescriptor(s);var b=new byte[sd.BinaryLength];sd.GetBinaryForm(b,0);return b;}
  static string Sddl(byte[] b){return new RawSecurityDescriptor(b,0).GetSddlForm(AccessControlSections.Owner|AccessControlSections.Group|AccessControlSections.Access);}
  static FI Info(SafeFileHandle h){FI i;if(!GetFileInformationByHandle(h,out i))throw new Win32Exception(Marshal.GetLastWin32Error());Require((i.Attr&0x400)==0,"Reparse object refused");Require((i.Attr&16)!=0||i.Links==1,"Hardlinked file refused");return i;}
  static string Final(SafeFileHandle h){var b=new StringBuilder(32768);uint n=GetFinalPathNameByHandleW(h,b,(uint)b.Capacity,1);if(n==0||n>=b.Capacity)throw new Win32Exception(Marshal.GetLastWin32Error());return b.ToString();}
  public static SafeFileHandle LockDirectory(string p){var h=Open(p,0x20081);try{Require((Info(h).Attr&16)!=0,"Directory lock type differs");return h;}catch{h.Dispose();throw;}}
  public static ObjectEvidence Read(string p,bool capture){
   using(var h=Open(p,0x80020000)){FI before=Info(h);byte[] descriptor=Sd(h);var sd=new RawSecurityDescriptor(descriptor,0);string final=Final(h);int end=final.IndexOf("}\\",StringComparison.Ordinal);Require(end>0,"No canonical GUID volume");string volume=final.Substring(0,end+2);string expected=volume+p.Substring(3);Require(final==expected,"Canonical ancestry/path spelling differs");
    var r=new ObjectEvidence{path=p,identity=Id(before),volume=volume,owner=sd.Owner.Value,finalPath=p,directory=(before.Attr&16)!=0,reparse=false,links=before.Links,bytes=((long)before.SizeHigh<<32)|before.SizeLow,sddl=Sddl(descriptor),ownerAccessSddl=sd.GetSddlForm(AccessControlSections.Owner|AccessControlSections.Access),rawDescriptor=Convert.ToBase64String(descriptor)};
    if(!r.directory){using(var fs=new FileStream(h,FileAccess.Read,65536,false)){if(capture){Require(r.bytes<=4194304,"Before-image payload over 4 MiB");var bytes=new byte[(int)r.bytes];int n=0,k;while(n<bytes.Length&&(k=fs.Read(bytes,n,bytes.Length-n))>0)n+=k;Require(n==bytes.Length,"Short payload read");r.sha256=Hash(bytes);r.content=Convert.ToBase64String(bytes);}else{using(var hash=SHA256.Create())r.sha256=BitConverter.ToString(hash.ComputeHash(fs)).Replace("-","").ToLowerInvariant();}FI after=Info(h);Require(Id(after)==r.identity&&after.SizeHigh==before.SizeHigh&&after.SizeLow==before.SizeLow&&after.Write.High==before.Write.High&&after.Write.Low==before.Write.Low&&Sddl(Sd(h))==r.sddl,"Object changed during read");}}
    return r;
   }
  }
  public static void NewDirectory(string p,string sddl){byte[] b=Binary(sddl);var pin=GCHandle.Alloc(b,GCHandleType.Pinned);try{var sa=new SA{Length=Marshal.SizeOf(typeof(SA)),Descriptor=pin.AddrOfPinnedObject(),Inherit=false};if(!CreateDirectoryW(p,ref sa))throw new Win32Exception(Marshal.GetLastWin32Error());}finally{pin.Free();}}
  public static void NewFile(string p,byte[] data,string sddl){byte[] b=Binary(sddl);var pin=GCHandle.Alloc(b,GCHandleType.Pinned);try{var sa=new SA{Length=Marshal.SizeOf(typeof(SA)),Descriptor=pin.AddrOfPinnedObject(),Inherit=false};using(var h=CreateFileSecure(p,0xC0020000,0,ref sa,1,0x00200000,IntPtr.Zero)){if(h.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());Require(Info(h).Links==1,"New file alias");using(var fs=new FileStream(h,FileAccess.ReadWrite,65536,false)){fs.Write(data,0,data.Length);fs.Flush(true);fs.Position=0;using(var hash=SHA256.Create())Require(BitConverter.ToString(hash.ComputeHash(fs)).Replace("-","").ToLowerInvariant()==Hash(data),"Same-handle new content mismatch");Require(Sddl(Sd(h))==sddl,"Same-handle creation descriptor mismatch");}}}finally{pin.Free();}}
  public static void SetDescriptor(string p,string identity,string before,string after){using(var h=Open(p,0x60000)){Require(Id(Info(h))==identity&&Sddl(Sd(h))==before,"Before descriptor/identity drift");var old=new RawSecurityDescriptor(before);var next=new RawSecurityDescriptor(after);Require(old.Owner==next.Owner&&((old.Group==null&&next.Group==null)||(old.Group!=null&&next.Group!=null&&old.Group.Value==next.Group.Value)),"Owner/group delta forbidden");uint flags=4|(((next.ControlFlags&ControlFlags.DiscretionaryAclProtected)!=0)?0x80000000u:0x20000000u);if(!SetKernelObjectSecurity(h,flags,Binary(after)))throw new Win32Exception(Marshal.GetLastWin32Error());Require(Id(Info(h))==identity&&Sddl(Sd(h))==after,"Same-handle descriptor delta mismatch");}}
  public static void WriteExisting(string p,string identity,string before,string hash,byte[] data){using(var h=Open(p,0xC0020000)){Require(Id(Info(h))==identity&&Sddl(Sd(h))==before,"Write identity/descriptor drift");using(var fs=new FileStream(h,FileAccess.ReadWrite,65536,false)){using(var s=SHA256.Create())Require(BitConverter.ToString(s.ComputeHash(fs)).Replace("-","").ToLowerInvariant()==hash,"Write original hash drift");fs.Position=0;fs.SetLength(0);fs.Write(data,0,data.Length);fs.Flush(true);fs.Position=0;using(var s=SHA256.Create())Require(BitConverter.ToString(s.ComputeHash(fs)).Replace("-","").ToLowerInvariant()==Hash(data),"Write same-handle readback mismatch");Require(Id(Info(h))==identity&&Sddl(Sd(h))==before,"Write descriptor/identity changed");}}}
  // Runtime-only pending-marker cleanup. The preparation coordinator never calls
  // this method; the runtime must already prove drain and zero shared delta.
  public static void DeleteExisting(string p,string identity,string before,string hash){using(var h=Open(p,0x80030000)){Require(Id(Info(h))==identity&&Sddl(Sd(h))==before,"Delete identity/descriptor drift");Require((Info(h).Attr&16)==0,"Directory deletion forbidden");using(var fs=new FileStream(h,FileAccess.Read,65536,false)){using(var s=SHA256.Create())Require(BitConverter.ToString(s.ComputeHash(fs)).Replace("-","").ToLowerInvariant()==hash,"Delete original hash drift");var remove=new Disposition{DeleteFile=true};if(!SetFileInformationByHandle(h,4,ref remove,(uint)Marshal.SizeOf(typeof(Disposition))))throw new Win32Exception(Marshal.GetLastWin32Error());}}}
 }
}
'@
function New-AzuriteStageTransitionHelper {
 [CmdletBinding()]param([Parameter(Mandatory)][string]$OutputDirectory)
 $path=[IO.Path]::GetFullPath($OutputDirectory);Assert-Transition ($path -cmatch '^C:\\' -and -not [IO.Directory]::Exists($path) -and -not [IO.File]::Exists($path)) 'Helper output must be a fresh C: directory'
 [void][IO.Directory]::CreateDirectory($path)
 $source=Join-Path $path 'FullRestrictedStageFileSecurity.cs';$assembly=Join-Path $path 'FullRestrictedStageFileSecurity.dll'
 [IO.File]::WriteAllText($source,$script:TransitionNativeSource,[Text.UTF8Encoding]::new($false))
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';Assert-Transition ([IO.File]::Exists($compiler)) 'Framework64 v4 compiler missing'
 $log=& $compiler /nologo /target:library /platform:x64 /optimize+ (('/out:'+$assembly)) $source 2>&1;$code=$LASTEXITCODE
 [IO.File]::WriteAllText((Join-Path $path 'compile.log'),($log -join "`r`n"),[Text.UTF8Encoding]::new($false));Assert-Transition ($code -eq 0) 'File/security helper compile failed'
 $receipt=[pscustomobject]@{schema=1;scope='controller-file-security-helper-build';sourceSha256=(Get-TransitionHash ([IO.File]::ReadAllBytes($source)));moduleSha256=(Get-TransitionHash ([IO.File]::ReadAllBytes($PSCommandPath)));compilerSha256=(Get-TransitionHash ([IO.File]::ReadAllBytes($compiler)));assemblySha256=(Get-TransitionHash ([IO.File]::ReadAllBytes($assembly)));target='library';platform='x64';exitCode=$code;nativeOperationsExecuted=$false}
 [IO.File]::WriteAllText((Join-Path $path 'build.json'),(ConvertTo-TransitionJson $receipt),[Text.UTF8Encoding]::new($false));return $receipt
}
function Read-TransitionPinnedCFile([string]$Path,[string]$ExpectedSha256,[long]$Maximum=8388608){
 Assert-Transition ($ExpectedSha256 -cmatch '^[a-f0-9]{64}$') 'External expected digest missing or malformed'
 $full=[IO.Path]::GetFullPath($Path);Assert-Transition ($full -ceq $Path -and $full -cmatch '^C:\\' -and $full -notmatch '[:][^\\]' ) 'Source must be an exact absolute C: path'
 $cursor=$full;while($cursor){$item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop;Assert-Transition (-not($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'C source ancestry contains a reparse point';$cursor=Split-Path -Parent $cursor}
 $item=Get-Item -LiteralPath $full -Force;Assert-Transition (-not $item.PSIsContainer -and $item.Length -le $Maximum) 'Frozen source type/size differs'
 $stream=[IO.FileStream]::new($full,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{$memory=[IO.MemoryStream]::new();try{$stream.CopyTo($memory);$bytes=$memory.ToArray()}finally{$memory.Dispose()}}finally{$stream.Dispose()}
 Assert-Transition ((Get-TransitionHash $bytes) -ceq $ExpectedSha256) ('Frozen C source digest differs: '+[IO.Path]::GetFileName($Path));return ,$bytes
}
function Read-TransitionFrozen([string]$Relative,[string]$Sha){return Read-TransitionPinnedCFile ([IO.Path]::GetFullPath((Join-Path $script:TransitionRepo $Relative))) $Sha}
function Get-TransitionJsonBytes($Bytes){
 $text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes)
 # PS7.6 otherwise converts ISO JSON strings to DateTime while Windows PS5.1
 # preserves them. The approval/receipt protocol requires the original strings.
 if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){return ($text|ConvertFrom-Json -DateKind String -ErrorAction Stop)}
 return ($text|ConvertFrom-Json -ErrorAction Stop)
}
function Get-TransitionCanonicalSddl([string]$Sddl){[Security.AccessControl.RawSecurityDescriptor]::new($Sddl).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Group,Access')}
function Invoke-TransitionRuntimeHelper([string]$Name,[object[]]$Arguments){$m=Get-Module FullRestrictedCapabilitiesRuntime;Assert-Transition ($null -ne $m) 'Runtime pure policy module missing';& $m {param($n,$a)& $n @a} $Name $Arguments}
function Read-TransitionInputs([string]$ManifestPath,[string]$ExpectedManifestSha256,[string]$ControlPath,[string]$ExpectedControlSha256,[string]$GatePath,[string]$ExpectedGateSha256,[string]$ExpectedPlanSha256,[string]$ApprovalPath,[string]$ExpectedApprovalSha256,[bool]$RequireApproval){
 $manifest=Get-TransitionJsonBytes (Read-TransitionPinnedCFile $ManifestPath $ExpectedManifestSha256)
 Assert-Transition ($manifest.schema -eq 1 -and $manifest.scope -ceq 'controller-full-capabilities-preapproval-sources' -and $manifest.runId -cmatch '^[a-f0-9]{32}$' -and $manifest.runId -cne ('0'*32)) 'Source manifest scope/run identity differs'
 $required=@('facts.json','plan.json','expected-acl-manifest.json','identity-baseline.json','scope.json','before.json','FullRestrictedStageFileSecurity.dll','helper-build.json','RestrictedCliCore.psm1','RestrictedCliNative.cs','RestrictedCliFullRestricted.cs','RestrictedCliNative.dll','RestrictedCliBootstrap.ps1','CurrentAccountCli.psm1','HeadlessCore.psm1','FullRestrictedCapabilitiesRuntime.psm1','FullRestrictedCapabilitiesPlan.psm1','tooling-manifest.json')
 Assert-Transition (@($manifest.files).Count -eq $required.Count) 'Exact frozen source set differs'
 $source=@{};foreach($entry in $manifest.files){Assert-Transition ($entry.name -cin $required -and -not $source.ContainsKey($entry.name)) 'Unexpected/duplicate source name';$bytes=Read-TransitionPinnedCFile $entry.path $entry.sha256;Assert-Transition ($bytes.Length -eq $entry.bytes) 'Source length differs';$source[$entry.name]=[pscustomobject]@{entry=$entry;bytes=$bytes}}
 foreach($name in @('FullRestrictedCapabilitiesPlan.psm1','FullRestrictedCapabilitiesRuntime.psm1')){Assert-Transition ((Get-TransitionHash ([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name)))) -ceq $source[$name].entry.sha256) 'Executing policy module differs from approved controller source'}
 $controlBytes=Read-TransitionPinnedCFile $ControlPath $ExpectedControlSha256;$control=Get-TransitionJsonBytes $controlBytes
 foreach($name in @('schema','identityMode','runId','command','labRoot','planSha256','contractSha256','controllerSid','targetSid','controllerProfile','currentLocalLow','currentEnvironment','runtimeTimeoutSeconds','intentValidUntilUtc','argumentsSha256','sharedStateBeforeSha256','toolingManifestSha256','expectedAclManifestSha256','toolHashes','privateHistoryPaths')){Assert-Transition ($null -ne $control.PSObject.Properties[$name]) ('Missing control input: '+$name)}
 Assert-Transition (-not $control.PSObject.Properties['deadlineUtc'] -and ($control.runtimeTimeoutSeconds -is [int] -or $control.runtimeTimeoutSeconds -is [long]) -and $control.runtimeTimeoutSeconds -ge 10 -and $control.runtimeTimeoutSeconds -le 300 -and $control.intentValidUntilUtc -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') 'Control must separate bounded fresh runtime duration from preparation intent expiry'
 if($RequireApproval){Assert-Transition ([datetimeoffset]::UtcNow -lt [datetimeoffset]::Parse($control.intentValidUntilUtc)) 'Preparation intent is expired'}
 $gate=Get-TransitionJsonBytes (Read-TransitionPinnedCFile $GatePath $ExpectedGateSha256)
 $facts=Get-TransitionJsonBytes $source['facts.json'].bytes;$plan=Get-TransitionJsonBytes $source['plan.json'].bytes
 Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesPlan.psm1') -Force -DisableNameChecking
 Import-Module (Join-Path $PSScriptRoot 'FullRestrictedCapabilitiesRuntime.psm1') -Force -DisableNameChecking
 $contract=Read-AzuriteFullCapabilitiesContract;$cache=Get-TransitionJsonBytes (Read-TransitionFrozen 'artifacts/aa-fr-cache-prerequisites/cache-prerequisites-contract.json' '11476211f744e7009fe1bd21b7bc4b786d3f73c89a747f6ecab955e43754c5f4')
 $fresh=New-AzuriteFullCapabilitiesPlan -Contract $contract -CachePrerequisites $cache -RunId $manifest.runId -Facts $facts
 Assert-Transition ($fresh.PreparationReady -and $null -eq $facts.transition -and $fresh.PlanSha256 -ceq $ExpectedPlanSha256 -and (ConvertTo-TransitionJson $fresh) -ceq (ConvertTo-TransitionJson $plan)) ('Frozen pretransition plan differs: '+($fresh.Failures -join '; '))
 Assert-Transition ($plan.LabRoot -ceq $script:TransitionRoot -and $plan.Executable -ceq ($script:TransitionRoot+'\install\AzureArchive.exe') -and $facts.ownerSid -ceq $script:TransitionOwner -and $facts.volumeGuid -ceq $script:TransitionVolume) 'Fixed production boundary differs'
 Assert-Transition ($control.schema -eq 2 -and $control.runId -ceq $plan.RunId -and $control.labRoot -ceq $script:TransitionRoot -and $control.planSha256 -ceq $ExpectedPlanSha256 -and $control.contractSha256 -ceq $plan.ContractSha256 -and $control.controllerSid -ceq $script:TransitionOwner -and $control.targetSid -ceq $script:TransitionOwner -and $control.command -ceq 'capabilities' -and $control.identityMode -ceq 'CurrentAccountFullRestrictedCapabilities') 'Prepared control identity differs'
 Assert-Transition ($gate.scope -ceq 'preapproval-full-restricted-capabilities-intent' -and $gate.runId -ceq $plan.RunId -and $gate.controlSha256 -ceq $ExpectedControlSha256 -and $gate.transitionSourceManifestSha256 -ceq $ExpectedManifestSha256 -and $gate.transitionHelperSha256 -ceq $source['FullRestrictedStageFileSecurity.dll'].entry.sha256) 'Forward gate does not bind exact controller sources/helper/control'
 foreach($bad in @('gateSha256','reviewApprovalSha256','transitionReceiptSha256','admissionSha256')){Assert-Transition (-not $control.PSObject.Properties[$bad]) 'Control contains reverse approval/receipt dependency'}
 $tooling=Get-TransitionJsonBytes $source['tooling-manifest.json'].bytes;Invoke-TransitionRuntimeHelper 'Assert-AzuriteFullToolManifest' @($tooling,$control)
 $framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319';$compilerHash=Get-TransitionHash ([IO.File]::ReadAllBytes((Join-Path $framework 'csc.exe')));$referenceHash=Get-TransitionHash ([IO.File]::ReadAllBytes((Join-Path $framework 'System.Web.Extensions.dll')))
 Assert-Transition ($tooling.build.compilerSha256 -ceq $compilerHash -and $tooling.build.references[0].sha256 -ceq $referenceHash) 'Native build compiler/reference provenance differs from fixed Framework inputs'
 Assert-Transition ($control.toolingManifestSha256 -ceq $source['tooling-manifest.json'].entry.sha256 -and $gate.toolingManifestSha256 -ceq $control.toolingManifestSha256) 'Frozen tooling manifest binding differs'
 foreach($file in $tooling.files){Assert-Transition ($source[$file.name].entry.sha256 -ceq $file.sha256) 'Tooling source differs from frozen build'}
 $acl=Get-TransitionJsonBytes $source['expected-acl-manifest.json'].bytes;Assert-TransitionAclIntent $acl $plan $control $facts.acl.actualLogonSid
 Assert-Transition ($control.expectedAclManifestSha256 -ceq $source['expected-acl-manifest.json'].entry.sha256 -and $gate.expectedAclManifestSha256 -ceq $control.expectedAclManifestSha256) 'ACL intent hash differs'
 $build=Get-TransitionJsonBytes $source['helper-build.json'].bytes
 Assert-Transition ($build.schema -eq 1 -and $build.scope -ceq 'controller-file-security-helper-build' -and $build.exitCode -eq 0 -and $build.target -ceq 'library' -and $build.platform -ceq 'x64' -and $build.compilerSha256 -ceq $compilerHash -and $build.sourceSha256 -ceq (Get-TransitionTextHash $script:TransitionNativeSource) -and $build.assemblySha256 -ceq $source['FullRestrictedStageFileSecurity.dll'].entry.sha256 -and $build.moduleSha256 -ceq (Get-TransitionHash ([IO.File]::ReadAllBytes($PSCommandPath)))) 'File/security helper source/module/output build pins differ'
 $approval=$null;$approvalBody=$null
 if($RequireApproval -or $ApprovalPath){Assert-Transition (-not [string]::IsNullOrWhiteSpace($ApprovalPath) -and -not [string]::IsNullOrWhiteSpace($ExpectedApprovalSha256)) 'Apply requires separately trusted positive approval digest';$approvalBytes=Read-TransitionPinnedCFile $ApprovalPath $ExpectedApprovalSha256 16384;$text=[Text.UTF8Encoding]::new($false,$true).GetString($approvalBytes);$approval=[pscustomobject]@{bytes=$approvalBytes.Length;content=$text};$planner=Get-Module FullRestrictedCapabilitiesPlan;$checked=& $planner {param($a,$hash,$p,$f)$failures=[Collections.Generic.List[string]]::new();$body=Test-Approval $a $hash $p $f $failures;[pscustomobject]@{body=$body;failures=$failures.ToArray()}} $approval $ExpectedApprovalSha256 $plan $facts;Assert-Transition ($checked.failures.Count -eq 0) ('Approval rejected: '+($checked.failures -join '; '));$approvalBody=$checked.body;Assert-Transition ($approvalBody.controlGateSha256 -ceq $ExpectedGateSha256) 'Approval gate digest differs';Assert-AzuriteFullRuntimeGate $gate $plan $approvalBody $plan.RunId $ExpectedGateSha256}
 return [pscustomobject]@{Manifest=$manifest;ManifestSha256=$ExpectedManifestSha256;Source=$source;Plan=$plan;Facts=$facts;Contract=$contract;Control=$control;ControlBytes=$controlBytes;Gate=$gate;Approval=$approval;ApprovalBody=$approvalBody;ApprovalSha256=$ExpectedApprovalSha256;Acl=$acl;Baseline=(Get-TransitionJsonBytes $source['identity-baseline.json'].bytes)}
}
function New-TransitionPhysicalSpec($PreparedInput){
 $plan=$PreparedInput.Plan;$root=$script:TransitionRoot;$install=$root+'\install';$run=$root+'\runs\restricted-'+$plan.RunId;$owner=$script:TransitionOwner
 $stage=Get-TransitionJsonBytes (Read-TransitionFrozen 'artifacts/editor-normal-install-preparation/source-bound-manifest-v2.json' '0bddb4285f5cee0c79c3f2ab6f6ab8c346937ae8ff12b4f49f29a6f102bd33dd')
 $post=Get-TransitionJsonBytes (Read-TransitionFrozen 'artifacts/editor-normal-install-postverify/verification-08a369f8f1e54fc58da8e0d2f72bf607.json' '806f4524aa7e7025e490be9aab9b87225195090149d9ede9727796b7ad0bf26d')
 $warm=Get-TransitionJsonBytes (Read-TransitionFrozen 'artifacts/aa-fr-cache-prerequisites/warm-stamp-result.json' 'ae20a85d28f254d14851deeb9d4ff0627bb7d821bd04154814c8fdfcca830fb6')
 $baseline=@{};foreach($e in $PreparedInput.Baseline.objects){Assert-Transition (-not $baseline.ContainsKey($e.path)) 'Duplicate baseline identity';$baseline[$e.path]=$e}
 Assert-Transition ($PreparedInput.Baseline.schema -eq 1 -and $PreparedInput.Baseline.scope -ceq 'preapproval-stage-object-identities' -and $PreparedInput.Baseline.runId -ceq $plan.RunId) 'Identity baseline scope differs'
 $aclMap=@{};foreach($e in $PreparedInput.Acl.objects){$aclMap[$e.path]=$e}
 $privateDir=$aclMap[($run+'\history')].sddl;$privateFile=$aclMap[($run+'\tooling\control.json')].sddl
 $existing=[Collections.Generic.List[object]]::new();$immutable=[Collections.Generic.List[string]]::new();$grants=[Collections.Generic.List[object]]::new()
 $paths=@('F:\AzuriteLabs',$root,$install)+@($stage.directories|ForEach-Object {$install+'\'+$_.Replace('/','\')})
 foreach($path in $paths){Assert-Transition ($baseline.ContainsKey($path)) "Missing preapproval directory identity: $path";$b=$baseline[$path];$sd=[Security.AccessControl.RawSecurityDescriptor]::new($b.sddl);$before=$sd.GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Access');$want='O:'+ $owner+'D:P'+$(if($path -ceq 'F:\AzuriteLabs' -or $path -ceq $root){''}else{'(D;;0x6;;;'+$owner+')'})+'(A;OICI;FA;;;'+$owner+')(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)';$want=[Security.AccessControl.RawSecurityDescriptor]::new($want).GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Access');Assert-Transition ($before -ceq $want) 'Pinned original directory creation seal/private baseline differs';$existing.Add([pscustomobject]@{path=$path;identity=$b.identity;directory=$true;sha256=$null;bytes=0L;baselineSddl=(Get-TransitionCanonicalSddl $b.sddl);captureBytes=$false})}
 foreach($row in $stage.entries){$path=$install+'\'+$row.path.Replace('/','\');Assert-Transition ($baseline.ContainsKey($path)) "Missing preapproval file identity: $path";$b=$baseline[$path];$sddl=Get-TransitionCanonicalSddl $b.sddl;$sd=[Security.AccessControl.RawSecurityDescriptor]::new($sddl);$oldHash=Get-TransitionTextHash ($sd.GetSddlForm([Security.AccessControl.AccessControlSections]'Owner,Access'));$saved=@($post.destinationFiles|Where-Object path -CEQ $row.path)[0];Assert-Transition ($oldHash -ceq $saved.aclSha256) 'Pinned inherited file descriptor differs';$capture=@($plan.MutableLeaves|Where-Object path -CEQ $row.path).Count -gt 0;$existing.Add([pscustomobject]@{path=$path;identity=$b.identity;directory=$false;sha256=$row.sha256;bytes=[long]$row.bytes;baselineSddl=$sddl;captureBytes=$capture});if($row.path -cne 'BepInEx/LogOutput.log'){$immutable.Add($path)}}
 $anchorPins=[ordered]@{'F:\AzuriteLabs\.azurite-phase0-parent.json'='8098538d54eadb7b6ee5cf07b514fb311a16ecdc97b80558fd30db565d9bd4fe';($root+'\lab.marker.json')='8098538d54eadb7b6ee5cf07b514fb311a16ecdc97b80558fd30db565d9bd4fe';($root+'\.phase0-staging\install-creation-seal.json')='abe2163a148bb825bd92edfd60a32a576bfa6dc583b7da24599617ae550ed454';($root+'\.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9\complete.json')='15506ff9993a2c51f3d508a880e3e2f3ec4eeae5417124b2fc7d42a2f2a90c28';($root+'\.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9\files.jsonl')='2fa532b2c2a483e8645b52d97bc2c916f0d94c438680271ae1f510952b8de1c2';($root+'\.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9\start.json')='c4e73c894ec326c1736bd9f05aaec4d993d1b4927217775d31ff86ce138ca008';($root+'\.phase0-staging\attempt-96a70ab6e0544f98910fba93142b9cb9\seal-complete.json')='371ff1df832daba64dd8206c49a4b941f026943399cee702b1cdfc9cfbaa246f'}
 foreach($path in $anchorPins.Keys){Assert-Transition ($baseline.ContainsKey($path)) 'Missing pinned historical anchor identity';$b=$baseline[$path];$existing.Add([pscustomobject]@{path=$path;identity=$b.identity;directory=$false;sha256=$anchorPins[$path];bytes=[long]$b.bytes;baselineSddl=(Get-TransitionCanonicalSddl $b.sddl);captureBytes=($path -like '*marker.json')});$immutable.Add($path)}
 Assert-Transition ($baseline.Count -eq $existing.Count) 'Unexpected preapproval identity inventory entry'
 $dirs=@(($root+'\runs'),$run,($run+'\tooling'),($run+'\history'),($run+'\history\transition'),($run+'\history\post-run'),($run+'\request'),($run+'\output'),($run+'\workspace'),($run+'\shared-state'))
 $files=[Collections.Generic.List[object]]::new()
 foreach($name in (Invoke-TransitionRuntimeHelper 'Get-AzuriteFullToolNames' @())+@('tooling-manifest.json','expected-acl-manifest.json')){$files.Add([pscustomobject]@{path=$run+'\tooling\'+$name;bytes=$PreparedInput.Source[$name].bytes;kind='static';early=$false;role='private-file'})}
 $files.Add([pscustomobject]@{path=$run+'\tooling\control.json';bytes=$PreparedInput.ControlBytes;kind='static';early=$false;role='private-file'})
 $arguments=[pscustomobject][ordered]@{argv=@('capabilities');cwd=$run+'\workspace'};$argumentBytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $arguments));Assert-Transition ((Get-TransitionHash $argumentBytes) -ceq $PreparedInput.Control.argumentsSha256) 'Protected exact capabilities arguments digest differs'
 $files.Add([pscustomobject]@{path=$run+'\request\arguments.json';bytes=$argumentBytes;kind='static';early=$false;role='arguments-file'})
 $files.Add([pscustomobject]@{path=$run+'\output\unity.log';bytes=[byte[]]@();kind='static';early=$false;role='mutable-file'})
 foreach($name in @('scope.json','before.json')){$files.Add([pscustomobject]@{path=$run+'\shared-state\'+$name;bytes=$PreparedInput.Source[$name].bytes;kind='static';early=$false;role='private-file'})}
 Assert-Transition ($PreparedInput.Control.sharedStateBeforeSha256 -ceq $PreparedInput.Source['before.json'].entry.sha256) 'Shared-state before pin differs'
 foreach($item in @(@('transition\before-images-raw.json','raw',$true),@('before-images.json','mutable-summary',$true),@('acl-before-images.json','acl-summary',$true),@('precreated-log-baselines.json','log-baselines',$false))){$files.Add([pscustomobject]@{path=$run+'\history\'+$item[0];bytes=[byte[]]@();kind=$item[1];early=$item[2];role='private-file'})}
 $pending=[pscustomobject][ordered]@{schema=1;scope='full-capabilities-global-pending';runId=$plan.RunId;planSha256=$plan.PlanSha256;controllerSid=$owner;runDirectory=$run;state='transition-pending'}
 $files.Add([pscustomobject]@{path=$root+'\runs\full-capabilities.pending.json';bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $pending));kind='static';early=$true;role='private-file'})
 foreach($entry in $PreparedInput.Acl.objects){if(@($existing|Where-Object path -CEQ $entry.path).Count -or $dirs -ccontains $entry.path -or @($files|Where-Object path -CEQ $entry.path).Count -or $entry.path -ceq ($install+'\'+$PreparedInput.Contract.cachePrerequisites.modTheLeaf.Replace('/','\'))){if($baseline.ContainsKey($entry.path)){$beforeSd=[Security.AccessControl.RawSecurityDescriptor]::new($baseline[$entry.path].sddl);$afterSd=[Security.AccessControl.RawSecurityDescriptor]::new($entry.sddl);Assert-Transition ($beforeSd.Group.Value -ceq $afterSd.Group.Value -and $beforeSd.Owner.Value -ceq $afterSd.Owner.Value) 'Approved ACL delta may not change frozen owner/group'};$grants.Add([pscustomobject]@{path=$entry.path;sddl=(Get-TransitionCanonicalSddl $entry.sddl)})}}
 $cachePath=$install+'\'+$PreparedInput.Contract.cachePrerequisites.modTheLeaf.Replace('/','\')
 $expectedCount=$plan.Grants.files.Count+$plan.Grants.directories.Count+$dirs.Count+$files.Count+1
 Assert-Transition ($grants.Count -eq $expectedCount) 'Planned ACL manifest omits an original or created target'
 return [pscustomobject]@{RunId=$plan.RunId;OwnerSid=$owner;LogonSid=$PreparedInput.Facts.acl.actualLogonSid;Volume=$script:TransitionVolume;Root=$root;Install=$install;Run=$run;CacheParent=$install+'\BepInEx\cache';CacheFile=$cachePath;Loader=$install+'\BepInEx\LogOutput.log';History=$run+'\history';ProtectedArguments=$run+'\request\arguments.json';Seed=[byte[]](0,0,0,0);Existing=$existing.ToArray();Directories=$dirs;Files=$files.ToArray();Grants=$grants.ToArray();ImmutablePaths=$immutable.ToArray();PrivateDirectorySddl=(Get-TransitionCanonicalSddl $privateDir);PrivateFileSddl=(Get-TransitionCanonicalSddl $privateFile);Input=$PreparedInput;Stage=$stage;Warm=$warm;AnchorPins=$anchorPins;Inventories=@();PendingPath=$root+'\runs\full-capabilities.pending.json'}
}
function Get-TransitionExpectedRoles($Plan){
 return Invoke-TransitionRuntimeHelper 'Get-AzuriteFullExpectedRoles' @($Plan,$null)
}
function Assert-TransitionAclIntent($Manifest,$Plan,$Control,[string]$LogonSid){
 Invoke-TransitionRuntimeHelper 'Assert-AzuriteFullExpectedAclManifest' @($Manifest,$Plan,$Control,$LogonSid)
}
function New-TransitionReceipt([string]$Kind,$Value,$Spec){
 $body=[pscustomobject][ordered]@{schema=1;kind=$Kind;stage='posttransition';runId=$Spec.RunId;planSha256=$Spec.Input.Plan.PlanSha256;contractSha256=$Spec.Input.Plan.ContractSha256;ownerSid=$Spec.OwnerSid;volumeGuid=$Spec.Volume;result=[pscustomobject]$Value}
 $text=ConvertTo-TransitionJson $body;return [pscustomobject][ordered]@{kind=$Kind;phase='posttransition';runId=$Spec.RunId;planSha256=$Spec.Input.Plan.PlanSha256;contractSha256=$Spec.Input.Plan.ContractSha256;ownerSid=$Spec.OwnerSid;volumeGuid=$Spec.Volume;bytes=[Text.UTF8Encoding]::new($false).GetByteCount($text);sha256=(Get-TransitionTextHash $text);content=$text}
}
function Assert-TransitionReceiptObservations($Spec,$Observation,$BeforeItems,$AclItems){
 $objects=@{};$snapshots=@{};$readbacks=@{}
 foreach($item in $Observation.objects){Assert-Transition (-not $objects.ContainsKey($item.path)) 'Duplicate final observation path';$objects[$item.path]=$item}
 foreach($item in $Observation.snapshots){Assert-Transition (-not $snapshots.ContainsKey($item.path)) 'Duplicate before-image observation path';$snapshots[$item.path]=$item}
 foreach($item in $Observation.grantReadbacks){Assert-Transition (-not $readbacks.ContainsKey($item.path)) 'Duplicate grant observation path';$readbacks[$item.path]=$item}
 Assert-Transition ($readbacks.Count -eq $Spec.Grants.Count) 'Grant observation coverage differs'
 foreach($grant in $Spec.Grants){
  Assert-Transition ($readbacks.ContainsKey($grant.path) -and $objects.ContainsKey($grant.path)) 'Missing actual grant observation'
  $actual=$readbacks[$grant.path];$final=$objects[$grant.path]
  Assert-TransitionObject $actual $Spec $grant.path ([bool]$final.directory)
  Assert-Transition ($actual.sddl -ceq $grant.sddl -and $final.sddl -ceq $actual.sddl -and $actual.identity -ceq $final.identity) 'Grant observation descriptor/identity differs from the applied exact delta'
 }
 foreach($item in $BeforeItems){
  $path=if($item.path -ceq 'output/unity.log'){$Spec.Run+'\output\unity.log'}else{$Spec.Install+'\'+$item.path.Replace('/','\')}
  if($item.absentBefore){Assert-Transition (-not $snapshots.ContainsKey($path) -and $null -eq $item.fileIdentity) 'Absent before-image has an invented original identity';continue}
  Assert-Transition ($snapshots.ContainsKey($path)) 'Before-image lacks a captured original object';$original=$snapshots[$path]
  Assert-Transition ($original.sddl -ceq (Get-TransitionCanonicalSddl $original.sddl) -and $item.fileIdentity -ceq $original.identity -and $item.initialSha256 -ceq $original.sha256 -and $item.aclSha256 -ceq (Get-TransitionTextHash $original.sddl)) 'Before-image payload/identity/full-descriptor digest differs from the actual snapshot'
 }
 foreach($item in $AclItems){
  $path=if($item.path -cmatch '^F:\\'){$item.path}elseif($item.path -ceq '.'){$Spec.Install}else{$Spec.Install+'\'+$item.path.Replace('/','\')}
  Assert-Transition ($snapshots.ContainsKey($path)) 'ACL before-image lacks a captured original object';$original=$snapshots[$path]
  Assert-Transition ($original.sddl -ceq (Get-TransitionCanonicalSddl $original.sddl) -and $item.objectIdentity -ceq $original.identity -and $item.ownerSid -ceq $original.owner -and $item.descriptorSha256 -ceq (Get-TransitionTextHash $original.sddl)) 'ACL before-image identity/full-descriptor digest differs from the actual snapshot'
 }
 foreach($file in @($Spec.Files|Where-Object {$_.kind -cin @('mutable-summary','acl-summary')})){
  Assert-Transition ($objects.ContainsKey($file.path) -and $objects[$file.path].sha256 -ceq (Get-TransitionHash $file.bytes)) 'Persisted before-image inventory differs from its actual creation readback'
 }
}
function Assert-TransitionPreparationChronology($Facts,$Approval,[string]$HeldFromUtc,[string]$HeldUntilUtc){
 $baseline=[datetimeoffset]::Parse($Facts.preTransition.observedUtc,[Globalization.CultureInfo]::InvariantCulture)
 $from=[datetimeoffset]::Parse($Approval.validFromUtc,[Globalization.CultureInfo]::InvariantCulture)
 $issued=[datetimeoffset]::Parse($Approval.issuedUtc,[Globalization.CultureInfo]::InvariantCulture)
 $expiry=[datetimeoffset]::Parse($Approval.validUntilUtc,[Globalization.CultureInfo]::InvariantCulture)
 $held=[datetimeoffset]::Parse($HeldFromUtc,[Globalization.CultureInfo]::InvariantCulture)
 Assert-Transition ($from -le $baseline -and $baseline -le $issued -and $issued -le $held -and $held -le $expiry) 'Preparation lease acquisition is outside approved baseline/issuance chronology'
 if($HeldUntilUtc){$released=[datetimeoffset]::Parse($HeldUntilUtc,[Globalization.CultureInfo]::InvariantCulture);Assert-Transition ($held -le $released -and $released -le $expiry) 'Preparation lease release is outside approved chronology/expiry'}
}
function New-TransitionReceiptBundle($Spec,$Result,$State){
 Assert-Transition ($Result.Success -and $Result.LeaseState -ceq 'released' -and $State.ReleaseOutcome -ceq 'success') 'No completion receipts without observed successful release'
 $o=$Result.Observations;$plan=$Spec.Input.Plan;$facts=Get-TransitionJsonBytes ([Text.Encoding]::UTF8.GetBytes((ConvertTo-TransitionJson $Spec.Input.Facts)));$receiptList=[Collections.Generic.List[object]]::new();$hashes=@{}
 $beforeFile=@($Spec.Files|Where-Object kind -CEQ 'mutable-summary')[0];$aclFile=@($Spec.Files|Where-Object kind -CEQ 'acl-summary')[0];$before=@(Get-TransitionJsonBytes $beforeFile.bytes|ForEach-Object {$_});$descriptors=@(Get-TransitionJsonBytes $aclFile.bytes|ForEach-Object {$_})
 Assert-Transition ($before.Count -eq 6 -and $descriptors.Count -eq 1221 -and $o.grantReadbacks.Count -eq $Spec.Grants.Count -and $o.cacheLeaf.sha256 -ceq $facts.cache.seedSha256 -and $o.cacheParentBefore.sddl -ceq $o.cacheParentRestored.sddl -and $o.loader.bytes -eq 0) 'Actual receipt coverage/content/seal postconditions incomplete'
 Assert-TransitionReceiptObservations $Spec $o $before $descriptors
 $approvalObject=Get-TransitionJsonBytes ([Text.UTF8Encoding]::new($false).GetBytes($Spec.Input.Approval.content))
 Assert-TransitionPreparationChronology $facts $approvalObject $State.HeldFromUtc $State.HeldUntilUtc
 $unity=@($o.objects|Where-Object path -CEQ ($Spec.Run+'\output\unity.log'))[0];Assert-Transition ($unity.bytes -eq 0 -and $unity.sha256 -ceq (Get-TransitionHash ([byte[]]@()))) 'Actual empty Unity baseline missing'
 $t=[ordered]@{phase='posttransition';runId=$Spec.RunId;ownerSid=$Spec.OwnerSid;volumeGuid=$Spec.Volume;seedSha256=$facts.cache.seedSha256;leaseId=$State.LeaseId;cacheLeafCreateMode='CreateNewNoOverwrite';cacheLeafCreated=$true;cacheLeafSha256=$o.cacheLeaf.sha256;cacheLeafFileIdentity=$o.cacheLeaf.identity;cacheParentBeforeDescriptorSha256=(Get-TransitionTextHash $o.cacheParentBefore.sddl);cacheParentRestoredDescriptorSha256=(Get-TransitionTextHash $o.cacheParentRestored.sddl);unityLogFileIdentity=$unity.identity;sealRestored=$true;stageSealsExceptCacheUnchanged=$true;sharedDelta='None';runtimeAllowed=$false;precreatedUnityLogEmpty=$true;requestArgumentsProtected=$true;beforeImageInventorySha256=(Get-TransitionHash $beforeFile.bytes);aclBeforeImageInventorySha256=(Get-TransitionHash $aclFile.bytes);reviewApprovalSha256=$Spec.Input.ApprovalSha256}
 $values=[ordered]@{
  'lease'=[ordered]@{state='released-after-preparation';leaseId=$State.LeaseId;runId=$Spec.RunId;ownerSid=$Spec.OwnerSid;volumeGuid=$Spec.Volume;scope='capabilities-only-preparation';globalMutex='Global\AzuriteRestrictedCli-AA';heldFromUtc=$State.HeldFromUtc;heldUntilUtc=$State.HeldUntilUtc;releaseOutcome='success';pendingRunId=$Spec.RunId}
  'before-image'=[ordered]@{state='captured';inventorySha256=$t.beforeImageInventorySha256;leafCount=6;mutablePathsSha256=(Get-TransitionTextHash ((@($plan.MutableLeaves.path)-join "`n")+"`n"));ownerSid=$Spec.OwnerSid;newCacheLeafAbsent=$true;descriptorEncoding='Utf8NoBomFullOwnerGroupAccessSddl';items=$before}
  'acl-before-image'=[ordered]@{state='captured';inventorySha256=$t.aclBeforeImageInventorySha256;descriptorCount=$descriptors.Count;cacheParent='BepInEx/cache';cacheParentDescriptorSha256=$t.cacheParentBeforeDescriptorSha256;ownerSid=$Spec.OwnerSid;descriptorEncoding='Utf8NoBomFullOwnerGroupAccessSddl';descriptors=$descriptors}
  'grant'=[ordered]@{state='applied';grantsSha256=$plan.GrantDigest;principalSid=$Spec.LogonSid;namedMutableMask='0x12019F';requestParentMask='0x1200AB';responseChildMask='0x13019F';argumentsMask='0x120089';sharedDelta='None'}
  'cache-create'=[ordered]@{state='created-new';leaf=$facts.cache.modTheLeaf;absentBefore=$true;createMode='CreateNewNoOverwrite';seedBytes=4;seedSha256=$facts.cache.seedSha256;resultSha256=$o.cacheLeaf.sha256;fileIdentity=$o.cacheLeaf.identity;parent='BepInEx/cache'}
  'seal-restoration'=[ordered]@{state='restored';parent='BepInEx/cache';creationDenyMask='0x6';policySha256=$facts.stage.sealPolicySha256;beforeDescriptorSha256=$t.cacheParentBeforeDescriptorSha256;restoredDescriptorSha256=$t.cacheParentRestoredDescriptorSha256;otherSealsUnchanged=$true}
  'unchanged-inputs'=[ordered]@{state='matched';manifestSha256=$facts.stage.manifestSha256;immutablePinDigest=$plan.ReadEnvelope.immutableFilePinDigest;immutableFileCount=1058;directoryPinDigest=$plan.ReadEnvelope.directoryPinDigest;directoryCount=156}
  'warm-input-order'=[ordered]@{state='matched';computedStamp=$facts.cache.stamp;recordedProofSha256=$facts.inputs.warmResultSha256;eventCount=147;inputFileCount=73;unityDllCount=72;generatorVersion='1.5.3.0';cpp2ilVersion='2022.1.0.0';optionalRenameMapPresent=$false;gameAssemblyOverridePresent=$false;unhollowedPathArgumentPresent=$false}
  'unity-log-precreation'=[ordered]@{state='precreated-empty';path='output/unity.log';baselineBytes=$unity.bytes;fileIdentity=$unity.identity;fileMask='0x12019F';ownerSid=$Spec.OwnerSid}
  'response-policy'=[ordered]@{state='prepared';parent='request';parentMask='0x1200AB';childFileMask='0x13019F';argumentsFile='arguments.json';argumentsMask='0x120089';argumentsProtected=$true;createSubdirectory=$false;deleteChild=$false;expectedOutput='response.json';responseMaxBytes=1048576;maxTemporaryFiles=1;postrunValidationRequired=$true;unexpectedOutput='Reject'}
  'review'=[ordered]@{state='positive-approval-bound';approvalSha256=$Spec.Input.ApprovalSha256;runId=$Spec.RunId;planSha256=$plan.PlanSha256;ownerSid=$Spec.OwnerSid;volumeGuid=$Spec.Volume}
 }
 $map=[ordered]@{'lease'='leaseReceiptSha256';'before-image'='beforeImageReceiptSha256';'acl-before-image'='aclBeforeImageReceiptSha256';'grant'='grantReceiptSha256';'cache-create'='cacheLeafCreateReceiptSha256';'seal-restoration'='sealRestorationReceiptSha256';'unchanged-inputs'='unchangedInputsReceiptSha256';'warm-input-order'='warmInputOrderReceiptSha256';'unity-log-precreation'='unityLogPrecreationReceiptSha256';'response-policy'='responsePolicyReceiptSha256';'review'='reviewReceiptSha256'}
 foreach($kind in $values.Keys){$r=New-TransitionReceipt $kind $values[$kind] $Spec;$receiptList.Add($r);$hashes[$kind]=$r.sha256;$t[$map[$kind]]=$r.sha256}
 $parts=@($values.Keys|ForEach-Object {$_+'|'+$hashes[$_]});$final=[ordered]@{state='complete-for-capabilities-only';phase0CompletionSha256=$facts.stage.completionSha256;receiptSetSha256=(Get-TransitionTextHash (($parts -join "`n")+"`n"));cacheLeafSha256=$facts.cache.seedSha256;sealRestored=$true;sharedDelta='None'};$r=New-TransitionReceipt 'transition' $final $Spec;$receiptList.Add($r);$t.receiptSha256=$r.sha256;$facts.transition=[pscustomobject]$t
 $bundle=[pscustomobject][ordered]@{schema=1;scope='full-restricted-capabilities-admission-evidence';stage='posttransition';runId=$Spec.RunId;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;ownerSid=$Spec.OwnerSid;volumeGuid=$Spec.Volume;receipts=$receiptList.ToArray()}
 $admission=[pscustomobject][ordered]@{schema=1;scope='capabilities-only-current-account-full-restricted';runId=$Spec.RunId;planSha256=$plan.PlanSha256;contractSha256=$plan.ContractSha256;labRoot=$plan.LabRoot;executable=$plan.Executable;operation='capabilities';volumeGuid=$Spec.Volume;ownerSid=$Spec.OwnerSid;controllerSid=$Spec.OwnerSid;phase0=$Spec.Input.Contract.stage;inputHashes=$facts.inputs;cacheProofSha256=$Spec.Input.Contract.cachePrerequisites.warmResultSha256;evidenceBundleSha256=(Get-TransitionTextHash (ConvertTo-TransitionJson $bundle));approvalSha256=$Spec.Input.ApprovalSha256;transition=$facts.transition}
 $cache=Get-TransitionJsonBytes (Read-TransitionFrozen 'artifacts/aa-fr-cache-prerequisites/cache-prerequisites-contract.json' '11476211f744e7009fe1bd21b7bc4b786d3f73c89a747f6ecab955e43754c5f4')
 $complete=New-AzuriteFullCapabilitiesPlan -Contract $Spec.Input.Contract -CachePrerequisites $cache -RunId $Spec.RunId -Facts $facts -Admission $admission -EvidenceBundle $bundle -Approval $Spec.Input.Approval -ExpectedApprovalSha256 $Spec.Input.ApprovalSha256
 Assert-Transition $complete.Ready ('Actual typed receipt admission failed: '+($complete.Failures -join '; '))
 return [pscustomobject]@{Facts=$facts;EvidenceBundle=$bundle;Admission=$admission;Plan=$complete;RequiresFreshRuntimeLease=$true;PreparationLeaseReleased=$true;WillLaunch=$false;RuntimeAllowed=$false}
}
function Get-TransitionEvidenceBytes($File,$Spec,$Snapshots,$Current){
 $map=@{};foreach($o in $Snapshots){$map[$o.path]=$o}
 $value=switch($File.kind){
  'static' {return ,$File.bytes}
  'raw' {[pscustomobject]@{schema=1;runId=$Spec.RunId;descriptorSections='Owner,Group,Access; SACL not read';objects=$Snapshots;absent=@($Spec.Directories)+@($Spec.Files.path)+@($Spec.CacheFile)};break}
  'mutable-summary' {@(foreach($leaf in $Spec.Input.Plan.BeforeImages){$path=if($leaf.path -ceq 'output/unity.log'){$Spec.Run+'\output\unity.log'}else{$Spec.Install+'\'+$leaf.path.Replace('/','\')};$o=$map[$path];[pscustomobject][ordered]@{path=$leaf.path;initialSha256=$(if($o){$o.sha256}else{$null});aclSha256=$(if($o){Get-TransitionTextHash $o.sddl}else{Get-TransitionTextHash $Spec.PrivateFileSddl});fileIdentity=$(if($o){$o.identity}else{$null});absentBefore=($null -eq $o)}});break}
  'acl-summary' {@(foreach($grant in @($Spec.Input.Plan.Grants.files)+@($Spec.Input.Plan.Grants.directories)){$path=if($grant.path -cmatch '^F:\\'){$grant.path}elseif($grant.path -ceq '.'){$Spec.Install}else{$Spec.Install+'\'+$grant.path.Replace('/','\')};$o=$map[$path];Assert-Transition ($null -ne $o) 'ACL before-image summary coverage incomplete';[pscustomobject][ordered]@{path=$grant.path;role=$grant.role;mask=$grant.mask;ownerSid=$o.owner;descriptorSha256=(Get-TransitionTextHash $o.sddl);objectIdentity=$o.identity}});break}
  'log-baselines' {[pscustomobject]@{schema=1;runId=$Spec.RunId;logs=@([pscustomobject]@{runId=$Spec.RunId;path=$Spec.Loader;identity=$Current[$Spec.Loader].identity;bytes=$Current[$Spec.Loader].bytes;sha256=$Current[$Spec.Loader].sha256},[pscustomobject]@{runId=$Spec.RunId;path=$Spec.Run+'\output\unity.log';identity=$Current[($Spec.Run+'\output\unity.log')].identity;bytes=0;sha256=$Current[($Spec.Run+'\output\unity.log')].sha256})};break}
  default {throw 'Unknown evidence file role'}
 }
 return ,[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $value))
}
function Write-TransitionNewCRecord([string]$Path,$Value){
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $Value));$f=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()};Assert-Transition ((Get-TransitionHash ([IO.File]::ReadAllBytes($Path))) -ceq (Get-TransitionHash $bytes)) 'Controller record readback failed'
}
function Add-TransitionJournal($State,$Value){
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $Value)+"`n");$f=[IO.FileStream]::new($State.Journal,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read);try{[void]$f.Seek(0,[IO.SeekOrigin]::End);$f.Write($bytes,0,$bytes.Length);$f.Flush($true);$f.Position=$f.Length-$bytes.Length;$read=New-Object byte[] $bytes.Length;Assert-Transition ($f.Read($read,0,$read.Length) -eq $bytes.Length -and (Get-TransitionHash $read) -ceq (Get-TransitionHash $bytes)) 'Journal durable readback differs'}finally{$f.Dispose()}
}
function Lock-TransitionAncestry($State,[string]$Path,[bool]$IncludeLeaf){
 $cursor=if($IncludeLeaf){$Path}else{Split-Path -Parent $Path};$paths=[Collections.Generic.List[string]]::new();while($cursor){$paths.Add($cursor);$cursor=Split-Path -Parent $cursor}
 for($i=$paths.Count-1;$i -ge 0;$i--){$p=$paths[$i];if(-not $State.Locks.ContainsKey($p)){$State.Locks[$p]=[Azurite.Stage.FileSecurity]::LockDirectory($p)}}
}
function Get-TransitionPhysicalObject($State,[string]$Path,[bool]$Capture){
 if(-not [IO.File]::Exists($Path) -and -not [IO.Directory]::Exists($Path)){# Check an existing parent before calling absence an owned location.
  $parent=Split-Path -Parent $Path;if([IO.Directory]::Exists($parent)){Lock-TransitionAncestry $State $parent $true};return $null
 }
 Lock-TransitionAncestry $State $Path ([IO.Directory]::Exists($Path));return [Azurite.Stage.FileSecurity]::Read($Path,$Capture)
}
function Assert-TransitionPhysicalInventories($State,[string]$Phase){
 $s=$State.Spec;$expected=@{}
 function AddExpected([string]$path){$parent=Split-Path -Parent $path;if(-not $expected.ContainsKey($parent)){$expected[$parent]=[Collections.Generic.List[string]]::new()};$expected[$parent].Add([IO.Path]::GetFileName($path))}
 $expected['F:\AzuriteLabs']=[Collections.Generic.List[string]]::new();$expected['F:\AzuriteLabs'].Add('.azurite-phase0-parent.json');$expected['F:\AzuriteLabs'].Add('Editor-20261108-normal')
 $expected[$s.Root]=[Collections.Generic.List[string]]::new();foreach($name in @('install','.phase0-staging','lab.marker.json')){$expected[$s.Root].Add($name)}
 foreach($dir in @($s.Install)+@($s.Stage.directories|ForEach-Object {$s.Install+'\'+$_.Replace('/','\')})){$expected[$dir]=[Collections.Generic.List[string]]::new()}
 foreach($entry in $s.Stage.entries){AddExpected ($s.Install+'\'+$entry.path.Replace('/','\'))};foreach($name in $s.Stage.directories){AddExpected ($s.Install+'\'+$name.Replace('/','\'))}
 if($Phase -ceq 'prepared'){foreach($dir in $s.Directories){$expected[$dir]=[Collections.Generic.List[string]]::new()};foreach($path in @($s.Directories)+@($s.Files.path)+@($s.CacheFile)){AddExpected $path}}
 foreach($path in $expected.Keys){Lock-TransitionAncestry $State $path $true;$items=@([IO.Directory]::EnumerateFileSystemEntries($path));Assert-Transition ($items.Count -le 2048 -and $items.Count -eq $expected[$path].Count) "Bounded exact child inventory differs: $path";$names=@($items|ForEach-Object {[IO.Path]::GetFileName($_)}|Sort-Object -CaseSensitive);$wanted=@($expected[$path]|Sort-Object -CaseSensitive);Assert-Transition (($names -join "`n") -ceq ($wanted -join "`n")) "Unexpected child/case spelling: $path"}
}
function Assert-TransitionPhysicalEnvironment($State){
 $s=$State.Spec;Assert-AzuriteFullEnvironment ([Environment]::GetEnvironmentVariables())
 Assert-Transition ([datetimeoffset]::UtcNow -lt [datetimeoffset]::Parse($s.Input.Control.intentValidUntilUtc) -and [datetimeoffset]::UtcNow -le [datetimeoffset]::Parse($s.Input.ApprovalBody.validUntilUtc)) 'Preparation intent/approval expired before mutation/readback completion'
 Assert-Transition (-not [IO.File]::Exists($s.Install+'\BepInEx\DeobfuscationMap.csv.gz')) 'Optional rename map appeared'
 $actual=@([IO.Directory]::EnumerateFiles($s.Install+'\BepInEx\unity-libs','*.dll',[IO.SearchOption]::TopDirectoryOnly)|ForEach-Object {[IO.Path]::GetFileName($_)})
 $expected=@($s.Warm.input_events|Where-Object {$_.kind -ceq 'utf8_string' -and $_.value.EndsWith('.dll')}|ForEach-Object value)
 Assert-Transition ($actual.Count -eq 72 -and ($actual -join "`n") -ceq ($expected -join "`n")) 'Warm input enumeration order differs from recorded computation'
 foreach($path in @('F:\AzuriteLabs\.azurite-phase0-parent.json',($s.Root+'\lab.marker.json'))){$o=Get-TransitionPhysicalObject $State $path $true;$marker=Get-TransitionJsonBytes ([Convert]::FromBase64String($o.content));Assert-Transition ($marker.runtimeAllowed -eq $false -and $marker.controllerSid -ceq $s.OwnerSid -and $marker.labRoot -ceq $s.Root -and $marker.manifestSha256 -ceq $s.Input.Facts.stage.manifestSha256 -and $marker.labId -cmatch '^[a-f0-9-]{32,36}$') 'Historical marker identity/runtimeAllowed changed'}
 $aa=@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop);Assert-Transition ($aa.Count -eq 0) 'An AzureArchive process exists'
 return [pscustomobject][ordered]@{warmStamp=$s.Warm.computed_stamp;warmProofSha256='ae20a85d28f254d14851deeb9d4ff0627bb7d821bd04154814c8fdfcca830fb6';orderedNames=$actual;aaCount=0;markerRuntimeAllowed=$false}
}
function New-TransitionPhysicalBackend {
 return {param($Action,$A,$State)
  $s=$State.Spec
  switch($Action){
   'Acquire' {
    $State.Mutex=[Threading.Mutex]::new($false,'Global\AzuriteRestrictedCli-AA');try{$held=$State.Mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$State.Mutex.ReleaseMutex();$State.Mutex.Dispose();throw 'Abandoned global mutex requires reviewed quarantine recovery'};if(-not $held){$State.Mutex.Dispose();throw 'Another restricted CLI controller holds the global mutex'};$State.Held=$true;$State.HeldFromUtc=[datetime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    try{Assert-TransitionPreparationChronology $s.Input.Facts $s.Input.ApprovalBody $State.HeldFromUtc $null;foreach($p in @(($s.Root+'\secrets\restricted-cli.pending.json'),$s.PendingPath)){Assert-Transition (-not [IO.File]::Exists($p)) 'Existing global/legacy quarantine blocks preparation'}
     Assert-Transition (@(Get-CimInstance Win32_Process -Filter "Name='AzureArchive.exe'" -ErrorAction Stop).Count -eq 0) 'AzureArchive exists before preparation'
     $identity=[Azurite.Restricted.FullBootstrapIdentity]::Read($s.RunId);Assert-Transition ($identity['identitySid'] -ceq $s.OwnerSid -and $identity['token']['logon_sid'] -ceq $s.LogonSid -and $identity['status'] -ceq 'passed' -and -not $identity['isAdministrator'] -and $identity['hiveLoaded'] -and @($identity['unexpectedPrivileges']).Count -eq 0) 'Actual current-account token differs from prepared owner/logon'
     $rootEvidence=Get-TransitionPhysicalObject $State 'F:\AzuriteLabs' $false;Assert-Transition ($rootEvidence.volume -ceq $s.Volume) 'Fixed F volume GUID differs'
     $State.LeaseId=[guid]::NewGuid().ToString('N');return [pscustomobject]@{leaseId=$State.LeaseId;ownerSid=$s.OwnerSid;logonSid=$s.LogonSid;volume=$s.Volume;heldFromUtc=$State.HeldFromUtc;globalMutex='Global\AzuriteRestrictedCli-AA'}
    }catch{foreach($handle in $State.Locks.Values){$handle.Dispose()};$State.Locks.Clear();if($State.Held){$State.Mutex.ReleaseMutex();$State.Held=$false};$State.Mutex.Dispose();throw}
   }
   'Release' {foreach($handle in $State.Locks.Values){$handle.Dispose()};$State.Locks.Clear();if($State.Held){$State.Mutex.ReleaseMutex();$State.Held=$false};$State.Mutex.Dispose();$State.HeldUntilUtc=[datetime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');$State.ReleaseOutcome='success';Write-TransitionNewCRecord (Join-Path $State.Evidence 'lease-release.json') ([pscustomobject]@{leaseId=$State.LeaseId;globalMutex='Global\AzuriteRestrictedCli-AA';heldFromUtc=$State.HeldFromUtc;heldUntilUtc=$State.HeldUntilUtc;releaseOutcome='success';runId=$s.RunId});return}
   'Observe' {return Get-TransitionPhysicalObject $State $A.path $A.captureBytes}
   'ValidateEnvironment' {return Assert-TransitionPhysicalEnvironment $State}
   'VerifyInventories' {Assert-TransitionPhysicalInventories $State $A.phase;return}
   'Persist' {Write-TransitionNewCRecord (Join-Path $State.Evidence ($A.name+'.json')) $A.value;return}
   'Journal' {Add-TransitionJournal $State ([pscustomobject]@{utc=[datetime]::UtcNow.ToString('o');kind=$A.name;value=$A.value});return}
   'CreateDirectory' {Lock-TransitionAncestry $State $A.path $false;[Azurite.Stage.FileSecurity]::NewDirectory($A.path,$A.sddl);$State.OwnedNew[$A.path]=[Azurite.Stage.FileSecurity]::Read($A.path,$false);return}
   'CreateFile' {Lock-TransitionAncestry $State $A.path $false;[Azurite.Stage.FileSecurity]::NewFile($A.path,$A.bytes,$A.sddl);return}
   'WriteBytes' {[Azurite.Stage.FileSecurity]::WriteExisting($A.path,$A.expectedIdentity,$A.expectedSddl,$A.expectedSha256,$A.bytes);return}
   'SetDescriptor' {[Azurite.Stage.FileSecurity]::SetDescriptor($A.path,$A.expectedIdentity,$A.expectedSddl,$A.sddl);return}
   'Quarantine' {
    $remaining=[Collections.Generic.List[object]]::new();if($State.Held){foreach($path in @($s.Directories)+@($s.Files.path)+@($s.CacheFile,$s.CacheParent,$s.Loader)){try{$remaining.Add((Get-TransitionPhysicalObject $State $path $false))}catch{$remaining.Add([pscustomobject]@{path=$path;inspectionError=$_.Exception.Message})}}}
    $record=[pscustomobject]@{schema=1;runId=$s.RunId;stage='controller-prelaunch';failure=$A.value;remaining=$remaining.ToArray();automaticGlobalRollback=$false}
    $target=Join-Path $State.Evidence ('quarantine-'+[guid]::NewGuid().ToString('N')+'.json');Write-TransitionNewCRecord $target $record
    if($State.Held -and $State.OwnedNew.ContainsKey($s.History)){$history=Get-TransitionPhysicalObject $State $s.History $false;Assert-TransitionSame $State.OwnedNew[$s.History] $history;$q=$s.History+'\transition-quarantine.json';if(-not [IO.File]::Exists($q)){[Azurite.Stage.FileSecurity]::NewFile($q,[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TransitionJson $record)),$s.PrivateFileSddl);$null=Get-TransitionPhysicalObject $State $q $false}}
    return
   }
   default {throw "Unknown physical operation $Action"}
  }
 }
}
function Invoke-AzuriteStageTransitionPreparation {
 [CmdletBinding()]param([Parameter(Mandatory)][string]$SourceManifestPath,[Parameter(Mandatory)][string]$ExpectedSourceManifestSha256,[Parameter(Mandatory)][string]$ControlPath,[Parameter(Mandatory)][string]$ExpectedControlSha256,[Parameter(Mandatory)][string]$GatePath,[Parameter(Mandatory)][string]$ExpectedGateSha256,[Parameter(Mandatory)][string]$ExpectedPlanSha256,[string]$ApprovalPath,[string]$ExpectedApprovalSha256,[switch]$Apply,[string]$EvidenceDirectory)
 if($Apply){Assert-Transition (-not [string]::IsNullOrWhiteSpace($ApprovalPath) -and $ExpectedApprovalSha256 -cmatch '^[a-f0-9]{64}$') 'Apply requires separately trusted positive approval digest'}
 $prepared=Read-TransitionInputs $SourceManifestPath $ExpectedSourceManifestSha256 $ControlPath $ExpectedControlSha256 $GatePath $ExpectedGateSha256 $ExpectedPlanSha256 $ApprovalPath $ExpectedApprovalSha256 ([bool]$Apply)
 $spec=New-TransitionPhysicalSpec $prepared
 if(-not $Apply){return [pscustomobject]@{schema=1;mode='planning-only';runId=$spec.RunId;planSha256=$prepared.Plan.PlanSha256;existingObjects=$spec.Existing.Count;aclTargets=$spec.Grants.Count;newDirectories=$spec.Directories;newFiles=@($spec.Files.path)+@($spec.CacheFile);approvalPresent=($null -ne $prepared.Approval);willMutate=$false;willLaunch=$false;runtimeAllowed=$false}}
 Assert-Transition ($PSVersionTable.PSEdition -ceq 'Desktop' -and [IntPtr]::Size -eq 8) 'Apply requires 64-bit Windows PowerShell / .NET Framework'
 $evidence=[IO.Path]::GetFullPath($EvidenceDirectory);$allowed=[IO.Path]::GetFullPath((Join-Path $script:TransitionRepo 'artifacts\aa-fr-transition-preparation'))+'\';Assert-Transition ($evidence.StartsWith($allowed,[StringComparison]::Ordinal) -and -not [IO.Directory]::Exists($evidence) -and -not [IO.File]::Exists($evidence)) 'Apply evidence must be a fresh C controller artifact directory'
 [void][IO.Directory]::CreateDirectory($evidence)
 Assert-Transition ($null -eq ('Azurite.Stage.FileSecurity' -as [type]) -and $null -eq ('Azurite.Restricted.FullBootstrapIdentity' -as [type])) 'Apply must start in a fresh controller process without previously loaded helper types'
 # Neither loaded library has a type initializer that starts native work. Their
 # exact independently reviewed assembly bytes were verified before this point.
 [void][Reflection.Assembly]::Load($prepared.Source['FullRestrictedStageFileSecurity.dll'].bytes)
 [void][Reflection.Assembly]::Load($prepared.Source['RestrictedCliNative.dll'].bytes)
 $state=@{Spec=$spec;Evidence=$evidence;Journal=(Join-Path $evidence 'journal.jsonl');Locks=@{};OwnedNew=@{};Mutex=$null;Held=$false;HeldFromUtc=$null;HeldUntilUtc=$null;LeaseId=$null;ReleaseOutcome=$null}
 Write-TransitionNewCRecord (Join-Path $evidence 'apply-intent.json') ([pscustomobject]@{runId=$spec.RunId;sourceManifestSha256=$ExpectedSourceManifestSha256;planSha256=$ExpectedPlanSha256;approvalSha256=$ExpectedApprovalSha256;controlGateSha256=$ExpectedGateSha256;willLaunch=$false})
 $result=Invoke-TransitionCoordinator $spec (New-TransitionPhysicalBackend) $state
 if($result.Success){try{$receipts=New-TransitionReceiptBundle $spec $result $state;Write-TransitionNewCRecord (Join-Path $evidence 'receipt-bundle.json') $receipts;$result|Add-Member -NotePropertyName ReceiptBundle -NotePropertyValue $receipts}catch{$result.Success=$false;$result.Prepared=$false;$result.Error='Post-release receipt persistence/validation failed: '+$_.Exception.Message;$result.Quarantined=$true;Invoke-TransitionOp (New-TransitionPhysicalBackend) $state Quarantine @{value=[pscustomobject]@{runId=$spec.RunId;error=$result.Error;stage='post-release-receipt-failure'}}}}
 Write-TransitionNewCRecord (Join-Path $evidence 'preparation-result.json') $result
 return $result
}
Export-ModuleMember -Function New-AzuriteStageTransitionHelper,Invoke-AzuriteStageTransitionPreparation
