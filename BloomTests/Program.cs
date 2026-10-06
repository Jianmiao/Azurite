using Azurite;
using Rendering;
using Studio.Scripts;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL: " + message);
    checks++;
}

using (var f = new Fixture())
{
    var settings = f.Feature.settings!;
    var pass = settings.PassSettings;
    f.Lease.Update(0, false);
    Check(f.Bloom.Enable && f.Bloom.Writes == 0, "default disallowed path has no writes");
    f.Lease.Update(0, true);
    Check(!f.Bloom.Enable && f.Lease.IsOwned, "eligible editor borrows only bloom Enable");
    Check(f.Feature.isActive && f.Asset.renderScale == 1 && ReferenceEquals(settings, f.Feature.settings) &&
          ReferenceEquals(pass, settings.PassSettings) && settings.PassTag == "UI pass" && f.Bloom.Intensity == 1 &&
          f.Bloom.Diffusion == 7 && f.Bloom.Threshold == 2, "renderer, settings, pass and other bloom properties untouched");
    int reads = f.Features.Reads;
    for (int i = 1; i < 100; i++) f.Lease.Update(i / 1000.0, true);
    Check(f.Bloom.Writes == 1 && f.Features.Reads == reads, "steady updates avoid repeated native writes and enumeration");
    Check(f.Lease.Restore() && f.Bloom.Enable && !f.Lease.IsOwned, "export handoff synchronously restores original Enable");
    Check(f.Lease.Restore() && f.Bloom.Writes == 2, "handoff restoration is idempotent");
    f.Lease.Update(1, true);
    Check(!f.Bloom.Enable, "editor may reacquire after completed export");
    f.Lease.Update(1.1, false);
    Check(f.Bloom.Enable, "configuration or ownership revocation restores immediately without scan delay");
}

using (var f = new Fixture())
{
    f.Bloom.Value = false;
    f.Lease.Update(0, true);
    f.Lease.Dispose();
    Check(!f.Bloom.Enable && f.Bloom.Writes == 0 && !f.Lease.IsOwned, "pre-disabled bloom is not borrowed or enabled on disposal");
}

foreach (string lifecycle in new[] { "inactive", "loading", "unloading", "missing", "scene", "replacement" })
{
    using var f = new Fixture();
    f.Lease.Update(0, true);
    switch (lifecycle)
    {
        case "inactive": f.Inspector.isActiveAndEnabled = false; break;
        case "loading": f.Inspector.loading = true; break;
        case "unloading": f.Inspector.unloading = true; break;
        case "missing": ScriptNodeInspector.instance = null; break;
        case "scene": SceneManager.ActiveHandle++; break;
        case "replacement": ScriptNodeInspector.instance = new(); break;
    }
    f.Lease.Update(0.01, true);
    Check(f.Bloom.Enable && !f.Lease.IsOwned, lifecycle + " editor transition restores without scan delay");
}

foreach (string invalid in new[] { "duplicate", "oversize", "zero", "inactive", "settings", "bloom", "pipeline", "data" })
{
    using var f = new Fixture();
    switch (invalid)
    {
        case "duplicate": f.Features.Add(new UIRenderFeature()); break;
        case "oversize": for (int i = 1; i < 17; i++) f.Features.Add(new ScriptableRendererFeature()); break;
        case "zero": f.Features.Clear(); break;
        case "inactive": f.Feature.isActive = false; break;
        case "settings": f.Feature.settings = null; break;
        case "bloom": f.Feature.settings!.BloomSettings = null; break;
        case "pipeline": GraphicsSettings.currentRenderPipeline = new RenderPipelineAsset(); break;
        case "data": f.Asset.scriptableRendererData = null; break;
    }
    f.Lease.Update(0, true);
    Check(f.Bloom.Enable && f.Bloom.Writes == 0 && !f.Lease.IsOwned, invalid + " discovery declines mutation");
    if (invalid == "oversize") Check(f.Features.Reads == 0, "more than sixteen features rejected before enumeration");
}

using (var f = new Fixture())
{
    for (int i = 1; i < 16; i++) f.Features.Add(new ScriptableRendererFeature());
    f.Lease.Update(0, true);
    Check(!f.Bloom.Enable && f.Features.Reads == 16, "exactly sixteen bounded features may contain one match");
    f.Features.Add(new UIRenderFeature());
    f.Lease.Update(0.6, true);
    Check(f.Bloom.Enable, "renderer list becoming unsupported restores original");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.Value = true; // another owner writes directly
    f.Lease.Update(0.01, true);
    Check(f.Bloom.Enable && f.Bloom.Writes == 1 && f.Lease.IsBlocked && !f.Lease.IsOwned,
          "observed external Enable change yields without overwriting its value");
    f.Bloom.Value = false;
    f.Lease.Update(2, true);
    f.Lease.Restore();
    Check(!f.Bloom.Enable && f.Bloom.Writes == 1, "yielded lease does not reacquire or restore over future external changes");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.Value = true;
    Check(f.Lease.Restore() && f.Bloom.Writes == 1 && f.Lease.IsBlocked,
          "export handoff leaves external original-value write untouched");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.Intensity = 3;
    f.Bloom.Diffusion = 4;
    f.Lease.Restore();
    Check(f.Bloom.Enable && f.Bloom.Intensity == 3 && f.Bloom.Diffusion == 4,
          "restoration preserves foreign changes to all unowned properties");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    var replacement = new MXBloomSettings();
    f.Feature.settings!.BloomSettings = replacement;
    f.Lease.Update(0.6, true);
    Check(f.Bloom.Enable && replacement.Enable && replacement.Writes == 0 && f.Lease.IsBlocked,
          "settings replacement restores old object and yields without mutating external replacement");
    f.Lease.Restore();
    Check(replacement.Enable && replacement.Writes == 0, "restoration never claims externally replaced settings");
}

foreach (string change in new[] { "pipeline", "renderer", "feature", "duplicate", "inactive" })
{
    using var f = new Fixture();
    f.Lease.Update(0, true);
    switch (change)
    {
        case "pipeline":
            GraphicsSettings.currentRenderPipeline = new UniversalRenderPipelineAsset { scriptableRendererData = f.Asset.scriptableRendererData };
            break;
        case "renderer":
            f.Asset.scriptableRendererData = new ScriptableRendererData { rendererFeatures = f.Features };
            break;
        case "feature":
            f.Features[0] = new UIRenderFeature { settings = f.Feature.settings };
            break;
        case "duplicate": f.Features.Add(new UIRenderFeature()); break;
        case "inactive": f.Feature.isActive = false; break;
    }
    f.Lease.Update(0.6, true);
    Check(f.Bloom.Enable && !f.Lease.IsOwned, change + " binding transition restores old original");
}

foreach (string fault in new[] { "before", "after", "ignored" })
{
    using var f = new Fixture();
    f.Bloom.ThrowBeforeWrite = fault == "before";
    f.Bloom.ThrowAfterWrite = fault == "after";
    f.Bloom.IgnoreNextWrite = fault == "ignored";
    f.Lease.Update(0, true);
    Check(f.Bloom.Enable && !f.Lease.IsOwned && f.Lease.IsBlocked, fault + " failed native acquisition returns to original value");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.ThrowBeforeWrite = true;
    Check(!f.Lease.Restore() && !f.Bloom.Enable && f.Lease.IsOwned,
          "failed restoration keeps ownership and reports incomplete export handoff");
    Check(f.Lease.Restore() && f.Bloom.Enable, "failed restoration can be retried safely");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.IgnoreNextWrite = true;
    Check(!f.Lease.Restore() && f.Lease.IsOwned && !f.Bloom.Enable, "restore readback failure cannot silently pass export handoff");
    Check(f.Lease.Restore() && f.Bloom.Enable, "restore readback failure retains original snapshot for retry");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.ThrowOnRead = true;
    Check(!f.Lease.Restore() && f.Lease.IsOwned, "unreadable setting prevents unsafe restore write");
    f.Bloom.ThrowOnRead = false;
    Check(f.Lease.Restore() && f.Bloom.Enable, "read failure retains lease for recovery");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Bloom.Destroyed = true;
    Check(f.Lease.Restore() && f.Bloom.Writes == 1, "destroyed settings are never written");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Lease.Dispose();
    f.Lease.Update(1, true);
    Check(f.Bloom.Enable && f.Bloom.Writes == 2, "disposal restores and permanently prevents reacquisition");
}

using (var f = new Fixture())
{
    f.Lease.Update(0, true);
    f.Lease.Update(double.NaN, true);
    Check(f.Bloom.Enable, "invalid clock observations release safely");
}

Console.WriteLine($"EditorBloomLease: {checks} checks passed (source-linked host stub tests; not GPU validation).");

sealed class Fixture : IDisposable
{
    public readonly ScriptNodeInspector Inspector = new();
    public readonly UniversalRenderPipelineAsset Asset = new();
    public readonly UIRenderFeature Feature = new();
    public readonly MXBloomSettings Bloom;
    public readonly EditorBloomLease Lease = new();
    public FeatureList Features => Asset.scriptableRendererData!.rendererFeatures!;
    public Fixture()
    {
        Bloom = Feature.settings!.BloomSettings!;
        Features.Add(Feature);
        ScriptNodeInspector.instance = Inspector;
        GraphicsSettings.currentRenderPipeline = Asset;
        SceneManager.ActiveHandle = 1;
    }
    public void Dispose()
    {
        Lease.Dispose();
        ScriptNodeInspector.instance = null;
        GraphicsSettings.currentRenderPipeline = null;
    }
}
