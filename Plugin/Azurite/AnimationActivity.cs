namespace Azurite;

internal static class AnimationActivity
{
	// Completed objects may remain resident (e.g. text that stays on screen).
	// Queued/not-started objects still count as work; only terminal flags release it.
	internal static int CountPending<T>(Il2CppSystem.Collections.Generic.List<T>? animations) where T : ScenarioAnimation.ScenarioAnimation
	{
		if (animations == null) return 0;
		int count = animations.Count;
		if (count > 4096) return 1; // unknown remains protected beyond the host safety bound
		for (int i = 0; i < count; i++)
		{
			T animation = animations[i];
			if (animation == null || (!animation.hasCompleted && !animation.isCancelled)) return 1;
		}
		return 0; // presence (0/1), not a diagnostic count of all pending entries
	}
}
