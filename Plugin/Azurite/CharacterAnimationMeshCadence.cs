using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Spine.Unity;
using Studio.Scripts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Azurite;

/// <summary>
/// Omits mesh uploads only on frames Unity will not draw. Animation Update,
/// skeleton world transforms/physics and event dispatch remain native.
/// The Spine 4.2 mesh boundary is documented in upstream SkeletonRenderer.cs
/// at commit e7dc1435fa4a0083ab431f1b28e083c14a1f5c68. AA's generated metadata
/// exposes that exact boundary; callbacks/custom physics remain protected.
/// </summary>
internal sealed class CharacterAnimationMeshCadence : IDisposable
{
	private static CharacterAnimationMeshCadence? _active;
	private readonly Harmony _harmony;
	private readonly Func<bool> _guard;
	private readonly Action<string>? _report;
	private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
	private readonly MethodInfo? _target;
	private bool _allowed;
	private bool _failed;
	private bool _disposed;
	private int _activityCacheFrame = -1;
	private IntPtr _activityCachePreview;
	private object? _activityCacheSlots;
	private CharacterAnimationActivity _activityCacheState;
	private bool _activityCacheReady;
	private readonly HashSet<IntPtr> _activityCacheRenderers = new();
	public bool IsInstalled { get; private set; }
	public long SkippedMeshes { get; private set; }
	// Exposed only to the source-linked tests so cache reuse can be verified
	// without changing the plugin's runtime contract.
	internal int ActivityCacheBuilds { get; private set; }

	public CharacterAnimationMeshCadence(Harmony harmony, Func<bool> guard, Action<string>? report = null)
	{
		_harmony = harmony;
		_guard = guard;
		_report = report;
		try
		{
			if (_active != null) throw new InvalidOperationException("another character mesh cadence owner is active");
			_target = AccessTools.DeclaredMethod(typeof(SkeletonRenderer), nameof(SkeletonRenderer.LateUpdateMesh), Type.EmptyTypes)
				?? throw new MissingMethodException("SkeletonRenderer.LateUpdateMesh");
			_harmony.Patch(_target, new HarmonyMethod(typeof(CharacterAnimationMeshCadence), nameof(BeforeMesh)));
			IsInstalled = Harmony.GetPatchInfo(_target)?.Owners.Contains(_harmony.Id) == true;
			if (!IsInstalled) throw new InvalidOperationException("mesh hook registration was not confirmed");
			_active = this;
			_report?.Invoke("character mesh cadence ready; only verified embedded preview meshes on Unity non-render frames are skipped; logical animation remains native.");
		}
		catch (Exception error)
		{
			_failed = true;
			IsInstalled = false;
			if (_active == this) _active = null;
			Unpatch();
			_report?.Invoke("character mesh cadence unavailable: " + error.GetType().Name);
		}
	}

	public void Update(bool allowed)
	{
		_allowed = allowed && IsInstalled && !_disposed && !_failed;
		ResetActivityCache();
	}
	public void Suspend()
	{
		_allowed = false;
		ResetActivityCache();
	}

	private static bool BeforeMesh(SkeletonRenderer __instance)
	{
		var active = _active;
		if (active == null) return true;
		return active.ShouldRun(__instance, OnDemandRendering.willCurrentFrameRender);
	}

	// Internal test seam exercises the same production prefix path with native
	// boundary stubs. It is not a replacement for in-host animation validation.
	internal bool ShouldRun(SkeletonRenderer renderer, bool willRender)
	{
		if (!_allowed || _failed || _disposed || Thread.CurrentThread.ManagedThreadId != _thread || willRender) return true;
		try
		{
			// These checks happen at the mesh call, not from a stale frame snapshot;
			// export acquisition/disable and fresh wheel/key input release immediately.
			if (!_guard() || Input.anyKey || Input.mouseScrollDelta.sqrMagnitude > 0f) return true;
			var inspector = ScriptNodeInspector.instance;
			if (inspector == null || !inspector.isActiveAndEnabled || inspector.loading || inspector.unloading) return true;
			var preview = inspector.preview;
			if (preview == null || !preview.isActiveAndEnabled || !preview.previewMode || preview.auto || preview.hasVoice ||
				preview.delayedAdvanceTask != null || OptionalHostActivity.CurrentEffectActive(preview) || OptionalHostActivity.CustomEffectActive(preview) ||
				AnimationActivity.CountPending(preview.currentAnims) != 0 || AnimationActivity.CountPending(preview.backgroundAnimations) != 0 ||
				AnimationActivity.CountPending(preview.currentSTs) != 0) return true;
			if (renderer == null || !renderer.isActiveAndEnabled || !renderer.valid || !CharacterActivity.MeshCallbacksAbsent(renderer)) return true;
			var slots = preview.slots;
			if (slots == null || slots.Length > 64) return true;
			EnsureActivityCache(preview, slots);
			if (_activityCacheState == CharacterAnimationActivity.Protected || !_activityCacheRenderers.Contains(renderer.Pointer)) return true;
			SkippedMeshes++;
			return false;
		}
		catch (Exception error)
		{
			_failed = true;
			_allowed = false;
			_report?.Invoke("character mesh cadence released after " + error.GetType().Name);
			return true;
		}
	}

	private void EnsureActivityCache(Test preview, object slots)
	{
		int frame = Time.frameCount;
		IntPtr previewPointer = preview.Pointer;
		if (_activityCacheReady && _activityCacheFrame == frame && _activityCachePreview == previewPointer &&
			ReferenceEquals(_activityCacheSlots, slots)) return;

		_activityCacheFrame = frame;
		_activityCachePreview = previewPointer;
		_activityCacheSlots = slots;
		_activityCacheState = CharacterActivity.Observe(preview, true);
		_activityCacheRenderers.Clear();
		ActivityCacheBuilds++;
		if (_activityCacheState == CharacterAnimationActivity.Protected) {
			_activityCacheReady = true;
			return;
		}

		// Build the renderer identity set once for this preview/frame. All mesh
		// calls in LateUpdateMesh then become a constant-time membership check.
		var nativeSlots = preview.slots;
		if (nativeSlots == null || nativeSlots.Length > 64)
		{
			_activityCacheState = CharacterAnimationActivity.Protected;
			_activityCacheReady = true;
			return;
		}
		for (int i = 0; i < nativeSlots.Length; i++)
		{
			var character = nativeSlots[i];
			var skeleton = character?.anim;
			if (character == null || !character.isActiveAndEnabled || skeleton == null || !skeleton.isActiveAndEnabled ||
				!CharacterActivity.CanInspect(skeleton)) continue;
			_activityCacheRenderers.Add(skeleton.Pointer);
		}
		_activityCacheReady = true;
	}

	private void ResetActivityCache()
	{
		_activityCacheReady = false;
		_activityCacheFrame = -1;
		_activityCachePreview = IntPtr.Zero;
		_activityCacheSlots = null;
		_activityCacheRenderers.Clear();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Suspend();
		if (_active == this) _active = null;
		Unpatch();
		IsInstalled = false;
	}

	private void Unpatch()
	{
		if (_target != null)
		{
			try { _harmony.Unpatch(_target, AccessTools.Method(typeof(CharacterAnimationMeshCadence), nameof(BeforeMesh))); }
			catch { }
		}
	}
}
