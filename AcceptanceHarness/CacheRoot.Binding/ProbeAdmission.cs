using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Azurite.CacheRootLab;

public sealed record CacheProbeRequest(string ManifestPath, string ManifestSha256, CacheProbeMode Mode);
public sealed record CacheProbeContext(string RunId, int Pid, uint NativeThreadId, string ManifestSha256, string RunRoot, string SeedRoot, string InstallRoot);
public enum OriginalCall { Bind, SeedRoot, BuildPath, DestroyResult, Finish }

/// <summary>
/// Supplied by a separately qualified runtime integration. No implementation is provided here.
/// It must validate actual startup/allocator ordering, guardian continuation hold and exclusion of
/// competing path/cache users, not infer exclusivity from a thread ID or empty/null observations.
/// </summary>
public interface IPreUseAdmissionLease
{
    string EvidenceSha256 { get; }
    void AssertPermitted(CacheProbeContext context, OriginalCall call);
}

public sealed record CacheProbeEvent(long sequence, DateTime utc, long monotonicTicks, string runId, int pid, string manifestSha256, string kind, object data);
public sealed record PathProbeOutcome(CacheProbeMode Mode, string Root, string OriginalBuilderPath, string AdmissionEvidenceSha256, bool NativeConsumerQualified, bool NormalStartupQualified);

internal static class ProbeAdmission
{
    [DllImport("kernel32.dll", ExactSpelling = true)] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    internal static string Install => Path.Combine(PinContract.ApprovedLab, "install");
    internal static void RequireOwnedHost()
    {
        using var process = Process.GetCurrentProcess();
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || IntPtr.Size != 8 || !Same(process.MainModule?.FileName, Path.Combine(Install, "AzureArchive.exe"))) throw new InvalidOperationException("Live cache operations require the exact approved AA child; no target call was made.");
    }
    internal static CacheProbeContext ReadRequest(CacheProbeRequest request)
    {
        if (request.Mode is not (CacheProbeMode.SeededPathOnly or CacheProbeMode.OmittedSeedPathOnly)) throw new InvalidOperationException("Normal cache admission remains unqualified.");
        var manifestPath = request.ManifestPath;
        var run = Path.GetDirectoryName(manifestPath)!; var id = Path.GetFileName(run);
        if (!Guid.TryParseExact(id, "D", out var guid) || !Same(manifestPath, Path.Combine(PinContract.ApprovedLab, "runs", id, "manifest.json"))) throw new InvalidOperationException("Unapproved run location.");
        SeedPathPolicy.RequireExistingNoReparse(manifestPath);
        if (new FileInfo(manifestPath).Length > 16384) throw new InvalidOperationException("Manifest size exceeded.");
        var raw = File.ReadAllBytes(manifestPath);
        if (PinContract.Hash(raw) != request.ManifestSha256) throw new InvalidOperationException("Manifest hash mismatch.");
        using var json = JsonDocument.Parse(raw); var m = json.RootElement;
        if (m.GetProperty("Version").GetInt32() != 1 || m.GetProperty("RunId").GetString() != id || m.GetProperty("Mode").GetString() != "route-selftest" || !Same(m.GetProperty("LabRoot").GetString(), PinContract.ApprovedLab) || !Same(m.GetProperty("RunRoot").GetString(), run) || !Same(m.GetProperty("InstallRoot").GetString(), Install)) throw new InvalidOperationException("Live run/CLI manifest identity mismatch.");
        var seed = Path.Combine(run, "unity-knownfolder"); SeedPathPolicy.RequireExistingNoReparse(seed);
        if ((File.GetAttributes(seed) & FileAttributes.Directory) == 0) throw new InvalidOperationException("Seed prefix directory must already exist.");
        var context = new CacheProbeContext(guid.ToString("D"), Environment.ProcessId, GetCurrentThreadId(), request.ManifestSha256, run, seed, Install);
        return context;
    }
    internal static void CheckLiveCli(CacheProbeContext context)
    {
        RequireOwnedHost();
        if (context.Pid != Environment.ProcessId || context.NativeThreadId != GetCurrentThreadId()) throw new InvalidOperationException("Bound native thread/process changed.");
        foreach (var flag in new[] { "-batchmode", "-nographics", "-disableaudio" }) if (Environment.GetCommandLineArgs().Count(a => a == flag) != 1) throw new InvalidOperationException("Missing/duplicate headless CLI flag.");
        var requestDirectory = Environment.GetEnvironmentVariable("AZUREARCHIVE_CLI_DIRECTORY");
        if (!Same(requestDirectory, Path.Combine(context.RunRoot, "request"))) throw new InvalidOperationException("CLI transport is not this run's request directory.");
        var arguments = Path.Combine(requestDirectory!, "arguments.json"); SeedPathPolicy.RequireExistingNoReparse(arguments);
        if (new FileInfo(arguments).Length > 4096) throw new InvalidOperationException("CLI arguments too large.");
        using var request = JsonDocument.Parse(File.ReadAllBytes(arguments)); var argv = request.RootElement.GetProperty("argv");
        if (argv.GetArrayLength() != 1 || argv[0].GetString() != "capabilities") throw new InvalidOperationException("Cache path-only probe requires resource-free capabilities context.");
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
        var assemblyPath = Path.Combine(context.InstallRoot, "BepInEx", "interop", "Assembly-CSharp.dll");
        if (!Same(assembly.Location, assemblyPath)) throw new InvalidOperationException("CLI interop loaded from another installation.");
        PinContract.RequireFileHash(assemblyPath, "d95062f3a49fba645986dc7fe0cee749197c9d0c84ff381fc3ca2d9d2823c013");
        // Read the actual generated native backing-field accessor, not a fabricated readiness flag or patched IsActive method.
        var type = assembly.GetType("AzureArchive.Automation.AuthoringCliHost", throwOnError: true)!;
        var active = type.GetProperty("_IsActive_k__BackingField", BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMemberException("Pinned native CLI field accessor is missing.");
        if (active.GetValue(null) is not true) throw new InvalidOperationException("Actual native CLI IsActive is false.");
    }
    internal static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void AbortOwnedChild()
    {
        TerminateProcess(GetCurrentProcess(), 86);
        Environment.FailFast("Owned cache-probe abort unexpectedly returned.");
        throw new InvalidOperationException("Unreachable.");
    }
}
