using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Azurite;

[Flags]
internal enum AuthoringCopyOutstandingProof
{
    SourceStateCoverage = 1,
    EffectiveCatalog = 2,
    NativeRoundTrip = 4,
    TransactionAndEditor = 8
}

internal sealed record AuthoringCopyResourceReference(string Kind, string ResourceId, string Path, string? FaceId);
internal sealed record AuthoringCopyProjectionDiagnostic(string Code, string Path, string Message);

/// <summary>A writable candidate, never a complete clone, native receipt, or permission to Apply.</summary>
internal sealed record AuthoringCardPackCopyProjectionResult(
    JsonElement SourceRead,
    JsonElement LineInput,
    IReadOnlyList<AuthoringCopyResourceReference> RequiredResources,
    AuthoringCopyOutstandingProof OutstandingProofs);

/// <summary>Pure projection of the pinned AuthoringLines.Read shape. No native calls or transaction owner.</summary>
internal static class AuthoringCardPackHostCopyProjection
{
    private static readonly string[] ReadFields = { "line_id", "node_id", "text", "mode", "speaker", "stage", "highlighted_slots", "place", "extra_script", "scene" };
    private static readonly string[] StageFields = { "slot", "resource_id", "face_id", "starting_position", "ending_position", "display_order", "action", "emoticon", "effect", "appear", "shape_overrides", "curve" };
    private static readonly string[] SceneFields = { "background", "background_effect", "transition", "bgm", "sound", "popup", "voice" };
    private static readonly string[] Actions = { "Idle", "Greeting", "FalldownL", "FalldownR", "Stiff", "Shake", "Jump", "Hophop", "Curve" };
    private static readonly string[] Emoticons = { "Angry", "Chat", "Dot", "Exclaim", "Heart", "Music", "Question", "Respond", "Shy", "Surprise", "Sweat", "Twinkle", "Upset", "Think", "Bulb", "Sad", "Sigh", "Steam", "Tear", "Zzz", "None" };

    /// <summary>
    /// Success means all represented writable values were retained. Read omits
    /// selectionGroup, bgFriendlyName and details of unoccupied/offstage records;
    /// inherit-from is NOT proof that all of those survive Create/Patch/Normalize.
    /// The caller still needs source-state coverage, fresh schema/catalog/face
    /// checks, native curve validation and detached round-trip proof, then the
    /// existing transaction owner and actual Editor post-state proof. No caller
    /// may turn this candidate alone into a complete-copy or ready-to-apply claim.
    /// </summary>
    internal static bool TryProject(JsonElement nativeRead, string expectedNodeId, string expectedLineId,
        out AuthoringCardPackCopyProjectionResult? result, out AuthoringCopyProjectionDiagnostic? diagnostic)
    {
        result = null;
        diagnostic = null;
        try
        {
            Fields(nativeRead, "$", ReadFields);
            NoDuplicateProperties(nativeRead);
            Require(!string.IsNullOrWhiteSpace(expectedNodeId), "InvalidIdentity", "$.node_id", "An existing source node ID is required.");
            Require(!string.IsNullOrWhiteSpace(expectedLineId), "InvalidIdentity", "$.line_id", "An existing persistent source line ID is required.");
            Require(Text(nativeRead.GetProperty("node_id"), "$.node_id", true) == expectedNodeId,
                "IdentityMismatch", "$.node_id", "Read belongs to a different source node.");
            Require(Text(nativeRead.GetProperty("line_id"), "$.line_id", true) == expectedLineId,
                "IdentityMismatch", "$.line_id", "Read belongs to a different source line.");

            var resources = new List<AuthoringCopyResourceReference>();
            string mode = Choice(nativeRead.GetProperty("mode"), "$.mode", "dialogue", "narration", "control");
            foreach (string field in new[] { "text", "place", "extra_script" }) Text(nativeRead.GetProperty(field), "$." + field, false);
            var stage = nativeRead.GetProperty("stage");
            Array(stage, "$.stage", 5);
            var slots = new HashSet<int>();
            int index = 0;
            foreach (var actor in stage.EnumerateArray())
            {
                string path = "$.stage[" + index++ + "]";
                Fields(actor, path, StageFields);
                int slot = Integer(actor.GetProperty("slot"), path + ".slot", 1, 5);
                Require(slots.Add(slot), "DuplicateSlot", path + ".slot", "Stage slots must be unique.");
                string id = Text(actor.GetProperty("resource_id"), path + ".resource_id", true);
                string face = Text(actor.GetProperty("face_id"), path + ".face_id", true);
                // Read-only starting_position is retained in SourceRead, never
                // written. It must be an integer but no writable range is assumed.
                Integer(actor.GetProperty("starting_position"), path + ".starting_position", int.MinValue, int.MaxValue);
                Integer(actor.GetProperty("ending_position"), path + ".ending_position", 1, 5);
                Integer(actor.GetProperty("display_order"), path + ".display_order", 1, 5);
                string action = Choice(actor.GetProperty("action"), path + ".action", Actions);
                Choice(actor.GetProperty("emoticon"), path + ".emoticon", Emoticons);
                Choice(actor.GetProperty("effect"), path + ".effect", "None", "Shot");
                Choice(actor.GetProperty("appear"), path + ".appear", "None", "AL", "AR", "A", "DL", "DR", "D");
                var curve = actor.GetProperty("curve");
                Require(action == "Curve" ? curve.ValueKind == JsonValueKind.Object : curve.ValueKind == JsonValueKind.Null,
                    "CurveActionMismatch", path + ".curve", "Curve requires an object; other actions require null. Curve contents still require native validation.");
                var shapes = actor.GetProperty("shape_overrides");
                Array(shapes, path + ".shape_overrides", 3);
                var shapeNames = new HashSet<string>(StringComparer.Ordinal);
                int shapeIndex = 0;
                foreach (var shape in shapes.EnumerateArray())
                {
                    string shapePath = path + ".shape_overrides[" + shapeIndex++ + "]";
                    Require(shapeNames.Add(Choice(shape, shapePath, "Sig", "Black", "Closeup")),
                        "DuplicateValue", shapePath, "Shape overrides must be unique.");
                }
                resources.Add(new("character", id, path, face));
            }

            var speaker = nativeRead.GetProperty("speaker");
            if (mode != "dialogue")
                Require(speaker.ValueKind == JsonValueKind.Null, "SpeakerModeMismatch", "$.speaker", "Narration and control require null speaker; clearing existing speaker state would lose content.");
            else
            {
                Require(speaker.ValueKind != JsonValueKind.Null, "SpeakerModeMismatch", "$.speaker", "Dialogue requires an explicit speaker.");
                Fields(speaker, "$.speaker", new[] { "slot", "offstage_resource_id" }, requireAll: false);
                bool hasSlot = speaker.TryGetProperty("slot", out var speakerSlot);
                bool hasOffstage = speaker.TryGetProperty("offstage_resource_id", out var offstage);
                Require(hasSlot != hasOffstage, "InvalidSpeaker", "$.speaker", "Speaker must contain exactly one of slot or offstage_resource_id.");
                if (hasSlot)
                    Require(slots.Contains(Integer(speakerSlot, "$.speaker.slot", 1, 5)), "UnoccupiedSpeaker", "$.speaker.slot", "Speaker must occupy the represented stage slot.");
                else resources.Add(new("character", Text(offstage, "$.speaker.offstage_resource_id", true), "$.speaker", null));
            }

            var highlights = nativeRead.GetProperty("highlighted_slots");
            Array(highlights, "$.highlighted_slots", 5);
            var highlighted = new HashSet<int>();
            index = 0;
            foreach (var value in highlights.EnumerateArray())
            {
                string path = "$.highlighted_slots[" + index++ + "]";
                Require(highlighted.Add(Integer(value, path, 1, 5)), "DuplicateValue", path, "Highlighted slots must be unique.");
            }

            var scene = nativeRead.GetProperty("scene");
            Fields(scene, "$.scene", SceneFields);
            foreach (string family in SceneFields)
            {
                string path = "$.scene." + family;
                var entry = scene.GetProperty(family);
                Fields(entry, path, new[] { "action", "resource_id" }, requireAll: false);
                Require(entry.TryGetProperty("action", out var actionValue), "MissingField", path + ".action", "A scene action is required.");
                string action = Choice(actionValue, path + ".action", "set", family is "background" or "bgm" ? "inherit" : "none");
                bool hasId = entry.TryGetProperty("resource_id", out var id);
                if (action == "set")
                {
                    Require(hasId, "MissingField", path + ".resource_id", "Set requires a copied catalog ID.");
                    resources.Add(new(family, Text(id, path + ".resource_id", true), path, null));
                }
                else Require(!hasId, "UnexpectedResource", path + ".resource_id", "An empty scene action must not carry a discarded resource ID.");
            }

            result = new(nativeRead.Clone(), WriteLineInput(nativeRead, expectedLineId), resources.AsReadOnly(),
                AuthoringCopyOutstandingProof.SourceStateCoverage | AuthoringCopyOutstandingProof.EffectiveCatalog |
                AuthoringCopyOutstandingProof.NativeRoundTrip | AuthoringCopyOutstandingProof.TransactionAndEditor);
            return true;
        }
        catch (ProjectionFailure failure)
        {
            diagnostic = failure.Diagnostic;
            return false;
        }
    }

    private static JsonElement WriteLineInput(JsonElement input, string lineId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("inherit"); writer.WriteStartObject();
            writer.WritePropertyName("from"); writer.WriteStartObject();
            writer.WriteString("id", lineId); writer.WriteEndObject(); writer.WriteEndObject();
            foreach (var property in input.EnumerateObject())
            {
                if (property.Name is "line_id" or "node_id") continue;
                writer.WritePropertyName(property.Name);
                if (property.Name != "stage") writer.WriteRawValue(property.Value.GetRawText());
                else
                {
                    writer.WriteStartArray();
                    foreach (var actor in property.Value.EnumerateArray())
                    {
                        writer.WriteStartObject();
                        foreach (var field in actor.EnumerateObject())
                        {
                            if (field.Name == "starting_position") continue;
                            writer.WritePropertyName(field.Name);
                            // Preserve open curve data and numeric lexemes in full;
                            // filtering tracks/extensions would be a lossy copy.
                            writer.WriteRawValue(field.Value.GetRawText());
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void Fields(JsonElement value, string path, string[] allowed, bool requireAll = true)
    {
        Require(value.ValueKind == JsonValueKind.Object, "InvalidObject", path, "Expected an object.");
        foreach (var property in value.EnumerateObject())
            Require(System.Array.IndexOf(allowed, property.Name) >= 0, "UnknownField", path + "." + property.Name, "Unmapped content cannot be discarded during copying.");
        if (requireAll)
            foreach (string field in allowed)
                Require(value.TryGetProperty(field, out _), "MissingField", path + "." + field, "Native Read must provide the complete field; no default is inferred.");
    }

    private static void NoDuplicateProperties(JsonElement input)
    {
        var pending = new Stack<(JsonElement Value, string Path)>();
        pending.Push((input, "$"));
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item.Value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in item.Value.EnumerateObject())
                {
                    string path = item.Path + "." + property.Name;
                    Require(names.Add(property.Name), "DuplicateField", path, "Duplicate JSON properties are ambiguous, including inside curve extensions.");
                    pending.Push((property.Value, path));
                }
            }
            else if (item.Value.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var child in item.Value.EnumerateArray()) pending.Push((child, item.Path + "[" + index++ + "]"));
            }
        }
    }

    private static void Array(JsonElement value, string path, int maximum)
        => Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= maximum, "InvalidArray", path, "Expected a bounded array.");

    private static string Text(JsonElement value, string path, bool nonempty)
    {
        Require(value.ValueKind == JsonValueKind.String, "InvalidString", path, "Expected a string without coercion.");
        string text = value.GetString()!;
        Require(!nonempty || !string.IsNullOrWhiteSpace(text), "InvalidString", path, "An explicit nonempty ID is required.");
        return text;
    }

    private static string Choice(JsonElement value, string path, params string[] choices)
    {
        string text = Text(value, path, false);
        Require(System.Array.IndexOf(choices, text) >= 0, "UnsupportedValue", path, "Value is not supported by the pinned native contract.");
        return text;
    }

    private static int Integer(JsonElement value, string path, int minimum, int maximum)
    {
        int number = 0;
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number) && number >= minimum && number <= maximum,
            "InvalidInteger", path, "Expected an integer in the native field range.");
        return number;
    }

    private static void Require(bool condition, string code, string path, string message)
    {
        if (!condition) throw new ProjectionFailure(new(code, path, message));
    }

    private sealed class ProjectionFailure : Exception
    {
        internal AuthoringCopyProjectionDiagnostic Diagnostic { get; }
        internal ProjectionFailure(AuthoringCopyProjectionDiagnostic diagnostic) : base(diagnostic.Message) => Diagnostic = diagnostic;
    }
}
