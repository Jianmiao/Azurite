using System.Reflection;
using Azurite;
using AzureArchive.Automation;
using HarmonyLib;
using Studio.Scripts;
using UnityEngine;

string hostPath = args.FirstOrDefault() ?? @"F:\AzureArchive_100_fix\BepInEx\interop\Assembly-CSharp.dll";
using var metadata = new HostMetadata(hostPath);
Harmony.Metadata = metadata;
var cases = new (string, Action)[]
{
    ("native no-BGM sentinel is not submitted as an asset", NativeBatchCases.NoBgmSentinelIsSkipped),
    ("actual production Install resolves installed host signatures", InstallResolves),
    ("project entry patches Workbench and leaves CoLoadSave unchanged", WorkbenchOnly),
    ("state one detaches preload without eagerly executing it", DefersWithoutExecution),
    ("unrecognized yield and wrong state remain native", RejectsWrongYield),
    ("editor structure is unlocked while resource work remains pending", StructureBeforeResources),
    ("initial catalog capture is deferred while editor entry is admitted", CatalogCaptureDeferred),
    ("first editor change does not repeat cold catalog capture", ChangeCaptureDeferred),
    ("authoritative catalog observation runs once after resources complete", CatalogObservationRestored),
    ("scene replacement removes current overlay before assets complete", SceneOverlayReleased),
    ("destroyed previous scene does not leak the private loading tag", DestroyedOverlayReleased),
    ("scene replacement preserves a foreign loading operation", ForeignSceneOverlayPreserved),
    ("temporarily absent scene overlay cannot strand the private tag", MissingOverlayRetries),
    ("pending resources allow simulated node entry, text edits and dialogue mutations", EditorActionCases.PendingResourcesAllowEditorActions),
    ("foreign native overlay remains an input barrier during progressive loading", EditorActionCases.ForeignOverlayStillBlocksUserActions),
    ("native preview waits and resumes once after resource completion", PreviewResumes),
    ("preview outside deferred project loading follows native behavior", PreviewWithoutPending),
    ("preview raised during scene initialization also waits for resources", PreviewBeforeStudioStart),
    ("startup loading preview is deferred out of the native Start call", PreviewDuringInspectorLoading),
    ("character and emotion operations remain usable while environment resources stay guarded", SelectorOperationsDuringPreload),
    ("native task failure leaves text available and preview protected", NativeBatchCases.FaultLeavesTextAvailable),
    ("editor close disposes queued preload exactly once", CloseQueued),
    ("editor close cancels native tasks without moving their coroutines", NativeBatchCases.CloseCancelsOutstandingNativeTasks),
    ("resource factories retain native enumerator identity", NativeBatchCases.ResourceFactoriesRemainNative),
    ("original native preload is never advanced by Azurite", NativeBatchCases.OriginalEnumeratorNeverAdvanced),
    ("native resource MoveNext never re-enters through a managed wrapper", NativeBatchCases.ResourceExecutionStaysNative),
    ("native task results are consumed exactly once", NativeBatchCases.NativeTasksCompleteExactlyOnce),
    ("large resource queue stays bounded while editor frames advance", NativeBatchCases.LargeQueueStaysBounded),
    ("slow native tasks keep at most four requests active", NativeBatchCases.ActiveNativeTasksStayBounded),
    ("same-frame updates cannot drain the native dispatch queue", NativeBatchCases.SameFrameUpdatesDoNotDrainQueue),
    ("cached native completions still admit at most one request per frame", NativeBatchCases.CachedCompletionsStayBounded),
    ("duplicate resource metadata loads every distinct resource once", NativeBatchCases.DuplicateResourcesAreDeduplicated),
    ("plugin disposal releases private loading tag only", DisposeOwnTagOnly),
    ("resource snapshot survives live script and character edits", NativeBatchCases.MetadataSnapshotSurvivesEdits),
    ("export ownership pauses admissions without cancelling native work", NativeBatchCases.ExportPausesQueueWithoutCancelling),
    ("disabling feature prevents new capture but completes owned preload", DisableFinishesOwnedWork),
};
int failed = 0;
foreach (var (name, test) in cases)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
Console.WriteLine($"RESULT {cases.Length - failed}/{cases.Length}; {metadata.VerifiedMethods} actual installed signature comparisons; source-linked production controller, simulated Unity/IL2CPP dispatch only.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

static void InstallResolves()
{
    using var f = new Fixture();
    Check(f.Plugin.IsInstalled, "production Install did not install");
    Harmony.Metadata.VerifyProperty(typeof(AuthoringWorkbench._Load_d__47), "__1__state");
    Harmony.Metadata.VerifyProperty(typeof(AuthoringWorkbench._Load_d__47), "__2__current");
    Harmony.Metadata.VerifyProperty(typeof(AuthoringWorkbench._Load_d__47), "project");
    Harmony.Metadata.VerifyProperty(typeof(ScriptData._CoPreloadAssets_d__74), "scripts");
    Harmony.Metadata.VerifyProperty(typeof(ProjectData), "nodes");
    Harmony.Metadata.VerifyProperty(typeof(ScriptNodeData), "Scripts");
    Harmony.Metadata.Verify(typeof(ScriptData._CoPreloadAssets_d__74).GetMethod("System_IDisposable_Dispose"));
    foreach (string factory in new[] { "CoTryPreloadBackgroundTexture", "CoTryPreloadVoice", "CoTryPreloadPopupImage", "CoTryPreloadBGMClip", "CoTryPreloadSound" })
        Harmony.Metadata.Verify(typeof(ScenarioResourceManager).GetMethod(factory));
    Harmony.Metadata.Verify(typeof(CharacterManager).GetMethod("CoTryPreloadSpine"));
    Harmony.Metadata.Verify(typeof(Loading).GetMethod("StartLoading"));
    Harmony.Metadata.Verify(typeof(Loading).GetMethod("StopLoading"));
    Harmony.Metadata.Verify(typeof(Loading).GetMethod("ResetLoading"));
    Harmony.Metadata.Verify(typeof(AuthoringEditorSession).GetMethod("Refresh", new[] { typeof(bool) }));
    Harmony.Metadata.Verify(typeof(StudioCommon).GetMethod("OnChangesMade", new[] { typeof(bool) }));
    foreach (string property in new[] { "bgName", "voice", "popup", "bgmId", "sound", "characters" })
        Harmony.Metadata.VerifyProperty(typeof(ScriptData), property);
    Harmony.Metadata.VerifyProperty(typeof(ScriptData.CharacterRecordData), "name");
}
static void WorkbenchOnly()
{
    using var f = new Fixture();
    Check(Harmony.Hooks.Keys.Any(m => m.DeclaringType == typeof(AuthoringWorkbench._Load_d__47) && m.Name == "MoveNext"), "actual editor-open state machine was never patched");
    Check(!Harmony.Hooks.Keys.Any(m => m.DeclaringType.FullName.Contains("CoLoadSave")), "save playback must retain native resource order");
}
static void DefersWithoutExecution()
{
    using var f = new Fixture();
    f.Capture();
    Check(f.Native.Moves == 0, "preload executed eagerly before editor entry");
    Check(f.Workbench.__2__current == null, "native coroutine still awaits all project resources");
    Check(f.Plugin.Busy && f.Plugin.BlocksEditorOperations, "pending scene must be guarded before Studio.Start");
}
static void RejectsWrongYield()
{
    using var f = new Fixture();
    f.Workbench.__1__state = 2;
    f.Workbench.__2__current = f.Native;
    f.Workbench.MoveNext();
    Check(!f.Plugin.Busy && f.Workbench.__2__current == f.Native, "wrong state was intercepted");
    f.Workbench.__1__state = 1;
    var unknown = new Il2CppSystem.Object();
    f.Workbench.__2__current = unknown;
    f.Workbench.MoveNext();
    Check(!f.Plugin.Busy && f.Workbench.__2__current == unknown, "unknown native child was swallowed");
    f.Workbench.__2__current = f.Native;
    f.Workbench.StepResult = false;
    f.Workbench.MoveNext();
    Check(!f.Plugin.Busy, "completed coroutine was intercepted");
}
static void StructureBeforeResources()
{
    using var f = new Fixture();
    f.Capture();
    f.Studio.Start();
    Check(!f.Plugin.BlocksEditorOperations && f.Plugin.EditorUnlocked, "text/structure remain blocked until all assets finish");
    Check(!f.Plugin.ResourceDependentWorkAllowed, "unready assets were marked usable");
    Check(f.Native.Moves == 0, "shell startup forced the whole resource preload");
    f.Advance(); f.Advance();
    Check(NativeTasks.Calls.Count > 0 && !f.Plugin.ResourceDependentWorkAllowed, "background work did not begin with editing available");
}
static void CatalogCaptureDeferred()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start();
    Check(AuthoringEditorSession.Current != null, "editor session fixture did not begin");
    Check(AuthoringEditorSession.Current.CatalogVersionCalls == 0, "initial Begin still performed cold CatalogVersion capture");
    Check(f.Plugin.EditorUnlocked && f.Plugin.BlocksEditorOperations == false, "catalog defer did not unlock editor structure");
}
static void ChangeCaptureDeferred()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start();
    var current = AuthoringEditorSession.Current;
    int before = current.CatalogVersionCalls;
    f.Studio.OnChangesMade(true);
    Check(current.CatalogVersionCalls == before, "first OnChangesMade repeated cold CatalogVersion capture");
}
static void CatalogObservationRestored()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start();
    var current = AuthoringEditorSession.Current;
    Check(current.CatalogVersionCalls == 0, "fixture started with authoritative catalog capture");
    f.Complete();
    Check(current.CatalogVersionCalls == 0, "background resource completion unexpectedly triggered a blocking catalog observation");
    current.BeginSave();
    Check(current.CatalogVersionCalls == 1, "explicit save did not retain authoritative catalog observation");
}
static void SceneOverlayReleased()
{
    using var f = new Fixture();
    f.Capture();
    var previous = Singleton<Loading>.Instance;
    var current = new Loading(); current.Awake();
    Check(current.loadingCount == 0 && current.gameObject.activeSelf, "native scene lifecycle not reproduced");
    f.Studio.Start();
    Check(f.Plugin.Busy && !current.gameObject.activeSelf, "current overlay still intercepts clicks while resources are pending");
    Check(Loading.LoadingTags.Count == 0, "private entry tag was retained");
    Check(!previous.gameObject.activeSelf, "surviving previous scene overlay still intercepts clicks");
    f.Complete();
    Check(!current.gameObject.activeSelf, "Now Loading remains after resource completion");
}
static void DestroyedOverlayReleased()
{
    using var f = new Fixture();
    f.Capture(); Singleton<Loading>.Instance.Destroyed = true;
    var current = new Loading(); current.Awake(); f.Studio.Start();
    Check(!current.gameObject.activeSelf && Loading.LoadingTags.Count == 0,
        "destroyed captured instance leaves the new scene blocked");
}
static void ForeignSceneOverlayPreserved()
{
    using var f = new Fixture();
    Singleton<Loading>.Instance.StartLoading("native-scene-transition");
    f.Capture();
    var current = new Loading(); current.Awake(); f.Studio.Start();
    Check(Loading.LoadingTags.SetEquals(new[] { "native-scene-transition" }), "foreign scene tag removed or private tag leaked");
    Check(current.gameObject.activeSelf && current.Resets == 0, "foreign load overlay was bypassed");
    current.StopLoading("native-scene-transition");
    Check(!current.gameObject.activeSelf && f.Plugin.Busy, "native scene completion still waits for resources");
    current.StartLoading("save-in-progress"); f.Complete();
    Check(current.gameObject.activeSelf && Loading.LoadingTags.SetEquals(new[] { "save-in-progress" }),
        "resource completion bypassed a real save");
}
static void MissingOverlayRetries()
{
    using var f = new Fixture();
    f.Capture(); Singleton<Loading>.Instance.Destroyed = true; Singleton<Loading>.Instance = null;
    f.Studio.Start();
    var current = new Loading(); current.Awake(); f.Advance();
    Check(!current.gameObject.activeSelf && Loading.LoadingTags.Count == 0,
        "lease was forgotten before the scene overlay became available");
}
static void PreviewResumes()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start();
    f.Preview.PlayPreview(); f.Preview.PlayPreview();
    Check(f.Preview.Played == 2, "native preview was not allowed immediately after editor entry");
    f.Complete();
    Check(f.Plugin.ResourceDependentWorkAllowed, "completed resources never become available");
    Check(f.Preview.Played >= 2, "preview count regressed after resources completed");
    f.Advance(); f.Advance();
    Check(f.Preview.Played >= 2, "completed preview was lost");
}
static void PreviewWithoutPending()
{
    using var f = new Fixture();
    f.Preview.PlayPreview();
    Check(f.Preview.Played == 1, "native preview was blocked without a pending load");
    f.Capture(); f.Studio.Start(); f.Complete(); f.Preview.PlayPreview();
    Check(f.Preview.Played == 2, "native preview remained blocked after readiness");
}
static void PreviewBeforeStudioStart()
{
    using var f = new Fixture();
    f.Capture(); f.Preview.PlayPreview();
    Check(f.Preview.Played == 0, "scene initialization preview touched incomplete resources");
    f.Studio.Start(); f.Preview.PlayPreview(); f.Advance();
    Check(f.Preview.Played >= 1, "initial preview was lost after editor entry");
}
static void PreviewDuringInspectorLoading()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start();
    f.Preview.loading = true;
    f.Preview.PlayPreview();
    Check(f.Preview.Played == 0, "preview advanced synchronously inside native inspector loading");
    f.Preview.loading = false;
    f.Advance();
    Check(f.Preview.Played == 1, "deferred preview was not admitted after the loading call yielded");
}
static void SelectorOperationsDuringPreload()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start(); f.Advance();
    Check(Harmony.Hooks.Keys.Count(m => m.DeclaringType == typeof(ScriptNodeInspector) &&
        m.Name is "OpenBackgroundSelector") == 1,
        "environment entry must remain guarded while character entry is available");
    int before = f.Preview.ResourceOperations;
    f.Preview.OpenCharacterSelector(1);
    f.Preview.OpenEmotionSelector(new Script.CharacterRecord(), new Script());
    f.Preview.SetCharacter(1, "character-during-load");
    f.Preview.OpenBackgroundSelector();
    Check(f.Preview.ResourceOperations == before + 3,
        "character/emotion entry and model mutation should remain usable during preload");
    Check(f.Plugin.Busy && !f.Plugin.ResourceDependentWorkAllowed, "selector test did not keep resources pending");
    f.Complete();
    Check(f.Preview.ResourceOperations >= before + 3, "resource operations did not resume after completion");
}
static void CloseQueued()
{
    using var f = new Fixture();
    f.Capture(); f.Studio.Start(); f.Preview.PlayPreview(); int played = f.Preview.Played; f.Studio.OnDestroy(); f.Studio.OnDestroy();
    Check(f.Native.Moves == 0 && f.Native.Disposals == 1, "queued original must dispose once without running");
    Check(!f.Plugin.Busy, "closed editor keeps pending session");
    f.Advance();
    Check(f.Preview.Played == played, "closed inspector preview was replayed");
}
static void DisposeOwnTagOnly()
{
    using var f = new Fixture();
    Singleton<Loading>.Instance.StartLoading("unrelated-operation");
    f.Capture(); f.Plugin.Dispose(); f.Plugin.Dispose();
    Check(Loading.LoadingTags.SetEquals(new[] { "unrelated-operation" }), "shutdown leaked private tag or released another owner's tag");
    Check(f.Native.Disposals == 1, "shutdown fails exact-once disposal");
}
static void DisableFinishesOwnedWork()
{
    using var f = new Fixture();
    f.Plugin.Enabled = false;
    f.Capture();
    Check(!f.Plugin.Busy && f.Workbench.__2__current == f.Native, "disabled controller started intercepting a project");
    f.Plugin.Enabled = true; f.Capture(); f.Studio.Start(); f.Advance(); f.Advance();
    f.Plugin.Enabled = false;
    f.Complete();
    Check(f.Plugin.ResourceDependentWorkAllowed && f.Native.Disposals == 1, "master toggle abandoned resources already owned by Azurite");
}

internal sealed class Fixture : IDisposable
{
    public readonly List<string> Log = new();
    public readonly ProgressiveEditorLoading Plugin;
    public readonly ScriptData._CoPreloadAssets_d__74 Native = new();
    public readonly AuthoringWorkbench._Load_d__47 Workbench = new();
    public readonly StudioCommon Studio = new();
    public readonly ScriptNodeInspector Preview = new();
    public readonly MonoBehaviour Host = new();
    public Fixture()
    {
        Time.frameCount = 0; Loading.LoadingTags.Clear(); Singleton<Loading>.Instance = new Loading();
        NativeTasks.Reset();
        Singleton<ScenarioResourceManager>.Instance = new ScenarioResourceManager();
        Singleton<CharacterManager>.Instance = new CharacterManager();
        var scripts = new Il2CppSystem.Collections.Generic.List<ScriptData>();
        scripts.Add(new ScriptData { bgName = 42 }); SetScripts(scripts);
        Harmony.Attempts.Clear();
        Plugin = new ProgressiveEditorLoading(Log.Add);
        if (!Plugin.Install()) { Plugin.Dispose(); throw new InvalidOperationException(string.Join(" | ", Log)); }
    }
    public void SetScripts(Il2CppSystem.Collections.Generic.List<ScriptData> scripts)
    {
        Native.scripts = scripts;
        Workbench.project = new ProjectData();
        Workbench.project.nodes.Add(new ScriptNodeData { Scripts = scripts });
    }
    public void Capture()
    {
        Workbench.project ??= new ProjectData();
        Workbench.__1__state = 1; Workbench.__2__current = Native;
        Workbench.MoveNext();
    }
    public void Advance()
    {
        Time.frameCount++;
        NativeTasks.Tick();
        Plugin.Update(Host, false);
        Host.Tick();
    }
    public void Complete()
    {
        for (int n = 0; n < 30 && !Plugin.ResourceDependentWorkAllowed; n++) Advance();
        Advance(); // Completion replay runs on the following controller update.
    }
    public void Dispose() => Plugin.Dispose();
}
