using Azurite.Core;

var transition = new PresetCardTemplate("transition.fade", PresetCardKind.Transition, "Fade",
    new[] { new PresetCardResource("transition.fade", "transition") },
    new[] { new PresetCardStep(PresetCardStepKind.ApplyTransition, "transition.fade", null, null, null) });
var sound = new PresetCardTemplate("sfx.confirm", PresetCardKind.SoundEffect, "Confirm",
    new[] { new PresetCardResource("sfx.confirm", "sfx") },
    new[] { new PresetCardStep(PresetCardStepKind.PlaySound, "sfx.confirm", null, null, null) });
var pack = new PresetCardPack("1", "pack", "Pack", new[] { "transition.apply", "audio.play" }, new[] { transition, sound });
var result = NativePresetCardPackMapper.TryMap(pack,
    new NativePresetResourceCatalog(new Dictionary<string,uint> { ["transition.fade"] = 7 }, new Dictionary<string,string> { ["sfx.confirm"] = "ui_confirm" }),
    new[] { "transition.apply", "audio.play" });
if (!result.IsSuccess) return Fail(string.Join(";", result.Errors));
if (result.Plan!.Scenario.Scripts.Count != 2) return Fail("expected two scripts");
var first = result.Plan.Scenario.Scripts[0];
if (first.LineId != "transition.fade" || first.transition != 7 || first.text != "" || first.isDialogScript != true || first.speakerSlotNum != -1) return Fail("transition ScriptData fields not mapped");
var second = result.Plan.Scenario.Scripts[1];
if (second.sound != "ui_confirm" || second.bgName != 0 || second.characters.Count != 0 || second.highlightedSlotNums.Count != 0) return Fail("sound ScriptData fields not preserved");
var unsupported = pack with { Cards = new[] { new PresetCardTemplate("combat.hit", PresetCardKind.Combat, "Hit", new[] { new PresetCardResource("sfx.confirm", "sfx") }, new[] { new PresetCardStep(PresetCardStepKind.StartCombat, "sfx.confirm", "hit", null, null) }) } };
var rejected = NativePresetCardPackMapper.TryMap(unsupported, new NativePresetResourceCatalog(new Dictionary<string,uint>(), new Dictionary<string,string> { ["sfx.confirm"] = "ui_confirm" }), new[] { "audio.play" });
if (rejected.IsSuccess || !rejected.Errors.Any(e => e.Contains("combat", StringComparison.OrdinalIgnoreCase))) return Fail("unsupported combat accepted");
Console.WriteLine("PASS native card pack mapping"); return 0;
static int Fail(string message) { Console.WriteLine("FAIL " + message); return 1; }
