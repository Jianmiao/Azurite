using UnityEngine;

namespace Azurite;

internal static class CurrentDisplayRate
{
    internal static bool TryRead(out DisplayRatePolicy policy)
    {
        try
        {
            return DisplayRatePolicy.TryCreate(Screen.currentResolution.refreshRateRatio.value, out policy);
        }
        catch
        {
            policy = default;
            return false;
        }
    }
}
