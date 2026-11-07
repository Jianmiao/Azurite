using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azurite.Core;

/// <summary>Strict data-only v2 boundary; unknown and duplicate properties are never ignored.</summary>
public static class CardPackV2Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static string Serialize(CardPackV2 pack)
    {
        var errors = CardPackV2Compiler.ValidateData(pack);
        if (errors.Count != 0) throw new FormatException(string.Join("; ", errors.Select(x => x.Code + ": " + x.Message)));
        return JsonSerializer.Serialize(pack, Options);
    }
    public static CardPackV2 Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("V2 card JSON is required.");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Object(root, new[] { "schemaVersion", "packId", "displayName", "cards" }, new[] { "schemaVersion", "packId", "displayName", "cards" });
            var cards = root.GetProperty("cards");
            if (cards.ValueKind != JsonValueKind.Array) throw new FormatException("cards must be an array.");
            foreach (var card in cards.EnumerateArray())
            {
                Object(card, new[] { "cardId", "kind", "resourceKey", "targetSlot", "action", "variants", "durationMs", "curve" }, new[] { "cardId", "kind" });
                if (card.TryGetProperty("curve", out _)) throw new FormatException("CURVE_UNSUPPORTED: Curve parameters cannot be authored by cards.");
                if (card.TryGetProperty("variants", out var variants) && variants.ValueKind != JsonValueKind.Null)
                {
                    if (variants.ValueKind != JsonValueKind.Object) throw new FormatException("variants must be a locale/text object.");
                    var locales = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var pair in variants.EnumerateObject())
                        if (!locales.Add(pair.Name) || pair.Value.ValueKind != JsonValueKind.String) throw new FormatException("Variant keys must be unique with string values.");
                }
            }
            var pack = JsonSerializer.Deserialize<CardPackV2>(json, Options) ?? throw new FormatException("Pack is null.");
            var errors = CardPackV2Compiler.ValidateData(pack);
            if (errors.Count != 0) throw new FormatException(string.Join("; ", errors.Select(x => x.Code + ": " + x.Message)));
            return pack;
        }
        catch (JsonException e) { throw new FormatException("Invalid v2 card data.", e); }
    }
    private static void Object(JsonElement value, string[] allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal) || !names.Add(field.Name)) throw new FormatException("Unknown or duplicate field: " + field.Name);
        if (required.Any(x => !names.Contains(x))) throw new FormatException("Missing required field.");
    }
}

public static class CardPackV2Migration
{
    /// <summary>No inferred targets, translated dialogue, invented timing or combat semantics.</summary>
    public static CardPackMigrationResult TryMigrate(PresetCardPack pack)
    {
        var errors = new List<CardPackDiagnostic>();
        var cards = new List<CardV2>();
        void Needed(string message, string? cardId = null) => errors.Add(new("MIGRATION_NEEDED", message, cardId));
        var valid = PresetCardPackValidator.Validate(pack, Array.Empty<string>(), Array.Empty<string>(), checkExternalAvailability: false);
        if (!valid.IsValid) foreach (var message in valid.Errors) Needed(message);
        if (pack?.Cards == null) return new(false, null, errors.AsReadOnly());
        foreach (var capability in pack.RequiredHostCapabilities ?? Array.Empty<string>())
        {
            if (capability is not ("dialogue.edit" or "transition.apply" or "audio.play")) Needed("Unmapped legacy capability '" + capability + "' must be reviewed.");
            if (capability == "transition.apply" && !pack.Cards.Any(x => x?.Kind == PresetCardKind.Transition) ||
                capability == "audio.play" && !pack.Cards.Any(x => x?.Kind == PresetCardKind.SoundEffect)) Needed("Legacy capability '" + capability + "' is not implied by the migrated cards and cannot be dropped.");
        }
        foreach (var card in pack.Cards)
        {
            if (card == null) continue;
            if (card.Kind == PresetCardKind.Language) { Needed("A language code alone contains no story dialogue. Supply explicit locale/text variants and a selected speaker slot.", card.CardId); continue; }
            if (card.Kind == PresetCardKind.Action) { Needed("Legacy action has no explicit validated target slot. Select the slot and verified action.", card.CardId); continue; }
            if (card.Kind == PresetCardKind.Combat) { Needed("Legacy combat does not define verified Shot/reaction/sound choreography. Author a v2 combat card explicitly.", card.CardId); continue; }
            if (card.Steps == null || card.Steps.Count == 0) { Needed("Legacy card has no supported steps.", card.CardId); continue; }
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < card.Steps.Count; i++)
            {
                var step = card.Steps[i];
                if (step == null) continue;
                var expectedStep = card.Kind == PresetCardKind.Transition ? PresetCardStepKind.ApplyTransition : PresetCardStepKind.PlaySound;
                var expectedType = card.Kind == PresetCardKind.Transition ? "transition" : "sfx";
                if (card.Kind is not (PresetCardKind.Transition or PresetCardKind.SoundEffect) || step.Kind != expectedStep || step.DurationMs != null || step.ActionId != null || step.LanguageCode != null)
                { Needed("Unsupported legacy family, timing, action or language parameters cannot be silently reinterpreted.", card.CardId); continue; }
                if (step.ResourceId == null || card.Resources == null || !card.Resources.Any(x => x != null && x.ResourceId == step.ResourceId && x.ResourceType == expectedType))
                { Needed("Legacy resource declaration must match its supported native resource kind.", card.CardId); continue; }
                referenced.Add(step.ResourceId);
                cards.Add(new CardV2(card.Steps.Count == 1 ? card.CardId : card.CardId + ".step-" + (i + 1), card.Kind, ResourceKey: step.ResourceId));
            }
            if (card.Resources != null && card.Resources.Any(x => x != null && !referenced.Contains(x.ResourceId))) Needed("Unused legacy resource requirements cannot be silently removed.", card.CardId);
        }
        var migrated = new CardPackV2("2", pack.PackId, pack.DisplayName, cards.AsReadOnly());
        foreach (var error in CardPackV2Compiler.ValidateData(migrated)) Needed(error.Message, error.CardId);
        return errors.Count == 0 ? new(true, migrated, Array.Empty<CardPackDiagnostic>()) : new(false, null, errors.AsReadOnly());
    }
}

/// <summary>Data examples. Keys are supplied by the caller, never fabricated native resource IDs.</summary>
public static class CardPackV2Examples
{
    public static CardPackV2 Create(int targetSlot, string transitionKey, string soundKey) => new("2", "azurite.examples.v2", "Offline five-family examples", new[] {
        new CardV2("transition", PresetCardKind.Transition, ResourceKey: transitionKey),
        new CardV2("combat", PresetCardKind.Combat, ResourceKey: soundKey, TargetSlot: targetSlot, Action: "Stiff"),
        new CardV2("action", PresetCardKind.Action, TargetSlot: targetSlot, Action: "Greeting"),
        new CardV2("language", PresetCardKind.Language, TargetSlot: targetSlot, Variants: new Dictionary<string,string>(StringComparer.Ordinal) { ["zh-CN"] = "老师，我们出发吧。", ["en"] = "Sensei, let's go." }),
        new CardV2("sound", PresetCardKind.SoundEffect, ResourceKey: soundKey)
    });
}
