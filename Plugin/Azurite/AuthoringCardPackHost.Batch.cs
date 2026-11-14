using System;
using System.Collections.Generic;
using System.Linq;
using Azurite.Core;
using AzureArchive.Automation;
using Newtonsoft.Json.Linq;
using Studio.Scripts;

namespace Azurite;

internal sealed partial class AuthoringCardPackHost
{
    private sealed record CapturedBatch(State State, AuthoringBatchHostPreview Preview, string ExpectedFingerprint);
    private CapturedBatch? _capturedBatch;

    public AuthoringBatchHostPreview CaptureSetText(string text)
    {
        EnterBoundary();
        try
        {
            ClearCaptures();
            var before = ReadState(true, out var session);
            if (Uncertainty.ContainsKey(before.Identity.EditSession))
                throw new CardPackRecoveryRequiredException("This native session has an unresolved Apply; explicit recovery is required.");
            var selection = ReadBatchSelection(before);
            var compiled = AuthoringBatchNativeCompiler.TryCompileSetText(selection, text);
            Require(compiled.IsValid && compiled.Plan != null, compiled.Message);
            string expected = ExpectedSetTextFingerprint(before, session, compiled.Plan!);
            Require(AuthoringBatchPlanner.Matches(selection, ReadBatchSelection(before)), "Selected rows changed during SetText capture.");
            Require(SameState(before, ReadState(false, out _)), "Native state changed during SetText capture.");
            var preview = new AuthoringBatchHostPreview(compiled.Plan!, before.Identity, SubmissionVersion(before.Identity.EditSession));
            _capturedBatch = new(before, preview, expected);
            return preview;
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult CommitSetText(AuthoringBatchHostPreview preview)
        => Submit(null, preview, preview?.Plan?.Selection.Revision ?? -1);

    private AuthoringBatchSelection ReadBatchSelection(State state)
    {
        Require(_readBatchRows != null, "The installed native selection owner must supply selected rows.");
        var rows = _readBatchRows!();
        Require(rows != null && rows.Count != 0, "An explicit nonempty native batch selection is required.");
        var inspector = ScriptNodeInspector.instance;
        var node = inspector?.scriptNode;
        Require(inspector != null && node != null && node.guid.ToString() == state.Identity.NodeId && node.scripts.Count == state.Lines.Length,
            "Selected node changed while reading batch rows.");
        var data = FindNode(state.Project, state.Identity.NodeId);
        var cache = inspector!.scriptNodeListItemsCache;
        Require(cache != null && cache.Count == state.Lines.Length, "Batch row cache is incomplete.");
        var result = new List<AuthoringBatchRow>(rows!.Count);
        int previous = -1;
        foreach (var row in rows)
        {
            Require(row != null && row.isActiveAndEnabled && row.gameObject.activeInHierarchy && row.inspector?.Pointer == inspector.Pointer && row.scriptNode?.Pointer == node!.Pointer &&
                row.index > previous && row.index < state.Lines.Length && cache![row.index]?.Pointer == row.Pointer, "Native selected row ownership, ordering or cache mapping is stale.");
            var live = node!.scripts[row!.index]; var snapshot = data.Scripts[row.index];
            Require(live != null && snapshot != null && live.LineId == state.Lines[row.index] && snapshot.LineId == live.LineId &&
                live.text == snapshot.text && live.speakerSlotNum == snapshot.speakerSlotNum, "Native selected row content differs from the captured project.");
            // Selection's node:pointer/script:pointer keys never leave that UI model.
            result.Add(new(live!.LineId, row.index, live.text, live.speakerSlotNum));
            previous = row.index;
        }
        return new(state.SelectionRevision, state.Identity.NodeId, result);
    }

    private JArray PrepareSetText(State before, AuthoringEditorSession session, CapturedBatch captured)
    {
        var selection = ReadBatchSelection(before);
        var plan = captured.Preview.Plan;
        Require(AuthoringBatchPlanner.Matches(plan.Selection, selection), "Native selected rows differ from the preview.");
        Require(plan.Changes.Count > 0, "SetText must contain actual changes.");
        var compiled = AuthoringBatchNativeCompiler.TryCompileSetText(selection, plan.Changes[0].After);
        Require(compiled.IsValid && compiled.Plan != null && compiled.Plan.OperationCount == plan.OperationCount &&
            compiled.Plan.OperationsJson == plan.OperationsJson && compiled.Plan.Changes.SequenceEqual(plan.Changes), "SetText payload no longer matches the actual selected rows.");
        Require(ExpectedSetTextFingerprint(before, session, plan) == captured.ExpectedFingerprint, "Expected complete project differs from preview.");
        var native = JArray.Parse(plan.OperationsJson);
        Require(native.Count == plan.OperationCount, "Native operation parser count mismatch.");
        return native;
    }

    private static string ExpectedSetTextFingerprint(State before, AuthoringEditorSession session, AuthoringBatchNativePlan plan)
    {
        // The same complete native clone facility used by Current.Apply, never a writable Read-field projection.
        var clone = ProjectFileStore.Clone(before.Project);
        Require(clone != null && clone.Pointer != before.Project.Pointer && clone.Pointer != session.Snapshot.Pointer &&
            AuthoringEditorSession.Fingerprint(clone) == before.Fingerprint, "Native complete project clone is missing, aliased or lossy.");
        var source = FindNode(before.Project, before.Identity.NodeId);
        var observed = FindNode(session.Snapshot, before.Identity.NodeId);
        var target = FindNode(clone!, before.Identity.NodeId);
        Require(target.Pointer != source.Pointer && target.Pointer != observed.Pointer && target.Scripts.Count == before.Lines.Length,
            "Native clone node must be detached and preserve all lines.");
        var targets = new List<(ScriptData Line, string Text)>();
        foreach (var change in plan.Changes)
        {
            int index = Array.IndexOf(before.Lines, change.StableId);
            Require(index >= 0 && target.Scripts[index].LineId == change.StableId && target.Scripts[index].text == change.Before &&
                source.Scripts[index].text == change.Before && target.Scripts[index].Pointer != source.Scripts[index].Pointer &&
                target.Scripts[index].Pointer != observed.Scripts[index].Pointer, "Native clone target is aliased or differs from its preview.");
            targets.Add((target.Scripts[index], change.After));
        }
        // Validate every target before setting any detached text field.
        foreach (var targetLine in targets) targetLine.Line.text = targetLine.Text;
        Require(AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint &&
            AuthoringEditorSession.Fingerprint(session.Snapshot) == before.Fingerprint, "Detached clone changed a captured native snapshot.");
        return AuthoringEditorSession.Fingerprint(clone!);
    }

    private static ScriptNodeData FindNode(ProjectData project, string nodeId)
    {
        ScriptNodeData? found = null;
        foreach (var candidate in project.nodes)
            if (candidate.Guid.ToString() == nodeId)
            {
                Require(found == null, "Duplicate target node identity in full snapshot.");
                found = candidate.TryCast<ScriptNodeData>();
                Require(found != null && found.Scripts != null, "Target node is not dialogue data.");
            }
        Require(found != null, "Target node is missing from full snapshot.");
        return found!;
    }

    private static bool IsSetTextApplied(State before, State current, AuthoringBatchNativePlan plan, string expectedFingerprint)
    {
        if (current.Fingerprint != expectedFingerprint || !before.Lines.SequenceEqual(current.Lines) ||
            before.Identity.AnchorLineId != current.Identity.AnchorLineId || before.Identity.SelectedTargetSlot != current.Identity.SelectedTargetSlot)
            return false;
        var node = FindNode(current.Project, current.Identity.NodeId);
        foreach (var change in plan.Changes)
        {
            int index = Array.IndexOf(current.Lines, change.StableId);
            if (index < 0 || node.Scripts[index].text != change.After) return false;
        }
        return true;
    }
}
