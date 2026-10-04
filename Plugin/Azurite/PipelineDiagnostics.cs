using System;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Azurite;

internal static class PipelineDiagnostics
{
	public readonly record struct FrameTimingSample(double CpuMainThreadMs, double CpuRenderThreadMs, double GpuMs, double PresentWaitMs, uint SyncInterval);

	private static Il2CppStructArray<FrameTiming>? _timings;

	public static bool TrySample(out FrameTimingSample sample)
	{
		sample = default(FrameTimingSample);
		try
		{
			if (_timings == null)
			{
				_timings = new Il2CppStructArray<FrameTiming>(1L);
			}
			FrameTimingManager.CaptureFrameTimings();
			if (FrameTimingManager.GetLatestTimings(1u, _timings) == 0)
			{
				return false;
			}
			FrameTiming frameTiming = _timings[0];
			sample = new FrameTimingSample(frameTiming.cpuMainThreadFrameTime, frameTiming.cpuRenderThreadFrameTime, frameTiming.gpuFrameTime, frameTiming.cpuMainThreadPresentWaitTime, frameTiming.syncInterval);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool TryLog(ManualLogSource log)
	{
		bool isEnabled2;
		try
		{
			RenderPipelineAsset currentRenderPipeline = GraphicsSettings.currentRenderPipeline;
			RenderPipeline currentPipeline = RenderPipelineManager.currentPipeline;
			int renderFrameInterval = OnDemandRendering.renderFrameInterval;
			int effectiveRenderFrameRate = OnDemandRendering.effectiveRenderFrameRate;
			bool willCurrentFrameRender = OnDemandRendering.willCurrentFrameRender;
			int vSyncCount = QualitySettings.vSyncCount;
			int targetFrameRate = Application.targetFrameRate;
			Il2CppSystem.Type currentRenderPipelineAssetType = GraphicsSettings.currentRenderPipelineAssetType;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler;
			if (!TryGetUniversalAsset(currentRenderPipeline, out UniversalRenderPipelineAsset urp))
			{
				if (currentRenderPipeline == null)
				{
					bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(130, 7, out var isEnabled);
					if (isEnabled)
					{
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Azurite pipeline probe: asset=<builtin-or-null>; assetType=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentRenderPipelineAssetType?.FullName ?? "unknown");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; pipeline=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentPipeline?.GetType().FullName ?? "not-created");
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; interval=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(renderFrameInterval);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; effectiveFps=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(effectiveRenderFrameRate);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; willRender=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(willCurrentFrameRender);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; targetFps=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(targetFrameRate);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; vsync=");
						bepInExInfoLogInterpolatedStringHandler.AppendFormatted(vSyncCount);
						bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
					}
					log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
					return false;
				}
				bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(126, 8, out isEnabled2);
				if (isEnabled2)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Azurite pipeline probe pending: asset type ");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentRenderPipeline.GetType().FullName);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; assetType=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentRenderPipelineAssetType?.FullName ?? "unknown");
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; pipeline=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentPipeline?.GetType().FullName ?? "not-created");
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; interval=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(renderFrameInterval);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; effectiveFps=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(effectiveRenderFrameRate);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; willRender=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(willCurrentFrameRender);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; targetFps=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(targetFrameRate);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; vsync=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(vSyncCount);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				return false;
			}
			ScriptableRendererData scriptableRendererData = urp.scriptableRendererData;
			if (scriptableRendererData == null)
			{
				bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(112, 6, out isEnabled2);
				if (isEnabled2)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Azurite pipeline probe: asset=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(urp.name);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; rendererData=pending; interval=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(renderFrameInterval);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; effectiveFps=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(effectiveRenderFrameRate);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; willRender=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(willCurrentFrameRender);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; targetFps=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(targetFrameRate);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; vsync=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(vSyncCount);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				return false;
			}
			int t = scriptableRendererData.rendererFeatures?.Count ?? (-1);
			bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(153, 11, out isEnabled2);
			if (isEnabled2)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("Azurite pipeline probe: asset=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(urp.name);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; assetType=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentRenderPipelineAssetType?.FullName ?? "unknown");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; pipeline=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(currentPipeline?.GetType().FullName ?? "not-created");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; rendererData=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(scriptableRendererData.name);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; features=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(t);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; renderScale=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(urp.renderScale, "F3");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; interval=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(renderFrameInterval);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; effectiveFps=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(effectiveRenderFrameRate);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; willRender=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(willCurrentFrameRender);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; targetFps=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(targetFrameRate);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; vsync=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(vSyncCount);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(".");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			return true;
		}
		catch (System.Exception ex)
		{
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(39, 1, out isEnabled2);
			if (isEnabled2)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("Azurite pipeline probe failed closed: ");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral(".");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			return true;
		}
	}

	private static bool TryGetUniversalAsset(RenderPipelineAsset? asset, out UniversalRenderPipelineAsset urp)
	{
		if (asset is UniversalRenderPipelineAsset universalRenderPipelineAsset)
		{
			urp = universalRenderPipelineAsset;
			return true;
		}
		if ((object)asset != null && asset.Pointer != System.IntPtr.Zero)
		{
			try
			{
				UniversalRenderPipelineAsset universalRenderPipelineAsset2 = asset.TryCast<UniversalRenderPipelineAsset>();
				if (universalRenderPipelineAsset2 != null)
				{
					urp = universalRenderPipelineAsset2;
					return true;
				}
			}
			catch
			{
			}
		}
		urp = null;
		return false;
	}
}
