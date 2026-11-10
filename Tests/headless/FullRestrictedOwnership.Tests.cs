using System;using System.Collections.Generic;using System.Diagnostics;using Azurite.Restricted;
internal static class FullRestrictedOwnershipTests {
 static int checks;static readonly List<string> failures=new List<string>();
 static void Check(bool value,string why){checks++;if(!value)failures.Add(why);}
 static int Main(){
  int jobCalls=0;var job=JobObserver.FromFullOwnedReference(new IntPtr(1234),value=>{jobCalls++;Check(value.ToInt64()==1234,"job release uses original owned identity");return 5;});
  var result=new PumpResult{CleanupVerified=true,DirectProcessExited=true,JobDrained=true};
  ProcessPump.FinalizeFullObserver(result,job);
  Check(!result.CleanupVerified&&result.FullJobObserverRetained&&!result.FullJobObserverClosed&&result.FullJobObserverCloseError==5,"failed Job close must invalidate otherwise successful drain");
  Check(job.RetainFull().Value.ToInt64()==1234&&job.RetainFull().Acquired.ToInt64()==1234,"failed Job close retains original reference");
  ProcessPump.FinalizeFullObserver(result,job);Check(jobCalls==1,"failed Job close is not retried or duplicated");
  int goodCalls=0;var good=JobObserver.FromFullOwnedReference(new IntPtr(2222),value=>{goodCalls++;return 0;});var okay=new PumpResult{CleanupVerified=true,DirectProcessExited=true,JobDrained=true};
  ProcessPump.FinalizeFullObserver(okay,good);ProcessPump.FinalizeFullObserver(okay,good);Check(okay.CleanupVerified&&okay.FullJobObserverClosed&&!okay.FullJobObserverRetained&&goodCalls==1,"successful checked Job close is committed once");
  foreach(string fault in new[]{"actual-name","actual-owner","native-query"}){
   int closes=0,acks=0;FullRejectedReference rejected=null;
   try{FullDesktopLease.AcceptOwnedDuplicate(new IntPtr(3333),"expected",value=>{throw new InvalidOperationException(fault);},value=>{closes++;Check(value.ToInt64()==3333,"rejected desktop release uses acquired duplicate");return 6;});acks++;}catch(FullRejectedReference e){rejected=e;}
   Check(rejected!=null&&rejected.Primary.Message==fault&&rejected.Message.Contains("close error=6"),"desktop rejection preserves primary and compensating-close errors: "+fault);
   Check(rejected!=null&&rejected.Owned.Retained&&!rejected.Owned.Closed&&rejected.Owned.Value.ToInt64()==3333,"desktop rejection retains duplicate after failed close: "+fault);
   if(rejected!=null){var resultRejected=new PumpResult{CleanupVerified=true};ProcessPump.RecordFullRejectedReference(resultRejected,rejected);Check(!resultRejected.CleanupVerified&&resultRejected.DesktopLeaseRetained&&resultRejected.CleanupError.Contains(fault)&&resultRejected.CleanupError.Contains("acquired=D05"),"actual outer rejection recorder preserves owned identity and both failures: "+fault);}
   Check(acks==0&&closes==1,"rejected handoff has no ACK and one compensating close: "+fault);
   if(rejected!=null){rejected.Owned.Close();Check(closes==1,"retained rejected desktop is not closed twice: "+fault);}
  }
  int cleanClose=0;try{FullDesktopLease.AcceptOwnedDuplicate(new IntPtr(4444),"expected",value=>{throw new InvalidOperationException("owner");},value=>{cleanClose++;return 0;});}catch(FullRejectedReference e){Check(e.Owned.Closed&&!e.Owned.Retained&&e.Owned.Value==IntPtr.Zero&&cleanClose==1,"rejected handoff records successful compensation");}
  int throwingCalls=0;var throwing=JobObserver.FromFullOwnedReference(new IntPtr(5555),value=>{throwingCalls++;throw new Exception("injected CloseHandle throw");});var thrownResult=new PumpResult{CleanupVerified=true,DirectProcessExited=true,JobDrained=true};ProcessPump.FinalizeFullObserver(thrownResult,throwing);Check(!thrownResult.CleanupVerified&&throwing.RetainFull().CloseException.Contains("injected")&&throwing.RetainFull().Value.ToInt64()==5555&&throwingCalls==1,"thrown close preserves actual reference and failed acceptance");
  foreach(string stage in new[]{"created-false","actual-token","job","streams","thread-only","close-process"}){
   var acquired=new AcquiredOperations(stage);var outcome=FullRestrictedCoordinator.Run(acquired);
   Check(outcome.Error!=null&&outcome.ResumeCalls==0&&!outcome.ResumeVerified,"partial acquisition never resumes/falls back: "+stage);
   Check(!acquired.Api.Result.Process.NoProcessCreated&&Convert.ToInt64(acquired.Api.Result.Evidence["partialThreadHandle"])==6002,"created handles are retained before failure assertion: "+stage);
   if(stage=="thread-only")Check(!outcome.CleanupVerified&&outcome.CleanupError!=null&&acquired.Lifetime.TerminateCalls==0,"thread-only result cannot prove process absence");
   else Check(acquired.Lifetime.TerminateCalls==1&&acquired.Lifetime.LastHandle.ToInt64()==6001&&acquired.Lifetime.WaitCalls==1,"actual direct cleanup uses acquired process handle: "+stage);
   if(stage=="close-process"){Check(!outcome.CleanupVerified&&!acquired.Api.Result.NativeHandlesClosed&&Convert.ToInt64(acquired.Api.Result.Evidence["retainedProcessHandle"])==6001,"failed partial process close retains actual identity");try{acquired.Api.Close();}catch{}Check(acquired.Closes[6001]==1&&acquired.Closes[6002]==1,"partial close cannot duplicate failed/succeeded closes");}
   else Check(acquired.Api.Result.NativeHandlesClosed&&acquired.Closes[6002]==1,"partial acquired thread closes exactly once: "+stage);
  }
  Console.WriteLine("{\"schema\":1,\"mode\":\"same-production ownership methods; injected close operations only\",\"passed\":"+(failures.Count==0?"true":"false")+",\"checks\":"+checks+",\"failures\":["+String.Join(",",failures.ConvertAll(x=>"\""+x+"\""))+"]}");return failures.Count==0?0:1;
 }
 sealed class Lifetime:IFullProcessLifetime {internal int TerminateCalls,WaitCalls;internal IntPtr LastHandle;public bool Exited(IntPtr p){LastHandle=p;return false;}public int Terminate(IntPtr p,uint code){LastHandle=p;TerminateCalls++;return 0;}public uint Wait(IntPtr p,uint ms){LastHandle=p;WaitCalls++;return 0;}public uint ExitCode(IntPtr p){LastHandle=p;return 1;}}
 sealed class AcquiredOperations:IFullRestrictedOperations {
  internal readonly FullRestrictedSpawn Api;internal readonly Lifetime Lifetime=new Lifetime();internal readonly Dictionary<long,int> Closes=new Dictionary<long,int>();readonly string stage;
  internal AcquiredOperations(string value){stage=value;Api=new FullRestrictedSpawn(new ProcessStartInfo(),1000,null,new string('a',32),FullRestrictedContract.Lab+@"\runs\restricted-"+new string('a',32),new string('b',64),16,new string[0],1024,Lifetime,handle=>{long id=handle.ToInt64();if(!Closes.ContainsKey(id))Closes[id]=0;Closes[id]++;return stage=="close-process"&&id==6001?5:0;});}
  public void ReadOriginalAndDerive(){}public void AcquireDesktopLease(){}
  public void CreateSuspended(){Api.RecordCreationResult(stage!="created-false"&&stage!="thread-only",5,stage=="thread-only"?IntPtr.Zero:new IntPtr(6001),new IntPtr(6002),123,124);}
  public void ValidateActualToken(){if(stage=="actual-token"||stage=="close-process")throw new Exception("actual-token failed after acquisition");}public void ValidateExactJob(){if(stage=="job")throw new Exception("job failed after acquisition");}public void PrepareStreams(){if(stage=="streams")throw new Exception("streams failed after acquisition");}public uint Resume(){throw new Exception("Unexpected resume in acquisition control");}public void Monitor(){}
  public bool Cleanup(){return Api.CleanupDirectProcess(20);}public void Close(){Api.Close();}
 }
}
