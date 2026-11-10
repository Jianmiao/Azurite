// Windows-only process containment shared by the production bootstrap and harmless fixtures.
// Compile under Windows PowerShell 5.1 as well as PowerShell 7. No target libraries are loaded.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Azurite.Restricted {
 public static class KnownFolders {
  [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
  public static string LocalLow(){var id=new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");IntPtr p;int result=SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out p);if(result!=0)Marshal.ThrowExceptionForHR(result);try{return Marshal.PtrToStringUni(p);}finally{Marshal.FreeCoTaskMem(p);}}
 }
 public sealed partial class OwnedJob {
  private IntPtr handle;
  [StructLayout(LayoutKind.Sequential)] struct BasicLimit { public long PerProcess,PerJob;public uint Flags;public UIntPtr Min,Max;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling; }
  [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
  [StructLayout(LayoutKind.Sequential)] struct ExtendedLimit { public BasicLimit Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int cls,ref ExtendedLimit info,uint size);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int cls,IntPtr buffer,uint length,out uint returned);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint exitCode);
  private OwnedJob() {}
  // Only a fresh bootstrap process may call this: never call it in the controller.
  public static OwnedJob CreateAndJoinCurrent() {
   var job=new OwnedJob();job.handle=CreateJobObject(IntPtr.Zero,null);
   if(job.handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create unnamed job");
   try {
    if(!SetHandleInformation(job.handle,1,0))throw new Win32Exception(Marshal.GetLastWin32Error());
    var limits=new ExtendedLimit();limits.Basic.Flags=0x2000|0x20;limits.Basic.Priority=0x4000;
    if(!SetInformationJobObject(job.handle,9,ref limits,(uint)Marshal.SizeOf(typeof(ExtendedLimit))))throw new Win32Exception(Marshal.GetLastWin32Error());
    if(!AssignProcessToJobObject(job.handle,Process.GetCurrentProcess().Handle))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot isolate bootstrap in owned job");
    return job;
   } catch {CloseHandle(job.handle);job.handle=IntPtr.Zero;throw;}
  }
  public int[] ProcessIds() {
   const int capacity=4096;int size=8+capacity*IntPtr.Size;IntPtr p=Marshal.AllocHGlobal(size);
   try {
    uint returned;if(!QueryInformationJobObject(handle,3,p,(uint)size,out returned))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot enumerate owned job");
    int assigned=Marshal.ReadInt32(p),count=Marshal.ReadInt32(p,4);
    if(count<0||count>capacity||assigned>capacity)throw new InvalidOperationException("Owned job exceeds bounded process count");
    int[] result=new int[count];for(int i=0;i<count;i++)result[i]=Marshal.ReadIntPtr(p,8+i*IntPtr.Size).ToInt32();return result;
   } finally {Marshal.FreeHGlobal(p);}
  }
  public bool Contains(int pid){foreach(int p in ProcessIds())if(p==pid)return true;return false;}
  public bool HasVisibleWindow(){return WindowProbe.HasVisible(ProcessIds());}
  public void AnnounceAndAwaitController(string runId) {
   if(!System.Text.RegularExpressions.Regex.IsMatch(runId,"^[a-f0-9]{32}$"))throw new ArgumentException("Invalid job handshake correlation");
   Console.Out.WriteLine("AZURITE_JOB_V1 "+runId+" "+handle.ToInt64().ToString("X"));Console.Out.Flush();
   // No identity-helper/native children before the controller verifies and ACKs this job.
   if(Console.In.ReadLine()!="AZURITE_JOB_ACK "+runId)throw new InvalidOperationException("Owned-job handshake was not acknowledged");
  }
  // Result files and streams must be flushed BEFORE this call. It intentionally kills self.
  public void Terminate(uint code){if(!TerminateJobObject(handle,code))throw new Win32Exception(Marshal.GetLastWin32Error());Thread.Sleep(Timeout.Infinite);}
 }
 internal sealed partial class JobObserver : IDisposable {
  IntPtr handle;
  [StructLayout(LayoutKind.Sequential)] struct Accounting {public long User,Kernel,PeriodUser,PeriodKernel;public uint PageFaults,Total,Active,Terminated;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr targetProcess,out IntPtr target,uint access,bool inherit,uint options);
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool inside);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int cls,IntPtr buffer,uint length,out uint returned);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int cls,out Accounting info,uint length,out uint returned);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  public static JobObserver Accept(Process bootstrap,long remoteHandle) {
   var observer=new JobObserver();
   // The source process HANDLE came from our own Start, never from a PID lookup.
   if(!DuplicateHandle(bootstrap.Handle,new IntPtr(remoteHandle),GetCurrentProcess(),out observer.handle,0x0004|0x0008,false,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot obtain owned-job query/terminate observer");
   try {
    bool child,parent;
    if(!IsProcessInJob(bootstrap.Handle,observer.handle,out child)||!child)throw new InvalidOperationException("Handshake job does not contain the owned bootstrap handle");
    if(!IsProcessInJob(GetCurrentProcess(),observer.handle,out parent)||parent)throw new InvalidOperationException("Handshake job contains controller or membership cannot be verified");
    int[] members=observer.ProcessIds();
    if(members.Length!=1||members[0]!=bootstrap.Id||observer.ActiveProcesses()!=1)throw new InvalidOperationException("Handshake job must contain exactly the pre-child bootstrap");
    return observer;
   } catch {observer.Dispose();throw;}
  }
  public int[] ProcessIds(){
   const int count=4096;int size=8+count*IntPtr.Size;IntPtr p=Marshal.AllocHGlobal(size);
   try{uint returned;if(!QueryInformationJobObject(handle,3,p,(uint)size,out returned))throw new Win32Exception(Marshal.GetLastWin32Error());int assigned=Marshal.ReadInt32(p),n=Marshal.ReadInt32(p,4);if(n<0||n>count||assigned>count)throw new InvalidOperationException("Owned job exceeds bounded membership");int[] ids=new int[n];for(int i=0;i<n;i++)ids[i]=Marshal.ReadIntPtr(p,8+i*IntPtr.Size).ToInt32();return ids;}finally{Marshal.FreeHGlobal(p);}
  }
  public int ActiveProcesses(){Accounting info;uint returned;if(!QueryInformationJobObject(handle,1,out info,(uint)Marshal.SizeOf(typeof(Accounting)),out returned))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot confirm owned-job drain");return checked((int)info.Active);}
  public bool HasVisibleWindow(){return WindowProbe.HasVisible(ProcessIds());}
  public void Terminate(uint code){if(!TerminateJobObject(handle,code))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot terminate exact owned job");}
  public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
 }
 public static partial class WindowProbe {
  delegate bool EnumProc(IntPtr hwnd,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  public static bool HasVisible(int[] ids) {
   var owned=new HashSet<int>(ids);bool found=false;
   EnumProc cb=delegate(IntPtr hwnd,IntPtr parameter){uint pid;GetWindowThreadProcessId(hwnd,out pid);if(owned.Contains((int)pid)&&IsWindowVisible(hwnd))found=true;return true;};
   if(!EnumWindows(cb,IntPtr.Zero))throw new InvalidOperationException("Owned-window enumeration failed");
   uint fg;GetWindowThreadProcessId(GetForegroundWindow(),out fg);return found||owned.Contains((int)fg);
  }
 }
 public sealed class PumpResult {
  public int Pid,ExitCode;public bool TimedOut,WindowViolation,OutputLimitExceeded;
  public DateTime StartedUtc;public long ElapsedMilliseconds;public string Stdout,Stderr,Error;
  public bool JobObserved,DirectProcessExited,JobDrained,CleanupVerified;
  public bool NoProcessCreated;
  public int RemainingOwnedProcesses=-1;public long CleanupMilliseconds;public string CleanupError;
  public string RunId;
  public bool FullRestrictedMode,DesktopLeaseObserved,DesktopLeaseClosed,DesktopLeaseRetained,StreamCaptureComplete;
  public bool FullJobObserverClosed,FullJobObserverRetained;public int FullJobObserverCloseError;
 }
 public static partial class ProcessPump {
  sealed class Handshake {
   readonly object gate=new object();readonly Process process;readonly string runId;bool stopped;JobObserver observer;
   public volatile string Error;
   public Handshake(Process p,string id){process=p;runId=id;}
   public void Receive(string line){lock(gate){
    if(stopped)return;
    try{
     string prefix="AZURITE_JOB_V1 "+runId+" ";long raw;
     if(!line.StartsWith(prefix,StringComparison.Ordinal)||!long.TryParse(line.Substring(prefix.Length),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out raw)||raw==0)throw new InvalidOperationException("Invalid bounded owned-job handshake");
     observer=JobObserver.Accept(process,raw);
     process.StandardInput.WriteLine("AZURITE_JOB_ACK "+runId);process.StandardInput.Flush();process.StandardInput.Close();
    }catch(Exception ex){Error=ex.Message;}
   }}
   public JobObserver Current(){lock(gate){return observer;}}
   public JobObserver Stop(){lock(gate){stopped=true;return observer;}}
  }
  sealed class Capture {
   readonly StringBuilder text=new StringBuilder();readonly object gate=new object();readonly int limit;
   public volatile bool Overflow;public Capture(int n){limit=n;}
   public void Pump(StreamReader reader,Action<string> firstLine){char[] buf=new char[2048];try{
    if(firstLine!=null){var line=new StringBuilder();int c;while((c=reader.Read())>=0){if(c=='\n')break;if(c!='\r')line.Append((char)c);if(line.Length>256)break;}string header=line.ToString();lock(gate){text.Append(header).Append('\n');}firstLine(header);}
    int n;while((n=reader.Read(buf,0,buf.Length))>0){lock(gate){int keep=Math.Min(n,limit-text.Length);if(keep>0)text.Append(buf,0,keep);if(keep<n)Overflow=true;}}
   }catch(IOException){}catch(ObjectDisposedException){}}
   public string Snapshot(){lock(gate){return text.ToString();}}
  }
  static bool FilesExceeded(string[] paths,long max) {
   long total=0;
   foreach(string path in paths) {
    string[] files=Directory.Exists(path)?Directory.GetFiles(path):new string[]{path};
    if(files.Length>64)return true;
    foreach(string f in files)if(File.Exists(f)){var info=new FileInfo(f);if((info.Attributes&FileAttributes.ReparsePoint)!=0)return true;long n=info.Length;if(n>max)return true;total+=n;}
   }
   return total>max*4;
  }
  public static PumpResult Run(ProcessStartInfo info,int timeoutMs,OwnedJob job,int maxChars,string[] watchedPaths,long maxFileBytes) {
   return Execute(info,timeoutMs,job,maxChars,watchedPaths,maxFileBytes,null,0);
  }
  public static PumpResult RunBootstrap(ProcessStartInfo info,int timeoutMs,int cleanupGraceMs,string runId,int maxChars,string[] watchedPaths,long maxFileBytes) {
   if(cleanupGraceMs<0||cleanupGraceMs>5000||!System.Text.RegularExpressions.Regex.IsMatch(runId,"^[a-f0-9]{32}$"))throw new ArgumentException("Invalid bootstrap cleanup/handshake contract");
   info.RedirectStandardInput=true; // No environment access; ACK pipe only.
   return Execute(info,timeoutMs,null,maxChars,watchedPaths,maxFileBytes,runId,cleanupGraceMs);
  }
  static PumpResult Execute(ProcessStartInfo info,int timeoutMs,OwnedJob job,int maxChars,string[] watchedPaths,long maxFileBytes,string runId,int cleanupGraceMs) {
   if(timeoutMs<1||maxChars<1)throw new ArgumentOutOfRangeException();
   var result=new PumpResult{ExitCode=-1,StartedUtc=DateTime.UtcNow,RunId=runId};var watch=Stopwatch.StartNew();
   var output=new Capture(maxChars);var error=new Capture(maxChars);var process=new Process{StartInfo=info};
   Handshake handshake=runId==null?null:new Handshake(process,runId);
   bool startReturned=false;
   Task stdout=null,stderr=null;
   try {
    if(!process.Start()){result.NoProcessCreated=true;throw new InvalidOperationException("Child did not start");}startReturned=true;result.Pid=process.Id;
    process.PriorityClass=ProcessPriorityClass.BelowNormal;
    if(job!=null&&!job.Contains(process.Id))throw new InvalidOperationException("Child escaped the owned job");
    stdout=Task.Run(()=>output.Pump(process.StandardOutput,handshake==null?null:(Action<string>)handshake.Receive));stderr=Task.Run(()=>error.Pump(process.StandardError,null));
    for(;;) {
     JobObserver observed=handshake==null?null:handshake.Current();
     if(handshake!=null&&handshake.Error!=null)throw new InvalidOperationException(handshake.Error);
     result.WindowViolation=observed!=null?observed.HasVisibleWindow():(job!=null?job.HasVisibleWindow():WindowProbe.HasVisible(new int[]{process.Id}));
     result.OutputLimitExceeded=output.Overflow||error.Overflow||FilesExceeded(watchedPaths,maxFileBytes);
     if(result.WindowViolation||result.OutputLimitExceeded)break;
     if(process.HasExited&&stdout.IsCompleted&&stderr.IsCompleted){result.ExitCode=process.ExitCode;break;}
     if(watch.ElapsedMilliseconds>=timeoutMs){result.TimedOut=true;break;}
     Thread.Sleep(20);
    }
   } catch(Exception ex) {
    result.Error=ex.Message;
    var native=ex as Win32Exception;
    if(!startReturned&&native!=null){int code=native.NativeErrorCode;result.NoProcessCreated=code==2||code==3||code==5||code==1326||code==1327||code==1330||code==1331||code==1385||code==1909;}
   }
   finally {
    var cleanup=Stopwatch.StartNew();JobObserver observed=handshake==null?null:handshake.Stop();
    result.JobObserved=observed!=null;
    try {
     if(result.Pid==0&&result.NoProcessCreated){result.DirectProcessExited=true;result.JobDrained=true;result.RemainingOwnedProcesses=0;result.CleanupVerified=true;}
     else if(result.Pid==0&&!startReturned){result.CleanupError="Process-start outcome is uncertain; no cleanup proof is available";}
     else {
      // The observer belongs to the verified unnamed job; no PID/name-based cleanup.
      if(observed!=null&&observed.ActiveProcesses()>0)observed.Terminate(result.TimedOut?124u:(result.ExitCode==0?0u:1u));
      if(!process.HasExited)process.Kill();
      do {
       result.DirectProcessExited=process.HasExited;
       if(observed!=null){result.RemainingOwnedProcesses=observed.ActiveProcesses();result.JobDrained=result.RemainingOwnedProcesses==0;}
       result.CleanupVerified=result.DirectProcessExited&&result.JobDrained;
       if(result.CleanupVerified||(observed==null&&result.DirectProcessExited)||cleanup.ElapsedMilliseconds>=cleanupGraceMs)break;
       int wait=(int)Math.Min(20,cleanupGraceMs-cleanup.ElapsedMilliseconds);
       if(wait>0){if(!result.DirectProcessExited)process.WaitForExit(wait);else Thread.Sleep(wait);}
      }while(true);
      if(result.DirectProcessExited)result.ExitCode=process.ExitCode;
      if(handshake!=null&&!result.CleanupVerified)result.CleanupError=observed==null?"No verified owned-job observer; cleanup is unverified":"Owned process/job did not drain within bounded cleanup grace";
     }
    }catch(Exception ex){result.CleanupError=ex.Message;result.CleanupVerified=false;}
    finally{if(observed!=null)observed.Dispose();}
    // No infinite WaitForExit/ReadToEnd. Mutable evidence is consumable only on CleanupVerified.
    result.CleanupMilliseconds=cleanup.ElapsedMilliseconds;
    result.Stdout=output.Snapshot();result.Stderr=error.Snapshot();result.ElapsedMilliseconds=watch.ElapsedMilliseconds;
    process.Dispose();
   }
   return result;
  }
 }
}
