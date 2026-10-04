using Azurite;
using Azurite.Core;
using Studio.Scripts;
using Studio.Scripts.Window.BackgroundExplorer;
using UnityEngine;
using UnityEngine.SceneManagement;

var tests = new (string Name, System.Action Run)[]
{
    ("raw wheel protects a complete one-second settle period", RawWheelHold),
    ("NGUI wheel delivered after Update wakes in LateUpdate", LateDeliveredWheel),
    ("Update phase avoids viewport scans while LateUpdate samples each viewport once", SplitFrameSampling),
    ("continuous scrolling restores cadence only once per protected interval", WakeOnlyOnProtectionEntry),
    ("inactive catalog viewports are pruned and a late wheel rediscovers the active view", ActiveCatalogViewsOnly),
    ("momentum, pending wheel, dragging and spring motion independently protect", NativeMotion),
    ("viewport clipping, position and panel replacement independently protect", ViewportChanges),
    ("focus loss does not retain foreground hold and focus regain wakes", FocusTransitions),
    ("native access exception conservatively protects until disposal", BoundaryFailure),
    ("callback replacement preserves other subscribers and fails closed", ReplacedCallback),
    ("disposal removes only our retained handler", Disposal),
    ("inactive views do not hold cadence and reactivate safely", InactiveViews),
    ("scene changes rediscover replacement views immediately", SceneTransitions),
    ("nonfinite movement and invalid time protect rather than idle", InvalidObservations),
    ("catalog, mod manager and background hierarchy views are included", OtherViewScopes),
    ("actual archived AdaptivePolicy remains every-frame during scroll at 60,120 and unlimited estimates", CorePolicyComposition)
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { ResetHost(); run(); Console.WriteLine("PASS " + name); }
    catch (System.Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length} passed. Pure host-boundary tests; no Unity, native DLLs or GPU measurements.");
return failures == 0 ? 0 : 1;

static void RawWheelHold()
{
    using var f = Fixture.Quiet();
    Input.mouseScrollDelta = new(0, 1);
    Check(f.Guard.Observe(1.1, true), "wheel must protect");
    var wakes = f.Wakes;
    Input.mouseScrollDelta = default;
    Check(f.Guard.Observe(2.099, true), "last input is still within one second");
    Check(!f.Guard.Observe(2.101, true), "quiet hold expires after one second");
    Check(f.Wakes == wakes, "holding protection need not create synthetic new input");
}

static void LateDeliveredWheel()
{
    using var f = Fixture.Quiet();
    Check(!f.Guard.Observe(2, true, sampleViewportMotion: false), "initial Update is idle");
    var wakes = f.Wakes;
    UICamera.onScroll!.Invoke(new GameObject(), -0.5f);
    Check(f.Wakes == wakes, "native event callback itself cannot touch Unity/wake");
    Check(f.Guard.Observe(2, true, sampleViewportMotion: true), "same-timestamp LateUpdate consumes event and wakes");
    Check(f.Wakes == wakes + 1, "LateUpdate issues one wake");
    Check(f.Guard.Observe(2.99, true), "delivered event keeps full hold");
    Check(!f.Guard.Observe(3.01, true), "event generation must not retrigger forever");
}

static void SplitFrameSampling()
{
    using var f = Fixture.Quiet();
    var wakeBaseline = f.Wakes;
    ScrollProbeCounters.Reset();
    Check(!f.Guard.Observe(2, true, sampleViewportMotion: false), "input phase stays idle before input");
    Check(ScrollProbeCounters.ViewSamples == 0, "input phase performs no viewport property reads");
    f.View.panel!.clipOffset = new(0, 4);
    Check(f.Guard.Observe(2, true, sampleViewportMotion: true), "LateUpdate catches viewport movement produced after Update");
    var lateSamples = ScrollProbeCounters.ViewSamples;
    Check(lateSamples > 0, "LateUpdate samples the tracked viewport");
    Check(f.Wakes == wakeBaseline + 1, "late viewport change wakes exactly once");

    ScrollProbeCounters.Reset();
    Check(f.Guard.Observe(2.005, true, sampleViewportMotion: false), "next input phase retains active scroll protection");
    Check(ScrollProbeCounters.ViewSamples == 0, "next input phase still avoids duplicate viewport scans");
    Check(f.Guard.Observe(2.005, true, sampleViewportMotion: true), "next LateUpdate renews protection");
    Check(ScrollProbeCounters.ViewSamples == lateSamples, "one full viewport scan is shared across the Update/LateUpdate pair");
    Check(f.Wakes == wakeBaseline + 1, "continuous viewport motion does not reset cadence state each frame");
}

static void WakeOnlyOnProtectionEntry()
{
    using var f = Fixture.Quiet();
    Input.MouseButtonDown = true;
    Check(f.Guard.Observe(2, true, sampleViewportMotion: false), "drag input wakes before native scroll updates");
    var wakes = f.Wakes;
    for (int i = 1; i <= 20; i++)
        Check(f.Guard.Observe(2 + i * 0.01, true, sampleViewportMotion: false), "held drag remains protected");
    Check(f.Wakes == wakes, "held input does not repeatedly reset estimators and cadence state");
    Input.MouseButtonDown = false;
    Check(!f.Guard.Observe(3.21, true, sampleViewportMotion: false), "protection settles one second after drag ends");
}

static void ActiveCatalogViewsOnly()
{
    var profiles = new System.Collections.Generic.List<Catalog.UIProfile>();
    for (int i = 0; i < 32; i++)
        profiles.Add(new Catalog.UIProfile { scroll = new UIScrollView { isActiveAndEnabled = i == 31 } });
    Singleton<Catalog>.Instance = new Catalog();
    foreach (var profile in profiles) Singleton<Catalog>.Instance.uiProfiles!.Add(profile);
    using var f = Fixture.Quiet();
    var newlyActive = profiles[0].scroll!;
    newlyActive.isActiveAndEnabled = true;
    ScrollProbeCounters.Reset();
    UICamera.onScroll!.Invoke(new GameObject(), 1);
    Check(f.Guard.Observe(2, true, sampleViewportMotion: false), "late wheel generation re-discovers the newly active catalogue");
    ScrollProbeCounters.Reset();
    Check(f.Guard.Observe(2, true, sampleViewportMotion: true), "LateUpdate samples the event's active catalogue viewport");
    Check(ScrollProbeCounters.ViewSamples < 24, "inactive catalogue viewports are not sampled every frame");
}

static void NativeMotion()
{
    foreach (var set in new System.Action<CenterableUIScrollView>[]
    {
        view => view.currentMomentum = new(0, 0.01f, 0),
        view => view.mScroll = 0.01f,
        view => view.isDragging = true,
        view => view.spring = new SpringPanel { isActiveAndEnabled = true }
    })
    {
        ResetHost(); using var f = Fixture.Quiet(); set(f.View);
        Check(f.Guard.Observe(1.1, true), "each movement signal protects independently");
        Check(f.Guard.Observe(8, true), "ongoing movement must not expire");
        f.View.currentMomentum = default; f.View.mScroll = 0; f.View.isDragging = false; f.View.spring = null;
        Check(f.Guard.Observe(8.99, true), "inertia tail gets settle period");
        Check(!f.Guard.Observe(9.01, true), "settled native movement permits idle");
    }
}

static void ViewportChanges()
{
    foreach (var set in new System.Action<CenterableUIScrollView>[]
    {
        view => view.panel!.clipOffset = new(0, 3),
        view => view.transform!.localPosition = new(0, 2, 0),
        view => view.panel = new UIPanel()
    })
    {
        ResetHost(); using var f = Fixture.Quiet(); set(f.View);
        Check(f.Guard.Observe(1.1, true), "viewport movement must protect even when raw input/momentum are zero");
        Check(!f.Guard.Observe(2.11, true), "unchanged viewport does not repeatedly wake");
    }
}

static void FocusTransitions()
{
    using var f = Fixture.Quiet();
    f.View.isDragging = true;
    Check(!f.Guard.Observe(1.1, false), "background state does not extend foreground protection");
    UICamera.onScroll!.Invoke(new GameObject(), 1);
    Check(!f.Guard.Observe(3, false), "background event is consumed without foreground wake");
    f.View.isDragging = false;
    Check(f.Guard.Observe(4, true), "focus regain re-samples visible view");
    Check(!f.Guard.Observe(5.01, true), "focus hold eventually expires");
}

static void BoundaryFailure()
{
    using var f = Fixture.Quiet();
    f.View.panel = null;
    Check(f.Guard.Observe(2, true), "missing native viewport must fail closed");
    Check(f.Messages.Count == 1 && UICamera.onScroll == null, "failure reports once and detaches handler");
    f.View.panel = new UIPanel();
    Check(f.Guard.Observe(20, true), "session failure remains protective");
    Check(!f.Guard.Observe(21, false), "failed observer does not manufacture background input");
}

static void ReplacedCallback()
{
    using var f = Fixture.Quiet();
    var foreignCalls = 0;
    var foreign = new UICamera.FloatDelegate((_, _) => foreignCalls++);
    UICamera.onScroll = foreign;
    Check(f.Guard.Observe(2.01, true), "callback replacement triggers conservative protection");
    Check(ReferenceEquals(UICamera.onScroll, foreign), "failure cleanup cannot remove foreign callback");
    UICamera.onScroll.Invoke(new GameObject(), 1);
    Check(foreignCalls == 1, "foreign callback remains functional");
}

static void Disposal()
{
    var foreignCalls = 0;
    var foreign = new UICamera.FloatDelegate((_, _) => foreignCalls++);
    UICamera.onScroll = foreign;
    using var f = Fixture.Quiet();
    Check(UICamera.onScroll!.GetInvocationList().Length == 2, "observer coexists with foreign listener");
    f.Guard.Dispose(); f.Guard.Dispose();
    Check(ReferenceEquals(UICamera.onScroll, foreign), "only own delegate removed");
    UICamera.onScroll!.Invoke(new GameObject(), 1);
    Check(foreignCalls == 1 && !f.Guard.Observe(5, true), "disposed observer cannot wake or protect");
}

static void InactiveViews()
{
    using var f = Fixture.Quiet();
    f.View.isActiveAndEnabled = false; f.View.isDragging = true;
    Check(!f.Guard.Observe(2, true), "inactive stale drag does not hold rendering");
    f.View.isActiveAndEnabled = true; f.View.isDragging = false;
    Check(f.Guard.Observe(2.1, true), "reactivation requires fresh viewport sample");
}

static void SceneTransitions()
{
    using var f = Fixture.Quiet();
    var replacement = new CenterableUIScrollView();
    ScriptNodeInspector.instance!.scriptListScroll = replacement;
    SceneManager.Handle++;
    Check(f.Guard.Observe(1.1, true), "scene transition discovers view before one-second timer");
    Check(!f.Guard.Observe(2.2, true), "new scene settles");
    replacement.transform!.localPosition = new(0, -10, 0);
    Check(f.Guard.Observe(2.21, true), "replacement view is observed");
}

static void InvalidObservations()
{
    using var f = Fixture.Quiet();
    Check(f.Guard.Observe(double.NaN, true), "invalid time protects focused window");
    Check(!f.Guard.Observe(double.NaN, false), "invalid time does not protect hidden window");
    f.View.currentMomentum = new(float.NaN, 0, 0);
    Check(f.Guard.Observe(2, true), "nonfinite native momentum is uncertain, not idle");
    f.View.currentMomentum = default;
    Check(f.Guard.Observe(0.1, true), "clock rollback resets view observation");
    Input.Throw = true;
    Check(f.Guard.Observe(3, true), "input access failure protects");
}

static void OtherViewScopes()
{
    var catalog = new UIScrollView(); var mods = new UIScrollView(); var backgrounds = new UIScrollView(); var hierarchy = new UIScrollView();
    Singleton<Catalog>.Instance = new Catalog();
    Singleton<Catalog>.Instance.uiProfiles!.Add(new Catalog.UIProfile { scroll = catalog });
    UI.UIPopupModManager.instance = new UI.UIPopupModManager { scroll = mods };
    Singleton<BackgroundExplorer>.Instance = new BackgroundExplorer
    {
        scroll = backgrounds, hierarchy = new BgSortingHierarchy { rootTable = new UITable { Parent = hierarchy } }
    };
    using var f = Fixture.Quiet();
    var now = 2d;
    foreach (var view in new[] { catalog, mods, backgrounds, hierarchy })
    {
        view.transform!.localPosition = new(0, 5, 0);
        Check(f.Guard.Observe(now, true), "known external list scope must wake");
        now += 1.1;
        Check(!f.Guard.Observe(now, true), "external list scope settles");
        now += 0.1;
    }
}

static void CorePolicyComposition()
{
    Check(typeof(AdaptivePolicy).Assembly.GetName().Name == "Azurite.Core", "must use actual archived Core assembly");
    foreach (var fps in new[] { 60d, 120, 320, 800 })
    {
        ResetHost(); using var f = Fixture.Quiet();
        var plan = CadenceResolver.Resolve(fps);
        var policy = new AdaptivePolicy();
        int Step(double now) => policy.Update(now, true, true, f.Guard.Observe(now, true), false, true,
            plan.IdleInterval, plan.DeepInterval, 0.35, 0.35).Interval;
        Step(2); Check(Step(2.5) > 1, "precondition policy is idle");
        UICamera.onScroll!.Invoke(new GameObject(), 1);
        Check(Step(2.5) == 1, "late input restores every-frame drawing for selected active rate");
        Check(Step(3.49) == 1, "actual policy cannot idle within scroll hold");
        f.View.currentMomentum = new(0, 0.1f, 0);
        Check(Step(4) == 1 && Step(8) == 1, "continuous inertia remains every-frame at every cap");
    }
}

static void ResetHost()
{
    Input.Throw = false; Input.MouseButtonDown = false; Input.mouseScrollDelta = default; ScrollProbeCounters.Reset(); SceneManager.Handle = 1; UICamera.onScroll = null;
    ScriptNodeInspector.instance = null; Singleton<Catalog>.Instance = null;
    UI.UIPopupModManager.instance = null; Singleton<BackgroundExplorer>.Instance = null;
}
static void Check(bool condition, string message) { if (!condition) throw new System.Exception(message); }

sealed class Fixture : System.IDisposable
{
    public CenterableUIScrollView View { get; } = new();
    public ScrollActivityGuard Guard { get; }
    public int Wakes;
    public System.Collections.Generic.List<string> Messages { get; } = new();
    private Fixture()
    {
        ScriptNodeInspector.instance = new ScriptNodeInspector { scriptListScroll = View };
        Guard = new ScrollActivityGuard(() => Wakes++, Messages.Add);
    }
    public static Fixture Quiet()
    {
        var f = new Fixture();
        if (!f.Guard.Observe(0, true) || f.Guard.Observe(1.01, true)) throw new System.Exception("fixture could not establish initial settled state");
        return f;
    }
    public void Dispose() => Guard.Dispose();
}
