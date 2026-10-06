namespace Azurite;

// A supported host and an explicit experiment opt-in are both required. The
// camera/texture leases still have to verify their live binding independently.
internal static class PreviewOptimizationPolicy
{
	internal static bool AllowsScopedOwnership(HostProfile profile, bool experimentalOptIn) =>
		profile.Supported && profile.PreviewOptimization && (profile.PreviewOwnership || experimentalOptIn);
}
