using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BepInEx.Bootstrap;
using BepInEx.Preloader.Core.Patching;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace Azurite.EditorIsolation;

[PatcherPluginInfo("lab.azurite.editor-isolation.guard", "Azurite Editor Isolation Startup Guard LAB ONLY", "0.1.0")]
public sealed class StartupGuard : BasePatcher
{
    private static readonly StartupGuardState state = new();
    private static CorrelatedEvidence? evidence;
    private static string? identity;

    public override void Finalizer()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("AZURITE_EDITOR_ISOLATION_MANIFEST");
            using var process = Process.GetCurrentProcess();
            if (!LabEntryAdmission.ShouldActivate(path, process.MainModule?.FileName, typeof(StartupGuard).Assembly.Location,
                @"BepInEx\patchers\AzuriteEditorIsolation\Azurite.EditorIsolation.Guard.LabOnly.dll")) return;
            // Bind only trusted location/actual process/raw bytes before parsing untrusted JSON content.
            var bound = BoundManifest.ReadAt(HostContract.ApprovedLabRoot, path!);
            evidence = new CorrelatedEvidence(bound, "editor-isolation-guardian.jsonl");
            identity = bound.Sha256;
            var manifest = bound.Parse();
            HostContract.RequireApprovedRun(manifest); manifest.Validate(path!);
            var guardDirectory = Path.Combine(manifest.InstallRoot, "BepInEx", "patchers", "AzuriteEditorIsolation");
            if (!RunPaths.Same(typeof(StartupGuard).Assembly.Location, Path.Combine(guardDirectory, "Azurite.EditorIsolation.Guard.LabOnly.dll")) ||
                !RunPaths.Same(typeof(RunManifest).Assembly.Location, Path.Combine(guardDirectory, "Azurite.EditorIsolation.Policy.LabOnly.dll"))) throw new InvalidOperationException("Guardian/policy assembly came from an unexpected path.");
            if (!RunPaths.Same(process.MainModule!.FileName, Path.Combine(manifest.InstallRoot, "AzureArchive.exe"))) throw new InvalidOperationException("Guardian is not running in the pinned lab executable.");
            HostContract.VerifyFiles(manifest.InstallRoot);
            state.Arm(identity);
            var method = typeof(BaseChainloader<BasePlugin>).GetMethod("Execute", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) ?? throw new MissingMethodException("Pinned chainloader Execute() not found.");
            var harmony = new Harmony("lab.azurite.editor-isolation.guard." + manifest.RunId);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(StartupGuard), nameof(AfterExecute)), finalizer: new HarmonyMethod(typeof(StartupGuard), nameof(AfterExecuteFinalizer)));
            var patch = Harmony.GetPatchInfo(method);
            if (patch is null || !patch.Postfixes.Any(p => p.owner == harmony.Id) || !patch.Finalizers.Any(p => p.owner == harmony.Id)) throw new InvalidOperationException("Guardian completion patches missing.");
            Emit("guardian-armed-before-chainloader", new { manifest.RunId, manifest.Mode, manifestSha256 = identity, executeMetadataToken = method.MetadataToken, executeAssembly = method.Module.Assembly.GetName().Name });
        }
        catch (Exception e) { Abort("guardian-setup-failed", e.Message); }
    }

    // Called by reflection on the unique already-loaded guardian assembly. No duplicate guardian DLL is loaded by the probe.
    public static void RequireArmed(string manifestSha256) => state.RequireArmed(manifestSha256);
    public static void RequireChainloaderCompleted(string manifestSha256) => state.RequireCompleted(manifestSha256);
    public static void RoutesReady(string manifestSha256, string[] routes)
    {
        state.RoutesReady(manifestSha256, routes);
        Emit("all-routing-hooks-registered", new { manifestSha256, routes, nativeHookHitsProved = false });
    }
    private static void AfterExecute()
    {
        try { state.Complete(null); Emit("chainloader-return-admitted", new { manifestSha256 = identity, nativeStartupMayResume = true, normalReadinessProved = false }); }
        catch (Exception e) { Abort("missing-or-incomplete-probe", e.Message); }
    }
    private static Exception? AfterExecuteFinalizer(Exception? __exception)
    {
        try { state.Complete(__exception); return __exception; }
        catch (Exception e) { Abort("chainloader-exception-or-missing-probe", e.Message); return __exception; }
    }
    private static void Emit(string kind, object data) => evidence?.Emit(kind, data);
    private static void Abort(string kind, string reason)
    {
        try { Emit(kind, new { reason, nativeStartupMayResume = false }); evidence?.Flush(); } catch { }
        OwnProcessAbort.Terminate(86);
    }
}
