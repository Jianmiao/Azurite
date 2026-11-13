using System.Reflection;
using Azurite;
using BepInEx.Configuration;

static class Program
{
    static int pass,fail;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object? Call(Plugin plugin,string method,params object[] args)
    {
        var m=typeof(Plugin).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Missing production integration: "+method);
        try{return m.Invoke(plugin,args);}catch(TargetInvocationException e){throw e.InnerException!;}
    }
    static Plugin New(bool optIn=true)
    {
        AuthoringCardPackHostRoot.Created.Clear(); var p=new Plugin();Call(p,"BindCardPackAuthoring");
        ((ConfigEntry<bool>)p.Config.Entries["Authoring/CardPacks"]).Value=optIn;return p;
    }
    static bool Tick(Plugin p)=>(bool)Call(p,"TickCardPackAuthoring")!;
    static AuthoringCardPackHostRoot Root()=>AuthoringCardPackHostRoot.Created.Single();
    static void Worker(Action action){Exception? failure=null;var t=new Thread(()=>{try{action();}catch(Exception e){failure=e;}});t.Start();t.Join();if(failure!=null)throw failure;}
    static void Test(string name,Action test){try{test();pass++;Console.WriteLine("PASS "+name);}catch(Exception e){fail++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static int Main()
    {
        Test("default off never creates native owner",()=>{var p=New(false);Check(Tick(p),"rendering should continue");Check(AuthoringCardPackHostRoot.Created.Count==0,"opt-out created owner");});
        Test("first actual Tick supplies editor thread",()=>{var p=New();Check(AuthoringCardPackHostRoot.Created.Count==0,"Load created owner");int thread=0;Worker(()=>{thread=Environment.CurrentManagedThreadId;Tick(p);});Check(Root().Thread==thread,"Load/caller thread captured instead of Tick");});
        Test("healthy no-export binding admits independently of performance patches",()=>{var p=New();Tick(p);var r=Root();Check(!p._host!.Profile.NativePatches&&r.Ownership(),"no-export/101 rejected");Check(r.Updates.SequenceEqual(new[]{true}),"normal update toggled off/on");Tick(p);Check(r.Updates.SequenceEqual(new[]{true,true}),"normal frame invalidates preview");});
        Test("unknown and copied profiles cannot create owner",()=>{var p=New();p._host!.Profile=p._host.Profile with{};Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==0,"copied profile admitted");p._host.Profile=HostProfile.Unsupported;Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==0,"unknown admitted");});
        Test("fresh probe rejects changed binding and exporter ownership",()=>{var p=New();Tick(p);var r=Root();p._host!.Snapshot=HostCompatibilitySnapshot.Pending("pending");Check(!r.Ownership(),"cached permission");p._host.Snapshot=new(true,true,true,true,"export");Check(!r.Ownership(),"export admitted");p._host.Snapshot=HostCompatibilitySnapshot.NoExport("healthy");p._host.PublicRenderingBlocked=true;Check(!r.Ownership(),"public hold ignored");p._host.PublicRenderingBlocked=false;p._host.ThrowProbe=true;Check(!r.Ownership(),"probe exception admitted");});
        Test("disable preserves owner and revokes callbacks",()=>{var p=New();Tick(p);var r=Root();p._enabled.Value=false;Tick(p);Check(!r.Ownership()&&r.Updates.Last()==false&&r.Disposals==0,"disabled owner lost or permitted");p._enabled.Value=true;Tick(p);Check(ReferenceEquals(r,Root())&&r.Updates.Last(),"reenable recreated workflow");p._disabledForSession=true;Tick(p);Check(!r.Ownership()&&!r.Updates.Last(),"session failure ignored");});
        Test("context checks progressive resources mutation and actual activity",()=>{var p=New();Tick(p);var r=Root();Check(r.Context(),"healthy context rejected");p._progressiveLoading=new(){BlocksEditorOperations=true};Check(!r.Context(),"busy admitted");p._progressiveLoading.BlocksEditorOperations=false;p._progressiveLoading.ResourceDependentWorkAllowed=false;Check(!r.Context(),"resources unavailable admitted");p._progressiveLoading=null;p._mutation=new(){Blocking=true};Check(!r.Context(),"mutation admitted");p._mutation=null;p._activity!.Allowed=false;Check(!r.Context(),"context denial ignored");});
        Test("export latch survives ordinary rendering reset until explicit release",()=>{var p=New();Tick(p);var r=Root();Call(p,"SuspendCardPackAuthoring");p._exportSuspended=false;Tick(p);Check(!r.Ownership()&&!r.Updates.Last()&&r.Suspends==1,"Tick silently reopened authoring");Call(p,"ResumeCardPackAuthoring");Tick(p);Check(r.Ownership()&&r.Resumes==1&&r.Updates.Last(),"explicit release failed");});
        Test("export before initial Tick prevents creation",()=>{var p=New();Call(p,"SuspendCardPackAuthoring");Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==0,"held owner created");Call(p,"ResumeCardPackAuthoring");Tick(p);Check(Root().Ownership(),"release before creation not consumed");});
        Test("export reentrancy during fresh probe cannot admit write",()=>{var p=New();Tick(p);var r=Root();p._host!.DuringProbe=()=>Call(p,"SuspendCardPackAuthoring");Check(!r.Ownership(),"stale pre-probe latch admitted");});
        Test("wrong thread callbacks do not touch native root",()=>{var p=New();Tick(p);var r=Root();Worker(()=>{Check(!r.Ownership()&&!r.Context(),"wrong thread admitted");Call(p,"DrawCardPackSelection");Call(p,"SuspendCardPackAuthoring");});Check(r.Draws==0&&r.Suspends==0,"native call on worker");Tick(p);Check(r.Suspends==1&&!r.Updates.Last(),"owner Tick did not consume hold");});
        Test("driver GUI route rechecks ownership",()=>{var p=New();var d=new AzuriteDriver();Call(p,"BindCardPackDriver",d);Tick(p);d.GuiTick!();Check(Root().Draws==1,"GUI not routed");p._enabled.Value=false;d.GuiTick!();Check(Root().Draws==1,"disabled GUI routed");});
        Test("wrong thread unload retains owner and renderer until safe retry",()=>{var p=New();Tick(p);var r=Root();Worker(()=>Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"wrong thread reports complete"));Check(r.Disposals==0&&!r.Ownership(),"wrong thread released or permits write");Check(Tick(p)&&r.IsDisposed&&r.Disposals==1,"pending unload must service rendering Tick alongside LateTick");Check(Tick(p)&&AuthoringCardPackHostRoot.Created.Count==1,"pending retry stopped renderer or rebuilt authoring");Check((bool)Call(p,"TryUnloadCardPackAuthoring")!,"completed owner cannot unload");Check(r.Disposals==1,"double cleanup");});
        Test("terminal cleanup needs no next Tick or editor lookup",()=>{var p=New();Tick(p);var r=Root();p._host=null;p._activity=null;Call(p,"ShutdownCardPackAuthoring");Check(r.IsDisposed&&r.Abandons==1,"terminal cleanup deferred");Call(p,"ShutdownCardPackAuthoring");Check(r.Abandons==1,"terminal double cleanup");Check(!Tick(p),"terminal Tick resumed");});
        Test("same-thread public unload waits for safe Tick outside UI callback",()=>{var p=New();Tick(p);var r=Root();Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"public call assumes safe callback depth");Check(r.Disposals==0&&!r.Ownership(),"disposed inside unknown UI callback");Check(Tick(p)&&r.IsDisposed&&r.Disposals==1,"safe Tick must clean authoring and continue rendering");Check((bool)Call(p,"TryUnloadCardPackAuthoring")!,"completed unload not acknowledged");});
        Test("cleanup failure stays incomplete without half-stopping renderer",()=>{var p=New();Tick(p);var r=Root();r.ThrowDispose=true;Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"queued cleanup reported complete");Check(Tick(p)&&r.Disposals==1,"pending cleanup half-stopped rendering");Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"failed cleanup reported complete");Check(!r.IsDisposed&&!r.Ownership()&&ReferenceEquals(r,Root()),"failed owner discarded");});
        Test("terminal cleanup failure retains owner and records incomplete state",()=>{var p=New();Tick(p);var r=Root();r.ThrowDispose=true;Call(p,"ShutdownCardPackAuthoring");Check(r.Abandons==1&&!r.IsDisposed&&!r.Ownership(),"failed terminal cleanup claimed complete");var field=typeof(Plugin).GetField("_cardPackRoot",BindingFlags.Instance|BindingFlags.NonPublic)!;Check(ReferenceEquals(field.GetValue(p),r)&&p.Log.Entries.Any(x=>x.Contains("retained owner")),"failed terminal owner or diagnostic lost");});
        Test("update failure revokes future admission without recreating owner",()=>{var p=New();Tick(p);var r=Root();r.ThrowUpdate=true;Tick(p);Check(!r.Ownership(),"fault remains writable");Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==1,"fault recreates owner");});
        Console.WriteLine($"{pass} passed, {fail} failed; production partial with explicit external boundaries, no AA/native execution.");return fail==0?0:1;
    }
}
