using System.Diagnostics;
using System.Text.Json;
using Azurite.EditorIsolation;

internal static class Fix1Tests
{
    internal static bool TryRunChild(string[] args, out int result)
    {
        result = 0;
        if (args.Length != 3 || args[0] != "--invalid-manifest-receipt-child") return false;
        CorrelatedEvidence? evidence = null;
        try
        {
            var bound = BoundManifest.ReadAt(args[1], args[2]);
            evidence = new CorrelatedEvidence(bound, "editor-isolation-guardian.jsonl");
            _ = bound.Parse();
        }
        catch (Exception e)
        {
            try { evidence?.Emit("guardian-setup-failed", new { reason = e.GetType().Name, nativeStartupMayResume = false }); evidence?.Flush(); } catch { }
            OwnProcessAbort.Terminate(86);
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[2])!, "must-not-continue.txt"), "unexpected continuation");
        return true;
    }

    internal static void Run(string root, RunPaths paths, RunManifest manifest, string manifestPath,
        Action<string, Action> test, Action<bool, string> check, Action<Action> deny)
    {
        var install = Path.Combine(HostContract.ApprovedLabRoot, "install");
        const string guardianRelative = @"BepInEx\patchers\AzuriteEditorIsolation\Azurite.EditorIsolation.Guard.LabOnly.dll";
        var executable = Path.Combine(install, "AzureArchive.exe"); var guardian = Path.Combine(install, guardianRelative);
        test("loaded lab guardian rejects absent manifest", () => deny(() => LabEntryAdmission.ShouldActivate(null, executable, guardian, guardianRelative)));
        test("loaded lab guardian rejects empty manifest", () => deny(() => LabEntryAdmission.ShouldActivate("", executable, guardian, guardianRelative)));
        test("outside lab absent manifest is explicitly unprotected/inert", () => check(!LabEntryAdmission.ShouldActivate(null, @"C:\outside\AzureArchive.exe", @"C:\outside\guard.dll", guardianRelative), "unexpected protection claim"));
        test("lab executable cannot silently ignore misplaced guardian", () => deny(() => LabEntryAdmission.ShouldActivate(null, executable, @"C:\outside\guard.dll", guardianRelative)));
        test("outside executable cannot use explicit lab manifest", () => deny(() => LabEntryAdmission.ShouldActivate("requested", @"C:\outside\AzureArchive.exe", guardian, guardianRelative)));

        var backup = paths.Settings + ".bk.1";
        test("initial manifest admission rejects ordinary SafeSave backup", () =>
        {
            File.WriteAllText(backup, "preexisting");
            try { deny(() => manifest.Validate(manifestPath)); } finally { File.Delete(backup); }
        });
        test("unhooked native-write dispatch is stopped by preflight", () =>
        {
            var sentinel = Path.Combine(root, "unhooked-write-sentinel.txt"); File.WriteAllText(sentinel, "untouched");
            File.WriteAllText(backup, "preexisting");
            try { deny(() => SettingsWriteAdmission.Dispatch(paths, () => File.WriteAllText(sentinel, "changed"))); check(File.ReadAllText(sentinel) == "untouched", "unhooked writer ran"); }
            finally { File.Delete(backup); }
        });
        test("artifact-only hardlinked bk1 rejected before any native dispatch", () =>
        {
            var sentinel = Path.Combine(root, "outside-run-sentinel.txt"); File.WriteAllText(sentinel, "retained evidence");
            check(NativeFixtures.CreateHardLink(backup, sentinel, IntPtr.Zero), "cannot create scoped hardlink fixture");
            try { deny(() => manifest.Validate(manifestPath)); deny(() => SettingsWriteAdmission.Dispatch(paths, () => File.WriteAllText(sentinel, "changed"))); check(File.ReadAllText(sentinel) == "retained evidence", "sentinel changed"); }
            finally { File.Delete(backup); }
        });
        test("reparse sidecar rejected by independent settings preflight", () =>
        {
            var target = Path.Combine(root, "sidecar-target"); Directory.CreateDirectory(target);
            using var child = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{backup}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
            child.WaitForExit(); check(child.ExitCode == 0, "cannot create scoped junction fixture");
            try { deny(() => SettingsWriteAdmission.ValidateFresh(paths)); } finally { Directory.Delete(backup); }
        });
        test("fresh settings preflight admits its actual write callback", () =>
        {
            var marker = Path.Combine(paths.Output, "admitted-write.txt");
            SettingsWriteAdmission.Dispatch(paths, () => File.WriteAllText(marker, "admitted"));
            check(File.ReadAllText(marker) == "admitted", "valid dispatch was stubbed out");
        });

        (string Lab, string Run, string Path, string Id) NewReceiptRun(string json)
        {
            var lab = Path.Combine(root, "receipt-lab"); var id = Guid.NewGuid().ToString("D"); var run = Path.Combine(lab, "runs", id);
            Directory.CreateDirectory(Path.Combine(run, "output")); var path = Path.Combine(run, "manifest.json"); File.WriteAllText(path, json);
            return (lab, run, path, id);
        }
        test("writer binds actual pid location run and raw hash despite nested tampering", () =>
        {
            var f = NewReceiptRun("{"); var bound = BoundManifest.ReadAt(f.Lab, f.Path);
            using (var writer = new CorrelatedEvidence(bound, "editor-isolation-guardian.jsonl")) writer.Emit("test", new { pid = -1, runId = "tampered", manifestSha256 = new string('0', 64) });
            using var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(f.Run, "output", "editor-isolation-guardian.jsonl")));
            var e = item.RootElement;
            check(e.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "sequence", "utc", "monotonicTicks", "runId", "pid", "manifestSha256", "kind", "data" }), "event envelope drift");
            check(e.GetProperty("runId").GetString() == f.Id && e.GetProperty("pid").GetInt32() == Environment.ProcessId && e.GetProperty("manifestSha256").GetString() == RunManifest.Sha256(f.Path), "event identity came from untrusted payload");
            check(e.GetProperty("sequence").GetInt64() == 1 && e.GetProperty("monotonicTicks").GetInt64() > 0 && e.GetProperty("utc").GetDateTime().Kind == DateTimeKind.Utc, "invalid clock/sequence envelope");
        });
        test("manifest content cannot override location-bound run identity", () =>
        {
            var f = NewReceiptRun(JsonSerializer.Serialize(manifest)); var bound = BoundManifest.ReadAt(f.Lab, f.Path); deny(() => bound.Parse());
        });
        test("early manifest envelope rejects non-GUID run directory", () =>
        {
            var f = NewReceiptRun("{"); var other = Path.Combine(f.Lab, "runs", "not-a-guid"); Directory.CreateDirectory(Path.Combine(other, "output")); var path = Path.Combine(other, "manifest.json"); File.WriteAllText(path, "{");
            deny(() => BoundManifest.ReadAt(f.Lab, path));
        });
        test("invalid JSON writes correlated abort receipt in harmless owned child", () =>
        {
            var f = NewReceiptRun("{"); var hash = RunManifest.Sha256(f.Path);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { typeof(Fix1Tests).Assembly.Location, "--invalid-manifest-receipt-child", f.Lab, f.Path }) start.ArgumentList.Add(a);
            using var child = Process.Start(start)!; check(child.WaitForExit(5000), "receipt child timed out");
            check(child.ExitCode == 86 && !File.Exists(Path.Combine(f.Run, "must-not-continue.txt")), "invalid JSON continued");
            using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(f.Run, "output", "editor-isolation-guardian.jsonl")));
            var e = receipt.RootElement;
            check(e.GetProperty("runId").GetString() == f.Id && e.GetProperty("pid").GetInt32() == child.Id && e.GetProperty("manifestSha256").GetString() == hash && e.GetProperty("kind").GetString() == "guardian-setup-failed" && !e.GetProperty("data").GetProperty("nativeStartupMayResume").GetBoolean(), "abort receipt is not bound to child/run/raw manifest");
        });
    }
}
