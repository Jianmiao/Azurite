using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Azurite;

internal sealed class UiRepaintObserver : System.IDisposable
{
	private sealed class PanelBinding
	{
		public UIPanel Panel;

		public readonly System.IntPtr Pointer;

		public readonly Transform Transform;

		public readonly System.Action GeometryHandler;

		public readonly System.Action<UIPanel> ClipHandler;

		public readonly UIPanel.OnGeometryUpdated NativeGeometry;

		public readonly UIPanel.OnClippingMoved NativeClip;

		public Matrix4x4 Matrix;

		public Vector2 ClipOffset;

		public long GeometryEvents;

		public long ClipEvents;

		public long ReportedGeometry;

		public long ReportedClip;

		public PanelBinding(UIPanel panel, Transform transform, UiRepaintObserver observer)
		{
			PanelBinding panelBinding = this;
			Panel = panel;
			Pointer = panel.Pointer;
			Transform = transform;
			Matrix = transform.localToWorldMatrix;
			ClipOffset = panel.clipOffset;
			GeometryHandler = delegate
			{
				Interlocked.Increment(ref panelBinding.GeometryEvents);
				Interlocked.Increment(ref observer._geometryEvents);
				observer.MarkDirty(UiDirtyKind.Geometry);
			};
			ClipHandler = delegate
			{
				Interlocked.Increment(ref panelBinding.ClipEvents);
				Interlocked.Increment(ref observer._clipEvents);
				observer.MarkDirty(UiDirtyKind.ClipEvent);
			};
			NativeGeometry = DelegateSupport.ConvertDelegate<UIPanel.OnGeometryUpdated>(GeometryHandler) ?? throw new System.InvalidOperationException("NGUI geometry delegate conversion returned null");
			NativeClip = DelegateSupport.ConvertDelegate<UIPanel.OnClippingMoved>(ClipHandler) ?? throw new System.InvalidOperationException("NGUI clip delegate conversion returned null");
		}
	}

	private const int MaximumPanels = 256;

	private const double TransformPollSeconds = 1.0 / 60.0;

	private readonly System.Action<string>? _report;

	private readonly System.Collections.Generic.Dictionary<System.IntPtr, PanelBinding> _panels = new System.Collections.Generic.Dictionary<System.IntPtr, PanelBinding>();

	private readonly HashSet<System.IntPtr> _seen = new HashSet<System.IntPtr>();

	private readonly System.Collections.Generic.List<System.IntPtr> _removed = new System.Collections.Generic.List<System.IntPtr>();

	private bool _healthy = true;

	private bool _disposed;

	private bool _ready;

	private int _sceneHandle = int.MinValue;

	private int _registryCount = -1;

	private double _nextRegistryScan;

	private double _nextTransformPoll;

	private long _generation = 1L;

	private long _consumedGeneration;

	private long _geometryEvents;

	private long _clipEvents;

	private long _transformChanges;

	private long _registryChanges;

	private long _sceneChanges;

	private int _lastDirtyKind;

	public bool IsHealthy
	{
		get
		{
			if (_healthy)
			{
				return !_disposed;
			}
			return false;
		}
	}

	public bool IsReady
	{
		get
		{
			if (IsHealthy)
			{
				return _ready;
			}
			return false;
		}
	}

	public long Generation => Interlocked.Read(ref _generation);

	public UiDirtyKind LastDirtyKind => (UiDirtyKind)Volatile.Read(ref _lastDirtyKind);

	public UiRepaintMetrics Metrics => new UiRepaintMetrics(Generation, Interlocked.Read(ref _geometryEvents), Interlocked.Read(ref _clipEvents), Interlocked.Read(ref _transformChanges), Interlocked.Read(ref _registryChanges), Interlocked.Read(ref _sceneChanges), _panels.Count, LastDirtyKind);

	public UiRepaintObserver(System.Action<string>? report = null)
	{
		_report = report;
	}

	public string SummarizeDirtyPanels()
	{
		System.Collections.Generic.List<(PanelBinding, long, long)> list = new System.Collections.Generic.List<(PanelBinding, long, long)>(_panels.Count);
		foreach (PanelBinding value in _panels.Values)
		{
			long num = Interlocked.Read(ref value.GeometryEvents);
			long num2 = Interlocked.Read(ref value.ClipEvents);
			long num3 = num - value.ReportedGeometry;
			long num4 = num2 - value.ReportedClip;
			value.ReportedGeometry = num;
			value.ReportedClip = num2;
			if (num3 != 0L || num4 != 0L)
			{
				list.Add((value, num3, num4));
			}
		}
		list.Sort(((PanelBinding Binding, long Geometry, long Clip) left, (PanelBinding Binding, long Geometry, long Clip) right) => (right.Geometry + right.Clip).CompareTo(left.Geometry + left.Clip));
		if (list.Count == 0)
		{
			return "no geometry/clip events";
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int num5 = 0; num5 < System.Math.Min(5, list.Count); num5++)
		{
			(PanelBinding, long, long) tuple = list[num5];
			if (num5 != 0)
			{
				stringBuilder.Append("; ");
			}
			stringBuilder.Append(tuple.Item1.Pointer.ToString("X"));
			try
			{
				UIPanel panel = tuple.Item1.Panel;
				stringBuilder.Append(':').Append((panel != null) ? panel.name : "destroyed");
				if (panel != null)
				{
					stringBuilder.Append("[widgets=").Append(panel.widgets?.Count ?? 0).Append(",draws=")
						.Append(panel.drawCalls?.Count ?? 0)
						.Append(']');
				}
			}
			catch
			{
				stringBuilder.Append(":unavailable");
			}
			stringBuilder.Append(" geometry=").Append(tuple.Item2).Append(" clip=")
				.Append(tuple.Item3);
		}
		return stringBuilder.ToString();
	}

	public bool ConsumeDirty()
	{
		long generation = Generation;
		if (generation == _consumedGeneration)
		{
			return false;
		}
		_consumedGeneration = generation;
		return true;
	}

	public void Update(double now)
	{
		if (!IsHealthy)
		{
			return;
		}
		try
		{
			if (double.IsNaN(now) || double.IsInfinity(now))
			{
				throw new System.ArgumentOutOfRangeException("now");
			}
			int handle = SceneManager.GetActiveScene().handle;
			if (handle != _sceneHandle)
			{
				RemoveAllBindings();
				_sceneHandle = handle;
				_registryCount = -1;
				_ready = false;
				_nextRegistryScan = now;
				_nextTransformPoll = now;
				Interlocked.Increment(ref _sceneChanges);
				MarkDirty(UiDirtyKind.Scene);
			}
			Il2CppSystem.Collections.Generic.List<UIPanel> list = UIPanel.list;
			if (list == null)
			{
				throw new System.InvalidOperationException("NGUI panel registry unavailable");
			}
			int count = list.Count;
			if (count > 256)
			{
				throw new System.InvalidOperationException("NGUI panel observation limit exceeded");
			}
			if (count != _registryCount && _ready)
			{
				_ready = false;
				MarkRegistryDirty();
			}
			if (now >= _nextRegistryScan)
			{
				_nextRegistryScan = now + 1.0;
				Synchronize(list, count);
				_registryCount = count;
				_ready = _panels.Count > 0;
			}
			if (now < _nextTransformPoll)
			{
				return;
			}
			_nextTransformPoll = now + 1.0 / 60.0;
			foreach (PanelBinding value in _panels.Values)
			{
				UIPanel panel = value.Panel;
				if (panel == null || !panel.isActiveAndEnabled || value.Transform == null)
				{
					if (_ready)
					{
						_ready = false;
						MarkRegistryDirty();
					}
					continue;
				}
				Matrix4x4 localToWorldMatrix = value.Transform.localToWorldMatrix;
				Vector2 clipOffset = panel.clipOffset;
				if (localToWorldMatrix != value.Matrix || clipOffset != value.ClipOffset)
				{
					value.Matrix = localToWorldMatrix;
					value.ClipOffset = clipOffset;
					Interlocked.Increment(ref _transformChanges);
					MarkDirty(UiDirtyKind.TransformOrClipOffset);
				}
			}
		}
		catch (System.Exception error)
		{
			Fail(error);
		}
	}

	private void Synchronize(Il2CppSystem.Collections.Generic.List<UIPanel> registry, int count)
	{
		_seen.Clear();
		for (int i = 0; i < count; i++)
		{
			UIPanel uIPanel = registry[i];
			if (uIPanel == null || !uIPanel.isActiveAndEnabled)
			{
				continue;
			}
			System.IntPtr pointer = uIPanel.Pointer;
			if (pointer == System.IntPtr.Zero || !_seen.Add(pointer))
			{
				continue;
			}
			if (_panels.TryGetValue(pointer, out PanelBinding value))
			{
				if (!Contains(uIPanel.onGeometryUpdated, value.NativeGeometry) || !Contains(uIPanel.onClipMove, value.NativeClip))
				{
					throw new System.InvalidOperationException("NGUI repaint callback was replaced");
				}
				value.Panel = uIPanel;
				continue;
			}
			Transform transform = uIPanel.transform;
			if (transform == null)
			{
				throw new System.InvalidOperationException("NGUI panel transform unavailable");
			}
			PanelBinding panelBinding = new PanelBinding(uIPanel, transform, this);
			_panels.Add(pointer, panelBinding);
			Il2CppSystem.Delegate obj = Il2CppSystem.Delegate.Combine(uIPanel.onGeometryUpdated, panelBinding.NativeGeometry) ?? throw new System.InvalidOperationException("NGUI geometry delegate combination failed");
			uIPanel.onGeometryUpdated = obj.Cast<UIPanel.OnGeometryUpdated>();
			Il2CppSystem.Delegate obj2 = Il2CppSystem.Delegate.Combine(uIPanel.onClipMove, panelBinding.NativeClip) ?? throw new System.InvalidOperationException("NGUI clip delegate combination failed");
			uIPanel.onClipMove = obj2.Cast<UIPanel.OnClippingMoved>();
			if (!Contains(uIPanel.onGeometryUpdated, panelBinding.NativeGeometry) || !Contains(uIPanel.onClipMove, panelBinding.NativeClip))
			{
				throw new System.InvalidOperationException("NGUI repaint subscription readback failed");
			}
			MarkRegistryDirty();
		}
		if (registry.Count != count)
		{
			throw new System.InvalidOperationException("NGUI panel registry changed during observation");
		}
		_removed.Clear();
		foreach (System.IntPtr key in _panels.Keys)
		{
			if (!_seen.Contains(key))
			{
				_removed.Add(key);
			}
		}
		foreach (System.IntPtr item in _removed)
		{
			RemoveBinding(_panels[item]);
			_panels.Remove(item);
			MarkRegistryDirty();
		}
	}

	private static bool Contains(Il2CppSystem.Delegate? combined, Il2CppSystem.Delegate own)
	{
		if (combined == null)
		{
			return false;
		}
		if (combined.Pointer == own.Pointer)
		{
			return true;
		}
		Il2CppReferenceArray<Il2CppSystem.Delegate> invocationList = combined.GetInvocationList();
		if (invocationList == null)
		{
			return false;
		}
		for (int i = 0; i < invocationList.Length; i++)
		{
			Il2CppSystem.Delegate obj = invocationList[i];
			if (obj != null && (obj.Pointer == own.Pointer || obj.Equals(own)))
			{
				return true;
			}
		}
		return false;
	}

	private void RemoveBinding(PanelBinding binding)
	{
		if (binding.Panel == null)
		{
			return;
		}
		System.Exception ex = null;
		try
		{
			Il2CppSystem.Delegate obj = Il2CppSystem.Delegate.Remove(binding.Panel.onGeometryUpdated, binding.NativeGeometry);
			binding.Panel.onGeometryUpdated = ((obj == null) ? null : obj.Cast<UIPanel.OnGeometryUpdated>());
		}
		catch (System.Exception ex2)
		{
			ex = ex2;
		}
		try
		{
			Il2CppSystem.Delegate obj2 = Il2CppSystem.Delegate.Remove(binding.Panel.onClipMove, binding.NativeClip);
			binding.Panel.onClipMove = ((obj2 == null) ? null : obj2.Cast<UIPanel.OnClippingMoved>());
		}
		catch (System.Exception ex3)
		{
			if (ex == null)
			{
				ex = ex3;
			}
		}
		System.GC.KeepAlive(binding.GeometryHandler);
		System.GC.KeepAlive(binding.ClipHandler);
		System.GC.KeepAlive(binding.NativeGeometry);
		System.GC.KeepAlive(binding.NativeClip);
		if (ex == null)
		{
			return;
		}
		throw ex;
	}

	private void RemoveAllBindings()
	{
		System.Exception ex = null;
		foreach (PanelBinding value in _panels.Values)
		{
			try
			{
				RemoveBinding(value);
			}
			catch (System.Exception ex2)
			{
				if (ex == null)
				{
					ex = ex2;
				}
			}
		}
		_panels.Clear();
		_seen.Clear();
		_removed.Clear();
		if (ex != null)
		{
			throw ex;
		}
	}

	private void MarkRegistryDirty()
	{
		Interlocked.Increment(ref _registryChanges);
		MarkDirty(UiDirtyKind.Registry);
	}

	private void MarkDirty(UiDirtyKind kind)
	{
		Volatile.Write(ref _lastDirtyKind, (int)kind);
		Interlocked.Increment(ref _generation);
	}

	private void Fail(System.Exception error)
	{
		if (_healthy)
		{
			_healthy = false;
			_ready = false;
			MarkDirty(UiDirtyKind.Failure);
			try
			{
				RemoveAllBindings();
			}
			catch
			{
			}
			_report?.Invoke("NGUI repaint observation unavailable: " + error.GetType().Name + "; " + error.Message);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_ready = false;
		MarkDirty(UiDirtyKind.Disposed);
		try
		{
			RemoveAllBindings();
		}
		catch (System.Exception error)
		{
			Fail(error);
		}
	}
}
