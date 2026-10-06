using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using Rendering;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Azurite;

internal sealed class EditorRenderProbe
{
	private static double _nextSample;

	private readonly Action<string> _report;

	public EditorRenderProbe(Action<string> report)
	{
		_report = report ?? throw new ArgumentNullException("report");
	}

	public void Update(double now)
	{
		if (double.IsFinite(now) && !(now < _nextSample))
		{
			_nextSample = now + 5.0;
			TryLogCore(_report);
		}
	}

	public static bool TryLog(ManualLogSource log)
	{
		double num = (double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
		if (num < _nextSample)
		{
			return false;
		}
		_nextSample = num + 5.0;
		return TryLogCore(delegate(string message)
		{
			log.LogInfo(message);
		});
	}

	private static bool TryLogCore(Action<string> log)
	{
		try
		{
			ScriptNodeInspector instance = ScriptNodeInspector.instance;
			if (instance == null || !instance.isActiveAndEnabled)
			{
				return false;
			}
			Test preview = instance.preview;
			if (preview != null)
				log($"editor preview producers: auto={preview.auto}; voice={preview.hasVoice}; delayed={preview.delayedAdvanceTask != null}; animations={AnimationActivity.CountPending(preview.currentAnims)}/{preview.currentAnims?.Count ?? 0}; backgroundAnimations={AnimationActivity.CountPending(preview.backgroundAnimations)}/{preview.backgroundAnimations?.Count ?? 0}; screenText={AnimationActivity.CountPending(preview.currentSTs)}/{preview.currentSTs?.Count ?? 0}; bgEffect={OptionalHostActivity.CurrentEffectActive(preview) || OptionalHostActivity.CustomEffectActive(preview)}; characterState={CharacterActivity.Observe(preview, true)}. Values are pending presence (0/1) / total; terminal entries do not count as work.");
			log($"editor render probe: inspector={instance.Pointer:X}; loading={instance.loading}; unloading={instance.unloading}; preview={((preview == null) ? "none" : preview.Pointer.ToString("X"))}; previewActive={preview != null && preview.isActiveAndEnabled}; previewMode={preview != null && preview.previewMode}; screen={Screen.width}x{Screen.height}.");
			GraphicsManager instance2 = Singleton<GraphicsManager>.Instance;
			if (instance2 != null)
			{
				UIRenderPassSettings uiPassSettings = instance2.uiPassSettings;
				log($"editor UI settings: manager={instance2.Pointer:X}; resolution={instance2.resolution}; renderSize={((uiPassSettings == null) ? "none" : (uiPassSettings.UIRenderWidth + "x" + uiPassSettings.UIRenderHeight))}; grabBeforeUI={uiPassSettings != null && uiPassSettings.GrabRTBeforeUI}; grabRequesters={instance2.grabTextureRequesters?.Count ?? (-1)}; dummyCamera={((instance2.dummyCamera == null) ? "none" : instance2.dummyCamera.Pointer.ToString("X"))}.");
			}
			HashSet<IntPtr> hashSet = new HashSet<IntPtr>();
			Il2CppReferenceArray<Camera> allCameras = Camera.allCameras;
			int num = allCameras?.Length ?? 0;
			log($"editor cameras: activeCount={num}; sampleLimit=64.");
			int num2 = 0;
			while (allCameras != null && num2 < Math.Min(num, 64))
			{
				Camera camera = allCameras[num2];
				if (!(camera == null))
				{
					try
					{
						RenderTexture targetTexture = camera.targetTexture;
						if (targetTexture != null)
						{
							hashSet.Add(targetTexture.Pointer);
						}
						UniversalAdditionalCameraData component = camera.GetComponent<UniversalAdditionalCameraData>();
						log($"editor camera: name={camera.name}; ptr={camera.Pointer:X}; scene={camera.gameObject.scene.name}; enabled={camera.enabled}; layer={camera.gameObject.layer}; mask=0x{camera.cullingMask:X}; rect={camera.pixelRect}; pixels={camera.pixelWidth}x{camera.pixelHeight}; depth={camera.depth}; target={Describe(targetTexture)}; URPtype={((component == null) ? "none" : component.renderType.ToString())}; post={component != null && component.renderPostProcessing}; requiresColor={component != null && component.requiresColorTexture}; requiresDepth={component != null && component.requiresDepthTexture}.");
						if (component != null && component.renderType == CameraRenderType.Base)
						{
							Il2CppSystem.Collections.Generic.List<Camera> cameraStack = component.cameraStack;
							log($"editor camera stack: base={camera.Pointer:X}; count={cameraStack?.Count ?? 0}.");
						}
					}
					catch (Exception ex)
					{
						log("editor camera sample unavailable: " + ex.GetType().Name + ".");
					}
				}
				num2++;
			}
			if (preview != null)
			{
				LogPreviewAnchor(log, "frontPanel", preview.frontPanel);
				LogPreviewAnchor(log, "background", preview.background);
			}
			Il2CppArrayBase<UITexture> componentsInChildren = instance.GetComponentsInChildren<UITexture>(includeInactive: true);
			int num3 = 0;
			while (componentsInChildren != null && num3 < Math.Min(componentsInChildren.Length, 64))
			{
				UITexture uITexture = componentsInChildren[num3];
				if (!(uITexture == null))
				{
					RenderTexture renderTexture = uITexture.mainTexture?.TryCast<RenderTexture>();
					if (!(renderTexture == null) && hashSet.Contains(renderTexture.Pointer))
					{
						Camera camera2 = uITexture.anchorCamera;
						if (camera2 == null)
						{
							camera2 = UICamera.FindCameraForLayer(uITexture.gameObject.layer)?.cachedCamera;
						}
						Il2CppStructArray<Vector3> worldCorners = uITexture.worldCorners;
						string value = "unavailable";
						if (camera2 != null && worldCorners != null && worldCorners.Length == 4)
						{
							Vector2 vector = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
							Vector2 vector2 = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
							for (int i = 0; i < 4; i++)
							{
								Vector3 vector3 = camera2.WorldToScreenPoint(worldCorners[i]);
								vector.x = Math.Min(vector.x, vector3.x);
								vector.y = Math.Min(vector.y, vector3.y);
								vector2.x = Math.Max(vector2.x, vector3.x);
								vector2.y = Math.Max(vector2.y, vector3.y);
							}
							value = $"{vector2.x - vector.x:F0}x{vector2.y - vector.y:F0}@{vector.x:F0},{vector.y:F0}";
						}
						log($"editor preview display: name={uITexture.name}; target={Describe(renderTexture)}; widget={uITexture.width}x{uITexture.height}; screenBounds={value}; visible={uITexture.isVisible}; camera={((camera2 == null) ? "none" : camera2.Pointer.ToString("X"))}.");
					}
				}
				num3++;
			}
			Il2CppSystem.Collections.Generic.List<ScriptableRendererFeature> list = (GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>())?.scriptableRendererData?.rendererFeatures;
			int num4 = 0;
			while (list != null && num4 < Math.Min(list.Count, 16))
			{
				UIRenderFeature uIRenderFeature = list[num4]?.TryCast<UIRenderFeature>();
				if (!(uIRenderFeature == null))
				{
					UIRenderPass pipelinePass = uIRenderFeature.pipelinePass;
					MXBloomSettings mXBloomSettings = uIRenderFeature.settings?.BloomSettings;
					log($"editor UI feature: name={uIRenderFeature.name}; active={uIRenderFeature.isActive}; bloom={mXBloomSettings != null && mXBloomSettings.Enable}; bloomIntensity={((mXBloomSettings == null) ? 0f : mXBloomSettings.Intensity)}; bloomDiffusion={((!(mXBloomSettings == null)) ? mXBloomSettings.Diffusion : 0)}; passScreen={((pipelinePass == null) ? "none" : (pipelinePass.screenWidth + "x" + pipelinePass.screenHeight))}; UIrt={Describe(pipelinePass?.m_UIRenderTargetHandle?.rt)}; beforeUI={Describe(pipelinePass?.beforeUIColorTexture?.rt)}; grabbed={pipelinePass?.isGrabbed ?? false}.");
				}
				num4++;
			}
			return true;
		}
		catch (Exception ex2)
		{
			log("editor render probe unavailable: " + ex2.GetType().Name + "; " + ex2.Message);
			return false;
		}
	}

	private static void LogPreviewAnchor(Action<string> log, string label, UIRect? rect)
	{
		if (!(rect == null))
		{
			Camera camera = rect.anchorCamera ?? UICamera.FindCameraForLayer(rect.gameObject.layer)?.cachedCamera;
			log($"editor preview anchor: {label}; name={rect.name}; layer={rect.gameObject.layer}; scene={rect.gameObject.scene.name}; camera={((camera == null) ? "none" : camera.Pointer.ToString("X"))}; target={Describe(camera?.targetTexture)}.");
		}
	}

	private static string Describe(RenderTexture? texture)
	{
		if (!(texture == null))
		{
			return $"{texture.name}@{texture.Pointer:X}:{texture.width}x{texture.height};msaa={texture.antiAliasing};format={texture.graphicsFormat};depth={texture.depth}";
		}
		return "screen/none";
	}
}
