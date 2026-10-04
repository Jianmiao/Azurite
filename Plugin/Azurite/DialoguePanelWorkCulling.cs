using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using Studio.Scripts.Window;
using UnityEngine;

namespace Azurite;

internal sealed class DialoguePanelWorkCulling : System.IDisposable
{
	internal readonly record struct Bounds2(float Left, float Bottom, float Right, float Top)
	{
		public float Height => Top - Bottom;

		public float Width => Right - Left;

		public float CenterY => (Bottom + Top) * 0.5f;

		public Bounds2 Include(float x, float y)
		{
			return new Bounds2(System.Math.Min(Left, x), System.Math.Min(Bottom, y), System.Math.Max(Right, x), System.Math.Max(Top, y));
		}
	}

	private sealed class Entry
	{
		public readonly ScriptListItem Row;

		public readonly UIPanel Panel;

		public readonly Bounds2 Bounds;

		public bool Outside;

		public Entry(ScriptListItem row, UIPanel panel, Bounds2 bounds)
		{
			Row = row;
			Panel = panel;
			Bounds = bounds;
		}
	}

	private const int MaximumRows = 4096;

	private const string Owner = "halocue.azurite.dialogue-panel-work";

	private readonly Harmony _harmony = new Harmony("halocue.azurite.dialogue-panel-work");

	private readonly System.Action<string> _log;

	private readonly System.Collections.Generic.Dictionary<System.IntPtr, Entry> _panels = new System.Collections.Generic.Dictionary<System.IntPtr, Entry>();

	private readonly System.Collections.Generic.List<Entry> _rows = new System.Collections.Generic.List<Entry>();

	private static DialoguePanelWorkCulling? _active;

	private ScriptNodeInspector? _inspector;

	private UIGrid? _grid;

	private UIPanel? _viewport;

	private Transform? _content;

	private System.IntPtr _cachePointer;

	private System.IntPtr _selectedPointer;

	private int _rowCount;

	private int _nextRow;

	private int _frame = -1;

	private int _checkedFrame = -1;

	private long _layoutRevision;

	private long _builtRevision;

	private System.Action? _managedReposition;

	private UIGrid.OnReposition? _nativeReposition;

	private bool _installed;

	private bool _disposed;

	private bool _allowed;

	private bool _ready;

	private bool _matrixMatches;

	private double _nextIdentityProbe;

	private double _nextLog;

	private Matrix4x4 _contentMatrix;

	private Matrix4x4 _viewportMatrix;

	private Vector4 _clip;

	private float _pitch;

	private long _eligible;

	private long _skipped;

	private double _updateMilliseconds;

	public bool Enabled { get; set; }

	public bool DryRun { get; set; } = true;

	public string Reason { get; private set; } = "not initialized";

	public int RegisteredPanels => _panels.Count;

	public int BuildingRows => _nextRow;

	public long EligibleCalls => _eligible;

	public long SkippedCalls => _skipped;

	public DialoguePanelWorkCulling(System.Action<string> log)
	{
		_log = log ?? throw new System.ArgumentNullException("log");
	}

	public bool Install()
	{
		if (_disposed || (_active != null && _active != this))
		{
			return false;
		}
		if (_installed)
		{
			return true;
		}
		try
		{
			MethodInfo original = AccessTools.Method(typeof(UIPanel), "UpdateSelf", System.Type.EmptyTypes) ?? throw new System.MissingMethodException("UIPanel.UpdateSelf()");
			MethodInfo original2 = AccessTools.Method(typeof(ScriptListItem), "Refresh", System.Type.EmptyTypes) ?? throw new System.MissingMethodException("ScriptListItem.Refresh()");
			_harmony.Patch(original, new HarmonyMethod(typeof(DialoguePanelWorkCulling), "BeforeUpdate"));
			_harmony.Patch(original2, new HarmonyMethod(typeof(DialoguePanelWorkCulling), "BeforeRefresh"));
			_active = this;
			_installed = true;
			return true;
		}
		catch (System.Exception ex)
		{
			try
			{
				_harmony.UnpatchSelf();
			}
			catch
			{
			}
			_log("Dialogue panel experiment unavailable: " + ex.GetType().Name);
			return false;
		}
	}

	public void Update(double now, bool allowed)
	{
		_frame = -1;
		_allowed = allowed && Enabled && _installed && !_disposed;
		if (!_allowed)
		{
			Reason = "disabled or protected host";
			return;
		}
		long timestamp = Stopwatch.GetTimestamp();
		try
		{
			ScriptNodeInspector instance = ScriptNodeInspector.instance;
			WindowManager instance2 = Singleton<WindowManager>.Instance;
			if (instance == null || !instance.isActiveAndEnabled || instance.loading || instance.unloading || instance.rearrangeScheduled || UICamera.isDragging || UICamera.inputHasFocus || instance2 == null || instance2.activeWindow != null || (instance2.blocking != null && instance2.blocking.activeInHierarchy))
			{
				Reason = "editor transition input or modal protection";
				return;
			}
			UIGrid scriptList = instance.scriptList;
			CenterableUIScrollView scriptListScroll = instance.scriptListScroll;
			UIPanel uIPanel = ((scriptListScroll != null && scriptListScroll.isActiveAndEnabled) ? scriptListScroll.panel : null);
			Il2CppSystem.Collections.Generic.List<ScriptListItem> scriptNodeListItemsCache = instance.scriptNodeListItemsCache;
			if (scriptList == null || scriptList.animateSmoothly || scriptList.animateFadeIn || uIPanel == null || !uIPanel.hasClipping || scriptNodeListItemsCache == null || scriptNodeListItemsCache.Count < 32 || scriptNodeListItemsCache.Count > 4096)
			{
				Reason = "list geometry is unsupported";
				return;
			}
			ScriptListItem selectedScriptItem = instance.selectedScriptItem;
			System.IntPtr intPtr = ((selectedScriptItem == null) ? System.IntPtr.Zero : selectedScriptItem.Pointer);
			if (_inspector == null || _inspector.Pointer != instance.Pointer || _grid == null || _grid.Pointer != scriptList.Pointer || _viewport == null || _viewport.Pointer != uIPanel.Pointer || _cachePointer != scriptNodeListItemsCache.Pointer || _rowCount != scriptNodeListItemsCache.Count || _selectedPointer != intPtr || _builtRevision != _layoutRevision)
			{
				ResetRegistry();
				_inspector = instance;
				_grid = scriptList;
				_viewport = uIPanel;
				_content = scriptList.transform;
				_cachePointer = scriptNodeListItemsCache.Pointer;
				_rowCount = scriptNodeListItemsCache.Count;
				_selectedPointer = intPtr;
				_builtRevision = _layoutRevision;
				_managedReposition = delegate
				{
					_layoutRevision++;
				};
				_nativeReposition = DelegateSupport.ConvertDelegate<UIGrid.OnReposition>(_managedReposition);
				scriptList.onReposition = Il2CppSystem.Delegate.Combine(scriptList.onReposition, _nativeReposition).Cast<UIGrid.OnReposition>();
			}
			if (_content == null)
			{
				return;
			}
			if (!_ready)
			{
				Matrix4x4 worldToLocalMatrix = _content.worldToLocalMatrix;
				int num = System.Math.Min(_rowCount, _nextRow + 64);
				while (_nextRow < num && ((_nextRow & 7) != 0 || !(Elapsed(timestamp) >= 2.0)))
				{
					ScriptListItem row = scriptNodeListItemsCache[_nextRow];
					if (!TryBuild(row, worldToLocalMatrix, out Entry entry))
					{
						ResetRegistry();
						Reason = "row structure is not a uniform scoped text list";
						return;
					}
					if (_panels.ContainsKey(entry.Panel.Pointer))
					{
						ResetRegistry();
						Reason = "multiple rows share a text panel";
						return;
					}
					_rows.Add(entry);
					_panels.Add(entry.Panel.Pointer, entry);
					_nextRow++;
				}
				if (_nextRow != _rowCount)
				{
					Reason = "building row bounds in bounded batches";
					return;
				}
				if (!ValidateUniformRows())
				{
					ResetRegistry();
					Reason = "row spacing is not uniform";
					return;
				}
				_ready = true;
			}
			if (now >= _nextIdentityProbe)
			{
				_nextIdentityProbe = now + 1.0;
				for (int num2 = 0; num2 < _rowCount; num2++)
				{
					if (scriptNodeListItemsCache[num2] == null || scriptNodeListItemsCache[num2].Pointer != _rows[num2].Row.Pointer)
					{
						_layoutRevision++;
						Reason = "row identity changed";
						return;
					}
				}
			}
			_contentMatrix = _content.localToWorldMatrix;
			_viewportMatrix = uIPanel.worldToLocal;
			_clip = uIPanel.finalClipRegion;
			if (!Finite(_clip.x) || !Finite(_clip.y) || !Finite(_clip.z) || !Finite(_clip.w) || _clip.z <= 0f || _clip.w <= 0f)
			{
				Reason = "viewport bounds invalid";
				return;
			}
			foreach (Entry row2 in _rows)
			{
				row2.Outside = IsOutside(row2.Bounds, _contentMatrix, _viewportMatrix, _clip, _pitch * 2f);
			}
			_frame = Time.frameCount;
			_checkedFrame = -1;
			Reason = (DryRun ? "dry-run eligible offscreen panel work" : "scoped offscreen panel work enabled");
		}
		catch (System.Exception ex)
		{
			_allowed = false;
			Reason = "panel scope failed closed";
			ResetRegistry();
			_log("Dialogue panel scope unavailable: " + ex.GetType().Name);
		}
		finally
		{
			_updateMilliseconds += Elapsed(timestamp);
			if (now >= _nextLog)
			{
				_nextLog = now + 5.0;
				_log($"dialogue-panel-work registered={_panels.Count} building={_nextRow}/{_rowCount} eligible={_eligible} skipped={_skipped} dryRun={DryRun} observerMs={_updateMilliseconds:F2} reason={Reason}.");
				_updateMilliseconds = 0.0;
			}
		}
	}

	private bool TryBuild(ScriptListItem row, Matrix4x4 inverse, out Entry entry)
	{
		entry = null;
		if (row == null || row.child == null || row.scriptPhonetic == null || row.inspector == null || row.inspector.Pointer != _inspector.Pointer)
		{
			return false;
		}
		UILabel regionText = row.scriptPhonetic.regionText;
		if (regionText == null)
		{
			return false;
		}
		UIPanel componentInParent = regionText.GetComponentInParent<UIPanel>();
		if (componentInParent == null || componentInParent.Pointer == _viewport.Pointer || !componentInParent.transform.IsChildOf(row.transform) || componentInParent.GetComponent<UIScrollView>() != null)
		{
			return false;
		}
		Il2CppStructArray<Vector3> worldCorners = row.child.worldCorners;
		if (worldCorners == null || worldCorners.Length != 4)
		{
			return false;
		}
		Bounds2 bounds = new Bounds2(float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);
		for (int i = 0; i < 4; i++)
		{
			Vector3 vector = TransformPoint(inverse, worldCorners[i]);
			if (!Finite(vector.x) || !Finite(vector.y))
			{
				return false;
			}
			bounds = bounds.Include(vector.x, vector.y);
		}
		if (bounds.Height <= 0f || bounds.Width <= 0f)
		{
			return false;
		}
		entry = new Entry(row, componentInParent, bounds);
		return true;
	}

	private bool ValidateUniformRows()
	{
		if (_rows.Count < 2)
		{
			return false;
		}
		Bounds2 bounds = _rows[0].Bounds;
		float num = _rows[1].Bounds.CenterY - bounds.CenterY;
		_pitch = System.Math.Abs(num);
		if (!Finite(_pitch) || _pitch < bounds.Height * 0.75f)
		{
			return false;
		}
		for (int i = 1; i < _rows.Count; i++)
		{
			Bounds2 bounds2 = _rows[i].Bounds;
			if (System.Math.Abs(bounds2.Height - bounds.Height) > 1f || System.Math.Abs(bounds2.Width - bounds.Width) > 1f || System.Math.Abs(bounds2.CenterY - (bounds.CenterY + (float)i * num)) > 1f)
			{
				return false;
			}
		}
		return true;
	}

	private static bool BeforeUpdate(UIPanel __instance)
	{
		DialoguePanelWorkCulling active = _active;
		if (active == null || !active._allowed || !active.Enabled || !active._ready || active._frame != Time.frameCount || !active._panels.TryGetValue(__instance.Pointer, out Entry value) || !value.Outside)
		{
			return true;
		}
		try
		{
			if (active._checkedFrame != active._frame)
			{
				active._checkedFrame = active._frame;
				active._matrixMatches = active._content != null && active._viewport != null && active._content.localToWorldMatrix == active._contentMatrix && active._viewport.worldToLocal == active._viewportMatrix && active._viewport.finalClipRegion == active._clip;
			}
			if (!active._matrixMatches || active._builtRevision != active._layoutRevision || __instance.mRebuild || __instance.mUpdateScroll || __instance.onGeometryUpdated != null || __instance.drawCalls == null || __instance.drawCalls.Count != 0)
			{
				return true;
			}
			active._eligible++;
			if (active.DryRun)
			{
				return true;
			}
			active._skipped++;
			return false;
		}
		catch
		{
			return true;
		}
	}

	private static void BeforeRefresh(ScriptListItem __instance)
	{
		DialoguePanelWorkCulling active = _active;
		if (active == null || !active._allowed || __instance == null)
		{
			return;
		}
		try
		{
			if (__instance.inspector != null && active._inspector != null && __instance.inspector.Pointer == active._inspector.Pointer)
			{
				active._layoutRevision++;
			}
		}
		catch
		{
			active._layoutRevision++;
		}
	}

	public void Restore()
	{
		_allowed = false;
		_frame = -1;
		ResetRegistry();
	}

	private void ResetRegistry()
	{
		_ready = false;
		_frame = -1;
		_nextRow = 0;
		_panels.Clear();
		_rows.Clear();
		if (_grid != null && _nativeReposition != null)
		{
			try
			{
				Il2CppSystem.Delegate obj = Il2CppSystem.Delegate.Remove(_grid.onReposition, _nativeReposition);
				_grid.onReposition = ((obj == null) ? null : obj.Cast<UIGrid.OnReposition>());
			}
			catch
			{
			}
		}
		_grid = null;
		_content = null;
		_viewport = null;
		_inspector = null;
		_managedReposition = null;
		_nativeReposition = null;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			Restore();
			if (_active == this)
			{
				_active = null;
			}
			if (_installed)
			{
				_harmony.UnpatchSelf();
			}
			_installed = false;
		}
	}

	private static double Elapsed(long start)
	{
		return (double)(Stopwatch.GetTimestamp() - start) * 1000.0 / (double)Stopwatch.Frequency;
	}

	private static bool Finite(float value)
	{
		return float.IsFinite(value);
	}

	internal static Vector3 TransformPoint(Matrix4x4 m, Vector3 p)
	{
		return new Vector3(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23);
	}

	internal static bool IsOutside(Bounds2 bounds, Matrix4x4 content, Matrix4x4 viewport, Vector4 clip, float overscan)
	{
		float num = float.PositiveInfinity;
		float num2 = float.PositiveInfinity;
		float num3 = float.NegativeInfinity;
		float num4 = float.NegativeInfinity;
		for (int i = 0; i < 4; i++)
		{
			Vector3 vector = TransformPoint(viewport, TransformPoint(content, new Vector3(((i & 1) == 0) ? bounds.Left : bounds.Right, ((i & 2) == 0) ? bounds.Bottom : bounds.Top, 0f)));
			if (!Finite(vector.x) || !Finite(vector.y))
			{
				return false;
			}
			num = System.Math.Min(num, vector.x);
			num3 = System.Math.Max(num3, vector.x);
			num2 = System.Math.Min(num2, vector.y);
			num4 = System.Math.Max(num4, vector.y);
		}
		Vector3 vector2 = TransformPoint(viewport, TransformPoint(content, default(Vector3)));
		Vector3 vector3 = TransformPoint(viewport, TransformPoint(content, new Vector3(0f, overscan, 0f)));
		float num5 = System.Math.Max(System.Math.Abs(vector3.y - vector2.y), System.Math.Abs(vector3.x - vector2.x));
		if (!(num3 < clip.x - clip.z * 0.5f - num5) && !(num > clip.x + clip.z * 0.5f + num5) && !(num4 < clip.y - clip.w * 0.5f - num5))
		{
			return num2 > clip.y + clip.w * 0.5f + num5;
		}
		return true;
	}
}
