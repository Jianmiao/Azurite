namespace Azurite.NormalEditor.Entry;

public sealed class CatalogTickFrame
{
    internal CatalogTickFrame(CatalogStamp stamp,long owner){Stamp=stamp;Owner=owner;}
    public CatalogStamp Stamp {get;}
    public long Owner {get;}
}

public sealed class CatalogTickCallbacks
{
    private readonly CatalogReadiness readiness;
    private readonly object sync=new();
    private CatalogTickFrame? active;
    private bool returned;
    private long workbench,lastFrame=-1,lastMilliseconds=-1;
    public CatalogTickCallbacks(CatalogReadiness readiness){this.readiness=readiness??throw new ArgumentNullException(nameof(readiness));}
    internal CatalogReadiness Readiness=>readiness;
    internal void RequireQuiescentForClose(){lock(sync)Require(active is null&&!returned,"Native Update return/finalizer is still pending.");}
    private static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);}
    private void Context(CatalogStamp stamp,long owner)
    {
        var binding=readiness.Binding;
        Require(!readiness.RecordingEvidence,"Reentrant catalog evidence callback.");
        Require(stamp.RunId==binding.RunId&&stamp.ProcessId==binding.ProcessId&&stamp.ManagedThreadId==binding.ManagedThreadId&&stamp.NativeThreadId==binding.NativeThreadId,"Foreign native Update run/process/thread.");
        Require(stamp.Frame>=0&&stamp.Milliseconds>=0&&stamp.Frame>=lastFrame&&stamp.Milliseconds>=lastMilliseconds,"Native Update clock/frame reversed.");
        Require(readiness.Ready||stamp.Milliseconds<=binding.TimeoutMilliseconds,"Catalog readiness deadline elapsed during Update.");
        Require(owner>0&&(workbench==0||workbench==owner),"Native Update workbench changed or is absent.");
        workbench=owner;lastFrame=stamp.Frame;lastMilliseconds=stamp.Milliseconds;
    }
    private void Completion(CatalogTickFrame? frame,CatalogStamp stamp,long owner)
    {
        Context(stamp,owner);
        Require(active is not null&&ReferenceEquals(active,frame)&&active.Owner==owner&&active.Stamp.Frame==stamp.Frame,"Foreign, duplicate, late or cross-frame Update completion.");
    }
    public CatalogTickFrame? Enter(CatalogStamp stamp,long owner)
    {
        lock(sync){if(readiness.Failure is not null)return null;try{Context(stamp,owner);Require(active is null,"Native Update entered before previous finalization.");active=new(stamp,owner);returned=false;return active;}catch(Exception e){readiness.Reject(e.Message,stamp);return null;}}
    }
    public void Returned(CatalogTickFrame? frame,CatalogStamp stamp,long owner,bool originalRan)
    {
        lock(sync){if(readiness.Failure is not null)return;try{Completion(frame,stamp,owner);Require(!returned,"Duplicate native Update return.");Require(originalRan,"Native workbench Update was suppressed.");returned=true;}catch(Exception e){readiness.Reject(e.Message,stamp);}}
    }
    public bool TryComplete(CatalogTickFrame? frame,CatalogStamp stamp,long owner,Exception? originalError)
    {
        lock(sync){if(readiness.Failure is not null)return false;try{Completion(frame,stamp,owner);active=null;if(originalError is not null)throw new InvalidOperationException("Native workbench Update failed: "+originalError.Message);Require(returned,"Workbench Update finalizer lacks original return.");returned=false;return true;}catch(Exception e){readiness.Reject(e.Message,stamp);return false;}}
    }
}
