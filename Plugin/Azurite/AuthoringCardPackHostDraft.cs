using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Azurite.Core;
using Studio.Scripts;

namespace Azurite;

internal sealed record AuthoringCardPackDraftTemplate(CardPackV2Draft Draft, bool IsBuiltInExample, string Description);

/// <summary>
/// Builds unverified V2 data using the actual selected slot and explicit picker
/// choices. Only Workflow.Preview/Confirm may capture capabilities and route native
/// recovery state. Safe to read from Panel without per-frame native preflights.
/// </summary>
internal sealed class AuthoringCardPackHostDraft
{
    private sealed record Selection(IntPtr Inspector, string NodeId, string AnchorId, int? Slot);
    private readonly int _editorThreadId;
    private readonly Func<bool> _contextAllowed;
    private readonly Func<IReadOnlyList<AuthoringCardPackResourceChoice>> _selectedResources;
    private readonly string _transitionKey, _soundKey;
    private bool _busy;

    internal AuthoringCardPackHostDraft(int editorThreadId, Func<bool> contextAllowed,
        Func<IReadOnlyList<AuthoringCardPackResourceChoice>> selectedResources, string transitionKey, string soundKey)
    {
        if (editorThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(editorThreadId));
        _editorThreadId = editorThreadId;
        _contextAllowed = contextAllowed ?? throw new ArgumentNullException(nameof(contextAllowed));
        _selectedResources = selectedResources ?? throw new ArgumentNullException(nameof(selectedResources));
        _transitionKey = transitionKey ?? throw new ArgumentNullException(nameof(transitionKey));
        _soundKey = soundKey ?? throw new ArgumentNullException(nameof(soundKey));
    }

    internal CardPackV2Draft Build(PresetCardKind kind, string? action = null,
        IReadOnlyDictionary<string, string>? variants = null, string? locale = null) => Run(() =>
    {
        var selection = ReadSelection();
        var card = new CardV2("card", kind,
            ResourceKey: kind == PresetCardKind.Transition ? _transitionKey : kind is PresetCardKind.SoundEffect or PresetCardKind.Combat ? _soundKey : null,
            TargetSlot: kind is PresetCardKind.Action or PresetCardKind.Combat or PresetCardKind.Language ? selection.Slot : null,
            Action: action, Variants: CopyVariants(variants));
        return Validate(new("2", "azurite.ui.card", "卡包草稿（待预览验证）", new[] { card }), selection, locale);
    });

    /// <summary>
    /// Explicit built-in example, not a claim of user input or automatic translation.
    /// Callers may replace both supplied texts before generating the immutable draft.
    /// </summary>
    internal AuthoringCardPackDraftTemplate BuildFiveFamilyExample(
        IReadOnlyDictionary<string, string>? editedVariants = null, string locale = "zh-CN") => Run(() =>
    {
        var selection = ReadSelection();
        var variants = CopyVariants(editedVariants) ?? new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zh-CN"] = "老师，我们出发吧。", ["en"] = "Sensei, let's go."
        };
        var cards = new[]
        {
            new CardV2("transition", PresetCardKind.Transition, ResourceKey: _transitionKey),
            new CardV2("combat", PresetCardKind.Combat, ResourceKey: _soundKey, TargetSlot: selection.Slot, Action: "Stiff"),
            new CardV2("action", PresetCardKind.Action, TargetSlot: selection.Slot, Action: "Greeting"),
            new CardV2("language", PresetCardKind.Language, TargetSlot: selection.Slot, Variants: variants),
            new CardV2("sound", PresetCardKind.SoundEffect, ResourceKey: _soundKey)
        };
        var draft = Validate(new("2", "azurite.ui.five-family-example", "五类内置示例（台词可编辑，待预览验证）", cards), selection, locale);
        return new AuthoringCardPackDraftTemplate(draft, true, "内置示例：简中和英文台词均可编辑；不会自动翻译或切换界面语言。资源能力须经预览验证。");
    });

    private CardPackV2Draft Validate(CardPackV2 pack, Selection selection, string? locale)
    {
        string json = CardPackV2Json.Serialize(pack);
        var choices = (_selectedResources() ?? throw new InvalidOperationException("Explicit picker choices are unavailable.")).ToArray();
        var bindings = new Dictionary<string, AuthoringCardPackResourceChoice>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            Require(choice != null && !string.IsNullOrWhiteSpace(choice.ResourceKey) && !string.IsNullOrWhiteSpace(choice.ResourceId) &&
                Enum.IsDefined(typeof(CardResourceKind), choice.Kind), "Resource choices must contain explicit logical keys and returned native IDs.");
            Require(bindings.TryAdd(choice.ResourceKey, choice) && ids.Add(choice.ResourceId), "Duplicate resource binding identity is ambiguous.");
        }
        foreach (var card in pack.Cards)
        {
            if (card.ResourceKey != null)
            {
                var expected = card.Kind == PresetCardKind.Transition ? CardResourceKind.Transition : CardResourceKind.Sound;
                Require(bindings.TryGetValue(card.ResourceKey, out var binding) && binding.Kind == expected,
                    "RESOURCE_MISSING: explicitly choose the required " + expected + " resource before preview.");
            }
            if (card.Kind == PresetCardKind.Language)
                Require(locale != null && card.Variants != null && card.Variants.ContainsKey(locale), "LOCALE_MISSING: selected locale has no supplied dialogue text.");
        }
        Require(ReadSelection() == selection && _contextAllowed(), "Native target/anchor or draft qualification changed while reading inputs.");
        // No Host.Capture, Resolve, Check, Apply or Recover here. In particular a
        // persistent host fault must reach Workflow's structured recovery handler.
        return new(json, locale);
    }

    private static Selection ReadSelection()
    {
        var inspector = ScriptNodeInspector.instance;
        var node = inspector?.scriptNode;
        var row = inspector?.selectedScriptItem;
        var cache = inspector?.scriptNodeListItemsCache;
        Require(inspector != null && inspector.isActiveAndEnabled && inspector.gameObject.activeInHierarchy &&
            !inspector.loading && !inspector.unloading && !inspector.rearrangeScheduled && node != null && row != null && row.selected &&
            row.inspector?.Pointer == inspector.Pointer && row.scriptNode?.Pointer == node.Pointer && node.scripts != null && cache != null &&
            row.index >= 0 && row.index < node.scripts.Count && row.index < cache.Count && cache[row.index]?.Pointer == row.Pointer,
            "Draft requires an explicitly selected current native anchor.");
        string id = node!.scripts[row!.index].LineId;
        Require(!string.IsNullOrWhiteSpace(id), "Native anchor LineId is missing.");
        int slot = inspector!.selectedSlotNum;
        return new(inspector.Pointer, node.guid.ToString(), id, slot is >= 1 and <= 5 ? slot : null);
    }
    private T Run<T>(Func<T> work)
    {
        Require(Environment.CurrentManagedThreadId == _editorThreadId, "Draft builder requires its qualified editor thread.");
        Require(!_busy && _contextAllowed(), "Draft generation is busy or unqualified.");
        _busy = true;
        try { return work(); } finally { _busy = false; }
    }
    private static IReadOnlyDictionary<string, string>? CopyVariants(IReadOnlyDictionary<string, string>? source) =>
        source == null ? null : new Dictionary<string, string>(source, StringComparer.Ordinal);
    private static void Require([DoesNotReturnIf(false)] bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
