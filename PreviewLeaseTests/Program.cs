using Azurite;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

int passed = 0, failed = 0;
void Check(bool result, string description)
{
    if (result) { passed++; Console.WriteLine("PASS " + description); }
    else { failed++; Console.WriteLine("FAIL " + description); }
}

using (var fixture = new Fixture())
{
    int allocations = RenderTexture.Allocations;
    fixture.Lease.Update(0, true);
    fixture.Lease.Update(0.49, true);
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "viewport must settle before ownership changes");
    fixture.Lease.Update(0.61, true);
    var owned = fixture.Camera.targetTexture!;
    Check(!ReferenceEquals(owned, fixture.Original) && ReferenceEquals(owned, fixture.Consumer.mainTexture) &&
          owned.width == 960 && owned.height == 540 && owned.Created,
          "actual lease creates and binds a shared smaller target after settling");
    Check(owned.descriptor.graphicsFormat == 42 && owned.descriptor.depthBufferBits == 24 && owned.descriptor.msaaSamples == 4 &&
          owned.filterMode == FilterMode.Trilinear && owned.wrapModeU == TextureWrapMode.Clamp && owned.anisoLevel == 2,
          "replacement retains format depth samples and filtering");
    for (int i = 7; i < 40; i++) fixture.Lease.Update(i / 10.0, true);
    Check(ReferenceEquals(owned, fixture.Camera.targetTexture) && RenderTexture.Allocations == allocations + 1 && owned.ReleaseCalls == 0,
          "stable input or animation with resolution ownership allowed does not reallocate across discovery polls");
    Check(fixture.Lease.Restore() && ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) &&
          ReferenceEquals(fixture.Consumer.mainTexture, fixture.Original) && owned.Destroyed && owned.ReleaseCalls == 1,
          "export-style restore returns both bindings before releasing the owned target");
    Check(fixture.Lease.Restore() && owned.ReleaseCalls == 1, "restore is idempotent");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    fixture.Lease.Update(0.7, false);
    Check(owned.Destroyed && ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "disabling restores immediately");
    fixture.Lease.Update(0.8, true);
    fixture.Lease.Update(1.4, true);
    Check(!ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && !fixture.Camera.targetTexture!.Destroyed,
          "re-enabling can acquire a new lease after stable viewport measurement");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    fixture.Camera.enabled = false;
    fixture.Lease.Update(0.7, true, fixture.Camera);
    fixture.Lease.Update(1.1, true, fixture.Camera);
    Check(ReferenceEquals(fixture.Camera.targetTexture, owned) && !owned.Destroyed,
          "only the exact cadence-suspended camera remains a valid texture producer");
    fixture.Lease.Update(1.2, true, new Camera());
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && owned.Destroyed,
          "an unrelated disabled-camera token cannot keep a texture lease");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    fixture.Preview.previewMode = false;
    fixture.Lease.Update(0.7, true);
    Check(owned.Destroyed && ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "leaving preview mode restores before the next discovery scan");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    fixture.Inspector.loading = true;
    fixture.Lease.Update(0.7, true);
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "native inspector loading releases owned texture");
    fixture.Inspector.loading = false;
    fixture.Lease.Update(0.8, true);
    fixture.Lease.Update(1.4, true);
    Check(!ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "a settled inspector resumes after loading completes");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    var external = Fixture.MakeTarget("External");
    fixture.Camera.targetTexture = external;
    fixture.Lease.Update(0.7, true);
    Check(ReferenceEquals(fixture.Camera.targetTexture, external) && ReferenceEquals(fixture.Consumer.mainTexture, fixture.Original) && owned.Destroyed,
          "external camera rebind is preserved while owned consumer is restored");
    fixture.Camera.targetTexture = fixture.Original;
    fixture.Lease.Update(2, true);
    fixture.Lease.Update(3, true);
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "external ownership conflict blocks further replacement for the lease session");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    var external = Fixture.MakeTarget("External UI");
    fixture.Consumer.mainTexture = external;
    fixture.Lease.Update(0.7, true);
    Check(ReferenceEquals(fixture.Consumer.mainTexture, external) && ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && owned.Destroyed,
          "external UI rebind is not overwritten by restoration");
}

using (var fixture = new Fixture())
{
    fixture.Inspector.Textures.Add(new UITexture { mainTexture = fixture.Original });
    fixture.Activate();
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "multiple original consumers reject replacement");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    var foreign = new UITexture { mainTexture = owned };
    fixture.Inspector.Textures.Add(foreign);
    fixture.Lease.Update(1.1, true);
    Check(!fixture.Lease.Restore() && !owned.Destroyed && owned.ReleaseCalls == 0 &&
          ReferenceEquals(foreign.mainTexture, owned) && ReferenceEquals(fixture.Camera.targetTexture, fixture.Original),
          "discovered foreign target consumer prevents destruction and reports incomplete handoff");
    foreign.mainTexture = fixture.Original;
    Check(fixture.Lease.Restore() && owned.Destroyed && owned.ReleaseCalls == 1,
          "owned texture can be released once a foreign consumer stops using it");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    var foreign = new UITexture { mainTexture = owned };
    fixture.Inspector.Textures.Add(foreign);
    Check(!fixture.Lease.Restore() && !owned.Destroyed && owned.ReleaseCalls == 0,
          "export handoff detects a new foreign consumer even before the next periodic discovery");
    foreign.mainTexture = fixture.Original;
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    fixture.Consumer.worldCorners = new(new[]
    {
        new Vector3(0, 0, 1), new Vector3(0, 720, 1),
        new Vector3(1280, 720, 1), new Vector3(1280, 0, 1)
    });
    fixture.Lease.Update(0.8, true);
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && owned.Destroyed,
          "viewport growth first restores the full-quality original");
    fixture.Lease.Update(1.4, true);
    Check(fixture.Camera.targetTexture!.width == 1280 && fixture.Camera.targetTexture.height == 720,
          "larger viewport receives a fresh correctly sized lease only after settling");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    var replacementOriginal = Fixture.MakeTarget("Preview");
    var replacementCamera = new Camera { targetTexture = replacementOriginal };
    fixture.Preview.background!.anchorCamera = replacementCamera;
    fixture.Consumer.mainTexture = replacementOriginal;
    fixture.Lease.Update(1.1, true);
    fixture.Lease.Update(1.7, true);
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && owned.Destroyed &&
          !ReferenceEquals(replacementCamera.targetTexture, replacementOriginal) &&
          ReferenceEquals(replacementCamera.targetTexture, fixture.Consumer.mainTexture),
          "valid camera replacement restores the old owner and binds the new preview");
    fixture.Lease.Restore();
    Check(ReferenceEquals(replacementCamera.targetTexture, replacementOriginal) && ReferenceEquals(fixture.Consumer.mainTexture, replacementOriginal),
          "replacement camera is restored to its own original target");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var replacementOriginal = Fixture.MakeTarget("ExternalOwner");
    var replacementCamera = new Camera { targetTexture = replacementOriginal };
    fixture.Preview.background!.anchorCamera = replacementCamera;
    fixture.Consumer.mainTexture = replacementOriginal;
    fixture.Lease.Update(1.1, true);
    fixture.Lease.Update(1.7, true);
    Check(ReferenceEquals(replacementCamera.targetTexture, replacementOriginal) && ReferenceEquals(fixture.Consumer.mainTexture, replacementOriginal),
          "camera replacement must revalidate the original target name before ownership transfer");
}

foreach (var scenario in new[] { "stack", "overlay", "scene", "viewport", "uv", "descriptor" })
{
    using var fixture = new Fixture();
    switch (scenario)
    {
        case "stack": fixture.Camera.Data!.cameraStack.Add(new Camera()); break;
        case "overlay": fixture.Camera.Data!.renderType = CameraRenderType.Overlay; break;
        case "scene": fixture.Camera.gameObject.scene = new Scene("Appreciation"); break;
        case "viewport": fixture.Camera.rect = new Rect(0, 0, 0.5f, 1); break;
        case "uv": fixture.Consumer.uvRect = new Rect(0, 0, 1, 0.5f); break;
        case "descriptor":
            var descriptor = fixture.Original.descriptor;
            descriptor.enableRandomWrite = true;
            fixture.Original.descriptor = descriptor;
            break;
    }
    int allocations = RenderTexture.Allocations;
    fixture.Activate();
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && RenderTexture.Allocations == allocations,
          scenario + " incompatibility rejects without allocating or changing bindings");
}

using (var fixture = new Fixture())
{
    RenderTexture.FailNextCreate = true;
    fixture.Activate();
    Check(ReferenceEquals(fixture.Camera.targetTexture, fixture.Original) && ReferenceEquals(fixture.Consumer.mainTexture, fixture.Original),
          "failed native allocation leaves original bindings intact");
}

using (var fixture = new Fixture())
{
    fixture.Activate();
    var owned = fixture.Camera.targetTexture!;
    fixture.Lease.Dispose();
    fixture.Lease.Update(2, true);
    Check(owned.Destroyed && ReferenceEquals(fixture.Camera.targetTexture, fixture.Original), "dispose restores and prevents reacquisition");
}

using (var fixture = new Fixture())
{
    var registry = new PreviewTextureRegistry();
    var first = registry.Read(fixture.Inspector, 10);
    var second = registry.Read(fixture.Inspector, 10.2);
    Check(ReferenceEquals(first, second) && fixture.Inspector.Enumerations == 1, "cadence and target can share one hierarchy scan within the discovery window");
    var foreign = new UITexture { mainTexture = fixture.Original };
    fixture.Inspector.Textures.Add(foreign);
    Check(registry.Read(fixture.Inspector, 10.3, force: true)!.Length == 2 && fixture.Inspector.Enumerations == 2, "resource release forces a live scan before freeing a target");
    registry.Read(fixture.Inspector, 9);
    Check(fixture.Inspector.Enumerations == 3, "clock rollback refreshes the hierarchy snapshot");
    var replacement = new ScriptNodeInspector();
    registry.Read(replacement, 9);
    Check(replacement.Enumerations == 1, "new inspector identity does not inherit the old snapshot");
    registry.Clear(); registry.Read(replacement, 9.1);
    Check(replacement.Enumerations == 2, "release and scene reset drop resident texture references");
}
Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public ScriptNodeInspector Inspector { get; } = new();
    public Camera Camera { get; } = new();
    public RenderTexture Original { get; } = MakeTarget("Preview");
    public UITexture Consumer { get; } = new() { anchorCamera = new Camera() };
    public Test Preview { get; } = new();
    public PreviewTargetLease Lease { get; } = new();
    public Fixture()
    {
        ScriptNodeInspector.instance = Inspector;
        Camera.targetTexture = Original;
        Consumer.mainTexture = Original;
        Inspector.Textures.Add(Consumer);
        Preview.background = new UITexture { anchorCamera = Camera };
        Inspector.preview = Preview;
    }
    public void Activate() { Lease.Update(0, true); Lease.Update(0.6, true); }
    public void Dispose() { Lease.Dispose(); ScriptNodeInspector.instance = null; }
    public static RenderTexture MakeTarget(string name) => new(new RenderTextureDescriptor
    {
        width = 1920, height = 1080, dimension = TextureDimension.Tex2D, volumeDepth = 1,
        depthBufferBits = 24, msaaSamples = 4, graphicsFormat = 42
    })
    {
        name = name, filterMode = FilterMode.Trilinear, wrapModeU = TextureWrapMode.Clamp,
        wrapModeV = TextureWrapMode.Mirror, anisoLevel = 2
    };
}
