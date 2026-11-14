using System.Reflection;
using Azurite;
using Azurite.Core;
using BepInEx.Configuration;

static partial class Program
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
    static string Failure(RenderIntervalLease lease) => (typeof(RenderIntervalLease).GetProperty("Failure")?.GetValue(lease)
        ?? throw new Exception("Production lease does not expose its first failure reason.")).ToString()!;
    static bool Released(Plugin plugin) => (bool)Call(plugin,"IsReleasedCardPackIntervalConflict")!;
    static bool Cleanup(Plugin plugin,Func<bool> cleanup,bool renderingOnly=true)
        => (bool)Call(plugin,"RunCardPackRenderingCleanup",cleanup,"fixture rendering transition",renderingOnly)!;
    static int Main()
    {
        Test("default off never creates native owner",()=>{var p=New(false);Check(Tick(p),"rendering should continue");Check(AuthoringCardPackHostRoot.Created.Count==0,"opt-out created owner");});
        Test("first actual Tick supplies editor thread",()=>{var p=New();Check(AuthoringCardPackHostRoot.Created.Count==0,"Load created owner");int thread=0;Worker(()=>{thread=Environment.CurrentManagedThreadId;Tick(p);});Check(Root().Thread==thread,"Load/caller thread captured instead of Tick");});
        Test("healthy no-export binding admits independently of performance patches",()=>{var p=New();Tick(p);var r=Root();Check(!p._host!.Profile.NativePatches&&r.Ownership(),"no-export/101 rejected");Check(r.Updates.SequenceEqual(new[]{true}),"normal update toggled off/on");Tick(p);Check(r.Updates.SequenceEqual(new[]{true,true}),"normal frame invalidates preview");});
        Test("unknown and copied profiles cannot create owner",()=>{var p=New();p._host!.Profile=p._host.Profile with{};Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==0,"copied profile admitted");p._host.Profile=HostProfile.Unsupported;Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==0,"unknown admitted");});
        Test("fresh probe rejects changed binding and exporter ownership",()=>{var p=New();Tick(p);var r=Root();p._host!.Snapshot=HostCompatibilitySnapshot.Pending("pending");Check(!r.Ownership(),"cached permission");p._host.Snapshot=new(true,true,true,true,"export");Check(!r.Ownership(),"export admitted");p._host.Snapshot=HostCompatibilitySnapshot.NoExport("healthy");p._host.PublicRenderingBlocked=true;Check(!r.Ownership(),"public hold ignored");p._host.PublicRenderingBlocked=false;p._host.ThrowProbe=true;Check(!r.Ownership(),"probe exception admitted");});
        Test("rendering configuration can toggle without closing the qualified authoring owner",()=>{var p=New();Tick(p);var r=Root();p._enabled.Value=false;Tick(p);Check(r.Ownership()&&r.Updates.Last()&&r.Disposals==0,"rendering opt-out closed authoring");p._enabled.Value=true;Tick(p);Check(ReferenceEquals(r,Root())&&r.Updates.Last(),"rendering toggle recreated workflow");});
        Test("authoring opt-out retains owner and revokes presentation",()=>{var p=New();Tick(p);var r=Root();((ConfigEntry<bool>)p.Config.Entries["Authoring/CardPacks"]).Value=false;Tick(p);Check(!r.Ownership()&&!r.Updates.Last()&&r.Disposals==0,"authoring opt-out permitted or destroyed owner");((ConfigEntry<bool>)p.Config.Entries["Authoring/CardPacks"]).Value=true;Tick(p);Check(ReferenceEquals(r,Root())&&r.Ownership(),"authoring reenable recreated owner");});
        Test("rendering disabled at startup still allows explicit qualified authoring",()=>{var p=New();p._enabled.Value=false;Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==1&&Root().Ownership(),"rendering-off startup suppressed authoring");});
        Test("context checks progressive resources mutation and actual activity",()=>{var p=New();Tick(p);var r=Root();Check(r.Context(),"healthy context rejected");p._progressiveLoading=new(){BlocksEditorOperations=true};Check(!r.Context(),"busy admitted");p._progressiveLoading.BlocksEditorOperations=false;p._progressiveLoading.ResourceDependentWorkAllowed=false;Check(!r.Context(),"resources unavailable admitted");p._progressiveLoading=null;p._mutation=new(){Blocking=true};Check(!r.Context(),"mutation admitted");p._mutation=null;p._activity!.Allowed=false;Check(!r.Context(),"context denial ignored");});
        Test("export latch survives ordinary rendering reset until explicit release",()=>{var p=New();Tick(p);var r=Root();Call(p,"SuspendCardPackAuthoring");p._exportSuspended=false;Tick(p);Check(!r.Ownership()&&!r.Updates.Last()&&r.Suspends==1,"Tick silently reopened authoring");Call(p,"ResumeCardPackAuthoring");Tick(p);Check(r.Ownership()&&r.Resumes==1&&r.Updates.Last(),"explicit release failed");});
        Test("export before initial Tick prevents creation",()=>{var p=New();Call(p,"SuspendCardPackAuthoring");Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==0,"held owner created");Call(p,"ResumeCardPackAuthoring");Tick(p);Check(Root().Ownership(),"release before creation not consumed");});
        Test("export reentrancy during fresh probe cannot admit write",()=>{var p=New();Tick(p);var r=Root();p._host!.DuringProbe=()=>Call(p,"SuspendCardPackAuthoring");Check(!r.Ownership(),"stale pre-probe latch admitted");});
        Test("wrong thread callbacks do not touch native root",()=>{var p=New();Tick(p);var r=Root();Worker(()=>{Check(!r.Ownership()&&!r.Context(),"wrong thread admitted");Call(p,"DrawCardPackSelection");Call(p,"SuspendCardPackAuthoring");});Check(r.Draws==0&&r.Suspends==0,"native call on worker");Tick(p);Check(r.Suspends==1&&!r.Updates.Last(),"owner Tick did not consume hold");});
        Test("driver GUI route remains available with rendering off but respects authoring opt-out",()=>{var p=New();var d=new AzuriteDriver();Call(p,"BindCardPackDriver",d);Tick(p);d.GuiTick!();Check(Root().Draws==1,"GUI not routed");p._enabled.Value=false;d.GuiTick!();Check(Root().Draws==2,"rendering opt-out blocked authoring GUI");((ConfigEntry<bool>)p.Config.Entries["Authoring/CardPacks"]).Value=false;d.GuiTick!();Check(Root().Draws==2,"authoring opt-out GUI routed");});
        Test("wrong thread unload retains owner and renderer until safe retry",()=>{var p=New();Tick(p);var r=Root();Worker(()=>Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"wrong thread reports complete"));Check(r.Disposals==0&&!r.Ownership(),"wrong thread released or permits write");Check(Tick(p)&&r.IsDisposed&&r.Disposals==1,"pending unload must service rendering Tick alongside LateTick");Check(Tick(p)&&AuthoringCardPackHostRoot.Created.Count==1,"pending retry stopped renderer or rebuilt authoring");Check((bool)Call(p,"TryUnloadCardPackAuthoring")!,"completed owner cannot unload");Check(r.Disposals==1,"double cleanup");});
        Test("terminal cleanup needs no next Tick or editor lookup",()=>{var p=New();Tick(p);var r=Root();p._host=null;p._activity=null;Call(p,"ShutdownCardPackAuthoring");Check(r.IsDisposed&&r.Abandons==1,"terminal cleanup deferred");Call(p,"ShutdownCardPackAuthoring");Check(r.Abandons==1,"terminal double cleanup");Check(!Tick(p),"terminal Tick resumed");});
        Test("same-thread public unload waits for safe Tick outside UI callback",()=>{var p=New();Tick(p);var r=Root();Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"public call assumes safe callback depth");Check(r.Disposals==0&&!r.Ownership(),"disposed inside unknown UI callback");Check(Tick(p)&&r.IsDisposed&&r.Disposals==1,"safe Tick must clean authoring and continue rendering");Check((bool)Call(p,"TryUnloadCardPackAuthoring")!,"completed unload not acknowledged");});
        Test("cleanup failure stays incomplete without half-stopping renderer",()=>{var p=New();Tick(p);var r=Root();r.ThrowDispose=true;Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"queued cleanup reported complete");Check(Tick(p)&&r.Disposals==1,"pending cleanup half-stopped rendering");Check(!(bool)Call(p,"TryUnloadCardPackAuthoring")!,"failed cleanup reported complete");Check(!r.IsDisposed&&!r.Ownership()&&ReferenceEquals(r,Root()),"failed owner discarded");});
        Test("terminal cleanup failure retains owner and records incomplete state",()=>{var p=New();Tick(p);var r=Root();r.ThrowDispose=true;Call(p,"ShutdownCardPackAuthoring");Check(r.Abandons==1&&!r.IsDisposed&&!r.Ownership(),"failed terminal cleanup claimed complete");var field=typeof(Plugin).GetField("_cardPackRoot",BindingFlags.Instance|BindingFlags.NonPublic)!;Check(ReferenceEquals(field.GetValue(p),r)&&p.Log.Entries.Any(x=>x.Contains("retained owner")),"failed terminal owner or diagnostic lost");});
        Test("update failure revokes future admission without recreating owner",()=>{var p=New();Tick(p);var r=Root();r.ThrowUpdate=true;Tick(p);Check(!r.Ownership(),"fault remains writable");Tick(p);Check(AuthoringCardPackHostRoot.Created.Count==1,"fault recreates owner");});
        Test("an external interval before acquisition is classified without writing it",()=>{
            int value=4,writes=0;var lease=new RenderIntervalLease(()=>value,x=>{writes++;value=x;});
            Check(!lease.TryApply(2)&&lease.IsBlocked&&!lease.IsOwned&&value==4&&writes==0,"external interval was acquired or overwritten");
            Check(Failure(lease)=="OwnershipConflict","external ownership was not distinguished");
            Check(!lease.Restore()&&value==4&&writes==0,"blocked Restore semantics changed");
            var p=New();p._intervalLease=lease;Tick(p);Check(Released(p),"released conflict was not recognized");
        });
        Test("a changed owned interval releases ownership without restoring the foreign value",()=>{
            int value=1,writes=0;var lease=new RenderIntervalLease(()=>value,x=>{writes++;value=x;});Check(lease.TryApply(2),"setup acquisition failed");value=7;
            Check(!lease.TryApply(3)&&Failure(lease)=="OwnershipConflict"&&!lease.IsOwned,"owned conflict not classified");Check(!lease.Restore()&&value==7&&writes==1,"foreign value was overwritten");
            var p=New();p._intervalLease=lease;Tick(p);var r=Root();Check(Released(p),"owned conflict did not release");Check(Cleanup(p,()=>{p._disabledForSession=true;return true;})&&r.Ownership(),"safe rendering-only stop closed authoring");Tick(p);Check(ReferenceEquals(r,Root())&&r.Updates.Last(),"rendering stop recreated or hid authoring");
        });
        Test("native interval read failures never qualify as released conflicts",()=>{
            var lease=new RenderIntervalLease(()=>throw new InvalidOperationException("read failed"),_=>throw new Exception("must not write"));Check(!lease.TryApply(2),"read failure accepted");
            Check(Failure(lease)=="NativeAccessFailure"&&lease.IsBlocked&&!lease.IsOwned,"read failure classification lost");var p=New();p._intervalLease=lease;Tick(p);Check(!Released(p),"native read error was called harmless");Cleanup(p,()=>true,renderingOnly:Released(p));Check(!Root().Ownership(),"native error left authoring writable");
        });
        Test("native interval write failures never qualify as released conflicts",()=>{
            var lease=new RenderIntervalLease(()=>1,_=>throw new InvalidOperationException("write failed"));Check(!lease.TryApply(2)&&Failure(lease)=="NativeAccessFailure","write failure classification lost");var p=New();p._intervalLease=lease;Tick(p);Check(!Released(p),"native write error was called harmless");
        });
        Test("interval write verification mismatch remains a safety failure",()=>{
            var lease=new RenderIntervalLease(()=>1,_=>{});Check(!lease.TryApply(2)&&Failure(lease)=="VerificationMismatch","readback mismatch classification lost");var p=New();p._intervalLease=lease;Tick(p);Check(!Released(p),"write verification failure was called external ownership");
        });
        Test("native verification read exception remains a safety failure",()=>{
            int reads=0;var lease=new RenderIntervalLease(()=>++reads==1?1:throw new InvalidOperationException("verification read failed"),_=>{});
            Check(!lease.TryApply(2)&&Failure(lease)=="NativeAccessFailure","verification read exception was not retained");var p=New();p._intervalLease=lease;Tick(p);Check(!Released(p),"verification read exception was called harmless");
        });
        Test("restore access failure keeps the native failure and original blocking semantics",()=>{
            int value=1;bool throwRead=false;var lease=new RenderIntervalLease(()=>throwRead?throw new InvalidOperationException("restore read failed"):value,x=>value=x);Check(lease.TryApply(2),"setup failed");throwRead=true;
            Check(!lease.Restore()&&Failure(lease)=="NativeAccessFailure"&&lease.IsBlocked&&!lease.IsOwned,"restore read failure changed");
        });
        Test("restore ownership conflict is classified without touching the foreign setting",()=>{
            int value=1,writes=0;var lease=new RenderIntervalLease(()=>value,x=>{value=x;writes++;});Check(lease.TryApply(2),"setup failed");value=8;
            Check(!lease.Restore()&&Failure(lease)=="OwnershipConflict"&&value==8&&writes==1,"restore conflict changed the external value");
        });
        Test("invalid or absent interval failure evidence cannot grant a rendering-only exemption",()=>{
            var p=New();Tick(p);Check(!Released(p),"missing lease granted exemption");p._intervalLease=new(()=>1,_=>{});Check(!p._intervalLease.TryApply(0)&&!p._intervalLease.IsBlocked&&Failure(p._intervalLease)=="None","invalid-input behavior changed");Check(!Released(p),"unclassified rejection granted exemption");Check(p._intervalLease.TryApply(1)&&!Released(p),"active owned lease granted exemption");
        });
        Test("the first blocked failure reason cannot be overwritten by retries",()=>{
            bool broken=true;int reads=0;var lease=new RenderIntervalLease(()=>{reads++;return broken?throw new InvalidOperationException("read failed"):9;},_=>{});Check(!lease.TryApply(2),"setup failed");broken=false;
            Check(!lease.TryApply(3)&&!lease.Restore()&&reads==1&&Failure(lease)=="NativeAccessFailure","retry erased the first failure or touched native state");
        });
        Test("rendering cleanup blocks reentrant authoring until every cleanup step returns",()=>{
            var p=New();Tick(p);var r=Root();var d=new AzuriteDriver();Call(p,"BindCardPackDriver",d);
            Check(Cleanup(p,()=>{Check(!r.Ownership()&&!r.Context(),"cleanup reentry admitted");d.GuiTick!();Check(r.Draws==0,"GUI entered during cleanup");return true;}),"successful cleanup rejected");Check(r.Ownership()&&r.Context(),"successful cleanup did not release its temporary hold");
        });
        Test("nested rendering cleanup does not release the outer admission hold",()=>{
            var p=New();Tick(p);var r=Root();Cleanup(p,()=>{Cleanup(p,()=>true);Check(!r.Ownership(),"nested cleanup released outer hold");return true;});Check(r.Ownership(),"outer cleanup hold leaked");
        });
        Test("unsuccessful rendering restoration closes authoring and cannot self-clear",()=>{
            var p=New();Tick(p);var r=Root();Check(!Cleanup(p,()=>false)&&!r.Ownership(),"false restoration result ignored");Cleanup(p,()=>true);Tick(p);Check(!r.Ownership()&&ReferenceEquals(r,Root())&&r.Disposals==0,"later cleanup cleared fault or replaced owner");
        });
        Test("rendering cleanup exception retains owner and closes subsequent admission",()=>{
            var p=New();Tick(p);var r=Root();bool threw=false;try{Cleanup(p,()=>throw new InvalidOperationException("cleanup failed"));}catch(InvalidOperationException){threw=true;}Check(threw&&!r.Ownership(),"cleanup exception swallowed or admitted");Cleanup(p,()=>true);Check(!r.Ownership()&&ReferenceEquals(r,Root()),"cleanup exception fault was cleared");
        });
        Test("a later critical stop escalates after rendering was already safely disabled",()=>{
            var p=New();Tick(p);var r=Root();Cleanup(p,()=>{p._disabledForSession=true;return true;});Check(r.Ownership(),"safe stop rejected");Cleanup(p,()=>{Check(!r.Ownership(),"critical stop did not close before cleanup");return true;},renderingOnly:false);Check(!r.Ownership(),"already-disabled renderer swallowed critical stop");
        });
        Test("successful rendering-only cleanup never bypasses host export or activity guards",()=>{
            var p=New();p._enabled.Value=false;Tick(p);var r=Root();Cleanup(p,()=>{p._disabledForSession=true;return true;});Check(r.Ownership(),"setup safe stop failed");p._host!.PublicRenderingBlocked=true;Check(!r.Ownership(),"public rendering ownership bypassed");p._host.PublicRenderingBlocked=false;Call(p,"SuspendCardPackAuthoring");p._exportSuspended=false;Check(!r.Ownership(),"export latch bypassed");Call(p,"ResumeCardPackAuthoring");p._activity!.Allowed=false;Check(!r.Context(),"activity guard bypassed");p._host.Profile=HostProfile.Unsupported;Check(!r.Ownership(),"unknown profile bypassed");
        });
        CleanupChecks();
        Console.WriteLine($"{pass} passed, {fail} failed; production partial and cleanup helpers with explicit external boundaries, no AA/native execution.");return fail==0?0:1;
    }
}
