using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Azurite.Core;
using AzureArchive.Automation;
using Newtonsoft.Json.Linq;

namespace Azurite;

internal sealed partial class AuthoringCardPackHost
{
    private sealed record MultilineIdentity(string LineId, string Voice);
    private sealed record MultilineProjection(string Fingerprint, string PreviewJson, string Resources, MultilineIdentity[] Identities);
    private sealed record CapturedMultiline(State State, AuthoringBatchMultilineHostPreview Preview, MultilineProjection Projection);
    private CapturedMultiline? _capturedMultiline;

    public AuthoringBatchMultilineHostPreview CaptureMultiline(string text, AuthoringBatchMultilineOptions? options = null)
    {
        EnterBoundary();
        try
        {
            ClearCaptures();
            options ??= new();
            ValidateMultilineOptions(options);
            var before = ReadState(true, out var session);
            var selection = ReadBatchSelection(before);
            var anchors = ReadMultilineAnchors(before, selection, options);
            var compiled = AuthoringBatchMultilineCompiler.TryCompile(selection, text, anchors);
            Require(compiled.IsValid && compiled.Plan != null, compiled.Message);
            var projection = ProjectMultiline(before, session, compiled.Plan!, null);
            Require(AuthoringBatchPlanner.Matches(selection, ReadBatchSelection(before)) && anchors.SequenceEqual(ReadMultilineAnchors(before, selection, options)), "Multiline anchors changed during preflight.");
            Require(SameState(before, ReadState(false, out _)), "Native state changed during multiline preflight.");
            var preview = new AuthoringBatchMultilineHostPreview(compiled.Plan!, before.Identity, SubmissionVersion(before.Identity.EditSession), options, projection.PreviewJson);
            _capturedMultiline = new(before, preview, projection);
            return preview;
        }
        finally { ExitBoundary(); }
    }

    public CardPackV2CommitResult CommitMultiline(AuthoringBatchMultilineHostPreview preview)
        => Submit(null, null, null, preview?.Plan?.Selection.Revision ?? -1, multiline: preview);

    private JArray PrepareMultiline(State before, AuthoringEditorSession session, CapturedMultiline captured)
    {
        var selection = ReadBatchSelection(before);
        var plan = captured.Preview.Plan;
        Require(AuthoringBatchPlanner.Matches(plan.Selection, selection), "Multiline selected rows differ from preview.");
        var anchors = ReadMultilineAnchors(before, selection, captured.Preview.Options);
        var compiled = AuthoringBatchMultilineCompiler.TryCompile(selection, plan.SourceText, anchors);
        Require(compiled.IsValid && compiled.Plan != null && compiled.Plan.OperationsJson == plan.OperationsJson && compiled.Plan.Lines.SequenceEqual(plan.Lines), "Multiline mode, speaker or insertion chain differs from preview.");
        var projection = ProjectMultiline(before, session, plan, captured.Projection.Identities);
        Require(projection.Fingerprint == captured.Projection.Fingerprint && projection.PreviewJson == captured.Projection.PreviewJson && projection.Resources == captured.Projection.Resources,
            "Multiline inherited/derived state or resource binding differs from preview.");
        var native = JArray.Parse(plan.OperationsJson);
        Require(native.Count == plan.OperationCount, "Native multiline parser count mismatch.");
        return native;
    }

    private static void ValidateMultilineOptions(AuthoringBatchMultilineOptions options)
    {
        bool valid = options.Mode is AuthoringBatchMultilineMode.PerAnchorReadable or AuthoringBatchMultilineMode.Narration
            ? options.SpeakerSlot == null && options.OffstageResourceId == null
            : options.Mode == AuthoringBatchMultilineMode.Dialogue && (options.SpeakerSlot is >= 1 and <= 5 && options.OffstageResourceId == null ||
                options.SpeakerSlot == null && !string.IsNullOrWhiteSpace(options.OffstageResourceId));
        Require(valid, "New-line mode requires unambiguous narration or exactly one explicit dialogue speaker.");
    }

    private static AuthoringBatchMultilineAnchor[] ReadMultilineAnchors(State state, AuthoringBatchSelection selection, AuthoringBatchMultilineOptions options)
    {
        ValidateMultilineOptions(options);
        var node = FindNode(state.Project, state.Identity.NodeId);
        var anchors = new List<AuthoringBatchMultilineAnchor>();
        foreach (var row in selection.Rows)
        {
            var line = node.Scripts[row.Index];
            var read = MultilineRead(line, node, row.StableId);
            string sourceMode = SpeakerString(read, "mode");
            string mode = options.Mode == AuthoringBatchMultilineMode.PerAnchorReadable ? sourceMode
                : options.Mode == AuthoringBatchMultilineMode.Narration ? "narration" : "dialogue";
            Require(mode is "narration" or "dialogue", "Control anchors require an explicit narration or dialogue new-line mode.");
            int? slot = null; string? offstage = null;
            if (mode == "dialogue")
            {
                if (options.Mode == AuthoringBatchMultilineMode.Dialogue) { slot = options.SpeakerSlot; offstage = options.OffstageResourceId; }
                else
                {
                    var speaker = read.GetProperty("speaker");
                    Require(speaker.ValueKind == JsonValueKind.Object && speaker.EnumerateObject().Count() == 1, "Readable anchor speaker is missing or ambiguous.");
                    if (speaker.TryGetProperty("slot", out var nativeSlot))
                    {
                        slot = nativeSlot.GetInt32();
                        Require(slot is >= 1 and <= 5 && line.speakerSlotNum == slot, "Anchor speaker projection differs from its snapshot.");
                    }
                    else
                    {
                        offstage = SpeakerString(speaker, "offstage_resource_id");
                        Require(line.speakerSlotNum == 0 && !string.IsNullOrEmpty(line.characters[0].name), "Anchor offstage identity is unavailable.");
                        var resource = ResolveMultilineResource(offstage, "character", state.Identity.CatalogVersion);
                        Require(SpeakerString(resource, "native_key") == line.characters[0].name, "Anchor offstage resource no longer names the same actor.");
                    }
                }
            }
            else if (options.Mode == AuthoringBatchMultilineMode.PerAnchorReadable)
                Require(read.GetProperty("speaker").ValueKind == JsonValueKind.Null && line.speakerSlotNum == 0 && string.IsNullOrEmpty(line.characters[0].name), "Narration anchor has inconsistent speaker state.");
            anchors.Add(new(row.StableId, mode, slot, offstage));
        }
        return anchors.ToArray();
    }

    private static JsonElement MultilineRead(ScriptData line, ScriptNodeData node, string expectedId)
    {
        Require(line != null && line.LineId == expectedId && line.characters != null && line.characters.Count == 6, "Multiline source identity or complete character layout changed.");
        using var document = JsonDocument.Parse(AuthoringLines.Read(line!, node.Guid).ToString());
        var read = document.RootElement;
        Require(SpeakerString(read, "line_id") == expectedId && SpeakerString(read, "node_id") == node.Guid.ToString() && read.GetProperty("text").GetString() == line.text, "Native Read identity/text differs from the complete snapshot.");
        string actualMode = !line.isDialogScript ? "control" : line.speakerSlotNum > 0 || !string.IsNullOrEmpty(line.characters[0].name) ? "dialogue" : "narration";
        Require(SpeakerString(read, "mode") == actualMode, "Native Read mode differs from the complete snapshot.");
        return read.Clone();
    }

    private static JsonElement ResolveMultilineResource(string id, string kind, string catalog)
    {
        var resolved = AuthoringResourceCatalog.Resolve(id, kind, catalog, true);
        Require(resolved != null, "Multiline resource Resolve returned no result.");
        using var document = JsonDocument.Parse(resolved!.ToString());
        var value = document.RootElement;
        Require(SpeakerString(value, "resource_id") == id && SpeakerString(value, "kind") == kind && SpeakerString(value, "catalog_version") == catalog &&
            value.GetProperty("available").ValueKind == JsonValueKind.True, "Multiline resource identity/catalog/availability changed.");
        return value.Clone();
    }

    private static string MultilineResources(ScriptData line, JsonElement read, string catalog)
    {
        var evidence = new List<JsonElement>();
        var slots = new HashSet<int>();
        var stage = read.GetProperty("stage");
        Require(stage.ValueKind == JsonValueKind.Array && stage.GetArrayLength() <= 5, "Inherited stage projection is invalid.");
        foreach (var actor in stage.EnumerateArray())
        {
            int slot = actor.GetProperty("slot").GetInt32();
            Require(slot is >= 1 and <= 5 && slots.Add(slot), "Inherited stage slot is invalid or duplicated.");
            var resource = ResolveMultilineResource(SpeakerString(actor, "resource_id"), "character", catalog);
            var capabilities = resource.GetProperty("capabilities");
            string face = SpeakerString(actor, "face_id");
            Require(SpeakerString(resource, "native_key") == line.characters[slot].name && face == line.characters[slot].faceId &&
                capabilities.GetProperty("stage").ValueKind == JsonValueKind.True && capabilities.GetProperty("faces_loaded").ValueKind == JsonValueKind.True &&
                !capabilities.TryGetProperty("load_error", out _) && capabilities.GetProperty("faces").EnumerateArray().Any(f => f.GetString() == face), "Inherited actor/face binding is unavailable.");
            evidence.Add(resource);
        }
        for (int slot = 1; slot <= 5; slot++) Require(slots.Contains(slot) == !string.IsNullOrEmpty(line.characters[slot].name), "Inherited stage Read omits or invents an occupied actor.");
        if (line.speakerSlotNum > 0) Require(slots.Contains(line.speakerSlotNum), "The requested speaker departed the inherited stage; choose another explicit new-line mode/speaker.");
        if (!string.IsNullOrEmpty(line.characters[0].name))
        {
            var resource = ResolveMultilineResource(SpeakerString(read.GetProperty("speaker"), "offstage_resource_id"), "character", catalog);
            Require(SpeakerString(resource, "native_key") == line.characters[0].name && resource.GetProperty("capabilities").GetProperty("offstage").ValueKind == JsonValueKind.True, "Offstage speaker is unavailable.");
            evidence.Add(resource);
        }
        var scene = read.GetProperty("scene");
        foreach (string kind in new[] { "background", "background_effect", "transition", "bgm", "sound", "popup" })
        {
            var value = scene.GetProperty(kind);
            string key = kind switch { "background" => line.bgName.ToString(CultureInfo.InvariantCulture), "background_effect" => line.bgEffect.ToString(CultureInfo.InvariantCulture),
                "transition" => line.transition.ToString(CultureInfo.InvariantCulture), "bgm" => line.bgmId.ToString(CultureInfo.InvariantCulture), "sound" => line.sound, _ => line.popup };
            bool set = !string.IsNullOrEmpty(key) && (kind is "sound" or "popup" || key != "0");
            Require(SpeakerString(value, "action") == (set ? "set" : kind is "background" or "bgm" ? "inherit" : "none"), "Inherited scene action differs from the full snapshot.");
            if (!set) continue;
            var resource = ResolveMultilineResource(SpeakerString(value, "resource_id"), kind, catalog);
            Require(SpeakerString(resource, "native_key") == key, "Inherited scene resource maps to another native value.");
            evidence.Add(resource);
        }
        // voice is a fresh native identity, not a selected existing audio resource.
        // It is validated separately and never resolved or replayed as source audio.
        return JsonSerializer.Serialize(evidence);
    }

    private static IEnumerable<ScriptData> MultilineProjectLines(ProjectData project)
    {
        foreach (var raw in project.nodes) { var node = raw.TryCast<ScriptNodeData>(); if (node != null) foreach (var line in node.Scripts) yield return line; }
    }

    private static MultilineIdentity NewMultilineIdentity(ScriptData line, HashSet<string> ids, HashSet<string> voices)
    {
        // The pinned constructors/Apply produce independent nonempty D-format
        // GUIDs. Existing project IDs remain opaque and are never reformatted.
        Require(Guid.TryParseExact(line.LineId, "D", out var id) && id != Guid.Empty && id.ToString("D") == line.LineId &&
            Guid.TryParseExact(line.voice, "D", out var voice) && voice != Guid.Empty && voice.ToString("D") == line.voice &&
            line.LineId != line.voice && ids.Add(line.LineId) && voices.Add(line.voice), "Inserted line/voice identities must match native generation and be distinct, new and unique across the entire project.");
        return new(line.LineId, line.voice);
    }

    private static MultilineProjection ProjectMultiline(State before, AuthoringEditorSession session, AuthoringBatchMultilinePlan plan, MultilineIdentity[]? comparisonIdentities)
    {
        var expected = ProjectFileStore.Clone(before.Project); var trial = ProjectFileStore.Clone(before.Project);
        RequireDeleteCloneSeparation(before, session, expected, trial, new HashSet<string>(StringComparer.Ordinal));
        var expectedNode = FindNode(expected, before.Identity.NodeId); var trialNode = FindNode(trial, before.Identity.NodeId);
        var original = MultilineProjectLines(before.Project).ToArray();
        var idsA = new HashSet<string>(original.Select(l => l.LineId), StringComparer.Ordinal); var idsB = new HashSet<string>(idsA, StringComparer.Ordinal);
        var voiceA = new HashSet<string>(original.Select(l => l.voice), StringComparer.Ordinal); var voiceB = new HashSet<string>(voiceA, StringComparer.Ordinal);
        var identities = new List<MultilineIdentity>(); var inserted = new List<ScriptData>(); var resources = new List<string>();
        using var operations = JsonDocument.Parse(plan.OperationsJson);
        string? lastTrialId = null;
        for (int i = 0; i < plan.Lines.Count; i++)
        {
            var spec = plan.Lines[i]; int index = spec.TargetIndex;
            Require(index > 0 && index <= expectedNode.Scripts.Count, "Expected multiline index is invalid.");
            if (spec.LineOffset == 0) Require(expectedNode.Scripts[index - 1].LineId == spec.AnchorId, "Expected insertion lost its original anchor.");
            Script previous = expectedNode.Scripts[index - 1];
            var fresh = new ScriptData(new Script(previous, true));
            fresh.characters[0] = new ScriptData.CharacterRecordData(new Script.CharacterRecord());
            fresh.speakerSlotNum = spec.SpeakerSlot ?? 0;
            if (spec.OffstageResourceId != null) fresh.characters[0].name = SpeakerString(ResolveMultilineResource(spec.OffstageResourceId, "character", before.Identity.CatalogVersion), "native_key");
            fresh.isDialogScript = true; fresh.text = spec.Text;
            Require(!MultilineProjectLines(expected).Any(l => l.LineId == fresh.LineId || l.voice == fresh.voice), "Expected constructor reused an identity already present in its project.");
            var generated = NewMultilineIdentity(fresh, idsA, voiceA);

            int actualIndex = DeleteLineIndex(trialNode, spec.LineOffset == 0 ? spec.AnchorId : lastTrialId!) + 1;
            Require(actualIndex > 0 && actualIndex == index, "Trial native ref-chain interleaving differs from expected original indices.");
            var actualPrevious = AuthoringGraph.PreviousFor(trial, trialNode, actualIndex);
            Require(actualPrevious != null, "Native previous insertion context is unavailable.");
            var created = AuthoringLines.Create(JObject.Parse(operations.RootElement[i].GetProperty("line").GetRawText()), actualPrevious!, null!, before.Identity.CatalogVersion);
            Require(created != null && created.Pointer != fresh.Pointer && !MultilineProjectLines(trial).Any(l => l.Pointer == created.Pointer || l.LineId == created.LineId || l.voice == created.voice), "Native Create returned an aliased existing line or identity.");
            NewMultilineIdentity(created!, idsB, voiceB);
            Require(fresh.characters.Pointer != created!.characters.Pointer && fresh.characters.Pointer != expectedNode.Scripts[index - 1].characters.Pointer &&
                created.characters.Pointer != trialNode.Scripts[actualIndex - 1].characters.Pointer, "Native inheritance returned aliased character containers.");
            for (int slot = 0; slot < 6; slot++) Require(fresh.characters[slot].Pointer != created.characters[slot].Pointer &&
                fresh.characters[slot].Pointer != expectedNode.Scripts[index - 1].characters[slot].Pointer && created.characters[slot].Pointer != trialNode.Scripts[actualIndex - 1].characters[slot].Pointer, "Native inheritance returned aliased character records.");
            // Only these newly constructed, verified identity fields are aligned
            // in detached proof projects; no payload or existing row ID is rewritten.
            var comparison = comparisonIdentities == null ? generated : comparisonIdentities[i];
            fresh.LineId = created.LineId = comparison.LineId; fresh.voice = created.voice = comparison.Voice;
            identities.Add(comparison); expectedNode.Scripts.Insert(index, fresh); trialNode.Scripts.Insert(actualIndex, created); lastTrialId = created.LineId; inserted.Add(fresh);
        }
        AuthoringGraph.Normalize(trial); AuthoringGraph.RequireValid(trial, "edit");
        // Independently derive only the established Normalize position outputs.
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
                    if (old != next) { int added = inserted.FindIndex(l => l.Pointer == line.Pointer); positions.Add(new { node_id = node.Guid.ToString(), line_id = added < 0 ? line.LineId : null, line_alias = added < 0 ? null : plan.Lines[added].Alias, slot, before = old, after = next }); }
                    line.characters[slot].startingPos = next;
                }
            }
        }
        string beforePreview = AuthoringEditorSession.Fingerprint(expected);
        var previewShadow = ProjectFileStore.Clone(expected);
        Require(previewShadow != null && previewShadow.Pointer != expected.Pointer && previewShadow.Pointer != trial.Pointer && AuthoringEditorSession.Fingerprint(previewShadow) == beforePreview, "Preview derivation clone is aliased or lossy.");
        AuthoringProjectPreview.Refresh(previewShadow!);
        Require(AuthoringEditorSession.Fingerprint(expected) == beforePreview, "Preview derivation changed its baseline.");
        expected.PreviewBgName = previewShadow!.PreviewBgName; expected.PreviewHeader = previewShadow.PreviewHeader; expected.PreviewTitle = previewShadow.PreviewTitle;
        string fingerprint = AuthoringEditorSession.Fingerprint(expected);
        Require(AuthoringEditorSession.Fingerprint(trial) == fingerprint, "Native Create/Normalize changed fields beyond independent multiline inheritance and declared changes.");
        var displayed = new List<object>();
        for (int i = 0; i < inserted.Count; i++)
        {
            var line = inserted[i]; var read = MultilineRead(line, expectedNode, identities[i].LineId);
            resources.Add(MultilineResources(line, read, before.Identity.CatalogVersion));
            var state = read.EnumerateObject().Where(p => p.Name is not ("line_id" or "scene")).ToDictionary(p => p.Name, p => (object)p.Value.Clone());
            var scene = read.GetProperty("scene").EnumerateObject().Where(p => p.Name != "voice").ToDictionary(p => p.Name, p => (object)p.Value.Clone());
            scene.Add("voice", new { identity = "new_on_apply", source_audio_replayed = false }); state.Add("scene", scene);
            displayed.Add(new { alias = plan.Lines[i].Alias, anchor_id = plan.Lines[i].AnchorId, state, selection_group = line.selectionGroup, background_name = line.bgFriendlyName });
        }
        var sourceNode = FindNode(before.Project, before.Identity.NodeId);
        var sources = plan.Selection.Rows.Select(row => MultilineRead(sourceNode.Scripts[row.Index], sourceNode, row.StableId)).ToArray();
        string preview = JsonSerializer.Serialize(new { sources, inserted = displayed, starting_positions = positions,
            project_preview = new { before = new { background = before.Project.PreviewBgName, header = before.Project.PreviewHeader, title = before.Project.PreviewTitle }, after = new { background = expected.PreviewBgName, header = expected.PreviewHeader, title = expected.PreviewTitle } } });
        Require(AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint && AuthoringEditorSession.Fingerprint(session.Snapshot) == before.Fingerprint,
            "Detached multiline preflight changed an original snapshot.");
        return new(fingerprint, preview, JsonSerializer.Serialize(resources), identities.ToArray());
    }

    private static bool IsMultilineApplied(State before, State current, CapturedMultiline captured, string? receiptJson)
    {
        var plan = captured.Preview.Plan;
        if (current.Lines.Length != before.Lines.Length + plan.OperationCount) return false;
        var newIndices = new HashSet<int>(plan.Lines.Select(l => l.TargetIndex));
        if (!current.Lines.Where((_, i) => !newIndices.Contains(i)).SequenceEqual(before.Lines)) return false;
        var allOld = MultilineProjectLines(before.Project).ToArray();
        var ids = new HashSet<string>(allOld.Select(l => l.LineId), StringComparer.Ordinal); var voices = new HashSet<string>(allOld.Select(l => l.voice), StringComparer.Ordinal);
        var node = FindNode(current.Project, before.Identity.NodeId); var actual = new List<MultilineIdentity>();
        foreach (var spec in plan.Lines) actual.Add(NewMultilineIdentity(node.Scripts[spec.TargetIndex], ids, voices));
        if (!VerifyMultilineReceipt(receiptJson, plan, actual, current.Identity)) return false;
        var comparable = ProjectFileStore.Clone(current.Project);
        Require(comparable != null && comparable.Pointer != current.Project.Pointer && AuthoringEditorSession.Fingerprint(comparable) == current.Fingerprint, "Post-state comparison clone is aliased or lossy.");
        var cloneNode = FindNode(comparable!, before.Identity.NodeId);
        Require(cloneNode.Pointer != node.Pointer && cloneNode.Scripts.Pointer != node.Scripts.Pointer, "Post-state comparison node is aliased.");
        for (int i = 0; i < plan.Lines.Count; i++)
        {
            int index = plan.Lines[i].TargetIndex;
            Require(cloneNode.Scripts[index].Pointer != node.Scripts[index].Pointer, "Post-state comparison line is aliased.");
            cloneNode.Scripts[index].LineId = captured.Projection.Identities[i].LineId;
            cloneNode.Scripts[index].voice = captured.Projection.Identities[i].Voice;
        }
        return AuthoringEditorSession.Fingerprint(comparable!) == captured.Projection.Fingerprint &&
            AuthoringEditorSession.Fingerprint(current.Project) == current.Fingerprint && AuthoringEditorSession.Fingerprint(before.Project) == before.Fingerprint;
    }

    private static bool VerifyMultilineReceipt(string? receiptJson, AuthoringBatchMultilinePlan plan, IReadOnlyList<MultilineIdentity> actual, CardPackContextIdentity identity)
    {
        if (receiptJson == null) return false;
        // Current.Apply wraps Service.Apply.Result in Success/Envelope. Service
        // serializes its Dictionary<string,string> refs as data.created.
        using var document = JsonDocument.Parse(receiptJson);
        var root = document.RootElement;
        if (root.GetProperty("ok").ValueKind != JsonValueKind.True || root.GetProperty("api_version").GetString() != "1") return false;
        var context = root.GetProperty("context");
        if (context.GetProperty("project_id").GetString() != identity.ProjectId || context.GetProperty("edit_session").GetString() != identity.EditSession || context.GetProperty("revision").GetString() != identity.Revision) return false;
        var data = root.GetProperty("data"); var created = data.GetProperty("created");
        if (created.ValueKind != JsonValueKind.Object || data.GetProperty("applied_operations").GetInt32() != plan.OperationCount || created.EnumerateObject().Count() != plan.OperationCount) return false;
        foreach (var obj in new[] { root, context, data, created }) if (obj.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != obj.EnumerateObject().Count()) return false;
        for (int i = 0; i < plan.Lines.Count; i++)
            if (!created.TryGetProperty(plan.Lines[i].Alias, out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != actual[i].LineId) return false;
        var nodes = data.GetProperty("changed_node_ids").EnumerateArray().Select(v => v.GetString()).ToArray();
        var lines = data.GetProperty("changed_line_ids").EnumerateArray().Select(v => v.GetString()).ToArray();
        return nodes.Length == 1 && nodes[0] == plan.Selection.NodeId && lines.Length == actual.Count &&
            new HashSet<string?>(lines, StringComparer.Ordinal).SetEquals(actual.Select(id => id.LineId));
    }
}
