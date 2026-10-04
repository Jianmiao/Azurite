using System;
using Azurite.Core;

namespace Azurite;

internal readonly record struct DisplayRatePolicy(double ReportedRate, int RefreshRate, int FirstRate, int SecondRate)
{
    private const double DividerTolerance = 0.01;

    internal static bool TryCreate(double reportedRate, out DisplayRatePolicy policy)
    {
        policy = default;
        if (!double.IsFinite(reportedRate) || reportedRate < 20.0 || reportedRate > 1000.0)
            return false;

        int refreshRate = (int)Math.Round(reportedRate, MidpointRounding.AwayFromZero);
        if (refreshRate < 20 || refreshRate > 1000)
            return false;

        int firstRate = refreshRate >= 120
            ? 60
            : Math.Max(1, (int)Math.Round(refreshRate / 2.0, MidpointRounding.AwayFromZero));
        int secondRate = refreshRate >= 120 ? 120 : refreshRate;
        policy = new DisplayRatePolicy(reportedRate, refreshRate, firstRate, secondRate);
        return true;
    }

    internal FpsPlan PlanForTier(int tier)
    {
        if (tier == 2) return new FpsPlan(-1, 1);
        int targetRate = tier switch
        {
            0 => FirstRate,
            1 => SecondRate,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };

        double ratio = ReportedRate / targetRate;
        int divider = (int)Math.Round(ratio, MidpointRounding.AwayFromZero);
        if (divider is >= 1 and <= 4 && Math.Abs(ratio - divider) <= DividerTolerance)
            return new FpsPlan(-1, divider);
        return new FpsPlan(targetRate, 0);
    }

    internal static FpsPlan ConservativeFallback(int tier) => tier switch
    {
        0 => new FpsPlan(30, 0),
        1 => new FpsPlan(60, 0),
        2 => new FpsPlan(-1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };
}
