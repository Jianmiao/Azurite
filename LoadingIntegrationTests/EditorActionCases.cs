using Studio.Scripts.Nodes;

// Call edges checked in installed GameAssembly disassembly:
// Load(100689939) -> SyncScriptList -> Select/OnChildSelect(100689969)
// -> SyncAll(100689971; text first) -> protected resource sync/PlayPreview.
// ApplyScriptText(100689961) modifies data and refreshes its row;
// DeleteScript(100689964) modifies data, syncs the list, selects a neighbor.
// Method signatures are verified against installed metadata on every dispatch.
// This catches overlay/guard regressions, not Unity collider behavior, real text
// layout, native editing semantics, or IL2CPP hook execution.
internal static class EditorActionCases
{
    public static void PendingResourcesAllowEditorActions()
    {
        using var f = new Fixture();
        NativeTasks.DelayFrames = 100;
        f.Capture();
        var sceneLoading = new Loading(); sceneLoading.Awake();
        f.Studio.Start(); f.Advance(); f.Advance();
        Check(f.Plugin.Busy && NativeTasks.Calls.Count > 0, "fixture did not hold native resources pending");
        var node = new ScriptNode(); node.TestDialogue.AddRange(new[] { "first", "second", "third" });
        DispatchUserAction(() => f.Preview.Load(node, false));
        Check(!f.Preview.loading && f.Preview.TestInput == "first", "script node did not finish entry with text available");
        Check(f.Preview.TestActions.Take(4).SequenceEqual(new[] { "load", "list", "select", "text-visible" }),
            "entry no longer follows audited native action chain");
        DispatchUserAction(() => { f.Preview.TestInput = "edited before assets"; f.Preview.ApplyScriptText(); });
        Check(node.TestDialogue[0] == "edited before assets", "text edit waited for resource completion");
        DispatchUserAction(() => f.Preview.InsertScript(1));
        Check(node.TestDialogue.Count == 4 && f.Preview.selectedScriptItem.TestIndex == 1, "insert blocked during preload");
        DispatchUserAction(f.Preview.DeleteScript);
        f.Advance();
        Check(node.TestDialogue.SequenceEqual(new[] { "edited before assets", "second", "third" }), "delete blocked or reordered dialogue");
        Check(f.Plugin.Busy && f.Preview.Played > 0 && f.Preview.ResourceOperations == 0,
            "editor actions did not preserve native preview while resource-dependent work stayed guarded");
        for (int n = 0; n < 110 && f.Plugin.Busy; n++) f.Advance();
        f.Advance();
        Check(!f.Plugin.Busy && f.Preview.Played > 0 && f.Preview.ResourceOperations == 3,
            "pending selection did not receive resource refresh after completion");
        Check(node.TestDialogue[0] == "edited before assets", "resource completion replaced edited dialogue");
    }
    public static void ForeignOverlayStillBlocksUserActions()
    {
        using var f = new Fixture();
        f.Capture();
        var sceneLoading = new Loading(); sceneLoading.Awake();
        sceneLoading.StartLoading("native-save"); f.Studio.Start();
        bool invoked = false;
        Check(!TryDispatchUserAction(() => invoked = true) && !invoked,
            "foreign native operation lost its input barrier");
        sceneLoading.StopLoading("native-save");
        Check(TryDispatchUserAction(() => invoked = true) && invoked && f.Plugin.Busy,
            "input did not resume after native barrier ended with resources pending");
    }
    private static bool TryDispatchUserAction(Action action)
    {
        // Models modal-overlay click interception; does not simulate raycast.
        if (Singleton<Loading>.Instance?.gameObject.activeInHierarchy == true) return false;
        action(); return true;
    }
    private static void DispatchUserAction(Action action) => Check(TryDispatchUserAction(action), "current Loading overlay intercepts input");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
