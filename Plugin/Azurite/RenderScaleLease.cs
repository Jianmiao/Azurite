using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Azurite;

internal sealed class RenderScaleLease
{
	private UniversalRenderPipelineAsset? _asset;

	private float _original;

	private float _lastWritten;

	private float _previousWritten;

	private bool _writePending;

	private bool _owned;

	private bool _blocked;

	public bool IsBlocked => _blocked;

	public bool TryApply(float scale)
	{
		if (_blocked || !IsFinite(scale) || scale < 0.5f || scale > 1f)
		{
			return false;
		}
		if (!TryGetAsset(out UniversalRenderPipelineAsset asset))
		{
			Restore();
			return false;
		}
		try
		{
			if (_owned && (_asset == null || _asset.Pointer != asset.Pointer) && !Restore())
			{
				return false;
			}
			float renderScale = asset.renderScale;
			if (!IsFinite(renderScale))
			{
				_blocked = true;
				return false;
			}
			if (_owned && !Matches(renderScale, _lastWritten))
			{
				_blocked = true;
				return false;
			}
			if (Matches(renderScale, scale))
			{
				return true;
			}
			if (!_owned)
			{
				_asset = asset;
				_original = renderScale;
				_lastWritten = renderScale;
				_owned = true;
			}
			_previousWritten = _lastWritten;
			_lastWritten = scale;
			_writePending = true;
			asset.renderScale = scale;
			if (!Matches(asset.renderScale, scale))
			{
				_blocked = true;
				Restore();
				return false;
			}
			_writePending = false;
			return true;
		}
		catch
		{
			_blocked = true;
			Restore();
			return false;
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
			if (_asset == null)
			{
				_blocked = true;
				return false;
			}
			float renderScale = _asset.renderScale;
			if (Matches(renderScale, _original))
			{
				Release();
				return true;
			}
			if (!Matches(renderScale, _lastWritten) && (!_writePending || !Matches(renderScale, _previousWritten)))
			{
				_blocked = true;
				return false;
			}
			_asset.renderScale = _original;
			if (!Matches(_asset.renderScale, _original))
			{
				_blocked = true;
				return false;
			}
			Release();
			return true;
		}
		catch
		{
			_blocked = true;
			return false;
		}
	}

	private static bool TryGetAsset(out UniversalRenderPipelineAsset asset)
	{
		asset = null;
		try
		{
			RenderPipelineAsset currentRenderPipeline = GraphicsSettings.currentRenderPipeline;
			if (currentRenderPipeline == null || currentRenderPipeline.Pointer == IntPtr.Zero)
			{
				return false;
			}
			UniversalRenderPipelineAsset universalRenderPipelineAsset = currentRenderPipeline.TryCast<UniversalRenderPipelineAsset>();
			if (universalRenderPipelineAsset == null || universalRenderPipelineAsset.Pointer == IntPtr.Zero)
			{
				return false;
			}
			asset = universalRenderPipelineAsset;
			return true;
		}
		catch
		{
		}
		return false;
	}

	private void Release()
	{
		_asset = null;
		_owned = false;
		_writePending = false;
	}

	private static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	private static bool Matches(float left, float right)
	{
		if (IsFinite(left) && IsFinite(right))
		{
			return Math.Abs(left - right) <= 0.001f;
		}
		return false;
	}
}
