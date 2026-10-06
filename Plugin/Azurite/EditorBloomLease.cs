using System;
using Rendering;
using Studio.Scripts;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Azurite;

// Optional editor-only quality tradeoff. The caller must gate this on supported
// host, explicit opt-in and export ownership, and call Restore before export.
// Only BloomSettings.Enable is borrowed; no renderer feature is disabled.
internal sealed class EditorBloomLease : IDisposable
{
	private const int MaximumFeatures = 16;
	private readonly Action<string>? _report;
	private MXBloomSettings? _bloom;
	private IntPtr _assetPointer;
	private IntPtr _rendererPointer;
	private IntPtr _featurePointer;
	private IntPtr _inspectorPointer;
	private int _sceneHandle;
	private bool _original;
	private bool _lastWritten;
	private bool _owned;
	private bool _blocked;
	private bool _disposed;
	private double _nextDiscovery;

	public bool IsOwned => _owned;
	public bool IsBlocked => _blocked;
	public string Reason { get; private set; } = "editor bloom experiment inactive";

	public EditorBloomLease(Action<string>? report = null) => _report = report;

	public void Update(double now, bool allowed)
	{
		if (_disposed) return;
		try
		{
			ScriptNodeInspector inspector = ScriptNodeInspector.instance;
			if (!allowed || !double.IsFinite(now) || inspector == null || inspector.Pointer == IntPtr.Zero ||
				!inspector.isActiveAndEnabled || inspector.loading || inspector.unloading)
			{
				Restore();
				_nextDiscovery = 0;
				if (!_blocked) Reason = "host or editor lifecycle did not allow bloom override";
				return;
			}
			if (_blocked)
			{
				Restore();
				return;
			}
			int sceneHandle = SceneManager.GetActiveScene().handle;
			if (_owned && (_inspectorPointer != inspector.Pointer || _sceneHandle != sceneHandle))
			{
				Restore();
				_nextDiscovery = 0;
				if (!_blocked) Reason = "editor scene changed; bloom restored";
				return;
			}
			if (_owned && (_bloom == null || _bloom.Pointer == IntPtr.Zero))
			{
				Release();
				Block("owned bloom settings were destroyed");
				return;
			}
			if (_owned && _bloom!.Enable != _lastWritten)
			{
				// A bool cannot expose equal-value or change-and-back writes. Any
				// observable foreign write permanently disables this lease instance.
				Release();
				Block("bloom enable changed externally; yielded ownership");
				return;
			}
			if (now < _nextDiscovery) return;
			_nextDiscovery = now + 0.5;
			if (!Discover(out UIRenderFeature? feature, out MXBloomSettings? bloom,
				out IntPtr assetPointer, out IntPtr rendererPointer, out string reason))
			{
				Restore();
				if (!_blocked) Reason = reason;
				return;
			}
			if (_owned && (_assetPointer != assetPointer || _rendererPointer != rendererPointer ||
				_featurePointer != feature!.Pointer || _bloom!.Pointer != bloom!.Pointer))
			{
				Restore();
				Block("renderer bloom binding changed externally; yielded ownership");
				return;
			}
			if (_owned)
			{
				Reason = "editor bloom override active";
				return;
			}
			if (!bloom!.Enable)
			{
				Reason = "bloom already disabled; no ownership taken";
				return;
			}
			_bloom = bloom;
			_assetPointer = assetPointer;
			_rendererPointer = rendererPointer;
			_featurePointer = feature!.Pointer;
			_inspectorPointer = inspector.Pointer;
			_sceneHandle = sceneHandle;
			_original = true;
			_lastWritten = false;
			// Capture before calling a native setter: it may write then throw.
			_owned = true;
			bloom.Enable = _lastWritten;
			if (bloom.Enable != _lastWritten)
			{
				Block("bloom override readback failed");
				Restore();
				return;
			}
			Reason = "editor bloom override active";
		}
		catch (Exception error)
		{
			Block("bloom override failed: " + error.GetType().Name);
			Restore();
		}
	}

	public bool Restore()
	{
		if (!_owned) return true;
		try
		{
			if (_bloom == null || _bloom.Pointer == IntPtr.Zero)
			{
				Release();
				Block("owned bloom settings were destroyed");
				return true;
			}
			bool current = _bloom.Enable;
			if (current != _lastWritten)
			{
				// Do not write over an external owner, even when it already put
				// back the original value. Export can proceed in that case.
				bool restored = current == _original;
				Release();
				Block("bloom enable changed externally; yielded ownership");
				return restored;
			}
			_bloom.Enable = _original;
			if (_bloom.Enable != _original)
			{
				Block("bloom restoration readback failed");
				return false;
			}
			Release();
			if (!_blocked) Reason = "original bloom restored";
			return true;
		}
		catch (Exception error)
		{
			Block("bloom restoration failed: " + error.GetType().Name);
			return false;
		}
	}

	private static bool Discover(out UIRenderFeature? feature, out MXBloomSettings? bloom,
		out IntPtr assetPointer, out IntPtr rendererPointer, out string reason)
	{
		feature = null;
		bloom = null;
		assetPointer = IntPtr.Zero;
		rendererPointer = IntPtr.Zero;
		reason = "supported renderer bloom binding unavailable";
		var asset = GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>();
		if (asset == null || asset.Pointer == IntPtr.Zero) return false;
		var data = asset.scriptableRendererData;
		if (data == null || data.Pointer == IntPtr.Zero) return false;
		assetPointer = asset.Pointer;
		rendererPointer = data.Pointer;
		var features = data.rendererFeatures;
		int count = features?.Count ?? 0;
		if (count < 1 || count > MaximumFeatures)
		{
			reason = "renderer feature enumeration outside safety bound";
			return false;
		}
		for (int i = 0; i < count; i++)
		{
			UIRenderFeature candidate = features![i]?.TryCast<UIRenderFeature>();
			if (candidate == null || candidate.Pointer == IntPtr.Zero) continue;
			if (feature != null)
			{
				reason = "multiple UI renderer features; bloom override declined";
				return false;
			}
			feature = candidate;
		}
		if (feature == null || !feature.isActive) return false;
		bloom = feature.settings?.BloomSettings;
		return bloom != null && bloom.Pointer != IntPtr.Zero;
	}

	private void Block(string reason)
	{
		bool first = !_blocked;
		_blocked = true;
		Reason = reason;
		if (first)
		{
			try { _report?.Invoke(reason); }
			catch { /* Diagnostics must not prevent restoration. */ }
		}
	}

	private void Release()
	{
		_bloom = null;
		_assetPointer = IntPtr.Zero;
		_rendererPointer = IntPtr.Zero;
		_featurePointer = IntPtr.Zero;
		_inspectorPointer = IntPtr.Zero;
		_owned = false;
	}

	public void Dispose()
	{
		_disposed = true;
		Restore();
	}
}
