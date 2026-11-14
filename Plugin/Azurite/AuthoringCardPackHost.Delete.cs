using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azurite.Core;
using AzureArchive.Automation;
using Newtonsoft.Json.Linq;

namespace Azurite;

internal sealed partial class AuthoringCardPackHost
{
    private sealed record DeleteProjection(string Fingerprint, string DerivedChangesJson);
    private sealed record CapturedDelete(State State, AuthoringBatchDeleteHostPreview Preview, string ExpectedFingerprint);
    private CapturedDelete? _capturedDelete;

    public AuthoringBatchDeleteHostPreview CaptureDelete()
    {
        EnterBoundary();
        try
        {
            ClearCaptures();
            var before = ReadState(true, out var session);
            var selection = ReadBatchSelection(before);
            var compiled = AuthoringBatchDeleteCompiler.TryCompile(selection);
            Require(compiled.IsValid && compiled.Plan != null, compiled.Message);
            var projection = ExpectedDeleteProjection(before, session, compiled.Plan!);
            Require(AuthoringBatchPlanner.Matches(selection, ReadBatchSelection(before)), "Selected rows changed during deletion preview.");
            Require(SameState(before, ReadState(false, out _)), "Native state changed during deletion preflight.");
            var preview = new AuthoringBatchDeleteHostPreview(compiled.Plan!, before.Identity, SubmissionVersion(before.Identity.EditSession),
                before.Lines.Length == compiled.Plan!.OperationCount, projection.DerivedChangesJson);
            _capturedDelete = new(before, preview, projection.Fingerprint);
            return preview;
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult CommitDelete(AuthoringBatchDeleteHostPreview preview)
        => Submit(null, null, null, preview?.Plan?.Selection.Revision ?? -1, preview);

    private JArray PrepareDelete(State before, AuthoringEditorSession session, CapturedDelete captured)
    {
        var selection = ReadBatchSelection(before);
        var plan = captured.Preview.Plan;
        Require(AuthoringBatchPlanner.Matches(plan.Selection, selection), "Deletion selection differs from preview.");
        var compiled = AuthoringBatchDeleteCompiler.TryCompile(selection);
        Require(compiled.IsValid && compiled.Plan != null && compiled.Plan.OperationCount == plan.OperationCount &&
            compiled.Plan.OperationsJson == plan.OperationsJson && compiled.Plan.RemovedRows.SequenceEqual(plan.RemovedRows), "Deletion payload differs from current native rows.");
        var projection = ExpectedDeleteProjection(before, session, plan);
        Require(projection.Fingerprint == captured.ExpectedFingerprint && projection.DerivedChangesJson == captured.Preview.DerivedChangesJson,
            "Deletion or its native-derived changes differ from preview.");
        var operations = JArray.Parse(plan.OperationsJson);
        Require(operations.Count == plan.OperationCount, "Native deletion parser count mismatch.");
        return operations;
    }

    private static DeleteProjection ExpectedDeleteProjection(State before, AuthoringEditorSession session, AuthoringBatchDeletePlan plan)
    {
        var expected = ProjectFileStore.Clone(before.Project);
        var trial = ProjectFileStore.Clone(before.Project);
        var removed = new HashSet<string>(plan.RemovedRows.Select(row => row.StableId), StringComparer.Ordinal);
        RequireDeleteCloneSeparation(before, session, expected, trial, removed);
        var expectedNode = FindNode(expected, before.Identity.NodeId);
        var trialNode = FindNode(trial, before.Identity.NodeId);
        foreach (var row in plan.RemovedRows)
        {
            Require(row.Index >= 0 && row.Index < expectedNode.Scripts.Count && expectedNode.Scripts[row.Index].LineId == row.StableId,
                "Independent deletion index/ID mapping changed.");
            expectedNode.Scripts.RemoveAt(row.Index);
            int actual = DeleteLineIndex(trialNode, row.StableId);
            Require(actual >= 0 && trialNode.Scripts.Remove(trialNode.Scripts[actual]), "Native detached removal did not remove the exact persistent ID.");
        }
        // Exact host edit validation decides validity, including valid non-null empty nodes.
        AuthoringGraph.Normalize(trial);
        AuthoringGraph.RequireValid(trial, "edit");

        // Independently derive only the fixed Normalize effects. PreviousFor uses
        // read-only Outgoing; MatchWithPrev writes transient prev plus slots1..5 startingPos.
        var positions = new List<object>();
        foreach (var raw in expected.nodes)
        {
            var node = raw.TryCast<ScriptNodeData>();
            if (node == null) continue;
            for (int index = 0; index < node.Scripts.Count; index++)
            {
                var line = node.Scripts[index];
                Script matched = line;
                matched.MatchWithPrev(AuthoringGraph.PreviousFor(expected, node, index));
                for (int slot = 1; slot <= 5; slot++)
                {
                    int old = line.characters[slot].startingPos;
                    int next = matched.characters[slot].startingPos;
                    if (old != next) positions.Add(new { node_id = node.Guid.ToString(), line_id = line.LineId, slot, before = old, after = next });
                    line.characters[slot].startingPos = next;
                }
            }
        }
        // The fixed Preview.Refresh writes exactly these three project fields.
        // Run it on its own detached clone, then copy only those explicit outputs.
        string beforePreviewRefresh = AuthoringEditorSession.Fingerprint(expected);
        var previewShadow = ProjectFileStore.Clone(expected);
        Require(previewShadow != null && previewShadow.Pointer != expected.Pointer && previewShadow.Pointer != trial.Pointer &&
            previewShadow.Pointer != before.Project.Pointer && previewShadow.Pointer != session.Snapshot.Pointer &&
            AuthoringEditorSession.Fingerprint(previewShadow) == beforePreviewRefresh, "Preview derivation clone is aliased or lossy.");
        AuthoringProjectPreview.Refresh(previewShadow!);
        Require(AuthoringEditorSession.Fingerprint(expected) == beforePreviewRefresh, "Preview helper changed its independent baseline.");
        expected.PreviewBgName = previewShadow!.PreviewBgName;
        expected.PreviewHeader = previewShadow.PreviewHeader;
        expected.PreviewTitle = previewShadow.PreviewTitle;
        string fingerprint = AuthoringEditorSession.Fingerprint(expected);
        Require(AuthoringEditorSession.Fingerprint(trial) == fingerprint, "Deletion Normalize changed fields beyond exact removals and independently proven derived values.");
        Require(AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint && AuthoringEditorSession.Fingerprint(session.Snapshot) == before.Fingerprint,
            "Detached deletion preflight changed an original snapshot.");
        var derived = new
        {
            starting_positions = positions,
            project_preview = new
            {
                before = new { background = before.Project.PreviewBgName, header = before.Project.PreviewHeader, title = before.Project.PreviewTitle },
                after = new { background = expected.PreviewBgName, header = expected.PreviewHeader, title = expected.PreviewTitle }
            }
        };
        return new(fingerprint, JsonSerializer.Serialize(derived));
    }

    private static void RequireDeleteCloneSeparation(State before, AuthoringEditorSession session, ProjectData expected, ProjectData trial, HashSet<string> removed)
    {
        Require(expected != null && trial != null && new HashSet<IntPtr> { before.Project.Pointer, session.Snapshot.Pointer }.All(p => p != expected.Pointer && p != trial.Pointer) &&
            expected.Pointer != trial.Pointer && AuthoringEditorSession.Fingerprint(expected) == before.Fingerprint && AuthoringEditorSession.Fingerprint(trial) == before.Fingerprint,
            "Complete deletion clones must be detached and lossless.");
        foreach (var raw in before.Project.nodes)
        {
            var source = raw.TryCast<ScriptNodeData>();
            if (source == null) continue;
            var snapshot = FindNode(session.Snapshot, source.Guid.ToString());
            var a = FindNode(expected!, source.Guid.ToString()); var b = FindNode(trial!, source.Guid.ToString());
            var nodePointers = new HashSet<IntPtr> { source.Pointer, snapshot.Pointer };
            var lists = new HashSet<IntPtr> { source.Scripts.Pointer, snapshot.Scripts.Pointer };
            Require(nodePointers.Add(a.Pointer) && nodePointers.Add(b.Pointer) && lists.Add(a.Scripts.Pointer) && lists.Add(b.Scripts.Pointer) &&
                a.Scripts.Count == source.Scripts.Count && b.Scripts.Count == source.Scripts.Count && snapshot.Scripts.Count == source.Scripts.Count,
                "Deletion clone shares a native node or Scripts container.");
            for (int i = 0; i < source.Scripts.Count; i++)
            {
                var c = source.Scripts[i]; var d = snapshot.Scripts[i]; var x = a.Scripts[i]; var y = b.Scripts[i];
                Require(x.LineId == c.LineId && y.LineId == c.LineId && d.LineId == c.LineId, "Cloned deletion line identities differ.");
                if (removed.Contains(c.LineId)) continue;
                var lines = new HashSet<IntPtr> { c.Pointer, d.Pointer };
                Require(lines.Add(x.Pointer) && lines.Add(y.Pointer) && c.characters != null && d.characters != null && x.characters != null && y.characters != null &&
                    c.characters.Count == 6 && d.characters.Count == 6 && x.characters.Count == 6 && y.characters.Count == 6, "Retained native line/character layout is aliased or invalid.");
                var characters = new HashSet<IntPtr> { c.characters.Pointer, d.characters.Pointer };
                Require(characters.Add(x.characters.Pointer) && characters.Add(y.characters.Pointer), "Retained character containers are aliased.");
                for (int slot = 1; slot <= 5; slot++)
                {
                    var actors = new HashSet<IntPtr> { c.characters[slot].Pointer, d.characters[slot].Pointer };
                    Require(actors.Add(x.characters[slot].Pointer) && actors.Add(y.characters[slot].Pointer), "Retained starting-position records are aliased.");
                }
            }
        }
    }

    private static int DeleteLineIndex(ScriptNodeData node, string id)
    {
        for (int i = 0; i < node.Scripts.Count; i++) if (node.Scripts[i].LineId == id) return i;
        return -1;
    }

    private static bool IsDeleteApplied(State before, State current, AuthoringBatchDeletePlan plan, string expectedFingerprint)
    {
        var removed = new HashSet<string>(plan.RemovedRows.Select(row => row.StableId), StringComparer.Ordinal);
        return current.Fingerprint == expectedFingerprint && current.Lines.SequenceEqual(before.Lines.Where(id => !removed.Contains(id))) &&
            current.Lines.Length == before.Lines.Length - plan.OperationCount && (current.Identity.AnchorLineId == null || current.Lines.Contains(current.Identity.AnchorLineId, StringComparer.Ordinal));
    }
}
