using Azurite;
using Azurite.Core;
using BepInEx.Logging;
using UnityEngine;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
FpsPlan Pair() => new(Application.targetFrameRate, QualitySettings.vSyncCount);
var policy = new CpuIdlePolicy();
Check(policy.Resolve(0, true, true, 2, 30, 15) == 0, "CPU idle delay starts with normal updates");
Check(policy.Resolve(1.9, true, true, 2, 30, 15) == 0, "CPU cap waits for a full quiet period");
Check(policy.Resolve(2, true, true, 2, 30, 15) == 30, "quiet foreground selects 30 FPS");
Check(policy.Resolve(2.1, true, false, 2, 30, 15) == 15, "quiet background selects 15 FPS");
Check(policy.Resolve(2.2, false, true, 2, 30, 15) == 0, "input playback loading mutation or dirty UI restores full updates");
Check(policy.Resolve(3, true, true, 2, 30, 15) == 0, "activity restarts the quiet period");
Check(policy.Resolve(5, true, true, 2, 30, 15) == 30, "quiet state can resume saving");
Check(policy.Resolve(4, true, true, 2, 30, 15) == 0, "backwards time releases the CPU cap");
Check(policy.Resolve(6, true, true, 2, 30, 40) == 0, "invalid CPU configuration does not acquire a cap");
foreach (int refresh in new[] { 60, 144, 240, 320 })
{
    CurrentDisplayRate.Rate = refresh;
    Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
    var settings = new UserSettings(); Singleton<UserSettings>.Instance = settings;
    int wakes = 0;
    using var fps = new FpsController(new ManualLogSource(), () => wakes++);
    fps.Update(0, true, 1); fps.Update(1.1, true, 1); fps.Update(1.2, true, 1);
    var normal = Pair();
    Check(fps.IsMappingApplied && normal == new FpsPlan(-1, 1), refresh + " Hz active unlimited follows the display");
    fps.SetIdleFrameRate(30);
    Check(Pair() == new FpsPlan(30, 0) && fps.IdleFrameRate == 30, refresh + " Hz quiet foreground lowers the actual player-loop cap");
    fps.Update(2.2, true, 1);
    Check(fps.IdleFrameRate == 30 && Pair() == new FpsPlan(30, 0), refresh + " Hz regular display audit does not undo idle saving");
    fps.SetIdleFrameRate(15);
    Check(Pair() == new FpsPlan(15, 0), refresh + " Hz background cap retains the active tier baseline");
    Check(fps.RestoreIdleFrameRate() && Pair() == normal, refresh + " Hz input wake restores exact active target and VSync");
    fps.SetIdleFrameRate(15);
    settings.ChangeTier(0);
    fps.Update(2.3, true, 1); fps.Update(2.4, true, 1);
    DisplayRatePolicy.TryCreate(refresh, out var display);
    Check(fps.IsOperational && Pair() == display.PlanForTier(0), refresh + " Hz native tier change while idle remains supported");
    var selected = Pair();
    fps.SetIdleFrameRate(30);
    if (refresh == 60) Check(Pair() == selected && fps.IdleFrameRate == 0, "idle cap does not rewrite or raise an already selected 30 FPS tier");
    fps.SetIdleFrameRate(15);
    Check(fps.SuspendForExport() && Pair() == new FpsPlan(-1, 2), refresh + " Hz export acquisition restores original host settings");
    fps.Dispose();
    Check(Pair() == new FpsPlan(-1, 2), refresh + " Hz disposal after export does not overwrite export settings");
}
CurrentDisplayRate.Rate = 240;
Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
Singleton<UserSettings>.Instance = new();
using (var fps = new FpsController(new ManualLogSource(), () => { }))
{
    fps.Update(0, true); fps.Update(1.1, true); fps.SetIdleFrameRate(30);
    fps.Update(1.2, false);
    Check(Pair() == new FpsPlan(-1, 1) && fps.IdleFrameRate == 0, "disabling scheduling releases the CPU override");
}
Check(Pair() == new FpsPlan(-1, 0), "disposal restores pre-mapping host frame settings");
Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
Singleton<UserSettings>.Instance = new();
using (var fps = new FpsController(new ManualLogSource(), () => { }))
{
    fps.Update(0, true); fps.Update(1.1, true); fps.SetIdleFrameRate(30);
    Application.targetFrameRate = 75;
    Check(!fps.RestoreIdleFrameRate() && Pair() == new FpsPlan(75, 0) && !fps.IsOperational, "external frame-cap ownership is preserved on wake");
    Check(fps.SuspendForExport() && Pair() == new FpsPlan(75, 0), "yielded ownership leaves no Azurite override to block export handoff");
}
Check(Pair() == new FpsPlan(75, 0), "disposal preserves an external frame-cap owner");
foreach (bool rejectTarget in new[] { true, false })
{
    Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
    Singleton<UserSettings>.Instance = new();
    using (var fps = new FpsController(new ManualLogSource(), () => { }))
    {
        fps.Update(0, true); fps.Update(1.1, true);
        Application.RejectTargetOnce = rejectTarget; QualitySettings.RejectVSyncOnce = !rejectTarget;
        fps.SetIdleFrameRate(15);
        Check(Pair() == new FpsPlan(-1, 1) && fps.IdleFrameRate == 0 && !fps.IsOperational, "partial " + (rejectTarget ? "target" : "VSync") + " write failure restores the normal pair and stops optimization");
    }
    Check(Pair() == new FpsPlan(-1, 0), "disposal after partial-write recovery restores the native baseline");
}
Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0; Singleton<UserSettings>.Instance = new();
using (var fps = new FpsController(new ManualLogSource(), () => { }))
{
    fps.Update(0, true); fps.Update(1.1, true); fps.SetIdleFrameRate(15);
    Check(fps.SuspendForExport(), "idle cap can hand frame settings to an exporter");
    fps.Update(2.2, true); fps.Update(2.3, true);
    Check(fps.IsMappingApplied && Pair() == new FpsPlan(-1, 1), "untouched exporter release resumes display mapping");
    fps.SetIdleFrameRate(15); CurrentDisplayRate.Rate = 60;
    fps.Update(3.3, true); fps.Update(3.4, true);
    Check(fps.IsOperational && fps.RestoreIdleFrameRate() && Pair() == new FpsPlan(-1, 1), "display refresh changes during idle preserve a recoverable normal plan");
}
Console.WriteLine($"RESULT {passed}/{passed}; source-linked FPS and CPU-idle lifecycle tests, not a native performance measurement.");
