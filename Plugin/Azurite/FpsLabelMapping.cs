namespace Azurite;

internal readonly record struct FpsLabelValues(string? Thirty, string? Sixty, string? Infinity)
{
    internal FpsLabelValues Map(bool enabled, DisplayRatePolicy display)
    {
        if (!enabled) return this;
        return new FpsLabelValues(
            Thirty == null ? null : display.FirstRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Sixty == null ? null : display.SecondRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Infinity == null ? null : "∞");
    }
}
