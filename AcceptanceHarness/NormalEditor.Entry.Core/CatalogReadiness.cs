namespace Azurite.NormalEditor.Entry;
public sealed class CatalogReadiness
{
    private readonly CatalogBinding binding;
    private readonly Action<CatalogEvidence> evidence;
    private readonly object sync=new();
    private CatalogStartFrame? active;
    private bool entered,returned,completed,guardActive,emitting,reading;
    private long owner,completionFrame,sequence,lastMilliseconds=-1,lastFrame=-1,workbench,settings,resource;
    public CatalogReadiness(CatalogBinding binding,Action<CatalogEvidence> evidence)
    {
        if(!Guid.TryParseExact(binding.RunId,"D",out var id)||id==Guid.Empty||id.ToString("D")!=binding.RunId||binding.ProcessId<=0||binding.ManagedThreadId<=0||binding.NativeThreadId==0||binding.TimeoutMilliseconds<1||binding.TimeoutMilliseconds>300000)throw new ArgumentException("Exact normal run/process/thread/deadline binding required.",nameof(binding));
        this.binding=binding;this.evidence=evidence??throw new ArgumentNullException(nameof(evidence));
    }
    public bool Ready {get;private set;}
    public string? Failure {get;private set;}
    internal CatalogBinding Binding=>binding;
    internal bool RecordingEvidence=>emitting;
    internal void RequireQuiescentForClose(CatalogStamp stamp)
    {
        lock(sync){
            Require(Failure is null&&Ready&&completed&&active is null&&!emitting&&!reading&&!guardActive,"Catalog is failed, not ready, or still executing a lifecycle/read/evidence callback.");
            Require(stamp.RunId==binding.RunId&&stamp.ProcessId==binding.ProcessId&&stamp.ManagedThreadId==binding.ManagedThreadId&&stamp.NativeThreadId==binding.NativeThreadId,"Foreign normal shutdown identity.");
            Require(stamp.Milliseconds>=lastMilliseconds,"Normal shutdown clock reversed.");
            // This is a shutdown context stamp, not a new native frame/sample.
            // The readiness deadline does not become an application lifetime.
        }
    }
    private static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);}
    private void Stamp(CatalogStamp stamp,bool requireOwner)
    {
        Require(stamp.RunId==binding.RunId&&stamp.ProcessId==binding.ProcessId&&stamp.ManagedThreadId==binding.ManagedThreadId&&stamp.NativeThreadId==binding.NativeThreadId,"Catalog observation belongs to another run/process/thread.");
        Require(stamp.Milliseconds>=0&&stamp.Frame>=0&&stamp.Milliseconds>=lastMilliseconds&&stamp.Frame>=lastFrame,"Catalog observation clock/frame reversed.");
        Require(stamp.Milliseconds<=binding.TimeoutMilliseconds,"Catalog readiness deadline elapsed.");
        if(requireOwner)Require(stamp.CatalogOwner>0&&(owner==0||stamp.CatalogOwner==owner),"Catalog owner changed or is absent.");
        lastMilliseconds=stamp.Milliseconds;lastFrame=stamp.Frame;
    }
    private void Emit(string kind,CatalogStamp stamp,CatalogObservation? observed=null,string? error=null)
    {
        var previous=emitting;emitting=true;
        try{evidence(new(++sequence,kind,binding.RunId,stamp,observed,error));}finally{emitting=previous;}
    }
    public void Reject(string reason,CatalogStamp stamp)
    {
        lock(sync){if(Failure is not null)return;Failure=reason;Ready=false;try{Emit("catalog-failed",stamp,error:reason);}catch{ /* The original failure remains authoritative. */ }}
    }
    public CatalogStartFrame? StartEntered(CatalogStamp stamp)
    {
        lock(sync){if(Failure is not null)return null;try{Require(!emitting,"Reentrant catalog evidence callback.");Stamp(stamp,true);Require(!entered,"Catalog.Start was observed more than once.");entered=true;owner=stamp.CatalogOwner;active=new(stamp);Emit("catalog-start-enter",stamp);return Failure is null?active:null;}catch(Exception e){Reject(e.Message,stamp);return null;}}
    }
    public void StartReturned(CatalogStartFrame? frame,CatalogStamp stamp,bool originalRan)
    {
        lock(sync){if(Failure is not null)return;try{Require(!emitting,"Reentrant catalog evidence callback.");Stamp(stamp,true);Require(active is not null&&ReferenceEquals(active,frame)&&!returned&&!completed,"Foreign, duplicate or late Catalog.Start return.");Require(originalRan,"Native Catalog.Start was suppressed.");returned=true;Emit("catalog-start-return",stamp);}catch(Exception e){Reject(e.Message,stamp);}}
    }
    public Exception? StartFinalized(CatalogStartFrame? frame,CatalogStamp stamp,Exception? originalError)
    {
        lock(sync){if(Failure is not null)return originalError;try{Require(!emitting,"Reentrant catalog evidence callback.");Stamp(stamp,true);Require(active is not null&&ReferenceEquals(active,frame)&&!completed,"Foreign, duplicate or late Catalog.Start finalizer.");if(originalError is not null)throw new InvalidOperationException("Native Catalog.Start failed: "+originalError.Message);Require(returned,"Catalog.Start finalizer lacks an original return.");completed=true;active=null;completionFrame=stamp.Frame;Emit("catalog-start-complete",stamp);}catch(Exception e){Reject(e.Message,stamp);}return originalError;}
    }
    private void Owners(CatalogObservation o)
    {
        void Stable(ref long previous,long current,string name){if(current==0)return;Require(previous==0||previous==current,name+" owner changed while awaiting readiness.");previous=current;}
        Stable(ref workbench,o.WorkbenchOwner,"Workbench");Stable(ref settings,o.SettingsOwner,"Settings");Stable(ref resource,o.ResourceOwner,"Resource");
    }
    private static bool Candidate(CatalogObservation o)=>o.WorkbenchOwner>0&&o.SettingsOwner>0&&o.ResourceOwner>0&&o.Scene=="CatalogScene"&&!o.CliActive&&!o.TransportPresent&&o.SettingsReady&&o.ResourcesReady&&!o.Preloading&&o.LoadingCount==0&&o.CurrentSession==0&&!o.ControlOpening;
    public void ObserveTick(CatalogStamp callback,Func<CatalogObservation> observe,Func<CatalogObservation> guardedReadback)
    {
        lock(sync){if(Failure is not null||Ready)return;try{
            Require(!emitting&&!reading&&!guardActive,"Reentrant catalog observation bridge.");Stamp(callback,false);
            if(!completed||callback.Frame<=completionFrame)return;
            reading=true;CatalogObservation observation;try{observation=observe();}finally{reading=false;}
            Require(Failure is null,"Catalog failed during field observation.");Require(observation.Stamp.Frame==callback.Frame,"Catalog field observation changed native frame.");
            Tick(observation,guardedReadback);
        }catch(Exception e){Reject(e.Message,callback);}}
    }
    public void Tick(CatalogObservation observation,Func<CatalogObservation> guardedReadback)
    {
        lock(sync){if(Failure is not null||Ready)return;try{
            Require(!emitting,"Reentrant catalog evidence callback.");Require(!reading,"Reentrant catalog field observation.");Require(!guardActive,"Reentrant catalog readiness guard.");
            Stamp(observation.Stamp,entered);if(!completed||observation.Stamp.Frame<=completionFrame)return;
            Owners(observation);Require(observation.LoadingCount>=0,"Native loading count is unavailable.");
            if(!Candidate(observation))return;
            guardActive=true;CatalogObservation after;try{after=guardedReadback();}finally{guardActive=false;}
            Require(Failure is null,"Catalog failed during idle guard.");Stamp(after.Stamp,true);
            Require(after.Stamp.Frame==observation.Stamp.Frame&&(after with{Stamp=observation.Stamp})==observation,"Catalog readiness changed across native idle guard/readback.");
            Emit("catalog-ready",after.Stamp,after);Require(Failure is null,"Catalog failed while recording readiness.");Ready=true;
        }catch(Exception e){Reject(e.Message,observation.Stamp);}}
    }
}
