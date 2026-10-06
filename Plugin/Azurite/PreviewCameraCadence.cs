using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Azurite;

internal sealed class PreviewCameraCadence : IDisposable
{
	private const int MaximumInspectorTextures = 8192;

	private readonly Action<string>? _report;
	private readonly PreviewTextureRegistry _textures;
	private double _observationTime;
	private UIPanel? _contentPanel;
	private System.Action? _managedGeometry;
	private System.Action<UIPanel>? _managedClip;
	private UIPanel.OnGeometryUpdated? _nativeGeometry;
	private UIPanel.OnClippingMoved? _nativeClip;
	private bool _cacheMode;
	private bool _hasCameraMatrix;
	private Matrix4x4 _cameraMatrix;

	private Camera? _camera;

	private ScriptNodeInspector? _inspector;

	private Test? _preview;

	private UITexture? _consumer;

	private RenderTexture? _target;

	private double _nextDiscovery;

	private readonly PreviewCadenceSchedule _schedule = new PreviewCadenceSchedule();

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

	// The target lease may inspect this exact camera while cadence intentionally
	// disables it between draws. It must not accept an unrelated disabled camera.
	internal Camera? SuspendedCamera => _owned && !_lastWritten && _camera != null && !_camera.enabled ? _camera : null;

	public PreviewCameraCadence(Action<string>? report = null, PreviewTextureRegistry? textures = null)
	{
		_report = report;
		_textures = textures ?? new PreviewTextureRegistry();
	}

	public void Update(double now, bool allowed, double targetFps = 60.0, bool cacheStatic = false, double safetyFps = 1.0)
	{
		_observationTime = now;
		if (_disposed)
		{
			return;
		}
		if (!allowed || !double.IsFinite(now) || !double.IsFinite(targetFps) || targetFps < 30.0 || targetFps > 120.0 ||
			(cacheStatic && (!double.IsFinite(safetyFps) || safetyFps < 0.1 || safetyFps > 30.0)))
		{
			Reason = ((!allowed) ? "host or configuration did not allow cadence" : "invalid time or FPS configuration");
			Restore();
			_schedule.Reset();
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
			if (_cacheMode != cacheStatic) { _cacheMode = cacheStatic; Invalidate(); if (!cacheStatic) UnbindContent(); }
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
				if (_ready && cacheStatic && !BindContent(_preview?.frontPanel)) _ready = Reject("static preview content callbacks unavailable");
			}
			if (!_ready || _camera == null || _inspector == null || _inspector.loading || _inspector.unloading || _preview == null || !_preview.isActiveAndEnabled || !_preview.previewMode || _consumer == null || !_camera.gameObject.activeInHierarchy)
			{
				if (_ready)
				{
					Reason = "cached preview lifecycle not ready";
				}
				Restore();
				_schedule.Reset();
				_wasHidden = false;
				return;
			}
			if (!Same(_camera.targetTexture, _target) || !Same(_consumer.mainTexture, _target))
			{
				Reason = "preview target rebound; rediscovery pending";
				Restore();
				_ready = false;
				_nextDiscovery = now;
				_schedule.Reset();
				return;
			}
			if (cacheStatic && !Same(_preview.frontPanel, _contentPanel) && !BindContent(_preview.frontPanel))
			{
				Reason = "static preview content callbacks unavailable";
				Restore(); return;
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
				_schedule.Reset();
				_skipped++;
				SetEnabled(value: false);
				return;
			}
			if (!_consumer.isActiveAndEnabled || !_consumer.isVisible)
			{
				Reason = "consumer visibility uncertain";
				Restore();
				_schedule.Reset();
				_wasHidden = false;
				return;
			}
			if (_wasHidden)
			{
				_wasHidden = false;
				_schedule.Reset();
			}
			if (cacheStatic)
			{
				Matrix4x4 matrix = _camera.transform.localToWorldMatrix;
				if (!_hasCameraMatrix || matrix != _cameraMatrix || (_target != null && !_target.IsCreated())) Invalidate();
				_cameraMatrix = matrix; _hasCameraMatrix = true;
			}
			Reason = cacheStatic ? "static preview content reused; geometry and clipping changes invalidate it" : "visible independent preview cadence active";
			bool flag = _schedule.ShouldRender(now, cacheStatic ? safetyFps : targetFps, OnDemandRendering.willCurrentFrameRender);
			if (flag)
			{
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

	internal void Invalidate() { _schedule.Reset(); _hasCameraMatrix = false; }

	private bool BindContent(UIPanel? panel)
	{
		if (panel == null) return false;
		if (!Same(panel, _contentPanel))
		{
			UnbindContent(); _contentPanel = panel;
			_managedGeometry = Invalidate;
			_managedClip = _ => Invalidate();
			_nativeGeometry = DelegateSupport.ConvertDelegate<UIPanel.OnGeometryUpdated>(_managedGeometry);
			_nativeClip = DelegateSupport.ConvertDelegate<UIPanel.OnClippingMoved>(_managedClip);
			Invalidate();
		}
		if (_nativeGeometry == null || _nativeClip == null) return false;
		if (!Contains(panel.onGeometryUpdated, _nativeGeometry)) panel.onGeometryUpdated = Il2CppSystem.Delegate.Combine(panel.onGeometryUpdated, _nativeGeometry).Cast<UIPanel.OnGeometryUpdated>();
		if (!Contains(panel.onClipMove, _nativeClip)) panel.onClipMove = Il2CppSystem.Delegate.Combine(panel.onClipMove, _nativeClip).Cast<UIPanel.OnClippingMoved>();
		return Contains(panel.onGeometryUpdated, _nativeGeometry) && Contains(panel.onClipMove, _nativeClip);
	}

	private static bool Contains(Il2CppSystem.Delegate? source, Il2CppSystem.Delegate callback)
	{
		if (source == null) return false;
		if (source.Pointer == callback.Pointer) return true;
		var list = source.GetInvocationList();
		for (int i = 0; i < list.Length; i++) if (list[i] != null && list[i].Pointer == callback.Pointer) return true;
		return false;
	}

	private void UnbindContent()
	{
		try
		{
			if (_contentPanel != null && _nativeGeometry != null)
			{
				var remaining = Il2CppSystem.Delegate.Remove(_contentPanel.onGeometryUpdated, _nativeGeometry);
				_contentPanel.onGeometryUpdated = remaining == null ? null : remaining.Cast<UIPanel.OnGeometryUpdated>();
			}
			if (_contentPanel != null && _nativeClip != null)
			{
				var remaining = Il2CppSystem.Delegate.Remove(_contentPanel.onClipMove, _nativeClip);
				_contentPanel.onClipMove = remaining == null ? null : remaining.Cast<UIPanel.OnClippingMoved>();
			}
		}
		catch { _blocked = true; }
		_contentPanel = null; _nativeGeometry = null; _nativeClip = null; _managedGeometry = null; _managedClip = null;
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
			_schedule.Reset();
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
		UnbindContent(); Invalidate();
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
