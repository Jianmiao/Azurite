using System.Diagnostics;
using Azurite.NormalEditor.Entry;

static class ClockAnchorChecks
{
    internal static void Run(Action<string,Action> test,Action<bool,string> need)
    {
        var utc=new DateTimeOffset(2026,11,13,1,2,3,TimeSpan.Zero);
        void Refuses(Action action){try{action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}catch(OverflowException){return;}throw new Exception("Invalid clock was accepted");}
        test("captured UTC and monotonic anchors belong to the same capture window",()=>{
            var beforeUtc=DateTimeOffset.UtcNow;var before=Stopwatch.GetTimestamp();var clock=CatalogClockAnchor.Capture();var after=Stopwatch.GetTimestamp();var afterUtc=DateTimeOffset.UtcNow;
            need(clock.ObserverStartedUtc.Offset==TimeSpan.Zero&&clock.ObserverStartedUtc>=beforeUtc&&clock.ObserverStartedUtc<=afterUtc,"UTC origin was not captured");
            need(clock.MonotonicStartedTimestamp>=before&&clock.MonotonicStartedTimestamp<=after&&clock.MonotonicFrequency==Stopwatch.Frequency,"Monotonic origin/source differs");
            need(clock.ElapsedMillisecondsAt(after,Stopwatch.Frequency)>=0,"Captured anchor cannot read its own clock");
        });
        test("a shared anchor converts elapsed time to the caller's exact UTC origin",()=>{
            var clock=new CatalogClockAnchor(utc,10000,1000);
            need(clock.ElapsedMillisecondsAt(11250,1000)==1250,"Elapsed time was rebased");
            need(clock.ObserverStartedUtc.AddMilliseconds(clock.ElapsedMillisecondsAt(11250,1000))==new DateTimeOffset(2026,11,13,1,2,4,250,TimeSpan.Zero),"UTC identity and elapsed time disagree");
            need(clock.MonotonicStartedTimestamp==10000&&clock.MonotonicFrequency==1000&&clock.ObserverStartedUtc==utc,"Conversion changed the anchor");
        });
        test("elapsed milliseconds truncate fractional ticks and include exact origin",()=>{
            var clock=new CatalogClockAnchor(utc,100,3);
            need(clock.ElapsedMillisecondsAt(100,3)==0&&clock.ElapsedMillisecondsAt(101,3)==333&&clock.ElapsedMillisecondsAt(103,3)==1000,"Elapsed millisecond precision differs");
        });
        test("large absolute counters retain small exact elapsed values",()=>{
            var clock=new CatalogClockAnchor(utc,long.MaxValue-50,1000);
            need(clock.ElapsedMillisecondsAt(long.MaxValue,1000)==50,"Counter subtraction overflowed or lost precision");
        });
        test("maximum representable elapsed milliseconds do not overflow intermediate arithmetic",()=>{
            var clock=new CatalogClockAnchor(utc,0,1000);
            need(clock.ElapsedMillisecondsAt(long.MaxValue,1000)==long.MaxValue,"Representable elapsed value was lost");
        });
        test("delayed registration cannot restart the readiness deadline",()=>{
            var clock=new CatalogClockAnchor(utc,10000,1000);var f=new Fixture();
            var stamp=f.Stamp(1,clock.ElapsedMillisecondsAt(10101,1000));f.Gate.StartEntered(stamp);
            need(f.Gate.Failure is not null&&!f.Gate.Ready,"Registration delay was excluded from the clock");
        });
        test("shared clock reversal is rejected by the actual readiness coordinator",()=>{
            var clock=new CatalogClockAnchor(utc,10000,1000);var f=new Fixture();
            var frame=f.Gate.StartEntered(f.Stamp(10,clock.ElapsedMillisecondsAt(10010,1000)));
            f.Gate.StartReturned(frame,f.Stamp(10,clock.ElapsedMillisecondsAt(10009,1000)),true);
            need(f.Gate.Failure is not null&&!f.Gate.Ready,"Reversed elapsed time was accepted");
        });
        test("non-UTC clock origin is rejected",()=>Refuses(()=>new CatalogClockAnchor(utc.ToOffset(TimeSpan.FromHours(8)),100,1000)));
        test("missing UTC clock origin is rejected",()=>Refuses(()=>new CatalogClockAnchor(default,100,1000)));
        test("negative monotonic origin is rejected",()=>Refuses(()=>new CatalogClockAnchor(utc,-1,1000)));
        foreach(var frequency in new[]{0L,-1L})test("invalid anchor frequency is rejected: "+frequency,()=>Refuses(()=>new CatalogClockAnchor(utc,100,frequency)));
        foreach(var frequency in new[]{0L,-1L,999L})test("different sampled clock frequency is rejected: "+frequency,()=>Refuses(()=>new CatalogClockAnchor(utc,100,1000).ElapsedMillisecondsAt(101,frequency)));
        test("sample before the monotonic origin is rejected",()=>Refuses(()=>new CatalogClockAnchor(utc,100,1000).ElapsedMillisecondsAt(99,1000)));
        test("negative sampled counter is rejected",()=>Refuses(()=>new CatalogClockAnchor(utc,0,1000).ElapsedMillisecondsAt(-1,1000)));
        test("unrepresentable elapsed milliseconds fail without wrapping",()=>Refuses(()=>new CatalogClockAnchor(utc,0,1).ElapsedMillisecondsAt(long.MaxValue,1)));
    }
}
