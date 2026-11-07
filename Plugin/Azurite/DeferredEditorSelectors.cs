using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using UnityEngine;
using BackgroundSelector = Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer;
using PopupSelector = Studio.Scripts.Window.PopupImageExplorer.PopupImageExplorer;
using CharacterSelector = Studio.Scripts.Window.CharacterExplorer.CharacterExplorer;
using CharacterList = Studio.Scripts.Window.CharacterExplorer.CharacterList;

namespace Azurite;

// On the verified host both selector Start methods build every item, then hide
// their own GameObject. Native inspector entry calls Init before Show. Retain
// that exact Start body, but pay for an unused gallery only on first demand.
// This does not make first-open construction incremental or load assets off-thread.
internal sealed class DeferredEditorSelectors : IDisposable
{
    private const string Owner = "halocue.azurite.deferred-editor-selectors";
    private const string EditorScene = "ScenarioEditorScene";
    private sealed class Entry
    {
        internal MonoBehaviour View = null!;
        internal bool Background, Ready, Running;
        internal int Scene;
    }
    private sealed class CharacterEntry
    {
        internal CharacterSelector View = null!;
        internal CharacterList List = null!;
        internal bool Ready, Running, Awaking;
        internal int Scene;
    }
    private readonly struct AwakeScope
    {
        internal readonly IntPtr List;
        internal readonly long Started;
        internal AwakeScope(IntPtr list) { List = list; Started = Stopwatch.GetTimestamp(); }
    }
    private readonly Harmony _harmony = new(Owner);
    private readonly Action<string> _log;
    private readonly Dictionary<IntPtr, Entry> _entries = new();
    private readonly Dictionary<IntPtr, CharacterEntry> _characters = new();
    private static DeferredEditorSelectors? _active;
    private bool _installed, _disposed, _quitting;
    public bool Enabled { get; set; } = true;
    internal int PendingCount
    {
        get { int n = 0; foreach (var e in _entries.Values) if (!e.Ready) n++; foreach (var e in _characters.Values) if (!e.Ready) n++; return n; }
    }
    public DeferredEditorSelectors(Action<string> log) => _log = log;
    public bool Install()
    {
        if (_disposed || (_active != null && _active != this)) return false;
        if (_installed) return true;
        try
        {
            var targets = new[]
            {
                Find(typeof(BackgroundSelector), "Start", Type.EmptyTypes),
                Find(typeof(PopupSelector), "Start", Type.EmptyTypes),
                Find(typeof(BackgroundSelector), "Init", new[] { typeof(Script), typeof(Il2CppSystem.Action<uint, string>) }),
                Find(typeof(PopupSelector), "Init", new[] { typeof(ScriptNode), typeof(Script) }),
                Find(typeof(BackgroundSelector), "Show", Type.EmptyTypes),
                Find(typeof(StudioCommon), "OnDestroy", Type.EmptyTypes),
                Find(typeof(CharacterSelector), "Awake", Type.EmptyTypes),
                Find(typeof(CharacterList), "LoadCharacters", Type.EmptyTypes),
                Find(typeof(CharacterSelector), "Init", new[] { typeof(Script), typeof(int), typeof(bool) }),
                Find(typeof(ScriptNodeInspector), "OpenCharacterSelector", new[] { typeof(int) })
            };
            _active = this;
            Patch(targets[0], nameof(BeforeBackgroundStart), nameof(AfterBackgroundStart));
            Patch(targets[1], nameof(BeforePopupStart), nameof(AfterPopupStart));
            Patch(targets[2], nameof(BeforeBackgroundDemand));
            Patch(targets[3], nameof(BeforePopupDemand));
            Patch(targets[4], nameof(BeforeBackgroundDemand));
            Patch(targets[5], null, nameof(AfterEditorDestroy));
            Patch(targets[6], nameof(BeforeCharacterAwake), finalizer: nameof(FinalizeCharacterAwake));
            Patch(targets[7], nameof(BeforeCharacterLoad), nameof(AfterCharacterLoad));
            Patch(targets[8], nameof(BeforeCharacterDemand));
            Patch(targets[9], nameof(BeforeInspectorCharacterDemand));
            _installed = true;
            _log("editor selector deferral ready; background, popup and character lists keep native first-open initialization; character Awake/Start lifecycle remains native; no background flush.");
            return true;
        }
        catch (Exception error)
        {
            _harmony.UnpatchSelf(); if (_active == this) _active = null;
            _log("editor selector deferral unavailable; native startup retained: " + error.GetType().Name);
            return false;
        }
    }
    private static MethodInfo Find(Type type, string name, Type[] parameters) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);
    private void Patch(MethodInfo method, string? prefix, string? postfix = null, string? finalizer = null)
    {
        _harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(DeferredEditorSelectors), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(DeferredEditorSelectors), postfix),
            finalizer: finalizer == null ? null : new HarmonyMethod(typeof(DeferredEditorSelectors), finalizer));
        if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) != true)
            throw new InvalidOperationException("Selector hook not registered: " + method.Name);
    }
    private static bool BeforeBackgroundStart(BackgroundSelector __instance) => _active?.BeforeStart(__instance, true) ?? true;
    private static bool BeforePopupStart(PopupSelector __instance) => _active?.BeforeStart(__instance, false) ?? true;
    private static void AfterBackgroundStart(BackgroundSelector __instance) => _active?.AfterStart(__instance, true);
    private static void AfterPopupStart(PopupSelector __instance) => _active?.AfterStart(__instance, false);
    private static bool BeforeBackgroundDemand(BackgroundSelector __instance) => _active?.BeforeDemand(__instance, true) ?? true;
    private static bool BeforePopupDemand(PopupSelector __instance) => _active?.BeforeDemand(__instance, false) ?? true;

    // The verified native Awake registers/hides the selector, then calls this
    // exact instance's LoadCharacters. Defer only that final list construction;
    // imports and alias edits call LoadCharacters outside Awake and stay native.
    private static void BeforeCharacterAwake(CharacterSelector __instance, out AwakeScope __state)
    {
        __state = default;
        var a = _active;
        if (a == null || a._disposed || a._quitting || !a.Enabled || !InEditor(__instance) || __instance.characterList == null) return;
        var list = __instance.characterList;
        if (!a._characters.TryGetValue(list.Pointer, out var entry))
        {
            entry = new CharacterEntry { View = __instance, List = list, Scene = __instance.gameObject.scene.handle };
            a._characters.Add(list.Pointer, entry);
        }
        entry.Awaking = true;
        __state = new AwakeScope(list.Pointer);
    }
    private static Exception? FinalizeCharacterAwake(AwakeScope __state, Exception? __exception)
    {
        var a = _active;
        if (a != null && __state.List != IntPtr.Zero && a._characters.TryGetValue(__state.List, out var entry))
        {
            entry.Awaking = false;
            a._log($"editor character Awake completed; listDeferred={!entry.Ready}; elapsed={(Stopwatch.GetTimestamp() - __state.Started) * 1000.0 / Stopwatch.Frequency:F2}ms; fault={__exception != null}.");
        }
        return __exception;
    }
    private static bool BeforeCharacterLoad(CharacterList __instance, out bool __state)
    {
        __state = true;
        var a = _active;
        if (a == null || a._disposed || __instance == null || !a._characters.TryGetValue(__instance.Pointer, out var entry)) return true;
        if (entry.Awaking && !entry.Running)
        {
            __state = false;
            return false;
        }
        return true;
    }
    private static void AfterCharacterLoad(CharacterList __instance, bool __state)
    {
        var a = _active;
        if (__state && a != null && !a._disposed && __instance != null && a._characters.TryGetValue(__instance.Pointer, out var entry))
            entry.Ready = true;
    }
    private static bool BeforeCharacterDemand(CharacterSelector __instance) => _active?.EnsureCharacterReady(__instance) ?? true;
    private static bool BeforeInspectorCharacterDemand(ScriptNodeInspector __instance) =>
        InEditor(__instance) ? _active?.EnsureCharacterReady(Singleton<CharacterSelector>.Instance) ?? true : true;
    private bool EnsureCharacterReady(CharacterSelector? view)
    {
        if (_quitting) return false;
        if (_disposed || view == null || view.characterList == null || !_characters.TryGetValue(view.characterList.Pointer, out var entry)) return true;
        if (entry.Ready) return true;
        if (entry.Running || entry.View == null || entry.List == null) return false;
        entry.Running = true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            entry.List.LoadCharacters();
            entry.Ready = true;
            _log($"editor character list initialized on demand; elapsed={(Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency:F2}ms.");
            return true;
        }
        catch (Exception error)
        {
            entry.Ready = false;
            _log("editor character initialization failed; selector entry suppressed and retry remains available: " + error.GetType().Name);
            return false;
        }
        finally { entry.Running = false; }
    }

    private static bool InEditor(MonoBehaviour view) => view != null && view.gameObject.scene.name == EditorScene;
    private Entry Create(MonoBehaviour view, bool background)
    {
        var entry = new Entry { View = view, Background = background, Scene = view.gameObject.scene.handle };
        _entries[view.Pointer] = entry;
        return entry;
    }
    private bool BeforeStart(MonoBehaviour view, bool background)
    {
        if (_quitting) return false;
        if (_disposed || view == null) return true;
        if (_entries.TryGetValue(view.Pointer, out var existing))
            return existing.Running; // Pending and already-run Start are suppressed.
        if (!Enabled || !InEditor(view)) return true;
        Create(view, background);
        view.gameObject.SetActive(false); // Same terminal visibility as native Start.
        _log("editor gallery deferred: " + (background ? "background" : "popup image"));
        return false;
    }
    private void AfterStart(MonoBehaviour view, bool background)
    {
        if (_disposed || _quitting || view == null || !InEditor(view)) return;
        if (_entries.TryGetValue(view.Pointer, out var entry))
        {
            if (entry.Running) entry.Ready = true;
        }
        else Create(view, background).Ready = true; // Native Start while feature disabled.
    }
    private bool BeforeDemand(MonoBehaviour view, bool background)
    {
        if (_quitting) return false;
        if (_disposed || view == null) return true;
        if (!_entries.TryGetValue(view.Pointer, out var entry))
        {
            if (!Enabled || !InEditor(view)) return true;
            entry = Create(view, background); // A programmatic Init before Unity Start.
        }
        return EnsureReady(entry);
    }
    private bool EnsureReady(Entry entry)
    {
        if (entry.Ready) return true;
        if (entry.Running || entry.View == null) return false;
        entry.Running = true;
        try
        {
            if (entry.Background) ((BackgroundSelector)entry.View).Start();
            else ((PopupSelector)entry.View).Start();
            entry.Ready = true;
            _log("editor gallery native initialization completed on demand: " + (entry.Background ? "background" : "popup image"));
            return true;
        }
        catch (Exception error)
        {
            // Keep pending and suppress this Init/Show. A later explicit demand
            // can retry; never publish partially constructed gallery state.
            entry.Ready = false;
            _log("editor gallery initialization failed; demand suppressed and retry remains available: " + error.GetType().Name);
            return false;
        }
        finally { entry.Running = false; }
    }
    private static void AfterEditorDestroy(StudioCommon __instance)
    {
        var a = _active;
        if (a == null || a._disposed || a._quitting || __instance == null) return;
        int scene = __instance.gameObject.scene.handle;
        var remove = new List<IntPtr>();
        foreach (var pair in a._entries)
            if (pair.Value.View == null || pair.Value.Scene == scene) remove.Add(pair.Key);
        foreach (var key in remove) a._entries.Remove(key);
        remove.Clear();
        foreach (var pair in a._characters)
            if (pair.Value.View == null || pair.Value.List == null || pair.Value.Scene == scene) remove.Add(pair.Key);
        foreach (var key in remove) a._characters.Remove(key);
    }
    public void AbandonForShutdown()
    {
        _quitting = true;
        _entries.Clear(); // Do not access native objects during terminal teardown.
        _characters.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        // Live plugin unload returns valid original gallery state before our
        // guards disappear. Application/scene close instead abandons pending UI.
        if (!_quitting)
        {
            foreach (var entry in new List<Entry>(_entries.Values))
                if (entry.View != null) EnsureReady(entry);
            foreach (var entry in new List<CharacterEntry>(_characters.Values))
                if (entry.View != null) EnsureCharacterReady(entry.View);
        }
        _disposed = true; _installed = false; _entries.Clear(); _characters.Clear();
        _harmony.UnpatchSelf(); if (_active == this) _active = null;
    }
}
