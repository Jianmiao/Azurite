using Azurite;
using Azurite.Core;

int passed = 0, failed = 0;
void Check(bool value, string description)
{
    if (value) { passed++; Console.WriteLine("PASS " + description); }
    else { failed++; Console.WriteLine("FAIL " + description); }
}

var fix = HostProfile.ResolvePortable(
    "2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528",
    "107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9", true);
Check(!PreviewOptimizationPolicy.AllowsScopedOwnership(fix, false), "Fix host retains the safe default");
Check(PreviewOptimizationPolicy.AllowsScopedOwnership(fix, true), "explicit opt-in allows the verified Fix host to attempt a live scoped lease");
Check(!PreviewOptimizationPolicy.AllowsScopedOwnership(HostProfile.Unsupported, true), "opt-in cannot bypass host validation");
Check(!fix.PreviewOwnership, "scoped opt-in does not enable legacy global renderScale ownership");

Check(PreviewResolutionPlan.TryCreate(1920, 1080, 960, 540, out var half) && half == new PreviewResolutionPlan(960, 540),
    "half-sized preview requests one quarter of the original pixels");
Check(PreviewResolutionPlan.TryCreate(1920, 1080, 901, 507, out var rounded) &&
      rounded.Width >= 901 && rounded.Height >= 507 && rounded.Width % 16 == 0 && rounded.Width * 9 == rounded.Height * 16,
    "viewport matching covers visible pixels and preserves source aspect");
Check(!PreviewResolutionPlan.TryCreate(1920, 1080, 1900, 1069, out _), "negligible resolution savings retain the original texture");
Check(!PreviewResolutionPlan.TryCreate(1920, 1080, 2400, 1350, out _), "preview target is never upscaled beyond its source");
Check(!PreviewResolutionPlan.TryCreate(0, 1080, 960, 540, out _) &&
      !PreviewResolutionPlan.TryCreate(1920, 1080, double.NaN, 540, out _) &&
      !PreviewResolutionPlan.TryCreate(1920, 1080, 960, 0, out _), "invalid source and viewport measurements reject replacement");
Check(!half.DiffersByFivePercent(new PreviewResolutionPlan(976, 549)) &&
      half.DiffersByFivePercent(new PreviewResolutionPlan(1024, 576)), "small viewport changes remain within texture hysteresis");

var schedule = new PreviewCadenceSchedule();
foreach (int fps in new[] { 60, 120, 320 })
{
    var activity = HostActivitySnapshot.Safe("static preview", true);
    var fallback = EditorCadencePolicy.UsePreviewFallback(true, true, activity, 60);
    var plan = EditorCadencePolicy.Resolve(fps, true, new VisualCadencePlan(true, 1, "static"),
        fallback, 60, true, 30, 15, 2, 4);
    var policy = new AdaptivePolicy();
    int Step(double now, HostActivitySnapshot snapshot, bool scroll = false, bool mutation = false) =>
        policy.Update(now, true, true, EditorCadencePolicy.IsProtected(snapshot, scroll, mutation, plan),
            snapshot.HasInteraction, true, plan.IdleInterval, plan.DeepInterval, .35, .35).Interval;
    Step(0, activity);
    var idleInterval = Step(1, activity);
    Check(!fallback && idleInterval > 1 && fps / (double)idleInterval <= 1.03,
        $"{fps} Hz static preview reaches 1 FPS within cadence tolerance instead of 60 FPS preview fallback");
    Check(Step(2, activity, scroll: true) == 1 && Step(3, activity, mutation: true) == 1,
        $"{fps} Hz scrolling and list changes restore every-frame drawing");
    var dynamicPreview = HostActivitySnapshot.Blocked("animation", true, true, true);
    Check(Step(4, dynamicPreview) == 1, $"{fps} Hz dynamic preview cannot enter the static cadence path");
}
Check(EditorCadencePolicy.UsePreviewFallback(false, true, HostActivitySnapshot.Safe("preview", true), 60),
    "unavailable observation permits the conservative preview fallback only");
foreach (double update in new[] { 60d, 120, 320 })
{
    var ambient = HostActivitySnapshot.Ambient("idle animation");
    var ambientPlan = EditorCadencePolicy.Resolve(update, true, new VisualCadencePlan(true, 1, "static UI"),
        false, 60, true, 30, 15, 2, 4, ambientPreview: true, ambientFps: 60);
    Check(ambient.HasDynamicPreview && !ambient.CanCachePreview && ambient.AmbientPreview && ambientPlan.Valid &&
        update / ambientPlan.IdleInterval >= 50 && update / ambientPlan.IdleInterval <= 61.8,
        $"{update}Hz ambient preview never inherits the 1fps static UI target or static texture cache");
    Check(!EditorCadencePolicy.IsProtected(ambient, false, false, ambientPlan) &&
        EditorCadencePolicy.IsProtected(ambient, true, false, ambientPlan) &&
        EditorCadencePolicy.IsProtected(ambient, false, true, ambientPlan),
        $"{update}Hz ambient input and mutations retain full rendering");
}
Check(!EditorCadencePolicy.Resolve(120, true, new VisualCadencePlan(true, 1, "static"), false, 60,
    true, 30, 15, 2, 4, ambientPreview: true, ambientFps: double.NaN).Valid,
    "invalid ambient configuration fails back to every-frame rendering");
Check(!schedule.ShouldRender(0, 60, false) && schedule.ShouldRender(0.001, 60, true),
    "whole-player skipped frames do not consume the first preview deadline");
Check(!schedule.ShouldRender(0.002, 60, true) && schedule.ShouldRender(1.0 / 60.0, 60, true),
    "preview limits only its own camera render frequency");
schedule.Reset();
int sparseDraws = 0;
for (int frame = 0; frame <= 320; frame++)
    if (schedule.ShouldRender(frame / 320.0, 60, frame % 32 == 1)) sparseDraws++;
Check(sparseDraws == 10, "every sparse player draw renders the preview despite out-of-phase deadlines");
schedule.Reset();
int regularDraws = 0;
for (int frame = 0; frame < 320; frame++)
    if (schedule.ShouldRender(frame / 320.0, 60, true)) regularDraws++;
Check(regularDraws == 60, "320 Hz updates schedule 60 preview draws per second");
Check(schedule.ShouldRender(0, 30, true), "clock rollback or rate change starts with a fresh preview frame");
Check(schedule.ShouldRender(double.NaN, 60, true), "invalid scheduling time restores a drawable camera");

Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
