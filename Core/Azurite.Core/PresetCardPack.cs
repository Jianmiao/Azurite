using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azurite.Core;

/// <summary>
/// The supported families of editor card templates.  A card is data only; it
/// cannot carry an executable command or a filesystem path.
/// </summary>
public enum PresetCardKind
{
    Transition,
    Combat,
    Action,
    Language,
    SoundEffect
}

public enum PresetCardStepKind
{
    ApplyTransition,
    StartCombat,
    ApplyAction,
    SetLanguage,
    PlaySound
}

public sealed record PresetCardResource(string ResourceId, string ResourceType);

public sealed record PresetCardStep(
    PresetCardStepKind Kind,
    string? ResourceId,
    string? ActionId,
    string? LanguageCode,
    int? DurationMs);

public sealed record PresetCardTemplate(
    string CardId,
    PresetCardKind Kind,
    string DisplayName,
    IReadOnlyList<PresetCardResource> Resources,
    IReadOnlyList<PresetCardStep> Steps);

public sealed record PresetCardPack(
    string SchemaVersion,
    string PackId,
    string DisplayName,
    IReadOnlyList<string> RequiredHostCapabilities,
    IReadOnlyList<PresetCardTemplate> Cards);

public sealed record PresetCardPackValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Deterministic, data-only JSON representation of a preset card pack.
/// </summary>
public static class PresetCardPackJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(PresetCardPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var result = PresetCardPackValidator.Validate(pack, Array.Empty<string>(), Array.Empty<string>(), checkExternalAvailability: false);
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Errors));
        return JsonSerializer.Serialize(pack, Options);
    }

    public static PresetCardPack Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("Preset card pack JSON is empty.");

        try
        {
            using var document = JsonDocument.Parse(json);
            RejectExecutableFields(document.RootElement);
            var pack = JsonSerializer.Deserialize<PresetCardPack>(json, Options)
                ?? throw new FormatException("Preset card pack JSON is null.");
            var result = PresetCardPackValidator.Validate(pack, Array.Empty<string>(), Array.Empty<string>(), checkExternalAvailability: false);
            if (!result.IsValid)
                throw new FormatException(string.Join("; ", result.Errors));
            return pack;
        }
        catch (FormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new FormatException("Preset card pack JSON is invalid.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private static void RejectExecutableFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("command", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("executable", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"Executable field '{property.Name}' is not allowed in a preset card pack.");
                RejectExecutableFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                RejectExecutableFields(child);
        }
    }
}

public static class PresetCardPackValidator
{
    private const string SupportedSchemaVersion = "1";
    private static readonly StringComparer IdComparer = StringComparer.Ordinal;

    public static PresetCardPackValidationResult Validate(
        PresetCardPack pack,
        IEnumerable<string>? availableResourceIds,
        IEnumerable<string>? availableHostCapabilities) =>
        Validate(pack, availableResourceIds, availableHostCapabilities, checkExternalAvailability: true);

    internal static PresetCardPackValidationResult Validate(
        PresetCardPack pack,
        IEnumerable<string>? availableResourceIds,
        IEnumerable<string>? availableHostCapabilities,
        bool checkExternalAvailability)
    {
        var errors = new List<string>();
        if (pack is null)
            return new PresetCardPackValidationResult(false, new[] { "Pack is required." });

        if (!string.Equals(pack.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
            errors.Add($"Unsupported schema version '{pack.SchemaVersion}'.");
        ValidateId(pack.PackId, "pack ID", errors);
        ValidateDisplayName(pack.DisplayName, "pack display name", errors);

        var capabilities = ValidateIdentifiers(pack.RequiredHostCapabilities, "host capability", errors, "duplicate host capability");
        if (checkExternalAvailability)
        {
            var availableCapabilities = ToSet(availableHostCapabilities);
            foreach (var capability in capabilities)
                if (!availableCapabilities.Contains(capability))
                    errors.Add($"Missing host capability '{capability}'.");
        }

        if (pack.Cards is null)
        {
            errors.Add("Cards are required.");
            return new PresetCardPackValidationResult(false, errors);
        }

        var cardIds = new HashSet<string>(IdComparer);
        var declaredResources = new HashSet<string>(IdComparer);
        foreach (var card in pack.Cards)
        {
            if (card is null)
            {
                errors.Add("Card cannot be null.");
                continue;
            }
            ValidateId(card.CardId, "card ID", errors);
            if (!cardIds.Add(card.CardId ?? string.Empty))
                errors.Add($"Duplicate card ID '{card.CardId}'.");
            ValidateDisplayName(card.DisplayName, $"card '{card.CardId}' display name", errors);
            if (!Enum.IsDefined(card.Kind))
                errors.Add($"Card '{card.CardId}' has an unsupported category.");

            var cardResources = new HashSet<string>(IdComparer);
            if (card.Resources is null)
            {
                errors.Add($"Card '{card.CardId}' resources are required.");
            }
            else
            {
                foreach (var resource in card.Resources)
                {
                    if (resource is null)
                    {
                        errors.Add($"Card '{card.CardId}' contains a null resource.");
                        continue;
                    }
                    ValidateId(resource.ResourceId, "resource ID", errors);
                    ValidateId(resource.ResourceType, $"resource '{resource.ResourceId}' type", errors);
                    if (!cardResources.Add(resource.ResourceId ?? string.Empty))
                        errors.Add($"Duplicate resource ID '{resource.ResourceId}' in card '{card.CardId}'.");
                    declaredResources.Add(resource.ResourceId ?? string.Empty);
                }
            }

            if (card.Steps is null)
            {
                errors.Add($"Card '{card.CardId}' steps are required.");
                continue;
            }
            foreach (var step in card.Steps)
            {
                if (step is null)
                {
                    errors.Add($"Card '{card.CardId}' contains a null step.");
                    continue;
                }
                if (!Enum.IsDefined(step.Kind))
                    errors.Add($"Card '{card.CardId}' contains an unsupported step kind.");
                if (step.ResourceId is not null)
                {
                    ValidateId(step.ResourceId, "resource ID", errors);
                    if (!cardResources.Contains(step.ResourceId))
                        errors.Add($"Step in card '{card.CardId}' references undeclared resource '{step.ResourceId}'.");
                }
                if (step.ActionId is not null)
                    ValidateId(step.ActionId, "action ID", errors);
                if (step.LanguageCode is not null)
                    ValidateId(step.LanguageCode, "language code", errors);
                if (step.DurationMs is < 0)
                    errors.Add($"Step in card '{card.CardId}' has a negative duration.");
            }
        }

        if (checkExternalAvailability)
        {
            var availableResources = ToSet(availableResourceIds);
            foreach (var resource in declaredResources)
                if (!availableResources.Contains(resource))
                    errors.Add($"Missing resource ID '{resource}'.");
        }

        return new PresetCardPackValidationResult(errors.Count == 0, errors);
    }

    private static HashSet<string> ToSet(IEnumerable<string>? values) =>
        new((values ?? Array.Empty<string>()).Where(value => value is not null), IdComparer);

    private static List<string> ValidateIdentifiers(
        IEnumerable<string>? values,
        string label,
        List<string> errors,
        string duplicateLabel)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(IdComparer);
        if (values is null)
        {
            errors.Add($"{label}s are required.");
            return result;
        }
        foreach (var value in values)
        {
            ValidateId(value, label, errors);
            if (!seen.Add(value ?? string.Empty))
                errors.Add($"{duplicateLabel} '{value}'.");
            else
                result.Add(value ?? string.Empty);
        }
        return result;
    }

    private static void ValidateDisplayName(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{label} is required.");
    }

    private static void ValidateId(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
            return;
        }
        if (value is "." or ".." || value.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
            errors.Add($"Invalid {label} '{value}'; resource IDs are logical IDs, not paths or commands.");
    }
}
