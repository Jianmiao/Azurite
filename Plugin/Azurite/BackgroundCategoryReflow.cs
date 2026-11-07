using System;
using System.Reflection;
using HarmonyLib;
using Studio.Scripts.Window.BackgroundExplorer;
using UnityEngine;

namespace Azurite;

// UIGrid/UITable may receive many clip updates during a fast wheel gesture.
// Reposition once after motion settles so labels do not overlap while avoiding
// a full table layout on every intermediate scroll event.
internal sealed class BackgroundCategoryReflow : IDisposable
{
    private const string Owner = "halocue.azurite.background-category-reflow";
    private static BackgroundCategoryReflow? _active;
    private readonly Harmony _harmony = new(Owner);
    private bool _installed;
    private bool _disposed;
    private bool _pending;
    private readonly Action<string> _log;

    internal BackgroundCategoryReflow(Action<string> log) => _log = log;

    internal bool Install()
    {
        if (_disposed || (_active != null && _active != this)) return false;
        if (_installed) return true;
        try
        {
            MethodInfo scrollLate = AccessTools.Method(typeof(UIScrollView), "LateUpdate", Type.EmptyTypes) ?? throw new MissingMethodException(typeof(UIScrollView).FullName, "LateUpdate");
            MethodInfo show = AccessTools.Method(typeof(BackgroundExplorer), "Show", Type.EmptyTypes) ?? throw new MissingMethodException(typeof(BackgroundExplorer).FullName, "Show");
            _active = this;
            _harmony.Patch(scrollLate, postfix: new HarmonyMethod(typeof(BackgroundCategoryReflow), nameof(AfterScrollLateUpdate)));
            _harmony.Patch(show, postfix: new HarmonyMethod(typeof(BackgroundCategoryReflow), nameof(AfterGalleryShow)));
            _installed = true;
            _log("background category reflow ready; settled scrolls receive one native UITable reposition.");
            return true;
        }
        catch (Exception error)
        {
            try { _harmony.UnpatchSelf(); } catch { }
            if (_active == this) _active = null;
            _log("background category reflow unavailable: " + error.GetType().Name);
            return false;
        }
    }

    private static void AfterGalleryShow()
    {
        if (_active != null) _active._pending = true;
    }

    private static void AfterScrollLateUpdate(UIScrollView __instance)
    {
        BackgroundCategoryReflow? active = _active;
        if (active == null || active._disposed || __instance == null) return;
        try
        {
            BackgroundExplorer? explorer = Singleton<BackgroundExplorer>.Instance;
            UITable? table = explorer?.hierarchy?.rootTable;
            UIScrollView? owner = table?.GetComponentInParent<UIScrollView>();
            if (table == null || owner == null || owner.Pointer != __instance.Pointer) return;
            bool moving = __instance.isDragging || Math.Abs(__instance.mScroll) > 0.0001f || __instance.currentMomentum.sqrMagnitude > 0.000001f;
            if (moving) { active._pending = true; return; }
            if (!active._pending) return;
            table.Reposition();
            active._pending = false;
        }
        catch (Exception error)
        {
            active._pending = false;
            active._log("background category reflow skipped: " + error.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _harmony.UnpatchSelf(); } finally { if (_active == this) _active = null; }
    }
}
