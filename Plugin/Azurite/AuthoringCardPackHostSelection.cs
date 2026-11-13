using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Azurite.Core;
using HarmonyLib;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using Studio.Scripts.Window;
using UnityEngine;

namespace Azurite;

/// <summary>
/// Narrow AA adapter for Windows-style selection in the dialogue list.  The
/// model owns only stable row IDs; AA remains the owner of the active native
/// row, focus, drag handles, scrolling and undo history.
/// </summary>
internal sealed class AuthoringCardPackHostSelection : IDisposable
{
    private const string Owner = "halocue.azurite.authoring-card-pack-selection";
    private static AuthoringCardPackHostSelection? _active;

    private readonly Harmony _harmony = new(Owner);
    private readonly Action<string>? _log;
    private readonly int _editorThreadId;
    private readonly Func<bool> _authoringAllowed;
    private readonly BoxSelectionModel _model = new();
    private readonly List<ScriptListItem> _nativeRows = new();
    private readonly List<BoxSelectionRow> _rows = new();
    private readonly Dictionary<string, ScriptListItem> _rowsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Rect> _screenBounds = new(StringComparer.Ordinal);
    private bool _installed;
    private bool _disposed;
    private bool _allowed;
    private bool _enabled;
    private bool _gestureStarted;
    private bool _suppressNativeClick;
    private int _suppressNativeClickFrame = -1;
    private int _lastGestureEndFrame = -1;
    private int _nativeClickConsumedFrame = -1;
    private IntPtr _cachePointer;
    private IntPtr _inspectorPointer;
    private IntPtr _nodePointer;
    private string _nodeId = string.Empty;
    private int _rowCount = -1;
    private string _rowSignature = string.Empty;
    private Texture2D? _overlayTexture;

    internal AuthoringCardPackHostSelection(int editorThreadId, Func<bool> authoringAllowed, Action<string>? log = null)
    {
        if (editorThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(editorThreadId));
        _editorThreadId = editorThreadId;
        _authoringAllowed = authoringAllowed ?? throw new ArgumentNullException(nameof(authoringAllowed));
        _log = log;
    }

    internal bool IsInstalled => _installed && !_disposed;
    internal IReadOnlyList<string> SelectedRowIds => _model.SelectedRowIds;
    internal bool IsDragging => _model.IsDragging;
    // Host reads this just before capture/commit, so it must reconcile current
    // identity even when Update has not run since a native list replacement.
    internal long SelectionRevision
    {
        get
        {
            if (!OnEditorThread || _disposed || !IsInstalled) return -1;
            try { SynchronizeRows(); return _model.SelectionRevision; }
            catch { ResetForHostChange(); return -1; }
        }
    }
    private bool OnEditorThread => Environment.CurrentManagedThreadId == _editorThreadId;
    private bool Qualified()
    {
        if (!OnEditorThread || !IsInstalled) return false;
        try { return _authoringAllowed(); } catch { return false; }
    }
    private static bool NativeInputOwnsPointer()
    {
        var window = Singleton<WindowManager>.Instance;
        return !Application.isFocused || window == null || window.activeWindow != null ||
            (window.blocking != null && window.blocking.activeInHierarchy) ||
            UICamera.inputHasFocus || UIInput.current != null || UIInput.selection != null || !string.IsNullOrEmpty(UIInput.mLastIME);
    }
    private bool IsCurrentRow(ScriptListItem row) => row.inspector?.Pointer == _inspectorPointer &&
        row.scriptNode?.Pointer == _nodePointer && TryGetStableRowId(row, out string id) &&
        _rowsById.TryGetValue(id, out var mapped) && mapped.Pointer == row.Pointer && row.isActiveAndEnabled && row.gameObject.activeInHierarchy;

    internal bool TryGetSelectedRows(out ScriptNodeInspector inspector, out IReadOnlyList<ScriptListItem> rows)
    {
        inspector = null!;
        rows = Array.Empty<ScriptListItem>();
        if (!OnEditorThread || _disposed || !IsInstalled) return false;
        try
        {
            ScriptNodeInspector current = SynchronizeRows();
            var cache = current.scriptNodeListItemsCache;
            var selected = new HashSet<string>(_model.SelectedRowIds, StringComparer.Ordinal);
            var ordered = new List<ScriptListItem>();
            foreach (ScriptListItem row in _nativeRows)
            {
                if (!TryGetStableRowId(row, out string id) || !selected.Contains(id)) continue;
                if (row.index < 0 || row.index >= cache.Count || cache[row.index] == null || cache[row.index].Pointer != row.Pointer)
                    return false;
                ordered.Add(row);
            }
            if (ordered.Count != selected.Count) throw new InvalidOperationException("Selected row mapping is incomplete.");
            inspector = current;
            rows = ordered.ToArray();
            return true;
        }
        catch { ResetForHostChange(); return false; }
    }

    /// <summary>
    /// Host post-Apply/recovery callback. No write-eligibility check here: native
    /// success may schedule autosave. Exact live node/line identity is mandatory.
    /// It never edits rows, history or data, and always retires the old gesture.
    /// </summary>
    internal void InvalidateNativeResult(string nodeId, IReadOnlyList<string> lineIds)
    {
        if (!OnEditorThread || _disposed || !IsInstalled)
            throw new InvalidOperationException("Selection refresh requires its installed editor-thread owner.");
        ResetForHostChange();
        var current = SynchronizeRows();
        if (lineIds == null || nodeId != current.scriptNode.guid.ToString() || lineIds.Count != current.scriptNode.scripts.Count)
            throw new InvalidOperationException("Post-result node identity is stale.");
        for (int i = 0; i < lineIds.Count; i++)
            if (lineIds[i] != current.scriptNode.scripts[i].LineId)
                throw new InvalidOperationException("Post-result ordered line identities are stale.");
        _model.Reconcile(_rows);
        CancelGesture();
    }

    private ScriptNodeInspector SynchronizeRows(bool verifyAllRows = true)
    {
        var inspector = ScriptNodeInspector.instance;
        var cache = inspector?.scriptNodeListItemsCache;
        var node = inspector?.scriptNode;
        if (inspector == null || !inspector.isActiveAndEnabled || !inspector.gameObject.activeInHierarchy ||
            inspector.loading || inspector.unloading || inspector.rearrangeScheduled || node == null || node.Pointer == IntPtr.Zero ||
            node.scripts == null || cache == null || cache.Count != node.scripts.Count)
            throw new InvalidOperationException("The current dialogue list is not synchronized.");
        string nodeId = node.guid.ToString();
        if (!Guid.TryParse(nodeId, out var guid) || guid == Guid.Empty)
            throw new InvalidOperationException("The current native node has no identity.");
        if (_inspectorPointer != inspector.Pointer || _nodePointer != node.Pointer || _nodeId != nodeId ||
            _cachePointer != cache.Pointer || _rowCount != cache.Count)
        {
            ResetForHostChange();
            _inspectorPointer = inspector.Pointer; _nodePointer = node.Pointer; _nodeId = nodeId;
            _cachePointer = cache.Pointer; _rowCount = cache.Count;
            verifyAllRows = true;
        }
        if (verifyAllRows) BuildRows(cache); else RefreshSelectedBounds(cache);
        return inspector;
    }

    internal bool Install()
    {
        if (!OnEditorThread || _disposed || (_active != null && _active != this)) return false;
        try { if (!_authoringAllowed()) return false; } catch { return false; }
        if (_installed) return true;
        try
        {
            VerifyRuntimeTargets();
            _active = this;
            Patch(typeof(ScriptListItem), "OnClick", Type.EmptyTypes, nameof(BeforeRowClick), nameof(AfterRowClick));
            Patch(typeof(ScriptListItem), "OnDraggerDragStart", Type.EmptyTypes, nameof(BeforeNativeRowDrag), null);
            Patch(typeof(UIDragScrollView), "OnDrag", new[] { typeof(Vector2) }, nameof(BeforeDialogueScrollDrag), null);
            Patch(typeof(ScriptListItem), "Refresh", Type.EmptyTypes, null, nameof(AfterRowRefresh));
            Patch(typeof(ScriptListItem), "SetIndexAndSiblingIndex", new[] { typeof(int) }, null, nameof(AfterRowReindexed));
            Patch(typeof(ScriptNodeInspector), "SyncScriptList", new[] { typeof(bool), typeof(bool), typeof(bool) }, null, nameof(AfterSyncScriptList));
            Patch(typeof(ScriptNodeInspector), "Load", new[] { typeof(Node), typeof(bool) }, null, nameof(AfterInspectorReset));
            Patch(typeof(ScriptNodeInspector), "Unload", new[] { typeof(bool) }, null, nameof(AfterInspectorReset));
            _installed = true;
            _log?.Invoke("dialogue box selection hooks ready; native row click, drag, scroll and undo remain authoritative.");
            return true;
        }
        catch (Exception error)
        {
            try { _harmony.UnpatchSelf(); } catch { }
            if (_active == this) _active = null;
            _log?.Invoke("dialogue box selection unavailable: " + error.GetType().Name + ". Native dialogue selection remains unchanged.");
            return false;
        }
    }

    internal void Update(bool allowed)
    {
        if (!OnEditorThread) return;
        _enabled = allowed && Qualified();
        if (!_enabled)
        {
            if (_allowed || _inspectorPointer != IntPtr.Zero || _gestureStarted) ResetForHostChange();
            return;
        }

        try
        {
            bool pointerActive = Input.GetMouseButtonDown(0) || Input.GetMouseButton(0) || _gestureStarted;
            ScriptNodeInspector inspector = SynchronizeRows(pointerActive);
            _allowed = true;
            HandlePointer(inspector);
        }
        catch (Exception error)
        {
            ResetForHostChange();
            _log?.Invoke("dialogue box selection failed closed: " + error.GetType().Name + ".");
        }
    }

    internal void OnGUI()
    {
        if (!Qualified() || !_allowed || !_enabled || _screenBounds.Count == 0) return;
        bool grouped = false;
        try
        {
            if (!TryGetViewportRect(out Rect viewport) || viewport.width <= 0f || viewport.height <= 0f) return;
            GUI.BeginGroup(viewport);
            grouped = true;
            EnsureStyles();
            foreach (string id in _model.SelectedRowIds)
                if (_screenBounds.TryGetValue(id, out Rect selected) && TryIntersect(selected, viewport, out Rect clippedSelected))
                    GUI.DrawTexture(ToGroupRect(clippedSelected, viewport), _overlayTexture!, ScaleMode.StretchToFill, alphaBlend: true, imageAspect: 0f, color: new Color(0.18f, 0.48f, 1f, 0.18f), borderWidth: 0f, borderRadius: 0f);

            BoxSelectionRect? rectangle = _model.SelectionRectangle;
            if (rectangle.HasValue && TryIntersect(ToGuiRect(rectangle.Value), viewport, out Rect clippedSelection))
            {
                Rect groupedSelection = ToGroupRect(clippedSelection, viewport);
                GUI.DrawTexture(groupedSelection, _overlayTexture!, ScaleMode.StretchToFill, alphaBlend: true, imageAspect: 0f, color: new Color(0.18f, 0.48f, 1f, 0.12f), borderWidth: 0f, borderRadius: 0f);
                var border = new Color(0.12f, 0.38f, 0.92f, 0.9f);
                GUI.DrawTexture(new Rect(groupedSelection.xMin, groupedSelection.yMin, groupedSelection.width, 1f), _overlayTexture!, ScaleMode.StretchToFill, true, 0f, border, 0f, 0f);
                GUI.DrawTexture(new Rect(groupedSelection.xMin, groupedSelection.yMax - 1f, groupedSelection.width, 1f), _overlayTexture!, ScaleMode.StretchToFill, true, 0f, border, 0f, 0f);
                GUI.DrawTexture(new Rect(groupedSelection.xMin, groupedSelection.yMin, 1f, groupedSelection.height), _overlayTexture!, ScaleMode.StretchToFill, true, 0f, border, 0f, 0f);
                GUI.DrawTexture(new Rect(groupedSelection.xMax - 1f, groupedSelection.yMin, 1f, groupedSelection.height), _overlayTexture!, ScaleMode.StretchToFill, true, 0f, border, 0f, 0f);
            }
        }
        catch
        {
            // Overlay drawing is presentation-only; never affect native input.
        }
        finally
        {
            if (grouped) GUI.EndGroup();
        }
    }

    private void HandlePointer(ScriptNodeInspector inspector)
    {
        if (_lastGestureEndFrame == Time.frameCount && !_gestureStarted)
            return;
        if (NativeInputOwnsPointer() ||
            (UICamera.isDragging && (!_gestureStarted || !_model.IsDragging)))
        {
            CancelGesture();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) { CancelGesture(); return; }

        BoxSelectionPoint point = CurrentPoint();
        if (Input.GetMouseButtonDown(0))
        {
            BoxSelectionPointerTarget target = HitTarget(inspector, point);
            BoxSelectionModifiers modifiers = CurrentModifiers();
            _gestureStarted = _model.Begin(target, point, modifiers);
            _suppressNativeClick = false;
            _suppressNativeClickFrame = -1;
            _lastGestureEndFrame = -1;
        }

        if (_gestureStarted && Input.GetMouseButton(0))
            _model.Move(point, _rows);

        if (_gestureStarted && Input.GetMouseButtonUp(0))
        {
            bool dragging = _model.IsDragging;
            _model.End(_rows);
            _suppressNativeClick = dragging;
            _suppressNativeClickFrame = dragging ? Time.frameCount : -1;
            _lastGestureEndFrame = Time.frameCount;
            _gestureStarted = false;
        }

        if (_suppressNativeClick && Time.frameCount != _suppressNativeClickFrame)
        {
            _suppressNativeClick = false;
            _suppressNativeClickFrame = -1;
        }
    }

    private BoxSelectionPointerTarget HitTarget(ScriptNodeInspector inspector, BoxSelectionPoint point)
    {
        if (!TryGetViewportRect(out Rect viewport) || !viewport.Contains(new Vector2(point.X, point.Y)))
            return BoxSelectionPointerTarget.OutsideDialogueList;

        if (UICamera.inputHasFocus || UIInput.current != null || UIInput.selection != null)
            return BoxSelectionPointerTarget.InputField;

        GameObject? hovered = UICamera.hoveredObject;
        if (hovered != null)
        {
            if (hovered.GetComponentInParent<Dragger>() != null)
                return BoxSelectionPointerTarget.RowDragHandle;
            if (hovered.GetComponentInParent<UIInput>() != null)
                return BoxSelectionPointerTarget.InputField;
            if (hovered.GetComponentInParent<UIScrollBar>() != null)
                return BoxSelectionPointerTarget.Scrollbar;
            foreach (ScriptListItem row in _nativeRows)
            {
                if (row?.insertBtn != null && IsInside(row.insertBtn, point))
                    return BoxSelectionPointerTarget.RowDragHandle;
            }
        }

        return BoxSelectionPointerTarget.DialogueList;
    }

    private void BuildRows(Il2CppSystem.Collections.Generic.List<ScriptListItem> cache)
    {
        _nativeRows.Clear();
        _rows.Clear();
        _rowsById.Clear();
        _screenBounds.Clear();
        var identityRows = new List<BoxSelectionRow>();
        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        var signature = new System.Text.StringBuilder();
        Camera? camera = null;
        for (int i = 0; i < cache.Count; i++)
        {
            ScriptListItem row = cache[i];
            if (row == null || row.Pointer == IntPtr.Zero || row.index != i || row.inspector?.Pointer != _inspectorPointer ||
                row.scriptNode?.Pointer != _nodePointer || !TryGetStableRowId(row, out string id))
                throw new InvalidOperationException("Native row cache identity is stale.");
            string lineId = row.scriptNode.scripts[i].LineId;
            if (string.IsNullOrWhiteSpace(lineId) || !lineIds.Add(lineId) || !_rowsById.TryAdd(id, row))
                throw new InvalidOperationException("Native line identity is missing or duplicated.");
            _nativeRows.Add(row);
            bool valid = row.isActiveAndEnabled && row.gameObject.activeInHierarchy;
            identityRows.Add(new BoxSelectionRow(id, default, valid));
            signature.Append(id.Length).Append(':').Append(id).Append('/').Append(row.Pointer.ToString("X16"))
                .Append('/').Append(lineId.Length).Append(':').Append(lineId).Append(valid ? '1' : '0');
            if (!valid) continue;
            if (row.child == null || !TryGetScreenRect(row.child, ref camera, out Rect bounds)) continue;
            _rows.Add(new BoxSelectionRow(id, new BoxSelectionRect(bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax), valid));
            _screenBounds[id] = bounds;
        }
        string freshSignature = signature.ToString();
        if (!string.Equals(_rowSignature, freshSignature, StringComparison.Ordinal))
        {
            // Identity changes invalidate previews even when membership was empty.
            // The pointer key is only a UI cache key, never a native line ID.
            _rowSignature = freshSignature;
            _model.Refresh(identityRows);
            CancelGesture();
        }
    }

    private static bool TryGetStableRowId(ScriptListItem row, out string id)
    {
        id = string.Empty;
        try
        {
            if (row == null || row.scriptNode == null || row.index < 0 || row.scriptNode.scripts == null || row.index >= row.scriptNode.scripts.Count)
                return false;
            var script = row.scriptNode.scripts[row.index];
            if (script == null || script.Pointer == IntPtr.Zero || row.scriptNode.Pointer == IntPtr.Zero)
                return false;
            id = $"node:{row.scriptNode.Pointer.ToString("X16")}/script:{script.Pointer.ToString("X16")}";
            return true;
        }
        catch { return false; }
    }

    private void RefreshSelectedBounds(Il2CppSystem.Collections.Generic.List<ScriptListItem> cache)
    {
        string[] selected = _model.SelectedRowIds.ToArray();
        if (selected.Length == 0)
        {
            _screenBounds.Clear();
            return;
        }
        Camera? camera = null;
        foreach (string id in selected)
        {
            if (!_rowsById.TryGetValue(id, out ScriptListItem? row) || row == null || row.index < 0 || row.index >= cache.Count ||
                cache[row.index]?.Pointer != row.Pointer || row.inspector?.Pointer != _inspectorPointer || row.scriptNode?.Pointer != _nodePointer ||
                !TryGetStableRowId(row, out string currentId) || currentId != id || !row.isActiveAndEnabled || !row.gameObject.activeInHierarchy)
            {
                _model.Reconcile(_rows.Where(item => item.RowId != id).ToArray());
                CancelGesture();
                _rowsById.Remove(id);
                _screenBounds.Remove(id);
                continue;
            }
            if (row.child != null && TryGetScreenRect(row.child, ref camera, out Rect bounds))
                _screenBounds[id] = bounds;
            else
                _screenBounds.Remove(id);
        }
    }

    private static BoxSelectionModifiers CurrentModifiers()
    {
        BoxSelectionModifiers modifiers = BoxSelectionModifiers.None;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) modifiers |= BoxSelectionModifiers.Control;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) modifiers |= BoxSelectionModifiers.Shift;
        return modifiers;
    }

    private static BoxSelectionPoint CurrentPoint() => new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

    private static bool BeforeRowClick(ScriptListItem __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active._allowed || !active.Qualified() || __instance == null || __instance.inspector == null || __instance.inspector.Pointer != active._inspectorPointer) return true;
        if (NativeInputOwnsPointer() || (UICamera.isDragging && !active._model.IsDragging)) return true;
        try
        {
            ScriptNodeInspector inspector = active.SynchronizeRows();
            if (!active.IsCurrentRow(__instance)) return true;
            BoxSelectionPoint point = CurrentPoint();
            if (active.HitTarget(inspector, point) != BoxSelectionPointerTarget.DialogueList) return true;
            BoxSelectionModifiers modifiers = CurrentModifiers();
            bool modified = modifiers != BoxSelectionModifiers.None;
            if (active._model.IsDragging || (active._suppressNativeClick && active._suppressNativeClickFrame == Time.frameCount))
            {
                if (active._gestureStarted)
                {
                    active._model.End(active._rows);
                    active._gestureStarted = false;
                }
                active._suppressNativeClick = false;
                active._suppressNativeClickFrame = -1;
                active._lastGestureEndFrame = Time.frameCount;
                active._nativeClickConsumedFrame = Time.frameCount;
                return false;
            }
            if (modified)
            {
                if (active._lastGestureEndFrame != Time.frameCount)
                {
                    if (active._gestureStarted)
                    {
                        active._model.End(active._rows);
                        active._gestureStarted = false;
                    }
                    else
                    {
                        active._model.Begin(BoxSelectionPointerTarget.DialogueList, point, modifiers);
                        active._model.End(active._rows);
                    }
                    active._lastGestureEndFrame = Time.frameCount;
                }
                active.ActivateNativeRow(__instance);
                active._nativeClickConsumedFrame = Time.frameCount;
                return false;
            }
        }
        catch { active.ResetForHostChange(); }
        return true;
    }

    private static void AfterRowClick(ScriptListItem __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active._allowed || !active.Qualified() || __instance == null || __instance.inspector == null || __instance.inspector.Pointer != active._inspectorPointer || CurrentModifiers() != BoxSelectionModifiers.None) return;
        if (NativeInputOwnsPointer()) return;
        if (active._lastGestureEndFrame == Time.frameCount || active._nativeClickConsumedFrame == Time.frameCount)
        {
            active._nativeClickConsumedFrame = -1;
            return;
        }
        try
        {
            ScriptNodeInspector inspector = active.SynchronizeRows();
            if (!active.IsCurrentRow(__instance)) return;
            BoxSelectionPoint point = CurrentPoint();
            if (active.HitTarget(inspector, point) != BoxSelectionPointerTarget.DialogueList) return;
            active._model.Begin(BoxSelectionPointerTarget.DialogueList, point, BoxSelectionModifiers.None);
            active._model.End(active._rows);
            active._gestureStarted = false;
            active._lastGestureEndFrame = Time.frameCount;
        }
        catch { active.ResetForHostChange(); }
    }

    private static void BeforeNativeRowDrag(ScriptListItem __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active.OnEditorThread || __instance == null || __instance.inspector == null || __instance.inspector.Pointer != active._inspectorPointer) return;
        active._model.Clear();
        active._gestureStarted = false;
        active._suppressNativeClick = false;
        active._suppressNativeClickFrame = -1;
    }

    private static bool BeforeDialogueScrollDrag(UIDragScrollView __instance, Vector2 delta)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active.Qualified() || !active._allowed || !active._gestureStarted || __instance == null || !Input.GetMouseButton(0)) return true;
        try
        {
            ScriptNodeInspector inspector = active.SynchronizeRows();
            CenterableUIScrollView? target = inspector.scriptListScroll;
            if (target == null || ((__instance.scrollView == null || __instance.scrollView.Pointer != target.Pointer) && (__instance.draggablePanel == null || __instance.draggablePanel.Pointer != target.Pointer)))
                return true;
            if (NativeInputOwnsPointer()) { active.CancelGesture(); return true; }
            active._model.Move(CurrentPoint(), active._rows);
            return !active._model.IsDragging;
        }
        catch
        {
            active.ResetForHostChange();
            return true;
        }
    }

    private void ActivateNativeRow(ScriptListItem row)
    {
        row.Select();
    }

    private static void AfterRowRefresh(ScriptListItem __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active.OnEditorThread || __instance?.inspector == null || __instance.inspector.Pointer != active._inspectorPointer) return;
        active.ResetForHostChange();
    }

    private static void AfterRowReindexed(ScriptListItem __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active.OnEditorThread || __instance?.inspector == null || __instance.inspector.Pointer != active._inspectorPointer) return;
        active.ResetForHostChange();
    }

    private static void AfterSyncScriptList(ScriptNodeInspector __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active.OnEditorThread || __instance == null) return;
        if (active._inspectorPointer == __instance.Pointer || ScriptNodeInspector.instance?.Pointer == __instance.Pointer)
            active.ResetForHostChange();
    }

    private static void AfterInspectorReset(ScriptNodeInspector __instance)
    {
        AuthoringCardPackHostSelection? active = _active;
        if (active == null || !active.OnEditorThread || __instance == null) return;
        if (active._inspectorPointer == __instance.Pointer || ScriptNodeInspector.instance?.Pointer == __instance.Pointer)
            active.ResetForHostChange();
    }

    private void Patch(Type type, string name, Type[] parameters, string? prefix, string? postfix)
    {
        MethodInfo method = RequireRuntimeTarget(type, name, parameters);
        _harmony.Patch(method,
            prefix == null ? null : new HarmonyMethod(typeof(AuthoringCardPackHostSelection), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(AuthoringCardPackHostSelection), postfix));
        Patches? patches = Harmony.GetPatchInfo(method);
        if (patches == null || !patches.Owners.Contains(Owner))
            throw new InvalidOperationException("Dialogue box-selection hook was not registered: " + type.FullName + "." + name);
    }

    private static void VerifyRuntimeTargets()
    {
        RequireRuntimeTarget(typeof(ScriptListItem), "OnClick", Type.EmptyTypes);
        RequireRuntimeTarget(typeof(ScriptListItem), "OnDraggerDragStart", Type.EmptyTypes);
        RequireRuntimeTarget(typeof(ScriptListItem), "Refresh", Type.EmptyTypes);
        RequireRuntimeTarget(typeof(ScriptListItem), "SetIndexAndSiblingIndex", new[] { typeof(int) });
        RequireRuntimeTarget(typeof(UIDragScrollView), "OnDrag", new[] { typeof(Vector2) });
        RequireRuntimeTarget(typeof(ScriptNodeInspector), "SyncScriptList", new[] { typeof(bool), typeof(bool), typeof(bool) });
        RequireRuntimeTarget(typeof(ScriptNodeInspector), "Load", new[] { typeof(Node), typeof(bool) });
        RequireRuntimeTarget(typeof(ScriptNodeInspector), "Unload", new[] { typeof(bool) });
    }

    private static MethodInfo RequireRuntimeTarget(Type type, string name, Type[] parameters)
    {
        MethodInfo? target = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null, types: parameters, modifiers: null);
        if (target == null || target.IsStatic || target.IsGenericMethod || target.ReturnType != typeof(void))
            throw new MissingMethodException(type.FullName, name + "(" + string.Join(",", parameters.Select(parameter => parameter.FullName)) + ")");
        return target!;
    }

    private void ResetForHostChange()
    {
        _allowed = false;
        CancelGesture();
        _model.Refresh();
        _nativeRows.Clear();
        _rows.Clear();
        _rowsById.Clear();
        _screenBounds.Clear();
        _cachePointer = IntPtr.Zero;
        _inspectorPointer = IntPtr.Zero;
        _nodePointer = IntPtr.Zero;
        _nodeId = string.Empty;
        _rowCount = -1;
        _rowSignature = string.Empty;
    }

    private void CancelGesture()
    {
        _model.Cancel();
        _gestureStarted = false;
        _suppressNativeClick = false;
        _suppressNativeClickFrame = -1;
        _lastGestureEndFrame = -1;
        _nativeClickConsumedFrame = -1;
    }

    private static bool TryGetScreenRect(UIWidget widget, ref Camera? camera, out Rect rect)
    {
        rect = default;
        try
        {
            camera ??= UICamera.FindCameraForLayer(widget.gameObject.layer)?.cachedCamera ?? Camera.main;
            if (camera == null || widget.worldCorners == null || widget.worldCorners.Length != 4) return false;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector3 screen = camera.WorldToScreenPoint(widget.worldCorners[i]);
                if (!float.IsFinite(screen.x) || !float.IsFinite(screen.y) || screen.z < 0f) return false;
                float x = screen.x;
                float y = Screen.height - screen.y;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            rect = Rect.MinMaxRect(left, top, right, bottom);
            return rect.width > 0f && rect.height > 0f;
        }
        catch { return false; }
    }

    private bool TryGetViewportRect(out Rect rect)
    {
        rect = default;
        try
        {
            ScriptNodeInspector? inspector = ScriptNodeInspector.instance;
            UIPanel? panel = inspector?.scriptListScroll?.panel;
            if (panel == null) return false;
            Camera? camera = UICamera.FindCameraForLayer(panel.gameObject.layer)?.cachedCamera ?? Camera.main;
            if (camera == null) return false;
            Vector4 clip = panel.finalClipRegion;
            if (!float.IsFinite(clip.x) || !float.IsFinite(clip.y) || !float.IsFinite(clip.z) || !float.IsFinite(clip.w) || clip.z <= 0f || clip.w <= 0f)
                return false;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = new((i & 1) == 0 ? clip.x - clip.z * 0.5f : clip.x + clip.z * 0.5f,
                    (i & 2) == 0 ? clip.y - clip.w * 0.5f : clip.y + clip.w * 0.5f, 0f);
                Vector3 screen = camera.WorldToScreenPoint(panel.transform.TransformPoint(local));
                if (!float.IsFinite(screen.x) || !float.IsFinite(screen.y) || screen.z < 0f) return false;
                float x = screen.x;
                float y = Screen.height - screen.y;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            rect = Rect.MinMaxRect(left, top, right, bottom);
            return rect.width > 0f && rect.height > 0f;
        }
        catch { return false; }
    }

    private static bool IsInside(GameObject obj, BoxSelectionPoint point)
    {
        try
        {
            UIWidget? widget = obj.GetComponent<UIWidget>();
            if (widget == null) widget = obj.GetComponentInChildren<UIWidget>();
            if (widget == null) return false;
            Camera? camera = null;
            return TryGetScreenRect(widget, ref camera, out Rect rect) && rect.Contains(new Vector2(point.X, point.Y));
        }
        catch { return false; }
    }

    private void EnsureStyles()
    {
        if (_overlayTexture == null)
        {
            _overlayTexture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            _overlayTexture.SetPixel(0, 0, Color.white);
            _overlayTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        }
    }

    private static Rect ToGuiRect(BoxSelectionRect rectangle) => Rect.MinMaxRect(rectangle.MinX, rectangle.MinY, rectangle.MaxX, rectangle.MaxY);

    private static Rect ToGroupRect(Rect rect, Rect viewport) => new(rect.x - viewport.x, rect.y - viewport.y, rect.width, rect.height);

    private static bool TryIntersect(Rect left, Rect right, out Rect result)
    {
        float xMin = Math.Max(left.xMin, right.xMin);
        float yMin = Math.Max(left.yMin, right.yMin);
        float xMax = Math.Min(left.xMax, right.xMax);
        float yMax = Math.Min(left.yMax, right.yMax);
        result = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return xMax > xMin && yMax > yMin;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!OnEditorThread) throw new InvalidOperationException("Selection hooks must be disposed on their editor thread.");
        _disposed = true;
        ResetForHostChange();
        if (_active == this) _active = null;
        if (_installed) { try { _harmony.UnpatchSelf(); } catch { } }
        _installed = false;
        if (_overlayTexture != null) UnityEngine.Object.Destroy(_overlayTexture);
        _overlayTexture = null;
    }
}
