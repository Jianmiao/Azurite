// Additive fixed-F AA capabilities path. No arbitrary-target public overload.
using System.Security.Principal;using System.Security.AccessControl;using System.ComponentModel;using System.Globalization;using Microsoft.Win32.SafeHandles;using System.Security.Cryptography;using System.Web.Script.Serialization;using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Text;using System.Threading;using System.Threading.Tasks;
namespace Azurite.Restricted {
 public static class FullFileEvidence {
  [StructLayout(LayoutKind.Sequential)]struct Fi{public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IdHigh,IdLow;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr sa,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Fi info);
  public static Dictionary<string,object> Read(string path){using(var handle=CreateFileW(path,0x80000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero)){if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());Fi before;if(!GetFileInformationByHandle(handle,out before))throw new Win32Exception(Marshal.GetLastWin32Error());FullRestrictedContract.Require((before.Attributes&0x400)==0&&before.Links==1&&before.SizeHigh==0&&before.SizeLow<=4194304,"Log linked/reparse/oversized");string hash;using(var stream=new FileStream(handle,FileAccess.Read,4096,false))using(var sha=SHA256.Create()){hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();Fi after;if(!GetFileInformationByHandle(handle,out after))throw new Win32Exception(Marshal.GetLastWin32Error());FullRestrictedContract.Require(after.IdLow==before.IdLow&&after.IdHigh==before.IdHigh&&after.SizeLow==before.SizeLow&&after.Write.dwLowDateTime==before.Write.dwLowDateTime&&after.Write.dwHighDateTime==before.Write.dwHighDateTime,"Log changed during capture");return new Dictionary<string,object>{{"path",path},{"identity",before.Volume.ToString("x8")+":"+before.IdHigh.ToString("x8")+before.IdLow.ToString("x8")},{"bytes",before.SizeLow},{"sha256",hash},{"lastWriteUtc",DateTime.FromFileTimeUtc(((long)before.Write.dwHighDateTime<<32)|(uint)before.Write.dwLowDateTime).ToString("o")}};}}}
 }
 internal static class FullRestrictedContract {
  internal const string Lab=@"F:\AzuriteLabs\Editor-20261108-normal",Executable=Lab+@"\install\AzureArchive.exe";
  internal static void Require(bool okay,string error){if(!okay)throw new InvalidOperationException(error);}
  internal static void ValidateRequest(string runId,string runDirectory,string command,string executable,string arguments,string request,string gameAssemblyOverride){
   Require(System.Text.RegularExpressions.Regex.IsMatch(runId??"","^[a-f0-9]{32}$"),"Invalid controlled run identity");string root=Lab+@"\runs\restricted-"+runId;
   Require(runDirectory==root&&request==root+@"\request"&&command=="capabilities"&&executable==Executable,"Only fixed F capabilities transport is permitted");
   string log=root+@"\output\unity.log";Require(arguments=="-batchmode -nographics -disableaudio -logFile "+log||arguments=="-batchmode -nographics -disableaudio -logFile \""+log+"\"","Unexpected fixed native arguments");Require(String.IsNullOrEmpty(gameAssemblyOverride),"BEPINEX_GAME_ASSEMBLY_PATH must be absent");
  }
  internal static void ValidateMembers(int bootstrap,int primary,int[] actual){Require(bootstrap>0&&primary>0&&bootstrap!=primary&&actual.Length==2&&new HashSet<int>(actual).SetEquals(new[]{bootstrap,primary}),"Unexpected owned helper lineage or missing Job member");}
  internal static void ValidateHandleList(long[] expected,long[] actual){Require(expected.Length==3&&expected.All(x=>x>0)&&expected.Distinct().Count()==3&&actual.SequenceEqual(expected),"Only the three intended inheritable stream endpoints are permitted");}
  internal static bool CanReleaseDesktop(bool directExited,bool jobDrained){return directExited&&jobDrained;}
  internal static void ValidateBootstrap(ProcessStartInfo info,string runId,string controlHash){
   Require(System.Text.RegularExpressions.Regex.IsMatch(runId??"","^[a-f0-9]{32}$")&&System.Text.RegularExpressions.Regex.IsMatch(controlHash??"","^[a-f0-9]{64}$"),"Invalid reviewed bootstrap identity");
   string run=Lab+@"\runs\restricted-"+runId;
   string args="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+run+@"\tooling\RestrictedCliBootstrap.ps1 -ControlPath "+run+@"\tooling\control.json -ReviewedControlSha256 "+controlHash;
   Require(info.FileName==Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")&&info.Arguments==args&&info.WorkingDirectory==run,"Only fixed system PS5.1 and reviewed run control are permitted");
   Require(String.IsNullOrEmpty(info.UserName)&&String.IsNullOrEmpty(info.Domain)&&!info.UseShellExecute&&info.CreateNoWindow&&info.RedirectStandardOutput&&info.RedirectStandardError&&!info.LoadUserProfile,"Full bootstrap must retain the current original account without credentials");
  }
  internal static void ValidateHandleEvidence(FullHandleEvidence[] facts,long[] listed,long[] excluded){
   string[] roles={"stdin-child-reader","stdout-child-writer","stderr-child-writer","stdin-parent-writer","stdout-parent-reader","stderr-parent-reader"};
   Require(facts!=null&&facts.Length==6&&facts.Select(x=>x.Role).SequenceEqual(roles)&&facts.Select(x=>x.Handle).Distinct().Count()==6&&facts.All(x=>x.Handle>0&&x.FileType==3),"Standard pipe role/type/distinct-handle evidence mismatch");
   for(int i=0;i<facts.Length;i++)Require((facts[i].Flags&1)==(i<3?1u:0u),"Child inheritance or parent exclusion mismatch: "+facts[i].Role);
   ValidateHandleList(facts.Take(3).Select(x=>x.Handle).ToArray(),listed);
   Require(excluded!=null&&excluded.Length==4&&excluded.All(x=>x>0)&&!listed.Intersect(excluded).Any(),"Token/Job/desktop handle leaked into standard-handle list");
  }
 }
 internal sealed class FullHandleEvidence {internal string Role;internal long Handle;internal uint Flags,FileType;}
 internal sealed class FullOwnedHandle {
  internal readonly IntPtr Acquired;internal IntPtr Value;internal readonly string Role;internal bool Closed,Retained,CloseAttempted;internal int CloseError;internal string CloseException;readonly Func<IntPtr,int> close;
  static readonly List<FullOwnedHandle> retained=new List<FullOwnedHandle>();
  internal FullOwnedHandle(string role,IntPtr value,Func<IntPtr,int> release){FullRestrictedContract.Require(value!=IntPtr.Zero&&release!=null,"Owned reference needs actual acquisition");Role=role;Acquired=value;Value=value;close=release;}
  internal void Retain(){if(!Retained){Retained=true;lock(retained)retained.Add(this);}}
  internal bool Close(){if(CloseAttempted)return Closed;CloseAttempted=true;try{CloseError=close(Value);}catch(Exception e){CloseError=-1;CloseException=e.ToString();}if(CloseError!=0){Retain();return false;}Value=IntPtr.Zero;Closed=true;return true;}
 }
 internal sealed class FullRejectedReference:Exception {
  internal readonly FullOwnedHandle Owned;internal readonly Exception Primary;internal FullRejectedReference(string message,Exception primary,FullOwnedHandle owned):base(message+"; "+primary.Message+"; role="+owned.Role+"; acquired="+owned.Acquired.ToInt64().ToString("X")+"; close error="+owned.CloseError+"; retained="+owned.Retained+"; "+owned.CloseException,primary){Owned=owned;Primary=primary;}
 }
 internal sealed partial class JobObserver {
  FullOwnedHandle fullReference;
  [DllImport("kernel32.dll",EntryPoint="CloseHandle",SetLastError=true)]static extern bool FullCloseHandle(IntPtr value);
  static int CloseFullNative(IntPtr value){if(FullCloseHandle(value))return 0;int error=Marshal.GetLastWin32Error();return error==0?-1:error;}
  internal static JobObserver FromFullOwnedReference(IntPtr value,Func<IntPtr,int> close){return new JobObserver{handle=value,fullReference=new FullOwnedHandle("outer-job-observer",value,close)};}
  internal static JobObserver AcceptFull(Process bootstrap,long remoteHandle){
   IntPtr duplicate;bool duplicated=DuplicateHandle(bootstrap.Handle,new IntPtr(remoteHandle),GetCurrentProcess(),out duplicate,0x0004|0x0008,false,0);int error=duplicated?0:Marshal.GetLastWin32Error();if(duplicate==IntPtr.Zero)throw new Win32Exception(error,"Duplicate exact full Job");
   var observer=FromFullOwnedReference(duplicate,CloseFullNative);try{if(!duplicated)throw new Win32Exception(error,"Duplicate returned a partial Job reference");bool child,parent;if(!IsProcessInJob(bootstrap.Handle,observer.handle,out child)||!child||!IsProcessInJob(GetCurrentProcess(),observer.handle,out parent)||parent)throw new InvalidOperationException("Full Job ownership/controller exclusion failed");int[] ids=observer.ProcessIds();FullRestrictedContract.Require(ids.Length==1&&ids[0]==bootstrap.Id&&observer.ActiveProcesses()==1,"Full initial Job must contain bootstrap only");return observer;}catch(Exception e){observer.ReleaseFull();throw new FullRejectedReference("Rejected Job handoff",e,observer.fullReference);}
  }
  internal FullOwnedHandle ReleaseFull(){FullRestrictedContract.Require(fullReference!=null,"Full observer ownership is missing");fullReference.Close();handle=fullReference.Value;return fullReference;}
  internal FullOwnedHandle RetainFull(){FullRestrictedContract.Require(fullReference!=null,"Full observer ownership is missing");fullReference.Retain();return fullReference;}
 }
 internal static class FullProtocol {
  internal static string ReadLine(TextReader reader){var b=new StringBuilder();for(;;){int c=reader.Read();if(c<0)throw new EndOfStreamException("Protocol ended before acknowledgement");if(c=='\n')return b.ToString();if(c!='\r')b.Append((char)c);if(b.Length>512)throw new InvalidOperationException("Protocol line exceeds bound");}}
  internal static string ReadBounded(int milliseconds){var task=Task.Factory.StartNew(()=>ReadLine(Console.In),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);if(!task.Wait(Math.Max(1,Math.Min(milliseconds,5000))))throw new TimeoutException("Controller acknowledgement deadline");return task.Result;}
  internal static long JobHandle(string line,string runId){string prefix="AZURITE_JOB_V1 "+runId+" ";long value;FullRestrictedContract.Require(line!=null&&line.StartsWith(prefix,StringComparison.Ordinal)&&Int64.TryParse(line.Substring(prefix.Length),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)&&value>0,"Invalid full-mode Job handshake");return Int64.Parse(line.Substring(prefix.Length),NumberStyles.HexNumber,CultureInfo.InvariantCulture);}
  internal static long DesktopHandle(string line,string runId,string owner){string[] fields=(line??"").Split(' ');long value;FullRestrictedContract.Require(fields.Length==5&&fields[0]=="AZURITE_DESKTOP_V1"&&fields[1]==runId&&fields[3]=="AzuriteFullCli_"+runId&&fields[4]==owner&&Int64.TryParse(fields[2],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)&&value>0,"Invalid desktop lease identity or handle");return Int64.Parse(fields[2],NumberStyles.HexNumber,CultureInfo.InvariantCulture);}
 }
 public static class FullBootstrapIdentity {
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr value);
  public static Dictionary<string,object> Read(string runId){FullRestrictedContract.Require(System.Text.RegularExpressions.Regex.IsMatch(runId??"","^[a-f0-9]{32}$"),"Identity run id invalid");IntPtr token=IntPtr.Zero;Dictionary<string,object> actual;try{if(!FullRestrictedToken.OpenProcessToken(GetCurrentProcess(),8,out token))throw new Win32Exception(Marshal.GetLastWin32Error());actual=FullRestrictedToken.Read(token,new Dictionary<string,object>());FullRestrictedToken.ValidateOriginal(actual);}finally{if(token!=IntPtr.Zero&&!CloseHandle(token))throw new Win32Exception(Marshal.GetLastWin32Error(),"Close bootstrap identity query token");}
   string sid=(string)actual["user_sid"];bool hiveLoaded;using(var hive=Microsoft.Win32.Registry.Users.OpenSubKey(sid))hiveLoaded=hive!=null;
   string[] ordinary={"SeChangeNotifyPrivilege","SeShutdownPrivilege","SeUndockPrivilege","SeIncreaseWorkingSetPrivilege","SeTimeZonePrivilege"};
   var privileges=((object[])actual["privileges"]).Cast<Dictionary<string,object>>().Select(x=>(string)x["name"]).ToArray();
   FullRestrictedContract.Require(privileges.Length>0,"Bootstrap token privileges unavailable");
   return new Dictionary<string,object>{{"runId",runId},{"pid",Process.GetCurrentProcess().Id},{"status","passed"},{"identitySid",sid},{"isAdministrator",new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)},{"unexpectedPrivileges",privileges.Where(x=>!ordinary.Contains(x)).ToArray()},{"userProfile",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)},{"localAppData",Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)},{"localLow",KnownFolders.LocalLow()},{"environmentProfile",Environment.GetEnvironmentVariable("USERPROFILE")},{"environmentLocalAppData",Environment.GetEnvironmentVariable("LOCALAPPDATA")},{"environmentAppData",Environment.GetEnvironmentVariable("APPDATA")},{"environmentTemp",Environment.GetEnvironmentVariable("TEMP")},{"environmentTmp",Environment.GetEnvironmentVariable("TMP")},{"hiveLoaded",hiveLoaded},{"audioIsolationVerified",false},{"token",actual},{"helperChildCreated",false}};
  }
 }
 internal interface IFullRestrictedOperations {void ReadOriginalAndDerive();void AcquireDesktopLease();void CreateSuspended();void ValidateActualToken();void ValidateExactJob();void PrepareStreams();uint Resume();void Monitor();bool Cleanup();void Close();}
 internal sealed class FullRestrictedOutcome {public bool ResumeVerified,CleanupVerified;public int ResumeCalls;public string Error,CleanupError;public List<string> Stages=new List<string>();}
 internal static class FullRestrictedCoordinator {
  internal static FullRestrictedOutcome Run(IFullRestrictedOperations api){var r=new FullRestrictedOutcome();try{
   r.Stages.Add("original-token");api.ReadOriginalAndDerive();r.Stages.Add("desktop-lease");api.AcquireDesktopLease();r.Stages.Add("create-suspended");api.CreateSuspended();r.Stages.Add("actual-token");api.ValidateActualToken();r.Stages.Add("exact-job");api.ValidateExactJob();r.Stages.Add("streams");api.PrepareStreams();r.Stages.Add("resume");r.ResumeCalls++;uint previous=api.Resume();FullRestrictedContract.Require(previous==1,"ResumeThread previous count must be exactly one; actual="+previous);r.ResumeVerified=true;r.Stages.Add("monitor");api.Monitor();
  }catch(Exception e){r.Error=e.ToString();}finally{try{r.CleanupVerified=api.Cleanup();}catch(Exception e){r.CleanupError=e.ToString();}try{api.Close();}catch(Exception e){r.CleanupVerified=false;r.CleanupError=(r.CleanupError??"")+e;}}return r;}
 }
 internal sealed class FullOuterDrainResult {internal bool DirectExited,JobDrained,WindowViolation,StreamsComplete,DesktopClosed,DesktopRetained;internal int Remaining=-1;internal string Error;internal bool Verified{get{return DirectExited&&JobDrained&&StreamsComplete&&DesktopClosed&&Error==null;}}}
 internal interface IFullOuterDrain {
  long Milliseconds{get;}bool DirectExited();int ActiveProcesses();bool WindowViolation();bool FinishStreams(int remaining);void CloseDesktop();void RetainDesktop();void Pause(int ms);
 }
 internal static class FullOuterDrain {
  internal static FullOuterDrainResult Run(IFullOuterDrain api,int limit){var r=new FullOuterDrainResult();long start=api.Milliseconds;try{
   for(;;){r.DirectExited=api.DirectExited();r.Remaining=api.ActiveProcesses();FullRestrictedContract.Require(r.Remaining>=0,"Negative Job membership");r.JobDrained=r.Remaining==0;
    try{if(api.WindowViolation())r.WindowViolation=true;}catch(Exception e){r.Error="Owned desktop monitor failed during cleanup: "+e.Message;}
    if(r.DirectExited&&r.JobDrained)break;if(api.Milliseconds-start>=limit)break;api.Pause(10);
   }
   r.StreamsComplete=api.FinishStreams((int)Math.Max(0,limit-(api.Milliseconds-start)));
   if(FullRestrictedContract.CanReleaseDesktop(r.DirectExited,r.JobDrained)){api.CloseDesktop();r.DesktopClosed=true;}else{api.RetainDesktop();r.DesktopRetained=true;}
   if(!r.DirectExited||!r.JobDrained||!r.StreamsComplete)r.Error=(r.Error??"")+" Owned process/Job/stream drain incomplete";
  }catch(Exception e){r.Error=(r.Error??"")+e.ToString();try{api.RetainDesktop();r.DesktopRetained=true;}catch(Exception keep){r.Error+="; lease retention failed: "+keep.Message;}}return r;}
 }
 internal sealed class FullOuterDrainAdapter:IFullOuterDrain {
  readonly Stopwatch watch=Stopwatch.StartNew();readonly Process process;readonly bool started;readonly JobObserver job;readonly FullDesktopLease desktop;readonly StrictCapture output,error;
  internal FullOuterDrainAdapter(Process p,bool created,JobObserver j,FullDesktopLease d,StrictCapture stdout,StrictCapture stderr){process=p;started=created;job=j;desktop=d;output=stdout;error=stderr;}
  public long Milliseconds{get{return watch.ElapsedMilliseconds;}}public bool DirectExited(){return !started||process.HasExited;}public int ActiveProcesses(){FullRestrictedContract.Require(job!=null,"Exact Job observer missing");return job.ActiveProcesses();}
  public bool WindowViolation(){FullRestrictedContract.Require(desktop!=null,"Outer desktop lease missing");return desktop.HasViolation(job.ProcessIds());}
  public bool FinishStreams(int remaining){var timer=Stopwatch.StartNew();bool a=output.Finish(remaining);bool b=error.Finish((int)Math.Max(0,remaining-timer.ElapsedMilliseconds));return a&&b&&output.Error==null&&error.Error==null;}
  public void CloseDesktop(){FullRestrictedContract.Require(desktop!=null,"Desktop lease unavailable");desktop.Finish(true,true);}public void RetainDesktop(){if(desktop!=null)desktop.Finish(false,false);}public void Pause(int ms){Thread.Sleep(ms);}
 }

 public sealed partial class OwnedJob {
  bool fullLeaseAcknowledged;
  internal IntPtr FullHandle{get{return handle;}}
  public void AnnounceAndAwaitControllerFull(string runId,int milliseconds){FullRestrictedContract.Require(System.Text.RegularExpressions.Regex.IsMatch(runId??"","^[a-f0-9]{32}$"),"Invalid full Job correlation");Console.Out.WriteLine("AZURITE_JOB_V1 "+runId+" "+handle.ToInt64().ToString("X"));Console.Out.Flush();FullRestrictedContract.Require(FullProtocol.ReadBounded(milliseconds)=="AZURITE_JOB_ACK "+runId,"Owned Job acknowledgement differs");}
  public void TerminateFull(uint code){if(!TerminateJobObject(handle,code))throw new Win32Exception(Marshal.GetLastWin32Error(),"Terminate exact full Job");throw new InvalidOperationException("TerminateJobObject unexpectedly returned to the terminating bootstrap");}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool inside);
  internal void VerifyFullChild(IntPtr process,int pid){bool inside;if(!IsProcessInJob(process,handle,out inside)||!inside)throw new InvalidOperationException("Suspended AA not in exact bootstrap-owned Job");FullRestrictedContract.ValidateMembers(Process.GetCurrentProcess().Id,pid,ProcessIds());}
  internal void TransferDesktop(string runId,IntPtr desktop,string name,string owner,int timeoutMs){
   if(fullLeaseAcknowledged)throw new InvalidOperationException("Desktop lease already transferred");
   Console.Out.WriteLine("AZURITE_DESKTOP_V1 "+runId+" "+desktop.ToInt64().ToString("X")+" "+name+" "+owner);Console.Out.Flush();
   if(FullProtocol.ReadBounded(timeoutMs)!="AZURITE_DESKTOP_ACK "+runId+" "+name)throw new InvalidOperationException("Outer private desktop lease not acknowledged within deadline");fullLeaseAcknowledged=true;
  }
 }
 internal sealed class FullDesktopLease {
  FullOwnedHandle owned;IntPtr handle{get{return owned.Value;}}internal string Name;internal bool Closed,Retained;static readonly List<FullDesktopLease> quarantine=new List<FullDesktopLease>();
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr targetProcess,out IntPtr target,uint access,bool inherit,uint options);
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  [DllImport("user32.dll",SetLastError=true)] static extern bool CloseDesktop(IntPtr desktop);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetUserObjectInformationW(IntPtr handle,int index,StringBuilder name,uint bytes,out uint needed);
  [DllImport("advapi32.dll")] static extern uint GetSecurityInfo(IntPtr handle,int type,uint information,out IntPtr owner,out IntPtr group,out IntPtr dacl,out IntPtr sacl,out IntPtr descriptor);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
  static int NativeClose(IntPtr value){if(CloseDesktop(value))return 0;int error=Marshal.GetLastWin32Error();return error==0?-1:error;}
  internal static FullDesktopLease AcceptOwnedDuplicate(IntPtr duplicate,string expected,Action<IntPtr> validate,Func<IntPtr,int> close){
   var lease=new FullDesktopLease{owned=new FullOwnedHandle("outer-private-desktop",duplicate,close)};
   try{validate(duplicate);lease.Name=expected;return lease;}catch(Exception e){lease.owned.Close();if(lease.owned.Retained){lease.Retained=true;lock(quarantine)quarantine.Add(lease);}throw new FullRejectedReference("Rejected desktop handoff",e,lease.owned);}
  }
  internal static FullDesktopLease Accept(Process bootstrap,string runId,string line){long remote=FullProtocol.DesktopHandle(line,runId,WindowsIdentity.GetCurrent().User.Value);string[] fields=line.Split(' ');string expected="AzuriteFullCli_"+runId;IntPtr duplicate;
   bool duplicated=DuplicateHandle(bootstrap.Handle,new IntPtr(remote),GetCurrentProcess(),out duplicate,0,false,2);int duplicateError=duplicated?0:Marshal.GetLastWin32Error();if(duplicate==IntPtr.Zero)throw new Win32Exception(duplicateError,"Duplicate owned bootstrap desktop");
   return AcceptOwnedDuplicate(duplicate,expected,value=>{if(!duplicated)throw new Win32Exception(duplicateError,"Duplicate returned a partial desktop reference");uint n;var name=new StringBuilder(256);if(!GetUserObjectInformationW(value,2,name,512,out n))throw new Win32Exception(Marshal.GetLastWin32Error());FullRestrictedContract.Require(name.ToString()==expected,"Actual duplicate desktop name mismatch");IntPtr owner,group,dacl,sacl,sd;uint status=GetSecurityInfo(value,7,1,out owner,out group,out dacl,out sacl,out sd);if(status!=0)throw new Win32Exception((int)status);try{FullRestrictedContract.Require(owner!=IntPtr.Zero&&new SecurityIdentifier(owner).Value==fields[4],"Actual desktop owner mismatch");}finally{if(sd!=IntPtr.Zero&&LocalFree(sd)!=IntPtr.Zero)throw new InvalidOperationException("Free desktop ownership descriptor failed");}},NativeClose);
  }
  internal bool HasViolation(int[] ids){return WindowProbe.HasVisibleOnDesktop(handle,ids);}
  internal void Finish(bool directExited,bool jobDrained){if(!FullRestrictedContract.CanReleaseDesktop(directExited,jobDrained)){owned.Retain();if(!Retained){Retained=true;lock(quarantine)quarantine.Add(this);}return;}if(!owned.Close()){Retained=true;lock(quarantine)quarantine.Add(this);throw new Win32Exception(owned.CloseError,"Close outer owned desktop lease");}Closed=true;}
 }
 public static partial class WindowProbe {
  [DllImport("user32.dll",SetLastError=true)] static extern bool EnumDesktopWindows(IntPtr desktop,EnumProc callback,IntPtr parameter);
  [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId",SetLastError=true)] static extern uint FullWindowThreadProcessId(IntPtr hwnd,out uint pid);
  internal static uint RequireWindowOwner(uint thread,uint pid,int error){if(thread==0||pid==0)throw new Win32Exception(error,"Window owner query failed; absence is not proven");return pid;}
  static uint FullWindowOwner(IntPtr hwnd){uint pid;uint tid=FullWindowThreadProcessId(hwnd,out pid);int error=tid==0?Marshal.GetLastWin32Error():0;return RequireWindowOwner(tid,pid,error);}
  internal static bool HasVisibleOnDesktop(IntPtr desktop,int[] ids){FullRestrictedContract.Require(desktop!=IntPtr.Zero,"Private desktop ownership missing");var owned=new HashSet<int>(ids);bool visible=false;Exception failure=null;EnumProc callback=delegate(IntPtr hwnd,IntPtr parameter){try{uint pid=FullWindowOwner(hwnd);if(owned.Contains((int)pid)&&IsWindowVisible(hwnd))visible=true;return true;}catch(Exception e){failure=e;return false;}};bool ok=EnumDesktopWindows(desktop,callback,IntPtr.Zero);int error=ok?0:Marshal.GetLastWin32Error();if(failure!=null)throw failure;if(!ok)throw new Win32Exception(error,"Private desktop window enumeration failed");IntPtr foreground=GetForegroundWindow();return visible||(foreground!=IntPtr.Zero&&owned.Contains((int)FullWindowOwner(foreground)));}
 }
 public sealed class FullRestrictedPumpResult {
  public PumpResult Process;public Dictionary<string,object> Evidence;public bool StreamsComplete,DesktopLeaseAcknowledged,NativeHandlesClosed;public string CleanupError;
 }
 internal sealed class StrictCapture {
  readonly object gate=new object();readonly StringBuilder text=new StringBuilder();readonly int limit;internal volatile bool Overflow,Complete,Cancelled;internal string Error;internal StreamReader Reader;internal Task Task;
  internal StrictCapture(int max){limit=max;}
  internal void Start(StreamReader reader,Action<string> protocol=null){Reader=reader;Task=System.Threading.Tasks.Task.Factory.StartNew(()=>{try{if(protocol!=null){for(int i=0;i<2;i++){var line=new StringBuilder();int c;while((c=reader.Read())>=0&&c!='\n'){if(c!='\r')line.Append((char)c);if(line.Length>512)throw new InvalidOperationException("Protocol line exceeds bound");}if(c<0)throw new IOException("Protocol ended before desktop lease");protocol(line.ToString());lock(gate)text.Append(line).Append('\n');}}char[] buffer=new char[2048];int n;while((n=reader.Read(buffer,0,buffer.Length))>0){lock(gate){int keep=Math.Min(n,Math.Max(0,limit-text.Length));text.Append(buffer,0,keep);if(keep<n)Overflow=true;}}Complete=true;}catch(Exception e){if(!Cancelled)Error=e.ToString();}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
  internal string Snapshot(){lock(gate)return text.ToString();}
  internal bool Finish(int ms){if(Task==null)return true;if(Task.Wait(Math.Max(0,ms)))return Complete&&Error==null;Cancelled=true;return false;}
 }
 internal interface IFullPipeRead {int Read(byte[] bytes,out int read);}
 internal sealed class FullNativePipeRead:IFullPipeRead {
  readonly IntPtr handle;internal FullNativePipeRead(IntPtr value){handle=value;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadFile(IntPtr handle,byte[] bytes,uint count,out uint read,IntPtr overlap);
  public int Read(byte[] bytes,out int count){uint read;bool okay=ReadFile(handle,bytes,(uint)bytes.Length,out read,IntPtr.Zero);int error=okay?0:Marshal.GetLastWin32Error();count=checked((int)read);return error;}
 }
 // Blocking OS reads live only on a dedicated background worker. The lifecycle
 // thread never calls ReadFile or closes a reader until its worker has joined.
 internal sealed class BoundedPipeCapture {
  readonly object gate=new object();readonly StringBuilder text=new StringBuilder();readonly Decoder decoder=new UTF8Encoding(false,true).GetDecoder();readonly int limit;internal IntPtr Handle;internal volatile bool Complete,Overflow,Finished;internal string Error;Task task;
  static readonly List<BoundedPipeCapture> retained=new List<BoundedPipeCapture>();
  internal BoundedPipeCapture(int n){limit=n;}
  void Decode(byte[] bytes,int count,bool flush){char[] chars=new char[Encoding.UTF8.GetMaxCharCount(count)];int n=decoder.GetChars(bytes,0,count,chars,0,flush);lock(gate){int keep=Math.Min(n,Math.Max(0,limit-text.Length));text.Append(chars,0,keep);if(keep<n)Overflow=true;}}
  internal void Drain(IFullPipeRead pipe){try{byte[] bytes=new byte[4096];for(;;){int count;int error=pipe.Read(bytes,out count);FullRestrictedContract.Require(count>=0&&count<=bytes.Length,"Pipe read count outside buffer");if(error==109){FullRestrictedContract.Require(count==0,"Broken pipe returned unexpected data");Decode(bytes,0,true);Complete=true;return;}if(error!=0)throw new Win32Exception(error,"Owned standard pipe read failed");FullRestrictedContract.Require(count>0,"Unexpected zero-byte successful pipe read");Decode(bytes,count,false);}}catch(Exception e){Error=e.ToString();}finally{Finished=true;}}
  internal void Start(){FullRestrictedContract.Require(Handle!=IntPtr.Zero,"Reader requires its unique owned handle");Start(new FullNativePipeRead(Handle));}
  internal void Start(IFullPipeRead pipe){FullRestrictedContract.Require(task==null&&pipe!=null,"Reader starts exactly once");task=Task.Factory.StartNew(()=>Drain(pipe),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
  internal bool Finish(int milliseconds){if(task==null)return true;return task.Wait(Math.Max(0,milliseconds))&&Complete&&Error==null;}
  internal bool CanClose{get{return task==null||Finished;}}
  internal void Retain(){lock(retained)retained.Add(this);}
  internal string Snapshot(){lock(gate)return text.ToString();}
 }
 public static partial class ProcessPump {
  static readonly List<object> fullQuarantineOwners=new List<object>();
  internal static void RecordFullRejectedReference(PumpResult result,FullRejectedReference failure){result.CleanupVerified=false;result.CleanupError=(result.CleanupError??"")+" "+failure.Message;if(failure.Owned.Role=="outer-private-desktop")result.DesktopLeaseRetained=failure.Owned.Retained;if(failure.Owned.Role=="outer-job-observer"){result.FullJobObserverRetained=failure.Owned.Retained;result.FullJobObserverCloseError=failure.Owned.CloseError;}}
  internal static void FinalizeFullObserver(PumpResult result,JobObserver observer){
   if(observer==null){result.CleanupVerified=false;return;}
   FullOwnedHandle released=result.DirectProcessExited&&result.JobDrained?observer.ReleaseFull():observer.RetainFull();result.FullJobObserverClosed=released.Closed;result.FullJobObserverRetained=released.Retained;result.FullJobObserverCloseError=released.CloseError;
   if(!released.Closed){result.CleanupVerified=false;result.CleanupError=(result.CleanupError??"")+" Job observer retained; CloseHandle error="+released.CloseError+"; "+released.CloseException;lock(fullQuarantineOwners)fullQuarantineOwners.Add(observer);}
  }
  public static FullRestrictedPumpResult RunFullRestrictedCapabilities(ProcessStartInfo info,int timeoutMs,OwnedJob job,string runId,string runDirectory,string argumentsSha256,int maxChars,string[] watchedPaths,long maxFileBytes){
   FullRestrictedContract.Require(job!=null&&timeoutMs>=1&&timeoutMs<=300000&&maxChars==262144&&maxFileBytes==4194304,"Invalid fixed full-restricted monitor limits");
   FullRestrictedContract.ValidateRequest(runId,runDirectory,"capabilities",info.FileName,info.Arguments,info.EnvironmentVariables["AZUREARCHIVE_CLI_DIRECTORY"],info.EnvironmentVariables["BEPINEX_GAME_ASSEMBLY_PATH"]);
   FullRestrictedContract.Require(info.WorkingDirectory==FullRestrictedContract.Lab+@"\install"&&String.IsNullOrEmpty(info.UserName)&&!info.UseShellExecute&&info.CreateNoWindow&&info.RedirectStandardOutput&&info.RedirectStandardError,"Unexpected native startup/credential contract");
   var api=new FullRestrictedSpawn(info,timeoutMs,job,runId,runDirectory,argumentsSha256,maxChars,watchedPaths,maxFileBytes);var outcome=FullRestrictedCoordinator.Run(api);api.Result.Process.Error=outcome.Error;api.Result.Process.CleanupError=outcome.CleanupError;api.Result.Process.CleanupVerified=outcome.CleanupVerified&&api.Result.NativeHandlesClosed;api.Result.Evidence["coordinatorStages"]=outcome.Stages;api.Result.Evidence["resumeCalls"]=outcome.ResumeCalls;api.Result.Evidence["resumeVerified"]=outcome.ResumeVerified;api.Result.CleanupError=outcome.CleanupError;return api.Result;
  }
  public static PumpResult RunBootstrapFullRestricted(ProcessStartInfo info,int timeoutMs,string runId,string reviewedControlSha256,int maxChars,string[] watchedPaths,long maxFileBytes){
   FullRestrictedContract.ValidateBootstrap(info,runId,reviewedControlSha256);FullRestrictedContract.Require(timeoutMs>0&&timeoutMs<=300000&&maxChars==262144&&maxFileBytes==4194304,"Invalid full bootstrap monitor limits");info.RedirectStandardInput=true;
   info.StandardOutputEncoding=new UTF8Encoding(false,true);info.StandardErrorEncoding=new UTF8Encoding(false,true);
   var result=new PumpResult{RunId=runId,StartedUtc=DateTime.UtcNow,ExitCode=-1,FullRestrictedMode=true};var timer=Stopwatch.StartNew();var output=new StrictCapture(maxChars);var error=new StrictCapture(maxChars);var process=new Process{StartInfo=info};JobObserver observer=null;FullDesktopLease desktop=null;object gate=new object();int protocolStage=0;string protocolError=null;bool started=false,protocolStopped=false;
   try{FullRestrictedContract.Require(process.Start(),"Bootstrap did not start");started=true;result.Pid=process.Id;
    output.Start(process.StandardOutput,line=>{lock(gate){try{FullRestrictedContract.Require(!protocolStopped,"Protocol arrived after cleanup began");if(protocolStage==0){long raw=FullProtocol.JobHandle(line,runId);observer=JobObserver.AcceptFull(process,raw);process.StandardInput.WriteLine("AZURITE_JOB_ACK "+runId);process.StandardInput.Flush();protocolStage=1;}else if(protocolStage==1){FullRestrictedContract.Require(observer!=null,"Job not accepted before desktop");desktop=FullDesktopLease.Accept(process,runId,line);result.DesktopLeaseObserved=true;process.StandardInput.WriteLine("AZURITE_DESKTOP_ACK "+runId+" "+desktop.Name);process.StandardInput.Flush();process.StandardInput.Close();protocolStage=2;}else throw new InvalidOperationException("Repeated protocol transfer");}catch(Exception e){var rejected=e as FullRejectedReference;if(rejected!=null)RecordFullRejectedReference(result,rejected);protocolError=e.ToString();throw;}}});error.Start(process.StandardError);
    for(;;){lock(gate){if(protocolError!=null)throw new InvalidOperationException(protocolError);if(observer!=null){int[] ids=observer.ProcessIds();result.JobObserved=true;result.WindowViolation=desktop!=null?desktop.HasViolation(ids):WindowProbe.HasVisible(ids);}}
     if(output.Error!=null||error.Error!=null)throw new IOException(output.Error??error.Error);result.OutputLimitExceeded=output.Overflow||error.Overflow||FilesExceeded(watchedPaths,maxFileBytes);if(result.WindowViolation||result.OutputLimitExceeded)break;
     if(process.HasExited&&output.Complete&&error.Complete){result.ExitCode=process.ExitCode;break;}if(timer.ElapsedMilliseconds>=timeoutMs){result.TimedOut=true;break;}Thread.Sleep(20);}
   }catch(Exception e){result.Error=e.ToString();result.NoProcessCreated=!started;}
   finally{var cleanup=Stopwatch.StartNew();try{lock(gate){protocolStopped=true;if(observer!=null&&observer.ActiveProcesses()>0)observer.Terminate(result.TimedOut?124u:1u);}if(started&&!process.HasExited)process.Kill();
     JobObserver finalObserver;FullDesktopLease finalDesktop;lock(gate){finalObserver=observer;finalDesktop=desktop;}
     FullOuterDrainResult drain=FullOuterDrain.Run(new FullOuterDrainAdapter(process,started,finalObserver,finalDesktop,output,error),5000);
     result.DirectProcessExited=drain.DirectExited;result.JobDrained=drain.JobDrained;result.RemainingOwnedProcesses=drain.Remaining;result.WindowViolation=result.WindowViolation||drain.WindowViolation;result.StreamCaptureComplete=drain.StreamsComplete;result.DesktopLeaseClosed=drain.DesktopClosed;result.DesktopLeaseRetained=result.DesktopLeaseRetained||drain.DesktopRetained;result.CleanupVerified=drain.Verified;result.CleanupError=(result.CleanupError??String.Empty)+(drain.Error??String.Empty);
     if(started&&process.HasExited)result.ExitCode=process.ExitCode;
    }catch(Exception e){result.CleanupError=e.ToString();result.CleanupVerified=false;lock(gate){if(desktop!=null&&!desktop.Closed){desktop.Finish(false,false);result.DesktopLeaseRetained=true;}}}
    finally{FinalizeFullObserver(result,observer);result.CleanupMilliseconds=cleanup.ElapsedMilliseconds;result.ElapsedMilliseconds=timer.ElapsedMilliseconds;result.Stdout=output.Snapshot();result.Stderr=error.Snapshot();if(output.Complete&&error.Complete)process.Dispose();else lock(fullQuarantineOwners){fullQuarantineOwners.Add(process);fullQuarantineOwners.Add(output);fullQuarantineOwners.Add(error);}}}
   return result;
  }
 }
 internal interface IFullProcessLifetime {bool Exited(IntPtr process);int Terminate(IntPtr process,uint code);uint Wait(IntPtr process,uint milliseconds);uint ExitCode(IntPtr process);}
 internal sealed class FullRestrictedSpawn:IFullRestrictedOperations {
  static readonly List<FullRestrictedSpawn> retainedOwners=new List<FullRestrictedSpawn>();
  internal readonly FullRestrictedPumpResult Result=new FullRestrictedPumpResult{Process=new PumpResult{ExitCode=-1},Evidence=new Dictionary<string,object>()};
  readonly ProcessStartInfo info;readonly int timeout,max;readonly OwnedJob job;readonly string runId,runDir,argHash;readonly string[] watched;readonly long fileLimit;readonly Stopwatch clock=Stopwatch.StartNew();readonly IFullProcessLifetime lifetime;readonly Func<IntPtr,int> releaseHandle;
  IntPtr original,derived,desktop,desktopSd,processSd,attributes,handleArray,environment,stdinRead,stdinWrite,stdoutRead,stdoutWrite,stderrRead,stderrWrite;Pi pi;bool childCreated,resumed,attributesInitialized;Dictionary<string,object> originalSnapshot;readonly BoundedPipeCapture output,error;readonly List<Dictionary<string,object>> closes=new List<Dictionary<string,object>>();readonly Dictionary<long,int> failedCloses=new Dictionary<long,int>();
  [StructLayout(LayoutKind.Sequential)] struct Sa {public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Si {public int cb;public string reserved,desktop,title;public int x,y,xsize,ysize,xc,yc,fill,flags;public short show,reservedBytes;public IntPtr reservedPointer,input,output,error;}
  [StructLayout(LayoutKind.Sequential)] struct Six {public Si Startup;public IntPtr Attributes;}
  [StructLayout(LayoutKind.Sequential)] struct Pi {public IntPtr Process,Thread;public uint Pid,Tid;}
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool CreatePipe(out IntPtr read,out IntPtr write,ref Sa attributes,uint size);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetHandleInformation(IntPtr handle,out uint flags);
  [DllImport("kernel32.dll",SetLastError=true)] static extern uint GetFileType(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref IntPtr bytes);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr bytes,IntPtr previous,IntPtr returned);
  [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessAsUserW(IntPtr token,string application,StringBuilder command,ref Sa process,ref Sa thread,bool inherit,uint flags,IntPtr environment,string cwd,ref Six startup,out Pi pi);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text,uint revision,out IntPtr descriptor,out uint size);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktopW(string name,IntPtr device,IntPtr mode,uint flags,uint access,ref Sa attributes);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenDesktopW(string name,uint flags,bool inherit,uint access);
  [DllImport("user32.dll",EntryPoint="GetUserObjectInformationW",SetLastError=true)] static extern bool FullDesktopFlags(IntPtr desktop,int index,out UserFlags flags,uint bytes,out uint needed);
  [StructLayout(LayoutKind.Sequential)]struct UserFlags{public int Inherit,Reserved;public uint Flags;}
  [DllImport("user32.dll",SetLastError=true)] static extern bool CloseDesktop(IntPtr desktop);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
  [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint ms);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
  internal FullRestrictedSpawn(ProcessStartInfo input,int milliseconds,OwnedJob owned,string id,string directory,string hash,int chars,string[] paths,long bytes,IFullProcessLifetime processLifetime=null,Func<IntPtr,int> closeHandle=null){info=input;timeout=milliseconds;job=owned;runId=id;runDir=directory;argHash=hash;max=chars;watched=paths;fileLimit=bytes;lifetime=processLifetime??new NativeLifetime();releaseHandle=closeHandle??NativeRelease;output=new BoundedPipeCapture(chars);error=new BoundedPipeCapture(chars);Result.Process.RunId=id;Result.Process.NoProcessCreated=true;Result.Process.StartedUtc=DateTime.UtcNow;Result.Evidence["closeResults"]=closes;Result.Evidence["mode"]="CurrentAccountFullRestrictedCapabilities";}
  static int NativeRelease(IntPtr value){if(CloseHandle(value))return 0;int e=Marshal.GetLastWin32Error();return e==0?-1:e;}
  sealed class NativeLifetime:IFullProcessLifetime {
   public bool Exited(IntPtr process){uint wait=WaitForSingleObject(process,0);if(wait==UInt32.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error(),"Wait owned AA handle");return wait==0;}
   public int Terminate(IntPtr process,uint code){if(TerminateProcess(process,code))return 0;int e=Marshal.GetLastWin32Error();return e==0?-1:e;}
   public uint Wait(IntPtr process,uint ms){return WaitForSingleObject(process,ms);}
   public uint ExitCode(IntPtr process){uint code;Win(GetExitCodeProcess(process,out code),"Read owned AA exit");return code;}
  }
  static void Win(bool okay,string label){if(!okay)throw new Win32Exception(Marshal.GetLastWin32Error(),label);}
  static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  int Remaining(){int remaining=timeout-(int)clock.ElapsedMilliseconds;FullRestrictedContract.Require(remaining>0,"Shared native deadline exhausted");return remaining;}
  Dictionary<string,object> Snapshot(IntPtr token,string label){var r=new Dictionary<string,object>();Result.Evidence[label]=r;try{return FullRestrictedToken.Read(token,r);}catch(Exception e){r["read_error"]=e.ToString();throw;}}
  public void ReadOriginalAndDerive(){
   Remaining();byte[] raw=File.ReadAllBytes(Path.Combine(runDir,"request","arguments.json"));FullRestrictedContract.Require(raw.Length<=4096&&Hash(raw)==argHash.ToLowerInvariant(),"Capabilities arguments pin differs");var json=new JavaScriptSerializer();var args=json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(raw));var argv=args["argv"] as System.Collections.IList;FullRestrictedContract.Require(args.Count==2&&argv!=null&&argv.Count==1&&(string)argv[0]=="capabilities"&&(string)args["cwd"]==runDir+@"\workspace","Fixed business transport differs");
   Win(FullRestrictedToken.OpenProcessToken(GetCurrentProcess(),0x8b,out original),"Open original bootstrap token");originalSnapshot=Snapshot(original,"originalToken");FullRestrictedToken.ValidateOriginal(originalSnapshot);derived=FullRestrictedToken.Derive(original,originalSnapshot);FullRestrictedToken.ValidateChild(Snapshot(derived,"derivedBeforeDefault"),originalSnapshot);FullRestrictedToken.ConfigureDefault(derived,originalSnapshot);FullRestrictedToken.ValidateChild(Snapshot(derived,"derivedToken"),originalSnapshot);
  }
  public void AcquireDesktopLease(){string user=(string)originalSnapshot["user_sid"],logon=(string)originalSnapshot["logon_sid"];uint size;Win(ConvertStringSecurityDescriptorToSecurityDescriptorW("O:"+user+"D:P(A;;0xF01FF;;;"+user+")(A;;0xF01FF;;;"+logon+")",1,out desktopSd,out size),"Private desktop descriptor");var sa=new Sa{Length=Marshal.SizeOf(typeof(Sa)),Descriptor=desktopSd,Inherit=false};string name="AzuriteFullCli_"+runId;
   IntPtr existing=OpenDesktopW(name,0,false,1);int openError=existing==IntPtr.Zero?Marshal.GetLastWin32Error():0;Result.Evidence["desktopBeforeCreateError"]=openError;if(existing!=IntPtr.Zero){bool closed=CloseDesktop(existing);Result.Evidence["existingDesktopReferenceClosed"]=closed;throw new InvalidOperationException("Run desktop already exists; refused");}FullRestrictedContract.Require(openError==2,"Desktop absence not established; error="+openError);
   desktop=CreateDesktopW(name,IntPtr.Zero,IntPtr.Zero,0,0xF01FF,ref sa);Win(desktop!=IntPtr.Zero,"Create fixed private desktop");UserFlags flags;uint needed;Win(FullDesktopFlags(desktop,1,out flags,(uint)Marshal.SizeOf(typeof(UserFlags)),out needed),"Read created desktop inheritance");FullRestrictedContract.Require(flags.Inherit==0&&needed==(uint)Marshal.SizeOf(typeof(UserFlags)),"Private desktop must not be inherited");Result.Evidence["privateDesktop"]=name;Result.Evidence["desktopInherit"]=false;job.TransferDesktop(runId,desktop,name,user,Remaining());Result.DesktopLeaseAcknowledged=true;Result.Evidence["outerDesktopLeaseAcknowledged"]=true;}
  void Pipe(out IntPtr read,out IntPtr write){var sa=new Sa{Length=Marshal.SizeOf(typeof(Sa)),Inherit=true};Win(CreatePipe(out read,out write,ref sa,4096),"Create owned standard-stream pipe");}
  void Close(ref IntPtr handle,string label){if(handle==IntPtr.Zero)return;IntPtr value=handle;int previous;if(failedCloses.TryGetValue(value.ToInt64(),out previous))throw new Win32Exception(previous,"Retained previous failed close: "+label);int e;try{e=releaseHandle(value);}catch(Exception failure){failedCloses[value.ToInt64()]=-1;throw new InvalidOperationException("Owned close threw; reference retained: "+label,failure);}bool okay=e==0;closes.Add(new Dictionary<string,object>{{"role",label},{"handle",value.ToInt64()},{"ok",okay},{"error",e}});if(!okay){failedCloses[value.ToInt64()]=e;throw new Win32Exception(e,"Close "+label);}handle=IntPtr.Zero;}
  internal void RecordCreationResult(bool created,int errorCode,IntPtr process,IntPtr thread,uint pid,uint tid){
   pi=new Pi{Process=process,Thread=thread,Pid=pid,Tid=tid};Result.Evidence["created"]=created;Result.Evidence["createError"]=errorCode;Result.Evidence["partialProcessHandle"]=pi.Process.ToInt64();Result.Evidence["partialThreadHandle"]=pi.Thread.ToInt64();childCreated=created||pi.Process!=IntPtr.Zero||pi.Thread!=IntPtr.Zero||pi.Pid!=0;Result.Process.NoProcessCreated=!childCreated;Result.Process.Pid=checked((int)pi.Pid);Result.Evidence["threadId"]=pi.Tid;if(!created)throw new Win32Exception(errorCode,"Create fixed suspended AA");FullRestrictedContract.Require(pi.Process!=IntPtr.Zero&&pi.Thread!=IntPtr.Zero&&pi.Pid>0,"Native create returned incomplete owned handles");
  }
  internal bool CleanupDirectProcess(uint remaining){
   if(!childCreated){Result.Process.DirectProcessExited=true;return true;}FullRestrictedContract.Require(pi.Process!=IntPtr.Zero,"Partial create cannot prove direct process absence");
   if(!lifetime.Exited(pi.Process)){int error=lifetime.Terminate(pi.Process,Result.Process.TimedOut?124u:1u);Result.Evidence["directTermination"]=new{ok=error==0,error=error,handle=pi.Process.ToInt64()};if(error!=0)throw new Win32Exception(error,"Terminate owned AA handle");}
   uint wait=lifetime.Wait(pi.Process,remaining);Result.Evidence["directWaitResult"]=wait;Result.Process.DirectProcessExited=wait==0;if(wait==0)Result.Process.ExitCode=unchecked((int)lifetime.ExitCode(pi.Process));return wait==0;
  }
  FullHandleEvidence PipeEvidence(string role,IntPtr handle){uint flags;Win(GetHandleInformation(handle,out flags),"Query "+role+" inheritance");uint type=GetFileType(handle);FullRestrictedContract.Require(type==3,"Expected native pipe handle: "+role);return new FullHandleEvidence{Role=role,Handle=handle.ToInt64(),Flags=flags,FileType=type};}
  void VerifyExcluded(IntPtr handle,string role){uint flags;Win(GetHandleInformation(handle,out flags),"Query excluded "+role);FullRestrictedContract.Require((flags&1)==0,"Sensitive handle unexpectedly inheritable: "+role);}
  public void CreateSuspended(){
   Remaining();Pipe(out stdinRead,out stdinWrite);Pipe(out stdoutRead,out stdoutWrite);Pipe(out stderrRead,out stderrWrite);Win(SetHandleInformation(stdoutRead,1,0),"Exclude parent stdout reader");Win(SetHandleInformation(stderrRead,1,0),"Exclude parent stderr reader");Win(SetHandleInformation(stdinWrite,1,0),"Exclude parent stdin writer");
   var facts=new[]{PipeEvidence("stdin-child-reader",stdinRead),PipeEvidence("stdout-child-writer",stdoutWrite),PipeEvidence("stderr-child-writer",stderrWrite),PipeEvidence("stdin-parent-writer",stdinWrite),PipeEvidence("stdout-parent-reader",stdoutRead),PipeEvidence("stderr-parent-reader",stderrRead)};
   VerifyExcluded(original,"original-token");VerifyExcluded(derived,"derived-token");VerifyExcluded(job.FullHandle,"owned-Job");long[] endpoints={stdinRead.ToInt64(),stdoutWrite.ToInt64(),stderrWrite.ToInt64()};Result.Evidence["pipeHandlesBeforeCreate"]=facts.Select(x=>new{role=x.Role,handle=x.Handle,flags=x.Flags,fileType=x.FileType}).ToArray();
   IntPtr bytes=IntPtr.Zero;bool probed=InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref bytes);int probeError=probed?0:Marshal.GetLastWin32Error();FullRestrictedContract.Require(!probed&&probeError==122&&bytes.ToInt64()>0&&bytes.ToInt64()<65536,"Handle-list attribute size probe invalid");attributes=Marshal.AllocHGlobal(bytes);Win(InitializeProcThreadAttributeList(attributes,1,0,ref bytes),"Initialize handle-list attributes");attributesInitialized=true;handleArray=Marshal.AllocHGlobal(3*IntPtr.Size);for(int i=0;i<3;i++)Marshal.WriteIntPtr(handleArray,i*IntPtr.Size,new IntPtr(endpoints[i]));
   var actual=new long[3];for(int i=0;i<3;i++)actual[i]=Marshal.ReadIntPtr(handleArray,i*IntPtr.Size).ToInt64();FullRestrictedContract.ValidateHandleEvidence(facts,actual,new[]{original.ToInt64(),derived.ToInt64(),job.FullHandle.ToInt64(),desktop.ToInt64()});Win(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20002),handleArray,new IntPtr(3*IntPtr.Size),IntPtr.Zero,IntPtr.Zero),"Set exact standard-handle inheritance list");Close(ref stdinWrite,"stdin-parent-EOF");
   var entries=info.EnvironmentVariables.Keys.Cast<string>().OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(x=>x+"="+info.EnvironmentVariables[x]);environment=Marshal.StringToHGlobalUni(String.Join("\0",entries)+"\0\0");string user=(string)originalSnapshot["user_sid"],logon=(string)originalSnapshot["logon_sid"];uint sdSize;Win(ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;"+user+")(A;;GA;;;"+logon+")(A;;GA;;;SY)",1,out processSd,out sdSize),"Private process/thread descriptor");var psa=new Sa{Length=Marshal.SizeOf(typeof(Sa)),Descriptor=processSd,Inherit=false};var start=new Six{Startup=new Si{cb=Marshal.SizeOf(typeof(Six)),desktop="WinSta0\\AzuriteFullCli_"+runId,flags=0x101,show=0,input=stdinRead,output=stdoutWrite,error=stderrWrite},Attributes=attributes};
   const uint creationFlags=0x08084404;Result.Evidence["creationFlags"]=creationFlags;bool created=CreateProcessAsUserW(derived,FullRestrictedContract.Executable,new StringBuilder("\""+FullRestrictedContract.Executable+"\" "+info.Arguments),ref psa,ref psa,true,creationFlags,environment,info.WorkingDirectory,ref start,out pi);int errorCode=created?0:Marshal.GetLastWin32Error();RecordCreationResult(created,errorCode,pi.Process,pi.Thread,pi.Pid,pi.Tid);Close(ref stdinRead,"stdin-child-copy");Close(ref stdoutWrite,"stdout-child-copy");Close(ref stderrWrite,"stderr-child-copy");
  }
  public void ValidateActualToken(){IntPtr child=IntPtr.Zero;try{Win(FullRestrictedToken.OpenProcessToken(pi.Process,8,out child),"Query actual suspended AA token");FullRestrictedToken.ValidateChild(Snapshot(child,"suspendedChildToken"),originalSnapshot);Result.Evidence["actualTokenVerified"]=true;}finally{Close(ref child,"suspended-token-query");}}
  public void ValidateExactJob(){job.VerifyFullChild(pi.Process,(int)pi.Pid);Result.Evidence["exactOwnedJobVerified"]=true;}
  public void PrepareStreams(){output.Handle=stdoutRead;error.Handle=stderrRead;output.Start();error.Start();Result.Evidence["streamsPreparedBeforeResume"]=true;}
  public uint Resume(){Remaining();uint value=ResumeThread(pi.Thread);Result.Evidence["resumePreviousCount"]=value;Result.Evidence["resumeError"]=value==UInt32.MaxValue?Marshal.GetLastWin32Error():0;resumed=value!=UInt32.MaxValue;return value;}
  bool Exited(){return lifetime.Exited(pi.Process);}
  bool OutputExceeded(){long total=0;foreach(string path in watched){var files=Directory.Exists(path)?Directory.GetFiles(path):new[]{path};if(files.Length>64)return true;foreach(string file in files)if(File.Exists(file)){var stat=new FileInfo(file);if((stat.Attributes&FileAttributes.ReparsePoint)!=0||stat.Length>fileLimit)return true;total+=stat.Length;}}return total>4*fileLimit;}
  public void Monitor(){for(;;){int[] members=job.ProcessIds();bool exited=Exited();if(!exited)FullRestrictedContract.ValidateMembers(Process.GetCurrentProcess().Id,(int)pi.Pid,members);else FullRestrictedContract.Require(members.All(x=>x==Process.GetCurrentProcess().Id||x==(int)pi.Pid),"Unexpected remaining helper lineage");Result.Process.WindowViolation=WindowProbe.HasVisibleOnDesktop(desktop,members);Result.Process.OutputLimitExceeded=output.Overflow||error.Overflow||OutputExceeded();if(output.Error!=null||error.Error!=null)throw new IOException(output.Error??error.Error);if(Result.Process.WindowViolation||Result.Process.OutputLimitExceeded)throw new InvalidOperationException("Owned window/output limit violation");if(exited&&output.Complete&&error.Complete){uint code;Win(GetExitCodeProcess(pi.Process,out code),"Read owned AA exit");Result.Process.ExitCode=unchecked((int)code);return;}if(clock.ElapsedMilliseconds>=timeout){Result.Process.TimedOut=true;throw new TimeoutException("AA deadline reached");}Thread.Sleep(20);}}
  public bool Cleanup(){var timer=Stopwatch.StartNew();bool direct=!childCreated;Close(ref stdinRead,"cleanup-stdin-child-copy");Close(ref stdinWrite,"cleanup-stdin-parent-copy");Close(ref stdoutWrite,"cleanup-stdout-child-copy");Close(ref stderrWrite,"cleanup-stderr-child-copy");
   direct=CleanupDirectProcess((uint)Math.Max(0,5000-timer.ElapsedMilliseconds));
   Result.Process.DirectProcessExited=direct;int[] members=job.ProcessIds();bool noDescendants=members.Length==1&&members[0]==Process.GetCurrentProcess().Id;Result.Evidence["bootstrapOnlyBeforeOuterDrain"]=noDescendants;Result.Process.RemainingOwnedProcesses=members.Length;bool a=output.Finish((int)Math.Max(0,5000-timer.ElapsedMilliseconds)),b=error.Finish((int)Math.Max(0,5000-timer.ElapsedMilliseconds));bool streams=!childCreated||(a&&b&&output.Complete&&error.Complete);Result.StreamsComplete=streams;Result.Process.Stdout=output.Snapshot();Result.Process.Stderr=error.Snapshot();Result.Process.ElapsedMilliseconds=clock.ElapsedMilliseconds;Result.Process.CleanupMilliseconds=timer.ElapsedMilliseconds;Result.Evidence["resumeAttemptedMayHaveRun"]=resumed;Result.Evidence["streamsComplete"]=streams;Result.Evidence["stdoutReadError"]=output.Error;Result.Evidence["stderrReadError"]=error.Error;
   if(desktop!=IntPtr.Zero&&WindowProbe.HasVisibleOnDesktop(desktop,members))Result.Process.WindowViolation=true;return direct&&noDescendants&&streams;
  }
  public void Close(){var failures=new List<string>();foreach(string role in new[]{"stdinRead","stdinWrite","stdoutRead","stdoutWrite","stderrRead","stderrWrite","thread","process","derived","original"}){try{switch(role){case "stdinRead":Close(ref stdinRead,role);break;case "stdinWrite":Close(ref stdinWrite,role);break;case "stdoutRead":if(output.CanClose)Close(ref stdoutRead,role);else{output.Retain();failures.Add("stdout worker not joined; reader ownership retained");}break;case "stdoutWrite":Close(ref stdoutWrite,role);break;case "stderrRead":if(error.CanClose)Close(ref stderrRead,role);else{error.Retain();failures.Add("stderr worker not joined; reader ownership retained");}break;case "stderrWrite":Close(ref stderrWrite,role);break;case "thread":Close(ref pi.Thread,role);break;case "process":Close(ref pi.Process,role);break;case "derived":Close(ref derived,role);break;case "original":try{if(original!=IntPtr.Zero&&originalSnapshot!=null){var after=Snapshot(original,"originalTokenAfter");FullRestrictedContract.Require(new JavaScriptSerializer().Serialize(after)==new JavaScriptSerializer().Serialize(originalSnapshot),"Original token changed");}}finally{Close(ref original,role);}break;}}catch(Exception e){failures.Add(e.Message);}}
   if(attributes!=IntPtr.Zero){if(attributesInitialized)DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);}if(handleArray!=IntPtr.Zero)Marshal.FreeHGlobal(handleArray);if(environment!=IntPtr.Zero)Marshal.FreeHGlobal(environment);foreach(IntPtr ptr in new[]{desktopSd,processSd})if(ptr!=IntPtr.Zero&&LocalFree(ptr)!=IntPtr.Zero)failures.Add("Descriptor free failed");
   if(desktop!=IntPtr.Zero){bool closed=CloseDesktop(desktop);Result.Evidence["bootstrapDesktopReferenceClosed"]=closed;if(!closed)failures.Add("Bootstrap desktop reference close failed: "+Marshal.GetLastWin32Error());else desktop=IntPtr.Zero;}Result.NativeHandlesClosed=failures.Count==0;Result.Evidence["closeErrors"]=failures;Result.Evidence["retainedProcessHandle"]=pi.Process.ToInt64();Result.Evidence["retainedThreadHandle"]=pi.Thread.ToInt64();if(failures.Count!=0){lock(retainedOwners)retainedOwners.Add(this);throw new InvalidOperationException(String.Join("; ",failures));}
  }
 }
 internal static class FullRestrictedToken {
  const string CompatibilitySid="S-1-1-0",UsersSid="S-1-5-32-545";const int TokenIsAppContainer=29,TokenIntegrityLevel=25,TokenMandatoryPolicy=27;
  [StructLayout(LayoutKind.Sequential)] internal struct SidAttribute {public IntPtr Sid;public uint Attributes;}
  [DllImport("advapi32.dll",SetLastError=true)] internal static extern bool OpenProcessToken(IntPtr process,uint rights,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int cls,IntPtr buffer,uint length,out uint needed);
  [DllImport("advapi32.dll",EntryPoint="GetTokenInformation",SetLastError=true)] static extern bool GetTokenScalar(IntPtr token,int cls,out uint value,uint length,out uint needed);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool CreateRestrictedToken(IntPtr original,uint flags,uint disabled,IntPtr sids,uint deleted,IntPtr privileges,uint count,[In]SidAttribute[] restricted,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetTokenInformation(IntPtr token,int cls,IntPtr value,uint length);
  [DllImport("advapi32.dll")] static extern bool IsTokenRestricted(IntPtr token);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool LookupPrivilegeNameW(string system,ref long luid,StringBuilder name,ref uint size);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text,uint revision,out IntPtr descriptor,out uint size);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetSecurityDescriptorDacl(IntPtr descriptor,out bool present,out IntPtr acl,out bool defaulted);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
  static void Check(bool b,string error){FullRestrictedContract.Require(b,error);}
  static void Win(bool b,string error){if(!b)throw new Win32Exception(Marshal.GetLastWin32Error(),error);}
  static string S(Dictionary<string,object> d,string key){object o;Check(d.TryGetValue(key,out o)&&o is string,"Missing string "+key);return (string)o;}
  static bool B(Dictionary<string,object> d,string key){object o;Check(d.TryGetValue(key,out o)&&o is bool,"Missing bool "+key);return (bool)o;}
  static long N(Dictionary<string,object> d,string key){object o;Check(d.TryGetValue(key,out o)&&o!=null,"Missing integer "+key);return Convert.ToInt64(o,CultureInfo.InvariantCulture);}
  static Dictionary<string,object> Obj(object value){return (Dictionary<string,object>)value;}
    static string QueryDetails(int cls,string phase,uint capacity,bool ok,int error,uint needed){return String.Format(System.Globalization.CultureInfo.InvariantCulture,"GetTokenInformation validation failed: class={0}; phase={1}; buffer_bytes={2}; api_success={3}; error_meaningful={4}; win32_error={5}; needed_bytes={6}",cls,phase,capacity,ok,!ok,ok?"inapplicable":error.ToString(System.Globalization.CultureInfo.InvariantCulture),needed);}
    static void ValidateVariableProbe(int cls,bool ok,int error,uint needed){Check(!ok&&error==122&&needed>0&&needed<=65536,QueryDetails(cls,"size-probe",0,ok,error,needed));}
    static void ValidateScalarRead(int cls,bool ok,int error,uint needed){Check(ok&&needed==4,QueryDetails(cls,"scalar-read",4,ok,error,needed));}
    static IntPtr Info(IntPtr token,int cls){
        uint n;bool ok=GetTokenInformation(token,cls,IntPtr.Zero,0,out n);int e=ok?0:Marshal.GetLastWin32Error();ValidateVariableProbe(cls,ok,e,n);
        uint capacity=n;IntPtr p=Marshal.AllocHGlobal((int)capacity);try{ok=GetTokenInformation(token,cls,p,capacity,out n);e=ok?0:Marshal.GetLastWin32Error();Check(ok,QueryDetails(cls,"buffer-read",capacity,ok,e,n));return p;}catch{Marshal.FreeHGlobal(p);throw;}
    }
    static int TokenInt(IntPtr token,int cls){Check(cls==20||cls==29||cls==8||cls==12,"Fixed4 scalar reader does not support class="+cls);uint value,n;bool ok=GetTokenScalar(token,cls,out value,4,out n);int e=ok?0:Marshal.GetLastWin32Error();ValidateScalarRead(cls,ok,e,n);return unchecked((int)value);}
    static List<Dictionary<string,object>> Groups(IntPtr token,int cls) { IntPtr p=Info(token,cls);try{int count=Marshal.ReadInt32(p),offset=IntPtr.Size,step=Marshal.SizeOf(typeof(SidAttribute));Check(count>=0&&count<256,"Group count");var r=new List<Dictionary<string,object>>();for(int i=0;i<count;i++){var a=(SidAttribute)Marshal.PtrToStructure(IntPtr.Add(p,offset+i*step),typeof(SidAttribute));r.Add(new Dictionary<string,object>{{"sid",new SecurityIdentifier(a.Sid).Value},{"attributes",a.Attributes}});}return r;}finally{Marshal.FreeHGlobal(p);} }
    static string DefaultDacl(IntPtr token) { IntPtr p=Info(token,6);try{IntPtr a=Marshal.ReadIntPtr(p);Check(a!=IntPtr.Zero,"Null token default DACL");int size=(ushort)Marshal.ReadInt16(a,2);byte[] b=new byte[size];Marshal.Copy(a,b,0,size);return new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent,null,null,null,new RawAcl(b,0)).GetSddlForm(AccessControlSections.Access);}finally{Marshal.FreeHGlobal(p);} }
    static Dictionary<string,object> Token(IntPtr token,Dictionary<string,object> capture=null) {
        var r=capture??new Dictionary<string,object>();IntPtr p=Info(token,1);try{r["user_sid"]=new SecurityIdentifier(Marshal.ReadIntPtr(p)).Value;}finally{Marshal.FreeHGlobal(p);}r["is_restricted"]=IsTokenRestricted(token);r["elevated"]=TokenInt(token,20)!=0;r["app_container_raw"]=TokenInt(token,TokenIsAppContainer);r["is_app_container"]=(int)r["app_container_raw"]!=0;r["token_type"]=TokenInt(token,8);r["session_id"]=TokenInt(token,12);r["default_dacl"]=DefaultDacl(token);
        var gs=Groups(token,2);var users=gs.Where(x=>S(x,"sid")==UsersSid).ToArray();r["users_group_count"]=users.Length;r["users_group_attributes"]=users.Length==1?users[0]["attributes"]:(object)0;r["users_enabled"]=users.Length==1&&(N(users[0],"attributes")&4)!=0&&(N(users[0],"attributes")&16)==0;var logons=gs.Where(x=>(Convert.ToUInt32(x["attributes"])&0xC0000000)==0xC0000000).ToArray();Check(logons.Length==1,"Exactly one actual Logon SID");r["logon_sid"]=logons[0]["sid"];r["restricting_sids"]=Groups(token,11).Select(x=>(string)x["sid"]).ToArray();
        p=Info(token,3);try{int c=Marshal.ReadInt32(p);Check(c>=0&&c<128,"Privilege bound");var list=new List<object>();for(int i=0;i<c;i++){long luid=Marshal.ReadInt64(p,4+i*12);uint size=128;var b=new StringBuilder(128);Win(LookupPrivilegeNameW(null,ref luid,b,ref size),"Privilege name");list.Add(new Dictionary<string,object>{{"name",b.ToString()},{"attributes",(uint)Marshal.ReadInt32(p,12+i*12)}});}r["privileges"]=list.ToArray();}finally{Marshal.FreeHGlobal(p);}ReadTokenIntegrity(token,r);return r;
    }
    static string[] RestrictingSids(string logon) { return new[]{logon,CompatibilitySid,UsersSid}; }
    static void ExactRestrictingSet(IEnumerable<string> actual,string logon) {
        var values=actual.ToArray();Check(values.Length==3&&values.All(x=>x!=null)&&values.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(RestrictingSids(logon).OrderBy(x=>x,StringComparer.Ordinal)),"Exact Logon plus World plus Users SID set required");
    }
    static bool NotAppContainer(Dictionary<string,object> token) { return !B(token,"is_app_container")&&N(token,"app_container_raw")==0; }
    static void RestrictedTokenIdentityMatches(Dictionary<string,object> t,string user,string sid,long session) {
        Check(S(t,"user_sid")==user&&S(t,"logon_sid")==sid&&B(t,"is_restricted")&&!B(t,"elevated")&&NotAppContainer(t)&&N(t,"token_type")==1&&N(t,"session_id")==session,"Actual restricted primary token mismatch");var rs=(IEnumerable<string>)t["restricting_sids"];ExactRestrictingSet(rs,sid);var privileges=(object[])t["privileges"];Check(privileges.Length==1&&S(Obj(privileges[0]),"name")=="SeChangeNotifyPrivilege"&&(N(Obj(privileges[0]),"attributes")&2)!=0,"Unexpected or disabled traversal privilege");
    }
    static IntPtr Descriptor(string sddl) { IntPtr p;uint size;Win(ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl,1,out p,out size),"Build fresh object DACL");return p; }
    static string ObjectSddl(string user,string selected) { return "D:P(A;;GA;;;"+user+")(A;;GA;;;"+selected+")(A;;GA;;;SY)"; }
    static void DerivedDefault(IntPtr derived,string user,string selected) { IntPtr sd=Descriptor(ObjectSddl(user,selected)),p=IntPtr.Zero;try{bool present,def;IntPtr acl;Win(GetSecurityDescriptorDacl(sd,out present,out acl,out def),"Read fresh DACL");Check(present&&acl!=IntPtr.Zero,"Missing fresh DACL");p=Marshal.AllocHGlobal(IntPtr.Size);Marshal.WriteIntPtr(p,acl);Win(SetTokenInformation(derived,6,p,(uint)IntPtr.Size),"Set derived-token default DACL only");}finally{if(p!=IntPtr.Zero)Marshal.FreeHGlobal(p);Check(LocalFree(sd)==IntPtr.Zero,"Free descriptor");} }

    static Dictionary<string,object> ParseIntegrityBuffer(byte[] data,long address,uint capacity,uint needed) {
        Check(IntPtr.Size==8&&capacity==28&&needed==28&&data!=null&&data.Length==28,"TokenIntegrityLevel requires exact AMD64 28-byte label/SID shape");
        Check(address>0&&address<=Int64.MaxValue-28&&BitConverter.ToInt64(data,0)==address+16,"Integrity SID pointer outside exact returned buffer");
        uint attributes=BitConverter.ToUInt32(data,8);Check(attributes==0x60,"Integrity attributes must be INTEGRITY|INTEGRITY_ENABLED");
        Check(data[16]==1&&data[17]==1&&data.Skip(18).Take(5).All(x=>x==0)&&data[23]==16,"Integrity SID requires revision1/count1/mandatory authority16");
        uint rid=BitConverter.ToUInt32(data,24);
        return new Dictionary<string,object>{{"integrity_sid","S-1-16-"+rid},{"integrity_rid",rid},{"integrity_attributes",attributes}};
    }
    static void ReadTokenIntegrity(IntPtr token,Dictionary<string,object> result) {
        uint needed;bool ok=GetTokenInformation(token,TokenIntegrityLevel,IntPtr.Zero,0,out needed);int error=ok?0:Marshal.GetLastWin32Error();
        ValidateVariableProbe(TokenIntegrityLevel,ok,error,needed);Check(needed==28,QueryDetails(TokenIntegrityLevel,"size-probe-exact-shape",0,ok,error,needed));
        IntPtr buffer=Marshal.AllocHGlobal(28);try{
            ok=GetTokenInformation(token,TokenIntegrityLevel,buffer,28,out needed);error=ok?0:Marshal.GetLastWin32Error();
            Check(ok&&needed==28,QueryDetails(TokenIntegrityLevel,"buffer-read",28,ok,error,needed));
            var bytes=new byte[28];Marshal.Copy(buffer,bytes,0,28);
            foreach(var entry in ParseIntegrityBuffer(bytes,buffer.ToInt64(),28,needed))result[entry.Key]=entry.Value;
            result["integrity_query"]=new Dictionary<string,object>{{"class",25},{"phase","buffer-read"},{"buffer_bytes",28},{"needed_bytes",needed},{"api_success",true},{"win32_error",0}};
        }finally{Marshal.FreeHGlobal(buffer);}
        uint policy;ok=GetTokenScalar(token,TokenMandatoryPolicy,out policy,4,out needed);error=ok?0:Marshal.GetLastWin32Error();ValidateScalarRead(TokenMandatoryPolicy,ok,error,needed);
        Check((policy&~3u)==0,"Unknown TokenMandatoryPolicy bits: "+policy);result["mandatory_policy"]=policy;
        result["mandatory_policy_query"]=new Dictionary<string,object>{{"class",27},{"phase","scalar-read"},{"buffer_bytes",4},{"needed_bytes",needed},{"api_success",true},{"win32_error",0}};
    }

    static void RequireUsers(Dictionary<string,object> t){long a=N(t,"users_group_attributes");Check(N(t,"users_group_count")==1&&B(t,"users_enabled")&&(a&4)!=0&&(a&16)==0,"Original/child enabled Users membership required");}
    static void RequireInheritedPolicy(Dictionary<string,object> t,Dictionary<string,object> original){foreach(string k in new[]{"integrity_rid","integrity_attributes","mandatory_policy"})Check(N(t,k)==N(original,k),"Inherited token field drift: "+k);Check(S(t,"integrity_sid")==S(original,"integrity_sid"),"Inherited integrity SID drift");}
    static void RestrictedTokenMatches(Dictionary<string,object> t,string user,string sid,long session){RestrictedTokenIdentityMatches(t,user,sid,session);RequireUsers(t);Check(N(t,"integrity_attributes")==96&&(N(t,"mandatory_policy")&~3L)==0,"Malformed inherited token policy");}

  internal static Dictionary<string,object> Read(IntPtr token,Dictionary<string,object> snapshot){return Token(token,snapshot);}
  internal static void ValidateOriginal(Dictionary<string,object> original){RequireUsers(original);Check(!B(original,"elevated")&&!B(original,"is_restricted")&&NotAppContainer(original)&&N(original,"token_type")==1,"Original token must be unelevated unrestricted current primary");}
  internal static void ValidateChild(Dictionary<string,object> child,Dictionary<string,object> original){RestrictedTokenMatches(child,S(original,"user_sid"),S(original,"logon_sid"),N(original,"session_id"));RequireInheritedPolicy(child,original);}
  internal static IntPtr Derive(IntPtr original,Dictionary<string,object> snapshot){ValidateOriginal(snapshot);string[] texts={S(snapshot,"logon_sid"),CompatibilitySid,UsersSid};var allocations=new List<IntPtr>();try{var sidArray=new SidAttribute[3];for(int i=0;i<3;i++){var sid=new SecurityIdentifier(texts[i]);var bytes=new byte[sid.BinaryLength];sid.GetBinaryForm(bytes,0);IntPtr memory=Marshal.AllocHGlobal(bytes.Length);allocations.Add(memory);Marshal.Copy(bytes,0,memory,bytes.Length);sidArray[i]=new SidAttribute{Sid=memory,Attributes=0};}IntPtr derived;Win(CreateRestrictedToken(original,1,0,IntPtr.Zero,0,IntPtr.Zero,3,sidArray,out derived),"Create exact FR1 derived token");return derived;}finally{foreach(IntPtr p in allocations)Marshal.FreeHGlobal(p);}}
  internal static void ConfigureDefault(IntPtr derived,Dictionary<string,object> original){DerivedDefault(derived,S(original,"user_sid"),S(original,"logon_sid"));}
 }
}
