using Azurite.NormalEditor.Adapter;

static class FinalizerCases
{
    internal static void Run(string output,Action<string,Action> test,Action<bool,string> check)
    {
        Fixture Awake()
        { var f=new Fixture(output);Fixture.Must(f.Adapter.AwakeEntered(100,out f.AwakeFrame));return f; }
        void Refused(Fixture f,Func<bool> next)
        {
            check(f.Adapter.Failure is not null && f.Adapter.Policy.Failure is not null && f.Fatal.Calls>0,"malformed finalizer did not latch both failures/fatal sink");
            check(!next(),"following bound original was admitted after finalizer refusal");
        }
        void OnOtherThread(Action body)
        {
            Exception? error=null;var thread=new Thread(()=>{try{body();}catch(Exception e){error=e;}});
            thread.Start();thread.Join();if(error is not null)throw new Exception("Shared callback worker failed.",error);
        }
        foreach(var exceptional in new[]{false,true})
        {
            test("common finalizer rejects missing bound token "+(exceptional?"with exception":"without exception"),()=>{
                var f=Awake();Exception? original=exceptional?new InvalidOperationException("original fault"):null;
                var result=FinalizerDispatch.Complete(f.Adapter,CallbackKind.Awake,100,null,original);
                check(ReferenceEquals(result,original),"original exception changed");
                Refused(f,()=>f.Adapter.CoroutineEntered(100,250,out _));
            });
            test("common finalizer never infers unrelated traffic from null "+(exceptional?"with exception":"without exception"),()=>{
                var f=Awake();Exception? original=exceptional?new InvalidOperationException("foreign original fault"):null;
                var result=FinalizerDispatch.Complete(f.Adapter,CallbackKind.Coroutine,999,null,original);
                check(ReferenceEquals(result,original),"original exception changed");
                Refused(f,()=>f.Adapter.CoroutineEntered(100,250,out _));
            });
        }
        test("common finalizer rejects a foreign issued bound token",()=>{
            var a=Awake();var b=Awake();
            FinalizerDispatch.Complete(b.Adapter,CallbackKind.Awake,100,a.AwakeFrame,null);
            Refused(b,()=>b.Adapter.CoroutineEntered(100,250,out _));
        });
        test("common finalizer preserves a bound original exception and denies following originals",()=>{
            var f=Awake();var original=new InvalidOperationException("real-shaped original exception");
            check(ReferenceEquals(FinalizerDispatch.Complete(f.Adapter,CallbackKind.Awake,100,f.AwakeFrame,original),original),"exception was replaced/suppressed");
            Refused(f,()=>f.Adapter.CoroutineEntered(100,250,out _));
        });
        test("common finalizer rejects missing original postfix for issued bound token",()=>{
            var f=Awake();FinalizerDispatch.Complete(f.Adapter,CallbackKind.Awake,100,f.AwakeFrame,null);
            Refused(f,()=>f.Adapter.CoroutineEntered(100,250,out _));
        });
        test("valid bound postfix then common finalizer runs once",()=>{
            var f=new Fixture(output);f.BeforeYield();f.NativeYield();Fixture.Must(f.Adapter.Returned(f.InitFrame,CallbackKind.Init,250,true,true));
            check(FinalizerDispatch.Complete(f.Adapter,CallbackKind.Init,250,f.InitFrame,null) is null && f.Adapter.Failure is null && f.Fatal.Calls==0,"valid finalizer refused");
            Fixture.Must(f.Adapter.IteratorEntered(300,NativeKind.Read,out _));
        });
        test("common finalizer rejects duplicate previously finalized bound token",()=>{
            var f=new Fixture(output);f.BeforeYield();f.NativeYield();f.Return(f.InitFrame,CallbackKind.Init,250,true,true);
            FinalizerDispatch.Complete(f.Adapter,CallbackKind.Init,250,f.InitFrame,null);
            Refused(f,()=>f.Adapter.IteratorEntered(300,NativeKind.Read,out _));
        });
        test("common finalizer rejects returned token for different actual seam",()=>{
            var f=new Fixture(output);f.BeforeYield();f.NativeYield();Fixture.Must(f.Adapter.Returned(f.InitFrame,CallbackKind.Init,250,true,true));
            FinalizerDispatch.Complete(f.Adapter,CallbackKind.Read,250,f.InitFrame,null);
            Refused(f,()=>f.Adapter.IteratorEntered(300,NativeKind.Read,out _));
        });
        test("common finalizer rejects returned token for different actual receiver",()=>{
            var f=new Fixture(output);f.BeforeYield();f.NativeYield();Fixture.Must(f.Adapter.Returned(f.InitFrame,CallbackKind.Init,250,true,true));
            FinalizerDispatch.Complete(f.Adapter,CallbackKind.Init,251,f.InitFrame,null);
            Refused(f,()=>f.Adapter.IteratorEntered(300,NativeKind.Read,out _));
        });
        test("unrelated shared completion carries a positive token without policy events",()=>{
            var f=Awake();var routes=f.Admission.Routes.Count;var frames=f.Adapter.OpenFrames;
            Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));check(token?.Disposition==CallbackDisposition.UnrelatedShared,"unrelated shared call has no explicit identity/disposition");
            Fixture.Must(f.Adapter.Returned(token,CallbackKind.Coroutine,999,true));
            check(FinalizerDispatch.Complete(f.Adapter,CallbackKind.Coroutine,999,token,null) is null,"unexpected exception");
            check(f.Adapter.Failure is null && f.Fatal.Calls==0 && f.Admission.Routes.Count==routes && f.Adapter.OpenFrames==frames,"unrelated shared call changed policy/lifecycle state");
            Fixture.Must(f.Adapter.CoroutineEntered(100,250,out _));
        });
        test("unrelated shared original exception is preserved through common dispatch",()=>{
            var f=Awake();var routes=f.Admission.Routes.Count;var frames=f.Adapter.OpenFrames;var original=new InvalidOperationException("unrelated original exception");
            Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));check(token?.Disposition==CallbackDisposition.UnrelatedShared,"unrelated exception lacks explicit disposition");
            check(ReferenceEquals(FinalizerDispatch.Complete(f.Adapter,CallbackKind.Coroutine,999,token,original),original),"unrelated exception was replaced/suppressed");
            check(f.Adapter.Failure is null && f.Fatal.Calls==0 && f.Admission.Routes.Count==routes && f.Adapter.OpenFrames==frames,"unrelated original failure was relabeled as a bound failure");
            Fixture.Must(f.Adapter.CoroutineEntered(100,250,out _));
        });
        test("common finalizer rejects foreign unrelated-shared token with matching fields",()=>{
            var a=Awake();var b=Awake();Fixture.Must(a.Adapter.CoroutineEntered(999,-999,out var foreign));Fixture.Must(b.Adapter.CoroutineEntered(999,-999,out _));
            check(foreign is not null,"unrelated token absent");
            FinalizerDispatch.Complete(b.Adapter,CallbackKind.Coroutine,999,foreign,new InvalidOperationException("foreign failure"));
            Refused(b,()=>b.Adapter.CoroutineEntered(100,250,out _));
        });
        test("common finalizer rejects duplicate unrelated-shared token",()=>{
            var f=Awake();Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));check(token is not null,"unrelated token absent");
            f.Return(token,CallbackKind.Coroutine,999,true);FinalizerDispatch.Complete(f.Adapter,CallbackKind.Coroutine,999,token,null);
            Refused(f,()=>f.Adapter.CoroutineEntered(100,250,out _));
        });
        test("quiescent close refuses an original return without its finalizer",()=>{
            var f=new Fixture(output);f.Start();Fixture.Must(f.Adapter.IteratorEntered(400,NativeKind.Save,out var token));
            Fixture.Must(f.Adapter.Returned(token,CallbackKind.Save,400,true,true));
            check(!f.Adapter.Close() && f.Adapter.Failure is not null,"close accepted a missing finalizer");
        });
        test("close refuses active unrelated shared callback on the owning thread",()=>{
            var f=new Fixture(output);f.Start();var snapshot=f.Native.Settings;var writes=f.Native.Writes.Count;var routes=f.Admission.Routes.Count;
            Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));check(token?.Disposition==CallbackDisposition.UnrelatedShared,"expected issued shared token");
            check(!f.Adapter.Close(),"close accepted an active shared callback");
            check(f.Adapter.Failure is not null && f.Adapter.Policy.Failure is not null && f.Fatal.Calls>0,"close refusal did not retain existing failure behavior");
            check(f.Native.Settings==snapshot && f.Native.Writes.Count==writes && f.Admission.Routes.Count==routes,"close refusal changed settings or routing");
        });
        foreach(var returned in new[]{false,true})test("close refuses other-thread shared callback "+(returned?"awaiting finalizer":"before original return"),()=>{
            var f=new Fixture(output);f.Start();AdapterFrame? token=null;
            OnOtherThread(()=>{Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out token));if(returned)Fixture.Must(f.Adapter.Returned(token,CallbackKind.Coroutine,999,true));});
            check(token is not null && token.ManagedThreadId!=Environment.CurrentManagedThreadId,"shared token did not originate on another managed thread");
            check(!f.Adapter.Close() && f.Adapter.Failure is not null,"close overlooked another thread's unfinished shared callback");
        });
        test("close still refuses retained outer shared callback after inner finalizer",()=>{
            var f=new Fixture(output);f.Start();Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out _));Fixture.Must(f.Adapter.CoroutineEntered(998,-998,out var inner));
            f.Return(inner,CallbackKind.Coroutine,998,true);
            check(!f.Adapter.Close() && f.Adapter.Failure is not null,"inner finalizer concealed an active outer shared callback");
        });
        test("close succeeds after shared normal finalizer retires the invocation",()=>{
            var f=new Fixture(output);f.Start();Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));f.Return(token,CallbackKind.Coroutine,999,true);
            check(f.Adapter.Close() && f.Adapter.Failure is null && f.Fatal.Calls==0,"completed shared callback prevented close");
        });
        test("close succeeds after shared exceptional finalizer preserves the original exception",()=>{
            var f=new Fixture(output);f.Start();Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));var original=new InvalidOperationException("unrelated original failure");
            check(ReferenceEquals(FinalizerDispatch.Complete(f.Adapter,CallbackKind.Coroutine,999,token,original),original),"shared original exception changed");
            check(f.Adapter.Close() && f.Adapter.Failure is null && f.Fatal.Calls==0,"exceptionally finalized shared callback prevented close");
        });
        test("close succeeds after another thread's shared finalizer retires its completion stack",()=>{
            var f=new Fixture(output);f.Start();OnOtherThread(()=>{Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var token));f.Return(token,CallbackKind.Coroutine,999,true);});
            check(f.Adapter.Close() && f.Adapter.Failure is null && f.Fatal.Calls==0,"retired other-thread completion stack prevented close");
        });
        test("close succeeds only after all nested shared callbacks finalize",()=>{
            var f=new Fixture(output);f.Start();Fixture.Must(f.Adapter.CoroutineEntered(999,-999,out var outer));Fixture.Must(f.Adapter.CoroutineEntered(998,-998,out var inner));
            f.Return(inner,CallbackKind.Coroutine,998,true);f.Return(outer,CallbackKind.Coroutine,999,true);
            check(f.Adapter.Close() && f.Adapter.Failure is null && f.Fatal.Calls==0,"fully finalized nested shared callbacks prevented close");
        });
        test("pending finalizer blocks next bound original before its native effects",()=>{
            var f=new Fixture(output);f.BeforeYield();f.NativeYield();Fixture.Must(f.Adapter.Returned(f.InitFrame,CallbackKind.Init,250,true,true));
            check(!f.Adapter.IteratorEntered(300,NativeKind.Read,out _),"next bound original passed a pending finalizer");
            Refused(f,()=>f.Adapter.IteratorEntered(300,NativeKind.Read,out _));
        });
        test("common finalizer rejects an issued outer token while inner original remains active",()=>{
            var f=new Fixture(output);f.BeforeYield();var original=new InvalidOperationException("out-of-order exception");
            check(ReferenceEquals(FinalizerDispatch.Complete(f.Adapter,CallbackKind.Awake,100,f.AwakeFrame,original),original),"original exception changed");
            Refused(f,()=>f.Adapter.IteratorEntered(300,NativeKind.Read,out _));
        });
    }
}
