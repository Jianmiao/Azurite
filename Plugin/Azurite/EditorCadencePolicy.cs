using System;
using Azurite.Core;

namespace Azurite;

internal static class EditorCadencePolicy
{
	internal static bool UsePreviewFallback(bool onChangeReady, bool previewEnabled,
		HostActivitySnapshot activity, double previewFps) =>
		!onChangeReady && previewEnabled && activity.HasPreview && activity.CanThrottle &&
		double.IsFinite(previewFps) && previewFps >= 30 && previewFps <= 120;

	internal static CadencePlan Resolve(double updateFps, bool onChangeReady, VisualCadencePlan visual,
		bool previewFallback, double previewFps, bool measured, double idleFps, double deepFps,
		int idleInterval, int deepInterval, bool ambientPreview = false, double ambientFps = 60)
	{
		if (ambientPreview)
			return double.IsFinite(ambientFps) && ambientFps >= 30 && ambientFps <= 120
				? CadenceResolver.Resolve(updateFps, ambientFps, ambientFps, 4096)
				: new CadencePlan(false, 1, 1, "invalid-ambient-cadence");
		if (onChangeReady && visual.Valid)
			return CadenceResolver.Resolve(updateFps, visual.RenderFps, visual.RenderFps, 4096);
		if (previewFallback)
			return CadenceResolver.Resolve(updateFps, previewFps, previewFps, 4096);
		return measured ? CadenceResolver.Resolve(updateFps, idleFps, deepFps) :
			new CadencePlan(idleInterval >= 1 && deepInterval >= idleInterval, idleInterval, deepInterval, "manual-intervals");
	}

	internal static bool IsProtected(HostActivitySnapshot activity, bool scrolling, bool mutating, CadencePlan plan) =>
		scrolling || mutating || !activity.IsEditor || !activity.CanThrottle ||
		(activity.HasDynamicPreview && !activity.AmbientPreview) || !plan.Valid;
}
