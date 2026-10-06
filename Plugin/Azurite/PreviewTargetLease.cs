using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Azurite;

internal sealed class PreviewTargetLease : IDisposable
{
	private const int MaximumInspectorTextures = 8192;

	private readonly Action<string>? _report;
	private readonly PreviewTextureRegistry _textures;
	private double _observationTime;

	private ScriptNodeInspector? _inspector;

	private Test? _preview;

	private Camera? _camera;

	private Camera? _cadenceSuspendedCamera;

	private UITexture? _consumer;

	private RenderTexture? _original;

	private RenderTexture? _owned;

	private PreviewResolutionPlan _candidate;

	private PreviewResolutionPlan _current;

	private double _candidateSince;

	private double _nextDiscovery;

	private double _nextMeasure;

	private bool _blocked;

	private bool _disposed;

	private bool _bindingReady;

	private readonly List<UITexture> _foreignConsumers = new List<UITexture>();

	public string Reason { get; private set; } = "not observed";

	public int TextureCount { get; private set; } = -1;

	public PreviewTargetLease(Action<string>? report = null, PreviewTextureRegistry? textures = null)
	{
		_report = report;
		_textures = textures ?? new PreviewTextureRegistry();
	}

	public void Update(double now, bool allowed, Camera? cadenceSuspendedCamera = null)
	{
		_observationTime = now;
		_cadenceSuspendedCamera = cadenceSuspendedCamera;
		if (_disposed)
		{
			return;
		}
		if (!allowed || !double.IsFinite(now))
		{
			Reason = ((!allowed) ? "host or configuration did not allow target matching" : "invalid observation time");
			Restore();
			ClearCandidate();
			return;
		}
		if (_blocked)
		{
			Restore();
			return;
		}
		try
		{
			if (now >= _nextDiscovery)
			{
				_nextDiscovery = now + 1.0;
				_bindingReady = Discover();
				if (!_bindingReady)
				{
					Restore();
					ClearCandidate();
					return;
				}
			}
			if (!_bindingReady || _inspector == null || !_inspector.isActiveAndEnabled || _inspector.loading || _inspector.unloading || _preview == null || !_preview.isActiveAndEnabled || !_preview.previewMode || !CameraReady(_camera) || _consumer == null || !_consumer.isActiveAndEnabled || !_consumer.isVisible || _original == null)
			{
				if (_bindingReady)
				{
					Reason = "cached preview lifecycle not ready";
				}
				Restore();
				ClearCandidate();
			}
			else if (!OwnsKnownReferences())
			{
				Block("preview target changed externally");
			}
			else
			{
				if (now < _nextMeasure)
				{
					return;
				}
				_nextMeasure = now + 0.1;
				if (!Measure(_consumer, out var width, out var height) || !PreviewResolutionPlan.TryCreate(_original.width, _original.height, width, height, out var plan))
				{
					Reason = "viewport measurement invalid or no meaningful resolution saving";
					Restore();
					ClearCandidate();
				}
				else if (_owned != null && plan == _current)
				{
					Reason = "matched preview target active";
					ClearCandidate();
				}
				else if (_owned != null && !_current.DiffersByFivePercent(plan) && width <= (double)_current.Width && height <= (double)_current.Height)
				{
					Reason = "within resolution hysteresis";
					ClearCandidate();
				}
				else if (!(_owned != null) || (!(width > (double)_current.Width) && !(height > (double)_current.Height)) || Restore())
				{
					if (_candidate != plan)
					{
						Reason = "waiting for stable preview viewport";
						_candidate = plan;
						_candidateSince = now;
					}
					else if (!(now - _candidateSince < 0.5))
					{
						Apply(plan);
						ClearCandidate();
					}
				}
			}
		}
		catch (Exception ex)
		{
			Block("preview target failed: " + ex.GetType().Name);
		}
	}

	private bool Discover()
	{
		TextureCount = -1;
		ScriptNodeInspector instance = ScriptNodeInspector.instance;
		if (instance == null)
		{
			return Reject("inspector instance unavailable");
		}
		if (!instance.isActiveAndEnabled)
		{
			return Reject("inspector inactive");
		}
		Test preview = instance.preview;
		if (preview == null)
		{
			return Reject("inspector preview unavailable");
		}
		if (!preview.isActiveAndEnabled)
		{
			return Reject("preview controller inactive");
		}
		if (!preview.previewMode)
		{
			return Reject("previewMode false");
		}
		if (preview.background == null)
		{
			return Reject("preview background unavailable");
		}
		Camera camera = preview.background.anchorCamera ?? UICamera.FindCameraForLayer(preview.background.gameObject.layer)?.cachedCamera;
		if (camera == null)
		{
			return Reject("preview layer camera unavailable");
		}
		if (!CameraReady(camera))
		{
			return Reject("preview camera inactive or disabled");
		}
		if (!string.Equals(camera.gameObject.scene.name, "PreviewScene", StringComparison.Ordinal))
		{
			return Reject("camera scene does not match PreviewScene");
		}
		UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
		if (cameraData == null || cameraData.renderType != CameraRenderType.Base || (cameraData.cameraStack?.Count ?? -1) != 0)
		{
			return Reject("preview camera must be a URP Base with an empty stack");
		}
		RenderTexture targetTexture = camera.targetTexture;
		if (targetTexture == null)
		{
			return Reject("camera has no render target");
		}
		if (targetTexture.width < 16 || targetTexture.height < 16)
		{
			return Reject("render target dimensions unsupported");
		}
		if (_owned != null && Matches(_camera, camera) && !Matches(targetTexture, _owned))
		{
			Block("preview camera target changed externally");
			return false;
		}
		if (!(_owned != null && Matches(camera, _camera) && Matches(targetTexture, _owned)) &&
			!string.Equals(targetTexture.name, "Preview", StringComparison.Ordinal))
		{
			return Reject("render target name unsupported");
		}
		if (!FullRect(camera.rect))
		{
			return Reject("preview camera viewport is not full target");
		}
		Il2CppArrayBase<UITexture> componentsInChildren = _textures.Read(instance, _observationTime);
		TextureCount = componentsInChildren?.Length ?? (-1);
		if (componentsInChildren == null)
		{
			return Reject("inspector texture enumeration unavailable");
		}
		if (componentsInChildren.Length > 8192)
		{
			return Reject("inspector texture safety limit exceeded");
		}
		UITexture uITexture = null;
		bool flag = false;
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			UITexture uITexture2 = componentsInChildren[i];
			if (uITexture2 == null || !Matches(uITexture2.mainTexture, targetTexture))
			{
				continue;
			}
			if (_owned != null && Matches(targetTexture, _owned) && !Matches(uITexture2, _consumer))
			{
				bool flag2 = false;
				foreach (UITexture foreignConsumer in _foreignConsumers)
				{
					flag2 |= Matches(foreignConsumer, uITexture2);
				}
				if (!flag2)
				{
					_foreignConsumers.Add(uITexture2);
				}
			}
			if (uITexture != null)
			{
				flag = true;
			}
			else
			{
				uITexture = uITexture2;
			}
		}
		if (flag)
		{
			if (_owned != null)
			{
				Block("another preview consumer started using the owned target");
			}
			return Reject("multiple render target consumers");
		}
		if (uITexture == null)
		{
			return Reject("no matching inspector texture consumer");
		}
		if (!uITexture.isActiveAndEnabled || !uITexture.isVisible)
		{
			return Reject("preview texture consumer inactive or invisible");
		}
		if (!FullRect(uITexture.uvRect))
		{
			return Reject("preview consumer uses partial texture UVs");
		}
		if (_owned != null && (!Matches(_camera, camera) || !Matches(_consumer, uITexture) || !Matches(targetTexture, _owned)))
		{
			if (!Restore())
			{
				return Reject("previous target could not be restored");
			}
			targetTexture = camera.targetTexture;
			if (targetTexture == null || !Matches(uITexture.mainTexture, targetTexture))
			{
				return Reject("camera consumer target mismatch after restore");
			}
		}
		if (_owned == null)
		{
			_inspector = instance;
			_preview = preview;
			_camera = camera;
			_consumer = uITexture;
			_original = targetTexture;
		}
		Reason = "preview binding verified";
		return true;
	}

	private bool Reject(string reason)
	{
		Reason = reason;
		return false;
	}

	private bool CameraReady(Camera? camera) => camera != null && camera.gameObject.activeInHierarchy &&
		(camera.enabled || Matches(camera, _cadenceSuspendedCamera));

	private bool OwnsKnownReferences()
	{
		RenderTexture renderTexture = _owned ?? _original;
		if (renderTexture != null && _camera != null && _consumer != null && Matches(_camera.targetTexture, renderTexture))
		{
			return Matches(_consumer.mainTexture, renderTexture);
		}
		return false;
	}

	private static bool Measure(UITexture consumer, out double width, out double height)
	{
		width = (height = 0.0);
		Camera camera = consumer.anchorCamera ?? UICamera.FindCameraForLayer(consumer.gameObject.layer)?.cachedCamera;
		Il2CppStructArray<Vector3> worldCorners = consumer.worldCorners;
		if (camera == null || camera.targetTexture != null || worldCorners == null || worldCorners.Length != 4)
		{
			return false;
		}
		double num = double.PositiveInfinity;
		double num2 = double.PositiveInfinity;
		double num3 = double.NegativeInfinity;
		double num4 = double.NegativeInfinity;
		for (int i = 0; i < 4; i++)
		{
			Vector3 vector = camera.WorldToScreenPoint(worldCorners[i]);
			if (!float.IsFinite(vector.x) || !float.IsFinite(vector.y) || !float.IsFinite(vector.z) || vector.z < 0f)
			{
				return false;
			}
			num = Math.Min(num, vector.x);
			num3 = Math.Max(num3, vector.x);
			num2 = Math.Min(num2, vector.y);
			num4 = Math.Max(num4, vector.y);
		}
		width = num3 - num;
		height = num4 - num2;
		if (width > 0.0)
		{
			return height > 0.0;
		}
		return false;
	}

	private void Apply(PreviewResolutionPlan desired)
	{
		if (_original == null || _camera == null || _consumer == null || !OwnsKnownReferences() || (_owned != null && !Restore()))
		{
			return;
		}
		RenderTextureDescriptor descriptor = _original.descriptor;
		descriptor.width = desired.Width;
		descriptor.height = desired.Height;
		if (descriptor.dimension != TextureDimension.Tex2D || descriptor.volumeDepth != 1 || descriptor.useDynamicScale || descriptor.enableRandomWrite)
		{
			Reason = "render target descriptor unsupported";
			return;
		}
		_owned = new RenderTexture(descriptor);
		_owned.name = "Azurite.EmbeddedPreview";
		_owned.filterMode = _original.filterMode;
		_owned.wrapModeU = _original.wrapModeU;
		_owned.wrapModeV = _original.wrapModeV;
		_owned.anisoLevel = _original.anisoLevel;
		if (!_owned.Create())
		{
			throw new InvalidOperationException("preview target creation failed");
		}
		Graphics.Blit(_original, _owned);
		_camera.targetTexture = _owned;
		_consumer.mainTexture = _owned;
		if (!OwnsKnownReferences())
		{
			throw new InvalidOperationException("preview target assignment did not remain in effect");
		}
		_current = desired;
		Reason = "matched preview target active";
		_report?.Invoke($"embedded preview target {_original.width}x{_original.height} -> {desired.Width}x{desired.Height}; source descriptor and aspect retained");
	}

	public bool Restore()
	{
		if (_owned == null)
		{
			return true;
		}
		try
		{
			if (_original == null)
			{
				_blocked = true;
				return false;
			}
			// Export may acquire between discovery polls. Recheck the live consumer
			// set before releasing a GPU resource rather than trusting the last poll.
			bool liveForeignConsumer = false;
			if (_inspector != null)
			{
				var consumers = _textures.Read(_inspector, _observationTime, force: true);
				if (consumers == null || consumers.Length > MaximumInspectorTextures)
				{
					_blocked = true;
					return false;
				}
				for (int i = 0; i < consumers.Length; i++)
				{
					UITexture consumer = consumers[i];
					if (consumer != null && !Matches(consumer, _consumer) && Matches(consumer.mainTexture, _owned))
					{
						liveForeignConsumer = true;
					}
				}
			}
			if (_camera != null && Matches(_camera.targetTexture, _owned))
			{
				_camera.targetTexture = _original;
			}
			if (_consumer != null && Matches(_consumer.mainTexture, _owned))
			{
				_consumer.mainTexture = _original;
			}
			if ((_camera != null && Matches(_camera.targetTexture, _owned)) || (_consumer != null && Matches(_consumer.mainTexture, _owned)))
			{
				_blocked = true;
				return false;
			}
			if (liveForeignConsumer)
			{
				_blocked = true;
				Reason = "another consumer still references the owned preview target";
				return false;
			}
			foreach (UITexture foreignConsumer in _foreignConsumers)
			{
				if (foreignConsumer != null && Matches(foreignConsumer.mainTexture, _owned))
				{
					_blocked = true;
					return false;
				}
			}
			_owned.Release();
			UnityEngine.Object.Destroy(_owned);
			_owned = null;
			_current = default(PreviewResolutionPlan);
			_foreignConsumers.Clear();
			return true;
		}
		catch
		{
			_blocked = true;
			return false;
		}
	}

	private void Block(string reason)
	{
		Reason = reason;
		bool num = !_blocked;
		_blocked = true;
		Restore();
		ClearCandidate();
		if (num)
		{
			_report?.Invoke(reason + "; preview target optimization suspended");
		}
	}

	private void ClearCandidate()
	{
		_candidate = default(PreviewResolutionPlan);
		_candidateSince = 0.0;
	}

	private static bool Matches(UnityEngine.Object? left, UnityEngine.Object? right)
	{
		if (left != null && right != null)
		{
			return left.Pointer == right.Pointer;
		}
		return false;
	}

	private static bool FullRect(Rect rect)
	{
		if (float.IsFinite(rect.x) && float.IsFinite(rect.y) && float.IsFinite(rect.width) && float.IsFinite(rect.height) && Math.Abs(rect.x) <= 0.0001f && Math.Abs(rect.y) <= 0.0001f && Math.Abs(rect.width - 1f) <= 0.0001f)
		{
			return Math.Abs(rect.height - 1f) <= 0.0001f;
		}
		return false;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			Restore();
		}
	}
}
