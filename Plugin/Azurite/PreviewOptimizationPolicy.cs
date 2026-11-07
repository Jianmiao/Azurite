namespace Azurite;

// A supported host and an explicit experiment opt-in are both required. The
// camera/texture leases still have to verify their live binding independently.
internal static class PreviewOptimizationPolicy
{
	internal static bool AllowsScopedOwnership(HostProfile profile, bool experimentalOptIn) =>
		profile.Supported && profile.PreviewOptimization && (profile.PreviewOwnership || experimentalOptIn);

	// Static camera caching relies on the inspected progressive-editor lifecycle
	// and is limited to the portable Fix host. Legacy hosts keep their existing
	// preview cadence even when the shared config enables the setting.
	internal static bool AllowsStaticCache(HostProfile profile, bool experimentalOptIn, bool cacheEnabled) =>
		cacheEnabled && profile.ProgressiveEditorLoading && AllowsScopedOwnership(profile, experimentalOptIn);
}
