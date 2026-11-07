using System.Reflection;
using Azurite;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using Studio.Scripts.OperationManagement;
using UnityEngine;

var tests = new (string Name, Action Test)[]
{
    ("Alt+2 changes the actual row before one history registration", AppliesBeforeHistory),
    ("native undo and redo restore the row's prior and new slots", UndoRedo),
    ("both Alt keys reach only slots one through five", KeyMapping),
    ("the current speaker slot creates no operation", SameSlot),
    ("invalid slot creates no operation", InvalidSlot),
    ("missing and out-of-range selections create no operation", MissingSelection),
    ("stale row, node and inspector identities create no operation", StaleIdentity),
    ("input fields and dragging block the shortcut", InputGuards),
    ("non-editor playback and export caller gates block the shortcut", CallerGuards),
    ("multi-selection without a certified batch handler fails closed", MultiSelection),
    ("a selection count of zero cannot take the single-row route", ZeroSelectionCount),
    ("a failed native Redo does not register history", RedoFailure),
    ("history failure after Redo reports the changed model and stops further shortcuts", HistoryFailure),
    ("Redo failure after mutation reports uncertainty and stops further shortcuts", RedoPartialFailure),
    ("caller admits only opted-in healthy unprotected AA 1.0.1", CallerAdmission),
    ("authoring predicate is checked only for a pending chord", AuthoringGuard),
    ("disposed shortcut does not handle later input", Disposed),
};
int failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Press(int slot = 2, KeyCode alt = KeyCode.LeftAlt)
{
    Input.Reset(); Input.Held.Add(alt);
    Input.Pressed.Add(slot switch { 1 => KeyCode.Alpha1, 2 => KeyCode.Alpha2, 3 => KeyCode.Alpha3, 4 => KeyCode.Alpha4, 5 => KeyCode.Alpha5, _ => throw new ArgumentOutOfRangeException(nameof(slot)) });
}
static void NoWrite(Fixture f, string reason)
{
    Require(f.Script.speakerSlotNum == 1, reason + ": model changed");
    Require(ScenarioScriptInspectorModifyOperation.Constructions == 0, reason + ": operation constructed");
    Require(OperationManager.History.Count == 0, reason + ": history registered");
}
static void AppliesBeforeHistory()
{
    using var f = new Fixture(); Press(); f.Shortcuts.Update(true);
    Require(f.Script.speakerSlotNum == 2, "actual node row did not change");
    Require(ScenarioScriptInspectorModifyOperation.Constructions == 1 && ScenarioScriptInspectorModifyOperation.Redos == 1, "expected one native operation and one Redo");
    Require(OperationManager.History.Count == 1 && OperationManager.Links.Single() == (false, false), "expected one unlinked history entry");
    Require(ScenarioScriptInspectorModifyOperation.Events.SequenceEqual(new[] { "construct", "redo", "history" }), "native Redo must precede history registration");
    Require(f.Inspector.NameSyncs == 1 && f.Inspector.SlotSyncs == 1 && f.Inspector.ObservedSlots.SequenceEqual(new[] { 2, 2 }), "read-only sync did not observe the changed model");
    var operation = OperationManager.History.Single();
    Require(operation.Script.Pointer == f.Script.Pointer && operation.Before.speakerSlotNum == 1 && operation.After.speakerSlotNum == 2, "operation snapshots or row identity are wrong");
}
static void UndoRedo()
{
    using var f = new Fixture(); Press(5); f.Shortcuts.Update(true);
    var operation = OperationManager.History.Single();
    Require(f.Script.speakerSlotNum == 5, "Redo did not apply slot five");
    operation.Undo(); Require(f.Script.speakerSlotNum == 1, "Undo did not restore slot one");
    operation.Redo(); Require(f.Script.speakerSlotNum == 5, "Redo did not reapply slot five");
    Require(OperationManager.History.Count == 1, "undo/redo changed history count");
}
static void KeyMapping()
{
    foreach (var (slot, alt) in new[] { (1, KeyCode.LeftAlt), (3, KeyCode.RightAlt), (4, KeyCode.LeftAlt), (5, KeyCode.RightAlt) })
    {
        using var f = new Fixture(); Press(slot, alt); f.Shortcuts.Update(true);
        Require(f.Script.speakerSlotNum == slot, $"Alt+{slot} wrote a different slot");
        Require(OperationManager.History.Count == (slot == 1 ? 0 : 1), $"Alt+{slot} registered the wrong history count");
    }
}
static void SameSlot()
{
    using var f = new Fixture(); Press(1); f.Shortcuts.Update(true); NoWrite(f, "same slot");
}
static void InvalidSlot()
{
    foreach (int slot in new[] { 0, 6, -1 })
    {
        using var f = new Fixture();
        var method = typeof(AuthoringSpeakerShortcuts).GetMethod("ApplySpeaker", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("ApplySpeaker route is missing");
        var result = method.Invoke(f.Shortcuts, new object[] { slot });
        Require(result is false, $"slot {slot} was accepted"); NoWrite(f, $"invalid slot {slot}");
    }
}
static void MissingSelection()
{
    using (var f = new Fixture()) { f.Inspector.selectedScriptItem = null; Press(); f.Shortcuts.Update(true); NoWrite(f, "missing selection"); }
    using (var f = new Fixture()) { f.Row.index = -1; Press(); f.Shortcuts.Update(true); NoWrite(f, "negative index"); }
    using (var f = new Fixture()) { f.Row.index = 1; Press(); f.Shortcuts.Update(true); NoWrite(f, "out-of-range index"); }
}
static void StaleIdentity()
{
    using (var f = new Fixture()) { f.Row.scriptNode = new ScriptNode { scripts = { new Script { speakerSlotNum = 1 } } }; Press(); f.Shortcuts.Update(true); NoWrite(f, "different node"); }
    using (var f = new Fixture()) { f.Row.inspector = new ScriptNodeInspector(); Press(); f.Shortcuts.Update(true); NoWrite(f, "different inspector"); }
    using (var f = new Fixture()) { f.Inspector.scriptNodeListItemsCache[0] = new ScriptListItem(); Press(); f.Shortcuts.Update(true); NoWrite(f, "different cached row"); }
    using (var f = new Fixture()) { f.Row.selected = false; Press(); f.Shortcuts.Update(true); NoWrite(f, "deselected row"); }
    using (var f = new Fixture()) { f.Row.gameObject.activeInHierarchy = false; Press(); f.Shortcuts.Update(true); NoWrite(f, "inactive row"); }
}
static void InputGuards()
{
    using (var f = new Fixture()) { UIInput.current = new UIInput(); Press(); f.Shortcuts.Update(true); NoWrite(f, "current input"); }
    using (var f = new Fixture()) { UIInput.selection = new UIInput(); Press(); f.Shortcuts.Update(true); NoWrite(f, "selected input"); }
    using (var f = new Fixture()) { UICamera.isDragging = true; Press(); f.Shortcuts.Update(true); NoWrite(f, "dragging"); }
    using (var f = new Fixture()) { Press(); Input.Held.Clear(); f.Shortcuts.Update(true); NoWrite(f, "no Alt key"); }
}
static void CallerGuards()
{
    using (var f = new Fixture()) { Press(); f.Shortcuts.Update(false); NoWrite(f, "playback context"); }
    using (var f = new Fixture()) { Press(); f.Shortcuts.Update(false); NoWrite(f, "export context"); }
    using (var f = new Fixture()) { f.Inspector.loading = true; Press(); f.Shortcuts.Update(true); NoWrite(f, "loading editor"); }
    using (var f = new Fixture()) { f.Inspector.unloading = true; Press(); f.Shortcuts.Update(true); NoWrite(f, "unloading editor"); }
}
static void MultiSelection()
{
    using var f = new Fixture(); f.Shortcuts.ConfigureBatch(() => 2, null!);
    Press(); f.Shortcuts.Update(true); NoWrite(f, "uncertified batch");
}
static void ZeroSelectionCount()
{
    using var f = new Fixture(); f.Shortcuts.ConfigureBatch(() => 0, null!);
    Press(); f.Shortcuts.Update(true); NoWrite(f, "zero selected rows");
}
static void RedoFailure()
{
    using var f = new Fixture(); ScenarioScriptInspectorModifyOperation.ThrowOnRedo = true;
    Press(); f.Shortcuts.Update(true);
    Require(f.Script.speakerSlotNum == 1, "failed Redo changed the model");
    Require(ScenarioScriptInspectorModifyOperation.Constructions == 1 && ScenarioScriptInspectorModifyOperation.Redos == 1, "failed native operation did not run once");
    Require(OperationManager.History.Count == 0, "failed Redo was registered in history");
    Require(f.Inspector.NameSyncs == 0 && f.Inspector.SlotSyncs == 0, "failed Redo refreshed the inspector");
    Require(f.Logs.Count == 1 && f.Logs[0].Contains("failed closed"), "failure was not reported");
}
static void HistoryFailure()
{
    using var f = new Fixture(); OperationManager.ThrowOnWrite = true;
    Press(); f.Shortcuts.Update(true);
    Require(f.Script.speakerSlotNum == 2 && OperationManager.History.Count == 0, "Redo did not change the row before history failed");
    Require(f.Logs.Count == 1 && f.Logs[0].Contains("changed") && f.Logs[0].Contains("history"), "partial write was misreported as no mutation");
    Press(3); f.Shortcuts.Update(true);
    Require(f.Script.speakerSlotNum == 2 && ScenarioScriptInspectorModifyOperation.Constructions == 1, "shortcut retried after an unrecorded model change");
}
static void RedoPartialFailure()
{
    using var f = new Fixture(); ScenarioScriptInspectorModifyOperation.ThrowAfterMutation = true;
    Press(); f.Shortcuts.Update(true);
    Require(f.Script.speakerSlotNum == 2 && OperationManager.History.Count == 0, "fixture did not model a partial Redo failure");
    Require(f.Logs.Count == 1 && f.Logs[0].Contains("changed"), "partial native failure was misreported as no mutation");
    Press(3); f.Shortcuts.Update(true);
    Require(f.Script.speakerSlotNum == 2 && ScenarioScriptInspectorModifyOperation.Constructions == 1, "shortcut retried after a partial native failure");
}
static void CallerAdmission()
{
    const string game = "0776516C59811C724C450E205930546C5D102975F279E33C46C96B878FA06C8B";
    const string metadata = "B4AEEE165E7E10E0F46F85E095FD94487FDB041215F6DCB7AE9DE5D28A364F43";
    var aa101 = HostProfile.ResolvePortable(game, metadata, true);
    bool Admit(HostProfile profile, bool plugin = true, bool option = true, bool supported = true, bool probe = true,
        bool export = false, bool suspended = false, bool progressive = false, bool mutation = false, bool focused = true) =>
        AuthoringSpeakerShortcuts.CanUpdate(profile, plugin, option, supported, probe, export, suspended, progressive, mutation, focused);
    Require(Admit(aa101), "verified opt-in authoring context was rejected");
    Require(!Admit(aa101, option: false), "default-off option was ignored");
    Require(!Admit(aa101, plugin: false), "disabled plugin was ignored");
    Require(!Admit(aa101, supported: false) && !Admit(aa101, probe: false), "unhealthy host was admitted");
    Require(!Admit(aa101, export: true) && !Admit(aa101, suspended: true), "export owner was admitted");
    Require(!Admit(aa101, progressive: true) && !Admit(aa101, mutation: true), "in-flight editor work was admitted");
    Require(!Admit(aa101, focused: false), "lost focus was admitted");
    Require(!Admit(HostProfile.Unsupported), "unknown host was admitted");
    Require(!Admit(HostProfile.ResolvePortable("2B8C36F681A3932071D4E609BB034034B087D4D87DFA88A7FFC1A4A206169528",
        "107C1E0F80C1C6A87CA7239C803DF711ED1DD1F9D2BBAAE0EBF5C9EA9F6078A9", true)), "fix host inherited AA 1.0.1 authoring capability");
}
static void AuthoringGuard()
{
    int calls = 0;
    using var f = new Fixture(() => { calls++; return false; });
    f.Shortcuts.Update(true);
    Require(calls == 0, "host predicate ran without a chord");
    Press(); f.Shortcuts.Update(true);
    Require(calls == 1, "host predicate was not checked once for the chord");
    NoWrite(f, "authoring predicate rejected");
}
static void Disposed()
{
    using var f = new Fixture(); f.Shortcuts.Dispose(); Press(); f.Shortcuts.Update(true); NoWrite(f, "disposed");
}

sealed class Fixture : IDisposable
{
    public readonly Script Script = new() { speakerSlotNum = 1 };
    public readonly ScriptNode Node = new();
    public readonly ScriptNodeInspector Inspector = new();
    public readonly ScriptListItem Row = new();
    public readonly List<string> Logs = new();
    public readonly AuthoringSpeakerShortcuts Shortcuts;
    public Fixture(Func<bool>? authoringAllowed = null)
    {
        Input.Reset(); UICamera.isDragging = false; UIInput.current = null; UIInput.selection = null;
        OperationManager.History.Clear(); OperationManager.Links.Clear();
        ScenarioScriptInspectorModifyOperation.Events.Clear(); ScenarioScriptInspectorModifyOperation.Constructions = 0;
        ScenarioScriptInspectorModifyOperation.Redos = 0; ScenarioScriptInspectorModifyOperation.ThrowOnRedo = false;
        ScenarioScriptInspectorModifyOperation.ThrowAfterMutation = false; OperationManager.ThrowOnWrite = false;
        Node.scripts.Add(Script); Inspector.scriptNode = Node;
        Row.scriptNode = Node; Row.inspector = Inspector; Row.index = 0;
        Inspector.selectedScriptItem = Row; Inspector.scriptNodeListItemsCache.Add(Row);
        ScriptNodeInspector.instance = Inspector;
        Shortcuts = new AuthoringSpeakerShortcuts(Logs.Add, authoringAllowed ?? (() => true));
    }
    public void Dispose() => Shortcuts.Dispose();
}
