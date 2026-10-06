using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using UnityEngine;

namespace Azurite;

/// <summary>
/// Keeps the native insert/delete/sync algorithm, including selection and grid
/// callbacks, but reuses the unchanged row objects during its destroy/add pass.
/// Never changes Script data. All interception is limited to one synchronous
/// main-thread mutation, its exact grid parent and its exact row prefab.
/// </summary>
internal sealed class DialogueRowReuse : IDisposable
{
	private const string Owner = "halocue.azurite.dialogue-row-reuse";
	private readonly Harmony _harmony = new(Owner);
	private readonly Action<string> _log;
	private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
	private static DialogueRowReuse? _active;
	[ThreadStatic] private static Scope? _scope;
	private bool _installed;
	private bool _disposed;
	private bool _allowed;
	private bool _failed;
	private long _reused;
	private long _created;
	private long _removed;
	private long _fallbacks;
	private long _attempts;
	private long _captured;
	private long _eligible;
	private long _commits;
	private string _lastRejectedReason = "none";
	private double _nextLog;

	private sealed class Snapshot
	{
		internal bool Consumed;
		internal ScriptNodeInspector Inspector = null!;
		internal ScriptNode Node = null!;
		internal UIGrid Grid = null!;
		internal Transform Parent = null!;
		internal GameObject Prefab = null!;
		internal IntPtr Scripts;
		internal readonly List<Script> Data = new();
		internal readonly List<ScriptListItem> Rows = new();
		internal readonly Dictionary<IntPtr, ScriptListItem> ByScript = new();
	}

	private sealed class Scope
	{
		internal Scope? Previous;
		internal Snapshot? Snapshot;
		internal bool DelegatingInsert;
		internal bool IsSync;
		internal bool DestroyIntercepted;
		internal bool Invalid;
		internal bool RecoveryAttempted;
		internal bool Mute;
		internal bool KeepSelected;
		internal bool FirstOnTop;
		internal int Next;
		internal ScriptListItem?[]? Reuse;
		internal Script[]? NewData;
		internal readonly List<GameObject> PendingDestruction = new();
	}

	public DialogueRowReuse(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));
	public bool IsInstalled => _installed && !_disposed;
	public bool Enabled { get; set; }

	public bool Install()
	{
		if (_disposed || (_active != null && _active != this)) return false;
		if (_installed) return true;
		try
		{
			_active = this;
			Patch(typeof(ScriptNodeInspector), "InsertScript", Type.EmptyTypes, nameof(BeginMutation), null, nameof(EndScope));
			Patch(typeof(ScriptNodeInspector), "InsertScript", new[] { typeof(int) }, nameof(BeginMutation), null, nameof(EndScope));
			Patch(typeof(ScriptNodeInspector), "DeleteScript", Type.EmptyTypes, nameof(BeginMutation), null, nameof(EndScope));
			Patch(typeof(ScriptNodeInspector), "SyncScriptList", new[] { typeof(bool), typeof(bool), typeof(bool) }, nameof(BeginSync), nameof(CompleteSync), nameof(EndScope));
			Patch(typeof(NGUITools), "DestroyChildren", new[] { typeof(Transform), typeof(int) }, nameof(BeforeDestroyChildren));
			Patch(typeof(NGUITools), "AddChild", new[] { typeof(Transform), typeof(GameObject) }, nameof(BeforeAddChild), nameof(AfterAddChild));
			_installed = true;
			_log("dialogue row reuse hooks ready; opt-in only; original Script data, Init/Refresh, selection and grid reflow remain synchronous.");
			return true;
		}
		catch (Exception error)
		{
			try { _harmony.UnpatchSelf(); } catch { }
			if (_active == this) _active = null;
			_log("dialogue row reuse unavailable: " + error.GetType().Name + ": " + error.Message);
			return false;
		}
	}

	private void Patch(Type type, string name, Type[] arguments, string prefix, string? postfix = null, string? finalizer = null)
	{
		MethodInfo method = AccessTools.Method(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);
		_harmony.Patch(method, new HarmonyMethod(typeof(DialogueRowReuse), prefix), postfix == null ? null : new HarmonyMethod(typeof(DialogueRowReuse), postfix), null, finalizer == null ? null : new HarmonyMethod(typeof(DialogueRowReuse), finalizer), null);
		if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) != true) throw new InvalidOperationException("Row reuse hook was not registered: " + name);
	}

	public void Update(double now, bool allowed)
	{
		_allowed = allowed && Enabled && IsInstalled && !_failed;
		if (now < _nextLog) return;
		_nextLog = now + 5;
		if (_attempts + _captured + _eligible + _commits + _reused + _created + _removed + _fallbacks != 0)
		{
			_log($"dialogue-row-reuse attempts={_attempts} captured={_captured} eligible={_eligible} commits={_commits} reused={_reused} created={_created} removed={_removed} fallbacks={_fallbacks} lastRejectedReason={_lastRejectedReason} enabled={_allowed}; counts since last report; native final reflow retained.");
			_reused = _created = _removed = _fallbacks = 0;
			_attempts = _captured = _eligible = _commits = 0;
			_lastRejectedReason = "none";
		}
	}

	public void Suspend() => _allowed = false;

	private static bool MayRun(DialogueRowReuse? active) => active != null && active._allowed && active.Enabled && !active._failed && active._thread == Thread.CurrentThread.ManagedThreadId;

	private void Reject(string reason) { _fallbacks++; _lastRejectedReason = reason; }

	private static void BeginMutation(ScriptNodeInspector __instance, MethodBase __originalMethod, out IntPtr __state)
	{
		Scope current = new() { Previous = _scope, DelegatingInsert = __originalMethod.Name == "InsertScript" && __originalMethod.GetParameters().Length == 0 };
		__state = GCHandle.ToIntPtr(GCHandle.Alloc(current));
		_scope = current;
		DialogueRowReuse? active = _active;
		if (current.Previous == null && active != null) active._attempts++;
		if (!MayRun(active))
		{
			if (current.Previous == null && active != null) active.Reject(active._failed ? "gate.session-failed" : !active.Enabled ? "gate.disabled" : active._thread != Thread.CurrentThread.ManagedThreadId ? "gate.foreign-thread" : "gate.suspended-or-scene-protected");
			return;
		}
		try
		{
			// Parameterless InsertScript delegates to InsertScript(index). Share the
			// original pre-mutation snapshot, but do not permit nested unrelated calls.
			if (current.Previous != null)
			{
				Snapshot? prior = current.Previous.Snapshot;
				if (current.Previous.DelegatingInsert && __originalMethod.Name == "InsertScript" && __originalMethod.GetParameters().Length == 1 && prior != null && !prior.Consumed && Same(prior.Inspector, __instance)) current.Snapshot = prior;
				else if (prior != null) { prior.Consumed = true; active!.Reject("mutation.nested-or-consumed"); }
				return;
			}
			current.Snapshot = Capture(__instance, out string reason);
			if (current.Snapshot == null) active!.Reject(reason);
			else active!._captured++;
		}
		catch { active!.Reject("capture.native-access-exception"); }
	}

	private static Snapshot? Capture(ScriptNodeInspector inspector, out string reason)
	{
		reason = "capture.scene-loading-rearranging-or-dragging";
		if (inspector == null || !inspector.isActiveAndEnabled || inspector.loading || inspector.unloading || inspector.rearrangeScheduled || UICamera.isDragging) return null;
		ScriptNode node = inspector.scriptNode;
		UIGrid grid = inspector.scriptList;
		var cache = inspector.scriptNodeListItemsCache;
		reason = "capture.missing-node-grid-or-cache";
		if (node == null || node.scripts == null || grid == null || cache == null) return null;
		reason = "capture.row-count-outside-32-to-4096";
		if (cache.Count < 32 || cache.Count > 4096) return null;
		reason = "capture.cache-data-count-mismatch";
		if (cache.Count != node.scripts.Count) return null;
		reason = "capture.grid-animation-or-springs";
		if (grid.animateSmoothly || grid.animateFadeIn || (grid.mSprings != null && grid.mSprings.Count != 0)) return null;
		Transform parent = grid.transform;
		reason = "capture.parent-children-or-prefab";
		if (parent == null || parent.childCount != cache.Count || inspector.scriptListItemPrefab == null) return null;
		Snapshot snapshot = new() { Inspector = inspector, Node = node, Grid = grid, Parent = parent, Prefab = inspector.scriptListItemPrefab, Scripts = node.scripts.Pointer };
		float firstY = 0, step = 0, width = 0, height = 0;
		for (int i = 0; i < cache.Count; i++)
		{
			ScriptListItem row = cache[i];
			Script data = node.scripts[i];
			reason = "capture.inactive-row-or-missing-data";
			if (row == null || data == null || !row.isActiveAndEnabled || !row.gameObject.activeInHierarchy) return null;
			reason = "capture.row-owner-mismatch";
			if (!Same(row.inspector, inspector) || !Same(row.scriptNode, node)) return null;
			reason = "capture.row-index-parent-or-sibling";
			if (row.index != i || !Same(row.transform.parent, parent) || row.transform.GetSiblingIndex() != i) return null;
			reason = "capture.row-components-mirage-or-selection";
			if (row.child == null || row.scriptPhonetic == null || row.nameLabel == null || (row.mirage != null && row.mirage.activeInHierarchy) || (row.selected && !Same(row, inspector.selectedScriptItem))) return null;
			reason = "capture.duplicate-script-identity";
			if (!snapshot.ByScript.TryAdd(data.Pointer, row)) return null;
			Vector3 position = row.transform.localPosition;
			reason = "capture.nonfinite-position";
			if (!float.IsFinite(position.y)) return null;
			reason = "capture.irregular-row-spacing";
			if (i == 0) { firstY = position.y; width = row.child.width; height = row.child.height; }
			else if (i == 1) { step = position.y - firstY; if (!float.IsFinite(step) || Math.Abs(step) < height * .75f) return null; }
			if (i > 1 && Math.Abs(position.y - (firstY + step * i)) > 1f) return null;
			reason = "capture.nonuniform-or-invalid-row-size";
			if (row.child.width != width || row.child.height != height || width <= 0 || height <= 0) return null;
			snapshot.Data.Add(data);
			snapshot.Rows.Add(row);
		}
		reason = "none";
		return snapshot;
	}

	private static void BeginSync(ScriptNodeInspector __instance, bool mute, bool keepSelected, bool firstOnTop, out IntPtr __state)
	{
		Scope current = new() { Previous = _scope, IsSync = true, Mute = mute, KeepSelected = keepSelected, FirstOnTop = firstOnTop };
		__state = GCHandle.ToIntPtr(GCHandle.Alloc(current));
		_scope = current;
		DialogueRowReuse? active = _active;
		if (!MayRun(active) || current.Previous == null || current.Previous.IsSync) return;
		Snapshot? snapshot = current.Previous.Snapshot;
		if (snapshot == null) return; // Preserve the earlier capture rejection.
		if (snapshot.Consumed || !Same(snapshot.Inspector, __instance)) { active!.Reject("sync.snapshot-consumed-or-inspector-changed"); return; }
		// A snapshot belongs to exactly one native rebuild. Selection callbacks or
		// other patches may synchronously issue a second Sync before Insert returns.
		snapshot.Consumed = true;
		try
		{
			if (!Same(__instance.scriptNode, snapshot.Node) || !Same(__instance.scriptList, snapshot.Grid) || !Same(__instance.scriptListItemPrefab, snapshot.Prefab)) { active!.Reject("sync.node-grid-or-prefab-changed"); return; }
			if (snapshot.Node.scripts == null || snapshot.Node.scripts.Pointer != snapshot.Scripts) { active!.Reject("sync.script-list-replaced"); return; }
			if (snapshot.Parent.childCount != snapshot.Rows.Count) { active!.Reject("sync.parent-child-count-changed"); return; }
			var scripts = snapshot.Node.scripts;
			if (Math.Abs(scripts.Count - snapshot.Data.Count) != 1) { active!.Reject("sync.not-single-insert-or-delete"); return; }
			var before = new List<IntPtr>(snapshot.Data.Count);
			var after = new List<IntPtr>(scripts.Count);
			var values = new Script[scripts.Count];
			for (int i = 0; i < snapshot.Data.Count; i++) before.Add(snapshot.Data[i].Pointer);
			for (int i = 0; i < scripts.Count; i++)
			{
				Script data = scripts[i];
				if (data == null) { active!.Reject("sync.null-script"); return; }
				values[i] = data;
				after.Add(data.Pointer);
			}
			if (!DialogueRowReusePlan.TryCreate(before, after, out DialogueRowReusePlan plan)) { active!.Reject("sync.single-change-plan-rejected"); return; }
			var reuse = new ScriptListItem?[scripts.Count];
			for (int i = 0; i < plan.ReuseBeforeIndices.Length; i++)
			{
				int oldIndex = plan.ReuseBeforeIndices[i];
				if (oldIndex >= 0) reuse[i] = snapshot.Rows[oldIndex];
			}
			for (int i = 0; i < snapshot.Rows.Count; i++)
			{
				ScriptListItem row = snapshot.Rows[i];
				if (row == null || !Same(row.transform.parent, snapshot.Parent) || row.transform.GetSiblingIndex() != i || row.index != i || !row.gameObject.activeInHierarchy) { active!.Reject("sync.row-parent-index-or-activity-changed"); return; }
			}
			current.Snapshot = snapshot;
			current.Reuse = reuse;
			current.NewData = values;
			active!._eligible++;
		}
		catch { active!.Reject("sync.native-access-exception"); }
	}

	private static bool BeforeDestroyChildren(Transform t, int remain)
	{
		Scope? current = _scope;
		Snapshot? snapshot = current?.Snapshot;
		if (!MayRun(_active) || current == null || !current.IsSync || current.DestroyIntercepted || current.Invalid || snapshot == null || current.Reuse == null || remain != 0 || !Same(t, snapshot.Parent)) return true;
		try
		{
			// Remove only data records deleted by AA. Detaching first ensures the
			// final native grid pass cannot see a deferred-destruction object.
			HashSet<IntPtr> retained = new();
			foreach (ScriptListItem? row in current.Reuse) if (row != null) retained.Add(row.Pointer);
			current.DestroyIntercepted = true;
			foreach (ScriptListItem row in snapshot.Rows)
			{
				if (retained.Contains(row.Pointer)) continue;
				row.gameObject.SetActive(false);
				current.PendingDestruction.Add(row.gameObject);
				row.transform.SetParent(null, false);
				NGUITools.Destroy(row.gameObject);
				current.PendingDestruction.RemoveAt(current.PendingDestruction.Count - 1);
				_active!._removed++;
			}
			return false;
		}
		catch (Exception error)
		{
			// Original DestroyChildren is safe here: no cache has been filled yet.
			current.Invalid = true;
			_active!.Fail(error);
			return true;
		}
	}

	private static bool BeforeAddChild(Transform parent, GameObject prefab, ref GameObject __result, out int __state)
	{
		__state = -1;
		Scope? current = _scope;
		Snapshot? snapshot = current?.Snapshot;
		if (!MayRun(_active) || current == null || !current.IsSync || !current.DestroyIntercepted || current.Invalid || snapshot == null || current.Reuse == null || !Same(parent, snapshot.Parent) || !Same(prefab, snapshot.Prefab)) return true;
		try
		{
			if (current.Next >= current.Reuse.Length) { current.Invalid = true; return true; }
			__state = current.Next;
			ScriptListItem? row = current.Reuse[current.Next++];
			if (row == null) { _active!._created++; return true; }
			if (!Same(row.transform.parent, parent) || !row.gameObject.activeInHierarchy) { current.Invalid = true; return true; }
			__result = row.gameObject;
			_active!._reused++;
			return false;
		}
		catch (Exception error) { current.Invalid = true; _active!.Fail(error); return true; }
	}

	private static void AfterAddChild(Transform parent, GameObject prefab, GameObject __result, int __state)
	{
		if (__state < 0) return;
		Scope? current = _scope;
		Snapshot? snapshot = current?.Snapshot;
		if (current == null || current.Invalid || snapshot == null) return;
		try
		{
			if (current.Next != __state + 1 || !Same(parent, snapshot.Parent) || !Same(prefab, snapshot.Prefab) || __result == null || !Same(__result.transform.parent, parent)) { current.Invalid = true; return; }
			// Native AddChild appends the newly created row. Move it once into its
			// insertion slot; retained siblings shift naturally, avoiding N moves.
			if (__result.transform.GetSiblingIndex() != __state) __result.transform.SetSiblingIndex(__state);
		}
		catch (Exception error) { current.Invalid = true; _active?.Fail(error); }
	}

	// Do not bypass Init or Refresh. Fix inlines Init in SyncScriptList, then
	// calls Refresh directly. Both paths must retain every host field assignment,
	// speaker-name/localization update and existing text-layout cache hook.

	private static void CompleteSync(IntPtr __state)
	{
		Scope? current = Get(__state);
		if (current == null || current.Snapshot == null) return;
		if (!current.DestroyIntercepted) { _active?.Reject("sync.destroy-hook-not-observed-or-suspended"); return; }
		if (!ValidateResult(current)) Repair(current);
		else if (_active != null) _active._commits++;
	}

	private static bool ValidateResult(Scope current)
	{
		try
		{
			Snapshot snapshot = current.Snapshot!;
			var cache = snapshot.Inspector.scriptNodeListItemsCache;
			if (current.Invalid || current.Reuse == null || current.NewData == null || current.Next != current.Reuse.Length || !Same(snapshot.Inspector.scriptNode, snapshot.Node) || !Same(snapshot.Inspector.scriptList, snapshot.Grid) || !Same(snapshot.Inspector.scriptListItemPrefab, snapshot.Prefab) || snapshot.Node.scripts == null || snapshot.Node.scripts.Pointer != snapshot.Scripts || cache == null || cache.Count != current.Reuse.Length || snapshot.Parent.childCount != current.Reuse.Length || snapshot.Node.scripts.Count != current.NewData.Length) return false;
			for (int i = 0; i < cache.Count; i++)
			{
				ScriptListItem row = cache[i];
				if (row == null || row.index != i || !Same(row.transform.parent, snapshot.Parent) || !Same(row.inspector, snapshot.Inspector) || !Same(row.scriptNode, snapshot.Node) || !row.isActiveAndEnabled || !row.gameObject.activeInHierarchy || !Same(snapshot.Node.scripts[i], current.NewData[i]) || row.transform.GetSiblingIndex() != i || (current.Reuse[i] != null && !Same(row, current.Reuse[i]))) return false;
			}
			return true;
		}
		catch { return false; }
	}

	private static void Repair(Scope current)
	{
		if (current.RecoveryAttempted) return;
		current.RecoveryAttempted = true;
		current.Invalid = true;
		DialogueRowReuse? active = _active;
		if (active != null) { active._failed = true; active._allowed = false; active.Reject("sync.postcondition-or-native-exception-recovery"); }
		// A deleted row may have been detached just before Destroy threw. It is
		// no longer visible to the native grid rebuild, so retry its disposal.
		foreach (GameObject removed in current.PendingDestruction)
		{
			try { if (removed != null) NGUITools.Destroy(removed); }
			catch (Exception error) { active?._log("dialogue row reuse detached-row cleanup failed: " + error.Message); }
		}
		current.PendingDestruction.Clear();
		try
		{
			// A new nested Sync scope deliberately cannot reuse the mutation plan.
			// AA reconstructs a coherent UI from its already committed Script list.
			current.Snapshot!.Inspector.SyncScriptList(current.Mute, current.KeepSelected, current.FirstOnTop);
			active?._log("dialogue row reuse failed postconditions; native list rebuilt and optimization disabled for this session.");
		}
		catch (Exception error) { active?._log("dialogue row reuse recovery failed: " + error); }
	}

	private static Exception? EndScope(Exception? __exception, IntPtr __state)
	{
		if (__state == IntPtr.Zero) return __exception;
		GCHandle handle = GCHandle.FromIntPtr(__state);
		try
		{
			if (handle.Target is Scope current)
			{
				if (!current.IsSync && current.Previous == null && current.Snapshot != null && !current.Snapshot.Consumed) _active?.Reject("mutation.no-sync-observed");
				// Invalid means interception encountered an error, not that recovery
				// succeeded. A later native exception must still rebuild a partial UI.
				if (__exception != null && current.IsSync && current.DestroyIntercepted) Repair(current);
				_scope = current.Previous;
			}
		}
		finally { handle.Free(); }
		return __exception;
	}

	private void Fail(Exception error)
	{
		_failed = true;
		_allowed = false;
		Reject("interception.native-access-exception");
		_log("dialogue row reuse disabled: " + error.GetType().Name + ": " + error.Message);
	}

	private static Scope? Get(IntPtr state) => state == IntPtr.Zero ? null : GCHandle.FromIntPtr(state).Target as Scope;
	private static bool Same(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? left, Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? right) => left != null && right != null && left.Pointer == right.Pointer;

	public void Dispose()
	{
		if (_disposed) return;
		_allowed = false;
		_disposed = true;
		if (_active == this) _active = null;
		if (_installed) { try { _harmony.UnpatchSelf(); } catch { } }
		_installed = false;
	}
}
