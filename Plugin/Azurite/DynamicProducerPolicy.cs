using System;

namespace Azurite;

internal static class DynamicProducerPolicy
{
    /// <summary>
    /// Returns whether a controller should keep the editor at its active cadence.
    /// A live Test object can remain enabled while the authoring UI is idle; in
    /// that state it is only a host object and is not evidence of playback.
    /// </summary>
    internal static bool IsDynamicForSurface(
        bool controllerActive,
        bool previewMode,
        bool editorSurface,
        bool autoAdvance,
        int scenarioAnimations,
        int backgroundAnimations,
        int screenTextAnimations,
        bool hasVoice,
        bool delayedAdvance,
        bool backgroundEffectActive)
    {
        if (!controllerActive)
        {
            return false;
        }

        if (editorSurface && !previewMode)
        {
            // The non-preview Test singleton may be resident in the authoring
            // scene. Only active producers make it a reason to keep drawing.
            return autoAdvance || scenarioAnimations > 0 || backgroundAnimations > 0 ||
                screenTextAnimations > 0 || hasVoice || delayedAdvance || backgroundEffectActive;
        }

        return IsDynamic(controllerActive, previewMode, autoAdvance, scenarioAnimations,
            backgroundAnimations, screenTextAnimations, hasVoice, delayedAdvance,
            backgroundEffectActive);
    }

    internal static bool IsKnownStaticWindowName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            return false;
        }

        return fullName.Contains("BackgroundExplorer", StringComparison.Ordinal) ||
            fullName.Contains("PopupImageExplorer", StringComparison.Ordinal) ||
            fullName.Contains("SoundExplorer", StringComparison.Ordinal) ||
            fullName.Contains("BGMExplorer", StringComparison.Ordinal) ||
            fullName.Contains("EmotionExplorer", StringComparison.Ordinal) ||
            fullName.Contains("CharacterExplorer", StringComparison.Ordinal) ||
            fullName.Contains("AdditionalPromptCommandHelp", StringComparison.Ordinal) ||
            fullName.Contains("UIPopupModManager", StringComparison.Ordinal) ||
            fullName.Contains("SettingPanel", StringComparison.Ordinal);
    }

    internal static bool IsDynamic(
        bool controllerActive,
        bool previewMode,
        bool autoAdvance,
        int scenarioAnimations,
        int backgroundAnimations,
        int screenTextAnimations,
        bool hasVoice,
        bool delayedAdvance,
        bool backgroundEffectActive)
    {
        if (!controllerActive) return false;
        if (!previewMode) return true;
        return autoAdvance || scenarioAnimations > 0 || backgroundAnimations > 0 ||
            screenTextAnimations > 0 || hasVoice || delayedAdvance || backgroundEffectActive;
    }
}
