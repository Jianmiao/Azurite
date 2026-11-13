using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Azurite.Core;
using AzureArchive.Automation;
using Newtonsoft.Json.Linq;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;
using UnityEngine;

namespace Azurite;

/// <summary>
/// Unregistered current-host boundary. The composition root must supply its verified
/// editor-thread ID, live export/compatibility guard, selection revision, and a real
/// native-inheritance/catalog projection. No default or synthetic projection exists.
/// </summary>
internal sealed class AuthoringCardPackHost : ICardPackV2Host
{
    private sealed record HistoryItem(IntPtr Pointer, IntPtr Previous, IntPtr Next);
    private sealed record State(CardPackContextIdentity Identity, long SelectionRevision,
        IntPtr Session, IntPtr Studio, string Fingerprint, string FileVersion, string? SavedFingerprint, bool Dirty,
        int SaveDelta, int CompileDelta, bool LinkNext, HistoryItem[] History, HistoryItem[] Future, string[] Lines);
    private sealed record Captured(State State, string? Locale, string Content);
    private sealed class Uncertain
    {
        internal readonly State Before;
        internal readonly CardPackNativePlan Plan;
        internal bool NativeReturnedReceipt;
        internal State? ConfirmedAfter;
        internal string Diagnostic = "Native Apply entered; outcome not yet verified.";
        internal Uncertain(State before, CardPackNativePlan plan) { Before = before; Plan = plan; }
    }
    // Session ownership outlives any individual host/workflow/panel instance.
    // Entries are removed only by a verified success or explicit verified recovery.
    private static readonly Dictionary<string, Uncertain> Uncertainty = new(StringComparer.Ordinal);
    private static int _nativeBoundaryBusy;
    private readonly int _editorThreadId;
    private readonly Func<bool> _authoringAllowed;
    private readonly Func<long> _selectionRevision;
    private readonly Func<CardPackContextIdentity, string?, CardPackCompilationContext> _captureContent;
    private readonly Action<string, IReadOnlyList<string>> _invalidateSelection;
    private Captured? _captured;

    internal AuthoringCardPackHost(int editorThreadId, Func<bool> authoringAllowed,
        Func<long> selectionRevision,
        Func<CardPackContextIdentity, string?, CardPackCompilationContext> captureContent,
        Action<string, IReadOnlyList<string>> invalidateSelection)
    {
        if (editorThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(editorThreadId));
        _editorThreadId = editorThreadId;
        _authoringAllowed = authoringAllowed ?? throw new ArgumentNullException(nameof(authoringAllowed));
        _selectionRevision = selectionRevision ?? throw new ArgumentNullException(nameof(selectionRevision));
        _captureContent = captureContent ?? throw new ArgumentNullException(nameof(captureContent));
        _invalidateSelection = invalidateSelection ?? throw new ArgumentNullException(nameof(invalidateSelection));
    }

    public CardPackV2Snapshot Capture(string? selectedLocale)
    {
        EnterBoundary();
        try
        {
            _captured = null;
            var state = ReadState(true, out _);
            Require(!Uncertainty.ContainsKey(state.Identity.EditSession), "RECOVERY_REQUIRED: this native session has an unresolved Apply.");
            var content = CaptureContent(state.Identity, selectedLocale);
            var current = ReadState(false, out _);
            Require(SameState(state, current), "Native state changed while capturing card content.");
            _captured = new(state, selectedLocale, JsonSerializer.Serialize(content));
            return new(content, state.SelectionRevision);
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult Commit(CardPackNativePlan plan, long selectionRevision)
    {
        bool entered = false, applyEntered = false, refreshAttempted = false;
        Uncertain? pending = null;
        try
        {
            EnterBoundary(); entered = true;
            var captured = _captured; _captured = null;
            Require(captured != null, "A fresh host Capture is required for each commit attempt.");
            var before = ReadState(true, out var session);
            Require(!Uncertainty.ContainsKey(before.Identity.EditSession), "RECOVERY_REQUIRED: previous Apply is unresolved.");
            Require(plan != null && plan.Identity == before.Identity && SameState(captured!.State, before) && before.SelectionRevision == selectionRevision,
                "Native identity, selection, project or history changed before commit.");
            var content = CaptureContent(before.Identity, captured!.Locale);
            Require(string.Equals(captured.Content, JsonSerializer.Serialize(content), StringComparison.Ordinal), "Native inherited stage or catalog capabilities changed.");
            var operations = PrepareOperations(plan!);
            Require(SameState(before, ReadState(false, out session)), "Native state changed during final content/resource validation.");
            // Current.Apply does NOT perform Check internally. Keep both guards true.
            session.Check(plan!.Identity.EditSession, plan.Identity.Revision, true, true);
            var checkedState = ReadState(false, out var checkedSession);
            Require(SameState(before, checkedState), "Native owner or selection changed inside Check.");
            pending = new(before, plan);
            Uncertainty.Add(before.Identity.EditSession, pending);
            applyEntered = true;
            var receipt = checkedSession.Apply(operations, plan.Identity.CatalogVersion);
            pending.NativeReturnedReceipt = receipt != null;
            var afterNative = ReadState(true, out _, requireWriteEligibility: false);
            Require(receipt != null, "Native Apply returned no receipt.");
            Require(IsSingleApplied(before, afterNative, plan), "Native result/model/history do not prove one complete snapshot commit.");
            pending.ConfirmedAfter = afterNative;
            refreshAttempted = true;
            _invalidateSelection(afterNative.Identity.NodeId, Array.AsReadOnly(afterNative.Lines));
            Require(SameState(afterNative, ReadState(false, out _, requireWriteEligibility: false), compareSelectionRevision: false), "Native state changed while rebuilding selection caches.");
            Uncertainty.Remove(before.Identity.EditSession);
            return new(CardPackV2CommitDisposition.Applied, "原生提交完成，已核对一个快照历史单元及重新取得的行身份。");
        }
        catch (Exception error)
        {
            if (pending != null) pending.Diagnostic = error.GetType().Name + ": " + error.Message;
            return new(applyEntered ? CardPackV2CommitDisposition.OutcomeUnknown : CardPackV2CommitDisposition.RejectedBeforeMutation,
                error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            if (entered)
            {
                if (applyEntered && !refreshAttempted)
                {
                    // Observe/rebind after native failure too; never retry Apply or clear
                    // uncertainty merely because the next capture happens to succeed.
                    try { var observed = RefreshSelectionState(); if (pending != null) pending.Diagnostic += " | observed revision=" + observed.Identity.Revision; }
                    catch (Exception error) { if (pending != null) pending.Diagnostic += " | refresh failed: " + error.Message; }
                }
                ExitBoundary();
            }
        }
    }

    public CardPackV2RecoveryResult Recover()
    {
        bool entered = false;
        try
        {
            EnterBoundary(); entered = true; _captured = null;
            var current = ReadState(true, out var session);
            if (!Uncertainty.TryGetValue(current.Identity.EditSession, out var fault))
                return new(true, "当前原生会话没有待确认提交；需要重新预览。");
            session.Check(current.Identity.EditSession, current.Identity.Revision, true, true);
            Require(SameState(current, ReadState(false, out _)), "Native state changed during recovery Check.");
            current = RefreshSelectionState();
            bool restored = IsRestored(fault.Before, current);
            // A snapshot history entry can precede a later native failure. Without
            // a completed Apply return we cannot prove its remaining bookkeeping.
            bool applied = !restored && fault.NativeReturnedReceipt && fault.ConfirmedAfter != null &&
                SameState(fault.ConfirmedAfter, current, compareSelectionRevision: false) && IsSingleApplied(fault.Before, current, fault.Plan);
            if (!restored && !applied) return new(false, "无法证明原状态已恢复或完整提交的模型/历史一致；继续保留锁定。" + fault.Diagnostic);
            Uncertainty.Remove(current.Identity.EditSession);
            return new(true, restored ? "已核对原模型指纹、完整历史与脏状态未变，并重建选择缓存。" : "已核对原生前后快照及单次历史，确认此前已提交；不会重试。");
        }
        catch (Exception error) { return new(false, error.GetType().Name + ": " + error.Message); }
        finally { if (entered) ExitBoundary(); }
    }

    private void EnterBoundary()
    {
        Require(Environment.CurrentManagedThreadId == _editorThreadId, "Must run on the qualified Unity editor thread.");
        Require(Interlocked.CompareExchange(ref _nativeBoundaryBusy, 1, 0) == 0, "Native card-pack boundary is already active.");
    }
    private static void ExitBoundary() => Volatile.Write(ref _nativeBoundaryBusy, 0);
    private static void Require([DoesNotReturnIf(false)] bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private CardPackCompilationContext CaptureContent(CardPackContextIdentity identity, string? locale)
    {
        // This required binding remains separate until exact Resolve capabilities and
        // native inheritance projection have evidence. Never set AllSupported here.
        var context = _captureContent(identity, locale);
        Require(context != null && context.Identity == identity && context.SelectedLocale == locale && context.StageIsNativeInherited,
            "A verified native inherited-stage/catalog projection for this exact context is required.");
        return context!;
    }

    private State ReadState(bool refresh, out AuthoringEditorSession session, bool requireWriteEligibility = true)
    {
        // Eligibility must hold before Check/Apply, but a successfully completed
        // edit may itself schedule auto-save or close the mutation gate. Read-only
        // post-observation still verifies owner, real busy flags and snapshot state.
        if (requireWriteEligibility)
        {
            Require(_authoringAllowed(), "Authoring compatibility/export/activity qualification is closed.");
            Require(Application.isFocused && !UICamera.inputHasFocus && !UICamera.isDragging && UIInput.current == null && UIInput.selection == null && string.IsNullOrEmpty(UIInput.mLastIME), "Input or IME owns the editor.");
        }
        var studio = StudioCommon.instance;
        var inspector = ScriptNodeInspector.instance;
        session = AuthoringEditorSession.Current!;
        Require(studio != null && studio.isActiveAndEnabled && studio.gameObject.activeInHierarchy && studio.entryNode != null && string.IsNullOrEmpty(studio.sessionInitializationError), "No qualified active Studio.");
        Require(session != null && !session.disposed && session.studio != null && session.studio.Pointer == studio!.Pointer && !session.IsApplying && !session.SaveInProgress,
            "Current is missing, busy, disposed or owned by another Studio; no session is manufactured.");
        Require(!OperationManager.Working && (!requireWriteEligibility || !studio!.autoSavePending && !studio.autoCompilePending), "Save, compile or history operation is in progress.");
        Require(inspector != null && inspector.isActiveAndEnabled && inspector.gameObject.activeInHierarchy && !inspector.loading && !inspector.unloading && !inspector.rearrangeScheduled && inspector.scriptNode != null, "Inspector is not ready.");
        if (refresh) session!.Refresh(true);
        Require(AuthoringEditorSession.Current?.Pointer == session!.Pointer && StudioCommon.instance?.Pointer == studio.Pointer && session.studio?.Pointer == studio.Pointer, "Current/Studio changed while refreshing.");
        var project = session.Capture();
        Require(project != null && session.Snapshot != null && !string.IsNullOrWhiteSpace(session.ProjectId) && project.ProjectId == session.ProjectId && session.Snapshot.ProjectId == session.ProjectId, "Native project identity is not synchronized.");
        string fingerprint = AuthoringEditorSession.Fingerprint(project!);
        Require(!string.IsNullOrWhiteSpace(fingerprint) && fingerprint == AuthoringEditorSession.Fingerprint(session.Snapshot), "Native live model and session snapshot disagree.");
        var node = inspector!.scriptNode;
        string nodeId = node.guid.ToString();
        var matching = new List<ScriptNodeData>();
        foreach (var candidate in project!.nodes)
            if (candidate.Guid.ToString() == nodeId)
            {
                var typed = candidate.TryCast<ScriptNodeData>();
                Require(typed != null, "Selected node snapshot is not dialogue data."); matching.Add(typed!);
            }
        Require(matching.Count == 1 && node.scripts != null && matching[0].Scripts != null && node.scripts.Count == matching[0].Scripts.Count, "Selected node is absent, duplicated or out of sync.");
        var lines = new string[node.scripts.Count]; var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = node.scripts[i].LineId;
            Require(!string.IsNullOrWhiteSpace(lines[i]) && seen.Add(lines[i]) && lines[i] == matching[0].Scripts[i].LineId, "Live/snapshot line identity mapping is invalid.");
        }
        var row = inspector.selectedScriptItem;
        Require(row != null && row.selected && row.isActiveAndEnabled && row.gameObject.activeInHierarchy && row.inspector?.Pointer == inspector.Pointer && row.scriptNode?.Pointer == node.Pointer && row.index >= 0 && row.index < lines.Length,
            "An explicit currently selected anchor row is required.");
        var cache = inspector.scriptNodeListItemsCache;
        Require(cache != null && row!.index < cache.Count && cache[row.index]?.Pointer == row.Pointer, "Selected row cache is stale.");
        long revision = _selectionRevision();
        Require(revision >= 0, "Selection revision is unavailable.");
        string catalog = AuthoringResourceCatalog.CatalogVersion;
        Require(!string.IsNullOrWhiteSpace(session.EditSession) && !string.IsNullOrWhiteSpace(session.Revision) && !string.IsNullOrWhiteSpace(catalog), "Opaque native session/revision/catalog identities are missing.");
        int slot = inspector.selectedSlotNum;
        var identity = new CardPackContextIdentity(session.ProjectId, session.EditSession, session.Revision, catalog, nodeId, lines[row!.index], slot is >= 1 and <= 5 ? slot : null);
        return new(identity, revision, session.Pointer, studio.Pointer, fingerprint, session.FileVersion, session.savedFingerprint, session.Dirty,
            OperationManager.saveDelta, OperationManager.compileDelta, OperationManager.needToLinkWithPrev,
            ReadStack(OperationManager.history), ReadStack(OperationManager.future), lines);
    }

    private State RefreshSelectionState()
    {
        var state = ReadState(true, out _, requireWriteEligibility: false);
        _invalidateSelection(state.Identity.NodeId, Array.AsReadOnly(state.Lines));
        var fresh = ReadState(false, out _, requireWriteEligibility: false);
        Require(SameState(state, fresh, compareSelectionRevision: false), "Native state changed while rebuilding selection caches.");
        return fresh;
    }

    private static HistoryItem[] ReadStack(Il2CppSystem.Collections.Generic.Stack<Operation> stack)
    {
        Require(stack != null, "Native history stack is unavailable.");
        int count = stack!.Count;
        var result = new List<HistoryItem>(count);
        // This host's IL2CPP Stack wrapper has GetEnumerator, but no ToArray.
        foreach (var operation in stack)
        {
            Require(operation != null, "Null native history entry.");
            result.Add(new(operation.Pointer, operation.linkedPrev?.Pointer ?? IntPtr.Zero, operation.linkedNext?.Pointer ?? IntPtr.Zero));
        }
        Require(count == stack.Count && result.Count == count, "Native history changed during inspection.");
        return result.ToArray();
    }

    private static bool SameOwner(State a, State b) => a.Session == b.Session && a.Studio == b.Studio &&
        a.Identity.ProjectId == b.Identity.ProjectId && a.Identity.EditSession == b.Identity.EditSession &&
        a.Identity.CatalogVersion == b.Identity.CatalogVersion && a.Identity.NodeId == b.Identity.NodeId && a.FileVersion == b.FileVersion && a.SavedFingerprint == b.SavedFingerprint;
    private static bool SameState(State a, State b, bool compareSelectionRevision = true) =>
        SameOwner(a,b) && a.Identity == b.Identity && (!compareSelectionRevision || a.SelectionRevision == b.SelectionRevision) &&
        a.Fingerprint == b.Fingerprint && a.Dirty == b.Dirty && a.SaveDelta == b.SaveDelta && a.CompileDelta == b.CompileDelta && a.LinkNext == b.LinkNext &&
        a.History.SequenceEqual(b.History) && a.Future.SequenceEqual(b.Future) && a.Lines.SequenceEqual(b.Lines);
    private static bool IsRestored(State before, State current) => SameOwner(before,current) && before.Fingerprint == current.Fingerprint &&
        before.Dirty == current.Dirty && before.SaveDelta == current.SaveDelta && before.CompileDelta == current.CompileDelta && before.LinkNext == current.LinkNext &&
        before.History.SequenceEqual(current.History) && before.Future.SequenceEqual(current.Future) && before.Lines.SequenceEqual(current.Lines);

    private static bool IsSingleApplied(State before, State current, CardPackNativePlan plan)
    {
        if (!SameOwner(before,current) || before.Fingerprint == current.Fingerprint || before.Identity.Revision == current.Identity.Revision ||
            current.History.Length != before.History.Length + 1 || current.Future.Length != 0 ||
            !current.History.Skip(1).SequenceEqual(before.History) || current.History[0].Previous != IntPtr.Zero || current.History[0].Next != IntPtr.Zero)
            return false;
        var top = OperationManager.history.Peek().TryCast<AuthoringEditorSession.ProjectSnapshotOperation>();
        if (top == null || top.session?.Pointer != current.Session || top.before == null || top.after == null ||
            AuthoringEditorSession.Fingerprint(top.before) != before.Fingerprint || AuthoringEditorSession.Fingerprint(top.after) != current.Fingerprint)
            return false;
        int anchor = Array.IndexOf(before.Lines, plan.Identity.AnchorLineId);
        if (anchor < 0 || current.Lines.Length != before.Lines.Length + plan.OperationCount) return false;
        for (int i = 0; i < before.Lines.Length; i++)
            if (current.Lines[i <= anchor ? i : i + plan.OperationCount] != before.Lines[i]) return false;
        return current.Lines.Skip(anchor + 1).Take(plan.OperationCount).All(id => !before.Lines.Contains(id, StringComparer.Ordinal));
    }

    private static JArray PrepareOperations(CardPackNativePlan plan)
    {
        Require(plan.OperationCount is >= 1 and <= CardPackV2Compiler.EffectiveNativeOperationLimit, "Native whole-pack operation count must be 1..500.");
        using var document = JsonDocument.Parse(plan.OperationsJson);
        var operations = document.RootElement;
        Require(operations.ValueKind == JsonValueKind.Array && operations.GetArrayLength() == plan.OperationCount, "Operation count differs from plan.");
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        void Resolve(JsonElement resource, string kind)
        {
            string? id = resource.GetString(); Require(!string.IsNullOrWhiteSpace(id), "Resource ID missing.");
            if (resolved.Add(kind + "\n" + id)) Require(AuthoringResourceCatalog.Resolve(id!, kind, plan.Identity.CatalogVersion, true) != null, "Native resource Resolve returned no result.");
        }
        for (int i = 0; i < plan.OperationCount; i++)
        {
            var operation = operations[i];
            Require(operation.EnumerateObject().All(p => p.Name is "op" or "node" or "ref" or "after_line" or "line") &&
                operation.GetProperty("op").GetString() == "line.insert" && operation.GetProperty("node").GetProperty("id").GetString() == plan.Identity.NodeId &&
                operation.GetProperty("ref").GetString() == "card_" + (i + 1), "Only the captured card compiler insertion chain is accepted.");
            var after = operation.GetProperty("after_line");
            Require(i == 0 ? after.GetProperty("id").GetString() == plan.Identity.AnchorLineId : after.GetProperty("ref").GetString() == "card_" + i, "Insertion anchor changed.");
            var line = operation.GetProperty("line");
            Require(line.EnumerateObject().All(p => p.Name is "inherit" or "mode" or "speaker" or "text" or "stage" or "scene") &&
                line.GetProperty("inherit").GetString() == "previous", "Unexpected writable field or inheritance request.");
            if (line.TryGetProperty("stage", out var stage)) foreach (var occupant in stage.EnumerateArray()) Resolve(occupant.GetProperty("resource_id"), "character");
            if (line.TryGetProperty("scene", out var scene)) foreach (var resource in scene.EnumerateObject())
            {
                Require(resource.Name is "transition" or "sound", "Unexpected scene resource family.");
                Require(resource.Value.GetProperty("action").GetString() == "set", "Unexpected scene action.");
                Resolve(resource.Value.GetProperty("resource_id"), resource.Name);
            }
        }
        var native = JArray.Parse(plan.OperationsJson);
        Require(native.Count == plan.OperationCount, "Native operation parser count mismatch.");
        return native;
    }
}
