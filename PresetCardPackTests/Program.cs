using Azurite.Core;

var passed = 0;
var failed = 0;

void Check(bool condition, string name)
{
    if (condition)
    {
        passed++;
        Console.WriteLine("PASS " + name);
    }
    else
    {
        failed++;
        Console.WriteLine("FAIL " + name);
    }
}

PresetCardPack SamplePack() => new(
    SchemaVersion: "1",
    PackId: "scene.basics",
    DisplayName: "基础场景卡包",
    RequiredHostCapabilities: new[] { "dialogue.edit", "audio.play", "transition.apply" },
    Cards: new[]
    {
        new PresetCardTemplate(
            CardId: "transition.fade",
            Kind: PresetCardKind.Transition,
            DisplayName: "淡入淡出",
            Resources: new[] { new PresetCardResource("transition.fade", "transition") },
            Steps: new[] { new PresetCardStep(PresetCardStepKind.ApplyTransition, "transition.fade", null, null, 250) }),
        new PresetCardTemplate(
            CardId: "combat.hit",
            Kind: PresetCardKind.Combat,
            DisplayName: "命中",
            Resources: new[] { new PresetCardResource("sfx.hit", "sfx") },
            Steps: new[] { new PresetCardStep(PresetCardStepKind.PlaySound, "sfx.hit", null, null, null) }),
        new PresetCardTemplate(
            CardId: "action.raise-hand",
            Kind: PresetCardKind.Action,
            DisplayName: "抬手",
            Resources: Array.Empty<PresetCardResource>(),
            Steps: new[] { new PresetCardStep(PresetCardStepKind.ApplyAction, null, "raise-hand", null, null) }),
        new PresetCardTemplate(
            CardId: "language.zh",
            Kind: PresetCardKind.Language,
            DisplayName: "简体中文",
            Resources: Array.Empty<PresetCardResource>(),
            Steps: new[] { new PresetCardStep(PresetCardStepKind.SetLanguage, null, null, "zh-CN", null) }),
        new PresetCardTemplate(
            CardId: "sfx.ui",
            Kind: PresetCardKind.SoundEffect,
            DisplayName: "界面音效",
            Resources: new[] { new PresetCardResource("sfx.confirm", "sfx") },
            Steps: new[] { new PresetCardStep(PresetCardStepKind.PlaySound, "sfx.confirm", null, null, null) })
    });

var sample = SamplePack();
var builtIn = PresetCardPackCatalog.CreateBuiltInBasics();
Check(builtIn.Cards.Select(card => card.Kind).Distinct().Count() == 5 &&
      builtIn.Cards.Count == 5 && builtIn.PackId == "azurite.basics",
    "built-in basics pack contains transition, combat, action, language and sound-effect cards");
Check(PresetCardPackValidator.Validate(builtIn, Array.Empty<string>(), Array.Empty<string>()).IsValid == false,
    "built-in pack requires its declared external resources before insertion");
var json = PresetCardPackJson.Serialize(sample);
var roundTrip = PresetCardPackJson.Deserialize(json);
Check(PresetCardPackJson.Serialize(roundTrip) == json &&
      roundTrip.PackId == sample.PackId &&
      roundTrip.Cards.Count == sample.Cards.Count &&
      roundTrip.Cards[2].Steps[0].ActionId == "raise-hand",
    "card pack JSON roundtrip preserves the complete contract");
Check(json == PresetCardPackJson.Serialize(roundTrip), "serializing the same pack is deterministic");

var valid = PresetCardPackValidator.Validate(
    sample,
    availableResourceIds: new[] { "transition.fade", "sfx.hit", "sfx.confirm" },
    availableHostCapabilities: new[] { "dialogue.edit", "audio.play", "transition.apply" });
Check(valid.IsValid, "five built-in card categories validate with available resources and host capabilities");

var missingResource = PresetCardPackValidator.Validate(
    sample,
    availableResourceIds: new[] { "transition.fade", "sfx.confirm" },
    availableHostCapabilities: new[] { "dialogue.edit", "audio.play", "transition.apply" });
Check(!missingResource.IsValid && missingResource.Errors.Any(e => e.Contains("sfx.hit", StringComparison.Ordinal)),
    "missing resource IDs reject the entire pack before insertion");

var missingCapability = PresetCardPackValidator.Validate(
    sample,
    availableResourceIds: new[] { "transition.fade", "sfx.hit", "sfx.confirm" },
    availableHostCapabilities: new[] { "dialogue.edit", "audio.play" });
Check(!missingCapability.IsValid && missingCapability.Errors.Any(e => e.Contains("transition.apply", StringComparison.Ordinal)),
    "missing host capabilities reject the pack before insertion");

var unsafeId = sample with
{
    Cards = new[]
    {
        sample.Cards[0] with
        {
            Resources = new[] { new PresetCardResource("..\\run.ps1", "transition") },
            Steps = new[] { new PresetCardStep(PresetCardStepKind.ApplyTransition, "..\\run.ps1", null, null, 0) }
        }
    }
};
var unsafeValidation = PresetCardPackValidator.Validate(unsafeId, Array.Empty<string>(), Array.Empty<string>());
Check(!unsafeValidation.IsValid && unsafeValidation.Errors.Any(e => e.Contains("resource ID", StringComparison.OrdinalIgnoreCase)),
    "path-like resource IDs are rejected and never treated as executable paths");

var unsafeJson = "{\"schemaVersion\":\"1\",\"packId\":\"unsafe\",\"displayName\":\"Unsafe\",\"requiredHostCapabilities\":[],\"cards\":[],\"path\":\"C:\\\\temp\\\\x\"}";
var rejected = false;
try
{
    _ = PresetCardPackJson.Deserialize(unsafeJson);
}
catch (FormatException)
{
    rejected = true;
}
Check(rejected, "JSON containing path or command fields is rejected");

var duplicate = sample with
{
    Cards = new[] { sample.Cards[0], sample.Cards[0] }
};
var duplicateValidation = PresetCardPackValidator.Validate(duplicate, new[] { "transition.fade" }, new[] { "dialogue.edit", "audio.play", "transition.apply" });
Check(!duplicateValidation.IsValid && duplicateValidation.Errors.Any(e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase)),
    "duplicate card IDs are rejected deterministically");

Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
