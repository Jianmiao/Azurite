namespace Azurite;

internal static class DynamicProducerPolicy
{
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
