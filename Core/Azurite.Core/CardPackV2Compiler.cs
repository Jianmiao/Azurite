using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Azurite.Core;

/// <summary>Pure compilation only. Does not call AA, mutate a project, or establish live ownership.</summary>
public static class CardPackV2Compiler
{
    public const int EffectiveNativeOperationLimit = 500;
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "Idle", "Greeting", "FalldownL", "FalldownR", "Stiff", "Shake", "Jump", "Hophop" };
    private static readonly HashSet<string> Reactions = new(StringComparer.Ordinal) { "Stiff", "Shake", "FalldownL", "FalldownR" };
    private static readonly HashSet<string> StageFields = new(StringComparer.Ordinal) { "slot", "resource_id", "face_id", "ending_position", "display_order", "action", "emoticon", "effect", "appear", "shape_overrides", "curve" };
    private static readonly HashSet<string> Emoticons = new(StringComparer.Ordinal) { "None", "Angry", "Chat", "Dot", "Exclaim", "Heart", "Music", "Question", "Respond", "Shy", "Surprise", "Sweat", "Twinkle", "Upset", "Think", "Bulb", "Sad", "Sigh", "Steam", "Tear", "Zzz" };

    public static CardPackCompileResult TryCompile(CardPackV2 pack, CardPackCompilationContext context, CardPackContextIdentity expectedIdentity)
    {
        var errors = ValidateData(pack);
        if (context?.Identity == null || expectedIdentity == null)
            return Failure(errors, "CONTEXT_IDENTITY", "Both fresh and preview context identities are required.");
        var id = context.Identity;
        if (new[] { id.ProjectId, id.EditSession, id.Revision, id.CatalogVersion, id.NodeId }.Any(string.IsNullOrWhiteSpace) ||
            (id.AnchorLineId != null && string.IsNullOrWhiteSpace(id.AnchorLineId)) || (id.SelectedTargetSlot.HasValue && !Slot(id.SelectedTargetSlot.Value)))
            errors.Add(new("CONTEXT_IDENTITY", "Context identities must be copied nonempty opaque values; selected slot must be 1..5."));
        if (id != expectedIdentity)
            errors.Add(new("STALE_CONTEXT", "Project/session/revision/catalog/node/anchor/selected slot changed since preview."));
        if (!context.StageIsNativeInherited)
            errors.Add(new("STAGE_NOT_INHERITED", "Adapter must provide verified native inheritance output, not the prior line's one-shot actions."));
        var resources = ValidateBindings(context.Resources, errors);
        var stage = ValidateStage(context.InheritedStage, resources, errors);
        if (id.AnchorLineId == null && stage.Count != 0)
            errors.Add(new("INITIAL_STAGE_REQUIRES_SOURCE", "A null anchor inserts at zero. This compiler has no verified predecessor source for a nonempty inherited stage there."));
        if (errors.Count != 0) return new(false, null, errors.AsReadOnly());

        var operations = new List<object>();
        for (var i = 0; i < pack.Cards.Count; i++)
        {
            var card = pack.Cards[i];
            RequireHost(context, CardNativeCapability.LineInsert, card, errors);
            var line = new Dictionary<string, object?> {
                ["inherit"] = i == 0 && id.AnchorLineId == null ? "none" : "previous",
                ["mode"] = card.Kind == PresetCardKind.Language ? "dialogue" : "control",
                ["speaker"] = card.Kind == PresetCardKind.Language ? new { slot = card.TargetSlot!.Value } : null,
                ["text"] = ""
            };
            switch (card.Kind)
            {
                case PresetCardKind.Transition:
                    RequireHost(context, CardNativeCapability.SceneTransition, card, errors);
                    AddScene(line, "transition", ResolveScene(card, CardResourceKind.Transition, resources, errors));
                    break;
                case PresetCardKind.SoundEffect:
                    RequireHost(context, CardNativeCapability.SceneSound, card, errors);
                    AddScene(line, "sound", ResolveScene(card, CardResourceKind.Sound, resources, errors));
                    break;
                case PresetCardKind.Action:
                case PresetCardKind.Combat:
                    RequireHost(context, CardNativeCapability.StageAction, card, errors);
                    var target = ResolveTarget(card, id, stage, resources, errors);
                    if (target != null && !Has(target.SupportedActions, card.Action!))
                        errors.Add(new("RESOURCE_CAPABILITY", "Target resource does not verify action '" + card.Action + "'.", card.CardId));
                    if (card.Kind == PresetCardKind.Combat)
                    {
                        RequireHost(context, CardNativeCapability.StageEffect | CardNativeCapability.SceneSound, card, errors);
                        if (target != null && !Has(target.SupportedEffects, "Shot"))
                            errors.Add(new("RESOURCE_CAPABILITY", "Target resource does not verify Shot.", card.CardId));
                        AddScene(line, "sound", ResolveScene(card, CardResourceKind.Sound, resources, errors));
                    }
                    line["stage"] = stage.Select(occupant => ProjectStage(occupant, card)).ToArray();
                    break;
                case PresetCardKind.Language:
                    RequireHost(context, CardNativeCapability.DialogueText, card, errors);
                    ResolveTarget(card, id, stage, resources, errors);
                    if (context.SelectedLocale == null || !card.Variants!.TryGetValue(context.SelectedLocale, out var text))
                        errors.Add(new("LOCALE_MISSING", "The selected locale has no supplied dialogue text.", card.CardId));
                    else line["text"] = text;
                    break;
            }
            operations.Add(new Dictionary<string, object?> {
                ["op"] = "line.insert", ["node"] = new { id = id.NodeId }, ["ref"] = "card_" + (i + 1),
                ["after_line"] = i > 0 ? new Dictionary<string,string> { ["ref"] = "card_" + i } : id.AnchorLineId == null ? null : new Dictionary<string,string> { ["id"] = id.AnchorLineId },
                ["line"] = line
            });
        }
        // No caller can observe a partial prefix: publish only after every card passed.
        return errors.Count == 0
            ? new(true, new CardPackNativePlan(JsonSerializer.Serialize(operations), id with { }, operations.Count), Array.Empty<CardPackDiagnostic>())
            : new(false, null, errors.AsReadOnly());
    }

    internal static List<CardPackDiagnostic> ValidateData(CardPackV2 pack)
    {
        var errors = new List<CardPackDiagnostic>();
        if (pack == null) { errors.Add(new("PACK_REQUIRED", "Pack is required.")); return errors; }
        if (pack.SchemaVersion != "2") errors.Add(new("SCHEMA_VERSION", "Expected schemaVersion 2. Legacy content requires explicit migration."));
        if (!LogicalId(pack.PackId) || string.IsNullOrWhiteSpace(pack.DisplayName)) errors.Add(new("PACK_ID", "A logical pack ID and display name are required."));
        if (pack.Cards == null || pack.Cards.Count == 0) { errors.Add(new("EMPTY_PACK", "At least one card is required.")); return errors; }
        if (pack.Cards.Count > EffectiveNativeOperationLimit) errors.Add(new("OPERATION_LIMIT", "Native Apply accepts at most 500 operations. This pack is never split across Apply calls."));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in pack.Cards)
        {
            if (card == null) { errors.Add(new("CARD_REQUIRED", "A card cannot be null.")); continue; }
            void Error(string code, string message) => errors.Add(new(code, message, card.CardId));
            if (!LogicalId(card.CardId) || !seen.Add(card.CardId)) Error("CARD_ID", "Card IDs must be unique logical IDs.");
            if (!Enum.IsDefined(typeof(PresetCardKind), card.Kind)) { Error("CARD_KIND", "Unsupported card family."); continue; }
            if (card.DurationMs != null) Error("DURATION_UNSUPPORTED", "No native duration mapping is verified; remove duration or migrate the intended timing explicitly.");
            if (card.Curve.HasValue) Error("CURVE_UNSUPPORTED", "Cards cannot author Curve payloads. Existing adapter-validated curve data is only retained.");
            var needsResource = card.Kind is PresetCardKind.Transition or PresetCardKind.SoundEffect or PresetCardKind.Combat;
            var needsTarget = card.Kind is PresetCardKind.Action or PresetCardKind.Combat or PresetCardKind.Language;
            var needsAction = card.Kind is PresetCardKind.Action or PresetCardKind.Combat;
            if (needsResource && !LogicalId(card.ResourceKey)) Error("RESOURCE_MISSING", "A logical resource binding key is required.");
            if (needsTarget && (!card.TargetSlot.HasValue || !Slot(card.TargetSlot.Value))) Error("TARGET_SLOT", "An explicit target slot 1..5 is required.");
            if (needsAction && (card.Action == null || !Actions.Contains(card.Action))) Error("ACTION_UNSUPPORTED", "Only verified ordinary native actions are supported; Curve and arbitrary action names are rejected.");
            if (card.Kind == PresetCardKind.Combat && (card.Action == null || !Reactions.Contains(card.Action))) Error("COMBAT_REACTION", "Combat needs Stiff, Shake, FalldownL or FalldownR with Shot and sound.");
            if ((!needsResource && card.ResourceKey != null) || (!needsTarget && card.TargetSlot != null) || (!needsAction && card.Action != null) || (card.Kind != PresetCardKind.Language && card.Variants != null)) Error("CARD_FIELDS", "This family does not consume one or more supplied parameters; they cannot be ignored.");
            if (card.Kind == PresetCardKind.Language && (card.Variants == null || card.Variants.Count == 0 || card.Variants.Any(x => !Locale(x.Key) || x.Value == null))) Error("LANGUAGE_VARIANTS", "Language cards require supplied locale/text variants. A code alone is not dialogue.");
        }
        return errors;
    }

    private static Dictionary<string, CardResourceBinding> ValidateBindings(IReadOnlyList<CardResourceBinding> bindings, List<CardPackDiagnostic> errors)
    {
        var byKey = new Dictionary<string, CardResourceBinding>(StringComparer.Ordinal);
        var actualIds = new HashSet<string>(StringComparer.Ordinal);
        if (bindings == null) { errors.Add(new("RESOURCE_MISSING", "Verified catalog bindings are required.")); return byKey; }
        foreach (var binding in bindings)
        {
            if (binding == null || !LogicalId(binding.ResourceKey) || string.IsNullOrWhiteSpace(binding.ResourceId) || !Enum.IsDefined(typeof(CardResourceKind), binding.Kind))
            { errors.Add(new("RESOURCE_SCHEMA", "Each binding needs a logical key, opaque catalog ID and known kind.")); continue; }
            if (!byKey.TryAdd(binding.ResourceKey, binding) || !actualIds.Add(binding.ResourceId))
                errors.Add(new("RESOURCE_SCHEMA", "Duplicate logical or actual resource binding is ambiguous."));
            if (binding.SupportedActions == null || binding.SupportedEffects == null || binding.FaceIds == null)
                errors.Add(new("RESOURCE_SCHEMA", "Resource capability lists must be explicit, even when empty."));
        }
        return byKey;
    }

    private static List<JsonElement> ValidateStage(JsonElement input, Dictionary<string, CardResourceBinding> resources, List<CardPackDiagnostic> errors)
    {
        var stage = new List<JsonElement>();
        void Error(string message) => errors.Add(new("STAGE_SCHEMA", message));
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() > 5) { Error("Inherited stage must be a native writable array of at most five occupants."); return stage; }
        var slots = new HashSet<int>();
        foreach (var item in input.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { Error("Stage occupant must be an object."); continue; }
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
                if (!fields.Add(property.Name) || !StageFields.Contains(property.Name)) Error("Unknown, duplicate or read-only stage field: " + property.Name);
            if (!item.TryGetProperty("slot", out var slot) || slot.ValueKind != JsonValueKind.Number || !slot.TryGetInt32(out var number) || !Slot(number) || !slots.Add(number)) { Error("Stage slots must be unique integers 1..5."); continue; }
            if (!item.TryGetProperty("resource_id", out var resource) || resource.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(resource.GetString())) { Error("Stage occupant needs an opaque resource_id."); continue; }
            var binding = resources.Values.FirstOrDefault(x => x.ResourceId == resource.GetString());
            if (binding == null || binding.Kind != CardResourceKind.Character || !binding.StageSupported) errors.Add(new("STAGE_RESOURCE", "Every existing stage resource must be a verified stage-capable character."));
            foreach (var key in new[] { "ending_position", "display_order" })
                if (item.TryGetProperty(key, out var value) && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || !Slot(n))) Error(key + " must be an integer 1..5.");
            var hasFace = item.TryGetProperty("face_id", out var face);
            if ((hasFace && (face.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(face.GetString()))) || binding != null && !Has(binding.FaceIds, hasFace && face.ValueKind == JsonValueKind.String ? face.GetString()! : "00")) errors.Add(new("STAGE_RESOURCE", "Existing face, including the omitted default 00, must be verified by the character catalog."));
            CheckEnum(item, "action", Actions.Concat(new[] { "Curve" }), Error);
            CheckEnum(item, "emoticon", Emoticons, Error);
            CheckEnum(item, "effect", new[] { "None", "Shot" }, Error);
            CheckEnum(item, "appear", new[] { "None", "AL", "AR", "A", "DL", "DR", "D" }, Error);
            if (item.TryGetProperty("shape_overrides", out var shapes))
            {
                if (shapes.ValueKind != JsonValueKind.Array || shapes.GetArrayLength() > 3) Error("shape_overrides must contain at most three unique native values.");
                else { var seen = new HashSet<string>(); foreach (var shape in shapes.EnumerateArray()) if (shape.ValueKind != JsonValueKind.String || !new[] { "Sig", "Black", "Closeup" }.Contains(shape.GetString()) || !seen.Add(shape.GetString()!)) Error("Invalid shape override."); }
            }
            if (item.TryGetProperty("curve", out var curve) && curve.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) Error("Retained curve must be an adapter-validated native object or null.");
            // Do not normalize, recreate, or strip retained native curve fields.
            stage.Add(item.Clone());
        }
        return stage;
    }

    private static void CheckEnum(JsonElement item, string key, IEnumerable<string> allowed, Action<string> error)
    {
        if (item.TryGetProperty(key, out var value) && (value.ValueKind != JsonValueKind.String || !allowed.Contains(value.GetString(), StringComparer.Ordinal))) error("Invalid native " + key + ".");
    }
    private static CardResourceBinding? ResolveTarget(CardV2 card, CardPackContextIdentity id, List<JsonElement> stage, Dictionary<string, CardResourceBinding> resources, List<CardPackDiagnostic> errors)
    {
        var item = stage.FirstOrDefault(x => x.GetProperty("slot").GetInt32() == card.TargetSlot);
        if (card.TargetSlot != id.SelectedTargetSlot || item.ValueKind == JsonValueKind.Undefined)
        { errors.Add(new("TARGET_SLOT", "Card target must match the validated selected slot and an existing inherited occupant.", card.CardId)); return null; }
        return resources.Values.First(x => x.ResourceId == item.GetProperty("resource_id").GetString());
    }
    private static Dictionary<string, object?> ProjectStage(JsonElement occupant, CardV2 card)
    {
        var projected = occupant.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone(), StringComparer.Ordinal);
        if (occupant.GetProperty("slot").GetInt32() == card.TargetSlot)
        {
            projected["action"] = card.Action;
            if (card.Kind == PresetCardKind.Combat) projected["effect"] = "Shot";
        }
        return projected;
    }
    private static string? ResolveScene(CardV2 card, CardResourceKind kind, Dictionary<string, CardResourceBinding> resources, List<CardPackDiagnostic> errors)
    {
        if (!resources.TryGetValue(card.ResourceKey!, out var resource)) { errors.Add(new("RESOURCE_MISSING", "No verified binding for '" + card.ResourceKey + "'.", card.CardId)); return null; }
        if (resource.Kind != kind) errors.Add(new("RESOURCE_KIND", "Catalog binding kind does not match the card family.", card.CardId));
        if (!resource.SceneSetSupported) errors.Add(new("RESOURCE_CAPABILITY", "Catalog binding does not verify native scene assignment.", card.CardId));
        return resource.ResourceId;
    }
    private static void AddScene(Dictionary<string, object?> line, string field, string? id) => line["scene"] = new Dictionary<string, object> { [field] = new { action = "set", resource_id = id } };
    private static void RequireHost(CardPackCompilationContext context, CardNativeCapability needed, CardV2 card, List<CardPackDiagnostic> errors)
    { if ((context.NativeCapabilities & needed) != needed) errors.Add(new("HOST_CAPABILITY", "Adapter has not verified native capabilities: " + needed + ".", card.CardId)); }
    private static bool Has(IReadOnlyList<string> values, string value) => values != null && values.Contains(value, StringComparer.Ordinal);
    private static bool Slot(int n) => n is >= 1 and <= 5;
    internal static bool LogicalId(string? value) => !string.IsNullOrWhiteSpace(value) && value != "." && value != ".." && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
    private static bool Locale(string value) => !string.IsNullOrWhiteSpace(value) && value.Split('-').All(x => x.Length > 0 && x.All(char.IsLetterOrDigit));
    private static CardPackCompileResult Failure(List<CardPackDiagnostic> errors, string code, string message) { errors.Add(new(code, message)); return new(false, null, errors.AsReadOnly()); }
}
