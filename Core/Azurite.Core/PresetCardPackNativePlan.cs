using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;


namespace Azurite.Core;

/// <summary>Native-shaped, data-only values accepted at the AA ScriptData mapping boundary.</summary>
public sealed record NativeScriptCharacterData(
    string Name, string FaceId, int StartingPos, int EndingPos, int DisplayOrder,
    int Emoticon, int Action, int Effect, int Appear, int ShapeOverride);

public sealed record NativeScriptData(
    string LineId, string text, string popup, uint bgEffect, uint bgName,
    string bgFriendlyName, string sound, string voice, uint transition, long bgmId,
    long selectionGroup, string additionalPrompt, IReadOnlyList<NativeScriptCharacterData> characters,
    int speakerSlotNum, IReadOnlyList<int> highlightedSlotNums, bool isDialogScript,
    string placeText, bool continuous);

public sealed record NativeScenario(string ScenarioId, IReadOnlyList<NativeScriptData> Scripts);
public sealed record NativePresetCardPackPlan(NativeScenario Scenario);
public sealed record NativePresetCardPackMapResult(bool IsSuccess, NativePresetCardPackPlan? Plan, IReadOnlyList<string> Errors)
{
    public static NativePresetCardPackMapResult Fail(IEnumerable<string> errors) => new(false, null, errors.ToArray());
}

/// <summary>Only IDs already present in AA's native catalogs may be used.</summary>
public sealed class NativePresetResourceCatalog
{
    public IReadOnlyDictionary<string, uint> Transitions { get; }
    public IReadOnlyDictionary<string, string> Sounds { get; }
    public NativePresetResourceCatalog(IReadOnlyDictionary<string, uint> transitions, IReadOnlyDictionary<string, string> sounds)
    {
        Transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        Sounds = sounds ?? throw new ArgumentNullException(nameof(sounds));
    }
}

public static class NativePresetCardPackMapper
{
    public static NativePresetCardPackMapResult TryMap(PresetCardPack pack, NativePresetResourceCatalog catalog, IEnumerable<string>? availableHostCapabilities)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = new List<string>();
        var validation = PresetCardPackValidator.Validate(pack, catalog.Transitions.Keys.Concat(catalog.Sounds.Keys), availableHostCapabilities);
        if (!validation.IsValid) errors.AddRange(validation.Errors);
        if (pack?.Cards is null) return NativePresetCardPackMapResult.Fail(errors);
        var scripts = new List<NativeScriptData>();
        foreach (var card in pack.Cards)
        {
            if (card is null || card.Steps is null) continue;
            foreach (var step in card.Steps)
            {
                if (step is null) continue;
                switch (step.Kind)
                {
                    case PresetCardStepKind.ApplyTransition:
                        if (card.Kind != PresetCardKind.Transition) errors.Add($"Card '{card.CardId}' transition step does not match its category.");
                        if (step.DurationMs is not null) errors.Add($"Card '{card.CardId}' transition duration is unsupported by native ScriptData.");
                        if (step.ResourceId is null || !catalog.Transitions.TryGetValue(step.ResourceId, out var transition)) errors.Add($"Missing native transition ID '{step.ResourceId}'.");
                        else scripts.Add(DefaultScript(card.CardId) with { transition = transition });
                        break;
                    case PresetCardStepKind.PlaySound:
                        if (card.Kind != PresetCardKind.SoundEffect) errors.Add($"Card '{card.CardId}' sound step does not match its category.");
                        if (step.ResourceId is null || !catalog.Sounds.TryGetValue(step.ResourceId, out var sound)) errors.Add($"Missing native sound ID '{step.ResourceId}'.");
                        else scripts.Add(DefaultScript(card.CardId) with { sound = sound });
                        break;
                    default:
                        errors.Add($"Card '{card.CardId}' step '{step.Kind}' is unsupported by native ScriptData (combat, action, language and arbitrary effects are rejected).");
                        break;
                }
            }
        }
        return errors.Count > 0 ? NativePresetCardPackMapResult.Fail(errors) : new(true, new NativePresetCardPackPlan(new NativeScenario(pack.PackId, new ReadOnlyCollection<NativeScriptData>(scripts))), Array.Empty<string>());
    }

    private static NativeScriptData DefaultScript(string lineId) => new(lineId, "", "", 0, 0, "", "", "", 0, 0, 0, "", Array.Empty<NativeScriptCharacterData>(), -1, Array.Empty<int>(), true, "", false);
}
