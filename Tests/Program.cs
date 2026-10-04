using Azurite;
using Azurite.Core;

var oldHashes = new[] {
    "BD45C2DFBA4EE59A3A3007E34B53B401985B838D66FFDEBAC863C8527948A80F",
    "AFA74D22354E75803E63E8D39F48C4FDE4F13379BA4400BC12C0E880EAEBB11C",
    "86FE750B9E5F0B5E5A9FB8E699671036E8EFB8BB05B613AFB4B9BA2DB197B927" };
const string game = "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528";
const string metadata = "107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9";
int passed = 0, failed = 0;
void Check(bool value, string message) {
    if (value) { passed++; Console.WriteLine("PASS " + message); }
    else { failed++; Console.WriteLine("FAIL " + message); }
}
HostProfile Resolve(string[] hashes) => HostProfile.Resolve(hashes[0], hashes[1], hashes[2]);
var legacy = Resolve(oldHashes);
Check(legacy.Supported && legacy.NativePatches && !legacy.FpsLabelPostfix && legacy.LegacyExporter &&
    legacy.PlainTextPatch && legacy.PreviewOptimization && legacy.PreviewOwnership && legacy.LegacyLoadingException, "existing verified host retains its capabilities");
var current = HostProfile.ResolvePortable(game, metadata, true);
Check(current.Supported, "AA 1.0.0-fix can activate basic scrolling and render scheduling");
Check(current.Supported && !current.NativePatches && current.FpsLabelPostfix && !current.LegacyExporter &&
    current.PlainTextPatch && current.PreviewOptimization && !current.PreviewOwnership && !current.LegacyLoadingException,
    "AA 1.0.0-fix enables safe text and idle-surface paths but not unverified preview ownership");
for (int i = 0; i < 3; i++) {
    var changed = (string[])oldHashes.Clone(); changed[i] = new string('0', 64);
    Check(!Resolve(changed).Supported, $"reject altered legacy identity component {i}");
}
Check(!HostProfile.ResolvePortable(new string('0', 64), metadata, true).Supported, "reject unknown native host despite compatible API");
Check(!HostProfile.ResolvePortable(game, new string('0', 64), true).Supported, "reject mismatched native metadata");
Check(!HostProfile.ResolvePortable(game, metadata, false).Supported, "reject missing or incompatible generated API");
Check(!HostProfile.Resolve(game, oldHashes[1], oldHashes[2]).Supported, "new host cannot skip structural verification using legacy hashes");
Check(!HostProfile.Resolve("", "", "").Supported, "reject missing host identity");
Check(HostProfile.ResolvePortable(game.ToLowerInvariant(), metadata.ToLowerInvariant(), true).Supported, "hash formatting does not change identity");
void CheckDisplay(double reported, int first, int second, int infinite,
    FpsPlan firstPlan, FpsPlan secondPlan)
{
    Check(DisplayRatePolicy.TryCreate(reported, out var policy), $"accept monitor refresh {reported:F2} Hz");
    Check(policy.FirstRate == first && policy.SecondRate == second && policy.RefreshRate == infinite,
        $"labels for {reported:F2} Hz follow the monitor tier");
    Check(policy.PlanForTier(0) == firstPlan && policy.PlanForTier(1) == secondPlan &&
          policy.PlanForTier(2) == new FpsPlan(-1, 1),
        $"tier caps for {reported:F2} Hz include a refresh-synchronized infinity tier");
}
CheckDisplay(60, 30, 60, 60, new FpsPlan(-1, 2), new FpsPlan(-1, 1));
CheckDisplay(119.88, 60, 120, 120, new FpsPlan(-1, 2), new FpsPlan(-1, 1));
CheckDisplay(120, 60, 120, 120, new FpsPlan(-1, 2), new FpsPlan(-1, 1));
CheckDisplay(144, 60, 120, 144, new FpsPlan(60, 0), new FpsPlan(120, 0));
CheckDisplay(240, 60, 120, 240, new FpsPlan(-1, 4), new FpsPlan(-1, 2));
CheckDisplay(320, 60, 120, 320, new FpsPlan(60, 0), new FpsPlan(120, 0));
CheckDisplay(75, 38, 75, 75, new FpsPlan(38, 0), new FpsPlan(-1, 1));
Check(!DisplayRatePolicy.TryCreate(0, out _) && !DisplayRatePolicy.TryCreate(double.NaN, out _) &&
      !DisplayRatePolicy.TryCreate(1500, out _), "reject unavailable or implausible display refresh rates");
Check(DisplayRatePolicy.ConservativeFallback(0) == new FpsPlan(30, 0) &&
      DisplayRatePolicy.ConservativeFallback(1) == new FpsPlan(60, 0) &&
      DisplayRatePolicy.ConservativeFallback(2) == new FpsPlan(-1, 1),
      "unknown refresh keeps finite tiers conservative and infinity display-synchronized");
Check(!DynamicProducerPolicy.IsDynamic(true, true, false, 0, 0, 0, false, false, false), "static editor preview is throttleable");
Check(DynamicProducerPolicy.IsDynamic(true, true, true, 0, 0, 0, false, false, false), "auto preview remains protected");
Check(DynamicProducerPolicy.IsDynamic(true, true, false, 1, 0, 0, false, false, false), "scenario animation remains protected");
Check(DynamicProducerPolicy.IsDynamic(true, false, false, 0, 0, 0, false, false, false), "non-preview test controller remains protected");
Check(!DynamicProducerPolicy.IsDynamic(false, false, false, 0, 0, 0, false, false, false), "inactive test controller is not dynamic work");
Check(!DynamicProducerPolicy.IsDynamicForSurface(true, false, true, false, 0, 0, 0, false, false, false), "idle authoring surface ignores a resident non-preview controller");
Check(DynamicProducerPolicy.IsDynamicForSurface(true, false, true, false, 1, 0, 0, false, false, false), "authoring surface keeps an active scenario animation protected");
Check(DynamicProducerPolicy.IsDynamicForSurface(true, true, true, true, 0, 0, 0, false, false, false), "embedded preview remains protected when an active producer is present");
Check(DynamicProducerPolicy.IsDynamicForSurface(true, false, false, false, 0, 0, 0, false, false, false), "appreciation controller remains protected outside authoring surfaces");
Check(DynamicProducerPolicy.IsKnownStaticWindowName("Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer"), "background explorer is recognized as a static editor window");
Check(DynamicProducerPolicy.IsKnownStaticWindowName("UI.UIPopupModManager"), "mod manager is recognized as a static editor window");
Check(!DynamicProducerPolicy.IsKnownStaticWindowName("UnknownWindow"), "unknown editor windows remain conservative");
Check(DisplayRatePolicy.TryCreate(240, out var display240), "read 240 Hz profile for labels");
var nativeLabels = new FpsLabelValues("160", "320", "∞");
var mappedLabels = nativeLabels.Map(enabled: true, display240);
Check(mappedLabels == new FpsLabelValues("60", "120", "∞"), "frame-rate labels use the current monitor policy");
Check(mappedLabels.Map(enabled: true, display240) == mappedLabels, "repeated label postfixes do not create further changes");
Check(nativeLabels.Map(enabled: false, display240) == nativeLabels, "labels restore to host values when mapping is disabled");
Check(DisplayRatePolicy.TryCreate(60, out var display60) &&
      new FpsLabelValues("60", "120", "∞").Map(true, display60) == new FpsLabelValues("30", "60", "∞"),
      "60 Hz labels are 30/60/infinity");
Check(DisplayRatePolicy.TryCreate(320, out var display320) &&
      new FpsLabelValues("160", "320", "∞").Map(true, display320) == new FpsLabelValues("60", "120", "∞"),
      "320 Hz labels stay at 60/120/infinity");

var mutation = new DialogueMutationState();
Check(!mutation.IsActive && !mutation.IsBlocking(1.0, 0.35),
    "mutation gate is idle before the first native operation");
var outerMutation = mutation.Enter();
Check(mutation.IsActive && mutation.Depth == 1 && mutation.IsBlocking(1.0, 0.35),
    "outer dialogue mutation protects the list while native work is running");
var innerMutation = mutation.Enter();
Check(mutation.Depth == 2 && mutation.Generation == 1,
    "nested sync call shares one mutation generation");
innerMutation.Complete(2.0);
Check(mutation.IsActive && mutation.Depth == 1,
    "nested sync completion does not release the outer mutation");
outerMutation.Complete(2.0);
Check(!mutation.IsActive && mutation.IsBlocking(2.2, 0.35) && !mutation.IsBlocking(2.36, 0.35),
    "completed mutation keeps a bounded settle hold then releases");
outerMutation.Complete(2.4);
Check(mutation.Depth == 0 && mutation.Generation == 1,
    "repeated finalizer completion is idempotent");
var failedMutation = mutation.Enter();
failedMutation.Complete(double.NaN);
Check(mutation.IsBlocking(3.0, 0.35),
    "invalid native completion time fails closed for the current session");
mutation.Reset(4.0);
Check(!mutation.IsActive && mutation.IsBlocking(4.1, 0.2) && !mutation.IsBlocking(4.21, 0.2),
    "reset clears depth while retaining a short protective settle window");
Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
