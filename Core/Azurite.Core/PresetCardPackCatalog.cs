using System;

namespace Azurite.Core;

/// <summary>
/// Small, resource-ID-only starter cards for the first authoring workflow.
/// They are deliberately data-only; native insertion still validates the
/// current resource catalog and host capabilities before mutating AA.
/// </summary>
public static class PresetCardPackCatalog
{
    public static PresetCardPack CreateBuiltInBasics() => new(
        SchemaVersion: "1",
        PackId: "azurite.basics",
        DisplayName: "Azurite 基础卡包",
        RequiredHostCapabilities: new[] { "dialogue.edit", "transition.apply", "action.apply", "language.set", "audio.play" },
        Cards: new[]
        {
            new PresetCardTemplate("transition.fade", PresetCardKind.Transition, "淡入淡出",
                new[] { new PresetCardResource("transition.fade", "transition") },
                new[] { new PresetCardStep(PresetCardStepKind.ApplyTransition, "transition.fade", null, null, 250) }),
            new PresetCardTemplate("combat.hit", PresetCardKind.Combat, "战斗命中",
                new[] { new PresetCardResource("sfx.hit", "sfx") },
                new[] { new PresetCardStep(PresetCardStepKind.StartCombat, "sfx.hit", "hit", null, 180) }),
            new PresetCardTemplate("action.raise-hand", PresetCardKind.Action, "抬手动作",
                Array.Empty<PresetCardResource>(),
                new[] { new PresetCardStep(PresetCardStepKind.ApplyAction, null, "raise-hand", null, null) }),
            new PresetCardTemplate("language.zh-cn", PresetCardKind.Language, "简体中文",
                Array.Empty<PresetCardResource>(),
                new[] { new PresetCardStep(PresetCardStepKind.SetLanguage, null, null, "zh-CN", null) }),
            new PresetCardTemplate("sfx.confirm", PresetCardKind.SoundEffect, "确认音效",
                new[] { new PresetCardResource("sfx.confirm", "sfx") },
                new[] { new PresetCardStep(PresetCardStepKind.PlaySound, "sfx.confirm", null, null, null) })
        });
}
