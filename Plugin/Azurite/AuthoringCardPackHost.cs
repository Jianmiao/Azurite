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
internal sealed partial class AuthoringCardPackHost : ICardPackV2Host, IAuthoringBatchHost
{
    private sealed record HistoryItem(IntPtr Pointer, IntPtr Previous, IntPtr Next);
    private sealed record State(CardPackContextIdentity Identity, long SelectionRevision,
        IntPtr Session, IntPtr Studio, string Fingerprint, string FileVersion, string? SavedFingerprint, bool Dirty,
        int SaveDelta, int CompileDelta, bool LinkNext, HistoryItem[] History, HistoryItem[] Future, string[] Lines, ProjectData Project);
    private sealed record Captured(State State, string? Locale, string Content, long SubmissionVersion);
    private sealed record Submission(CardPackNativePlan? Cards, AuthoringBatchHostPreview? Text, AuthoringBatchSpeakerHostPreview? Speaker, AuthoringBatchDeleteHostPreview? Delete, string? ExpectedFingerprint, CapturedMultiline? Multiline = null);
    private sealed class Uncertain
    {
        internal readonly State Before;
        internal readonly Submission Plan;
        internal bool NativeReturnedReceipt;
        internal string? MultilineReceiptJson;
        internal State? ConfirmedAfter;
        internal string Diagnostic = "Native Apply entered; outcome not yet verified.";
        internal Uncertain(State before, Submission plan) { Before = before; Plan = plan; }
    }
    // Session ownership outlives any individual host/workflow/panel instance.
    // Entries are removed only by a verified success or explicit verified recovery.
    private static readonly Dictionary<string, Uncertain> Uncertainty = new(StringComparer.Ordinal);
    // Preview epochs belong to this same session owner; they do not authorize Apply or recover faults.
    private static readonly Dictionary<string, long> SubmissionVersions = new(StringComparer.Ordinal);
    private static int _nativeBoundaryBusy;
    private readonly int _editorThreadId;
    private readonly Func<bool> _authoringAllowed;
    private readonly Func<long> _selectionRevision;
    private readonly Func<CardPackContextIdentity, string?, CardPackCompilationContext> _captureContent;
    private readonly Action<string, IReadOnlyList<string>> _invalidateSelection;
    private readonly Func<IReadOnlyList<ScriptListItem>>? _readBatchRows;
    private Captured? _captured;
    // Retained after local captures are consumed, for managed-only revocation
    // when a later explicit attempt fails before qualified ReadState completes.
    private string? _lastQualifiedSession;

    internal AuthoringCardPackHost(int editorThreadId, Func<bool> authoringAllowed,
        Func<long> selectionRevision,
        Func<CardPackContextIdentity, string?, CardPackCompilationContext> captureContent,
        Action<string, IReadOnlyList<string>> invalidateSelection,
        Func<IReadOnlyList<ScriptListItem>>? readBatchRows = null)
    {
        if (editorThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(editorThreadId));
        _editorThreadId = editorThreadId;
        _authoringAllowed = authoringAllowed ?? throw new ArgumentNullException(nameof(authoringAllowed));
        _selectionRevision = selectionRevision ?? throw new ArgumentNullException(nameof(selectionRevision));
        _captureContent = captureContent ?? throw new ArgumentNullException(nameof(captureContent));
        _invalidateSelection = invalidateSelection ?? throw new ArgumentNullException(nameof(invalidateSelection));
        _readBatchRows = readBatchRows;
    }

    public CardPackV2Snapshot Capture(string? selectedLocale)
    {
        EnterBoundary();
        try
        {
            ClearCaptures();
            var state = ReadState(true, out _);
            if (Uncertainty.ContainsKey(state.Identity.EditSession)) throw new CardPackRecoveryRequiredException("This native session has an unresolved Apply; explicit recovery is required.");
            var content = CaptureContent(state.Identity, selectedLocale);
            var current = ReadState(false, out _);
            Require(SameState(state, current), "Native state changed while capturing card content.");
            long version = SubmissionVersion(state.Identity.EditSession);
            _captured = new(state, selectedLocale, JsonSerializer.Serialize(content), version);
            return new(content, state.SelectionRevision, version);
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult Commit(CardPackNativePlan plan, long selectionRevision)
        => Submit(plan, null, null, selectionRevision);

    // Both operation families enter exactly this owner, Check/Apply and uncertainty lifecycle.
    private CardPackV2CommitResult Submit(CardPackNativePlan? cards, AuthoringBatchHostPreview? text, AuthoringBatchSpeakerHostPreview? speaker, long selectionRevision, AuthoringBatchDeleteHostPreview? deletion = null, AuthoringBatchMultilineHostPreview? multiline = null)
    {
        bool entered = false, applyEntered = false, refreshAttempted = false;
        Uncertain? pending = null;
        try
        {
            EnterBoundary(); entered = true;
            var captured = _captured; var batch = _capturedBatch; var speakerCapture = _capturedSpeaker; var deleteCapture = _capturedDelete; var multilineCapture = _capturedMultiline;
            var attempt = RevokeOwnedPreviews();
            Require((cards != null ? 1 : 0) + (text != null ? 1 : 0) + (speaker != null ? 1 : 0) + (deletion != null ? 1 : 0) + (multiline != null ? 1 : 0) == 1, "Exactly one supported submission kind is required.");
            State capturedState;
            long capturedVersion;
            if (cards != null)
            {
                Require(captured != null, "A fresh host Capture is required for each commit attempt.");
                capturedState = captured!.State; capturedVersion = captured.SubmissionVersion;
            }
            else if (text != null)
            {
                Require(batch != null && ReferenceEquals(batch.Preview, text), "A fresh SetText capture from this owner is required.");
                capturedState = batch!.State; capturedVersion = batch.Preview.SubmissionVersion;
            }
            else if (speaker != null)
            {
                Require(speakerCapture != null && ReferenceEquals(speakerCapture.Preview, speaker), "A fresh speaker capture from this owner is required.");
                capturedState = speakerCapture!.State; capturedVersion = speakerCapture.Preview.SubmissionVersion;
            }
            else if (deletion != null)
            {
                Require(deleteCapture != null && ReferenceEquals(deleteCapture.Preview, deletion), "A fresh deletion capture from this owner is required.");
                capturedState = deleteCapture!.State; capturedVersion = deleteCapture.Preview.SubmissionVersion;
            }
            else
            {
                Require(multilineCapture != null && ReferenceEquals(multilineCapture.Preview, multiline), "A fresh multiline capture from this owner is required.");
                capturedState = multilineCapture!.State; capturedVersion = multilineCapture.Preview.SubmissionVersion;
            }
            Require(capturedState.Identity.EditSession == attempt.Session && capturedVersion == attempt.Version,
                "Another client consumed or revoked this session's previews.");
            var before = ReadState(true, out var session);
            Require(!Uncertainty.ContainsKey(before.Identity.EditSession), "RECOVERY_REQUIRED: previous Apply is unresolved.");
            Require((cards?.Identity ?? text?.Identity ?? speaker?.Identity ?? deletion?.Identity ?? multiline!.Identity) == before.Identity && SameState(capturedState, before) && before.SelectionRevision == selectionRevision,
                "Native identity, selection, project or history changed before commit.");
            JArray operations;
            Submission submission;
            if (cards != null)
            {
                var content = CaptureContent(before.Identity, captured!.Locale);
                Require(string.Equals(captured.Content, JsonSerializer.Serialize(content), StringComparison.Ordinal), "Native inherited stage or catalog capabilities changed.");
                operations = PrepareOperations(cards);
                submission = new(cards, null, null, null, null);
            }
            else if (text != null)
            {
                operations = PrepareSetText(before, session, batch!);
                submission = new(null, text, null, null, batch!.ExpectedFingerprint);
            }
            else if (speaker != null)
            {
                operations = PrepareSpeaker(before, session, speakerCapture!);
                submission = new(null, null, speaker, null, speakerCapture!.ExpectedFingerprint);
            }
            else if (deletion != null)
            {
                operations = PrepareDelete(before, session, deleteCapture!);
                submission = new(null, null, null, deletion, deleteCapture!.ExpectedFingerprint);
            }
            else
            {
                operations = PrepareMultiline(before, session, multilineCapture!);
                submission = new(null, null, null, null, multilineCapture!.Projection.Fingerprint, multilineCapture);
            }
            Require(SameState(before, ReadState(false, out session)), "Native state changed during final content/resource validation.");
            // Current.Apply does NOT perform Check internally. Keep both guards true.
            session.Check(before.Identity.EditSession, before.Identity.Revision, true, true);
            var checkedState = ReadState(false, out var checkedSession);
            Require(SameState(before, checkedState), "Native owner or selection changed inside Check.");
            if (text != null || speaker != null || deletion != null || multiline != null)
            {
                var expectedSelection = text?.Plan.Selection ?? speaker?.Plan.Selection ?? deletion?.Plan.Selection ?? multiline!.Plan.Selection;
                Require(AuthoringBatchPlanner.Matches(expectedSelection, ReadBatchSelection(checkedState)), "Selected batch rows changed inside Check.");
                if (speaker != null)
                    Require(ReadSpeakerTargets(checkedState, expectedSelection, speaker.Plan.TargetSlot) == speakerCapture!.Targets,
                        "Speaker resources or target stage changed inside Check.");
                if (multiline != null) PrepareMultiline(checkedState, checkedSession, multilineCapture!);
                Require(SameState(before, ReadState(false, out checkedSession)), "Native owner changed while rechecking selected rows.");
            }
            pending = new(before, submission);
            Uncertainty.Add(before.Identity.EditSession, pending);
            applyEntered = true;
            var receipt = checkedSession.Apply(operations, before.Identity.CatalogVersion);
            pending.NativeReturnedReceipt = receipt != null;
            if (multiline != null) pending.MultilineReceiptJson = receipt?.ToString();
            string? deleteScope = submission.Delete?.Identity.NodeId;
            var afterNative = ReadState(true, out _, requireWriteEligibility: false, deletionNodeId: deleteScope);
            Require(receipt != null, "Native Apply returned no receipt.");
            Require(IsApplied(before, afterNative, submission, pending.MultilineReceiptJson), "Native result/model/history do not prove one complete snapshot commit.");
            pending.ConfirmedAfter = afterNative;
            refreshAttempted = true;
            _invalidateSelection(afterNative.Identity.NodeId, Array.AsReadOnly(afterNative.Lines));
            Require(SameState(afterNative, ReadState(false, out _, requireWriteEligibility: false, deletionNodeId: deleteScope), compareSelectionRevision: false), "Native state changed while rebuilding selection caches.");
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
                    try { var observed = RefreshSelectionState(pending?.Plan.Delete?.Identity.NodeId); if (pending != null) pending.Diagnostic += " | observed revision=" + observed.Identity.Revision; }
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
            EnterBoundary(); entered = true;
            var attempt = RevokeOwnedPreviews();
            var current = ReadState(true, out var session, recovering: true);
            if (current.Identity.EditSession != attempt.Session) AdvanceSubmissionVersion(current.Identity.EditSession);
            if (!Uncertainty.TryGetValue(current.Identity.EditSession, out var fault))
                return new(true, "当前原生会话没有待确认提交；需要重新预览。");
            string? deleteScope = fault.Plan.Delete?.Identity.NodeId;
            session.Check(current.Identity.EditSession, current.Identity.Revision, true, true);
            Require(SameState(current, ReadState(false, out _, deletionNodeId: deleteScope, recovering: true)), "Native state changed during recovery Check.");
            current = RefreshSelectionState(deleteScope);
            bool restored = IsRestored(fault.Before, current);
            // A snapshot history entry can precede a later native failure. Without
            // a completed Apply return we cannot prove its remaining bookkeeping.
            bool applied = !restored && fault.NativeReturnedReceipt && fault.ConfirmedAfter != null &&
                SameState(fault.ConfirmedAfter, current, compareSelectionRevision: false) && IsApplied(fault.Before, current, fault.Plan, fault.MultilineReceiptJson);
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
    private void ClearCaptures() { _captured = null; _capturedBatch = null; _capturedSpeaker = null; _capturedDelete = null; _capturedMultiline = null; }
    private (string? Session, long Version) RevokeOwnedPreviews()
    {
        ClearCaptures();
        // Called only after EnterBoundary. Never read native Current or trust a
        // supplied preview to invent/reassign an unqualified revocation scope.
        string? session = _lastQualifiedSession;
        if (session == null) return (null, 0);
        long previous = SubmissionVersion(session);
        AdvanceSubmissionVersion(session);
        return (session, previous);
    }
    private static long SubmissionVersion(string session) => SubmissionVersions.TryGetValue(session, out long version) ? version : 0;
    private static void AdvanceSubmissionVersion(string session) => SubmissionVersions[session] = checked(SubmissionVersion(session) + 1);
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

    private State ReadState(bool refresh, out AuthoringEditorSession session, bool requireWriteEligibility = true, string? deletionNodeId = null, bool recovering = false)
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
        // A deleted anchor must not hide the existing session fault from another client.
        // This is an exception signal, never permission to capture/submit without a selection.
        if (requireWriteEligibility && !recovering && deletionNodeId == null && Uncertainty.ContainsKey(session.EditSession))
            throw new CardPackRecoveryRequiredException("This native session has an unresolved Apply; explicit recovery is required.");
        if (recovering && deletionNodeId == null && Uncertainty.TryGetValue(session.EditSession, out var pendingDelete) && pendingDelete.Plan.Delete != null)
            deletionNodeId = pendingDelete.Before.Identity.NodeId;
        bool observingDelete = deletionNodeId != null;
        if (observingDelete) Require(nodeId == deletionNodeId, "Deletion result observation requires the original native node.");
        var row = inspector.selectedScriptItem;
        var cache = inspector.scriptNodeListItemsCache;
        string? anchor = null;
        if (row != null && row.selected)
        {
            Require(row.isActiveAndEnabled && row.gameObject.activeInHierarchy && row.inspector?.Pointer == inspector.Pointer && row.scriptNode?.Pointer == node.Pointer && row.index >= 0 && row.index < lines.Length,
                "The active anchor row is stale.");
            Require(cache != null && row.index < cache.Count && cache[row.index]?.Pointer == row.Pointer, "Selected row cache is stale.");
            anchor = lines[row.index];
        }
        else Require(observingDelete, "An explicit currently selected anchor row is required.");
        if (observingDelete)
        {
            Require(cache != null && cache.Count == lines.Length, "Deletion result cache does not match remaining rows.");
            for (int i = 0; i < lines.Length; i++)
            {
                var item = cache![i];
                Require(item != null && item.index == i && item.inspector?.Pointer == inspector.Pointer && item.scriptNode?.Pointer == node.Pointer &&
                    (!item.selected || anchor != null && item.Pointer == row!.Pointer), "Deletion result cache identity/selection is stale.");
            }
        }
        long revision = _selectionRevision();
        Require(revision >= 0, "Selection revision is unavailable.");
        string catalog = AuthoringResourceCatalog.CatalogVersion;
        Require(!string.IsNullOrWhiteSpace(session.EditSession) && !string.IsNullOrWhiteSpace(session.Revision) && !string.IsNullOrWhiteSpace(catalog), "Opaque native session/revision/catalog identities are missing.");
        int slot = inspector.selectedSlotNum;
        var identity = new CardPackContextIdentity(session.ProjectId, session.EditSession, session.Revision, catalog, nodeId, anchor, slot is >= 1 and <= 5 ? slot : null);
        var state = new State(identity, revision, session.Pointer, studio.Pointer, fingerprint, session.FileVersion, session.savedFingerprint, session.Dirty,
            OperationManager.saveDelta, OperationManager.compileDelta, OperationManager.needToLinkWithPrev,
            ReadStack(OperationManager.history), ReadStack(OperationManager.future), lines, project);
        if (requireWriteEligibility) _lastQualifiedSession = state.Identity.EditSession;
        return state;
    }

    private State RefreshSelectionState(string? deletionNodeId = null)
    {
        var state = ReadState(true, out _, requireWriteEligibility: false, deletionNodeId: deletionNodeId);
        _invalidateSelection(state.Identity.NodeId, Array.AsReadOnly(state.Lines));
        var fresh = ReadState(false, out _, requireWriteEligibility: false, deletionNodeId: deletionNodeId);
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

    private static bool IsApplied(State before, State current, Submission submission, string? multilineReceipt = null)
        => IsSingleHistory(before, current) && (submission.Cards != null
            ? IsInserted(before, current, submission.Cards)
            : submission.Text != null ? IsSetTextApplied(before, current, submission.Text.Plan, submission.ExpectedFingerprint!)
            : submission.Speaker != null ? IsSpeakerApplied(before, current, submission.Speaker.Plan, submission.ExpectedFingerprint!)
            : submission.Delete != null ? IsDeleteApplied(before, current, submission.Delete.Plan, submission.ExpectedFingerprint!)
            : submission.Multiline != null && IsMultilineApplied(before, current, submission.Multiline, multilineReceipt));

    private static bool IsSingleHistory(State before, State current)
    {
        if (!SameOwner(before,current) || before.Fingerprint == current.Fingerprint || before.Identity.Revision == current.Identity.Revision ||
            current.History.Length != before.History.Length + 1 || current.Future.Length != 0 ||
            !current.History.Skip(1).SequenceEqual(before.History) || current.History[0].Previous != IntPtr.Zero || current.History[0].Next != IntPtr.Zero)
            return false;
        var top = OperationManager.history.Peek().TryCast<AuthoringEditorSession.ProjectSnapshotOperation>();
        if (top == null || top.session?.Pointer != current.Session || top.before == null || top.after == null ||
            AuthoringEditorSession.Fingerprint(top.before) != before.Fingerprint || AuthoringEditorSession.Fingerprint(top.after) != current.Fingerprint)
            return false;
        return !current.LinkNext && current.SaveDelta == DeltaAfterIsolatedWrite(before.SaveDelta) &&
            current.CompileDelta == (top.IsMajor ? DeltaAfterIsolatedWrite(before.CompileDelta) : before.CompileDelta);
    }

    // Pinned native WriteHistory, new/uncombined branch: negative deltas become
    // 0x1bf52; nonnegative counters use a 32-bit increment. This only verifies;
    // native history remains the sole writer. Compile changes only for IsMajor.
    private static int DeltaAfterIsolatedWrite(int before) => before < 0 ? 114514 : unchecked(before + 1);

    private static bool IsInserted(State before, State current, CardPackNativePlan plan)
    {
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
            if (resolved.Add(kind + "\n" + id))
            {
                var nativeResource = AuthoringResourceCatalog.Resolve(id!, kind, plan.Identity.CatalogVersion, true);
                Require(nativeResource != null, "Native resource Resolve returned no result.");
                // Catalog.Resolve can return a normal object with available:false;
                // unlike AuthoringLines.Resolve, it does not reject that state.
                using var response = JsonDocument.Parse(nativeResource.ToString());
                var value = response.RootElement;
                Require(value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty("resource_id", out var resolvedId) && resolvedId.ValueKind == JsonValueKind.String && resolvedId.GetString() == id &&
                    value.TryGetProperty("kind", out var resolvedKind) && resolvedKind.ValueKind == JsonValueKind.String && resolvedKind.GetString() == kind &&
                    value.TryGetProperty("catalog_version", out var catalog) && catalog.ValueKind == JsonValueKind.String && catalog.GetString() == plan.Identity.CatalogVersion &&
                    value.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True,
                    "Native resource is unavailable or its identity/catalog changed before commit.");
            }
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
