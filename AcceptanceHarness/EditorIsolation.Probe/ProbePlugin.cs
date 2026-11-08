using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using AzureArchive.Automation;
using RuntimeProperties = UnityEngine.AddressableAssets.Initialization.AddressablesRuntimeProperties;

namespace Azurite.EditorIsolation;

// This assembly is not part of any shipping project, package or profile.
[BepInPlugin("lab.azurite.editor-isolation", "Azurite Editor Isolation LAB ONLY", "0.1.0")]
public sealed class ProbePlugin : BasePlugin
{
    public override void Load()
    {
        try
        {
            var manifestPath = Environment.GetEnvironmentVariable("AZURITE_EDITOR_ISOLATION_MANIFEST");
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (!LabEntryAdmission.ShouldActivate(manifestPath, process.MainModule?.FileName, typeof(ProbePlugin).Assembly.Location,
                @"BepInEx\plugins\AzuriteEditorIsolation\Azurite.EditorIsolation.Probe.LabOnly.dll")) return;
            ProbeRuntime.Install(manifestPath!);
        }
        catch (Exception e) { ProbeRuntime.Stop("install-rejected", e.GetType().Name + ": " + e.Message); }
    }
}

internal static class ProbeRuntime
{
    private const string AddressProperty = "UnityEngine.Application.persistentDataPath";
    private static RunManifest manifest = null!;
    private static RunPaths paths = null!;
    private static ProbeLedger ledger = null!;
    private static Dictionary<string, NativeBinding> bindings = null!;
    private static readonly Dictionary<string, int> hits = new();
    private static CorrelatedEvidence? evidence;
    private static int mainThread;
    private static bool installed, awakeObserved, selfTestRunning;
    private static IntPtr ownerPointer;
    private static FileStream? seedLock, controlLock;
    private static Type guardian = null!;
    private static string manifestHash = "";

    internal static void Install(string manifestPath)
    {
        var bound = BoundManifest.ReadAt(HostContract.ApprovedLabRoot, manifestPath);
        evidence = new CorrelatedEvidence(bound, "editor-isolation-evidence.jsonl");
        manifestHash = bound.Sha256;
        manifest = bound.Parse();
        HostContract.RequireApprovedRun(manifest);
        manifest.Validate(manifestPath);
        paths = manifest.Paths;
        var guardians = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Azurite.EditorIsolation.Guard.LabOnly").ToArray();
        if (guardians.Length != 1 || !RunPaths.Same(guardians[0].Location, Path.Combine(paths.Install, "BepInEx", "patchers", "AzuriteEditorIsolation", "Azurite.EditorIsolation.Guard.LabOnly.dll"))) throw new InvalidOperationException("Exactly one preloaded guardian at the pinned lab path is required.");
        guardian = guardians[0].GetType("Azurite.EditorIsolation.StartupGuard", throwOnError: true)!;
        guardian.GetMethod("RequireArmed", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { manifestHash });
        // No flag, manifest Boolean or hidden option can enable normal startup in this version.
        if (manifest.ProbeMode != ProbeMode.RouteSelfTest) throw new InvalidOperationException("normalObserverCatalogOnly is gated pending native hook-hit, resource closure, ordering and NoSound review.");
        var argv = Environment.GetCommandLineArgs();
        foreach (var flag in new[] { "-batchmode", "-nographics", "-disableaudio" }) if (argv.Count(a => a == flag) != 1) throw new InvalidOperationException("Missing/duplicate bounded startup flag: " + flag);
        var cliDirectory = Environment.GetEnvironmentVariable("AZUREARCHIVE_CLI_DIRECTORY") ?? throw new InvalidOperationException("Selftest requires the real resource-free CLI directory transport.");
        paths.RequireContained(cliDirectory);
        var request = Path.Combine(cliDirectory, "arguments.json"); paths.RequireContained(request);
        using (var requestJson = JsonDocument.Parse(File.ReadAllBytes(request)))
        {
            var args = requestJson.RootElement.GetProperty("argv");
            if (args.GetArrayLength() != 1 || args[0].GetString() != "capabilities") throw new InvalidOperationException("Only the resource-free capabilities request may host this selftest.");
        }
        mainThread = Environment.CurrentManagedThreadId;
        ledger = new ProbeLedger(manifest.ProbeMode);
        Emit("install-enter", new { manifest.RunId, manifest.Mode, manifestSha256 = RunManifest.Sha256(manifestPath), startupOrderingProved = false });
        bindings = NativeBindings.Bind(paths);
        foreach (var b in bindings.Values) Emit("native-binding", new { b.Contract.Id, b.Contract.Token, b.Contract.Rva, methodInfo = b.MethodInfo.ToInt64(), entry = b.Entry.ToInt64() });
        var harmony = new Harmony("lab.azurite.editor-isolation." + manifest.RunId);
        Patch(harmony, "read", nameof(ReadPrefix), nameof(ReadPostfix));
        Patch(harmony, "write", nameof(WritePrefix), nameof(WritePostfix));
        Patch(harmony, "fallback", nameof(FallbackPrefix));
        Patch(harmony, "cache", nameof(CachePrefix));
        Patch(harmony, "workspace", nameof(WorkspacePrefix));
        Patch(harmony, "resources", nameof(ResourcePrefix));
        Patch(harmony, "discovery", nameof(DiscoveryPrefix));
        Patch(harmony, "offline", nameof(OfflinePrefix));
        Patch(harmony, "addressSet", nameof(AddressSetPrefix));
        Patch(harmony, "addressClear", null, nameof(AddressClearPostfix));
        Patch(harmony, "addressEvaluate", nameof(AddressEvaluatePrefix), nameof(AddressEvaluatePostfix));
        Patch(harmony, "awake", nameof(AwakePrefix), nameof(AwakePostfix));
        installed = true;
        guardian.GetMethod("RoutesReady", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { manifestHash, StartupGuardState.RequiredRoutes });
        Emit("hooks-registered", new { count = 12, nativeInterceptionProved = false, priorAwakeAbsenceProved = false });
    }

    private static void Patch(Harmony harmony, string id, string? prefix, string? postfix = null)
    {
        var method = bindings[id].Method;
        if (Harmony.GetPatchInfo(method)?.Owners.Any() == true) throw new InvalidOperationException("Another patch owns isolation seam: " + id);
        harmony.Patch(method, prefix is null ? null : new HarmonyMethod(typeof(ProbeRuntime), prefix), postfix is null ? null : new HarmonyMethod(typeof(ProbeRuntime), postfix));
        var info = Harmony.GetPatchInfo(method);
        if (info is null || (prefix is not null && !info.Prefixes.Any(p => p.owner == harmony.Id)) || (postfix is not null && !info.Postfixes.Any(p => p.owner == harmony.Id))) throw new InvalidOperationException("Patch registration failed: " + id);
    }

    private static bool AwakePrefix(UserSettings __instance)
    {
        try
        {
            MainThread();
            Hit("awake");
            guardian.GetMethod("RequireChainloaderCompleted", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { manifestHash });
            if (!installed || awakeObserved || !AuthoringCliHost.IsActive || __instance.data is not null || __instance.isReady) throw new InvalidOperationException("Selftest trigger is late, repeated, non-CLI or non-pristine.");
            awakeObserved = true; ownerPointer = __instance.Pointer;
            ledger.Arm(cliActive: true, beforeFirstAwake: installed);
            Emit("cli-awake-enter", new { owner = ownerPointer.ToInt64(), cliActive = true, nativeAwakeWillRun = true, globalStartupOrderProved = false });
            return true; // Native Awake takes its own existing CLI branch. Never set IsActive/readiness.
        }
        catch (Exception e) { Stop("awake-rejected", e.Message); return false; }
    }

    private static void AwakePostfix(UserSettings __instance)
    {
        try { RunSafeRouteProbe(__instance); }
        catch (Exception e) { Stop("route-selftest-failed", e.GetType().Name + ": " + e.Message); }
    }

    private static void RunSafeRouteProbe(UserSettings owner)
    {
        MainThread();
        if (!AuthoringCliHost.IsActive || owner.Pointer != ownerPointer || selfTestRunning || owner.data is not null || owner.isReady) throw new InvalidOperationException("Invalid native CLI owner.");
        selfTestRunning = true;
        manifest.Validate(Path.Combine(paths.Run, "manifest.json"));
        seedLock = new FileStream(paths.Settings, FileMode.Open, FileAccess.Read, FileShare.Read);
        controlLock = new FileStream(paths.Control, FileMode.Open, FileAccess.Read, FileShare.Read);
        manifest.Validate(Path.Combine(paths.Run, "manifest.json"));
        // If the native patch misses, the original can only open this second fully validated lab seed.
        owner.settingFilePath = paths.Control;
        owner._cachedWorkspacePath = paths.Workspace;
        var iterator = new UserSettings._ReadUserSettings_d__43(0) { __4__this = owner };
        Emit("native-read-dispatch", new { owner = owner.Pointer.ToInt64(), iterator = iterator.Pointer.ToInt64(), missControl = paths.Control });
        var yielded = NativeBindings.InvokeMoveNext(bindings["read"], iterator.Pointer);
        RequireHit("read", 1);
        if (yielded || owner.data is null || owner.data.username != "lab-read-" + manifest.RunId || !RunPaths.Same(owner.settingFilePath, paths.Settings) || owner.isReady) throw new InvalidOperationException("Native read result or untouched readiness mismatch.");
        ProveReadOnlyRoutes(owner);
        // Exercise the pinned native SaveTask -> WriteUserSettings call edge, not the managed wrapper.
        SettingsWriteAdmission.Dispatch(paths, () =>
        {
            // The fresh-directory admission is outside the hook being tested, even if WritePrefix misses.
            seedLock.Dispose(); seedLock = null;
            owner.data.username = "lab-saved-" + manifest.RunId;
            owner.isDataDirty = true;
            var saver = new UserSettings._SaveTask_d__41(0) { __4__this = owner };
            if (!NativeBindings.InvokeMoveNext(bindings["save"], saver.Pointer) || !NativeBindings.InvokeMoveNext(bindings["save"], saver.Pointer)) throw new InvalidOperationException("Unexpected native save iterator state.");
        });
        RequireHit("write", 1);
        using var saved = JsonDocument.Parse(File.ReadAllBytes(paths.Settings));
        if (saved.RootElement.GetProperty("username").GetString() != "lab-saved-" + manifest.RunId || !ledger.RouteProofComplete || owner.isReady) throw new InvalidOperationException("Original native save was not proved or readiness changed.");
        if (!string.Equals(RunManifest.Sha256(paths.Control), manifest.ControlSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Miss-control file changed.");
        controlLock.Dispose(); controlLock = null;
        Emit("route-selftest-complete", new { readHits = hits["read"], writeHits = hits["write"], saveEdge = "native SaveTask.MoveNext -> native WriteUserSettings", savedSha256 = RunManifest.Sha256(paths.Settings), readinessUntouched = !owner.isReady, normalStartupProved = false, catalogProved = false });
        // No lifecycle advancement, Current/Begin, resource initialization, input or project open.
    }

    private static void ProveReadOnlyRoutes(UserSettings owner)
    {
        if (!AuthoringCliHost.IsActive || owner.isReady) throw new InvalidOperationException("Read-only route checks require the unchanged resource-free CLI state.");
        // Exact pinned native getters were inspected as read-only. On a detour miss CachePath may
        // read the daily JSON; the returned string is recorded/rejected and is NEVER used for IO.
        foreach (var id in new[] { "cache", "workspace" })
        {
            var b = bindings[id]; var before = HitCount(id);
            var pointer = Marshal.GetDelegateForFunctionPointer<NativeBindings.InstanceStringEntry>(b.Entry)(owner.Pointer, b.MethodInfo);
            var returned = IL2CPP.Il2CppStringToManaged(pointer);
            Emit("native-readonly-getter-returned", new { id, returned, downstreamIo = false, hitDelta = HitCount(id) - before });
            RequireHitDelta(id, before);
            if (!RunPaths.Same(returned, id == "cache" ? paths.Cache : paths.Workspace)) throw new InvalidOperationException("Native getter route mismatch; result was not consumed.");
        }
        var discovery = bindings["discovery"]; var discoveryBefore = HitCount("discovery");
        var discoveryPointer = Marshal.GetDelegateForFunctionPointer<NativeBindings.StaticStringEntry>(discovery.Entry)(discovery.MethodInfo);
        var discoveryPath = IL2CPP.Il2CppStringToManaged(discoveryPointer);
        Emit("native-discovery-getter-returned", new { returned = discoveryPath, listenerStarted = false });
        RequireHitDelta("discovery", discoveryBefore);
        if (!RunPaths.Same(discoveryPath, paths.Discovery)) throw new InvalidOperationException("Native discovery route mismatch.");

        // These three native methods only edit/query Addressables' property dictionary. No catalog is loaded.
        var setter = bindings["addressSet"]; var setterBefore = HitCount("addressSet");
        Marshal.GetDelegateForFunctionPointer<NativeBindings.StaticTwoStringVoidEntry>(setter.Entry)(IL2CPP.ManagedStringToIl2Cpp(AddressProperty), IL2CPP.ManagedStringToIl2Cpp(paths.Output), setter.MethodInfo);
        RequireHitDelta("addressSet", setterBefore);
        if (!RunPaths.Same(RuntimeProperties.s_CachedValues[AddressProperty], paths.Persistent)) throw new InvalidOperationException("Original native SetPropertyValue did not retain the routed value.");
        var clearer = bindings["addressClear"]; var clearBefore = HitCount("addressClear");
        Marshal.GetDelegateForFunctionPointer<NativeBindings.StaticVoidEntry>(clearer.Entry)(clearer.MethodInfo);
        RequireHitDelta("addressClear", clearBefore);
        if (!RunPaths.Same(RuntimeProperties.s_CachedValues[AddressProperty], paths.Persistent)) throw new InvalidOperationException("Native clear/reseed did not retain the routed value.");
        var evaluator = bindings["addressEvaluate"]; var evaluateBefore = HitCount("addressEvaluate");
        var evaluatedPointer = Marshal.GetDelegateForFunctionPointer<NativeBindings.InstanceStringEntry>(evaluator.Entry)(IL2CPP.ManagedStringToIl2Cpp(AddressProperty), evaluator.MethodInfo);
        var evaluated = IL2CPP.Il2CppStringToManaged(evaluatedPointer);
        Emit("native-addressables-property-returned", new { returned = evaluated, catalogLoaded = false });
        RequireHitDelta("addressEvaluate", evaluateBefore);
        if (!RunPaths.Same(evaluated, paths.Persistent)) throw new InvalidOperationException("Addressables native property route mismatch.");
    }

    private static bool ReadPrefix(UserSettings._ReadUserSettings_d__43 __instance)
    {
        try
        {
            MainThread(); Hit("read");
            var owner = __instance.__4__this;
            if (!selfTestRunning || owner.Pointer != ownerPointer || !AuthoringCliHost.IsActive) throw new InvalidOperationException("Unexpected settings read.");
            ledger.ReadPrefix(owner.Pointer.ToInt64(), __instance.__1__state);
            RunManifest.ValidateSeed(paths.Settings, manifest.SeedSha256, paths, "lab-read-" + manifest.RunId);
            owner.settingFilePath = paths.Settings; owner._cachedWorkspacePath = paths.Workspace;
            if (!RunPaths.Same(owner.settingFilePath, paths.Settings) || !RunPaths.Same(owner._cachedWorkspacePath, paths.Workspace)) throw new InvalidOperationException("Native field write did not stick.");
            Emit("native-read-prefix", new { owner = owner.Pointer.ToInt64(), iterator = __instance.Pointer.ToInt64(), __instance.__1__state, actualSettingFilePath = owner.settingFilePath, actualCachedWorkspace = owner._cachedWorkspacePath });
            return true;
        }
        catch (Exception e) { Stop("read-rejected", e.Message); return false; }
    }
    private static void ReadPostfix(UserSettings._ReadUserSettings_d__43 __instance, bool __result)
    {
        try
        {
            var owner = __instance.__4__this;
            var data = owner.data ?? throw new InvalidOperationException("Native deserialization returned null.");
            ledger.ReadCompleted(owner.Pointer.ToInt64(), __result, data.username, "lab-read-" + manifest.RunId);
            if (!RunPaths.Same(data.workspacePath, paths.Workspace) || !RunPaths.Same(data.cachePath, paths.Cache)) throw new InvalidOperationException("Native deserialized paths disagree.");
            Emit("native-original-read-completed", new { owner = owner.Pointer.ToInt64(), data = data.Pointer.ToInt64(), yielded = __result, marker = data.username });
        }
        catch (Exception e) { Stop("read-result-rejected", e.Message); }
    }
    private static bool WritePrefix(UserSettings __instance)
    {
        try
        {
            MainThread(); Hit("write"); paths.RequireSettingsTarget(__instance.settingFilePath);
            SettingsWriteAdmission.ValidateFresh(paths);
            if (__instance.data is null || !RunPaths.Same(__instance.data.workspacePath, paths.Workspace) || !RunPaths.Same(__instance.data.cachePath, paths.Cache)) throw new InvalidOperationException("Native save data escaped lab paths.");
            ledger.AdmitWrite(__instance.Pointer.ToInt64());
            Emit("native-write-admitted", new { owner = __instance.Pointer.ToInt64(), target = __instance.settingFilePath });
            return true;
        }
        catch (Exception e) { Stop("write-rejected", e.Message); return false; }
    }
    private static void WritePostfix(UserSettings __instance)
    {
        try { ledger.WriteCompleted(__instance.Pointer.ToInt64()); Emit("native-original-write-returned", new { owner = __instance.Pointer.ToInt64() }); }
        catch (Exception e) { Stop("write-result-rejected", e.Message); }
    }
    private static bool CachePrefix(ref string __result) => RouteGetter("cache", paths.Cache, ref __result);
    private static bool WorkspacePrefix(ref string __result) => RouteGetter("workspace", paths.Workspace, ref __result);
    private static bool DiscoveryPrefix(ref string __result) => RouteGetter("discovery", paths.Discovery, ref __result);
    private static bool RouteGetter(string id, string route, ref string result)
    {
        try { MainThread(); Hit(id); result = paths.RequireContained(route); Emit("native-getter-routed", new { id, route }); return false; }
        catch (Exception e) { Stop("getter-rejected", e.Message); return false; }
    }
    private static bool FallbackPrefix() { Stop("native-fallback-rejected", "InitializeSettings would enter a default-user/dialog path."); return false; }
    private static bool ResourcePrefix() { Stop("native-resource-rejected", "Resource initialization is forbidden in route-selftest; normal observer remains gated."); return false; }
    private static bool OfflinePrefix(ref string __3)
    {
        try
        {
            MainThread(); Hit("offline");
            if (manifest.ProbeMode == ProbeMode.RouteSelfTest) throw new InvalidOperationException("Offline resource validation is outside resource-free selftest.");
            __3 = paths.RequireContained(paths.Persistent);
            RuntimeProperties.SetPropertyValue(AddressProperty, __3);
            if (!RunPaths.Same(RuntimeProperties.EvaluateProperty(AddressProperty), __3)) throw new InvalidOperationException("Offline validator/Addressables persistent disagreement.");
            Emit("native-offline-argument-routed", new { persistentPath = __3 }); return true;
        }
        catch (Exception e) { Stop("offline-rejected", e.Message); return false; }
    }
    private static bool AddressSetPrefix(string __0, ref string __1)
    {
        try { if (__0 == AddressProperty) { MainThread(); Hit("addressSet"); __1 = paths.RequireContained(paths.Persistent); } return true; }
        catch (Exception e) { Stop("address-set-rejected", e.Message); return false; }
    }
    private static void AddressClearPostfix()
    {
        try
        {
            MainThread(); Hit("addressClear");
            if (RuntimeProperties.s_CachedValues.Count != 0) throw new InvalidOperationException("Original native property clear did not empty the dictionary.");
            RuntimeProperties.SetPropertyValue(AddressProperty, paths.RequireContained(paths.Persistent));
            Emit("addressables-reseeded-after-native-clear", new { persistent = paths.Persistent, originalClearObserved = true });
        }
        catch (Exception e) { Stop("address-clear-rejected", e.Message); }
    }
    private static bool AddressEvaluatePrefix(string __0)
    {
        try { if (__0 == AddressProperty) { MainThread(); Hit("addressEvaluate"); RuntimeProperties.SetPropertyValue(AddressProperty, paths.RequireContained(paths.Persistent)); } return true; }
        catch (Exception e) { Stop("address-evaluate-rejected", e.Message); return false; }
    }
    private static void AddressEvaluatePostfix(string __0, string __result)
    {
        if (__0 == AddressProperty && !RunPaths.Same(__result, paths.Persistent)) Stop("address-result-rejected", "Original EvaluateProperty did not return lab persistent path.");
    }
    private static void MainThread() { if (Environment.CurrentManagedThreadId != mainThread) throw new InvalidOperationException("Native isolation callback is off the installation thread."); }
    private static void Hit(string id) { hits[id] = hits.TryGetValue(id, out var n) ? n + 1 : 1; }
    private static int HitCount(string id) => hits.TryGetValue(id, out var n) ? n : 0;
    private static void RequireHitDelta(string id, int before) { if (HitCount(id) - before != 1) throw new InvalidOperationException("Native interception miss/duplicate: " + id); }
    private static void RequireHit(string id, int expected) { if (!hits.TryGetValue(id, out var n) || n != expected) throw new InvalidOperationException("Native interception miss/duplicate: " + id); }
    private static void Emit(string kind, object data) => evidence?.Emit(kind, data);
    [DoesNotReturn]
    internal static void Stop(string kind, string reason)
    {
        try { ledger?.Fail(reason); Emit(kind, new { reason, accepted = false }); evidence?.Flush(); } catch { }
        // Harmony IL2CPP bridges may catch managed exceptions. Terminate the bounded child instead of relying on propagation.
        OwnProcessAbort.Terminate(86);
    }
}
