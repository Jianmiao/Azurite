using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AzureArchive.Automation;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Studio.Scripts;
using UnityEngine;
using NativeEnumerator = Il2CppSystem.Collections.IEnumerator;
using NativeCancellation = Il2CppSystem.Threading.CancellationTokenSource;

namespace Azurite;

// Resource enumerators never cross back through a managed MoveNext trampoline.
// Native UniTask owns their execution, yields, nested waits and disposal.
internal sealed class ProgressiveEditorLoading : IDisposable
{
    private const string HarmonyId = "halocue.azurite.progressive-editor-loading";
    private const string LoadingTag = "Azurite: 正在进入编辑器";
    private sealed class Job
    {
        internal ProgressiveResourceRequest Request;
        internal NativeEnumerator Enumerator; // Retain wrapper until native task consumed.
        internal UniTask Task;
    }
    private sealed class Session
    {
        internal ScriptData._CoPreloadAssets_d__74 Original;
        internal ProjectData Project;
        internal Loading Loading;
        internal StudioCommon Studio;
        internal NativeCancellation Cancellation = new();
        internal readonly List<Job> Active = new();
        internal readonly List<ScriptNodeInspector> PendingInspectors = new();
        internal readonly HashSet<IntPtr> EarlyPreviewed = new();
        internal List<ProgressiveResourceRequest> Requests;
        internal readonly double Started = Now;
        internal double LastProgress = Now, NextReport, NextNotice;
        internal int ShellFrame = -1, LastAdmissionFrame = -1, Next, Completed, ScriptCount;
        internal bool Finished, Failed, Lease, SourceDisposed, CancelRequested;
    }
    private sealed record Hook(Type Type, string Method, Type[] Parameters, string Callback, bool Prefix = false);
    private readonly Action<string> _log;
    private readonly Harmony _harmony = new(HarmonyId);
    private static ProgressiveEditorLoading _active;
    private Session _session;
    private bool _installed, _disposed, _renderingOwned;
    private static bool _entryRefreshScope, _changeRefreshScope;
    private static double Now => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    public bool Enabled { get; set; } = true;
    public bool Busy => _session != null && !_session.Finished;
    public bool IsInstalled => _installed && !_disposed;
    public bool EditorUnlocked => _session?.ShellFrame >= 0;
    public bool BlocksEditorOperations => _session != null && (_session.ShellFrame < 0 || _renderingOwned);
    public bool ResourceDependentWorkAllowed => _session == null || (_session.Finished && !_renderingOwned);
    public ProgressiveEditorLoading(Action<string> log) => _log = log;

    private static Hook[] Hooks() => new[]
    {
        new Hook(typeof(AuthoringWorkbench._Load_d__47), "MoveNext", Type.EmptyTypes, nameof(AfterWorkbenchStep)),
        new Hook(typeof(StudioCommon), "Start", Type.EmptyTypes, nameof(AfterStudioStart)),
        new Hook(typeof(StudioCommon), "Start", Type.EmptyTypes, nameof(BeforeStudioStart), true),
        new Hook(typeof(StudioCommon), "OnChangesMade", new[] { typeof(bool) }, nameof(BeforeChangesMade), true),
        new Hook(typeof(StudioCommon), "OnChangesMade", new[] { typeof(bool) }, nameof(AfterChangesMade)),
        new Hook(typeof(AuthoringEditorSession), "Refresh", new[] { typeof(bool) }, nameof(BeforeAuthoringRefresh), true),
        new Hook(typeof(AuthoringEditorSession), "Begin", new[] { typeof(StudioCommon), typeof(ProjectData), typeof(string) }, nameof(BeforeSessionBegin), true),
        new Hook(typeof(AuthoringEditorSession), "Begin", new[] { typeof(StudioCommon), typeof(ProjectData), typeof(string) }, nameof(AfterSessionBegin)),
        new Hook(typeof(StudioCommon), "OnDestroy", Type.EmptyTypes, nameof(AfterStudioDestroy)),
        new Hook(typeof(ScriptNodeInspector), "PlayPreview", Type.EmptyTypes, nameof(BeforePreview), true),
        new Hook(typeof(ScriptNodeInspector), "SyncSlots", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "SyncOrClearSlotProperties", new[] { typeof(bool) }, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "SyncEnvironmentProperties", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "SetBGM", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "OpenBackgroundSelector", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "OpenPopupImageSelector", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "OpenBGMSelector", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
        new Hook(typeof(ScriptNodeInspector), "OpenSoundSelector", Type.EmptyTypes, nameof(BeforeResourceOperation), true),
    };
    public bool Install()
    {
        if (_disposed || (_active != null && _active != this)) return false;
        if (_installed) return true;
        try
        {
            var hooks = Hooks(); var targets = new MethodInfo[hooks.Length];
            for (int n = 0; n < hooks.Length; n++)
            {
                var h = hooks[n];
                targets[n] = AccessTools.Method(h.Type, h.Method, h.Parameters)
                    ?? throw new MissingMethodException(h.Type.FullName, h.Method);
            }
            _active = this;
            for (int n = 0; n < hooks.Length; n++)
            {
                var patch = new HarmonyMethod(typeof(ProgressiveEditorLoading), hooks[n].Callback);
                _harmony.Patch(targets[n], prefix: hooks[n].Prefix ? patch : null, postfix: hooks[n].Prefix ? null : patch);
                if (Harmony.GetPatchInfo(targets[n])?.Owners.Contains(HarmonyId) != true)
                    throw new InvalidOperationException("Hook not registered: " + hooks[n].Method);
            }
            _installed = true;
            _log("progressive editor hooks ready build=chooser-multiplier-cap-12-1; deferred catalog refresh during entry/change; character/emotion model edits remain live; environment resource entry remains guarded; native resource tasks; one admission per frame, at most four native tasks.");
            return true;
        }
        catch (Exception error)
        {
            _harmony.UnpatchSelf(); if (_active == this) _active = null;
            _log("progressive editor loading unavailable; native load order retained: " + error);
            return false;
        }
    }
    private static void AfterWorkbenchStep(AuthoringWorkbench._Load_d__47 __instance, bool __result)
    {
        var a = _active;
        if (a == null || !a.Enabled || !a.IsInstalled || a._renderingOwned || a._session != null || !__result || __instance.__1__state != 1) return;
        var child = __instance.__2__current;
        Session s = null;
        try
        {
            var preload = child?.TryCast<ScriptData._CoPreloadAssets_d__74>();
            if (preload == null || __instance.project == null) { a._log("progressive-load entry declined: unexpected native yield."); return; }
            var loading = Singleton<Loading>.Instance;
            if (loading == null) return;
            var requests = SnapshotResources(__instance.project, out int count);
            s = new Session { Original = preload, Project = __instance.project, Loading = loading, Requests = requests, ScriptCount = count };
            loading.StartLoading(LoadingTag); s.Lease = true;
            a._session = s;
            __instance.__2__current = null;
            a._log($"progressive-load phase=deferred scripts={count} uniqueResources={requests.Count}; native queue will start after editor entry.");
        }
        catch (Exception error)
        {
            __instance.__2__current = child;
            if (s != null) { a.ReleaseLease(s); s.Cancellation.Dispose(); }
            a._session = null;
            a._log("progressive-load defer declined; native wait retained: " + error);
        }
    }
    // Index native lists; never drive a native enumerable through managed MoveNext.
    private static List<ProgressiveResourceRequest> SnapshotResources(ProjectData project, out int scripts)
    {
        var result = new List<ProgressiveResourceRequest>();
        var seen = new HashSet<ProgressiveResourceRequest>();
        void Add(ProgressiveResourceKind kind, long id, string key)
        {
            var request = new ProgressiveResourceRequest(kind, id, key ?? "");
            if (seen.Add(request)) result.Add(request);
        }
        void Text(ProgressiveResourceKind kind, string key) { if (!string.IsNullOrEmpty(key)) Add(kind, 0, key); }
        scripts = 0;
        var nodes = project.nodes ?? throw new InvalidOperationException("Missing project nodes");
        // The editor opens at the tail. Queue later script resources first so
        // the first preview can obtain the selected dialogue's background and
        // characters before the rest of the project is streamed.
        for (int n = nodes.Count - 1; n >= 0; n--)
        {
            var node = nodes[n]?.TryCast<ScriptNodeData>();
            if (node == null) continue;
            var list = node.Scripts ?? throw new InvalidOperationException("Missing node scripts");
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (++scripts > 100000) throw new InvalidOperationException("Unsupported project size");
                var data = list[i] ?? throw new InvalidOperationException("Null script");
                if (data.bgName != 0) Add(ProgressiveResourceKind.Background, data.bgName, "");
                Text(ProgressiveResourceKind.Voice, data.voice);
                Text(ProgressiveResourceKind.Popup, data.popup);
                // AA treats both 0 and 999 as no music, not asset identifiers.
                if (data.bgmId != 0 && data.bgmId != 999) Add(ProgressiveResourceKind.Bgm, data.bgmId, "");
                Text(ProgressiveResourceKind.Sound, data.sound);
                var characters = data.characters;
                if (characters != null)
                    for (int c = 0; c < characters.Count; c++)
                    {
                        var character = characters[c] ?? throw new InvalidOperationException("Null character");
                        Text(ProgressiveResourceKind.Character, character.name);
                    }
            }
        }
        return result;
    }
    private static void AfterStudioStart(StudioCommon __instance)
    {
        var a = _active; var s = a?._session;
        try
        {
            if (s == null || s.ShellFrame >= 0 || s.Finished) return;
            s.Studio = __instance; s.ShellFrame = Time.frameCount;
            a.ReleaseLease(s);
            DisposeSource(s); // Exact known type, never advanced and never handed to another scheduler.
            a._log($"progressive-load phase=editor-entered elapsed={Now - s.Started:F1}ms; nativeTasksStarted=0; uniqueResources={s.Requests.Count}; {OverlayState(s)}.");
            a.Notice(s, "Azurite：可以编辑对白，资源正在后台加载…");
        }
        finally { _entryRefreshScope = false; }
    }
    private static void BeforeStudioStart(StudioCommon __instance)
    {
        var s = _active?._session;
        _entryRefreshScope = s != null && !s.Finished && !s.Failed;
    }
    private static void BeforeSessionBegin(StudioCommon __0, ProjectData __1, string __2) => _entryRefreshScope = _active?._session is { Finished: false, Failed: false };
    private static void AfterSessionBegin() { }
    private static void BeforeChangesMade(StudioCommon __instance, bool __0)
    {
        var s = _active?._session;
        _changeRefreshScope = s != null && !s.Finished && !s.Failed && s.ShellFrame >= 0;
    }
    private static void AfterChangesMade() => _changeRefreshScope = false;
    private static void BeforeAuthoringRefresh(AuthoringEditorSession __instance, ref bool __0)
    {
        var s = _active?._session;
        if (s == null || s.Finished || s.Failed || !(_entryRefreshScope || _changeRefreshScope)) return;
        if (__0)
        {
            __0 = false;
            aLog("progressive-load catalog refresh deferred to preserve immediate editor entry");
        }
    }
    private static void aLog(string message) => _active?._log(message);
    private static void AfterStudioDestroy(StudioCommon __instance)
    {
        _entryRefreshScope = false;
        _changeRefreshScope = false;
        var a = _active; var s = a?._session;
        if (s?.Studio != null && s.Studio.Pointer == __instance.Pointer) a.Cancel("editor closed");
    }
    private static bool BeforeResourceOperation(ScriptNodeInspector __instance)
    {
        var a = _active; var s = a?._session;
        if (s == null || s.Finished || __instance == null) return true;
        if (!s.PendingInspectors.Exists(i => i != null && i.Pointer == __instance.Pointer)) s.PendingInspectors.Add(__instance);
        a.Notice(s, s.Failed ? "Azurite：资源加载失败，文字仍可编辑；请重新打开工程恢复预览。" : "Azurite：资源加载中，素材设置稍后可用；可以继续编辑对白。");
        return false;
    }
    private static bool BeforePreview(ScriptNodeInspector __instance)
    {
        var a = _active; var s = a?._session;
        if (s == null || s.Finished || __instance == null) return true;
        if (!s.PendingInspectors.Exists(i => i != null && i.Pointer == __instance.Pointer)) s.PendingInspectors.Add(__instance);
        // AA's native preview path already handles missing assets. Keep it
        // blocked only until the editor shell exists or export owns rendering;
        // once the shell is interactive, let the selected script render while
        // the independent resource queue continues in the background.
        // Native Load and the startup-selection repair both invoke PlayPreview
        // while inspector.loading is true. PlayPreview synchronously advances
        // the scenario and can traverse many dialogue rows; defer it until
        // Update sees a yielded, resource-ready inspector instead of blocking
        // the Unity message pump inside Start/Load.
        if (s.ShellFrame < 0 || a._renderingOwned || __instance.loading || __instance.preview == null || !__instance.preview.isActiveAndEnabled) return false;
        return true;
    }
    private void Notice(Session s, string message)
    {
        if (Now < s.NextNotice) return;
        s.NextNotice = Now + 3000;
        try { Singleton<NotificationManager>.Instance?.Notify(message); } catch { }
    }
    public void Update(MonoBehaviour coroutineHost, bool renderingOwned)
    {
        _renderingOwned = renderingOwned;
        var s = _session;
        if (!_installed || _disposed || s == null) return;
        if (s.ShellFrame >= 0 && s.Lease) ReleaseLease(s);
        if (s.Failed) return;
        if (s.ShellFrame >= 0 && !s.Finished && !renderingOwned)
        {
            foreach (var i in s.PendingInspectors)
                if (i != null && i.isActiveAndEnabled && !i.loading && !i.unloading && i.selectedScriptItem != null && i.preview != null && i.preview.isActiveAndEnabled && s.EarlyPreviewed.Add(i.Pointer))
                {
                    try { i.PlayPreview(); }
                    catch (Exception e) { _log("progressive-load early preview failed: " + e.GetType().Name); }
                }
        }
        if (s.Finished)
        {
            if (renderingOwned) return;
            _log("progressive-load catalog observation deferred until explicit save/context; editor remains responsive after resources-ready.");
            _session = null;
            s.Cancellation.Dispose();
            foreach (var i in s.PendingInspectors)
                if (i != null && i.isActiveAndEnabled && !i.loading && !i.unloading && i.selectedScriptItem != null)
                {
                    try { i.SyncSlots(); i.SyncOrClearSlotProperties(true); i.SyncEnvironmentProperties(); i.PlayPreview(); }
                    catch (Exception e) { _log("progressive-load preview refresh failed: " + e.GetType().Name); }
                }
            s.PendingInspectors.Clear(); return;
        }
        try
        {
            // Poll only native status; the Unity PlayerLoop executes every MoveNext.
            for (int n = s.Active.Count - 1; n >= 0; n--)
            {
                Job job = s.Active[n];
                if (job.Task.Status == UniTaskStatus.Pending) continue;
                s.Active.RemoveAt(n); // Consume each pooled task exactly once, including failure.
                job.Task.GetAwaiter().GetResult();
                s.Completed++; s.LastProgress = Now;
            }
            if (s.ShellFrame >= 0 && !renderingOwned && Time.frameCount - s.ShellFrame >= 2 &&
                s.LastAdmissionFrame != Time.frameCount && s.Active.Count < 4 && s.Next < s.Requests.Count)
            {
                s.LastAdmissionFrame = Time.frameCount;
                var request = s.Requests[s.Next];
                s.Next++;
                var native = CreateNativeResource(request);
                // AA itself uses this native UniTask bridge. No managed enumerator
                // is returned to native, and no resource factory is Harmony-patched.
                var task = EnumeratorAsyncExtensions.ToUniTask(native, PlayerLoopTiming.Update, s.Cancellation.Token);
                s.Active.Add(new Job { Request = request, Enumerator = native, Task = task });
                s.LastProgress = Now;
            }
            if (s.ShellFrame >= 0 && s.Next == s.Requests.Count && s.Active.Count == 0)
            {
                s.Finished = true; ReleaseLease(s);
                _log($"progressive-load phase=resources-ready elapsed={Now - s.Started:F1}ms completed={s.Completed}/{s.Requests.Count}; native-task results consumed; {OverlayState(s)}.");
                return;
            }
            if (Now >= s.NextReport)
            {
                s.NextReport = Now + 2000;
                _log($"progressive-load phase=resource-loading completed={s.Completed}/{s.Requests.Count} submitted={s.Next} active={s.Active.Count} pausedAdmissions={renderingOwned}; editorEntered={s.ShellFrame >= 0}; {OverlayState(s)}.");
            }
            if (s.ShellFrame < 0 && Now - s.Started > 120000) throw new TimeoutException("Editor scene did not start");
            if (s.ShellFrame >= 0 && !renderingOwned && s.Active.Count > 0 && Now - s.LastProgress > 120000)
                throw new TimeoutException("No native resource completed for 120 seconds; pending kind=" + s.Active[0].Request.Kind);
        }
        catch (Exception e) { Fail(s, e); }
    }
    private static NativeEnumerator CreateNativeResource(ProgressiveResourceRequest request)
    {
        NativeEnumerator result;
        if (request.Kind == ProgressiveResourceKind.Character)
        {
            var characters = CharacterManager.Instance ?? throw new InvalidOperationException("Character manager unavailable");
            result = characters.CoTryPreloadSpine(request.Key);
        }
        else
        {
            var resources = ScenarioResourceManager.Instance ?? throw new InvalidOperationException("Resource manager unavailable");
            result = request.Kind switch
            {
                ProgressiveResourceKind.Background => resources.CoTryPreloadBackgroundTexture(checked((uint)request.Id)),
                ProgressiveResourceKind.Voice => resources.CoTryPreloadVoice(request.Key),
                ProgressiveResourceKind.Popup => resources.CoTryPreloadPopupImage(request.Key),
                ProgressiveResourceKind.Bgm => resources.CoTryPreloadBGMClip(request.Id),
                ProgressiveResourceKind.Sound => resources.CoTryPreloadSound(request.Key),
                _ => throw new InvalidOperationException("Unknown resource kind")
            };
        }
        return result ?? throw new InvalidOperationException("Native resource factory returned null");
    }
    public void SuspendForExport() => _renderingOwned = true;
    public void ResumeAfterExport()
    {
        _renderingOwned = false;
        if (_session != null) _session.LastProgress = Now;
    }
    private void Fail(Session s, Exception error)
    {
        if (s.Failed) return;
        s.Failed = true;
        CancelNativeTasks(s); DisposeSource(s); ReleaseLease(s);
        _log("progressive-load phase=failed; resource guards retained, text is not globally locked: " + error);
        Notice(s, "Azurite：资源加载失败，文字仍可编辑；请重新打开工程恢复预览。");
    }
    private void Cancel(string reason)
    {
        var s = _session;
        if (s == null) return;
        CancelNativeTasks(s); DisposeSource(s); ReleaseLease(s);
        s.PendingInspectors.Clear(); _session = null;
        _log("progressive-load phase=cancelled; reason=" + reason);
    }
    private static void CancelNativeTasks(Session s)
    {
        if (s.CancelRequested) return;
        s.CancelRequested = true;
        s.Cancellation.Cancel();
        // Native Forget owns result consumption after cancellation. Never manually
        // dispose native resource iterators while the native scheduler owns them.
        foreach (var job in s.Active) UniTaskExtensions.Forget(job.Task);
        s.Active.Clear();
        s.Cancellation.Dispose();
    }
    private static void DisposeSource(Session s)
    {
        if (s.SourceDisposed) return;
        s.SourceDisposed = true;
        s.Original.System_IDisposable_Dispose();
    }
    private void ReleaseLease(Session s)
    {
        if (!s.Lease) return;
        var tags = Loading.LoadingTags;
        if (tags == null) return;
        // LoadingTags spans scenes, but StopLoading hides only its receiver.
        // The pre-scene receiver may already be Unity-null or still alive on a
        // previous canvas. Always resolve the current scene instance afresh.
        var current = Singleton<Loading>.Instance;
        var receiver = current != null ? current : s.Loading;
        if (tags.Contains(LoadingTag))
        {
            if (receiver != null) receiver.StopLoading(LoadingTag);
            else tags.Remove(LoadingTag); // Our exact tag only; next Awake sees the updated set.
        }
        if (tags.Contains(LoadingTag)) return;
        s.Lease = false;
        // A surviving old canvas can still be visible after the shared tag was
        // removed on the new one. Reset only with no outstanding owner at all.
        ReconcileReleasedOverlay(current);
        if (s.Loading != null && (current == null || s.Loading.Pointer != current.Pointer))
            ReconcileReleasedOverlay(s.Loading);
        _log("progressive-load phase=entry-lease-released; " + OverlayState(s));
    }
    private static void ReconcileReleasedOverlay(Loading loading)
    {
        if (loading != null && Loading.LoadingTags != null && Loading.LoadingTags.Count == 0 && loading.gameObject.activeSelf)
            loading.ResetLoading();
    }
    private static string OverlayState(Session s)
    {
        var current = Singleton<Loading>.Instance;
        var tags = Loading.LoadingTags;
        return $"overlayPresent={current != null} overlayActive={(current != null && current.gameObject.activeInHierarchy)} " +
            $"loadingTags={tags?.Count ?? -1} ownTag={(tags != null && tags.Contains(LoadingTag))} " +
            $"sceneInstanceChanged={(current != null && (s.Loading == null || current.Pointer != s.Loading.Pointer))}";
    }
    public void Dispose()
    {
        if (_disposed) return;
        Cancel("plugin shutdown"); _disposed = true; _installed = false;
        _harmony.UnpatchSelf();
        if (_active == this) _active = null;
    }
}

