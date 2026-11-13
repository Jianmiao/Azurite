using Azurite.NormalEditor.Entry;

static class ForeignCallbackChecks
{
    // Real managed thread + actual production Core. No Host/native execution.
    private sealed class Worker : IDisposable
    {
        private readonly ManualResetEventSlim started=new(),done=new();
        private readonly Thread thread;
        private Exception? error;
        internal Worker(Action action)
        {
            thread=new(()=>{started.Set();try{action();}catch(Exception e){error=e;}finally{done.Set();}}){IsBackground=true};thread.Start();
            if(!started.Wait(TimeSpan.FromSeconds(2)))throw new Exception("Worker start was inconclusive.");
        }
        internal bool Returned=>done.Wait(TimeSpan.FromMilliseconds(300));
        internal void Finish(){if(!thread.Join(TimeSpan.FromSeconds(2)))throw new Exception("Managed worker did not finish after owner release.");if(error is not null)throw error;}
        public void Dispose(){Finish();started.Dispose();done.Dispose();}
    }
    internal static void Run(Action<string,Action> test,Action<bool,string> need)
    {
        foreach(string operation in new[]{"TryEnter","Leave"})test("S1 foreign "+operation+" returns while owner holds real readiness evidence lock",()=>{
            ShutdownFixture? f=null;IDisposable? scope=null;Worker? worker=null;bool quick=false,recordedWhileHeld=false;int failureThread=0;
            f=new ShutdownFixture(sink:e=>{
                if(e.Kind=="catalog-failed")failureThread=Environment.CurrentManagedThreadId;
                if(e.Kind!="catalog-ready")return;
                worker=new(()=>{if(operation=="TryEnter")need(!f!.Shutdown.TryEnter(f.Stamp(5),out _),"foreign callback accepted");else scope!.Dispose();});
                quick=worker.Returned;recordedWhileHeld=f!.Gate.Failure is not null;
            });
            need(f.Shutdown.MarkRegistered(),"registration");need(f.Shutdown.TryEnter(f.Stamp(0),out scope),"owner callback");
            f.Ready();worker!.Finish();scope!.Dispose();bool closed=f.Close();worker.Dispose();
            need(quick,"foreign operation waited for the owner evidence lock");
            need(!recordedWhileHeld,"foreign thread synchronously recorded observer failure");
            need(!closed&&!f.Shutdown.Closed&&f.CloseCalls==0&&f.Gate.Failure!.Contains("Foreign managed Catalog callback"),"owner swallowed deferred failure");
            need(failureThread==f.Binding.ManagedThreadId&&f.Events.Count(e=>e.Kind=="catalog-failed")==1,"failure was not recorded once on owner");
        });
        foreach(string phase in new[]{"verify","adapter"})foreach(string operation in new[]{"TryEnter","Leave"})test("S1 foreign "+operation+" during close "+phase+" cannot be overwritten by true",()=>{
            int failureThread=0;var f=new ShutdownFixture(sink:e=>{if(e.Kind=="catalog-failed")failureThread=Environment.CurrentManagedThreadId;});
            need(f.Shutdown.MarkRegistered(),"registration");need(f.Shutdown.TryEnter(f.Stamp(0),out var retiredScope),"scope");retiredScope!.Dispose();f.Ready();
            void Violate(){using var worker=new Worker(()=>{if(operation=="TryEnter")need(!f.Shutdown.TryEnter(f.Stamp(7),out _),"foreign accepted");else retiredScope.Dispose();});need(worker.Returned,"foreign waited for close/evidence lock");need(f.Gate.Failure is null,"foreign recorded instead of latching");}
            bool closed=f.Shutdown.TryClose(()=>f.Stamp(7),()=>{f.VerifyCalls++;if(phase=="verify")Violate();},()=>{f.CloseCalls++;if(phase=="adapter")Violate();return true;});
            need(!closed&&!f.Shutdown.Closed&&f.Gate.Failure is not null,"close overwrote a foreign violation");
            need(f.CloseCalls==(phase=="verify"?0:1),"foreign refusal invoked or skipped wrong adapter boundary");
            need(failureThread==f.Binding.ManagedThreadId,"failure sink ran on foreign thread");
        });
        test("S1 first foreign failure remains latched until owner Close consumes it",()=>{
            var f=new ShutdownFixture();need(f.Shutdown.MarkRegistered(),"registration");need(f.Shutdown.TryEnter(f.Stamp(0),out var scope),"scope");scope!.Dispose();f.Ready();
            using(var a=new Worker(()=>need(!f.Shutdown.TryEnter(f.Stamp(7),out _),"foreign accepted")))need(a.Returned,"first foreign blocked");
            using(var b=new Worker(scope.Dispose))need(b.Returned,"second foreign blocked");
            need(f.Gate.Failure is null,"foreign code entered evidence sink");need(!f.Close()&&f.CloseCalls==0,"pending fault allowed close");
            need(f.Gate.Failure=="Foreign managed Catalog callback thread/process.","first deferred failure was replaced");
        });
        test("S1 owner registration cannot swallow an earlier foreign callback failure",()=>{
            var f=new ShutdownFixture();using(var worker=new Worker(()=>need(!f.Shutdown.TryEnter(f.Stamp(0),out _),"foreign accepted")))need(worker.Returned,"foreign blocked");
            need(f.Gate.Failure is null,"foreign recorded synchronously");need(!f.Shutdown.MarkRegistered()&&f.Gate.Failure is not null&&!f.Close(),"registration erased pending failure");
        });
        test("S1 failed deferred evidence recording still refuses Close and Seal",()=>{
            int owner=Environment.CurrentManagedThreadId;int failureThread=0;var f=new ShutdownFixture(sink:e=>{if(e.Kind=="catalog-failed"){failureThread=Environment.CurrentManagedThreadId;throw new IOException("writer unavailable");}});
            need(f.Shutdown.MarkRegistered(),"registration");f.Ready();using(var worker=new Worker(()=>f.Shutdown.TryEnter(f.Stamp(7),out _)))need(worker.Returned,"foreign blocked");
            need(!f.Close()&&!f.Shutdown.Closed&&f.CloseCalls==0&&f.Gate.Failure is not null,"failed deferred writer permitted close");
            need(failureThread==owner,"deferred failure writer ran on a foreign thread");
        });
        test("S1 formally completed retirement keeps late foreign Enter and Leave inert",()=>{
            var f=new ShutdownFixture();need(f.Shutdown.MarkRegistered(),"registration");need(f.Shutdown.TryEnter(f.Stamp(0),out var scope),"scope");scope!.Dispose();f.Ready();need(f.Close(),"normal close");int count=f.Events.Count;
            using(var worker=new Worker(()=>{need(!f.Shutdown.TryEnter(f.Stamp(9),out _),"stopped callback entered");scope.Dispose();}))need(worker.Returned,"late callback blocked");
            scope.Dispose();need(f.Shutdown.Closed&&f.Gate.Failure is null&&f.Events.Count==count,"late retirement poisoned a formally stopped producer");
        });
        test("S1 close versus foreign callback race has one consistent terminal result",()=>{
            for(int i=0;i<128;i++){
                var f=new ShutdownFixture();need(f.Shutdown.MarkRegistered(),"registration");f.Ready();using var go=new ManualResetEventSlim();
                using var worker=new Worker(()=>{go.Wait();need(!f.Shutdown.TryEnter(f.Stamp(7),out _),"racing foreign callback admitted");});
                bool closed=f.Shutdown.TryClose(()=>f.Stamp(7),()=>{},()=>{go.Set();Thread.Yield();return true;});worker.Finish();
                need(closed?(f.Shutdown.Closed&&f.Gate.Failure is null):(!f.Shutdown.Closed&&f.Gate.Failure is not null),"true close raced with a retained violation or false close lost its cause");
            }
        });
    }
}
