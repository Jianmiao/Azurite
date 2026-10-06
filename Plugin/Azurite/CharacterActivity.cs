using System;
using Spine.Unity;

namespace Azurite;

internal enum CharacterAnimationActivity { Static, Ambient, Protected }

internal static class CharacterActivity
{
	// Compatibility callers keep the original full-rate protection for actual
	// animation. Only explicitly scoped editor callers may use Ambient cadence.
	internal static bool IsDynamic(Test controller) => Observe(controller) != CharacterAnimationActivity.Static;

	internal static CharacterAnimationActivity Observe(Test controller, bool allowAmbientPreview = false)
	{
		try
		{
		var slots = controller.slots;
		if (slots == null) return controller.previewMode ? CharacterAnimationActivity.Protected : CharacterAnimationActivity.Static;
		if (slots.Length > 64) return CharacterAnimationActivity.Protected;
		bool ambient = false;
		for (int i = 0; i < slots.Length; i++)
		{
			Character character = slots[i];
			if (character == null || !character.isActiveAndEnabled) continue;
			if ((character.animator != null && character.animator.isActiveAndEnabled) ||
				(character.posTweener != null && character.posTweener.isActiveAndEnabled) ||
				(character.animationQueue?.Count ?? 0) != 0 || AnimationActivity.CountPending(character.animationList) != 0 ||
				character.currentQueuedAnimation != null || (character.actionQueue?.Count ?? 0) != 0) return CharacterAnimationActivity.Protected;
			bool changing = character.blinkTask != null;
			var skeleton = character.anim;
			if (skeleton != null && skeleton.isActiveAndEnabled)
			{
				if (!float.IsFinite(skeleton.timeScale) || skeleton.timeScale < 0f || !character.isInitialized || !CanInspect(skeleton))
					return CharacterAnimationActivity.Protected;
				if (skeleton.timeScale != 0f)
				{
					// Enabled/nonzero timeScale only says the player can update, not
					// that it has an animation. Empty known tracks + no physics are static.
					var tracks = skeleton.AnimationState?.Tracks;
					if (tracks == null || tracks.Count < 0 || tracks.Count > 64 || tracks.Items == null || tracks.Items.Length < tracks.Count)
						return CharacterAnimationActivity.Protected;
					for (int track = 0; track < tracks.Count; track++)
					{
						var entry = tracks.Items[track];
						if (entry == null) continue;
						// Transitions/queued clips retain full cadence. Ordinary idle
						// loops and native blinking may be sampled at an explicit 30+ FPS.
						if (entry.Next != null || entry.MixingFrom != null || entry.MixingTo != null) return CharacterAnimationActivity.Protected;
						changing = true;
					}
				}
			}
			if (!changing) continue;
			if (!allowAmbientPreview || !controller.previewMode || !character.isInitialized || skeleton == null || !CanInspect(skeleton))
				return CharacterAnimationActivity.Protected;
			ambient = true;
		}
		return ambient ? CharacterAnimationActivity.Ambient : CharacterAnimationActivity.Static;
		}
		catch { return CharacterAnimationActivity.Protected; }
	}

	internal static bool CanInspect(SkeletonAnimation skeleton)
	{
		if (!skeleton.valid || skeleton.updateMode != UpdateMode.FullUpdate || skeleton.updateTiming == UpdateTiming.ManualUpdate ||
			!MeshCallbacksAbsent(skeleton) || skeleton._BeforeApply != null || skeleton._UpdateLocal != null ||
			skeleton._UpdateWorld != null || skeleton._UpdateComplete != null) return false;
		var state = skeleton.AnimationState;
		var body = skeleton.Skeleton;
		if (state == null || !float.IsFinite(state.TimeScale) || state.TimeScale < 0f || body == null) return false;
		// Physics may continue without an animation track. Until an individual
		// constraint's settling can be established, keep its normal path.
		return body.PhysicsConstraints != null && body.PhysicsConstraints.Count == 0;
	}

	internal static bool MeshCallbacksAbsent(SkeletonRenderer skeleton) => skeleton.generateMeshOverride == null &&
		skeleton.OnPostProcessVertices == null && skeleton.OnMeshAndMaterialsUpdated == null;
}
