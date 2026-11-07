using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Studio.Scripts.Window.BackgroundExplorer;
using UnityEngine;

namespace Azurite;

// Native Update already applies NGUI visibility and unloading. Intercept the
// acquisition boundary only: do not recompute every item's world corners or
// change UITexture.enabled (which feeds back into native isVisible/unloading).
internal sealed class BackgroundGalleryLoadCulling : IDisposable
{
    private const string Owner = "halocue.azurite.background-gallery-load";
    private const int RequestsPerFrame = 2;
    private const int MaximumInFlight = 8;
    private static BackgroundGalleryLoadCulling? _active;
    private readonly Harmony _harmony = new(Owner);
    private sealed class PendingLoad
    {
        internal BackgroundItem Item = null!;
        internal int Generation;
        internal double Started;
    }
    private readonly Dictionary<IntPtr, PendingLoad> _pending = new();
    private readonly List<IntPtr> _completed = new(MaximumInFlight);
    private bool _installed;
    private bool _disposed;
    private bool _enabled = true;
    private bool _allowed;
    private int _frame = -1;
    private int _startedThisFrame;
    private long _skipped;
    private long _deferred;
    private double _now;
    private double _nextLog;
    private readonly Action<string> _log;

    internal BackgroundGalleryLoadCulling(Action<string> log) => _log = log;
    internal bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) ResetAdmission(); }
    }
    internal long Skipped => _skipped;

    internal bool Install()
    {
        if (_disposed || (_active != null && _active != this)) return false;
        if (_installed) return true;
        try
        {
            _active = this;
            Patch(typeof(BackgroundItem), "LoadBGAsync", nameof(BeforeLoad), nameof(AfterLoad));
            Patch(typeof(BackgroundItem), "UnloadBg", null, nameof(AfterUnload));
            Patch(typeof(BackgroundItem.__c__DisplayClass22_0), "_LoadBGAsync_b__0", nameof(BeforeCompleted), null);
            _installed = true;
            _log("background gallery admission ready; native visibility retained, at most 2 requests/frame and 8 in flight.");
            return true;
        }
        catch (Exception error)
        {
            try { _harmony.UnpatchSelf(); } catch { }
            if (_active == this) _active = null;
            _log("background gallery load culling unavailable: " + error.GetType().Name);
            return false;
        }
    }

    internal void Update(double now, bool allowed)
    {
        _now = now;
        _allowed = allowed && _installed && !_disposed;
        if (!_allowed || !Enabled) { ResetAdmission(); return; }
        BeginFrame();
        if (now < _nextLog) return;
        _nextLog = now + 5;
        if (_skipped != 0 || _deferred != 0)
        {
            _log("background gallery admission skipped redundant requests=" + _skipped +
                "; deferred=" + _deferred + "; inFlight=" + _pending.Count + ".");
            _skipped = _deferred = 0;
        }
    }

    private void Patch(Type type, string name, string? prefix, string? postfix)
    {
        MethodInfo? target = AccessTools.Method(type, name);
        if (target == null) throw new MissingMethodException(type.FullName, name);
        _harmony.Patch(target,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(BackgroundGalleryLoadCulling), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(BackgroundGalleryLoadCulling), postfix));
        if (Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) != true)
            throw new InvalidOperationException("Background gallery hook not registered: " + name);
    }

    private static bool BeforeLoad(BackgroundItem __instance, out int __state)
    {
        __state = 0;
        BackgroundGalleryLoadCulling? active = _active;
        if (active == null || !active._allowed || !active.Enabled || active._disposed || __instance == null) return true;
        try
        {
            // AA allocates its async callback before checking this flag.
            // While loaded OR loading, calling it every frame only allocates.
            if (!__instance.unloaded) { ++active._skipped; return false; }
            UITexture? texture = __instance.texture;
            if (texture == null) return true;
            if (texture.mainTexture != null || !__instance.gameObject.activeInHierarchy || !texture.isVisible)
            { ++active._skipped; return false; }
            active.BeginFrame();
            if (active._startedThisFrame >= RequestsPerFrame || active._pending.Count >= MaximumInFlight)
            {
                ++active._deferred;
                return false;
            }

            // Record before the native call: a cached thumbnail may invoke its
            // completion synchronously, so a postfix-only record would leak.
            __state = unchecked(__instance.loadGeneration + 1);
            active._pending[__instance.Pointer] = new PendingLoad
            { Item = __instance, Generation = __state, Started = active._now };
            ++active._startedThisFrame;
            return true;
        }
        catch { return true; }
    }

    private static void AfterLoad(BackgroundItem __instance, int __state)
    {
        BackgroundGalleryLoadCulling? active = _active;
        if (active == null || __instance == null) return;
        try
        {
            // Native guards or another prefix may prevent an admitted load.
            if (__instance.unloaded || __instance.loadGeneration != __state || __instance.texture?.mainTexture != null)
                active.Release(__instance.Pointer, __state);
        }
        catch { }
    }

    private static void BeforeCompleted(BackgroundItem.__c__DisplayClass22_0 __instance)
    {
        try
        {
            BackgroundItem? item = __instance?.__4__this;
            if (item != null) _active?.Release(item.Pointer, __instance!.generation);
        }
        catch { }
    }

    private static void AfterUnload(BackgroundItem __instance)
    {
        try { if (__instance != null && __instance.unloaded) _active?._pending.Remove(__instance.Pointer); }
        catch { }
    }

    private void Release(IntPtr pointer, int generation)
    {
        if (_pending.TryGetValue(pointer, out var load) && load.Generation == generation) _pending.Remove(pointer);
    }

    private void BeginFrame()
    {
        int frame = Time.frameCount;
        if (_frame == frame) return;
        _frame = frame;
        _startedThisFrame = 0;
        // Bound this inspection to the eight admitted requests, never to the
        // full gallery. Native cancellation/destruction may bypass a callback.
        _completed.Clear();
        foreach (var pair in _pending)
        {
            PendingLoad load = pair.Value;
            try
            {
                if (load.Item == null || load.Item.unloaded || load.Item.loadGeneration != load.Generation ||
                    load.Item.texture == null || load.Item.texture.mainTexture != null || _now - load.Started > 10)
                    _completed.Add(pair.Key);
            }
            catch { _completed.Add(pair.Key); }
        }
        foreach (IntPtr pointer in _completed) _pending.Remove(pointer);
        _completed.Clear();
    }

    private void ResetAdmission()
    {
        // No texture/camera/resource ownership was taken. Clearing admission
        // immediately restores the entire native path, including export.
        _pending.Clear();
        _completed.Clear();
        _frame = -1;
        _startedThisFrame = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _allowed = false;
        try { _harmony.UnpatchSelf(); } finally { ResetAdmission(); if (_active == this) _active = null; }
    }
}
