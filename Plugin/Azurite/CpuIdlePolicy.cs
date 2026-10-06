using System;

namespace Azurite;

internal sealed class CpuIdlePolicy
{
    private double _quietSince = double.NaN;
    private double _lastNow = double.NaN;

    internal int Resolve(double now, bool eligible, bool focused, double delay, int foregroundFps, int backgroundFps)
    {
        if (!double.IsFinite(now) || now < 0 || !double.IsFinite(delay) || delay < .5 || delay > 30 ||
            foregroundFps < 15 || foregroundFps > 60 || backgroundFps < 10 || backgroundFps > foregroundFps ||
            (double.IsFinite(_lastNow) && now < _lastNow))
        {
            Reset();
            return 0;
        }
        _lastNow = now;
        if (!eligible) { _quietSince = double.NaN; return 0; }
        if (!double.IsFinite(_quietSince)) _quietSince = now;
        return now - _quietSince >= delay ? (focused ? foregroundFps : backgroundFps) : 0;
    }

    internal void Reset() { _quietSince = _lastNow = double.NaN; }
}
