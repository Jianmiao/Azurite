using System;
using System.Collections.Generic;
using System.Threading;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using Studio.Scripts.Window.BackgroundExplorer;
using UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Azurite;

internal sealed class ScrollActivityGuard : System.IDisposable
{
	private sealed class ViewState
	{
		public UIScrollView Scroll;

		public CenterableUIScrollView? Centerable;

		public bool HasSample;

		private System.IntPtr _panel;

		private Vector2 _clip;

		private Vector3 _position;

		public ViewState(UIScrollView scroll, CenterableUIScrollView? centerable)
		{
			Scroll = scroll;
			Centerable = centerable;
		}

		public bool ObserveMotion()
		{
			if (Scroll == null || !Scroll.isActiveAndEnabled)
			{
				HasSample = false;
				return false;
			}
			UIPanel panel = Scroll.panel;
			Transform transform = Scroll.transform;
			if (panel == null || transform == null)
			{
				throw new System.InvalidOperationException("Known scroll viewport unavailable");
			}
			Vector2 clipOffset = panel.clipOffset;
			Vector3 localPosition = transform.localPosition;
			bool num = !HasSample || panel.Pointer != _panel || clipOffset != _clip || localPosition != _position;
			_panel = panel.Pointer;
			_clip = clipOffset;
			_position = localPosition;
			HasSample = true;
			float sqrMagnitude = Scroll.currentMomentum.sqrMagnitude;
			float mScroll = Scroll.mScroll;
			SpringPanel springPanel = ((Centerable != null) ? Centerable.spring : null);
			if (!num && !Scroll.isDragging && float.IsFinite(sqrMagnitude) && float.IsFinite(mScroll) && !(sqrMagnitude > 1E-06f) && !(System.Math.Abs(mScroll) > 1E-06f))
			{
				if (springPanel != null)
				{
					return springPanel.isActiveAndEnabled;
				}
				return false;
			}
			return true;
		}
	}

	private const double HoldSeconds = 1.0;

	private const int MaximumCatalogProfiles = 32;

	private readonly System.Action _wake;

	private readonly System.Action<string>? _log;

	private readonly System.Action<GameObject, float> _managedScroll;

	private UICamera.FloatDelegate? _nativeScroll;

	private readonly System.Collections.Generic.Dictionary<System.IntPtr, ViewState> _views = new System.Collections.Generic.Dictionary<System.IntPtr, ViewState>();

	private readonly HashSet<System.IntPtr> _seen = new HashSet<System.IntPtr>();

	private readonly System.Collections.Generic.List<System.IntPtr> _retired = new System.Collections.Generic.List<System.IntPtr>();

	private long _scrollGeneration;

	private long _consumedGeneration;

	private double _holdUntil;

	private double _nextDiscovery;

	private double _lastNow = double.NaN;

	private int _scene = int.MinValue;

	private bool _focused;

	private bool _pointerButtonDown;

	private bool _subscribed;

	private bool _failed;

	private bool _disposed;

	public ScrollActivityGuard(System.Action wake, System.Action<string>? log = null)
	{
		_wake = wake ?? throw new System.ArgumentNullException("wake");
		_log = log;
		_managedScroll = OnScroll;
	}

	public bool Observe(double now, bool focused)
	{
		return Observe(now, focused, sampleViewportMotion: true);
	}

	public bool Observe(double now, bool focused, bool sampleViewportMotion)
	{
		if (_disposed)
		{
			return false;
		}
		if (!double.IsFinite(now) || now < 0.0)
		{
			return focused;
		}
		if (_failed)
		{
			return focused;
		}
		try
		{
			bool wasProtected = now < _holdUntil;
			bool flag = double.IsFinite(_lastNow) && now < _lastNow;
			_lastNow = now;
			if (flag)
			{
				_nextDiscovery = 0.0;
				_holdUntil = now;
				wasProtected = false;
				foreach (ViewState value in _views.Values)
				{
					value.HasSample = false;
				}
			}
			int handle = SceneManager.GetActiveScene().handle;
			bool flag2 = handle != _scene;
			if (flag2)
			{
				_scene = handle;
				_views.Clear();
				_nextDiscovery = 0.0;
			}
			long num = Interlocked.Read(ref _scrollGeneration);
			bool flag3 = num != _consumedGeneration;
			_consumedGeneration = num;
			if (!focused)
			{
				_focused = false;
				_pointerButtonDown = false;
				_holdUntil = now;
				foreach (ViewState value2 in _views.Values)
				{
					value2.HasSample = false;
				}
				return false;
			}
			bool flag4 = !_focused || flag2 || flag || flag3;
			_focused = true;
			Vector2 mouseScrollDelta = Input.mouseScrollDelta;
			bool rawScroll = !float.IsFinite(mouseScrollDelta.x) || !float.IsFinite(mouseScrollDelta.y) || mouseScrollDelta.sqrMagnitude > 0f;
			bool pointerButtonStarted = false;
			if (!sampleViewportMotion)
			{
				bool buttonDown = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
				pointerButtonStarted = buttonDown && !_pointerButtonDown;
				_pointerButtonDown = buttonDown;
			}
			bool pointerButton = _pointerButtonDown;
			flag4 |= rawScroll || pointerButton;
			if (flag2 || now >= _nextDiscovery || flag3 || rawScroll || pointerButtonStarted)
			{
				_nextDiscovery = now + 1.0;
				EnsureSubscription();
				DiscoverViews();
			}
			if (sampleViewportMotion)
			{
				foreach (ViewState value3 in _views.Values)
				{
					flag4 |= value3.ObserveMotion();
				}
			}
			if (flag4)
			{
				_holdUntil = now + 1.0;
				if (!wasProtected)
				{
					_wake();
				}
			}
			return now < _holdUntil;
		}
		catch (System.Exception ex)
		{
			_failed = true;
			try
			{
				RemoveSubscription();
			}
			catch
			{
			}
			_views.Clear();
			_log?.Invoke("Scroll activity observation failed closed: " + ex.GetType().Name + ".");
			if (focused)
			{
				_wake();
			}
			return focused;
		}
	}

	private void OnScroll(GameObject _, float delta)
	{
		if (float.IsFinite(delta) && delta != 0f)
		{
			Interlocked.Increment(ref _scrollGeneration);
		}
	}

	private void EnsureSubscription()
	{
		if (_subscribed)
		{
			if (_nativeScroll == null || !Contains(UICamera.onScroll, _nativeScroll))
			{
				throw new System.InvalidOperationException("NGUI wheel callback replaced");
			}
			return;
		}
		if ((object)_nativeScroll == null)
		{
			_nativeScroll = DelegateSupport.ConvertDelegate<UICamera.FloatDelegate>(_managedScroll) ?? throw new System.InvalidOperationException("NGUI wheel delegate conversion failed");
		}
		_subscribed = true;
		UICamera.onScroll = (Il2CppSystem.Delegate.Combine(UICamera.onScroll, _nativeScroll) ?? throw new System.InvalidOperationException("NGUI wheel delegate combination failed")).Cast<UICamera.FloatDelegate>();
		if (Contains(UICamera.onScroll, _nativeScroll))
		{
			return;
		}
		throw new System.InvalidOperationException("NGUI wheel delegate readback failed");
	}

	private void DiscoverViews()
	{
		_seen.Clear();
		ScriptNodeInspector instance = ScriptNodeInspector.instance;
		if (instance != null)
		{
			Track(instance.scriptListScroll, instance.scriptListScroll);
			Track(instance.characterTabScroll, instance.characterTabScroll);
			Track(instance.environmentTabScroll, instance.environmentTabScroll);
		}
		Catalog instance2 = Singleton<Catalog>.Instance;
		Il2CppSystem.Collections.Generic.List<Catalog.UIProfile> list = ((instance2 != null) ? instance2.uiProfiles : null);
		if (list != null)
		{
			for (int i = 0; i < System.Math.Min(list.Count, 32); i++)
			{
				Catalog.UIProfile uIProfile = list[i];
				if (uIProfile != null)
				{
					Track(uIProfile.scroll);
				}
			}
		}
		UIPopupModManager instance3 = UIPopupModManager.instance;
		if (instance3 != null)
		{
			Track(instance3.scroll);
		}
		BackgroundExplorer instance4 = Singleton<BackgroundExplorer>.Instance;
		if (instance4 != null)
		{
			Track(instance4.scroll);
			BgSortingHierarchy hierarchy = instance4.hierarchy;
			UITable uITable = ((hierarchy != null) ? hierarchy.rootTable : null);
			if (uITable != null)
			{
				Track(uITable.GetComponentInParent<UIScrollView>());
			}
		}
		_retired.Clear();
		foreach (System.IntPtr key in _views.Keys)
		{
			if (!_seen.Contains(key))
			{
				_retired.Add(key);
			}
		}
		foreach (System.IntPtr item in _retired)
		{
			_views.Remove(item);
		}
	}

	private void Track(UIScrollView? scroll, CenterableUIScrollView? centerable = null)
	{
		if (!(scroll == null) && scroll.isActiveAndEnabled && _seen.Add(scroll.Pointer))
		{
			if (_views.TryGetValue(scroll.Pointer, out ViewState value))
			{
				value.Scroll = scroll;
				value.Centerable = centerable;
			}
			else
			{
				_views.Add(scroll.Pointer, new ViewState(scroll, centerable));
			}
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

	private void RemoveSubscription()
	{
		if (_subscribed && !(_nativeScroll == null))
		{
			UICamera.FloatDelegate onScroll = UICamera.onScroll;
			if (Contains(onScroll, _nativeScroll))
			{
				Il2CppSystem.Delegate obj = Il2CppSystem.Delegate.Remove(onScroll, _nativeScroll);
				UICamera.onScroll = ((obj == null) ? null : obj.Cast<UICamera.FloatDelegate>());
			}
			_subscribed = false;
			System.GC.KeepAlive(_nativeScroll);
			System.GC.KeepAlive(_managedScroll);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			try
			{
				RemoveSubscription();
			}
			catch (System.Exception ex)
			{
				_log?.Invoke("Scroll observer unsubscribe unavailable: " + ex.GetType().Name + ".");
			}
			_views.Clear();
			_seen.Clear();
			_retired.Clear();
		}
	}
}
