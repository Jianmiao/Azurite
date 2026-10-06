namespace Azurite;

/// <summary>
/// Decides whether the opt-in row-layout experiment may run. The experiment
/// can only be used on a verified host and a live mutation gate; otherwise the
/// native list lifecycle remains untouched.
/// </summary>
internal static class LargeProjectOptimizationPolicy
{
	internal static bool CanEnableLayoutCache(bool hostVerified, bool nativePatches, bool mutationGateInstalled, bool configured, bool exporting)
	{
		if (!hostVerified || !configured || exporting)
		{
			return false;
		}
		return nativePatches || mutationGateInstalled;
	}
}
