using System.Diagnostics;

namespace Azurite.NormalEditor.Entry;

// One caller-owned epoch for both the journal identity and native callback stamps.
// Explicit anchor values are data, not authentication of their physical source.
public sealed class CatalogClockAnchor
{
    public CatalogClockAnchor(DateTimeOffset observerStartedUtc,long monotonicStartedTimestamp,long monotonicFrequency)
    {
        if(observerStartedUtc==default||observerStartedUtc.Offset!=TimeSpan.Zero)throw new ArgumentException("An explicit UTC observer origin is required.",nameof(observerStartedUtc));
        if(monotonicStartedTimestamp<0)throw new ArgumentOutOfRangeException(nameof(monotonicStartedTimestamp));
        if(monotonicFrequency<=0)throw new ArgumentOutOfRangeException(nameof(monotonicFrequency));
        ObserverStartedUtc=observerStartedUtc;MonotonicStartedTimestamp=monotonicStartedTimestamp;MonotonicFrequency=monotonicFrequency;
    }
    public DateTimeOffset ObserverStartedUtc {get;}
    public long MonotonicStartedTimestamp {get;}
    public long MonotonicFrequency {get;}
    public static CatalogClockAnchor Capture()
    {
        var timestamp=Stopwatch.GetTimestamp();var utc=DateTimeOffset.UtcNow;
        return new(utc,timestamp,Stopwatch.Frequency);
    }
    public long ElapsedMillisecondsAt(long timestamp,long frequency)
    {
        if(frequency!=MonotonicFrequency)throw new InvalidOperationException("Sampled monotonic clock frequency differs from the observer anchor.");
        if(timestamp<MonotonicStartedTimestamp)throw new InvalidOperationException("Sampled monotonic clock precedes the observer anchor.");
        return checked((long)(((decimal)timestamp-MonotonicStartedTimestamp)*1000/MonotonicFrequency));
    }
}
