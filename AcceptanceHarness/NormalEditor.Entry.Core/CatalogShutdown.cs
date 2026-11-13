namespace Azurite.NormalEditor.Entry;

/// <summary>Managed producer retirement only; does not prove native exit or seal a journal.</summary>
public sealed class CatalogShutdown
{
    private readonly CatalogReadiness readiness;
    private readonly CatalogTickCallbacks ticks;
    private readonly object sync=new();
    private bool registered,closing,stopped,closed;
    private static readonly object Retired=new();
    // null = no foreign refusal yet; ForeignFailure = permanently refused;
    // Retired = successful close commit. Both foreign refusal and close commit
    // use this one CAS location, so neither can overwrite the other.
    private object? retirementDecision;
    private CallbackScope? active;
    private CatalogStamp lastStamp;
    public CatalogShutdown(CatalogReadiness readiness,CatalogTickCallbacks ticks)
    {
        this.readiness=readiness??throw new ArgumentNullException(nameof(readiness));
        this.ticks=ticks??throw new ArgumentNullException(nameof(ticks));
        if(!ReferenceEquals(ticks.Readiness,readiness))throw new ArgumentException("Shutdown coordinators belong to different observers.",nameof(ticks));
        var b=readiness.Binding;lastStamp=new(b.RunId,b.ProcessId,b.ManagedThreadId,b.NativeThreadId,0,0,0);
    }
    public bool Closed {get{lock(sync)return closed&&IsRetired&&readiness.Failure is null;}}
    public bool ProducersStopped {get{lock(sync)return stopped;}}
    private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    private bool IsRetired=>ReferenceEquals(System.Threading.Volatile.Read(ref retirementDecision),Retired);
    private void LatchForeignFailure(string reason)=>System.Threading.Interlocked.CompareExchange(ref retirementDecision,new ForeignFailure(reason),null);
    private void RecordForeignFailure()
    {
        // Owner only. A foreign callback must never enter readiness/evidence
        // locks or the user-provided evidence sink, even outside our own lock.
        if(System.Threading.Volatile.Read(ref retirementDecision) is ForeignFailure failure)readiness.Reject(failure.Reason,lastStamp);
    }
    private void Healthy(){RecordForeignFailure();Require(readiness.Failure is null,"Catalog observer already failed.");}
    private bool OnOwnerThread()=>Environment.ProcessId==readiness.Binding.ProcessId&&Environment.CurrentManagedThreadId==readiness.Binding.ManagedThreadId;
    private void OwnerThread()=>Require(OnOwnerThread(),"Shutdown is not on the registered managed owner thread/process.");
    private void Context(CatalogStamp stamp)
    {
        OwnerThread();var b=readiness.Binding;
        Require(stamp.RunId==b.RunId&&stamp.ProcessId==b.ProcessId&&stamp.ManagedThreadId==b.ManagedThreadId&&stamp.NativeThreadId==b.NativeThreadId,"Shutdown/callback belongs to a different run/process/thread.");
        lastStamp=stamp;
    }
    private void Fail(Exception error)=>readiness.Reject(error.Message,lastStamp);
    public bool MarkRegistered()
    {
        if(!OnOwnerThread())return false;
        lock(sync){RecordForeignFailure();if(readiness.Failure is not null||stopped)return false;try{OwnerThread();Require(!registered&&!closing&&active is null,"Registration is incomplete, repeated or reentrant.");registered=true;return true;}catch(Exception e){Fail(e);return false;}}
    }
    /// <summary>Entered before external verification; remains active through field reads and evidence emission.</summary>
    public bool TryEnter(CatalogStamp stamp,out IDisposable? scope)
    {
        scope=null;
        if(IsRetired||System.Threading.Volatile.Read(ref stopped)&&!System.Threading.Volatile.Read(ref closing))return false;
        if(!OnOwnerThread()){LatchForeignFailure("Foreign managed Catalog callback thread/process.");return false;}
        lock(sync){
            // After successful or failed producer retirement, hooks are inert.
            // In-progress close reentry must still fail and invalidate its result.
            RecordForeignFailure();if(stopped&&!closing||readiness.Failure is not null)return false;
            try{Context(stamp);Require(registered&&!closing&&active is null,"Catalog callback before registration or during callback/close reentry.");active=new(this);scope=active;return true;}
            catch(Exception e){Fail(e);return false;}
        }
    }
    /// <summary>
    /// Explicit normal-shutdown step, never driven by Ready. Only true permits
    /// an external caller to Seal afterward. This does not invoke Seal, Quit,
    /// unpatch a trampoline, prove native drain, or certify process exit.
    /// </summary>
    public bool TryClose(Func<CatalogStamp> capture,Action verify,Func<bool> closeAdapter)
    {
        // A foreign close request has no authority to retire this producer. It
        // returns false before taking either lock or invoking any delegate.
        if(!OnOwnerThread())return false;
        lock(sync){
            RecordForeignFailure();
            if(readiness.Failure is not null||stopped&&!closing)return false;
            if(closing){Fail(new InvalidOperationException("Reentrant Catalog shutdown."));return false;}
            closing=true;
            try{
                OwnerThread();Require(registered&&active is null,"Normal shutdown requires complete registration and no active host callback.");
                Context(capture());Healthy();
                readiness.RequireQuiescentForClose(lastStamp);ticks.RequireQuiescentForClose();
                verify();Healthy();
                // No admitted callback may pass TryEnter after this point. An
                // adapter-close failure remains failed/stopped, never closed.
                stopped=true;
                Require(closeAdapter(),"The registered settings adapter refused normal Close.");Healthy();
                // A foreign refusal between Healthy and this instruction wins
                // the same atomic decision and forces false. Once Retired wins,
                // late hooks are inert even before closing is cleared below.
                Require(System.Threading.Interlocked.CompareExchange(ref retirementDecision,Retired,null) is null,"Foreign Catalog callback raced with normal close commit.");
                closed=true;return true;
            }catch(Exception e){RecordForeignFailure();Fail(e);return false;}
            finally{closing=false;}
        }
    }
    private void Leave(CallbackScope scope)
    {
        if(IsRetired)return;
        if(!OnOwnerThread()){LatchForeignFailure("Foreign managed Catalog callback retirement.");return;}
        lock(sync){RecordForeignFailure();try{OwnerThread();Require(ReferenceEquals(active,scope),"Foreign or repeated Catalog callback retirement.");active=null;}catch(Exception e){Fail(e);}}
    }
    private sealed record ForeignFailure(string Reason);
    private sealed class CallbackScope : IDisposable
    {
        private readonly CatalogShutdown owner;
        internal CallbackScope(CatalogShutdown owner)=>this.owner=owner;
        public void Dispose()=>owner.Leave(this);
    }
}
