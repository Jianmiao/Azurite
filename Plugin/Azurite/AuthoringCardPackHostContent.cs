using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Azurite.Core;
using AzureArchive.Automation;
using Newtonsoft.Json.Linq;
using Studio.Scripts;

namespace Azurite;

/// <summary>A user-selected logical binding to an actual effective-catalog ID.</summary>
internal sealed record AuthoringCardPackResourceChoice(string ResourceKey, string ResourceId, CardResourceKind Kind);

/// <summary>Current-host inheritance and catalog projection; not a readiness or UI registration mechanism.</summary>
internal sealed class AuthoringCardPackHostContent
{
    private const string InheritPrevious = "{\"inherit\":\"previous\",\"mode\":\"control\",\"speaker\":null,\"text\":\"\"}";
    private static readonly HashSet<string> WritableStageFields = new(StringComparer.Ordinal)
        { "slot", "resource_id", "face_id", "ending_position", "display_order", "action", "emoticon", "effect", "appear", "shape_overrides", "curve" };
    private readonly int _editorThreadId;
    private readonly Func<IReadOnlyList<AuthoringCardPackResourceChoice>> _selectedResources;
    private sealed record Schema(CardNativeCapability Capabilities, string[] Actions, string[] Effects, IReadOnlySet<string> WritableStageFields);

    internal AuthoringCardPackHostContent(int editorThreadId, Func<IReadOnlyList<AuthoringCardPackResourceChoice>> selectedResources)
    {
        if (editorThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(editorThreadId));
        _editorThreadId = editorThreadId;
        _selectedResources = selectedResources ?? throw new ArgumentNullException(nameof(selectedResources));
    }

    /// <summary>
    /// Supply this method as AuthoringCardPackHost.captureContent. It performs no
    /// live Apply/history/selection mutation. Choice IDs must come from native
    /// Search/Resolve UI selection, never names, native_key or generated IDs.
    /// </summary>
    internal CardPackCompilationContext Capture(CardPackContextIdentity identity, string? selectedLocale)
    {
        Require(Environment.CurrentManagedThreadId == _editorThreadId, "Native content must be read on the qualified editor thread.");
        Script previous = Anchor(identity);
        var schema = ReadSchema(Parse(AuthoringApiSchema.GetSchema().ToString()));
        var choices = (_selectedResources() ?? throw new InvalidOperationException("Resource selection is unavailable.")).ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var actual = new Dictionary<string, CardResourceBinding>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            Require(choice != null && LogicalKey(choice.ResourceKey) && !string.IsNullOrWhiteSpace(choice.ResourceId), "Each selected resource needs a logical key and copied native ID.");
            Require(keys.Add(choice!.ResourceKey) && !actual.ContainsKey(choice.ResourceId), "Duplicate logical or native resource bindings are ambiguous; reuse one logical key instead.");
            actual.Add(choice.ResourceId, Resolve(choice.ResourceKey, choice.ResourceId, choice.Kind, identity.CatalogVersion, schema));
        }

        // Current Create's previous branch calls Script(previous, removeInvisible:true),
        // wraps detached ScriptData, then Patch; it never invokes the from-ref resolver.
        // Do not copy raw previous.stage: one-shot actions/effects and exited actors differ.
        var inherited = AuthoringLines.Create(JObject.Parse(InheritPrevious), previous, null!, identity.CatalogVersion);
        Require(inherited != null, "Native inherited line is unavailable.");
        var stage = ReadStage(inherited!, identity, schema);
        Script inheritedPrevious = inherited!;
        var next = AuthoringLines.Create(JObject.Parse(InheritPrevious), inheritedPrevious, null!, identity.CatalogVersion);
        Require(next != null, "Native next-line inheritance is unavailable.");
        var nextStage = ReadStage(next!, identity, schema);
        Require(Canonical(stage) == Canonical(nextStage), "Native stage inheritance changes across this insertion chain; preview cannot reuse one baseline.");

        foreach (var occupant in stage.EnumerateArray())
        {
            string id = RequiredString(occupant, "resource_id");
            if (actual.TryGetValue(id, out var existing))
            {
                Require(existing.Kind == CardResourceKind.Character, "Retained stage resource is bound to another family.");
                continue;
            }
            string key = "_native_stage_" + occupant.GetProperty("slot").GetInt32();
            while (!keys.Add(key)) key += "_";
            actual.Add(id, Resolve(key, id, CardResourceKind.Character, identity.CatalogVersion, schema));
        }
        // Both native factories allocate detached data. Discard their generated line
        // IDs; only writable stage fields go to the compiler. Recheck the live anchor
        // and catalog after every external source has been read.
        Anchor(identity);
        return new(identity with { }, stage, true, new ReadOnlyCollection<CardResourceBinding>(actual.Values.ToArray()), schema.Capabilities, selectedLocale);
    }

    private static Script Anchor(CardPackContextIdentity identity)
    {
        var session = AuthoringEditorSession.Current;
        var studio = StudioCommon.instance;
        var inspector = ScriptNodeInspector.instance;
        Require(identity != null && session != null && studio != null && session.studio?.Pointer == studio.Pointer && !session.disposed && !session.IsApplying && !session.SaveInProgress,
            "Native inheritance requires the active existing session owner.");
        Require(session!.ProjectId == identity!.ProjectId && session.EditSession == identity.EditSession && session.Revision == identity.Revision && AuthoringResourceCatalog.CatalogVersion == identity.CatalogVersion,
            "Native project/session/revision/catalog changed during content capture.");
        var node = inspector?.scriptNode; var row = inspector?.selectedScriptItem;
        Require(node != null && row != null && row.selected && row.inspector?.Pointer == inspector!.Pointer && row.scriptNode?.Pointer == node.Pointer && node.guid.ToString() == identity.NodeId,
            "Native selected node/anchor owner changed.");
        Require(row!.index >= 0 && node!.scripts != null && row.index < node.scripts.Count && inspector!.scriptNodeListItemsCache != null &&
            row.index < inspector.scriptNodeListItemsCache.Count && inspector.scriptNodeListItemsCache[row.index]?.Pointer == row.Pointer,
            "Native selected anchor mapping is stale.");
        int slot = inspector!.selectedSlotNum;
        Require((slot is >= 1 and <= 5 ? (int?)slot : null) == identity.SelectedTargetSlot &&
            !string.IsNullOrWhiteSpace(identity.AnchorLineId) && node!.scripts[row.index].LineId == identity.AnchorLineId,
            "Native selected slot or persistent anchor changed.");
        return node!.scripts[row.index];
    }

    private static JsonElement ReadStage(ScriptData line, CardPackContextIdentity identity, Schema schema)
    {
        var node = ScriptNodeInspector.instance!.scriptNode;
        var read = Parse(AuthoringLines.Read(line, node.guid).ToString());
        Require(RequiredString(read, "node_id") == identity.NodeId, "Native inherited line belongs to another node.");
        var input = read.GetProperty("stage");
        Require(input.ValueKind == JsonValueKind.Array && input.GetArrayLength() <= 5, "Native inherited stage is not a bounded stage array.");
        var slots = new HashSet<int>(); var projected = new List<Dictionary<string, JsonElement>>();
        foreach (var occupant in input.EnumerateArray())
        {
            Require(occupant.ValueKind == JsonValueKind.Object, "Native stage occupant is not an object.");
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in occupant.EnumerateObject())
            {
                Require(seen.Add(field.Name), "Duplicate native stage field: " + field.Name);
                if (field.Name == "starting_position") continue; // explicitly derived/read-only
                Require(WritableStageFields.Contains(field.Name), "Unmapped native stage field cannot be discarded: " + field.Name);
                Require(schema.WritableStageFields.Contains(field.Name), "Current native schema does not declare inherited field writable: " + field.Name);
                // The pinned native previous-copy constructor resets these fields;
                // never label a raw/replayed transient stage as verified inheritance.
                if (field.Name is "action" or "effect" or "emoticon" or "appear")
                    Require(field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() == (field.Name == "action" ? "Idle" : "None"), "Unexpected one-shot state in native inheritance: " + field.Name);
                if (field.Name == "curve") Require(field.Value.ValueKind == JsonValueKind.Null, "Native inheritance returned an unverified transient curve; it cannot be dropped or replayed.");
                fields.Add(field.Name, field.Value.Clone());
            }
            int slot = occupant.GetProperty("slot").GetInt32();
            Require(slot is >= 1 and <= 5 && slots.Add(slot), "Native stage slots are invalid or duplicated.");
            RequiredString(occupant, "resource_id");
            projected.Add(fields);
        }
        return Parse(JsonSerializer.Serialize(projected));
    }

    private static CardResourceBinding Resolve(string logicalKey, string id, CardResourceKind kind, string version, Schema schema)
    {
        string nativeKind = kind switch { CardResourceKind.Character => "character", CardResourceKind.Transition => "transition", CardResourceKind.Sound => "sound", _ => throw new InvalidOperationException("Unsupported card resource kind.") };
        var resource = Parse(AuthoringResourceCatalog.Resolve(id, nativeKind, version, true).ToString());
        Require(RequiredString(resource, "resource_id") == id && RequiredString(resource, "kind") == nativeKind && RequiredString(resource, "catalog_version") == version,
            "Native Resolve changed the requested resource identity, kind or catalog version.");
        Require(True(resource, "available"), "Native catalog resource is unavailable: " + id);
        if (kind != CardResourceKind.Character)
        {
            bool sceneSet = (schema.Capabilities & (kind == CardResourceKind.Transition ? CardNativeCapability.SceneTransition : CardNativeCapability.SceneSound)) != 0;
            // Scene support comes from the actual scene schema plus resolved kind and
            // availability; ToJson does not emit invented scene_set_supported fields.
            return new(logicalKey, id, kind, sceneSet, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }
        var capabilities = resource.GetProperty("capabilities");
        Require(True(capabilities, "stage") && True(capabilities, "faces_loaded") && !capabilities.TryGetProperty("load_error", out _),
            "Native character stage/face metadata is unavailable: " + id);
        string[] faces = Strings(capabilities.GetProperty("faces"));
        Require(faces.Length != 0, "Native character has no published face IDs: " + id);
        // Actions/effects are native engine enum commands (not Spine clip names):
        // current Patch resolves stage/faces, then parses these global enums.
        return new(logicalKey, id, kind, false, true, schema.Actions.ToArray(), schema.Effects.ToArray(), faces);
    }

    private static Schema ReadSchema(JsonElement root)
    {
        var capabilities = root.GetProperty("capabilities");
        Require(RequiredString(root, "api_version") == "1" && capabilities.GetProperty("format_version").GetInt32() == 2 &&
            capabilities.GetProperty("limits").GetProperty("max_operations").GetInt32() == CardPackV2Compiler.EffectiveNativeOperationLimit &&
            Strings(capabilities.GetProperty("tools")).Contains("apply_edits", StringComparer.Ordinal), "Native authoring schema/version/operation limit is not the supported contract.");
        var definitions = root.GetProperty("$defs");
        var insertions = definitions.GetProperty("EditOperation").GetProperty("oneOf").EnumerateArray()
            .Where(item => item.GetProperty("properties").GetProperty("op").GetProperty("const").GetString() == "line.insert").ToArray();
        Require(insertions.Length == 1, "Native schema has no unambiguous line.insert operation.");
        var insertion = insertions[0].GetProperty("properties");
        Require(Ref(insertion.GetProperty("node"), "Ref") && Ref(insertion.GetProperty("line"), "LineInput"), "Native line insertion references changed.");
        var input = definitions.GetProperty("LineInput").GetProperty("properties");
        Require(Ref(input.GetProperty("inherit"), "Inherit") && definitions.GetProperty("Inherit").GetProperty("oneOf").EnumerateArray().Any(item => item.TryGetProperty("enum", out var values) && Strings(values).Contains("previous", StringComparer.Ordinal)),
            "Native previous-line inheritance is not declared.");
        var stage = definitions.GetProperty("StageCharacter").GetProperty("properties");
        bool stageInput = input.TryGetProperty("stage", out var inputStage) && Type(inputStage, "array") && Ref(inputStage.GetProperty("items"), "StageCharacter");
        string[] actions = stage.TryGetProperty("action", out var action) && action.TryGetProperty("enum", out var actionNames) ? Strings(actionNames) : Array.Empty<string>();
        string[] effects = stage.TryGetProperty("effect", out var effect) && effect.TryGetProperty("enum", out var effectNames) ? Strings(effectNames) : Array.Empty<string>();
        var flags = CardNativeCapability.LineInsert;
        if (stageInput && actions.Length > 0) flags |= CardNativeCapability.StageAction;
        if (stageInput && effects.Length > 0) flags |= CardNativeCapability.StageEffect;
        var kinds = Strings(capabilities.GetProperty("resource_kinds"));
        var scene = definitions.GetProperty("SceneInput").GetProperty("properties");
        if (Ref(input.GetProperty("scene"), "SceneInput"))
        {
            if (kinds.Contains("transition", StringComparer.Ordinal) && SceneSet(scene, "transition")) flags |= CardNativeCapability.SceneTransition;
            if (kinds.Contains("sound", StringComparer.Ordinal) && SceneSet(scene, "sound")) flags |= CardNativeCapability.SceneSound;
        }
        if (Type(input.GetProperty("text"), "string") && Strings(input.GetProperty("mode").GetProperty("enum")).Contains("dialogue", StringComparer.Ordinal) && Ref(input.GetProperty("speaker"), "Speaker"))
            flags |= CardNativeCapability.DialogueText;
        return new(flags, actions, effects, stage.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal));
    }

    private static bool SceneSet(JsonElement scene, string name) => scene.TryGetProperty(name, out var field) &&
        field.GetProperty("oneOf").EnumerateArray().Any(variant => variant.GetProperty("properties").GetProperty("action").GetProperty("const").GetString() == "set" &&
            variant.GetProperty("properties").TryGetProperty("resource_id", out var id) && Type(id, "string") && Strings(variant.GetProperty("required")).Contains("resource_id", StringComparer.Ordinal));
    private static bool Ref(JsonElement item, string name) => item.TryGetProperty("$ref", out var value) && value.GetString() == "#/$defs/" + name;
    private static bool Type(JsonElement item, string name) => item.TryGetProperty("type", out var value) && value.GetString() == name;
    private static bool True(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string RequiredString(JsonElement item, string name)
    {
        var value = item.GetProperty(name); Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()), "Native string is missing: " + name); return value.GetString()!;
    }
    private static string[] Strings(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Array, "Native string list is not an array.");
        var values = value.EnumerateArray().Select(item => { Require(item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()), "Native string list contains an invalid value."); return item.GetString()!; }).ToArray();
        Require(values.Distinct(StringComparer.Ordinal).Count() == values.Length, "Native string list contains duplicate identities.");
        return values;
    }
    private static bool LogicalKey(string? key) => !string.IsNullOrWhiteSpace(key) && key is not "." and not ".." && key.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
    private static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
    private static void Require([DoesNotReturnIf(false)] bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
