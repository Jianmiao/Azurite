using System.Reflection;
using Azurite;
using BepInEx.Logging;

static partial class Program
{
    static bool NativeCleanupResult(object controller,string method)
    {
        var target=controller.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            ?? throw new Exception("Real cleanup helper has no observable result: "+method);
        try{return (bool)target.Invoke(controller,null)!;}catch(TargetInvocationException e){throw e.InnerException!;}
    }
    static FpsController OwnedFps(bool idle=false)
    {
        var fps=new FpsController(new ManualLogSource(),()=>{});fps.Update(0,true);fps.Update(1.1,true);
        Check(fps.IsMappingApplied&&CleanupNative.Target==-1&&CleanupNative.Sync==4,"real FPS setup did not acquire its mapped pair");
        if(idle){fps.SetIdleFrameRate(20);Check(fps.IdleFrameRate==20&&CleanupNative.Target==20&&CleanupNative.Sync==0,"real idle setup failed");}
        return fps;
    }
    static (EditorScrollTuning Controller,UIScrollView First,UIScrollView Second) OwnedScroll()
    {
        var first=new UIScrollView{RawFactor=10};var second=new UIScrollView{RawFactor=20};
        Singleton<Catalog>.Instance=new();Singleton<Catalog>.Instance.uiProfiles.Add(new(){scroll=first});Singleton<Catalog>.Instance.uiProfiles.Add(new(){scroll=second});
        var scroll=new EditorScrollTuning();scroll.Update(0,true,2);Check(first.RawFactor==20&&second.RawFactor==40,"real scroll setup did not acquire both factors");return(scroll,first,second);
    }
    static bool Controllers(Plugin plugin,bool disposeScroll)=>(bool)Call(plugin,"ReleaseCardPackPerformanceControllers",disposeScroll)!;
    static void CleanupChecks()
    {
        Test("actual unowned FPS disposal succeeds and stays idempotent",()=>{CleanupNative.Reset();var fps=new FpsController(new ManualLogSource(),()=>{});Check(NativeCleanupResult(fps,"TryDispose"),"unowned release was falsely failed");int calls=CleanupNative.Calls.Count;fps.Dispose();Check(NativeCleanupResult(fps,"TryDispose")&&CleanupNative.Calls.Count==calls,"disposed success repeated native work");});
        Test("actual FPS cleanup restores idle then mapping after event removal",()=>{CleanupNative.Reset();var fps=OwnedFps(true);CleanupNative.Calls.Clear();Check(NativeCleanupResult(fps,"TryDispose"),"normal FPS cleanup failed");Check(CleanupNative.Target==-1&&CleanupNative.Sync==2,"FPS baseline not restored");Check(CleanupNative.Calls[0]=="unsubscribe"&&CleanupNative.Calls.IndexOf("write-sync:4")<CleanupNative.Calls.IndexOf("write-sync:2"),"event or restoration order changed");});
        Test("actual FPS readback false remains failed after disposal",()=>{CleanupNative.Reset();var fps=OwnedFps();CleanupNative.IgnoreSyncWrite=true;Check(!NativeCleanupResult(fps,"TryDispose"),"ReleaseOwned false was discarded");Check(CleanupNative.Sync==4,"fixture did not retain the incorrect actual value");int calls=CleanupNative.Calls.Count;CleanupNative.IgnoreSyncWrite=false;fps.Dispose();Check(!NativeCleanupResult(fps,"TryDispose")&&CleanupNative.Calls.Count==calls,"disposed FPS failure became success or retried writes");});
        Test("actual FPS native read exception is an unsuccessful cleanup",()=>{CleanupNative.Reset();var fps=OwnedFps();CleanupNative.ThrowTargetRead=true;Check(!NativeCleanupResult(fps,"TryDispose"),"native read exception was hidden");CleanupNative.ThrowTargetRead=false;Check(!NativeCleanupResult(fps,"TryDispose"),"read exception failure was cleared");});
        Test("actual FPS partial idle restore remains failed",()=>{CleanupNative.Reset();var fps=OwnedFps(true);CleanupNative.ThrowTargetWrite=true;Check(!NativeCleanupResult(fps,"TryDispose"),"partial idle restore was hidden");Check(CleanupNative.Target==20&&CleanupNative.Sync==4,"partial write boundary was not exercised");CleanupNative.ThrowTargetWrite=false;Check(!NativeCleanupResult(fps,"TryDispose"),"partial cleanup became successful after disposal");});
        Test("actual FPS unsubscribe exception does not skip restoring its pair",()=>{CleanupNative.Reset();var fps=OwnedFps();CleanupNative.ThrowUnsubscribe=true;Check(!NativeCleanupResult(fps,"TryDispose"),"event removal failure was hidden");Check(CleanupNative.Target==-1&&CleanupNative.Sync==2,"other FPS cleanup was skipped after unsubscribe failure");});
        Test("actual legacy FPS Dispose retains its failed cleanup result",()=>{CleanupNative.Reset();var fps=OwnedFps();CleanupNative.ThrowSyncWrite=true;fps.Dispose();Check(!NativeCleanupResult(fps,"TryDispose"),"legacy Dispose lost its failure");});
        Test("actual scroll cleanup restores every owned factor",()=>{CleanupNative.Reset();var s=OwnedScroll();Check(NativeCleanupResult(s.Controller,"TryRestore")&&s.First.RawFactor==10&&s.Second.RawFactor==20,"scroll restoration failed");s.Controller.Dispose();Check(NativeCleanupResult(s.Controller,"TryDispose"),"successful scroll disposal failed");});
        Test("actual scroll read exception fails while other rows still restore",()=>{CleanupNative.Reset();var s=OwnedScroll();s.First.ThrowRead=true;Check(!NativeCleanupResult(s.Controller,"TryRestore"),"scroll native read exception was hidden");Check(s.First.RawFactor==20&&s.Second.RawFactor==20,"partial scroll cleanup was not retained");s.First.ThrowRead=false;s.Controller.Dispose();int writes=s.First.Writes+s.Second.Writes;Check(!NativeCleanupResult(s.Controller,"TryDispose")&&!NativeCleanupResult(s.Controller,"TryRestore")&&s.First.Writes+s.Second.Writes==writes,"disposed scroll failure was forgotten");});
        Test("actual scroll write exception fails while other rows still restore",()=>{CleanupNative.Reset();var s=OwnedScroll();s.First.ThrowWrite=true;Check(!NativeCleanupResult(s.Controller,"TryDispose"),"scroll native write exception was hidden");Check(s.Second.RawFactor==20,"later scroll row was skipped");});
        Test("actual scroll silent write rejection fails its readback proof",()=>{CleanupNative.Reset();var s=OwnedScroll();s.First.IgnoreWrite=true;Check(!NativeCleanupResult(s.Controller,"TryRestore")&&s.First.RawFactor==20,"scroll readback mismatch was treated as restored");});
        Test("actual legacy scroll Restore and Dispose preserve failure",()=>{CleanupNative.Reset();var s=OwnedScroll();s.First.ThrowWrite=true;s.Controller.Restore();s.First.ThrowWrite=false;s.Controller.Dispose();Check(!NativeCleanupResult(s.Controller,"TryDispose"),"void wrapper hid a previous scroll failure");});
        Test("actual controller aggregation succeeds for both Plugin stop modes",()=>{foreach(bool dispose in new[]{false,true}){CleanupNative.Reset();var p=New();Tick(p);var fps=OwnedFps(true);var s=OwnedScroll();p._fps=fps;p._scroll=s.Controller;var r=Root();Check(Cleanup(p,()=>Controllers(p,dispose))&&r.Ownership(),"successful real helper cleanup closed authoring");Check(CleanupNative.Target==-1&&CleanupNative.Sync==2&&s.First.RawFactor==10&&s.Second.RawFactor==20,"actual helper cleanup incomplete");Check(p._fps==null&&(dispose?p._scroll==null:ReferenceEquals(p._scroll,s.Controller)),"Plugin cleanup field ordering changed");}});
        Test("actual FPS cleanup false closes both Plugin stop modes despite successful scroll",()=>{foreach(bool dispose in new[]{false,true}){CleanupNative.Reset();var p=New();Tick(p);p._fps=OwnedFps();var s=OwnedScroll();p._scroll=s.Controller;CleanupNative.IgnoreSyncWrite=true;Check(!Cleanup(p,()=>Controllers(p,dispose))&&!Root().Ownership(),"FPS false disappeared in actual Plugin aggregation");Check(s.First.RawFactor==10&&s.Second.RawFactor==20,"FPS failure skipped scroll cleanup");Check(p._fps==null,"normal-return failed FPS cleanup did not finish existing ownership release");CleanupNative.IgnoreSyncWrite=false;Cleanup(p,()=>Controllers(p,dispose));Check(!Root().Ownership(),"later empty/null controllers cleared the fault");}});
        Test("actual scroll cleanup false closes both Plugin stop modes despite successful FPS",()=>{foreach(bool dispose in new[]{false,true}){CleanupNative.Reset();var p=New();Tick(p);p._fps=OwnedFps();var s=OwnedScroll();p._scroll=s.Controller;s.First.ThrowWrite=true;Check(!Cleanup(p,()=>Controllers(p,dispose))&&!Root().Ownership(),"scroll false disappeared in actual Plugin aggregation");Check(CleanupNative.Sync==2&&s.Second.RawFactor==20,"other cleanup was skipped");}});
        Test("actual already-disposed controller failures still close Plugin aggregation",()=>{CleanupNative.Reset();var p=New();Tick(p);var fps=OwnedFps();CleanupNative.ThrowTargetRead=true;fps.Dispose();CleanupNative.ThrowTargetRead=false;p._fps=fps;Check(!Cleanup(p,()=>Controllers(p,true))&&!Root().Ownership(),"already disposed FPS was assumed clean");});
        Test("actual already-disposed scroll failure closes both Plugin stop modes",()=>{foreach(bool dispose in new[]{false,true}){CleanupNative.Reset();var p=New();Tick(p);var s=OwnedScroll();s.First.ThrowWrite=true;s.Controller.Dispose();s.First.ThrowWrite=false;p._scroll=s.Controller;Check(!Cleanup(p,()=>Controllers(p,dispose))&&!Root().Ownership(),"already disposed scroll was assumed clean");}});
    }
}
