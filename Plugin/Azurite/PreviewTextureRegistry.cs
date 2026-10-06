using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Studio.Scripts;

namespace Azurite;

// Shares one hierarchy enumeration between target and cadence discovery.
// Bindings are read live from the returned objects, never cached as pointers.
// Resource release always forces a new enumeration.
internal sealed class PreviewTextureRegistry
{
	private ScriptNodeInspector? _inspector;
	private Il2CppArrayBase<UITexture>? _textures;
	private double _sampleTime = double.NaN;
	internal Il2CppArrayBase<UITexture>? Read(ScriptNodeInspector inspector, double now, bool force = false)
	{
		if (inspector == null) { Clear(); return null; }
		if (force || _inspector == null || _inspector.Pointer != inspector.Pointer || _textures == null ||
			!double.IsFinite(now) || !double.IsFinite(_sampleTime) || now < _sampleTime || now - _sampleTime >= 1.0)
		{
			_textures = inspector.GetComponentsInChildren<UITexture>(includeInactive: true);
			_inspector = inspector;
			_sampleTime = now;
		}
		return _textures;
	}
	internal void Clear() { _textures = null; _inspector = null; _sampleTime = double.NaN; }
}
