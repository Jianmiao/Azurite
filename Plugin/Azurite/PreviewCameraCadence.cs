using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Azurite;

internal sealed class PreviewCameraCadence : IDisposable
{
	private const int MaximumInspectorTextures = 8192;

	private readonly Action<string>? _report;

	private Camera? _camera;

	private ScriptNodeInspector? _inspector;

	private Test? _preview;

	private UITexture? _consumer;

	private RenderTexture? _target;

	private double _nextDiscovery;

	private double _nextRender;

	private double _lastNow = double.NaN;

	private double _lastRate;

	private bool _ready;

	private bool _owned;

	private bool _lastWritten;

	private bool _blocked;

	private bool _disposed;

	private bool _wasHidden;

	private long _scheduled;

	private long _skipped;

	public long ScheduledFrames => _scheduled;

	public long SkippedFrames => _skipped;

	public string Reason { get; private set; } = "not observed";

	public int TextureCount { get; private set; } = -1;

	public PreviewCameraCadence(Action<string>? report = null)
	{
		_report = report;
	}

	public void Update(double now, bool allowed, double targetFps = 60.0)
	{
		if (_disposed)
		{
			return;
		}
		if (!allowed || !double.IsFinite(now) || !double.IsFinite(targetFps) || targetFps < 30.0 || targetFps > 120.0)
		{
			Reason = ((!allowed) ? "host or configuration did not allow cadence" : "invalid time or FPS configuration");
			Restore();
			_nextRender = 0.0;
			_lastNow = double.NaN;
			_wasHidden = false;
			return;
		}
		if (_blocked)
		{
			Restore();
			return;
		}
		try
		{
			if (_owned && _camera != null && _camera.enabled != _lastWritten)
			{
				_owned = false;
				Block("embedded preview camera enable state changed externally");
				return;
			}
			if (now >= _nextDiscovery)
			{
				_nextDiscovery = now + 1.0;
				_ready = Discover();
			}
			if (!_ready || _camera == null || _inspector == null || _inspector.loading || _inspector.unloading || _preview == null || !_preview.isActiveAndEnabled || !_preview.previewMode || _consumer == null || !_camera.gameObject.activeInHierarchy)
			{
				if (_ready)
				{
					Reason = "cached preview lifecycle not ready";
				}
				Restore();
				_nextRender = 0.0;
				_wasHidden = false;
				return;
			}
			if (!Same(_camera.targetTexture, _target) || !Same(_consumer.mainTexture, _target))
			{
				Reason = "preview target rebound; rediscovery pending";
				Restore();
				_ready = false;
				_nextDiscovery = now;
				_nextRender = 0.0;
				return;
			}
			if (!_owned)
			{
				if (!_camera.enabled)
				{
					Reason = "camera disabled by another owner";
					return;
				}
				_owned = true;
				_lastWritten = true;
			}
			if (!_inspector.isActiveAndEnabled || !_consumer.gameObject.activeInHierarchy)
			{
				Reason = "known hidden preview suppressed";
				_wasHidden = true;
				_nextRender = 0.0;
				_skipped++;
				SetEnabled(value: false);
				return;
			}
			if (!_consumer.isActiveAndEnabled || !_consumer.isVisible)
			{
				Reason = "consumer visibility uncertain";
				Restore();
				_nextRender = 0.0;
				_wasHidden = false;
				return;
			}
			if (_wasHidden)
			{
				_wasHidden = false;
				_nextRender = now;
			}
			if (!double.IsFinite(_lastNow) || now < _lastNow || targetFps != _lastRate)
			{
				_nextRender = now;
			}
			_lastNow = now;
			_lastRate = targetFps;
			double num = 1.0 / targetFps;
			Reason = "visible independent preview cadence active";
			bool flag = now + 1E-06 >= _nextRender;
			if (flag)
			{
				if (now - _nextRender >= num)
				{
					_nextRender = now + num;
				}
				else
				{
					_nextRender += num;
				}
				_scheduled++;
			}
			else
			{
				_skipped++;
			}
			SetEnabled(flag);
		}
		catch (Exception ex)
		{
			Block("preview camera cadence failed: " + ex.GetType().Name);
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
		Test test = ((instance != null) ? instance.preview : null);
		if (test == null)
		{
			return Reject("inspector preview unavailable");
		}
		if (!test.isActiveAndEnabled)
		{
			return Reject("preview controller inactive");
		}
		if (!test.previewMode)
		{
			return Reject("previewMode false");
		}
		if (test.background == null)
		{
			return Reject("preview background unavailable");
		}
		Camera camera = test.background.anchorCamera ?? UICamera.FindCameraForLayer(test.background.gameObject.layer)?.cachedCamera;
		if (camera == null)
		{
			return Reject("preview layer camera unavailable");
		}
		if (!camera.gameObject.activeInHierarchy)
		{
			return Reject("camera object inactive");
		}
		if (!string.Equals(camera.gameObject.scene.name, "PreviewScene", StringComparison.Ordinal))
		{
			return Reject("camera scene does not match PreviewScene");
		}
		if (!Same(camera, _camera) && !camera.enabled)
		{
			return Reject("new camera already disabled");
		}
		RenderTexture targetTexture = camera.targetTexture;
		if (targetTexture == null)
		{
			return Reject("camera has no render target");
		}
		if (targetTexture.name != "Preview" && targetTexture.name != "Azurite.EmbeddedPreview")
		{
			return Reject("render target name unsupported");
		}
		UniversalAdditionalCameraData component = camera.GetComponent<UniversalAdditionalCameraData>();
		if (component == null)
		{
			return Reject("URP camera data unavailable");
		}
		if (component.renderType != CameraRenderType.Base)
		{
			return Reject("preview camera is not Base");
		}
		if ((component.cameraStack?.Count ?? (-1)) != 0)
		{
			return Reject("preview camera stack nonempty or unavailable");
		}
		Il2CppArrayBase<UITexture> componentsInChildren = instance.GetComponentsInChildren<UITexture>(includeInactive: true);
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
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			UITexture uITexture2 = componentsInChildren[i];
			if (!(uITexture2 == null) && Same(uITexture2.mainTexture, targetTexture))
			{
				if (uITexture != null)
				{
					return Reject("multiple render target consumers");
				}
				uITexture = uITexture2;
			}
		}
		if (uITexture == null)
		{
			return Reject("no matching inspector texture consumer");
		}
		if (!Same(camera, _camera) && !Restore())
		{
			return Reject("previous camera could not be restored");
		}
		if (!Same(targetTexture, _target))
		{
			_nextRender = 0.0;
		}
		_camera = camera;
		_target = targetTexture;
		_inspector = instance;
		_preview = test;
		_consumer = uITexture;
		Reason = "preview binding verified";
		return true;
	}

	private bool Reject(string reason)
	{
		Reason = reason;
		return false;
	}

	private void SetEnabled(bool value)
	{
		if (_camera == null)
		{
			return;
		}
		_lastWritten = value;
		if (_camera.enabled != value)
		{
			_camera.enabled = value;
			if (_camera.enabled != value)
			{
				throw new InvalidOperationException("camera enable write rejected");
			}
		}
	}

	public bool Restore()
	{
		if (!_owned)
		{
			return true;
		}
		try
		{
			if (_camera == null)
			{
				_owned = false;
				return true;
			}
			if (_camera.enabled != _lastWritten)
			{
				_owned = false;
				_blocked = true;
				return false;
			}
			if (!_camera.enabled)
			{
				_camera.enabled = true;
			}
			if (!_camera.enabled)
			{
				return false;
			}
			_owned = false;
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
		if (num)
		{
			_report?.Invoke(reason + "; embedded preview cadence suspended");
		}
	}

	private static bool Same(UnityEngine.Object? left, UnityEngine.Object? right)
	{
		if (left != null && right != null)
		{
			return left.Pointer == right.Pointer;
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
