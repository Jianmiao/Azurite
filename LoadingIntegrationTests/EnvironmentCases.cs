using System.Reflection;
using HarmonyLib;

public sealed class AudioManager
{
    public int Plays, Stops;
    public long LastId;
    public void SetBGM(long id) => Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), new object[] { id }, () => { Plays++; LastId = id; return null; });
    public void StopAllBGMs() => Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () => { Stops++; return null; });
}

internal static class EnvironmentCases
{
    private static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    public static void PendingEnvironmentIsEditable()
    {
        using var f = new Fixture(); f.Capture(); f.Studio.Start();
        f.Preview.SyncEnvironmentProperties();
        Check(f.Plugin.Busy && f.Preview.TestEnvironmentReady, "environment controls still wait for all resource loads");
        Check(Singleton<AudioManager>.Instance.Plays == 0, "environment synchronization started audio during preload");
        int before = f.Preview.ResourceOperations;
        f.Preview.OpenBackgroundSelector();
        Check(f.Preview.ResourceOperations == before + 1, "background gallery still blocked until all project resources load");
        f.Preview.TestBgmId = 0; f.Preview.SyncEnvironmentProperties();
        Check(Singleton<AudioManager>.Instance.Stops == 0, "environment synchronization stopped unrelated audio during preload");
    }
    public static void AudioScopeDoesNotLeak()
    {
        using var f = new Fixture(); f.Capture(); f.Studio.Start();
        f.Preview.TestThrowEnvironment = true;
        try { f.Preview.SyncEnvironmentProperties(); } catch (InvalidOperationException) { }
        var audio = Singleton<AudioManager>.Instance;
        audio.SetBGM(23); audio.StopAllBGMs();
        Check(audio.Plays == 1 && audio.Stops == 1, "environment audio deferral leaked into an unrelated caller");
        f.Preview.TestThrowEnvironment = false;
        f.Preview.SyncEnvironmentProperties();
        Check(audio.Plays == 1, "environment nested audio was not scoped");
        f.Preview.TestBgmId = 99; f.Complete();
        Check(audio.LastId == 99 && audio.Plays == 2, "completion did not synchronize the latest selected environment");
    }
    public static void ExportAndNativeEnvironmentRemainNative()
    {
        using var f = new Fixture();
        f.Preview.SyncEnvironmentProperties();
        Check(Singleton<AudioManager>.Instance.Plays == 1, "ordinary native synchronization was intercepted");
        f.Capture(); f.Studio.Start(); f.Plugin.SuspendForExport();
        f.Preview.SyncEnvironmentProperties();
        Check(Singleton<AudioManager>.Instance.Plays == 2, "Azurite intercepted audio while export owns rendering");
        int before = f.Preview.ResourceOperations; f.Preview.OpenBackgroundSelector();
        Check(f.Preview.ResourceOperations == before, "gallery input entered while export owns the pending editor");
    }
}
