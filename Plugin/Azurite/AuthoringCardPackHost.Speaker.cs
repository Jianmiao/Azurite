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
    private sealed record CapturedSpeaker(State State, AuthoringBatchSpeakerHostPreview Preview, string Targets, string ExpectedFingerprint);
    private CapturedSpeaker? _capturedSpeaker;

    public AuthoringBatchSpeakerHostPreview CaptureSpeaker(int targetSlot)
    {
        EnterBoundary();
        try
        {
            ClearCaptures();
            Require(targetSlot is >= 1 and <= 5, "Speaker target slot must be explicitly 1..5.");
            var before = ReadState(true, out var session);
            if (Uncertainty.ContainsKey(before.Identity.EditSession))
                throw new CardPackRecoveryRequiredException("This native session has an unresolved Apply; explicit recovery is required.");
            var selection = ReadBatchSelection(before);
            // Every selected row is validated, including rows later filtered as no-ops.
            string targets = ReadSpeakerTargets(before, selection, targetSlot);
            var compiled = AuthoringBatchSpeakerCompiler.TryCompile(selection, targetSlot);
            Require(compiled.IsValid && compiled.Plan != null, compiled.Message);
            string expected = ExpectedSpeakerFingerprint(before, session, compiled.Plan!);
            Require(AuthoringBatchPlanner.Matches(selection, ReadBatchSelection(before)), "Selected rows changed during speaker capture.");
            Require(ReadSpeakerTargets(before, selection, targetSlot) == targets, "Speaker resources changed during detached validation.");
            Require(SameState(before, ReadState(false, out _)), "Native state changed during speaker capture.");
            var preview = new AuthoringBatchSpeakerHostPreview(compiled.Plan!, before.Identity, SubmissionVersion(before.Identity.EditSession));
            _capturedSpeaker = new(before, preview, targets, expected);
            return preview;
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult CommitSpeaker(AuthoringBatchSpeakerHostPreview preview)
        => Submit(null, null, preview, preview?.Plan?.Selection.Revision ?? -1);

    private JArray PrepareSpeaker(State before, AuthoringEditorSession session, CapturedSpeaker captured)
    {
        var plan = captured.Preview.Plan;
        var selection = ReadBatchSelection(before);
        Require(AuthoringBatchPlanner.Matches(plan.Selection, selection), "Native selected rows differ from speaker preview.");
        Require(ReadSpeakerTargets(before, selection, plan.TargetSlot) == captured.Targets, "Speaker target resources differ from preview.");
        var compiled = AuthoringBatchSpeakerCompiler.TryCompile(selection, plan.TargetSlot);
        Require(compiled.IsValid && compiled.Plan != null && compiled.Plan.OperationsJson == plan.OperationsJson &&
            compiled.Plan.OperationCount == plan.OperationCount && compiled.Plan.Changes.SequenceEqual(plan.Changes), "Speaker payload differs from actual selected rows.");
        Require(ExpectedSpeakerFingerprint(before, session, plan) == captured.ExpectedFingerprint, "Complete speaker result differs from preview.");
        var native = JArray.Parse(plan.OperationsJson);
        Require(native.Count == plan.OperationCount, "Native speaker operation parser count mismatch.");
        return native;
    }

    private static string ReadSpeakerTargets(State state, AuthoringBatchSelection selection, int targetSlot)
    {
        Require(targetSlot is >= 1 and <= 5, "Speaker target slot must be 1..5.");
        var node = FindNode(state.Project, state.Identity.NodeId);
        var targets = new List<object>();
        foreach (var row in selection.Rows)
        {
            var line = node.Scripts[row.Index];
            Require(line.LineId == row.StableId && line.characters != null && line.characters.Count == 6 && line.isDialogScript,
                "Speaker changes require an existing dialogue row with its complete native stage.");
            using var readDocument = JsonDocument.Parse(AuthoringLines.Read(line, node.Guid).ToString());
            var read = readDocument.RootElement;
            Require(SpeakerString(read, "line_id") == row.StableId && SpeakerString(read, "node_id") == state.Identity.NodeId &&
                SpeakerString(read, "mode") == "dialogue", "Narration/control rows cannot be implicitly converted to dialogue.");
            var currentSpeaker = read.GetProperty("speaker");
            Require(currentSpeaker.ValueKind == JsonValueKind.Object && currentSpeaker.EnumerateObject().Count() == 1, "Existing dialogue speaker is unavailable.");
            if (line.speakerSlotNum is >= 1 and <= 5)
                Require(currentSpeaker.GetProperty("slot").GetInt32() == line.speakerSlotNum, "Native speaker slot projection disagrees with its snapshot.");
            else
                Require(line.speakerSlotNum == 0 && !string.IsNullOrEmpty(line.characters[0].name) &&
                    !string.IsNullOrWhiteSpace(SpeakerString(currentSpeaker, "offstage_resource_id")), "Existing offstage dialogue identity is unavailable.");

            var stage = read.GetProperty("stage");
            Require(stage.ValueKind == JsonValueKind.Array && stage.GetArrayLength() <= 5, "Native occupied stage projection is invalid.");
            JsonElement target = default; var slots = new HashSet<int>();
            foreach (var actor in stage.EnumerateArray())
            {
                int slot = actor.GetProperty("slot").GetInt32();
                Require(slot is >= 1 and <= 5 && slots.Add(slot), "Native stage slots are invalid or duplicated.");
                if (slot == targetSlot) target = actor;
            }
            Require(target.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(line.characters[targetSlot].name),
                "Each selected row must already contain the target stage actor.");
            string id = SpeakerString(target, "resource_id");
            string face = SpeakerString(target, "face_id");
            Require(face == line.characters[targetSlot].faceId, "Native target face differs from the captured actor.");
            var resolved = AuthoringResourceCatalog.Resolve(id, "character", state.Identity.CatalogVersion, true);
            Require(resolved != null, "Native target resource is unavailable.");
            using var resourceDocument = JsonDocument.Parse(resolved!.ToString());
            var resource = resourceDocument.RootElement;
            Require(SpeakerString(resource, "resource_id") == id && SpeakerString(resource, "kind") == "character" &&
                SpeakerString(resource, "catalog_version") == state.Identity.CatalogVersion && resource.GetProperty("available").ValueKind == JsonValueKind.True &&
                SpeakerString(resource, "native_key") == line.characters[targetSlot].name, "Target resource identity, availability or native actor binding changed.");
            var capabilities = resource.GetProperty("capabilities");
            Require(capabilities.GetProperty("stage").ValueKind == JsonValueKind.True && capabilities.GetProperty("faces_loaded").ValueKind == JsonValueKind.True &&
                !capabilities.TryGetProperty("load_error", out _), "Target stage/face capabilities are unavailable.");
            string[] faces = capabilities.GetProperty("faces").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Require(faces.Length > 0 && faces.All(value => !string.IsNullOrWhiteSpace(value)) && faces.Contains(face, StringComparer.Ordinal), "Target actor does not publish its current face.");
            targets.Add(new { line = row.StableId, slot = targetSlot, resource_id = id, native_key = line.characters[targetSlot].name, face_id = face, faces = faces.OrderBy(value => value, StringComparer.Ordinal).ToArray() });
        }
        return JsonSerializer.Serialize(targets);
    }

    private static string SpeakerString(JsonElement value, string property)
    {
        Require(value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()),
            "Native speaker projection is missing " + property + ".");
        return item.GetString()!;
    }

    private static string ExpectedSpeakerFingerprint(State before, AuthoringEditorSession session, AuthoringBatchSpeakerPlan plan)
    {
        var expected = ProjectFileStore.Clone(before.Project);
        var trial = ProjectFileStore.Clone(before.Project);
        Require(expected != null && trial != null && expected.Pointer != trial.Pointer &&
            expected.Pointer != before.Project.Pointer && trial.Pointer != before.Project.Pointer &&
            expected.Pointer != session.Snapshot.Pointer && trial.Pointer != session.Snapshot.Pointer &&
            AuthoringEditorSession.Fingerprint(expected) == before.Fingerprint && AuthoringEditorSession.Fingerprint(trial) == before.Fingerprint,
            "Independent complete speaker clones must be detached and lossless.");
        var source = FindNode(before.Project, before.Identity.NodeId);
        var snapshot = FindNode(session.Snapshot, before.Identity.NodeId);
        var expectedNode = FindNode(expected!, before.Identity.NodeId);
        var trialNode = FindNode(trial!, before.Identity.NodeId);
        var targets = new List<(ScriptData Expected, ScriptData Trial, int Slot)>();
        foreach (var change in plan.Changes)
        {
            int index = Array.IndexOf(before.Lines, change.StableId);
            Require(index >= 0, "Speaker target disappeared.");
            var a = expectedNode.Scripts[index]; var b = trialNode.Scripts[index]; var c = source.Scripts[index]; var d = snapshot.Scripts[index];
            Require(a.LineId == change.StableId && b.LineId == change.StableId && a.speakerSlotNum == change.BeforeSlot && b.speakerSlotNum == change.BeforeSlot &&
                a.Pointer != b.Pointer && a.Pointer != c.Pointer && a.Pointer != d.Pointer && b.Pointer != c.Pointer && b.Pointer != d.Pointer &&
                a.characters != null && b.characters != null && a.characters.Count == 6 && b.characters.Count == 6 &&
                a.characters.Pointer != b.characters.Pointer && a.characters.Pointer != c.characters.Pointer && a.characters.Pointer != d.characters.Pointer &&
                b.characters.Pointer != c.characters.Pointer && b.characters.Pointer != d.characters.Pointer, "Speaker clone row or character list is aliased or stale.");
            targets.Add((a, b, change.AfterSlot));
        }
        foreach (var target in targets)
        {
            // Native Patch explicitly uses these real constructors to reset offstage record 0.
            // No default field values, actors or resources are manufactured by this adapter.
            target.Expected.characters[0] = new ScriptData.CharacterRecordData(new Script.CharacterRecord());
            target.Expected.speakerSlotNum = target.Slot;
            var patch = JObject.Parse(JsonSerializer.Serialize(new { speaker = new { slot = target.Slot } }));
            AuthoringLines.Patch(target.Trial, patch, null!, before.Identity.CatalogVersion);
        }
        // Native Service.Apply normalizes the whole result after its operation loop.
        // Keep this actual step on the trial only; the independent expected model
        // must still permit only the requested slot and declared offstage reset.
        AuthoringGraph.Normalize(trial!);
        Require(AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint && AuthoringEditorSession.Fingerprint(session.Snapshot) == before.Fingerprint,
            "Detached speaker preflight changed an original snapshot.");
        string expectedFingerprint = AuthoringEditorSession.Fingerprint(expected!);
        Require(AuthoringEditorSession.Fingerprint(trial!) == expectedFingerprint,
            "Native detached Patch/Normalize changed fields beyond speakerSlotNum and the declared offstage reset.");
        return expectedFingerprint;
    }

    private static bool IsSpeakerApplied(State before, State current, AuthoringBatchSpeakerPlan plan, string expectedFingerprint)
    {
        if (current.Fingerprint != expectedFingerprint || !before.Lines.SequenceEqual(current.Lines) ||
            before.Identity.AnchorLineId != current.Identity.AnchorLineId || before.Identity.SelectedTargetSlot != current.Identity.SelectedTargetSlot) return false;
        var node = FindNode(current.Project, current.Identity.NodeId);
        return plan.Changes.All(change => { int index = Array.IndexOf(current.Lines, change.StableId); return index >= 0 && node.Scripts[index].speakerSlotNum == change.AfterSlot && node.Scripts[index].isDialogScript; });
    }
}
