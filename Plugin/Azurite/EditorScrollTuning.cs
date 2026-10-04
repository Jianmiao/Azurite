using System;
using System.Collections.Generic;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using Studio.Scripts.Window.BackgroundExplorer;
using UI;

namespace Azurite;

internal sealed class EditorScrollTuning : IDisposable
{
	private sealed class Lease
	{
		public UIScrollView View { get; }

		public string Scope { get; }

		public float Original { get; set; }

		public float LastWritten { get; set; }

		public bool HasBaseline { get; set; }

		public bool Owned { get; set; }

		public bool Blocked { get; set; }

		public Lease(UIScrollView view, string scope)
		{
			View = view;
			Scope = scope;
		}
	}

	private const float MaximumMultiplier = 6f;

	private const int MaximumCatalogProfiles = 32;

	private readonly Action<string>? _log;

	private readonly System.Collections.Generic.Dictionary<IntPtr, Lease> _views = new System.Collections.Generic.Dictionary<IntPtr, Lease>();

	private readonly HashSet<IntPtr> _seen = new HashSet<IntPtr>();

	private readonly System.Collections.Generic.List<IntPtr> _retired = new System.Collections.Generic.List<IntPtr>();

	private double _nextProbe;

	private bool _engaged;

	private bool _disposed;

	private bool _failed;

	public EditorScrollTuning(Action<string>? log = null)
	{
		_log = log;
	}

	public void Update(double now, bool allowed, float multiplier)
	{
		Update(now, allowed, multiplier, multiplier);
	}

	public void Update(double now, bool allowed, float multiplier, float dialogueMultiplier)
	{
		Update(now, allowed, multiplier, dialogueMultiplier, 1f);
	}

	public void Update(double now, bool allowed, float multiplier, float dialogueMultiplier, float modManagerMultiplier)
	{
		Update(now, allowed, multiplier, dialogueMultiplier, modManagerMultiplier, 1f);
	}

	public void Update(double now, bool allowed, float multiplier, float dialogueMultiplier, float modManagerMultiplier, float backgroundMultiplier)
	{
		if (_disposed || _failed)
		{
			return;
		}
		multiplier = NormalizeMultiplier(multiplier);
		dialogueMultiplier = NormalizeMultiplier(dialogueMultiplier);
		modManagerMultiplier = NormalizeMultiplier(modManagerMultiplier);
		backgroundMultiplier = NormalizeMultiplier(backgroundMultiplier);
		if (!allowed || (multiplier <= 1f && dialogueMultiplier <= 1f && modManagerMultiplier <= 1f && backgroundMultiplier <= 1f))
		{
			Restore();
		}
		else
		{
			if (!double.IsFinite(now) || now < _nextProbe)
			{
				return;
			}
			_nextProbe = now + 1.0;
			_engaged = true;
			try
			{
				_seen.Clear();
				Catalog instance = Singleton<Catalog>.Instance;
				if (instance != null)
				{
					Il2CppSystem.Collections.Generic.List<Catalog.UIProfile> uiProfiles = instance.uiProfiles;
					if (uiProfiles != null)
					{
						int num = Math.Min(uiProfiles.Count, 32);
						for (int i = 0; i < num; i++)
						{
							Catalog.UIProfile uIProfile = uiProfiles[i];
							if (uIProfile != null)
							{
								Touch(uIProfile.scroll, "catalog", multiplier);
							}
						}
					}
				}
				ScriptNodeInspector instance2 = ScriptNodeInspector.instance;
				if (instance2 != null)
				{
					Touch(instance2.scriptListScroll, "dialogue list", dialogueMultiplier);
					Touch(instance2.characterTabScroll, "character properties", multiplier);
					Touch(instance2.environmentTabScroll, "environment properties", multiplier);
				}
				UIPopupModManager instance3 = UIPopupModManager.instance;
				if (instance3 != null)
				{
					Touch(instance3.scroll, "mod manager", modManagerMultiplier);
				}
				BackgroundExplorer instance4 = Singleton<BackgroundExplorer>.Instance;
				if (instance4 != null)
				{
					Touch(instance4.scroll, "background gallery", backgroundMultiplier);
					BgSortingHierarchy hierarchy = instance4.hierarchy;
					UITable uITable = ((hierarchy != null) ? hierarchy.rootTable : null);
					if (uITable != null)
					{
						UIScrollView componentInParent = uITable.GetComponentInParent<UIScrollView>();
						Touch(componentInParent, "background categories", backgroundMultiplier);
					}
				}
				_retired.Clear();
				foreach (System.Collections.Generic.KeyValuePair<IntPtr, Lease> view in _views)
				{
					if (!_seen.Contains(view.Key))
					{
						Release(view.Value);
						if (!view.Value.Blocked || view.Value.View == null)
						{
							_retired.Add(view.Key);
						}
					}
				}
				foreach (IntPtr item in _retired)
				{
					_views.Remove(item);
				}
			}
			catch (Exception ex)
			{
				_failed = true;
				Restore();
				_log?.Invoke("Editor wheel tuning stopped: " + ex.GetType().Name + ".");
			}
		}
	}

	private static float NormalizeMultiplier(float multiplier)
	{
		if (!float.IsFinite(multiplier) || !(multiplier > 1f))
		{
			return 1f;
		}
		return Math.Min(multiplier, 6f);
	}

	private void Touch(UIScrollView? view, string scope, float multiplier)
	{
		if (view == null)
		{
			return;
		}
		IntPtr pointer = view.Pointer;
		if (pointer == IntPtr.Zero || !_seen.Add(pointer))
		{
			return;
		}
		if (!_views.TryGetValue(pointer, out Lease value))
		{
			if (multiplier <= 1f)
			{
				return;
			}
			value = new Lease(view, scope);
			_views.Add(pointer, value);
		}
		if (multiplier <= 1f)
		{
			Release(value);
		}
		else
		{
			if (value.Blocked)
			{
				return;
			}
			float scrollWheelFactor = view.scrollWheelFactor;
			if (value.HasBaseline && scrollWheelFactor != (value.Owned ? value.LastWritten : value.Original))
			{
				Relinquish(value);
			}
			else
			{
				if (!float.IsFinite(scrollWheelFactor) || scrollWheelFactor == 0f)
				{
					return;
				}
				if (!value.HasBaseline)
				{
					value.Original = scrollWheelFactor;
					value.HasBaseline = true;
				}
				float num = value.Original * multiplier;
				if (!float.IsFinite(num))
				{
					return;
				}
				if (scrollWheelFactor != num)
				{
					value.LastWritten = num;
					value.Owned = true;
					view.scrollWheelFactor = num;
					if (view.scrollWheelFactor != num)
					{
						Relinquish(value);
						return;
					}
					_log?.Invoke($"Editor wheel tuning: {scope}; factor={value.Original:0.###}->{num:0.###}; multiplier={multiplier:0.##}.");
				}
				else
				{
					value.LastWritten = num;
					value.Owned = true;
				}
			}
		}
	}

	public void Restore()
	{
		if (!_engaged)
		{
			return;
		}
		_engaged = false;
		_nextProbe = 0.0;
		foreach (Lease value in _views.Values)
		{
			Release(value);
		}
	}

	private void Release(Lease lease)
	{
		if (!lease.Owned)
		{
			return;
		}
		try
		{
			if (lease.View != null)
			{
				if (lease.View.scrollWheelFactor != lease.LastWritten)
				{
					Relinquish(lease);
					return;
				}
				lease.View.scrollWheelFactor = lease.Original;
			}
			lease.Owned = false;
		}
		catch (Exception ex)
		{
			lease.Owned = false;
			lease.Blocked = true;
			_log?.Invoke($"Editor wheel restore stopped for {lease.Scope}: {ex.GetType().Name}.");
		}
	}

	private void Relinquish(Lease lease)
	{
		lease.Owned = false;
		lease.Blocked = true;
		_log?.Invoke("Editor wheel tuning yielded for " + lease.Scope + ": another writer changed the factor.");
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			Restore();
			_disposed = true;
			_views.Clear();
			_seen.Clear();
			_retired.Clear();
		}
	}
}
