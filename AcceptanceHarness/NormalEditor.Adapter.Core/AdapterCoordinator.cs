using Azurite.NormalEditor;
using Azurite.EditorIsolation;
using System.Diagnostics.CodeAnalysis;

namespace Azurite.NormalEditor.Adapter;

public sealed class AdapterCoordinator
{
    private readonly object gate=new();
    private readonly AdapterBinding binding;
    private readonly INativeAccess native;
    private readonly IFatalSink fatal;
    private readonly IAdapterAdmission? admission;
    private readonly string? evidenceHash;
    private readonly List<AdapterFrame> stack=new();
    private readonly Dictionary<AdapterFrame,bool> awaitingFinalizer=new();
    private readonly Dictionary<int,List<AdapterFrame>> completionStacks=new();
    private readonly int managedThread=Environment.CurrentManagedThreadId;
    private long owner,init,read,save,ordinal,sequence;
    private bool dispatching,rerouted;
    public NormalSettingsLifecycle Policy {get;}
    public string? Failure {get;private set;}
    public int OpenFrames {get {lock(gate)return stack.Count;}}
    public AdapterCoordinator(AdapterBinding binding,INativeAccess native,IFatalSink fatal,IAdapterAdmission? admission=null)
    {
        if(string.IsNullOrWhiteSpace(binding.ExpectedNativeSettingsPath))throw new ArgumentException("Controller-bound native settings string is required.");
        this.binding=binding;this.native=native;this.fatal=fatal;this.admission=admission;evidenceHash=admission?.EvidenceSha256;
        Policy=new NormalSettingsLifecycle(binding.Policy,admission);
    }
    public bool AwakeEntered(long pointer,out AdapterFrame? frame)
    {
        AdapterFrame? result=null;
        var allowed=Guard(()=>{
            Callback(CallbackKind.Awake,pointer);
            Require(owner==0 && stack.Count==0 && native.KindOf(pointer)==NativeKind.Settings,"Repeated/non-settings Awake.");
            var raw=native.ReadSettings(pointer);Require(raw.Owner==pointer,"Awake owner differs.");
            Policy.BindAwake(Observation(raw),raw.CliActive,raw.TransportPresent);owner=pointer;
            Require(!raw.Dirty,"Pristine Awake is already dirty.");
            result=Push(CallbackKind.Awake,pointer);
            Route(RouteStage.Awake,"settingFilePath",binding.Policy.Paths.Settings);
            Route(RouteStage.Awake,"_cachedWorkspacePath",binding.Policy.Paths.Workspace);
        });frame=result;return allowed;
    }
    public bool CoroutineEntered(long pointer,long routine,out AdapterFrame? frame)
    {
        AdapterFrame? result=null;
        var allowed=Guard(()=>{
            if(owner==0 || pointer!=owner)
            {
                result=Issue(new AdapterFrame(++ordinal,CallbackKind.Coroutine,pointer,disposition:CallbackDisposition.UnrelatedShared));
                return; // Explicit pass-through identity; no iterator dereference, policy event or native mutation.
            }
            Callback(CallbackKind.Coroutine,pointer);var iterator=native.ReadIterator(routine);
            Require(iterator.Pointer==routine && iterator.Owner==owner && iterator.State==0,"Bound owner's coroutine identity/state differs.");
            if(init==0 && stack.Any(x=>x.Kind==CallbackKind.Awake) && iterator.Kind==NativeKind.Init)
            { Policy.InitTaskCreated(Observe(),routine,iterator.State);init=routine; }
            else
            {
                var continuation=ActiveInit();
                Require(continuation?.Init?.State==1 && iterator.Kind==NativeKind.Save && save==0,"Conflicting bound-owner coroutine origin.");
                Policy.SaveTaskCreated(Observe(),continuation!.Init!,routine,iterator.State);save=routine;
            }
            result=Push(CallbackKind.Coroutine,pointer);
        });frame=result;return allowed;
    }
    public bool IteratorEntered(long pointer,NativeKind expected,out AdapterFrame? frame)
    {
        AdapterFrame? result=null;
        var allowed=Guard(()=>{
            Require(expected is NativeKind.Init or NativeKind.Read or NativeKind.Save,"Invalid iterator callback kind.");
            var iterator=native.ReadIterator(pointer);
            Require(iterator.Pointer==pointer && iterator.Kind==expected,"Native iterator class differs.");
            var kind=expected==NativeKind.Init?CallbackKind.Init:expected==NativeKind.Read?CallbackKind.Read:CallbackKind.Save;
            Callback(kind,pointer);Require(owner!=0 && iterator.Owner==owner,"Bound iterator owner differs.");
            if(expected==NativeKind.Init)
            { Require(pointer==init,"Foreign InitTask.");result=Push(kind,pointer,Policy.InitMoveNextEntered(Observe(),pointer,iterator.State)); }
            else if(expected==NativeKind.Read)
            { Require(pointer==read,"Read is not the actual yielded child.");Policy.ReadEntered(Observe(),pointer,iterator.State);result=Push(kind,pointer); }
            else
            { Require(pointer==save,"Foreign SaveTask.");var settings=Snapshot();result=Push(kind,pointer,saveToken:Policy.SaveMoveNextEntered(Observation(settings),pointer,iterator.State,settings.Dirty)); }
        });frame=result;return allowed;
    }
    public bool WriteEntered(long pointer,out AdapterFrame? frame)
    {
        AdapterFrame? result=null;
        var allowed=Guard(()=>{
            Callback(CallbackKind.Write,pointer);Require(pointer==owner && stack.LastOrDefault()?.Kind==CallbackKind.Save,"Write lacks its direct actual SaveTask frame.");
            result=Push(CallbackKind.Write,pointer,write:Policy.AdmitWrite(Observe(),stack[^1].Save!));
        });frame=result;return allowed;
    }
    public bool ResourceEntered(long pointer,out AdapterFrame? frame)
    {
        AdapterFrame? result=null;
        var allowed=Guard(()=>{
            Callback(CallbackKind.Resource,pointer);Require(owner!=0 && native.KindOf(pointer)==NativeKind.Resource,"Resource callback is not bound to initialized settings.");
            var actual=Snapshot();admission!.RequireResources(binding,pointer,actual);Healthy();
            Policy.ResourceInitializationObserved(Observation(actual),ActiveInit()?.Init);
            result=Push(CallbackKind.Resource,pointer);
        });frame=result;return allowed;
    }
    public bool Returned(AdapterFrame? frame,CallbackKind kind,long pointer,bool ranOriginal,bool? yielded=null)
    {
        return Guard(()=>{
            Require(!KnownFrame(frame,kind,pointer),"Original return was already observed.");
            Require(ranOriginal,"A skipped original is not a successful return.");
            if(frame.Disposition==CallbackDisposition.UnrelatedShared)
            { Require(kind==CallbackKind.Coroutine,"Unrelated disposition belongs only to the shared entry.");awaitingFinalizer[frame]=true;return; }
            Callback(kind,pointer);Require(stack.Count>0 && ReferenceEquals(stack[^1],frame) && frame.Kind==kind && frame.Instance==pointer,"Missing, foreign, stale or out-of-order original return.");
            if(kind==CallbackKind.Init)
            {
                Require(yielded.HasValue,"Missing actual InitTask return value.");var actual=Iterator(pointer,NativeKind.Init);
                if(frame.Init!.State==0)
                {
                    Require(!rerouted && yielded==true && actual.State==1 && actual.Current>0 && actual.Current!=init,"Invalid original state-zero yield.");
                    var child=Iterator(actual.Current,NativeKind.Read);Require(child.State==0,"Yielded read child is not state zero.");
                    var raw=Snapshot();Require(raw.Data==0 && !raw.Ready && !raw.Dirty && raw.SettingsPath==binding.ExpectedNativeSettingsPath && RunPaths.Same(raw.CachedWorkspace,binding.Policy.Paths.Workspace),"Unexpected native default-path construction or owner drift.");
                    rerouted=true;Route(RouteStage.InitReadYield,"settingFilePath",binding.Policy.Paths.Settings);
                    Policy.InitMoveNextReturned(Observe(),frame.Init,pointer,true,actual.State,child.Pointer);read=child.Pointer;
                }
                else Policy.InitMoveNextReturned(Observe(),frame.Init,pointer,yielded!.Value,actual.State);
            }
            else if(kind==CallbackKind.Read)
            {
                Require(yielded.HasValue,"Missing actual read return.");Require(Iterator(pointer,NativeKind.Read).State==-1,"Original read did not terminate its iterator.");
                var actual=Snapshot();Policy.OriginalReadReturned(Observation(actual),pointer,yielded!.Value,actual.Marker!);
            }
            else if(kind==CallbackKind.Save)
            {
                Require(yielded.HasValue,"Missing actual saver return.");var actual=Iterator(pointer,NativeKind.Save);
                Policy.SaveMoveNextReturned(Observe(),frame.Save!,pointer,yielded!.Value,actual.State);
            }
            else if(kind==CallbackKind.Write)Policy.OriginalWriteReturned(Observe(),frame.Write!);
            else if(kind==CallbackKind.Awake) { Require(init!=0,"Awake returned without its actual InitTask capture.");Snapshot(); }
            stack.RemoveAt(stack.Count-1);
            awaitingFinalizer[frame]=true;
        });
    }
    internal bool FinalizerObserved(CallbackKind kind,long pointer,AdapterFrame? frame,Exception? originalException)
    {
        return Guard(()=>{
            var returned=KnownFrame(frame,kind,pointer);
            if(frame.Disposition==CallbackDisposition.UnrelatedShared)
            {
                Require(kind==CallbackKind.Coroutine,"Unrelated disposition belongs only to the shared entry.");
                Require(returned || originalException is not null,"Unrelated shared return is missing before finalization.");
            }
            else
            {
                Callback(kind,pointer,finalizing:true);
                if(originalException is not null)throw new InvalidOperationException("Original callback failed: "+originalException.GetType().Name+": "+originalException.Message);
                Require(returned && !stack.Contains(frame),"Original return callback is missing at finalization.");
            }
            var completions=completionStacks[frame.ManagedThreadId];completions.RemoveAt(completions.Count-1);
            if(completions.Count==0)completionStacks.Remove(frame.ManagedThreadId);
            awaitingFinalizer.Remove(frame); // Retirement is one-shot; unknown or retired identities never pass.
        });
    }
    public bool RejectFallback()=>Guard(()=>throw new InvalidOperationException("Native settings fallback/dialog initialization is forbidden."));
    public bool Close()=>Guard(()=>{Callback(CallbackKind.Awake,owner);Require(stack.Count==0 && !awaitingFinalizer.Keys.Any(f=>f.Disposition==CallbackDisposition.Tracked),"Original callback return/finalizer is missing.");Policy.Close(Observe());});
    public bool Reject(string reason)=>Guard(()=>throw new InvalidOperationException(reason));
    private AdapterFrame Push(CallbackKind kind,long pointer,InitInvocation? initToken=null,SaveInvocation? saveToken=null,WritePermit? write=null)
    { var frame=Issue(new AdapterFrame(++ordinal,kind,pointer,initToken,saveToken,write));stack.Add(frame);return frame; }
    private AdapterFrame Issue(AdapterFrame frame)
    {
        CanEnter();
        if(!completionStacks.TryGetValue(frame.ManagedThreadId,out var completions))completionStacks.Add(frame.ManagedThreadId,completions=new());
        completions.Add(frame);awaitingFinalizer.Add(frame,false);return frame;
    }
    private bool KnownFrame([NotNull] AdapterFrame? frame,CallbackKind kind,long pointer)
    {
        Require(frame is not null,"Missing callback/finalizer identity is never unrelated traffic.");
        Require(frame.Kind==kind && frame.Instance==pointer && frame.ManagedThreadId==Environment.CurrentManagedThreadId,"Finalizer/callback method, receiver or thread identity differs.");
        Require(awaitingFinalizer.TryGetValue(frame,out var returned),"Foreign or previously finalized callback identity.");
        Require(completionStacks.TryGetValue(frame.ManagedThreadId,out var completions) && completions.Count>0 && ReferenceEquals(completions[^1],frame),"Finalizer/callback identity is out of nested order.");
        return returned;
    }
    private void CanEnter()
    {
        if(completionStacks.TryGetValue(Environment.CurrentManagedThreadId,out var completions) && completions.Count>0)
            Require(!awaitingFinalizer[completions[^1]],"Previous original return has not reached its finalizer.");
    }
    private AdapterFrame? ActiveInit()=>stack.LastOrDefault(x=>x.Kind==CallbackKind.Init);
    private NativeIterator Iterator(long pointer,NativeKind kind)
    { var result=native.ReadIterator(pointer);Require(result.Pointer==pointer && result.Owner==owner && result.Kind==kind,"Native iterator identity changed.");return result; }
    private NativeSettings Snapshot()
    {
        Require(owner>0 && native.KindOf(owner)==NativeKind.Settings,"Missing exact settings owner.");var actual=native.ReadSettings(owner);
        Require(actual.Owner==owner && !actual.CliActive && !actual.TransportPresent,"Owner/CLI context changed.");return actual;
    }
    private SettingsObservation Observe()=>Observation(Snapshot());
    private SettingsObservation Observation(NativeSettings actual)=>new(binding.Policy.RunId,binding.Policy.Generation,binding.Policy.ProcessId,native.NativeThreadId,++sequence,
        actual.Owner,actual.Data,actual.Ready,actual.SettingsPath!,actual.CachedWorkspace!,actual.DataWorkspace!,actual.DataCache!);
    private void Route(RouteStage stage,string field,string target)
    {
        var before=Snapshot();var intent=new RouteRecord(stage,field,target,before,null);
        admission!.RequireRouting(binding,intent);Healthy();admission.RecordRouting(intent);Healthy();
        Require(Snapshot()==before,"Native fields changed during routing admission/audit.");
        if(field=="settingFilePath")native.SetSettingsPath(owner,target);else if(field=="_cachedWorkspacePath")native.SetCachedWorkspace(owner,target);else throw new InvalidOperationException("Forbidden field route.");
        var after=Snapshot();var restored=field=="settingFilePath"?after with{SettingsPath=before.SettingsPath}:after with{CachedWorkspace=before.CachedWorkspace};
        Require(restored==before && (field=="settingFilePath"?after.SettingsPath:after.CachedWorkspace)==target,"Routing changed an unauthorized field or missed its target.");
        admission.RecordRouting(intent with{After=after});Healthy();
    }
    private void Callback(CallbackKind kind,long instance,bool finalizing=false)
    {
        if(!finalizing)CanEnter();
        Require(admission is not null,"External adapter admission is absent.");
        Require(Environment.ProcessId==binding.Policy.ProcessId && Environment.CurrentManagedThreadId==managedThread && native.NativeThreadId==binding.Policy.NativeThreadId,"Adapter process/thread identity changed.");
        admission!.RequireCallback(binding,kind,instance);Healthy();
    }
    private void Healthy()
    {
        InstallationAuthority.RequireEvidence(evidenceHash,admission?.EvidenceSha256);
        Require(Failure is null && Policy.Failure is null,"A nested refusal was swallowed.");
    }
    private static void Require([DoesNotReturnIf(false)] bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    private bool Guard(Action action)
    {
        lock(gate)
        {
            if(Failure is not null)return false;
            if(dispatching){Abort("Reentrant adapter transition.");return false;}
            dispatching=true;
            try{action();Healthy();return true;}
            catch(Exception error){Abort(error.Message);return false;}
            finally{dispatching=false;}
        }
    }
    private void Abort(string reason)
    {
        Failure??=reason;Policy.Fail(Failure);
        try{fatal.Terminate(Failure);}catch{ }
        // Always return false from the enclosing guard even if a test/faulty fatal sink returns.
    }
}
