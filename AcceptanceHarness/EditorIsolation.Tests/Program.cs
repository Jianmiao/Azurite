using Azurite.EditorIsolation;
using System.Text.Json;
using System.Runtime.InteropServices;

if (Fix1Tests.TryRunChild(args, out var childResult)) return childResult;

if (args.Length == 2 && args[0] == "--abort-before-marker")
{
    try { OwnProcessAbort.Terminate(86); } catch { }
    File.WriteAllText(args[1], "FAILED: plugin exception swallowed and execution continued");
    return 0;
}

if (args.Length != 1) throw new Exception("Pass an existing workspace-owned test output directory.");
var root = Path.Combine(Path.GetFullPath(args[0]), "policy-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var paths = new RunPaths(root, Path.Combine(root, "run"), Path.Combine(root, "install"));
foreach (var p in new[] { paths.Run, paths.Install, Path.GetDirectoryName(paths.Settings)!, paths.Workspace, paths.Cache, paths.Persistent, paths.Discovery, paths.Output }) Directory.CreateDirectory(p);
File.WriteAllText(paths.Settings, "seed"); File.WriteAllText(paths.Control, "control");
int failed = 0, passed = 0;
void Test(string name, Action body) { try { body(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; } }
void Check(bool b, string why) { if (!b) throw new Exception(why); }
void Deny(Action a) { try { a(); } catch (InvalidOperationException) { return; } throw new Exception("unsafe action admitted"); }
ProbeLedger Ready() { var s = new ProbeLedger(ProbeMode.RouteSelfTest); s.Arm(true, true); return s; }
Test("canonical existing separate lab roots admitted", () => paths.Validate());
Test("sibling prefix escape rejected", () => Deny(() => paths.RequireContained(paths.Run + "-escape\\x")));
Test("parent traversal rejected rather than normalized", () => Deny(() => paths.RequireContained(Path.Combine(paths.Run, "workspace", "..", "settings", "x"))));
Test("relative path rejected", () => Deny(() => paths.RequireContained("settings\\user_settings.json")));
Test("alternate stream rejected", () => Deny(() => paths.RequireContained(paths.Settings + ":hidden")));
Test("wrong in-run write target rejected", () => Deny(() => paths.RequireSettingsTarget(paths.Control)));
Test("exact settings target admitted", () => paths.RequireSettingsTarget(paths.Settings));
Test("run inside installation rejected", () => Deny(() => new RunPaths(root, paths.Install, paths.Install).Validate()));
Test("reparse ancestor rejected", () => { var link = Path.Combine(root, "junction"); var target = Path.Combine(root, "outside"); Directory.CreateDirectory(target); var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }); p!.WaitForExit(); Check(p.ExitCode == 0, "junction fixture failed"); Deny(() => new RunPaths(root, link, paths.Install).Validate()); });
Test("selftest rejects CLI inactive", () => Deny(() => new ProbeLedger(ProbeMode.RouteSelfTest).Arm(false, true)));
Test("late hook install rejected", () => Deny(() => new ProbeLedger(ProbeMode.RouteSelfTest).Arm(true, false)));
Test("normal mode remains closed pending independent review", () => Deny(() => new ProbeLedger(ProbeMode.NormalObserverCatalogOnly).Arm(false, true)));
Test("write without original read evidence rejected", () => Deny(() => Ready().AdmitWrite(10)));
Test("wrong iterator state rejected", () => Deny(() => Ready().ReadPrefix(10, 1)));
Test("settings read fallback yield rejected", () => { var s = Ready(); s.ReadPrefix(10, 0); Deny(() => s.ReadCompleted(10, true, "lab", "lab")); });
Test("managed native-hit miss cannot satisfy route proof", () => Deny(() => Ready().ReadCompleted(10, false, "lab", "lab")));
Test("miss control marker rejected", () => { var s = Ready(); s.ReadPrefix(10, 0); Deny(() => s.ReadCompleted(10, false, "control", "lab")); });
Test("owner identity mismatch rejected", () => { var s = Ready(); s.ReadPrefix(10, 0); s.ReadCompleted(10, false, "lab", "lab"); Deny(() => s.AdmitWrite(11)); });
Test("read success alone cannot claim saved route", () => { var s = Ready(); s.ReadPrefix(10, 0); s.ReadCompleted(10, false, "lab", "lab"); Check(!s.RouteProofComplete, "premature completion"); });
Test("original read then admitted completed write satisfies route", () => { var s = Ready(); Check(s.ReadPrefix(10, 0) == 1, "missing sequence"); s.ReadCompleted(10, false, "lab", "lab"); s.AdmitWrite(10); s.WriteCompleted(10); Check(s.RouteProofComplete, "route incomplete"); });
Test("write completion without prefix rejected", () => { var s = Ready(); s.ReadPrefix(10, 0); s.ReadCompleted(10, false, "lab", "lab"); Deny(() => s.WriteCompleted(10)); });
Test("failure latch closes future writes and completion", () => { var s = Ready(); s.ReadPrefix(10, 0); s.ReadCompleted(10, false, "lab", "lab"); s.Fail("mismatch"); Deny(() => s.AdmitWrite(10)); Check(!s.RouteProofComplete, "failure reported success"); });
var id = Guid.NewGuid().ToString("D");
Dictionary<string, object> Seed(string marker) => new() { ["stEnabled"] = false, ["catalogUrlOverride"] = "", ["masterVolume"] = 1, ["voiceVolume"] = 1, ["bgmVolume"] = 1, ["soundEffectVolume"] = 1, ["lastProfileIndex"] = 0, ["username"] = marker, ["goldText"] = "0", ["diamondText"] = "0", ["staminaText"] = "0", ["autoSave"] = false, ["autoCompile"] = false, ["lobbyBgmEnabled"] = false, ["autoDelay"] = 0, ["fpsTier"] = 0, ["checkForUpdate"] = false, ["isAutoPlay"] = false, ["fontIndex"] = 0, ["visitedFiles"] = Array.Empty<string>(), ["favoriteBgmIds"] = Array.Empty<string>(), ["favoriteSoundNames"] = Array.Empty<string>(), ["completeResStructVer"] = 0, ["customSource"] = "", ["cachePath"] = paths.Cache, ["workspacePath"] = paths.Workspace };
File.WriteAllText(paths.Settings, JsonSerializer.Serialize(Seed("lab-read-" + id)));
File.WriteAllText(paths.Control, JsonSerializer.Serialize(Seed("miss-control-" + id)));
var manifest = new RunManifest(1, id, "route-selftest", root, paths.Run, paths.Install, RunManifest.Sha256(paths.Settings), RunManifest.Sha256(paths.Control));
var manifestPath = Path.Combine(paths.Run, "manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
Test("complete matching manifest and sanitized seeds admitted", () => manifest.Validate(manifestPath));
Test("manifest located outside declared run rejected", () => Deny(() => manifest.Validate(Path.Combine(root, "manifest.json"))));
Test("unknown manifest version rejected", () => Deny(() => (manifest with { Version = 2 }).Validate(manifestPath)));
Test("manifest cannot relabel daily installation as lab", () => Deny(() => HostContract.RequireApprovedRun(manifest with { LabRoot = "F:\\", InstallRoot = "F:\\AzureArchive_101", RunRoot = "F:\\runs\\" + id })));
Test("fixed copied lab and exact GUID run admitted", () => HostContract.RequireApprovedRun(manifest with { LabRoot = @"E:\aamod\labs\Azurite-20261108-editor", InstallRoot = @"E:\aamod\labs\Azurite-20261108-editor\install", RunRoot = @"E:\aamod\labs\Azurite-20261108-editor\runs\" + id }));
Test("tampered seed hash rejected", () => Deny(() => RunManifest.ValidateSeed(paths.Settings, new string('0', 64), paths, "lab-read-" + id)));
Test("seed missing native field rejected", () => { var data = Seed("lab-read-" + id); data.Remove("visitedFiles"); var file = Path.Combine(paths.Run, "missing.json"); File.WriteAllText(file, JsonSerializer.Serialize(data)); Deny(() => RunManifest.ValidateSeed(file, RunManifest.Sha256(file), paths, "lab-read-" + id)); });
Test("seed with daily visited path rejected", () => { var data = Seed("lab-read-" + id); data["visitedFiles"] = new[] { "C:\\daily.aap2" }; var file = Path.Combine(paths.Run, "visited.json"); File.WriteAllText(file, JsonSerializer.Serialize(data)); Deny(() => RunManifest.ValidateSeed(file, RunManifest.Sha256(file), paths, "lab-read-" + id)); });
Test("seed cache disagreement rejected", () => { var data = Seed("lab-read-" + id); data["cachePath"] = paths.Workspace; var file = Path.Combine(paths.Run, "wrong-cache.json"); File.WriteAllText(file, JsonSerializer.Serialize(data)); Deny(() => RunManifest.ValidateSeed(file, RunManifest.Sha256(file), paths, "lab-read-" + id)); });
Test("seed PCM muting rejected; NoSound is the silence boundary", () => { var data = Seed("lab-read-" + id); data["masterVolume"] = 0; var file = Path.Combine(paths.Run, "audio.json"); File.WriteAllText(file, JsonSerializer.Serialize(data)); Deny(() => RunManifest.ValidateSeed(file, RunManifest.Sha256(file), paths, "lab-read-" + id)); });
Test("single-link seed admitted", () => WindowsFileIdentity.RequireSingleLink(paths.Settings));
Test("long-path hardlink fixture preserves real shared file identity", () =>
{
    var directory = root;
    while (directory.Length < 280) directory = Path.Combine(directory, "long-hardlink-fixture");
    Directory.CreateDirectory(directory);
    var source = Path.Combine(directory, "source.txt"); var alias = Path.Combine(directory, "alias.txt");
    File.WriteAllText(source, "before");
    Check(NativeFixtures.CreateHardLink(alias, source, IntPtr.Zero), "long-path hardlink fixture failed");
    File.WriteAllText(alias, "written through real alias");
    Check(File.ReadAllText(source) == "written through real alias", "fixture did not share file identity");
    Deny(() => WindowsFileIdentity.RequireSingleLink(alias));
});
Fix1Tests.Run(root, paths, manifest, manifestPath, Test, Check, Deny);
Test("hardlinked seed rejected before content read", () => { var alias = Path.Combine(paths.Run, "linked.json"); Check(NativeFixtures.CreateHardLink(alias, paths.Control, IntPtr.Zero), "hardlink fixture failed"); Deny(() => WindowsFileIdentity.RequireSingleLink(alias)); });
Test("own-process abort bypasses swallowed plugin exceptions", () =>
{
    var marker = Path.Combine(paths.Output, "must-not-exist.txt");
    var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("--abort-before-marker"); start.ArgumentList.Add(marker);
    using var child = System.Diagnostics.Process.Start(start)!;
    Check(child.WaitForExit(5000), "abort child timed out");
    Check(child.ExitCode == 86 && !File.Exists(marker), "abort returned into swallowed exception/following write");
});
var routeIds = new[] { "awake", "read", "write", "fallback", "cache", "workspace", "resources", "discovery", "offline", "addressSet", "addressClear", "addressEvaluate" };
Test("missing plugin leaves startup guard closed", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); Deny(() => gate.Complete(null)); });
Test("partial routing set leaves startup guard closed", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); Deny(() => gate.RoutesReady("run-hash", new[] { "read", "write" })); });
Test("different manifest cannot satisfy startup guard", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); Deny(() => gate.RoutesReady("other-hash", routeIds)); });
Test("probe cannot proceed without installed startup guard", () => Deny(() => new StartupGuardState().RequireArmed("run-hash")));
Test("all native routes register before successful Execute completion", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); gate.RequireArmed("run-hash"); gate.RoutesReady("run-hash", routeIds); gate.Complete(null); });
Test("Execute exception remains fatal after routes registered", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); gate.RoutesReady("run-hash", routeIds); Deny(() => gate.Complete(new Exception("loader error"))); });
Test("native Awake before Execute completion rejected", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); gate.RoutesReady("run-hash", routeIds); Deny(() => gate.RequireCompleted("run-hash")); });
Test("native Awake after guarded Execute completion admitted", () => { var gate = new StartupGuardState(); gate.Arm("run-hash"); gate.RoutesReady("run-hash", routeIds); gate.Complete(null); gate.RequireCompleted("run-hash"); });
Console.WriteLine($"RESULT passed={passed} failed={failed}; evidenceRoot={root}");
return failed == 0 ? 0 : 1;

internal static class NativeFixtures
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkNative(string path, string existing, IntPtr security);
    internal static bool CreateHardLink(string path, string existing, IntPtr security)
    {
        var nativePath = NativeFixturePath(path); var nativeExisting = NativeFixturePath(existing);
        if (!CreateHardLinkNative(nativePath, nativeExisting, security))
        {
            var error = Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(error, $"CreateHardLinkW fixture failed: Win32={error}; native destination chars={nativePath.Length}; native existing chars={nativeExisting.Length}");
        }
        return true;
    }
    private static string NativeFixturePath(string path)
    {
        // Only canonical local drive paths are supported by this test helper.
        if (path.Length < 3 || path[1] != ':' || path[2] != '\\' || !Path.IsPathFullyQualified(path) || path != Path.GetFullPath(path))
            throw new ArgumentException("Hardlink fixtures require canonical local drive-absolute paths.", nameof(path));
        return @"\\?\" + path;
    }
}
