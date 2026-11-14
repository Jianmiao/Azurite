using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Azurite.Core;
using AzureArchive.Automation;
using Newtonsoft.Json.Linq;

namespace Azurite;

internal sealed partial class AuthoringCardPackHost
{
    private sealed record DuplicateSources(AuthoringBatchDuplicateInput[] Inputs, string Resources, string SourceJson);
    private sealed record DuplicateProjection(string Fingerprint, string PreviewJson, string[] NewLineIds);
    private sealed record CapturedDuplicate(State State, AuthoringBatchDuplicateHostPreview Preview, DuplicateSources Sources, DuplicateProjection Projection);
    private CapturedDuplicate? _capturedDuplicate;

    public AuthoringBatchDuplicateHostPreview CaptureDuplicate(AuthoringBatchDuplicateOptions? options = null)
    {
        EnterBoundary();
        try
        {
            ClearCaptures();
            options ??= new();
            var before = ReadState(true, out var session);
            var selection = ReadBatchSelection(before);
            var sources = ReadDuplicateSources(before, selection, options);
            var compiled = AuthoringBatchDuplicateCompiler.TryCompile(selection, sources.Inputs);
            Require(compiled.IsValid && compiled.Plan != null, compiled.Message);
            var projection = ProjectDuplicate(before, session, compiled.Plan!, options, sources.SourceJson, null);
            Require(AuthoringBatchPlanner.Matches(selection, ReadBatchSelection(before)), "Duplicate source selection changed during preflight.");
            var rechecked = ReadDuplicateSources(before, selection, options);
            Require(rechecked.Resources == sources.Resources && rechecked.SourceJson == sources.SourceJson && rechecked.Inputs.SequenceEqual(sources.Inputs), "Duplicate resources or full source projection changed during preflight.");
            Require(SameState(before, ReadState(false, out _)), "Native state changed during duplicate preflight.");
            var preview = new AuthoringBatchDuplicateHostPreview(compiled.Plan!, before.Identity, SubmissionVersion(before.Identity.EditSession), options, projection.PreviewJson);
            _capturedDuplicate = new(before, preview, sources, projection);
            return preview;
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult CommitDuplicate(AuthoringBatchDuplicateHostPreview preview)
        => Submit(null, null, null, preview?.Plan?.Selection.Revision ?? -1, duplicate: preview);

    private JArray PrepareDuplicate(State before, AuthoringEditorSession session, CapturedDuplicate captured)
    {
        var selection = ReadBatchSelection(before); var plan = captured.Preview.Plan;
        Require(AuthoringBatchPlanner.Matches(plan.Selection, selection), "Duplicate selected sources differ from preview.");
        var sources = ReadDuplicateSources(before, selection, captured.Preview.Options);
        Require(sources.Resources == captured.Sources.Resources && sources.SourceJson == captured.Sources.SourceJson && sources.Inputs.SequenceEqual(captured.Sources.Inputs), "Duplicate full source or effective resource bindings differ from preview.");
        var compiled = AuthoringBatchDuplicateCompiler.TryCompile(selection, sources.Inputs);
        Require(compiled.IsValid && compiled.Plan != null && compiled.Plan.OperationsJson == plan.OperationsJson && compiled.Plan.Copies.SequenceEqual(plan.Copies), "Duplicate native payload differs from actual selected sources.");
        var projection = ProjectDuplicate(before, session, plan, captured.Preview.Options, sources.SourceJson, captured.Projection.NewLineIds);
        Require(projection.Fingerprint == captured.Projection.Fingerprint && projection.PreviewJson == captured.Projection.PreviewJson, "Complete duplicate result differs from preview.");
        var native = JArray.Parse(plan.OperationsJson);
        Require(native.Count == plan.OperationCount, "Native duplicate parser count mismatch.");
        return native;
    }

    private static DuplicateSources ReadDuplicateSources(State state, AuthoringBatchSelection selection, AuthoringBatchDuplicateOptions options)
    {
        Require(selection.Rows.Count is >= 1 and <= AuthoringBatchNativeCompiler.MaxNativeOperations, "Native duplication accepts 1..500 original rows; no expansion or prefix is submitted.");
        var logical = AuthoringBatchPlanner.TryPlan(selection, AuthoringBatchAction.Duplicate);
        Require(logical.IsValid, logical.Message);
        var node = FindNode(state.Project, state.Identity.NodeId);
        var inputs = new List<AuthoringBatchDuplicateInput>(); var resources = new List<object>(); var sourceStates = new List<object>();
        foreach (var row in selection.Rows)
        {
            var source = node.Scripts[row.Index]; var read = MultilineRead(source, node, row.StableId);
            Require(AuthoringCardPackHostCopyProjection.TryProject(read, state.Identity.NodeId, row.StableId, out var projected, out var diagnostic),
                "Source is not completely representable: " + diagnostic?.Code + " " + diagnostic?.Path + " " + diagnostic?.Message);
            string retained = MultilineResources(source, read, state.Identity.CatalogVersion);
            var voice = read.GetProperty("scene").GetProperty("voice"); JsonElement? voiceResource = null;
            if (!options.CopyVoice)
            {
                // Explicit caller choice changes only the new copy's content.
                // It is not a claim that an unknown voice was never assigned.
            }
            else if (string.IsNullOrEmpty(source.voice))
                Require(SpeakerString(voice, "action") == "none" && !voice.TryGetProperty("resource_id", out _), "Explicit no-audio source disagrees with native Read.");
            else
            {
                Require(SpeakerString(voice, "action") == "set", "Source voice assignment is not represented by native Read.");
                // A generated-looking key or Resolve failure is not proof of no
                // assignment. Unknown/unavailable voice stays a visible refusal.
                var resolved = ResolveMultilineResource(SpeakerString(voice, "resource_id"), "voice", state.Identity.CatalogVersion);
                Require(SpeakerString(resolved, "native_key") == source.voice, "Copy voice resource maps to another native audio key.");
                voiceResource = resolved;
            }
            resources.Add(new { source_id = row.StableId, retained, voice = voiceResource });
            inputs.Add(new(row.StableId, DuplicateLineInput(projected!.LineInput, options.CopyVoice)));
            sourceStates.Add(new { state = read, selection_group = source.selectionGroup, background_name = source.bgFriendlyName,
                voice_policy = !options.CopyVoice ? "omit_by_explicit_user_choice" : string.IsNullOrEmpty(source.voice) ? "explicit_no_audio" : "preserve_assigned_audio_resource" });
        }
        return new(inputs.ToArray(), JsonSerializer.Serialize(resources), JsonSerializer.Serialize(sourceStates));
    }

    private static string DuplicateLineInput(JsonElement projected, bool copyVoice)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in projected.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name != "scene") writer.WriteRawValue(property.Value.GetRawText());
                else
                {
                    writer.WriteStartObject();
                    foreach (var scene in property.Value.EnumerateObject())
                    {
                        // The pinned from-copy constructor retains bgName and
                        // bgFriendlyName. Reassigning background would replace the
                        // stored friendly name with current catalog display text.
                        if (scene.Name == "background") continue;
                        writer.WritePropertyName(scene.Name);
                        if (scene.Name == "voice" && !copyVoice) { writer.WriteStartObject(); writer.WriteString("action", "none"); writer.WriteEndObject(); }
                        else writer.WriteRawValue(scene.Value.GetRawText());
                    }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string NewDuplicateLineId(ScriptData line, HashSet<string> ids)
    {
        Require(Guid.TryParseExact(line.LineId, "D", out var id) && id != Guid.Empty && id.ToString("D") == line.LineId && ids.Add(line.LineId),
            "Native copied line identity must be new, unique and match the pinned native GUID contract.");
        return line.LineId;
    }

    private static DuplicateProjection ProjectDuplicate(State before, AuthoringEditorSession session, AuthoringBatchDuplicatePlan plan, AuthoringBatchDuplicateOptions options, string sourceJson, string[]? comparisonIds)
    {
        var expected = ProjectFileStore.Clone(before.Project); var trial = ProjectFileStore.Clone(before.Project); var copySource = ProjectFileStore.Clone(before.Project);
        var none = new HashSet<string>(StringComparer.Ordinal);
        RequireDeleteCloneSeparation(before, session, expected, trial, none);
        RequireDeleteCloneSeparation(before, session, expected, copySource, none);
        RequireDeleteCloneSeparation(before, session, trial, copySource, none);
        var expectedNode = FindNode(expected, before.Identity.NodeId); var trialNode = FindNode(trial, before.Identity.NodeId); var sourceNode = FindNode(copySource, before.Identity.NodeId);
        var allIds = new HashSet<string>(MultilineProjectLines(before.Project).Select(l => l.LineId), StringComparer.Ordinal);
        var newIds = new List<string>(); var inserted = new List<ScriptData>();
        for (int i = 0; i < plan.Copies.Count; i++)
        {
            var spec = plan.Copies[i]; int index = spec.TargetIndex;
            Require(index > 0 && index <= expectedNode.Scripts.Count && expectedNode.Scripts[index - 1].LineId == spec.SourceId, "Independent copy position no longer follows its original source.");
            // This row already came from a separately verified complete project
            // clone. Its full content is the expected copy; no resetting Create,
            // Patch, Read reconstruction or constructor defines its contents.
            var copy = sourceNode.Scripts[spec.SourceIndex];
            Require(copy.LineId == spec.SourceId, "Complete clone copy source identity changed.");
            if (!options.CopyVoice) copy.voice = string.Empty;
            int actual = DeleteLineIndex(trialNode, spec.SourceId) + 1;
            Require(actual > 0 && actual == index, "Native source-ID placement differs from independent copy order.");
            int resolutions = 0;
            Il2CppSystem.Func<JToken, ScriptData> resolve = new System.Func<JToken, ScriptData>(token =>
            {
                using var reference = JsonDocument.Parse(token.ToString()); var value = reference.RootElement;
                Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 && value.GetProperty("id").GetString() == spec.SourceId, "Native copy resolver requested an uncaptured source.");
                resolutions++;
                int sourceIndex = DeleteLineIndex(trialNode, spec.SourceId);
                Require(sourceIndex >= 0, "Native copy source disappeared.");
                return trialNode.Scripts[sourceIndex];
            });
            var previous = AuthoringGraph.PreviousFor(trial, trialNode, actual);
            Require(previous != null, "Native copy insertion has no actual previous source context.");
            var created = AuthoringLines.Create(JObject.Parse(spec.LineInputJson), previous!, resolve, before.Identity.CatalogVersion);
            Require(resolutions == 1 && created != null && created.Pointer != copy.Pointer && !MultilineProjectLines(trial).Any(l => l.Pointer == created.Pointer || l.LineId == created.LineId), "Native from-copy did not resolve once or returned an existing line.");
            string generated = NewDuplicateLineId(created!, allIds);
            Require(created!.characters != null && created.characters.Count == 6 && created.characters.Pointer != copy.characters.Pointer &&
                created.characters.Pointer != trialNode.Scripts[actual - 1].characters.Pointer, "Created copy shares a source character container.");
            for (int slot = 0; slot < 6; slot++) Require(created.characters[slot].Pointer != copy.characters[slot].Pointer &&
                created.characters[slot].Pointer != trialNode.Scripts[actual - 1].characters[slot].Pointer, "Created copy shares a source character record.");
            string identity = comparisonIds == null ? generated : comparisonIds[i];
            copy.LineId = created.LineId = identity;
            // voice remains content: retained by default, or cleared only by
            // the explicit owner-bound option. It is never identity-normalized.
            expectedNode.Scripts.Insert(index, copy); trialNode.Scripts.Insert(actual, created); inserted.Add(copy); newIds.Add(identity);
        }
        AuthoringGraph.Normalize(trial); AuthoringGraph.RequireValid(trial, "edit");
        var positions = new List<object>();
        foreach (var raw in expected.nodes)
        {
            var node = raw.TryCast<ScriptNodeData>(); if (node == null) continue;
            for (int index = 0; index < node.Scripts.Count; index++)
            {
                var line = node.Scripts[index]; Script matched = line; matched.MatchWithPrev(AuthoringGraph.PreviousFor(expected, node, index));
                for (int slot = 1; slot <= 5; slot++)
                {
                    int old = line.characters[slot].startingPos, next = matched.characters[slot].startingPos;
                    if (old != next) { int added = inserted.FindIndex(l => l.Pointer == line.Pointer); positions.Add(new { node_id = node.Guid.ToString(), line_id = added < 0 ? line.LineId : null, line_alias = added < 0 ? null : plan.Copies[added].Alias, slot, before = old, after = next }); }
                    line.characters[slot].startingPos = next;
                }
            }
        }
        string beforePreview = AuthoringEditorSession.Fingerprint(expected); var shadow = ProjectFileStore.Clone(expected);
        Require(shadow != null && shadow.Pointer != expected.Pointer && shadow.Pointer != trial.Pointer && AuthoringEditorSession.Fingerprint(shadow) == beforePreview, "Copy preview shadow is aliased or lossy.");
        AuthoringProjectPreview.Refresh(shadow!);
        Require(AuthoringEditorSession.Fingerprint(expected) == beforePreview, "Preview derivation modified its copy baseline.");
        expected.PreviewBgName = shadow!.PreviewBgName; expected.PreviewHeader = shadow.PreviewHeader; expected.PreviewTitle = shadow.PreviewTitle;
        string fingerprint = AuthoringEditorSession.Fingerprint(expected);
        Require(AuthoringEditorSession.Fingerprint(trial) == fingerprint,
            "Native copy cannot preserve this row completely (including unused/offstage residue, curve and stored scene fields), or Normalize changed undeclared fields.");
        var copied = new List<object>();
        for (int i = 0; i < inserted.Count; i++)
        {
            var line = inserted[i]; var read = MultilineRead(line, expectedNode, newIds[i]);
            var state = read.EnumerateObject().Where(p => p.Name != "line_id").ToDictionary(p => p.Name, p => p.Value.Clone());
            copied.Add(new { alias = plan.Copies[i].Alias, source_id = plan.Copies[i].SourceId, state, selection_group = line.selectionGroup, background_name = line.bgFriendlyName });
        }
        using var sourceDocument = JsonDocument.Parse(sourceJson);
        string preview = JsonSerializer.Serialize(new { sources = sourceDocument.RootElement, copied, starting_positions = positions,
            project_preview = new { before = new { background = before.Project.PreviewBgName, header = before.Project.PreviewHeader, title = before.Project.PreviewTitle }, after = new { background = expected.PreviewBgName, header = expected.PreviewHeader, title = expected.PreviewTitle } } });
        Require(AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint && AuthoringEditorSession.Fingerprint(session.Snapshot) == before.Fingerprint, "Detached full-copy preflight changed an original snapshot.");
        return new(fingerprint, preview, newIds.ToArray());
    }

    private static bool IsDuplicateApplied(State before, State current, CapturedDuplicate captured, string? receiptJson)
    {
        var plan = captured.Preview.Plan;
        if (current.Lines.Length != before.Lines.Length + plan.OperationCount) return false;
        var added = new HashSet<int>(plan.Copies.Select(c => c.TargetIndex));
        if (!current.Lines.Where((_, i) => !added.Contains(i)).SequenceEqual(before.Lines)) return false;
        var ids = new HashSet<string>(MultilineProjectLines(before.Project).Select(l => l.LineId), StringComparer.Ordinal);
        var node = FindNode(current.Project, before.Identity.NodeId); var actualIds = new List<string>();
        foreach (var copy in plan.Copies) actualIds.Add(NewDuplicateLineId(node.Scripts[copy.TargetIndex], ids));
        if (!VerifyDuplicateReceipt(receiptJson, plan, actualIds, current.Identity)) return false;
        var comparable = ProjectFileStore.Clone(current.Project);
        Require(comparable != null && comparable.Pointer != current.Project.Pointer && AuthoringEditorSession.Fingerprint(comparable) == current.Fingerprint, "Copy result comparison clone is aliased or lossy.");
        var clonedNode = FindNode(comparable!, before.Identity.NodeId);
        Require(clonedNode.Pointer != node.Pointer && clonedNode.Scripts.Pointer != node.Scripts.Pointer, "Copy comparison node is aliased.");
        for (int i = 0; i < plan.Copies.Count; i++)
        {
            int index = plan.Copies[i].TargetIndex;
            Require(clonedNode.Scripts[index].Pointer != node.Scripts[index].Pointer, "Copy result comparison row is aliased.");
            clonedNode.Scripts[index].LineId = captured.Projection.NewLineIds[i];
        }
        return AuthoringEditorSession.Fingerprint(comparable!) == captured.Projection.Fingerprint && AuthoringEditorSession.Fingerprint(current.Project) == current.Fingerprint &&
            AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint;
    }

    private static bool VerifyDuplicateReceipt(string? receiptJson, AuthoringBatchDuplicatePlan plan, IReadOnlyList<string> actual, CardPackContextIdentity identity)
    {
        if (receiptJson == null) return false;
        using var document = JsonDocument.Parse(receiptJson); var root = document.RootElement;
        if (root.GetProperty("api_version").GetString() != "1" || root.GetProperty("ok").ValueKind != JsonValueKind.True) return false;
        var context = root.GetProperty("context"); var data = root.GetProperty("data"); var created = data.GetProperty("created");
        foreach (var obj in new[] { root, context, data, created }) if (obj.ValueKind != JsonValueKind.Object || obj.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != obj.EnumerateObject().Count()) return false;
        if (context.GetProperty("project_id").GetString() != identity.ProjectId || context.GetProperty("edit_session").GetString() != identity.EditSession || context.GetProperty("revision").GetString() != identity.Revision ||
            data.GetProperty("applied_operations").GetInt32() != plan.OperationCount || created.EnumerateObject().Count() != plan.OperationCount) return false;
        for (int i = 0; i < plan.Copies.Count; i++) if (!created.TryGetProperty(plan.Copies[i].Alias, out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != actual[i]) return false;
        var nodes = data.GetProperty("changed_node_ids").EnumerateArray().Select(v => v.GetString()).ToArray();
        var lines = data.GetProperty("changed_line_ids").EnumerateArray().Select(v => v.GetString()).ToArray();
        return nodes.Length == 1 && nodes[0] == plan.Selection.NodeId && lines.Length == actual.Count && new HashSet<string?>(lines, StringComparer.Ordinal).SetEquals(actual);
    }
}
