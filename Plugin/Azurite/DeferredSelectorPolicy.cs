using BepInEx.Configuration;

namespace Azurite;

// Only admission of new deferrals changes. Existing pending native demand is
// owned by DeferredEditorSelectors and must not be flushed or discarded here.
internal static class DeferredSelectorPolicy
{
    internal static ConfigEntry<bool> Bind(ConfigFile config) =>
        config.Bind("Large Projects", "DeferEditorSelectors", false,
            "Experimental verified-host deferral of unused character, background and popup selector construction until first demand. First-open construction remains native and synchronous. Default off; native and performance acceptance pending.");

    internal static bool Install(DeferredEditorSelectors selectors, bool initialized, HostProfile profile,
        bool configured, bool globallyEnabled, bool disabledForSession, HostCompatibilitySnapshot snapshot, bool exportSuspended)
    {
        Apply(selectors, configured && initialized, globallyEnabled, disabledForSession, profile, snapshot, exportSuspended);
        return initialized && profile.Supported && profile.EditorPatches && !profile.LegacyExporter && selectors.Install();
    }

    internal static void Apply(DeferredEditorSelectors? selectors, bool configured, bool globallyEnabled,
        bool disabledForSession, HostProfile profile, HostCompatibilitySnapshot snapshot, bool exportSuspended)
    {
        if (selectors != null)
            selectors.Enabled = configured && globallyEnabled && !disabledForSession && profile.Supported &&
                profile.EditorPatches && !profile.LegacyExporter && snapshot.Supported && snapshot.ProbeHealthy &&
                !snapshot.ExportActive && !exportSuspended;
    }

    internal static void Pause(DeferredEditorSelectors? selectors)
    {
        if (selectors != null) selectors.Enabled = false;
    }
}
