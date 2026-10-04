namespace Azurite;

internal readonly record struct FpsLabelValues(string? Thirty, string? Sixty, string? Infinity)
{
    internal FpsLabelValues Map(bool enabled)
    {
        if (!enabled) return this;
        return new FpsLabelValues(
            Thirty == null ? null : "60",
            Sixty == null ? null : "120",
            Infinity == null ? null : "∞");
    }
}
