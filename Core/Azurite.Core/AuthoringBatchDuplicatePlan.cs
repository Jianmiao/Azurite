using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Azurite.Core;

public sealed record AuthoringBatchDuplicateInput(string StableId, string LineInputJson);
public sealed record AuthoringBatchCopiedLine(string Alias, string SourceId, int SourceIndex, int TargetIndex, string LineInputJson);

/// <summary>Immutable copy payload; native content/resources and full-state equivalence remain Host obligations.</summary>
public sealed class AuthoringBatchDuplicatePlan
{
    internal AuthoringBatchDuplicatePlan(AuthoringBatchSelection selection, IEnumerable<AuthoringBatchDuplicateInput> inputs,
        IEnumerable<AuthoringBatchCopiedLine> copies, string operationsJson)
    {
        Selection = selection; Inputs = new ReadOnlyCollection<AuthoringBatchDuplicateInput>(inputs.ToArray());
        Copies = new ReadOnlyCollection<AuthoringBatchCopiedLine>(copies.ToArray()); OperationsJson = operationsJson;
    }
    public AuthoringBatchSelection Selection { get; }
    public IReadOnlyList<AuthoringBatchDuplicateInput> Inputs { get; }
    public IReadOnlyList<AuthoringBatchCopiedLine> Copies { get; }
    public string OperationsJson { get; }
    public int OperationCount => Copies.Count;
}

public sealed record AuthoringBatchDuplicatePlanResult(bool IsValid, AuthoringBatchDuplicatePlan? Plan,
    AuthoringBatchNativeFailure Failure, AuthoringBatchFailure LogicalFailure, string Message);

public static class AuthoringBatchDuplicateCompiler
{
    public static AuthoringBatchDuplicatePlanResult TryCompile(AuthoringBatchSelection? selection, IReadOnlyList<AuthoringBatchDuplicateInput>? inputs)
    {
        if (selection != null && selection.Rows.Count > AuthoringBatchPlanner.MaxOperations)
            return Fail(AuthoringBatchFailure.TooManyOperations, "Logical duplicate count exceeds 1024.");
        if (selection != null && selection.Rows.Count > AuthoringBatchNativeCompiler.MaxNativeOperations)
            return new(false, null, AuthoringBatchNativeFailure.NativeOperationLimit, AuthoringBatchFailure.None, "Native Apply accepts at most 500 copies; the batch is never split.");
        var logical = AuthoringBatchPlanner.TryPlan(selection, AuthoringBatchAction.Duplicate);
        if (!logical.IsValid) return Fail(logical.Failure, logical.Message);
        if (inputs == null || inputs.Count != selection!.Rows.Count)
            return Fail(AuthoringBatchFailure.InvalidSelection, "Every selected source requires its complete writable copy projection.");
        if (!UnicodeValid(selection.NodeId)) return UnicodeFailure();
        var copies = new List<AuthoringBatchCopiedLine>(inputs.Count);
        try
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i]; var row = selection.Rows[i];
                if (input == null || input.StableId != row.StableId) return Fail(AuthoringBatchFailure.InvalidSelection, "Copy input source order/identity changed.");
                if (!UnicodeValid(row.StableId) || !UnicodeValid(row.Text) || !UnicodeValid(input.LineInputJson)) return UnicodeFailure();
                using var document = JsonDocument.Parse(input.LineInputJson);
                var line = document.RootElement;
                if (!ValidateJson(line)) return Fail(AuthoringBatchFailure.InvalidSelection, "Copy JSON has duplicate properties or invalid text.");
                string[] fields = { "inherit", "mode", "speaker", "text", "stage", "highlighted_slots", "scene", "place", "extra_script" };
                if (line.ValueKind != JsonValueKind.Object || line.EnumerateObject().Count() != fields.Length ||
                    fields.Any(f => !line.TryGetProperty(f, out _))) return Fail(AuthoringBatchFailure.InvalidSelection, "Complete copy fields are required; unknown writable fields are forbidden.");
                var inherit = line.GetProperty("inherit");
                if (inherit.ValueKind != JsonValueKind.Object || inherit.EnumerateObject().Count() != 1 || !inherit.TryGetProperty("from", out var from) ||
                    from.ValueKind != JsonValueKind.Object || from.EnumerateObject().Count() != 1 || !from.TryGetProperty("id", out var id) || id.GetString() != row.StableId)
                    return Fail(AuthoringBatchFailure.InvalidSelection, "Copy inheritance must reference its original persistent source ID.");
                string? mode = line.GetProperty("mode").GetString(); var speaker = line.GetProperty("speaker");
                bool validSpeaker = mode is "narration" or "control" ? speaker.ValueKind == JsonValueKind.Null
                    : mode == "dialogue" && speaker.ValueKind == JsonValueKind.Object && speaker.EnumerateObject().Count() == 1 &&
                        (speaker.TryGetProperty("slot", out var slot) && slot.TryGetInt32(out int n) && n is >= 1 and <= 5 ||
                         speaker.TryGetProperty("offstage_resource_id", out var actor) && actor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(actor.GetString()));
                if (!validSpeaker || line.GetProperty("text").GetString() != row.Text || line.GetProperty("stage").ValueKind != JsonValueKind.Array ||
                    line.GetProperty("highlighted_slots").ValueKind != JsonValueKind.Array || line.GetProperty("scene").ValueKind != JsonValueKind.Object ||
                    line.GetProperty("place").ValueKind != JsonValueKind.String || line.GetProperty("extra_script").ValueKind != JsonValueKind.String)
                    return Fail(AuthoringBatchFailure.InvalidSelection, "Copy projection changes source text or has invalid modes/field shapes.");
                var edit = logical.Plan!.Edits[i];
                copies.Add(new("duplicate_" + (i + 1), row.StableId, edit.SourceIndex, edit.TargetIndex, input.LineInputJson));
            }
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                foreach (var copy in copies)
                {
                    writer.WriteStartObject(); writer.WriteString("op", "line.insert");
                    writer.WritePropertyName("node"); writer.WriteStartObject(); writer.WriteString("id", selection.NodeId); writer.WriteEndObject();
                    writer.WriteString("ref", copy.Alias);
                    writer.WritePropertyName("after_line"); writer.WriteStartObject(); writer.WriteString("id", copy.SourceId); writer.WriteEndObject();
                    writer.WritePropertyName("line"); writer.WriteRawValue(copy.LineInputJson); writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            return new(true, new(selection, inputs, copies, Encoding.UTF8.GetString(stream.ToArray())), AuthoringBatchNativeFailure.None, AuthoringBatchFailure.None, string.Empty);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        {
            return Fail(AuthoringBatchFailure.InvalidSelection, "Copy projection is not valid strict JSON: " + error.Message);
        }
    }

    private static bool ValidateJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return UnicodeValid(value.GetString()!);
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(ValidateJson);
        if (value.ValueKind != JsonValueKind.Object) return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(p => seen.Add(p.Name) && UnicodeValid(p.Name) && ValidateJson(p.Value));
    }
    private static bool UnicodeValid(string value)
    {
        for (int i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i]))) return false;
        return true;
    }
    private static AuthoringBatchDuplicatePlanResult Fail(AuthoringBatchFailure failure, string message)
        => new(false, null, AuthoringBatchNativeFailure.LogicalPlanRejected, failure, message);
    private static AuthoringBatchDuplicatePlanResult UnicodeFailure()
        => new(false, null, AuthoringBatchNativeFailure.InvalidUnicode, AuthoringBatchFailure.None, "Copy text/identity contains invalid UTF-16.");
}
