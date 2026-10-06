using Azurite;
using Studio.Scripts;
using UnityEngine;

var tests = new (string Name, Action Run)[]
{
    ("native inline Init reuses rows, retains Refresh and moves only the new sibling", NativeInlineInsertion),
    ("non-inlined Init retains field assignment and native text/name refresh", WrapperInitInsertion),
    ("parameterless Insert delegates once and preserves native selection", DelegatingInsertion),
    ("insertion at front, middle and end preserves all old identities", AllInsertionPositions),
    ("deletion at front, middle and end removes only one identity", AllDeletionPositions),
    ("same Script identity with changed text/name must refresh", ChangedContent),
    ("standalone Sync remains fully native", StandaloneSync),
    ("same-count reorder falls back to original rebuild", ReorderedData),
    ("replaced Script list falls back", ReplacedDataList),
    ("changed prefab before Sync falls back", ReplacedPrefab),
    ("extra grid child prevents capture", ExtraChild),
    ("dragging or animated grid uses native path", UnsafeCapture),
    ("foreign parent and prefab calls never consume reuse slots", ForeignBoundaries),
    ("nested unrelated mutation invalidates inherited snapshot", NestedMutation),
    ("repeat Sync in one mutation never consumes a snapshot twice", RepeatedSync),
    ("suspension after intercepted destroy triggers one native repair", MidSyncSuspension),
    ("native AddChild failure rebuilds before propagating original error", AddFailure),
    ("native Refresh failure rebuilds before propagating original error", RefreshFailure),
    ("invalid interception followed by native exception still recovers", InvalidThenException),
    ("postcondition mismatch rebuilds and disables reuse for the session", InvalidPostcondition),
    ("detached deleted row is cleaned up when its destruction throws", DestroyFailure),
    ("sibling placement failure recovers exact native ordering", SiblingFailure),
    ("failed native recovery is bounded and preserves the original exception", FailedRecovery),
    ("native keepSelected callback runs on the rebuilt cache", KeepSelection),
    ("failed hook registration removes every partial hook", FailedRegistration),
    ("disabled optimizer retains native churn", Disabled),
    ("worker-thread call cannot use main-thread reuse", WorkerThread),
    ("120 consecutive edits preserve data, ordering and native selection", RepeatedEdits),
    ("bounded diagnostics report successful capture, plan and commit", DiagnosticSuccess),
    ("capture diagnostics distinguish scene, parent, index and geometry barriers", DiagnosticCaptureBarriers),
    ("sync diagnostics distinguish changed identity and invalid edit plan", DiagnosticSyncBarriers),
};
int failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length}; linked production row reuse with native-call-chain host stubs, not a Unity performance measurement.");
return failures == 0 ? 0 : 1;

static void NativeInlineInsertion()
{
    using var fixture = new Fixture();
    fixture.Host.InsertScript(32);
    Fixture.Check(HostStats.Created == 1, "exactly one new row expected");
    Fixture.Check(HostStats.Refreshes == 65, "native refresh remains unchanged");
    Fixture.Check(HostStats.SiblingMoves == 1, "one insertion sibling move, not one per row");
    Fixture.Check(HostStats.Reflows == 1 && HostStats.Syncs == 1, "single native reflow and sync");
    fixture.AssertCoherent();
}

static void WrapperInitInsertion()
{
    using var f = new Fixture();
    HostStats.InlineInit = false;
    f.Host.InsertScript(20);
    Fixture.Check(HostStats.Inits == 65 && HostStats.Refreshes == 65, "all Init and Refresh side effects retained");
    Fixture.Check(HostStats.Created == 1, "only inserted row instantiated");
    f.AssertCoherent();
}
static void DelegatingInsertion()
{
    using var f = new Fixture(); var prior = f.Host.selectedScriptItem!;
    f.Host.InsertScript();
    Fixture.Check(HostStats.Created == 1 && HostStats.Removed == 0, "delegated insertion optimized");
    Fixture.Check(f.Host.selectedScriptItem == f.Host.scriptNodeListItemsCache[11] && !prior.selected, "host selected new row and deselected old");
    f.AssertCoherent();
}
static void AllInsertionPositions()
{
    foreach (int index in new[] { 0, 32, 64 })
    {
        using var f = new Fixture(); var before = f.Rows();
        f.Host.InsertScript(index);
        Fixture.Check(HostStats.Created == 1 && HostStats.Removed == 0, "insert churn");
        Fixture.Check(HostStats.SiblingMoves <= 1, "insert sibling shifts bounded");
        for (int i = 0; i < before.Length; i++) Fixture.Check(f.Host.scriptNodeListItemsCache[i < index ? i : i + 1] == before[i], "retained row identity");
        f.AssertCoherent();
    }
}
static void AllDeletionPositions()
{
    foreach (int index in new[] { 0, 32, 63 })
    {
        using var f = new Fixture(); var before = f.Rows();
        f.Host.scriptNodeListItemsCache[index].Select();
        f.Host.DeleteScript();
        Fixture.Check(HostStats.Created == 0 && HostStats.Removed == 1, "delete churn");
        Fixture.Check(HostStats.SiblingMoves == 0, "delete requires no sibling reposition calls");
        Fixture.Check(before[index].gameObject.Destroyed, "deleted row destroyed");
        Fixture.Check(index == 0 ? f.Host.selectedScriptItem == null : f.Host.selectedScriptItem == before[index - 1], "native previous-row selection");
        for (int i = 0; i < 63; i++) Fixture.Check(f.Host.scriptNodeListItemsCache[i] == before[i < index ? i : i + 1], "retained row identity");
        f.AssertCoherent();
    }
}
static void ChangedContent()
{
    using var f = new Fixture();
    f.Host.AfterDataChange = () => { var data = f.Host.scriptNode.scripts[2]; data.text = "changed"; data.speakerSlotNum = 1; data.characters = new[] { new Script.CharacterRecord(), new Script.CharacterRecord { name = "new name" } }; };
    HostStats.InlineInit = false;
    f.Host.InsertScript(32);
    Fixture.Check(f.Host.scriptNodeListItemsCache[2].nameLabel.text == "new name", "retained row name updated");
    f.AssertCoherent();
}
static void StandaloneSync()
{
    using var f = new Fixture();
    f.Host.SyncScriptList(false, true, false);
    Fixture.Check(HostStats.Created == 64 && HostStats.Removed == 64, "standalone sync remains native"); f.AssertCoherent();
}
static void ReorderedData()
{
    using var f = new Fixture();
    f.Host.AfterDataChange = () => { var scripts = f.Host.scriptNode.scripts; (scripts[0], scripts[1]) = (scripts[1], scripts[0]); };
    f.Host.InsertScript(30);
    Fixture.Check(HostStats.Created == 65, "non single-edit sequence falls back"); f.AssertCoherent();
}
static void ReplacedDataList()
{
    using var f = new Fixture();
    f.Host.AfterDataChange = () => { var replacement = new NativeList<Script>(); for (int i = 0; i < f.Host.scriptNode.scripts.Count; i++) replacement.Add(f.Host.scriptNode.scripts[i]); f.Host.scriptNode.scripts = replacement; };
    f.Host.InsertScript(30);
    Fixture.Check(HostStats.Created == 65, "new list identity falls back"); f.AssertCoherent();
}
static void ReplacedPrefab()
{
    using var f = new Fixture(); f.Host.AfterDataChange = () => f.Host.scriptListItemPrefab = new();
    f.Host.InsertScript(30); Fixture.Check(HostStats.Created == 65, "different prefab falls back"); f.AssertCoherent();
}
static void ExtraChild()
{
    using var f = new Fixture(); var extra = new GameObject(); extra.transform.SetParent(f.Host.scriptList.transform, false);
    f.Host.InsertScript(30); Fixture.Check(HostStats.Created == 65 && extra.Destroyed, "non row child forces native reconstruction"); f.AssertCoherent();
}
static void UnsafeCapture()
{
    using (var f = new Fixture()) { f.Host.scriptList.animateSmoothly = true; f.Host.InsertScript(30); Fixture.Check(HostStats.Created == 65, "animated grid native"); f.AssertCoherent(); }
    using (var f = new Fixture()) { UICamera.isDragging = true; try { f.Host.InsertScript(30); } finally { UICamera.isDragging = false; } Fixture.Check(HostStats.Created == 65, "drag native"); f.AssertCoherent(); }
}
static void ForeignBoundaries()
{
    using var f = new Fixture(); var foreign = new GameObject(); var otherPrefab = new GameObject();
    HostStats.BeforeAdd = () =>
    {
        var foreignRow = NGUITools.AddChild(foreign.transform, f.Host.scriptListItemPrefab);
        Fixture.Check(foreignRow.transform.parent == foreign.transform, "foreign parent unchanged");
        var wrongPrefab = NGUITools.AddChild(f.Host.scriptList.transform, otherPrefab);
        Fixture.Check(wrongPrefab.transform.GetSiblingIndex() == f.Host.scriptList.transform.childCount - 1, "foreign prefab native append");
        NGUITools.Destroy(wrongPrefab);
        NGUITools.DestroyChildren(foreign.transform, 0);
    };
    f.Host.InsertScript(30);
    Fixture.Check(HostStats.Created == 3, "only two unrelated creations plus inserted row"); f.AssertCoherent();
}
static void NestedMutation()
{
    using var f = new Fixture(); f.Host.AfterDataChange = () => f.Host.InsertScript(5);
    f.Host.InsertScript(30); Fixture.Check(HostStats.Created == 132, "unrelated nested insert falls back twice"); f.AssertCoherent();
}
static void RepeatedSync()
{
    using var f = new Fixture(); f.Host.AfterDataChange = () => f.Host.SyncScriptList(true, false, false);
    f.Host.InsertScript(30); Fixture.Check(HostStats.Created == 66 && HostStats.Removed == 65, "first sync reuses, second fully native"); f.AssertCoherent();
}
static void MidSyncSuspension()
{
    using var f = new Fixture(); HostStats.AfterDestroy = f.Optimizer.Suspend;
    f.Host.InsertScript(30); Fixture.Check(HostStats.Syncs == 2, "one native recovery"); f.AssertCoherent();
}
static void AddFailure()
{
    using var f = new Fixture(); HostStats.Failure = "AddChild";
    ExpectError(() => f.Host.InsertScript(30), "AddChild"); Fixture.Check(HostStats.Syncs == 2, "native error repaired once"); f.AssertCoherent();
}
static void RefreshFailure()
{
    using var f = new Fixture(); HostStats.Failure = "Refresh";
    ExpectError(() => f.Host.InsertScript(30), "Refresh"); Fixture.Check(HostStats.Syncs == 2, "refresh exception repaired once"); f.AssertCoherent();
}
static void InvalidThenException()
{
    using var f = new Fixture();
    HostStats.BeforeAdd = () => { f.Host.scriptNodeListItemsCache.Clear(); f.Host.scriptList.transform.Children[0].gameObject.SetActive(false); HostStats.Failure = "AddChild"; };
    ExpectError(() => f.Host.InsertScript(30), "AddChild"); Fixture.Check(HostStats.Syncs == 2, "invalid then exception still repairs"); f.AssertCoherent();
}
static void InvalidPostcondition()
{
    using var f = new Fixture(); HostStats.BeforeSyncReturn = () => f.Host.scriptNodeListItemsCache[2].index = 900;
    f.Host.InsertScript(30); Fixture.Check(HostStats.Syncs == 2, "mismatch native repair"); f.AssertCoherent();
    HostStats.Reset(); f.Optimizer.Update(10, true); f.Host.InsertScript(30);
    Fixture.Check(HostStats.Created == 66, "session remains disabled after recovery"); f.AssertCoherent();
}
static void DestroyFailure()
{
    using var f = new Fixture(); var removed = f.Host.selectedScriptItem!;
    HostStats.Failure = "Destroy"; f.Host.DeleteScript();
    Fixture.Check(removed.gameObject.Destroyed && removed.transform.parent == null, "detached row disposal retried");
    Fixture.Check(HostStats.Syncs == 2, "native recovery after destroy failure"); f.AssertCoherent();
}
static void SiblingFailure()
{
    using var f = new Fixture(); HostStats.Failure = "SetSiblingIndex"; f.Host.InsertScript(30);
    Fixture.Check(HostStats.Syncs == 2, "placement failure repaired once"); f.AssertCoherent();
}
static void FailedRecovery()
{
    using var f = new Fixture(); HostStats.Failure = "Refresh";
    HostStats.AfterDestroy = () => HostStats.BeforeDestroy = () => throw new InvalidOperationException("recovery failed");
    ExpectError(() => f.Host.InsertScript(30), "Refresh");
    Fixture.Check(HostStats.Syncs == 2 && f.Logs.Any(s => s.Contains("recovery failed")), "recovery error logged without recursion");
    // Scopes are unwound even after recovery itself fails, and future native
    // recovery remains possible without touching the committed Script data.
    HostStats.Reset(); f.Host.SyncScriptList(true, false, false); f.AssertCoherent();
}
static void KeepSelection()
{
    using var f = new Fixture();
    f.Host.AfterDataChange = () =>
    {
        f.Host.SyncScriptList(true, true, false);
        Fixture.Check(f.Host.selectedScriptItem == f.Host.scriptNodeListItemsCache[10], "native selects previous index before outer mutation selects new row");
    };
    f.Host.InsertScript(3); Fixture.Check(f.Host.selectedScriptItem == f.Host.scriptNodeListItemsCache[3], "outer native selection preserved"); f.AssertCoherent();
}
static void FailedRegistration()
{
    foreach (string method in new[] { "InsertScript", "DeleteScript", "SyncScriptList", "DestroyChildren", "AddChild" })
    {
        HarmonyLib.Harmony.RejectMethod = method;
        using var optimizer = new DialogueRowReuse(_ => { });
        Fixture.Check(!optimizer.Install(), "rejected registration reports unavailable");
        Fixture.Check(HarmonyLib.Harmony.Patches.Count == 0, "all partial hooks removed");
    }
    HarmonyLib.Harmony.RejectMethod = null;
}
static void Disabled()
{
    using var f = new Fixture(false); f.Host.InsertScript(30); Fixture.Check(HostStats.Created == 65, "disabled native"); f.AssertCoherent();
}
static void WorkerThread()
{
    using var f = new Fixture(); Task.Run(() => f.Host.InsertScript(30)).GetAwaiter().GetResult(); Fixture.Check(HostStats.Created == 65, "foreign thread cannot intercept"); f.AssertCoherent();
}
static void RepeatedEdits()
{
    using var f = new Fixture(); var random = new Random(20873);
    for (int operation = 0; operation < 120; operation++)
    {
        HostStats.Reset();
        if (operation % 2 == 0) { f.Host.InsertScript(random.Next(65)); Fixture.Check(HostStats.Created == 1 && HostStats.Removed == 0, "repeated insert churn"); }
        else { f.Host.scriptNodeListItemsCache[random.Next(65)].Select(); f.Host.DeleteScript(); Fixture.Check(HostStats.Created == 0 && HostStats.Removed == 1, "repeated delete churn"); }
        f.AssertCoherent();
    }
}
static void DiagnosticSuccess()
{
    using var f = new Fixture(); f.Host.InsertScript();
    Fixture.Check(!f.Logs.Any(s => s.Contains("attempts=")), "no per-row or synchronous report");
    f.Optimizer.Update(10, true);
    string report = f.Logs.Last();
    foreach (string field in new[] { "attempts=1", "captured=1", "eligible=1", "commits=1", "fallbacks=0", "lastRejectedReason=none" }) Fixture.Check(report.Contains(field), "missing success diagnostic " + field);
    int count = f.Logs.Count; f.Optimizer.Update(11, true); f.Optimizer.Update(20, true);
    Fixture.Check(f.Logs.Count == count, "empty reports suppressed and counters reset");
}
static void DiagnosticCaptureBarriers()
{
    var cases = new (Action<Fixture> Change, string Reason)[]
    {
        (f => f.Host.loading = true, "capture.scene-loading-rearranging-or-dragging"),
        (f => new GameObject().transform.SetParent(f.Host.scriptList.transform, false), "capture.parent-children-or-prefab"),
        (f => f.Host.scriptNodeListItemsCache[3].index = 8, "capture.row-index-parent-or-sibling"),
        (f => f.Host.scriptNodeListItemsCache[3].transform.localPosition = new(0, -321, 0), "capture.irregular-row-spacing"),
        (f => f.Host.scriptNodeListItemsCache[3].child.width = 601, "capture.nonuniform-or-invalid-row-size")
    };
    foreach (var (change, reason) in cases)
    {
        using var f = new Fixture(); change(f); f.Host.InsertScript(30); f.Optimizer.Update(10, true);
        string report = f.Logs.Last();
        Fixture.Check(report.Contains("captured=0") && report.Contains("eligible=0") && report.Contains("commits=0") && report.Contains("fallbacks=1") && report.Contains("lastRejectedReason=" + reason), "capture barrier diagnostic " + reason);
    }
}
static void DiagnosticSyncBarriers()
{
    using (var f = new Fixture())
    {
        f.Host.AfterDataChange = () => { var scripts = f.Host.scriptNode.scripts; (scripts[0], scripts[1]) = (scripts[1], scripts[0]); };
        f.Host.InsertScript(30); f.Optimizer.Update(10, true);
        Fixture.Check(f.Logs.Last().Contains("captured=1 eligible=0 commits=0") && f.Logs.Last().Contains("lastRejectedReason=sync.single-change-plan-rejected"), "invalid plan visible");
    }
    using (var f = new Fixture())
    {
        f.Host.AfterDataChange = () => f.Host.scriptListItemPrefab = new();
        f.Host.InsertScript(30); f.Optimizer.Update(10, true);
        Fixture.Check(f.Logs.Last().Contains("lastRejectedReason=sync.node-grid-or-prefab-changed"), "changed host identity visible");
    }
}
static void ExpectError(Action action, string text)
{
    try { action(); } catch (InvalidOperationException error) when (error.Message.Contains(text)) { return; }
    throw new Exception("expected original exception " + text);
}

internal sealed class Fixture : IDisposable
{
    internal readonly ScriptNodeInspector Host = new();
    internal readonly DialogueRowReuse Optimizer;
    internal readonly List<string> Logs = new();
    internal Fixture(bool enabled = true)
    {
        HarmonyLib.Harmony.RejectMethod = null;
        for (int i = 0; i < 64; i++) Host.scriptNode.scripts.Add(new Script { text = "text " + i });
        Host.SyncScriptList(true, false, false);
        Host.scriptNodeListItemsCache[10].Select();
        Optimizer = new(Logs.Add) { Enabled = enabled };
        Check(Optimizer.Install(), "installation"); Optimizer.Update(0, true);
        HostStats.Reset();
    }
    internal void AssertCoherent()
    {
        Check(Host.scriptList.transform.childCount == Host.scriptNode.scripts.Count, "exact child count");
        Check(Host.scriptNodeListItemsCache.Count == Host.scriptNode.scripts.Count, "exact cache count");
        for (int i = 0; i < Host.scriptNodeListItemsCache.Count; i++)
        {
            var row = Host.scriptNodeListItemsCache[i];
            Check(row.index == i && row.transform.GetSiblingIndex() == i, "row/sibling index " + i);
            Check(ReferenceEquals(row.inspector, Host) && ReferenceEquals(row.scriptNode, Host.scriptNode), "row bindings " + i);
            Check(!row.gameObject.Destroyed && row.gameObject.activeInHierarchy, "live row " + i);
            Check(row.scriptPhonetic.Text == Host.scriptNode.scripts[i].text, "text corresponds to committed data " + i);
        }
    }
    internal ScriptListItem[] Rows() => Enumerable.Range(0, Host.scriptNodeListItemsCache.Count).Select(i => Host.scriptNodeListItemsCache[i]).ToArray();
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public void Dispose() { Optimizer.Dispose(); HostStats.Reset(); }
}
