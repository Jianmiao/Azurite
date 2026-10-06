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

		public System.IntPtr GeometrySubscription;

		public System.IntPtr ClipSubscription;
		public readonly bool PreviewScene;

		public PanelBinding(UIPanel panel, Transform transform, UiRepaintObserver observer)
		{
			PanelBinding panelBinding = this;
			Panel = panel;
			Pointer = panel.Pointer;
			Transform = transform;
			Matrix = transform.localToWorldMatrix;
			ClipOffset = panel.clipOffset;
			PreviewScene = panel.gameObject != null && string.Equals(panel.gameObject.scene.name, "PreviewScene", System.StringComparison.Ordinal);
			GeometryHandler = delegate
			{
				Interlocked.Increment(ref panelBinding.GeometryEvents);
				Interlocked.Increment(ref observer._geometryEvents);
				if (panelBinding.PreviewScene) Interlocked.Increment(ref observer._previewGeneration);
				observer.MarkDirty(UiDirtyKind.Geometry);
			};
			ClipHandler = delegate
			{
				Interlocked.Increment(ref panelBinding.ClipEvents);
				Interlocked.Increment(ref observer._clipEvents);
				if (panelBinding.PreviewScene) Interlocked.Increment(ref observer._previewGeneration);
				observer.MarkDirty(UiDirtyKind.ClipEvent);
			};
			NativeGeometry = DelegateSupport.ConvertDelegate<UIPanel.OnGeometryUpdated>(GeometryHandler) ?? throw new System.InvalidOperationException("NGUI geometry delegate conversion returned null");
			NativeClip = DelegateSupport.ConvertDelegate<UIPanel.OnClippingMoved>(ClipHandler) ?? throw new System.InvalidOperationException("NGUI clip delegate conversion returned null");
		}
	}

	private const int MaximumPanels = 4096;

	private const int RegistryBatchSize = 32;

	private const int TransformBatchSize = 32;

	private const double TransformPollSeconds = 1.0 / 60.0;

	private readonly System.Action<string>? _report;

	private readonly System.Collections.Generic.Dictionary<System.IntPtr, PanelBinding> _panels = new System.Collections.Generic.Dictionary<System.IntPtr, PanelBinding>();

	private readonly HashSet<System.IntPtr> _seen = new HashSet<System.IntPtr>();

	private readonly System.Collections.Generic.List<System.IntPtr> _removed = new System.Collections.Generic.List<System.IntPtr>();

	private readonly System.Collections.Generic.List<System.IntPtr> _panelOrder = new System.Collections.Generic.List<System.IntPtr>();

	private bool _healthy = true;

	private bool _disposed;

	private bool _ready;

	private int _sceneHandle = int.MinValue;

	private int _registryCount = -1;

	private int _syncExpectedCount = -1;

	private int _syncCursor;

	private int _transformCursor;

	private bool _syncInProgress;

	private bool _panelOrderDirty;

	private bool _capacityBlocked;

	private double _nextRegistryScan;

	private double _nextTransformPoll;

	private long _generation = 1L;

	private long _consumedGeneration;

	private long _geometryEvents;
	private long _previewGeneration;

	private long _clipEvents;

	private long _transformChanges;

	private long _registryChanges;

	private long _sceneChanges;

	private long _callbackReplacements;

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
	internal long PreviewGeneration => Interlocked.Read(ref _previewGeneration);

	public UiDirtyKind LastDirtyKind => (UiDirtyKind)Volatile.Read(ref _lastDirtyKind);

	public UiRepaintMetrics Metrics => new UiRepaintMetrics(Generation, Interlocked.Read(ref _geometryEvents), Interlocked.Read(ref _clipEvents), Interlocked.Read(ref _transformChanges), Interlocked.Read(ref _registryChanges), Interlocked.Read(ref _sceneChanges), _panels.Count, LastDirtyKind);

	public bool CapacityBlocked => _capacityBlocked;

	public int SynchronizationCursor => _syncCursor;

	public long CallbackReplacements => Interlocked.Read(ref _callbackReplacements);

	internal bool HasOnlyOwnGeometryCallback(UIPanel panel) => panel != null &&
		_panels.TryGetValue(panel.Pointer, out PanelBinding binding) &&
		panel.onGeometryUpdated?.Pointer == binding.NativeGeometry.Pointer;

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
				_syncExpectedCount = -1;
				_syncCursor = 0;
				_transformCursor = 0;
				_syncInProgress = false;
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
			if (count > MaximumPanels)
			{
				if (!_capacityBlocked)
				{
					_report?.Invoke("NGUI repaint observation paused: panel registry exceeds " + MaximumPanels + " entries.");
				}
				_capacityBlocked = true;
				_syncInProgress = false;
				_syncExpectedCount = count;
				_syncCursor = 0;
				_registryCount = -1;
				_transformCursor = 0;
				_ready = false;
				return;
			}
			if (_capacityBlocked)
			{
				_capacityBlocked = false;
				_syncInProgress = false;
				_syncExpectedCount = -1;
				_syncCursor = 0;
				_registryCount = -1;
				_transformCursor = 0;
				_ready = false;
				_nextRegistryScan = now;
				MarkRegistryDirty();
			}
			if (_syncInProgress && count != _syncExpectedCount)
			{
				RestartSynchronization(count, now);
			}
			else if (!_syncInProgress && count != _registryCount)
			{
				RestartSynchronization(count, now);
			}
			if (!_syncInProgress && now >= _nextRegistryScan)
			{
				StartSynchronization(count);
			}
			if (_syncInProgress)
			{
				// Established bindings remain valid during a routine registry audit.
				// Poll their transforms as well, so a long registry cannot starve its tail.
				SynchronizeBatch(list, count, now);
			}
			if (!_ready || now < _nextTransformPoll)
			{
				return;
			}
			_nextTransformPoll = now + 1.0 / 60.0;
			int polled = 0;
			int budget = System.Math.Min(TransformBatchSize, _panelOrder.Count);
			while (polled++ < budget)
			{
				if (_transformCursor >= _panelOrder.Count)
				{
					_transformCursor = 0;
				}
				System.IntPtr pointer = _panelOrder[_transformCursor++];
				if (!_panels.TryGetValue(pointer, out PanelBinding value))
				{
					continue;
				}
				UIPanel panel = value.Panel;
				if (panel == null || !panel.isActiveAndEnabled || value.Transform == null)
				{
					if (_ready)
					{
						_ready = false;
						_nextRegistryScan = now;
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
					if (value.PreviewScene) Interlocked.Increment(ref _previewGeneration);
					MarkDirty(UiDirtyKind.TransformOrClipOffset);
				}
			}
		}
		catch (System.Exception error)
		{
			Fail(error);
		}
	}

	private void StartSynchronization(int count)
	{
		_syncExpectedCount = count;
		_syncCursor = 0;
		_syncInProgress = true;
		_seen.Clear();
		// Merely checking an unchanged registry is not a UI mutation. Initial and
		// invalidated coverage already has Ready=false; keep steady coverage intact.
	}

	private void RestartSynchronization(int count, double now)
	{
		_syncInProgress = false;
		_syncExpectedCount = -1;
		_syncCursor = 0;
		_registryCount = -1;
		_ready = false;
		_nextRegistryScan = now;
		_seen.Clear();
		MarkRegistryDirty();
	}

	private bool SynchronizeBatch(Il2CppSystem.Collections.Generic.List<UIPanel> registry, int count, double now)
	{
		if (count != _syncExpectedCount || registry.Count != _syncExpectedCount)
		{
			RestartSynchronization(registry.Count, now);
			return false;
		}
		int end = System.Math.Min(count, _syncCursor + RegistryBatchSize);
		for (int i = _syncCursor; i < end; i++)
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
				if (EnsureSubscriptions(uIPanel, value, previouslyObserved: true))
				{
					_ready = false;
					MarkRegistryDirty();
				}
				value.Panel = uIPanel;
				continue;
			}
			Transform transform = uIPanel.transform;
			if (transform == null)
			{
				continue;
			}
			PanelBinding panelBinding = new PanelBinding(uIPanel, transform, this);
			_panels.Add(pointer, panelBinding);
			EnsureSubscriptions(uIPanel, panelBinding, previouslyObserved: false);
			_panelOrderDirty = true;
			_ready = false;
			MarkRegistryDirty();
		}
		_syncCursor = end;
		if (_syncCursor < count)
		{
			return false;
		}
		if (registry.Count != count)
		{
			RestartSynchronization(registry.Count, now);
			return false;
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
		}
		if (_removed.Count > 0)
		{
			_panelOrderDirty = true;
			MarkRegistryDirty();
		}
		if (_panelOrderDirty)
		{
			_panelOrder.Clear();
			_panelOrder.AddRange(_panels.Keys);
			_panelOrderDirty = false;
			// Keep progress across registry scans. Resetting every second prevents
			// large registries from ever checking the last panels' matrices/clip offsets.
			if (_transformCursor >= _panelOrder.Count)
			{
				_transformCursor = 0;
			}
		}
		_registryCount = count;
		_syncExpectedCount = -1;
		_syncCursor = 0;
		_syncInProgress = false;
		_ready = _panels.Count > 0;
		_nextRegistryScan = now + 1.0;
		return true;
	}

	private bool EnsureSubscriptions(UIPanel panel, PanelBinding binding, bool previouslyObserved)
	{
		bool changed = false;
		UIPanel.OnGeometryUpdated? geometry = panel.onGeometryUpdated;
		if (geometry == null || geometry.Pointer != binding.GeometrySubscription)
		{
			if (!Contains(geometry, binding.NativeGeometry))
			{
				if (previouslyObserved) Interlocked.Increment(ref _callbackReplacements);
				changed = true;
				Il2CppSystem.Delegate combined = Il2CppSystem.Delegate.Combine(geometry, binding.NativeGeometry) ?? throw new System.InvalidOperationException("NGUI geometry delegate combination failed");
				panel.onGeometryUpdated = combined.Cast<UIPanel.OnGeometryUpdated>();
				geometry = panel.onGeometryUpdated;
				if (!Contains(geometry, binding.NativeGeometry)) throw new System.InvalidOperationException("NGUI geometry callback re-subscription failed");
			}
			binding.GeometrySubscription = geometry!.Pointer;
		}
		UIPanel.OnClippingMoved? clip = panel.onClipMove;
		if (clip == null || clip.Pointer != binding.ClipSubscription)
		{
			if (!Contains(clip, binding.NativeClip))
			{
				if (previouslyObserved) Interlocked.Increment(ref _callbackReplacements);
				changed = true;
				Il2CppSystem.Delegate combined = Il2CppSystem.Delegate.Combine(clip, binding.NativeClip) ?? throw new System.InvalidOperationException("NGUI clip delegate combination failed");
				panel.onClipMove = combined.Cast<UIPanel.OnClippingMoved>();
				clip = panel.onClipMove;
				if (!Contains(clip, binding.NativeClip)) throw new System.InvalidOperationException("NGUI clip callback re-subscription failed");
			}
			binding.ClipSubscription = clip!.Pointer;
		}
		return changed;
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
		_panelOrder.Clear();
		_panelOrderDirty = false;
		_seen.Clear();
		_removed.Clear();
		_syncExpectedCount = -1;
		_syncCursor = 0;
		_transformCursor = 0;
		_syncInProgress = false;
		_ready = false;
		if (ex != null)
		{
			throw ex;
		}
	}

	private void MarkRegistryDirty()
	{
		Interlocked.Increment(ref _previewGeneration);
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
