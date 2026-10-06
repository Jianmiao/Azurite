using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using UnityEngine;

namespace Azurite;

/// <summary>
/// Experimental, verified-host-only virtual presentation for the fixed-height
/// dialogue list. Model edits, undo registration and selection remain AA code.
/// Cache retains logical length, with null offscreen entries. Only the audited
/// native access paths may run with this cache; unsupported operations first
/// restore a dense list. This is not an API for arbitrary third-party cache readers.
/// </summary>
internal sealed class DialogueVirtualization : IDisposable
{
    private const string Owner = "halocue.azurite.dialogue-virtualization";
    private readonly Harmony _harmony = new(Owner);
    private readonly Action<string> _log;
    private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
    private static DialogueVirtualization? _active;
    private State? _state;
    private bool _installed, _allowed, _failed, _bypass, _disposed;
    private int _nativeDepth;
    private int _required = -1;
    private long _syncs, _created, _bound, _fallbacks;
    private double _nextLog;
    private ScriptNodeInspector? _resumeInspector;
    private int _fillFrame = -1, _fillRows;
    private long _fillTicks;
    private bool _tailAnchorReapplying;
    private OpenScope? _opening;
    // Native ScriptListItem.Select schedules CenterOn for the next frame. The
    // opening prefix can suppress the immediate call, but the deferred call
    // runs after Load has unwound and therefore needs a short-lived identity
    // guard of its own. Keep the guard target-specific so user selection of a
    // different row remains native and interactive.
    private ScriptNodeInspector? _tailAnchorInspector;
    private IntPtr _tailAnchorTarget;
    private int _tailAnchorUntilFrame = -1;
    private bool _shuttingDown;
    public string Reason { get; private set; } = "not started";

    private sealed class OpenScope
    {
        internal ScriptNodeInspector Inspector = null!;
        internal OpenScope? Parent;
        internal long Started, SyncTicks, CreatedBefore;
        internal int Tail = -1;
    }

    private sealed class StartupSelection
    {
        internal ScriptListItem Row = null!;
        internal ScriptNode Node = null!;
        internal IntPtr Data;
        internal int Index;
    }

    private sealed class Slot
    {
        internal ScriptListItem Row = null!;
        internal IntPtr Data;
        internal int Index = -1;
        internal bool Dirty;
    }
    private sealed class State
    {
        internal ScriptNodeInspector Inspector = null!;
        internal ScriptNode Node = null!;
        internal UIGrid Grid = null!;
        internal CenterableUIScrollView Scroll = null!;
        internal UIPanel Panel = null!;
        internal GameObject Prefab = null!;
        internal readonly List<Slot> Slots = new();
        internal bool GridEnabled;
        internal Vector3 Origin;
        internal float Pitch;
        internal Bounds FirstBounds;
        internal int RowWidth, RowHeight;
        internal int Count;
        internal int[] Indices = Array.Empty<int>();
        internal bool Busy;
        internal bool Pending;
    }

    public DialogueVirtualization(Action<string> log) => _log = log;
    public bool Enabled { get; set; }
    public bool IsInstalled => _installed && !_disposed;
    public bool IsActive => _state != null;
    public int LiveRows => _state?.Slots.Count ?? 0;
    internal bool HasPendingRows => _state?.Pending ?? false;

    public bool Install()
    {
        if (_disposed || (_active != null && _active != this)) return false;
        if (_installed) return true;
        try
        {
            _active = this;
            Patch(typeof(ScriptNodeInspector), "Start", Type.EmptyTypes, nameof(BeforeInspectorStart), nameof(AfterInspectorStart));
            Patch(typeof(ScriptNodeInspector), "SyncScriptList", new[] { typeof(bool), typeof(bool), typeof(bool) }, nameof(BeforeSync));
            Patch(typeof(ScriptNodeInspector), "InsertScript", new[] { typeof(int) }, nameof(BeforeInsert), null, nameof(AfterMutation));
            Patch(typeof(ScriptNodeInspector), "DeleteScript", Type.EmptyTypes, nameof(BeforeDelete), null, nameof(AfterMutation));
            Patch(typeof(ScriptNodeInspector), "RestoreStatus", Type.EmptyTypes, nameof(BeforeRestore));
            Patch(typeof(ScriptNodeInspector), "RestoreStatus", new[] { typeof(ScriptNode.ScriptNodeInspectorInfo) }, nameof(BeforeRestoreInfo));
            Patch(typeof(ScriptNodeInspector), "Load", new[] { typeof(Node), typeof(bool) }, nameof(BeforeLoad), null, nameof(AfterLoad));
            Patch(typeof(ScriptNodeInspector), "Unload", new[] { typeof(bool) }, nameof(BeforeUnload), null, nameof(AfterUnload));
            foreach (string method in new[] { "RearrangeScriptList", "ValidateNewArrangement", "UpdateScriptListCache", "Method_Private_Void_0" })
                Patch(typeof(ScriptNodeInspector), method, Type.EmptyTypes, nameof(BeforeNativeScope), null, nameof(AfterNativeScope));
            Patch(typeof(ScriptListItem), "OnDraggerDragStart", Type.EmptyTypes, nameof(BeforeDrag));
            Patch(typeof(UIGrid), "Reposition", Type.EmptyTypes, nameof(BeforeGrid));
            Patch(typeof(UIScrollView), "get_bounds", Type.EmptyTypes, nameof(BeforeBounds));
            Patch(typeof(UIScrollView), "LateUpdate", Type.EmptyTypes, null, nameof(AfterLateScroll));
            Patch(typeof(UIScrollView), "SetDragAmount", new[] { typeof(float), typeof(float), typeof(bool) }, null, nameof(AfterScrollInput));
            // Native selection calls CenterOn while Load is still unwinding. Its
            // verified implementation schedules a next-frame callback, which
            // would overwrite the logical tail anchor after our sparse bounds
            // are installed. Tail entry owns that transient positioning call.
            Patch(typeof(CenterableUIScrollView), "CenterOn", new[] { typeof(Transform), typeof(bool), typeof(bool) }, nameof(BeforeOpeningCenter));
            Patch(typeof(CenterableUIScrollView), "CenterOn", new[] { typeof(Transform), typeof(Vector3), typeof(bool) }, nameof(BeforeOpeningCenterWithPanelCenter));
            // Undo implementations have direct indexed cache reads or enumerate it.
            // A dense cache is needed before the native operation starts, including
            // its nested Sync, not merely in a postfix after those reads occurred.
            foreach (string typeName in new[] { "ScenarioScriptAddOperation", "ScenarioScriptDeleteOperation", "ScenarioScriptInspectorModifyOperation", "ScriptNodeInspectorStateChangeOperation", "ScriptNodeRearrangeOperation" })
            {
                Type type = typeof(ScriptNodeInspector).Assembly.GetType("Studio.Scripts.OperationManagement." + typeName, true)!;
                foreach (string method in new[] { "Undo", "Redo" }) Patch(type, method, Type.EmptyTypes, nameof(BeforeNativeScope), null, nameof(AfterNativeScope));
            }
            // External authoring API may inspect editor state. Its entrypoints
            // must see AA's dense list; re-entry remains native for this call.
            Type? session = typeof(ScriptNodeInspector).Assembly.GetType("AzureArchive.Automation.AuthoringEditorSession");
            if (session != null)
                foreach (MethodInfo method in session.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (!method.IsSpecialName && !IsAuditedModelOnlyAuthoring(method) && (method.Name.Contains("Capture") || method.Name.Contains("Apply") || method.Name.Contains("Restore") || method.Name.Contains("Save")))
                        Patch(method, nameof(BeforeNativeScope), null, nameof(AfterNativeScope));
            _installed = true;
            _log("dialogue virtualization hooks ready; experimental fixed-host row pool with native fallback for reorder, history, authoring APIs and export handoff.");
            return true;
        }
        catch (Exception error)
        {
            try { _harmony.UnpatchSelf(); } catch { }
            _active = null; _log("dialogue virtualization unavailable: " + error); return false;
        }
    }
    private static bool IsAuditedModelOnlyAuthoring(MethodInfo method)
    {
        ParameterInfo[] args = method.GetParameters();
        if (method.Name is "Capture" or "Save") return args.Length == 0;
        if (method.Name == "BeginSave") return args.Length == 1 && args[0].ParameterType == typeof(bool);
        if (method.Name is "CheckSave" or "ReleaseSave") return args.Length == 1 && args[0].ParameterType.FullName == "AzureArchive.Automation.AuthoringSaveOperation";
        return method.Name == "CompleteSave" && args.Length == 2 && args[0].ParameterType.FullName == "AzureArchive.Automation.AuthoringSaveOperation" && args[1].ParameterType == typeof(string);
    }
    private void Patch(Type type, string name, Type[] args, string? prefix, string? postfix = null, string? finalizer = null) =>
        Patch(AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name), prefix, postfix, finalizer);
    private void Patch(MethodInfo method, string? prefix, string? postfix, string? finalizer)
    {
        _harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(DialogueVirtualization), prefix) { priority = Priority.First },
            postfix == null ? null : new HarmonyMethod(typeof(DialogueVirtualization), postfix), null,
            finalizer == null ? null : new HarmonyMethod(typeof(DialogueVirtualization), finalizer), null);
        if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) != true) throw new InvalidOperationException("Hook not registered: " + method.Name);
    }

    public void Update(double now, bool allowed)
    {
        if (_shuttingDown || Thread.CurrentThread.ManagedThreadId != _thread) return;
        if (_tailAnchorUntilFrame >= 0 && Time.frameCount > _tailAnchorUntilFrame)
            ClearTailAnchor();
        _allowed = allowed && Enabled && IsInstalled && !_failed;
        if (!_allowed && _state != null) Materialize("suspend");
        if (_allowed && _state != null && !_state.Busy)
        {
            try
            {
                State s = _state;
                if (s.Inspector == null || s.Node == null) { _state = null; }
                else if (!Same(s.Inspector.scriptNode, s.Node) || !Same(s.Inspector.scriptList, s.Grid) || !Same(s.Inspector.scriptListItemPrefab, s.Prefab)) Materialize("host bindings changed");
                else if (s.Slots[0].Row.child.width != s.RowWidth || s.Slots[0].Row.child.height != s.RowHeight) Materialize("row dimensions changed");
                else if (s.Inspector.isActiveAndEnabled && !s.Inspector.loading && !s.Inspector.unloading && !UICamera.isDragging) RefreshWindow(s, false);
            }
            catch (Exception error) { Recover(error); }
        }
        if (_allowed && _state == null && _resumeInspector != null && _nativeDepth == 0 && !UICamera.isDragging)
        {
            ScriptNodeInspector inspector = _resumeInspector;
            if (inspector == null || inspector.scriptNode == null) _resumeInspector = null;
            else if (inspector.isActiveAndEnabled && !inspector.loading && !inspector.unloading && !inspector.rearrangeScheduled)
            {
                _resumeInspector = null;
                inspector.SyncScriptList(true, true, false);
            }
        }
        if (now < _nextLog) return;
        _nextLog = now + 5;
        if (_syncs + _created + _bound + _fallbacks != 0)
        {
            _log($"dialogue-virtualization active={IsActive} logical={_state?.Count ?? 0} live={LiveRows} syncs={_syncs} created={_created} binds={_bound} nativeFallbacks={_fallbacks} reason={Reason}; presentation only, not a measured latency.");
            _syncs = _created = _bound = _fallbacks = 0;
        }
    }
    public void Suspend() { _allowed = false; if (_state != null) Materialize("render handoff"); }
    private bool MayRun => _allowed && Enabled && !_failed && !_bypass && !_shuttingDown && _nativeDepth == 0 && Thread.CurrentThread.ManagedThreadId == _thread;

    private static void BeforeInspectorStart(ScriptNodeInspector __instance, out StartupSelection? __state)
    {
        __state = null;
        DialogueVirtualization? a = _active;
        if (a == null || !a.MayRun || __instance == null || __instance.unloading) return;
        ScriptListItem? row = __instance.selectedScriptItem;
        ScriptNode? node = __instance.scriptNode;
        if (row == null || node?.scripts == null || !row.selected || !row.isActiveAndEnabled ||
            !row.gameObject.activeInHierarchy || !Same(row.inspector, __instance) || !Same(row.scriptNode, node)) return;
        int index = row.index;
        var cache = __instance.scriptNodeListItemsCache;
        if (index < 0 || index >= node.scripts.Count || cache == null || index >= cache.Count || !Same(cache[index], row)) return;
        __state = new StartupSelection { Row = row, Node = node, Data = node.scripts[index].Pointer, Index = index };
    }

    private static void AfterInspectorStart(ScriptNodeInspector __instance, StartupSelection? __state)
    {
        DialogueVirtualization? a = _active;
        if (__state == null || a == null || !a.MayRun || __instance == null ||
            !__instance.isActiveAndEnabled || __instance.unloading || __instance.selectedScriptItem != null) return;
        ScriptListItem row = __state.Row;
        ScriptNode node = __state.Node;
        int index = __state.Index;
        var cache = __instance.scriptNodeListItemsCache;
        if (row == null || node == null || !row.selected || !row.isActiveAndEnabled || !row.gameObject.activeInHierarchy ||
            !Same(__instance.scriptNode, node) || !Same(row.scriptNode, node) || !Same(row.inspector, __instance) ||
            row.index != index || node.scripts == null || index >= node.scripts.Count || node.scripts[index].Pointer != __state.Data ||
            cache == null || index >= cache.Count || !Same(cache[index], row)) return;

        // Native Start (100689938) binds the preview, then clears the inspector's
        // selectedScriptItem without clearing Selectable.selected. Early Load
        // can already have selected the tail: Select would now return false,
        // Delete would see null, and a different click would leave two highlights.
        // Restore only that exact live selection through native OnChildSelect;
        // it also shows the pane and renders the now-initialized preview. Keep
        // this initialization sync out of the user's undo history.
        bool wasLoading = __instance.loading;
        try
        {
            __instance.loading = true;
            __instance.OnChildSelect(row);
            a._log($"dialogue startup selection restored index={index} logical={node.scripts.Count}; native pane/preview synchronized; no additional Select or row construction.");
        }
        catch (Exception error) { a._log("dialogue startup selection refresh failed: " + error); }
        finally { __instance.loading = wasLoading; }
    }

    private static bool BeforeSync(ScriptNodeInspector __instance, bool mute, bool keepSelected, ref bool firstOnTop)
    {
        DialogueVirtualization? a = _active;
        if (a == null || !a.MayRun) return true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            bool opening = a._opening != null && Same(a._opening.Inspector, __instance) && __instance.loading;
            // Native Load has already entered loading and reset the inspector.
            // Deselect runs under that flag, retaining its native history rules.
            // No native path before this Sync enumerates the dialogue cache.
            if (opening && a._state != null && Same(a._state.Inspector, __instance))
            {
                __instance.selectedScriptItem?.Deselect(mute);
                a.ReleasePresentation("node load");
            }
            int count = __instance.scriptNode?.scripts?.Count ?? 0;
            if (opening && count > 0 && count < 32 && __instance.scriptNode.inspectorInfo != null)
            {
                // Small lists keep AA's native row construction, but follow
                // the same requested continue-writing entry position. Suppress
                // native deferred FirstOnTop so it cannot undo tail selection.
                __instance.scriptNode.inspectorInfo.lastScriptIndex = count - 1;
                a._opening!.Tail = count - 1;
                firstOnTop = false;
                return true;
            }
            if (!a.Eligible(__instance)) { if (a._state != null) a.Materialize("unsupported list"); return true; }
            int selected = __instance.selectedScriptItem?.index ?? -1;
            int restored = __instance.loading ? __instance.scriptNode.inspectorInfo?.lastScriptIndex ?? -1 : -1;
            // Native Sync writes the node name and deselects before rebuilding.
            __instance.nodeNameInput.Set(__instance.scriptNode.nodeName, false);
            __instance.selectedScriptItem?.Deselect(mute);
            State s = a.EnsureState(__instance);
            s.Busy = true;
            try
            {
                a.ResizeCache(s);
                if (opening && s.Count > 0 && s.Node.inspectorInfo != null)
                {
                    int tail = s.Count - 1;
                    // Native Load reads inspectorInfo AFTER this Sync and then
                    // calls cache[lastScriptIndex].Select while loading=true.
                    // Pin/position that exact row instead of selecting twice.
                    s.Node.inspectorInfo.lastScriptIndex = tail;
                    a._opening!.Tail = tail;
                    a.RefreshWindow(s, true, required: tail, fill: false);
                    // Native CenterOn is deferred while CenterableUIScrollView
                    // is not ready and a later OnChildSelect can leave a stale
                    // centeringChild. The normalized bottom anchor is applied
                    // after Load, once the full mathematical bounds exist.
                    // Do not perform a second fill against the pre-anchor
                    // viewport. AnchorTail moves the full logical bounds after
                    // native Load returns; the next frame then fills the actual
                    // bottom viewport from bottom to top.
                }
                else a.RefreshWindow(s, true, keepSelected ? selected : -1, a._required, restored);
                if (!opening)
                {
                    if (keepSelected && selected >= 0 && selected < s.Count) __instance.scriptNodeListItemsCache[selected].Select(false);
                    else if (firstOnTop && s.Count > 0) s.Scroll.FirstOnTop(false);
                }
                a._syncs++;
                a.Reason = "bounded visible row pool; bottom-to-top incremental presentation";
            }
            finally { s.Busy = false; }
            return false;
        }
        catch (Exception error) { a.Recover(error); return true; }
        finally { if (a._opening != null && Same(a._opening.Inspector, __instance)) a._opening.SyncTicks += Stopwatch.GetTimestamp() - started; }
    }

    private bool Eligible(ScriptNodeInspector i)
    {
        Reason = "ineligible inspector, count or active operation";
        if (i == null || i.unloading || i.rearrangeScheduled || UICamera.isDragging || i.scriptNode?.scripts == null || i.scriptNode.scripts.Count < 32 || i.scriptNode.scripts.Count > 100000 || i.scriptList == null || i.scriptListScroll?.panel == null || i.scriptListItemPrefab == null || i.scriptNodeListItemsCache == null || i.nodeNameInput == null) return false;
        UIGrid g = i.scriptList;
        Reason = $"grid arrangement={g.arrangement} maxPerLine={g.maxPerLine} inverted={g.inverted} pivot={g.pivot} smooth={g.animateSmoothly} fade={g.animateFadeIn} customSort={g.onCustomSort != null} callback={g.onReposition != null}";
        return g.arrangement == UIGrid.Arrangement.Vertical && g.maxPerLine == 0 && !g.inverted &&
            (g.pivot == UIWidget.Pivot.TopLeft || g.pivot == UIWidget.Pivot.Top || g.pivot == UIWidget.Pivot.TopRight) &&
            !g.animateSmoothly && !g.animateFadeIn && g.onCustomSort == null && g.onReposition == null &&
            (g.mSprings == null || g.mSprings.Count == 0) && i.scriptListScroll.panel.hasClipping;
    }

    private State EnsureState(ScriptNodeInspector i)
    {
        if (_state != null && Same(_state.Inspector, i) && Same(_state.Node, i.scriptNode)) return _state;
        if (_state != null) Materialize("different node");
        var s = new State { Inspector = i, Node = i.scriptNode, Grid = i.scriptList, Scroll = i.scriptListScroll, Panel = i.scriptListScroll.panel, Prefab = i.scriptListItemPrefab, GridEnabled = i.scriptList.enabled, Count = i.scriptNode.scripts.Count };
        // Calibrate exactly two actual row prefabs using AA's native layout.
        // This avoids constructing every row just to discover fixed geometry.
        NGUITools.DestroyChildren(s.Grid.transform, 0); i.scriptNodeListItemsCache.Clear();
        for (int index = 0; index < 2; index++)
        {
            Slot slot = NewSlot(s); slot.Row.Init(s.Node, index, i); ValidateInitializedRow(slot.Row); slot.Index = index; slot.Data = s.Node.scripts[index].Pointer;
            i.scriptNodeListItemsCache.Add(slot.Row);
        }
        s.Grid.Reposition();
        s.Origin = s.Slots[0].Row.transform.localPosition;
        s.Pitch = s.Origin.y - s.Slots[1].Row.transform.localPosition.y;
        if (!float.IsFinite(s.Pitch) || s.Pitch <= 0) throw new InvalidOperationException("Unsupported dialogue row pitch");
        s.FirstBounds = NGUIMath.CalculateRelativeWidgetBounds(s.Grid.transform, s.Slots[0].Row.transform, false, true);
        s.RowWidth = s.Slots[0].Row.child.width; s.RowHeight = s.Slots[0].Row.child.height;
        if (s.FirstBounds.size.y <= 0 || s.FirstBounds.size.x <= 0 || s.Pitch < s.FirstBounds.size.y * .75f) throw new InvalidOperationException("Unsupported row geometry");
        s.Grid.enabled = false;
        _state = s;
        return s;
    }

    private Slot NewSlot(State s)
    {
        if (s.Slots.Count >= DialogueVirtualizationWindow.MaximumRows) throw new InvalidOperationException("Virtual row pool bound reached");
        GameObject go = NGUITools.AddChild(s.Grid.transform, s.Prefab);
        Utils.Util.AdaptWidgetToParent(go, s.Grid.gameObject);
        ScriptListItem row = go.GetComponent<ScriptListItem>();
        // Native Init resolves child from transform.GetChild(0); checking it
        // before Init rejects valid AA prefabs and disables virtualization.
        // Only serialized references can be required at creation time.
        if (row == null || row.scriptPhonetic == null || row.nameLabel == null) throw new InvalidOperationException("Invalid dialogue prefab: missing row, text or name reference");
        var slot = new Slot { Row = row }; s.Slots.Add(slot); _created++; return slot;
    }

    private static void ValidateInitializedRow(ScriptListItem row)
    {
        if (row.child == null) throw new InvalidOperationException("Native dialogue Init did not bind the row widget");
    }

    private void ResizeCache(State s)
    {
        var cache = s.Inspector.scriptNodeListItemsCache;
        int count = s.Node.scripts.Count;
        foreach (Slot slot in s.Slots) if (slot.Index >= 0 && slot.Index < cache.Count) cache[slot.Index] = null!;
        while (cache.Count < count) cache.Add(null!);
        while (cache.Count > count) cache.RemoveAt(cache.Count - 1);
        s.Count = count;
    }

    private void RefreshWindow(State s, bool force, int selection = -1, int required = -1, int restored = -1, bool fill = true)
    {
        // Init/Refresh can provoke native grid/scroll notifications. They must
        // not recursively consume another fill batch inside this one.
        bool wasBusy = s.Busy;
        s.Busy = true;
        try { RefreshWindowCore(s, force, selection, required, restored, fill); }
        finally { s.Busy = wasBusy; }
    }

    private void RefreshWindowCore(State s, bool force, int selection, int required, int restored, bool fill)
    {
        if (s.Node.scripts.Count != s.Count) throw new InvalidOperationException("Model changed without SyncScriptList");
        ScriptListItem? selectedRow = s.Inspector.selectedScriptItem;
        if (selectedRow != null) selection = selectedRow.index;
        Vector4 clip = s.Panel.finalClipRegion;
        Vector3 topPoint = s.Grid.transform.InverseTransformPoint(s.Panel.transform.TransformPoint(new Vector3(clip.x, clip.y + clip.w * .5f, 0)));
        Vector3 bottomPoint = s.Grid.transform.InverseTransformPoint(s.Panel.transform.TransformPoint(new Vector3(clip.x, clip.y - clip.w * .5f, 0)));
        int[] desired = DialogueVirtualizationWindow.Plan(s.Count, s.Origin.y, s.Pitch, topPoint.y, bottomPoint.y, selection, required, restored);
        bool changed = desired.Length != s.Indices.Length;
        if (!changed) for (int n = 0; n < desired.Length; n++) if (desired[n] != s.Indices[n]) { changed = true; break; }
        if (!force && !changed && !s.Pending) return;
        var used = new HashSet<Slot>();
        var assigned = new Slot[desired.Length];
        // Match unchanged Script identities first. Selected objects must never
        // be rebound, including while scrolling them outside the viewport.
        for (int n = 0; n < desired.Length; n++)
        {
            IntPtr data = s.Node.scripts[desired[n]].Pointer;
            foreach (Slot slot in s.Slots)
                if (slot.Data == data && !used.Contains(slot)) { assigned[n] = slot; used.Add(slot); if (force) slot.Dirty = true; break; }
        }
        // The UI keeps its full logical extent immediately. Missing rows are
        // empty presentation slots, never reordered or removed model records.
        // Abandon a previous viewport's pending work whenever the user scrolls.
        var cache = s.Inspector.scriptNodeListItemsCache;
        foreach (Slot slot in s.Slots) if (slot.Index >= 0 && slot.Index < cache.Count && Same(cache[slot.Index], slot.Row)) cache[slot.Index] = null!;
        foreach (Slot slot in s.Slots) if (!used.Contains(slot))
        {
            if (Same(slot.Row, selectedRow)) throw new InvalidOperationException("Selected dialogue escaped pinned pool");
            slot.Row.gameObject.SetActive(false); slot.Index = -1; slot.Data = IntPtr.Zero; slot.Dirty = false;
        }

        bool presentationDirty = false;
        void Bind(int n)
        {
            int index = desired[n];
            Slot? slot = assigned[n];
            if (slot == null)
            {
                Slot? available = null;
                foreach (Slot candidate in s.Slots)
                    if (!used.Contains(candidate) && !Same(candidate.Row, selectedRow)) { available = candidate; break; }
                available ??= NewSlot(s); assigned[n] = available; used.Add(available);
                slot = available;
            }
            IntPtr data = s.Node.scripts[index].Pointer;
            bool bind = slot.Data != data || slot.Index != index;
            // Native Init creates child references and text. Do not expose an
            // old row's collider while it is waiting to be rebound to new data.
            slot.Row.gameObject.SetActive(true);
            if (bind) { slot.Row.Init(s.Node, index, s.Inspector); ValidateInitializedRow(slot.Row); _bound++; presentationDirty = true; }
            else if (slot.Dirty) { slot.Row.Refresh(); presentationDirty = true; }
            slot.Index = index; slot.Data = data; slot.Dirty = false;
        }

        // Native Load/Insert/Delete/Restore reads these cache entries in the
        // same call stack, so required pins are exempt from the fill budget.
        // Row zero also supplies FirstOnTop's first physical-child geometry.
        foreach (int pin in new[] { 0, selection, required, restored })
        {
            int n = Array.BinarySearch(desired, pin);
            if (n >= 0) Bind(n);
        }
        if (_fillFrame != Time.frameCount) { _fillFrame = Time.frameCount; _fillRows = 0; _fillTicks = 0; }
        foreach (int index in DialogueVirtualizationWindow.FillOrder(desired, s.Origin.y, s.Pitch, topPoint.y, bottomPoint.y))
        {
            if (!fill) break;
            int n = Array.BinarySearch(desired, index);
            Slot? slot = assigned[n];
            if (slot != null && slot.Index == index && !slot.Dirty) continue;
            if (_fillRows >= DialogueVirtualizationWindow.RowsPerFrame ||
                _fillTicks * 1000d / Stopwatch.Frequency >= DialogueVirtualizationWindow.MillisecondsPerFrame) break;
            long started = Stopwatch.GetTimestamp();
            Bind(n);
            _fillTicks += Stopwatch.GetTimestamp() - started;
            _fillRows++;
        }
        s.Pending = false;
        int sibling = 0;
        for (int n = 0; n < desired.Length; n++)
        {
            int index = desired[n]; Slot? slot = assigned[n];
            if (slot == null || slot.Index != index)
            {
                if (slot != null) slot.Row.gameObject.SetActive(false);
                s.Pending = true;
                continue;
            }
            if (slot.Dirty) s.Pending = true;
            slot.Row.gameObject.SetActive(true);
            slot.Row.transform.localPosition = new Vector3(s.Origin.x, s.Origin.y - index * s.Pitch, s.Origin.z);
            slot.Row.transform.SetSiblingIndex(sibling++);
            cache[index] = slot.Row;
        }
        s.Indices = desired;
        s.Scroll.InvalidateBounds(); s.Scroll.UpdateScrollbars(true);
        // A virtual row bypasses UIGrid.Reposition, which is normally the
        // native trigger that makes each row's nested NGUI panel rebuild its
        // draw call. Refresh only after positions, sibling order, and scroll
        // bounds are final; rebuilding before that can cache old geometry and
        // remain blank.
        if (presentationDirty)
        {
            var refreshedPanels = new HashSet<IntPtr>();
            foreach (Slot slot in s.Slots)
            {
                if (slot.Index < 0 || slot.Row.child == null) continue;
                UIPanel? panel = slot.Row.child.panel;
                if (panel != null && refreshedPanels.Add(panel.Pointer)) { panel.SetDirty(); panel.UpdateSelf(); }
            }
            s.Panel.SetDirty(); s.Panel.UpdateSelf();
        }
    }

    private static bool BeforeBounds(UIScrollView __instance, ref Bounds __result)
    {
        State? s = _active?._state;
        if (s == null || !Same(__instance, s.Scroll)) return true;
        // Include the entire logical list in scroll bounds without creating
        // thousands of spacer Widgets. This is a mathematical spacer.
        Bounds source = s.FirstBounds;
        Vector3 min = source.min; min.y -= Math.Max(0, s.Count - 1) * s.Pitch;
        Vector3 max = source.max;
        Bounds result = new(s.Scroll.transform.InverseTransformPoint(s.Grid.transform.TransformPoint(min)), Vector3.zero);
        for (int n = 0; n < 8; n++) result.Encapsulate(s.Scroll.transform.InverseTransformPoint(s.Grid.transform.TransformPoint(new Vector3((n & 1) == 0 ? min.x : max.x, (n & 2) == 0 ? min.y : max.y, (n & 4) == 0 ? min.z : max.z))));
        __result = result; __instance.mBounds = result; __instance.mCalculatedBounds = true; return false;
    }
    private static bool BeforeGrid(UIGrid __instance)
    {
        DialogueVirtualization? a = _active; State? s = a?._state;
        if (s == null || !Same(__instance, s.Grid) || a!._bypass) return true;
        try
        {
            if (!s.Busy) a.RefreshWindow(s, true);
            s.Grid.enabled = false;
            return false;
        }
        catch (Exception error) { a.Recover(error); return true; }
    }
    private static void AfterLateScroll(UIScrollView __instance)
    {
        DialogueVirtualization? a = _active; State? s = a?._state;
        if (s == null || !Same(__instance, s.Scroll) || s.Busy || !a!.MayRun) return;
        try
        {
            if (!a._tailAnchorReapplying && a._tailAnchorInspector != null && Same(a._tailAnchorInspector, s.Inspector) && a._tailAnchorUntilFrame >= Time.frameCount)
            {
                if (UICamera.isDragging)
                    a.ClearTailAnchor();
                else
                {
                    // Native LateUpdate/RestrictWithinBounds can rewrite the
                    // clip after Load. Reassert the mathematical bottom after
                    // that work for the short opening guard window.
                    a._tailAnchorReapplying = true;
                    try
                    {
                        s.Scroll.InvalidateBounds();
                        s.Scroll.UpdateScrollbars(true);
                        // Native ResetPosition uses false then true: false
                        // translates the content transform, true only refreshes
                        // scrollbars. Calling true alone moves the clip and
                        // leaves rows outside the camera (cameraVisible=0).
                        s.Scroll.SetDragAmount(0f, 1f, false);
                        s.Scroll.SetDragAmount(0f, 1f, true);
                        s.Scroll.DisableSpring();
                    }
                    finally { a._tailAnchorReapplying = false; }
                }
            }
            a.RefreshWindow(s, false);
        }
        catch (Exception error) { a.Recover(error); }
    }
    private static void AfterScrollInput(UIScrollView __instance)
    {
        DialogueVirtualization? a = _active; State? s = a?._state;
        if (s == null || !Same(__instance, s.Scroll) || a!._tailAnchorReapplying) return;
        // A SetDragAmount outside our own LateUpdate correction is a user or
        // native operation; it owns the viewport and ends the opening guard.
        a.ClearTailAnchor();
        if (!s.Busy && a.MayRun)
            try { a.RefreshWindow(s, false); } catch (Exception error) { a.Recover(error); }
    }
    private static bool BeforeOpeningCenter(CenterableUIScrollView __instance, Transform target, bool instant, bool restrain)
    {
        DialogueVirtualization? a = _active;
        if (a != null && a.SuppressDeferredTailCenter(__instance, target)) return false;
        // Only suppress the transient callback generated by native Load. User
        // selection and scrolling after entry retain AA's native CenterOn.
        return a == null || a._opening == null || !Same(a._opening.Inspector?.scriptListScroll, __instance) ||
            (a._opening.Inspector?.scriptNode?.scripts?.Count ?? 0) < 32;
    }
    private static bool BeforeOpeningCenterWithPanelCenter(CenterableUIScrollView __instance, Transform target, Vector3 panelCenter, bool instant)
    {
        var a = _active;
        if (a != null && a.SuppressDeferredTailCenter(__instance, target)) return false;
        return true;
    }
    private static void BeforeInsert(ScriptNodeInspector __instance, int index, out int __state)
    {
        DialogueVirtualization? a = _active; __state = a?._required ?? -1;
        if (a != null && a.MayRun) a._required = index;
    }
    private static void BeforeDelete(ScriptNodeInspector __instance, out int __state)
    {
        DialogueVirtualization? a = _active; __state = a?._required ?? -1;
        if (a != null && a.MayRun) a._required = (__instance.selectedScriptItem?.index ?? 0) - 1;
    }
    private static Exception? AfterMutation(Exception? __exception, int __state)
    {
        if (_active != null) { _active._required = __state; if (__exception != null) _active.Recover(__exception); }
        return __exception;
    }
    private static void BeforeRestore(ScriptNodeInspector __instance) => EnsureIndex(__instance, __instance.scriptNode?.inspectorInfo?.lastScriptIndex ?? -1);
    private static void BeforeRestoreInfo(ScriptNodeInspector __instance, ScriptNode.ScriptNodeInspectorInfo info) => EnsureIndex(__instance, info?.lastScriptIndex ?? -1);
    private static void EnsureIndex(ScriptNodeInspector inspector, int index)
    {
        DialogueVirtualization? a = _active; State? s = a?._state;
        if (s == null || !Same(s.Inspector, inspector)) return;
        try { a!.RefreshWindow(s, false, required: index); } catch (Exception e) { a!.Recover(e); }
    }
    private static void BeforeLoad(ScriptNodeInspector __instance, out OpenScope? __state)
    {
        DialogueVirtualization? a = _active; __state = null;
        if (a == null || !a.MayRun) return;
        __state = new OpenScope { Inspector = __instance, Parent = a._opening, Started = Stopwatch.GetTimestamp(), CreatedBefore = a._created };
        a._opening = __state;
    }
    private static Exception? AfterLoad(Exception? __exception, OpenScope? __state)
    {
        DialogueVirtualization? a = _active;
        if (a == null || __state == null) return __exception;
        a._opening = __state.Parent;
        if (__exception == null && __state.Tail >= 0) a.AnchorTail(__state.Inspector, __state.Tail);
        double elapsed = (Stopwatch.GetTimestamp() - __state.Started) * 1000d / Stopwatch.Frequency;
        double sync = __state.SyncTicks * 1000d / Stopwatch.Frequency;
        a._log($"dialogue-open totalMs={elapsed:F2} rowSyncMs={sync:F2} tail={__state.Tail} live={a.LiveRows} created={a._created - __state.CreatedBefore} pending={a.HasPendingRows} success={__exception == null}; native Load includes reset and selected-row inspector work.");
        if (__exception != null && a._state != null && Same(a._state.Inspector, __state.Inspector)) a.ReleasePresentation("failed node load");
        return __exception;
    }
    private static void BeforeUnload(out bool __state)
    {
        DialogueVirtualization? a = _active; __state = a != null && !a._shuttingDown;
        if (__state) a!._nativeDepth++;
    }

    private void AnchorTail(ScriptNodeInspector inspector, int tail)
    {
        if (tail < 0 || inspector?.scriptListScroll == null) return;
        try
        {
            _tailAnchorInspector = inspector;
            _tailAnchorTarget = IntPtr.Zero;
            if (inspector.scriptNodeListItemsCache != null && tail < inspector.scriptNodeListItemsCache.Count)
                _tailAnchorTarget = inspector.scriptNodeListItemsCache[tail]?.transform?.Pointer ?? IntPtr.Zero;
            _tailAnchorUntilFrame = Time.frameCount + 4;
            inspector.scriptListScroll.centeringChild = null!;
            inspector.scriptListScroll.InvalidateBounds();
            inspector.scriptListScroll.UpdateScrollbars(true);
            // On this NGUI host normalized Y=1 is the lower end of a vertical
            // TopLeft grid. Y=0 is the first dialogue row; using it here lets
            // native deferred CenterOn visibly spring from the beginning.
            _tailAnchorReapplying = true;
            try
            {
                inspector.scriptListScroll.SetDragAmount(0f, 1f, false);
                inspector.scriptListScroll.SetDragAmount(0f, 1f, true);
            }
            finally { _tailAnchorReapplying = false; }
            inspector.scriptListScroll.DisableSpring();
            _log($"dialogue tail-anchor index={tail} normalizedY=1; centerTargetCleared=True logical={inspector.scriptNode?.scripts?.Count ?? 0}");
        }
        catch (Exception error) { _log("dialogue tail-anchor deferred: " + error.Message); }
    }

    private bool SuppressDeferredTailCenter(CenterableUIScrollView scroll, Transform target)
    {
        if (_tailAnchorInspector == null || _tailAnchorUntilFrame < 0) return false;
        if (!Same(_tailAnchorInspector.scriptListScroll, scroll))
        {
            ClearTailAnchor();
            return false;
        }
        if (_tailAnchorTarget == IntPtr.Zero || target == null || target.Pointer != _tailAnchorTarget)
        {
            // A different target is an explicit user selection. Let native AA
            // center it and drop the one-shot opening guard.
            ClearTailAnchor();
            return false;
        }
        scroll.centeringChild = null!;
        scroll.DisableSpring();
        _log("dialogue tail-center suppressed: deferred native selection callback");
        ClearTailAnchor();
        return true;
    }

    private void ClearTailAnchor()
    {
        _tailAnchorInspector = null;
        _tailAnchorTarget = IntPtr.Zero;
        _tailAnchorUntilFrame = -1;
    }
    private static Exception? AfterUnload(ScriptNodeInspector __instance, Exception? __exception, bool __state)
    {
        DialogueVirtualization? a = _active;
        if (!__state || a == null) return __exception;
        try
        {
            // Audited Unload only maintains model dummy links, deselects its
            // selected row and deactivates the inspector. Keep those operations
            // native, then release the pool without ever filling missing rows.
            if (a._state != null && Same(a._state.Inspector, __instance)) a.ReleasePresentation("node unload");
        }
        finally { a._nativeDepth = Math.Max(0, a._nativeDepth - 1); }
        return __exception;
    }
    private static void BeforeNativeScope(MethodBase __originalMethod, out bool __state)
    {
        DialogueVirtualization? a = _active; __state = a != null;
        if (a == null) return;
        a._nativeDepth++;
        if (a._state != null) a.Materialize("native " + __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name);
    }
    private static Exception? AfterNativeScope(Exception? __exception, bool __state)
    {
        if (__state && _active != null) _active._nativeDepth = Math.Max(0, _active._nativeDepth - 1);
        return __exception;
    }
    private static void BeforeDrag(ScriptListItem __instance)
    {
        if (_active?._state != null && Same(_active._state.Inspector, __instance.inspector)) _active.Materialize("drag rearrangement");
    }

    private void ReleasePresentation(string reason)
    {
        State? s = _state;
        if (s == null) return;
        _state = null;
        _resumeInspector = null;
        s.Pending = false;
        // Used only after native lifecycle deselection, never for export,
        // live-disable or an unknown cache reader. Data/undo belongs to AA.
        foreach (Slot slot in s.Slots)
        {
            if (Same(s.Inspector.selectedScriptItem, slot.Row)) s.Inspector.selectedScriptItem = null!;
            if (Same(s.Scroll.centeringChild, slot.Row.transform)) s.Scroll.centeringChild = null!;
            slot.Row.gameObject.SetActive(false);
            slot.Row.transform.SetParent(null, false);
            NGUITools.Destroy(slot.Row.gameObject);
        }
        s.Inspector.scriptNodeListItemsCache.Clear();
        s.Grid.enabled = s.GridEnabled;
        s.Scroll.InvalidateBounds();
        if (Same(_tailAnchorInspector, s.Inspector)) ClearTailAnchor();
        Reason = reason;
        _log($"dialogue presentation released: {reason}; logical={s.Count} pooled={s.Slots.Count}; no dense rebuild.");
    }

    public void AbandonForShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _allowed = false;
        _state = null;
        _resumeInspector = null;
        _opening = null;
        ClearTailAnchor();
        // OnApplicationQuit means Unity owns destruction. Do not even touch
        // native UI references here: localization/services may already be gone.
        Reason = "application quit";
        _log("dialogue presentation abandoned for application quit; no row initialization or refresh.");
    }

    private void Materialize(string reason)
    {
        State? s = _state; if (s == null || _bypass) return;
        _bypass = true;
        try
        {
            // Retain all existing row identities (especially the dragged/selected
            // row), fill missing logical rows, then return native child ordering.
            var cache = s.Inspector.scriptNodeListItemsCache;
            int count = s.Node.scripts.Count;
            // Preserve the selected object before touching sparse slots. A
            // failed bind can happen after its cache entry was cleared; native
            // selection still points at that exact object and must survive the
            // recovery. Reinsert it at its logical index before filling gaps.
            ScriptListItem? selected = s.Inspector.selectedScriptItem;
            int selectedIndex = selected != null && Same(selected.scriptNode, s.Node) ? selected.index : -1;
            while (cache.Count < count) cache.Add(null!);
            while (cache.Count > count) cache.RemoveAt(cache.Count - 1);
            if (selected != null && selectedIndex >= 0 && selectedIndex < count)
            {
                for (int i = 0; i < cache.Count; i++)
                    if (i != selectedIndex && Same(cache[i], selected)) cache[i] = null!;
                cache[selectedIndex] = selected;
                selected.gameObject.SetActive(true);
                if (!Same(selected.transform.parent, s.Grid.transform)) selected.transform.SetParent(s.Grid.transform, false);
            }
            var retained = new HashSet<IntPtr>();
            for (int index = 0; index < count; index++)
            {
                ScriptListItem? row = cache[index];
                if (row == null)
                {
                    GameObject go = NGUITools.AddChild(s.Grid.transform, s.Prefab);
                    Utils.Util.AdaptWidgetToParent(go, s.Grid.gameObject);
                    row = go.GetComponent<ScriptListItem>();
                    row.Init(s.Node, index, s.Inspector); cache[index] = row;
                }
                else
                {
                    // A text-only Sync may have left this bound row dirty
                    // until its next budgeted frame. Dense consumers must see
                    // current text before authoring/save/history resumes.
                    foreach (Slot slot in s.Slots)
                        if (Same(slot.Row, row) && slot.Dirty) { row.Refresh(); slot.Dirty = false; break; }
                }
                retained.Add(row.Pointer); row.gameObject.SetActive(true); row.transform.SetSiblingIndex(index);
            }
            foreach (Slot slot in s.Slots) if (!retained.Contains(slot.Row.Pointer))
            {
                // Unity destruction is deferred. Detach before native grid
                // enumeration, which includes inactive children on this host.
                slot.Row.gameObject.SetActive(false);
                slot.Row.transform.SetParent(null, false);
                NGUITools.Destroy(slot.Row.gameObject);
            }
            _state = null;
            _resumeInspector = s.Inspector;
            s.Grid.enabled = s.GridEnabled;
            s.Grid.Reposition(); s.Scroll.InvalidateBounds(); s.Scroll.UpdateScrollbars(true);
            Reason = reason; _fallbacks++; _log("dialogue virtualization restored dense native list: " + reason);
        }
        finally { _bypass = false; }
    }
    private void Recover(Exception error)
    {
        _failed = true; _allowed = false;
        try { Materialize("recovery"); }
        catch (Exception recovery)
        {
            // A recovery exception must never leave a sparse cache exposed to
            // native code. Disable our hooks first, then ask AA's original
            // synchronous SyncScriptList to rebuild a dense cache. Its
            // keepSelected path restores the selected logical index.
            try
            {
                State? failedState = _state;
                _state = null;
                _resumeInspector = null;
                if (failedState?.Inspector != null)
                {
                    _bypass = true;
                    failedState.Inspector.SyncScriptList(false, true, false);
                }
            }
            catch (Exception denseRecovery) { _log("dialogue virtualization dense recovery failed: " + denseRecovery); }
            finally { _bypass = false; }
            _log("dialogue virtualization recovery failed: " + recovery);
        }
        _log("dialogue virtualization disabled for this session: " + error);
    }
    private static bool Same(Il2CppObjectBase? a, Il2CppObjectBase? b) => a != null && b != null && a.Pointer == b.Pointer;
    public void Dispose()
    {
        if (_disposed) return;
        Suspend(); _disposed = true;
        try { _harmony.UnpatchSelf(); } finally { if (_active == this) _active = null; }
    }
}
