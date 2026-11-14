using System.Security.Cryptography;
using System.Text.Json;
using Azurite.Core;

internal static class NativeSetTextTests
{
    public static int Run()
    {
        (string Name, Action Test)[] tests =
        {
            (nameof(UsesPersistentIdsForSparseSelection), UsesPersistentIdsForSparseSelection),
            (nameof(EmitsOnlyChangedRowsInSelectionOrder), EmitsOnlyChangedRowsInSelectionOrder),
            (nameof(PreservesExactUnicodeWhitespaceAndNewlines), PreservesExactUnicodeWhitespaceAndNewlines),
            (nameof(OmitsAllUnchangedNativeFields), OmitsAllUnchangedNativeFields),
            (nameof(AcceptsExactly500UpdatesAsOneArray), AcceptsExactly500UpdatesAsOneArray),
            (nameof(Rejects501WithoutPublishingAPrefix), Rejects501WithoutPublishingAPrefix),
            (nameof(PreservesLogical1024Boundary), PreservesLogical1024Boundary),
            (nameof(CountsChangedRowsRatherThanSelectedRows), CountsChangedRowsRatherThanSelectedRows),
            (nameof(RetainsImmutableSelectionAndBeforeAfterPreview), RetainsImmutableSelectionAndBeforeAfterPreview),
            (nameof(RejectsAllNoOpWithoutAnEmptyNativeBatch), RejectsAllNoOpWithoutAnEmptyNativeBatch),
            (nameof(RejectsMissingSelectionAndText), RejectsMissingSelectionAndText),
            (nameof(RejectsInvalidSelectionWithoutPartialOutput), RejectsInvalidSelectionWithoutPartialOutput),
            (nameof(RejectsUnpairedTextSurrogatesInsteadOfReplacingThem), RejectsUnpairedTextSurrogatesInsteadOfReplacingThem),
            (nameof(RejectsUnpairedIdSurrogatesInsteadOfChangingTheTarget), RejectsUnpairedIdSurrogatesInsteadOfChangingTheTarget),
            (nameof(OutputConformsToSavedFullHostSchema), OutputConformsToSavedFullHostSchema)
        };
        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failed++; Console.WriteLine($"FAIL {name}: {exception.Message}"); }
        }
        Console.WriteLine($"NativeSetTextTests: {tests.Length - failed}/{tests.Length} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void UsesPersistentIdsForSparseSelection()
    {
        var plan = Compile(Selection(new("native-B", 2, "old B", 1), new("native-A", 70, "old A", 2)), "new");
        using var json = JsonDocument.Parse(plan.OperationsJson);
        var ops = json.RootElement;
        Check(ops.GetArrayLength() == 2, "both sparse selected rows must be compiled");
        Check(ops[0].GetProperty("line").GetProperty("id").GetString() == "native-B" &&
              ops[1].GetProperty("line").GetProperty("id").GetString() == "native-A", "references must copy persistent IDs, not indexes or sorted IDs");
        Check(plan.Selection.Revision == 7 && plan.Selection.NodeId == "node-opaque", "preview must retain original selection context");
    }

    private static void EmitsOnlyChangedRowsInSelectionOrder()
    {
        var plan = Compile(Selection(new("b", 1, "before b", 1), new("skip", 4, "new", 2), new("a", 9, "before a", 3)), "new");
        using var json = JsonDocument.Parse(plan.OperationsJson);
        Check(json.RootElement.GetArrayLength() == 2 && plan.OperationCount == 2, "no-op rows must not become native operations");
        Check(plan.Changes.Select(c => c.StableId).SequenceEqual(new[] { "b", "a" }), "preview must use the same changed-row order");
        Check(json.RootElement[1].GetProperty("line").GetProperty("id").GetString() == "a", "native operations must preserve selection order");
    }

    private static void PreservesExactUnicodeWhitespaceAndNewlines()
    {
        const string target = "  中文 e\u0301 😀 \"quote\" \\ path\r\nsecond\rthird\n\t\u0000  ";
        const string id = "原生-\"\\😀";
        var plan = Compile(Selection(new AuthoringBatchRow(id, 10, "before", 0)), target);
        using var json = JsonDocument.Parse(plan.OperationsJson);
        Check(json.RootElement[0].GetProperty("set").GetProperty("text").GetString() == target, "SetText must preserve the entire supplied text exactly");
        Check(json.RootElement[0].GetProperty("line").GetProperty("id").GetString() == id, "escaping must preserve opaque IDs");
        Check(plan.Changes[0].After == target, "preview and serialized text must agree");
        var whitespace = Compile(Selection(new AuthoringBatchRow("space", 0, "before", 0)), " \r\n\t ");
        using var whitespaceJson = JsonDocument.Parse(whitespace.OperationsJson);
        Check(whitespaceJson.RootElement[0].GetProperty("set").GetProperty("text").GetString() == " \r\n\t ", "nonempty whitespace is valid full-row text");
    }

    private static void OmitsAllUnchangedNativeFields()
    {
        var plan = Compile(Selection(new AuthoringBatchRow("control", 4, "old", 0)), "replacement");
        using var json = JsonDocument.Parse(plan.OperationsJson);
        var op = json.RootElement[0];
        Check(Names(op).SetEquals(new[] { "op", "line", "set" }), "update must not emit node/index/clear/session/insert fields");
        Check(op.GetProperty("op").GetString() == "line.update", "actual host operation must be line.update");
        Check(Names(op.GetProperty("line")).SetEquals(new[] { "id" }), "line reference must contain only a persistent ID");
        Check(Names(op.GetProperty("set")).SetEquals(new[] { "text" }), "mode/speaker/stage/scene/place/extra_script must remain omitted");
    }

    private static void AcceptsExactly500UpdatesAsOneArray()
    {
        var plan = Compile(Many(500), "new");
        using var json = JsonDocument.Parse(plan.OperationsJson);
        Check(json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() == 500 && plan.OperationCount == 500,
            "500 updates must be one complete array, with no create_dialogue or split batches");
        Check(json.RootElement[499].GetProperty("line").GetProperty("id").GetString() == "row-499", "last target must be present");
    }

    private static void Rejects501WithoutPublishingAPrefix()
    {
        var selection = Many(501);
        Check(AuthoringBatchPlanner.TryPlan(selection, AuthoringBatchAction.SetText, "new").IsValid, "501 logical edits remain allowed");
        var result = AuthoringBatchNativeCompiler.TryCompileSetText(selection, "new");
        Check(!result.IsValid && result.Plan is null && result.Failure == AuthoringBatchNativeFailure.NativeOperationLimit &&
              result.LogicalFailure == AuthoringBatchFailure.None, "501 native updates must reject the whole output independently of logical validation");
    }

    private static void PreservesLogical1024Boundary()
    {
        var max = Many(1024);
        var logical = AuthoringBatchPlanner.TryPlan(max, AuthoringBatchAction.SetText, "new");
        Check(logical.IsValid && logical.Plan!.Edits.Count == 1024, "logical planner must still accept 1024 changed rows");
        var native = AuthoringBatchNativeCompiler.TryCompileSetText(max, "new");
        Check(!native.IsValid && native.Plan is null && native.Failure == AuthoringBatchNativeFailure.NativeOperationLimit, "native limit is independently lower");
        var overflow = AuthoringBatchNativeCompiler.TryCompileSetText(Many(1025), "new");
        Check(!overflow.IsValid && overflow.Plan is null && overflow.Failure == AuthoringBatchNativeFailure.LogicalPlanRejected &&
              overflow.LogicalFailure == AuthoringBatchFailure.TooManyOperations, "1025 must retain logical rejection instead of lowering the planner bound");
    }

    private static void CountsChangedRowsRatherThanSelectedRows()
    {
        var selection = new AuthoringBatchSelection(7, "node-opaque", Enumerable.Range(0, 1024)
            .Select(i => new AuthoringBatchRow("row-" + i, i, i < 500 ? "old" : "new", 1)).ToArray());
        var plan = Compile(selection, "new");
        using var json = JsonDocument.Parse(plan.OperationsJson);
        Check(plan.Selection.Rows.Count == 1024 && plan.OperationCount == 500 && json.RootElement.GetArrayLength() == 500,
            "a 1024-row selection with only 500 changes must stay one valid native batch");
    }

    private static void RetainsImmutableSelectionAndBeforeAfterPreview()
    {
        var source = new List<AuthoringBatchRow> { new("keep-id", 3, "before", 2) };
        var selection = new AuthoringBatchSelection(7, "node-opaque", source);
        var plan = Compile(selection, "after");
        string json = plan.OperationsJson;
        source[0] = new("other", 8, "changed outside", 5);
        source.Clear();
        Check(plan.Selection.Rows[0].StableId == "keep-id" && plan.Selection.Rows[0].Index == 3 && plan.Selection.Rows[0].SpeakerSlot == 2,
            "source collection changes must not alter preview identity/order/speaker");
        Check(plan.Changes[0] == new AuthoringBatchTextChange("keep-id", "before", "after"), "preview must retain exact before/after text");
        if (plan.Changes is IList<AuthoringBatchTextChange> list)
        {
            bool rejected = false;
            try { list[0] = new("other", "wrong", "wrong"); } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "published change collection must reject mutation");
        }
        Check(plan.OperationsJson == json && plan.Changes[0].StableId == "keep-id", "published output must stay immutable");
        Check(!AuthoringBatchPlanner.Matches(plan.Selection, Selection(new AuthoringBatchRow("keep-id", 3, "stale", 2))), "retained snapshot must detect later text changes");
    }

    private static void RejectsAllNoOpWithoutAnEmptyNativeBatch()
        => LogicalFailure(Selection(new AuthoringBatchRow("same", 0, "new", 1)), "new", AuthoringBatchFailure.NoOp);

    private static void RejectsMissingSelectionAndText()
    {
        LogicalFailure(null, "new", AuthoringBatchFailure.NullSelection);
        LogicalFailure(Selection(), "new", AuthoringBatchFailure.EmptySelection);
        LogicalFailure(Many(1), null, AuthoringBatchFailure.EmptyText);
        LogicalFailure(Many(1), "", AuthoringBatchFailure.EmptyText);
    }

    private static void RejectsInvalidSelectionWithoutPartialOutput()
    {
        LogicalFailure(Selection(new("dup", 0, "old", 1), new("dup", 1, "old", 1)), "new", AuthoringBatchFailure.InvalidSelection);
        LogicalFailure(Selection(new("a", 5, "old", 1), new("b", 4, "old", 1)), "new", AuthoringBatchFailure.InvalidSelection);
        LogicalFailure(Selection(new("a", 5, "old", 1), new("b", 5, "old", 1)), "new", AuthoringBatchFailure.InvalidSelection);
        LogicalFailure(Selection(new("a", 0, "old", 1), null!), "new", AuthoringBatchFailure.InvalidSelection);
    }

    private static void RejectsUnpairedTextSurrogatesInsteadOfReplacingThem()
    {
        foreach (string text in new[] { "bad\uD800", "\uDC00bad" })
        {
            var result = AuthoringBatchNativeCompiler.TryCompileSetText(Many(1), text);
            Check(!result.IsValid && result.Plan is null && result.Failure == AuthoringBatchNativeFailure.InvalidUnicode,
                "invalid UTF-16 text must not silently become replacement characters");
        }
    }

    private static void RejectsUnpairedIdSurrogatesInsteadOfChangingTheTarget()
    {
        var result = AuthoringBatchNativeCompiler.TryCompileSetText(Selection(new("good", 0, "old", 1), new("bad\uD800", 1, "old", 2)), "new");
        Check(!result.IsValid && result.Plan is null && result.Failure == AuthoringBatchNativeFailure.InvalidUnicode,
            "an unencodable ID must reject the entire batch, not rewrite its native target");
    }

    private static void OutputConformsToSavedFullHostSchema()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NativeSchemaResponse.json"));
        Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals("b97d7bb5da9a86738a7d43eec79e5b26547bccb50d68400d92891741258f978d", StringComparison.OrdinalIgnoreCase),
            "schema must be the unchanged complete recorded host response");
        using var schema = JsonDocument.Parse(bytes);
        var data = schema.RootElement.GetProperty("data");
        var defs = data.GetProperty("$defs");
        var update = defs.GetProperty("EditOperation").GetProperty("oneOf").EnumerateArray()
            .Single(s => s.GetProperty("properties").GetProperty("op").GetProperty("const").GetString() == "line.update");
        var idRef = defs.GetProperty("Ref").GetProperty("oneOf").EnumerateArray()
            .Single(s => s.GetProperty("properties").TryGetProperty("id", out _));
        var patch = defs.GetProperty("LinePatch");
        var plan = Compile(Many(500), "schema\r\n完整文本");
        using var json = JsonDocument.Parse(plan.OperationsJson);
        Check(json.RootElement.GetArrayLength() <= data.GetProperty("capabilities").GetProperty("limits").GetProperty("max_operations").GetInt32(),
            "emitted operation count must satisfy recorded host limit");
        foreach (var op in json.RootElement.EnumerateArray())
        {
            CheckObjectShape(op, update);
            Check(op.GetProperty("op").GetString() == update.GetProperty("properties").GetProperty("op").GetProperty("const").GetString(), "operation const must match host");
            CheckObjectShape(op.GetProperty("line"), idRef);
            Check(op.GetProperty("line").GetProperty("id").GetString()!.Length >= idRef.GetProperty("properties").GetProperty("id").GetProperty("minLength").GetInt32(), "ID must meet host minimum length");
            CheckObjectShape(op.GetProperty("set"), patch);
            Check(op.GetProperty("set").EnumerateObject().Count() >= update.GetProperty("anyOf")[0].GetProperty("properties").GetProperty("set").GetProperty("minProperties").GetInt32(), "nonempty set must satisfy update anyOf");
        }
    }

    // Deliberately scoped to the three object shapes emitted above; not a substitute for native Apply validation.
    private static void CheckObjectShape(JsonElement value, JsonElement schema)
    {
        Check(schema.GetProperty("type").GetString() == "object" && value.ValueKind == JsonValueKind.Object, "host requires an object");
        var properties = schema.GetProperty("properties");
        if (schema.TryGetProperty("required", out var required))
            foreach (var key in required.EnumerateArray()) Check(value.TryGetProperty(key.GetString()!, out _), "required native field missing");
        foreach (var property in value.EnumerateObject())
        {
            bool known = properties.TryGetProperty(property.Name, out var definition);
            Check(known || schema.GetProperty("additionalProperties").GetBoolean(), "unknown native field emitted");
            if (known && definition.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "string")
                Check(property.Value.ValueKind == JsonValueKind.String, "native field must be a string");
        }
    }

    private static HashSet<string> Names(JsonElement value) => value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    private static AuthoringBatchSelection Selection(params AuthoringBatchRow[] rows) => new(7, "node-opaque", rows);
    private static AuthoringBatchSelection Many(int count) => new(7, "node-opaque", Enumerable.Range(0, count).Select(i => new AuthoringBatchRow("row-" + i, i, "old", 1)).ToArray());
    private static AuthoringBatchNativePlan Compile(AuthoringBatchSelection selection, string text)
    {
        var result = AuthoringBatchNativeCompiler.TryCompileSetText(selection, text);
        Check(result.IsValid && result.Plan is not null && result.Failure == AuthoringBatchNativeFailure.None && result.LogicalFailure == AuthoringBatchFailure.None,
            "expected complete native SetText output: " + result.Message);
        return result.Plan!;
    }
    private static void LogicalFailure(AuthoringBatchSelection? selection, string? text, AuthoringBatchFailure expected)
    {
        var result = AuthoringBatchNativeCompiler.TryCompileSetText(selection, text);
        Check(!result.IsValid && result.Plan is null && result.Failure == AuthoringBatchNativeFailure.LogicalPlanRejected && result.LogicalFailure == expected,
            "logical rejection must be preserved with no native prefix: " + expected);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
