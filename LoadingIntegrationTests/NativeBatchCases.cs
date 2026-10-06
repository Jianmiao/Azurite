using HarmonyLib;
using UnityEngine;

internal static class NativeBatchCases
{
    internal static void NoBgmSentinelIsSkipped()
    {
        using var f = Prepared();
        var scripts = new Il2CppSystem.Collections.Generic.List<ScriptData>();
        scripts.Add(new ScriptData { bgmId = 0 });
        scripts.Add(new ScriptData { bgmId = 999 });
        scripts.Add(new ScriptData { bgmId = 42 });
        f.SetScripts(scripts); f.Capture(); f.Studio.Start(); f.Complete();
        Check(f.Plugin.ResourceDependentWorkAllowed && NativeTasks.Calls.Count == 1 &&
            NativeTasks.Calls.Single().Scripts.Single().bgmId == 42,
            "AA no-BGM markers 0/999 were treated as loadable BGM assets");
    }
    internal static void OriginalEnumeratorNeverAdvanced()
    {
        using var f = Prepared();
        f.Native.Throw = true;
        f.Capture(); f.Studio.Start(); f.Complete();
        Check(f.Native.Moves == 0, "Azurite still calls native preload IEnumerator.MoveNext across the crash boundary");
        Check(f.Native.Disposals == 1, "detached unstarted native preload must be disposed once");
        Check(NativeTasks.Calls.Count > 0 && f.Plugin.ResourceDependentWorkAllowed,
            "native async task path never completed the deferred resources");
    }

    internal static void ResourceFactoriesRemainNative()
    {
        using var f = Prepared();
        var native = new NativeSelfYieldWait();
        var manager = new ScenarioResourceManager { Next = native };
        Check(!Harmony.Hooks.Keys.Any(m => m.DeclaringType == typeof(ScenarioResourceManager) || m.DeclaringType == typeof(CharacterManager)),
            "per-resource factory hooks still replace native coroutines");
        f.Capture(); f.Studio.Start(); f.Complete();
        Check(ReferenceEquals(manager.CoTryPreloadBackgroundTexture(42), native), "native coroutine identity changed");
        Check(native.MaximumCallsInOneFrame == 0 && native.Disposals == 0,
            "Azurite directly touched a coroutine owned by the native resource manager");
    }

    internal static void ResourceExecutionStaysNative()
    {
        using var f = Prepared();
        var native = new EngineOwnedResource(new ScriptData { bgName = 1 });
        Singleton<ScenarioResourceManager>.Instance.Next = native;
        // This child recreates the old controller's factory -> managed wrapper
        // -> NativeAdapter -> MoveNext crossing. The native scheduler fixture
        // rejects that crossing rather than attempting an unsafe native crash.
        f.Native.FirstChild = () => Singleton<ScenarioResourceManager>.Instance.CoTryPreloadBackgroundTexture(1);
        f.Capture(); f.Studio.Start(); f.Complete();
        Check(EngineOwnedResource.UnsafeMoves == 0,
            "native resource MoveNext was invoked by a managed replacement instead of the native scheduler");
        Check(native.Moves > 0 && NativeTasks.Calls.Any(c => ReferenceEquals(c.Enumerator, native)),
            "original native resource never reached the unchanged native scheduler handoff");
        Check(f.Plugin.ResourceDependentWorkAllowed, "native-owned resource never completed");
    }

    internal static void NativeTasksCompleteExactlyOnce()
    {
        using var f = Prepared();
        f.Capture(); f.Studio.Start(); f.Complete();
        Check(NativeTasks.Calls.Count > 0, "no native task dispatched");
        Check(NativeTasks.Calls.All(t => t.ResultReads == 1), "native task results must be consumed exactly once");
        for (int n = 0; n < 5; n++) f.Advance();
        Check(NativeTasks.Calls.All(t => t.ResultReads == 1), "completed native result was consumed again in later updates");
    }

    internal static void LargeQueueStaysBounded()
    {
        using var f = Prepared(100);
        f.Capture(); f.Studio.Start();
        for (int n = 0; n < 1000 && !f.Plugin.ResourceDependentWorkAllowed; n++) f.Advance();
        f.Advance();
        Check(f.Plugin.ResourceDependentWorkAllowed, "bounded queue never completed");
        Check(NativeTasks.Calls.SelectMany(c => c.Scripts).Select(s => s.bgName).Where(n => n != 0).Distinct().Count() == 100,
            "large project silently omitted native resource requests");
        Check(NativeTasks.Calls.GroupBy(t => t.StartedFrame).All(g => g.Count() == 1),
            "one controller frame eagerly issued an unbounded native task batch");
        Check(NativeTasks.Calls.Select(c => c.StartedFrame).Distinct().Count() > 1,
            "large queue did not return control to editor frames");
        Check(NativeTasks.Calls.All(t => t.ResultReads == 1), "large queue left native task result unobserved");
    }

    internal static void SameFrameUpdatesDoNotDrainQueue()
    {
        using var f = Prepared(20);
        f.Capture(); f.Studio.Start(); f.Advance(); f.Advance();
        int frame = Time.frameCount;
        for (int n = 0; n < 100; n++) f.Plugin.Update(f.Host, false);
        Check(NativeTasks.Calls.Count(c => c.StartedFrame == frame) == 1,
            "repeated updates in one frame bypassed native dispatch budget");
    }

    internal static void ActiveNativeTasksStayBounded()
    {
        using var f = Prepared(20);
        NativeTasks.DelayFrames = 20;
        f.Capture(); f.Studio.Start();
        for (int n = 0; n < 12; n++) f.Advance();
        Check(NativeTasks.Calls.Count == 4 && NativeTasks.Calls.All(c => c.ResultReads == 0),
            "slow resources exceeded four in-flight native tasks or blocked admission before the intended cap");
        for (int n = 0; n < 150 && !f.Plugin.ResourceDependentWorkAllowed; n++) f.Advance();
        f.Advance();
        Check(f.Plugin.ResourceDependentWorkAllowed && NativeTasks.Calls.Count == 20 && NativeTasks.Calls.All(c => c.ResultReads == 1),
            "slow native tasks never released their slots to later resource requests");
    }

    internal static void CachedCompletionsStayBounded()
    {
        using var f = Prepared(20);
        NativeTasks.DelayFrames = 0; NativeTasks.CompleteSynchronously = true;
        f.Capture(); f.Studio.Start(); f.Advance(); f.Advance();
        int frame = Time.frameCount;
        for (int n = 0; n < 100; n++) f.Plugin.Update(f.Host, false);
        Check(NativeTasks.Calls.Count(c => c.StartedFrame == frame) == 1,
            "cached completion drained an unbounded queue in one frame");
        f.Complete();
        Check(NativeTasks.Calls.Count == 20 && NativeTasks.Calls.All(c => c.ResultReads == 1),
            "synchronous native completion lost requests or consumed a result more than once");
    }

    internal static void DuplicateResourcesAreDeduplicated()
    {
        using var f = Prepared();
        var scripts = new Il2CppSystem.Collections.Generic.List<ScriptData>();
        for (int n = 0; n < 100; n++)
        {
            var characters = new Il2CppSystem.Collections.Generic.List<ScriptData.CharacterRecordData>();
            characters.Add(new ScriptData.CharacterRecordData { name = "same-key" });
            scripts.Add(new ScriptData { bgName = 42, voice = "same-key", popup = "same-key", bgmId = 42, sound = "same-key", characters = characters });
        }
        f.SetScripts(scripts); f.Capture(); f.Studio.Start(); f.Complete();
        Check(f.Plugin.ResourceDependentWorkAllowed && NativeTasks.Calls.Count == 6,
            "dedup either reloaded shared assets or merged different resource kinds with identical keys");
        Check(NativeTasks.Calls.All(c => c.ResultReads == 1), "deduplicated native task was not observed exactly once");
    }

    internal static void FaultLeavesTextAvailable()
    {
        using var f = Prepared();
        NativeTasks.Fault = true;
        f.Capture(); f.Studio.Start(); f.Preview.PlayPreview();
        for (int n = 0; n < 12; n++) f.Advance();
        Check(NativeTasks.Calls.Count > 0 && NativeTasks.Calls.Any(t => t.ResultReads == 1), "faulted native task result was never observed");
        Check(!f.Plugin.BlocksEditorOperations && !f.Plugin.ResourceDependentWorkAllowed,
            "native task failure either locked text or exposed missing assets");
        Check(f.Preview.Played >= 1 && f.Log.Any(s => s.Contains("phase=failed")), "resource failure did not preserve native preview");
        Check(NativeTasks.Calls.All(t => t.ResultReads == 1), "failed session abandoned active native task results");
    }

    internal static void CloseCancelsOutstandingNativeTasks()
    {
        using var f = Prepared(20);
        NativeTasks.DelayFrames = 100;
        f.Capture(); f.Studio.Start(); f.Advance(); f.Advance(); f.Advance();
        Check(NativeTasks.Calls.Count > 0, "fixture never entered async resource work");
        int calls = NativeTasks.Calls.Count;
        f.Studio.OnDestroy(); f.Studio.OnDestroy();
        for (int n = 0; n < 6; n++) f.Advance();
        Check(NativeTasks.Calls.Count == calls, "closed editor kept issuing native resource tasks");
        Check(NativeTasks.Calls.All(t => t.Token?.IsCancellationRequested == true), "closed editor did not signal native cancellation");
        Check(NativeTasks.Calls.All(t => t.ResultReads == 1 && t.Forgotten), "native cancellation results were abandoned or consumed twice");
        Check(NativeTasks.Calls.All(t => t.Token.Source.CancelCalls == 1 && t.Token.Source.DisposeCalls == 1), "cancellation source did not have exact-once cleanup");
        Check(!f.Plugin.Busy && !f.Plugin.BlocksEditorOperations, "closed editor retained an active operation guard");
    }

    internal static void ExportPausesQueueWithoutCancelling()
    {
        using var f = Prepared(20);
        f.Capture(); f.Studio.Start(); f.Advance(); f.Advance(); f.Advance();
        int dispatched = NativeTasks.Calls.Count;
        Check(dispatched > 0, "fixture never dispatched native tasks");
        f.Plugin.SuspendForExport();
        for (int n = 0; n < 10; n++) { Time.frameCount++; NativeTasks.Tick(); f.Plugin.Update(f.Host, true); }
        Check(NativeTasks.Calls.Count == dispatched, "export ownership did not pause queue dispatch");
        Check(NativeTasks.Calls.All(t => t.Token?.IsCancellationRequested != true), "export cancelled native-owned tasks");
        Check(f.Plugin.BlocksEditorOperations, "export handoff failed to guard edits");
        f.Plugin.ResumeAfterExport();
        for (int n = 0; n < 250 && !f.Plugin.ResourceDependentWorkAllowed; n++) f.Advance();
        f.Advance();
        Check(f.Plugin.ResourceDependentWorkAllowed && NativeTasks.Calls.All(t => t.ResultReads == 1),
            "export release failed to drain queued work or consumed native results twice");
    }

    internal static void MetadataSnapshotSurvivesEdits()
    {
        using var f = Prepared();
        var source = new Il2CppSystem.Collections.Generic.List<ScriptData>();
        var characters = new Il2CppSystem.Collections.Generic.List<ScriptData.CharacterRecordData>();
        characters.Add(new ScriptData.CharacterRecordData { name = "original-character" });
        var original = new ScriptData { bgName = 42, voice = "original-voice", popup = "original-popup", bgmId = 7, sound = "original-sound", characters = characters };
        source.Add(original); f.SetScripts(source);
        f.Capture();
        original.bgName = 99; original.voice = "edited"; original.popup = "edited"; original.bgmId = 999; original.sound = "edited";
        characters[0].name = "edited"; characters.Clear(); source.Clear();
        f.Studio.Start();
        for (int n = 0; n < 100 && !f.Plugin.ResourceDependentWorkAllowed; n++) f.Advance();
        f.Advance();
        var issued = NativeTasks.Calls.SelectMany(c => c.Scripts).ToArray();
        Check(issued.Any(s => s.bgName == 42) && issued.Any(s => s.voice == "original-voice") && issued.Any(s => s.popup == "original-popup") && issued.Any(s => s.bgmId == 7) && issued.Any(s => s.sound == "original-sound"),
            "editing live records changed queued resource identity");
        Check(issued.Any(s => s.characters != null && Enumerate(s.characters).Any(c => c.name == "original-character")), "editing characters changed queued resource identity");
        Check(f.Plugin.ResourceDependentWorkAllowed && f.Native.Moves == 0, "immutable queue never completed via native task path");
    }

    internal static Fixture Prepared(int resources = 1)
    {
        var f = new Fixture();
        NativeTasks.Reset();
        var scripts = new Il2CppSystem.Collections.Generic.List<ScriptData>();
        for (uint n = 1; n <= resources; n++) scripts.Add(new ScriptData { bgName = n });
        f.SetScripts(scripts);
        return f;
    }
    private static IEnumerable<ScriptData.CharacterRecordData> Enumerate(Il2CppSystem.Collections.Generic.List<ScriptData.CharacterRecordData> list)
    {
        foreach (var item in list) yield return item;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
