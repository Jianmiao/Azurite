using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azurite.NormalEditor;
using Azurite.EditorIsolation;
using Azurite.NormalEditor.Entry;
using Azurite.NormalEditor.Entry.Evidence;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || !args[0].StartsWith("C:\\", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Supply an explicit C-only output directory. Fixtures are retained.");
Directory.CreateDirectory(args[0]);
var root = Path.Combine(args[0], "file-fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var identity = new CatalogRunIdentity(new("3ca4b8f4-687b-4210-a2d2-21ca586d5637", 456, 1, 42, 1000),
    new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 13, 0, 0, 1, TimeSpan.Zero));
var paths = new RunPaths(root, Path.Combine(root, "runs", identity.Binding.RunId), Path.Combine(root, "install"));
Directory.CreateDirectory(paths.Output);
Directory.CreateDirectory(paths.Install);
var binding = new NormalBinding(identity.Binding.RunId, 1, 456, 42, paths, new string('a', 64), new string('b', 64), "controlled-format-fixture" );
CatalogEvidenceOrigin Origin(string path) => new(paths.Output, path);
var passed = 0;
var failed = 0;
void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Test(string name, Action check) { try { check(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
string FilePath(string name) => Path.Combine(paths.Output, name + ".jsonl");
string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
ExternalCatalogCloseProof Proof(string path) => new(identity, Origin(path), 100, 789, identity.ProcessStartUtc.AddSeconds(-2),
    identity.ObserverStartedUtc.AddSeconds(1), identity.ObserverStartedUtc.AddSeconds(2), identity.ObserverStartedUtc.AddSeconds(3),
    5000, 0, true, false, false, false, new FileInfo(path).Length, Digest(File.ReadAllBytes(path)));
void Observe(Action<CatalogEvidence> record)
{
    // Controlled input to the actual Core coordinator. This is file-format
    // qualification only; these values are never runtime admission evidence.
    var gate = new CatalogReadiness(identity.Binding, record);
    CatalogStamp Stamp(long frame, long ms) => new(identity.Binding.RunId, 456, 1, 42, frame, ms, 100);
    var start = gate.StartEntered(Stamp(10, 10));
    gate.StartReturned(start, Stamp(10, 11), true);
    gate.StartFinalized(start, Stamp(10, 12), null);
    var observation = new CatalogObservation(Stamp(11, 20), 200, 300, 400, "CatalogScene", false, false, true, true, false, 0, 0, false);
    gate.Tick(observation, () => observation);
    Need(gate.Ready, "Controlled coordinator did not become ready: " + gate.Failure);
}
string Complete(string name)
{
    var path = FilePath(name);
    using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding)) { Observe(writer.Record); writer.Seal(); }
    return path;
}
void Reject(string path, ExternalCatalogCloseProof? proof, string reason)
{
    var result = CatalogEvidenceConsumer.Evaluate(path, identity, Origin(path), proof);
    Need(!result.EligibleForCatalogOnlyEvidence && result.Reason.Contains(reason, StringComparison.OrdinalIgnoreCase), "Unexpected assessment: " + result);
}
List<CatalogEvidence> Records() { var records = new List<CatalogEvidence>(); Observe(records.Add); return records; }
void Rewrite(string path, Action<List<JsonNode>> change, bool refreshTerminal = true)
{
    var rows = File.ReadAllLines(path).Select(s => JsonNode.Parse(s)!).ToList(); change(rows);
    if (refreshTerminal) {
        var terminal = rows.FirstOrDefault(n => (string?)n["Kind"] == "sealed");
        if (terminal is not null) {
            var payload = string.Concat(rows.TakeWhile(n => !ReferenceEquals(n, terminal)).Select(n => n.ToJsonString() + "\n"));
            terminal["PayloadSha256"] = Digest(Encoding.UTF8.GetBytes(payload));
        }
    }
    File.WriteAllText(path, string.Concat(rows.Select(n => n.ToJsonString() + "\n")), new UTF8Encoding(false));
}

Test("record callback flushes complete UTF8 LF records before returning", () => {
    var path = FilePath("synchronous-records");
    using var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding);
    Observe(writer.Record);
    using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var text = new StreamReader(reader, new UTF8Encoding(false, true));
    var contents = text.ReadToEnd();
    Need(contents.EndsWith('\n') && contents.Split('\n').Length == 6 && contents.Contains("catalog-ready") && !contents.Contains('\r'), "Complete header plus four records were not visible synchronously.");
});
Test("CreateNew cannot overwrite an existing evidence file", () => {
    var path = FilePath("existing"); File.WriteAllText(path, "preserve me");
    var rejected = false; try { using var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding); } catch (IOException) { rejected = true; }
    Need(rejected && File.ReadAllText(path) == "preserve me", "Existing file was accepted or changed.");
});
Test("Dispose does not seal or accept isolated ready records", () => {
    var path = FilePath("ready-without-seal"); using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding)) Observe(writer.Record);
    Reject(path, Proof(path), "terminal");
});
Test("sealed ready without external close proof remains ineligible", () => {
    var path = Complete("missing-proof"); Reject(path, null, "external close");
});
Test("complete journal and fully bound external input are structurally eligible only", () => {
    var path = FilePath("complete"); CatalogJournalSeal seal;
    using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding)) { Observe(writer.Record); seal = writer.Seal(); }
    Need(seal.FileBytes == new FileInfo(path).Length && seal.Sha256 == Digest(File.ReadAllBytes(path)) && seal.RecordCount == 4, "Seal does not describe durable file bytes.");
    var result = CatalogEvidenceConsumer.Evaluate(path, identity, Origin(path), Proof(path));
    Need(result.EligibleForCatalogOnlyEvidence && result.ReadyRecordSeen, "Complete format fixture not eligible: " + result.Reason);
});
foreach (var path in new[] { "relative.jsonl", "F:\\forbidden.jsonl", "\\\\server\\share\\forbidden.jsonl", "\\\\?\\C:\\forbidden.jsonl", FilePath("ads") + ":stream", Path.Combine(root, "..", "escape.jsonl"), FilePath("NUL"), FilePath("COM1") })
    Test("writer rejects noncanonical or unbound route " + path, () => {
        var rejected = false; try { using var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding); } catch (ArgumentException) { rejected = true; }
        Need(rejected, "Disallowed path accepted.");
    });
Test("writer bounds one record and latches rejection before seal", () => {
    var path = FilePath("oversize-record"); using var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding, new(1024, 16384, 64));
    var record = Records()[0] with { Kind = "catalog-failed", Failure = new string('x', 2000) };
    var rejected = false; try { writer.Record(record); } catch (IOException) { rejected = true; }
    Need(rejected, "Oversized record accepted.");
    rejected = false; try { writer.Seal(); } catch (InvalidOperationException) { rejected = true; }
    Need(rejected, "Failed writer sealed.");
});
Test("writer bounds total bytes and leaves an incomplete rejected journal", () => {
    var path = FilePath("total-bound"); var rejected = false;
    using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding, new(1024, 1200, 64))) {
        try { foreach (var record in Records()) writer.Record(record); writer.Seal(); } catch (IOException) { rejected = true; }
    }
    Need(rejected && new FileInfo(path).Length <= 1200, "Total byte bound not enforced."); Reject(path, Proof(path), "terminal");
});
Test("writer bounds record count and preserves failed partial file", () => {
    var path = FilePath("count-bound"); var rejected = false;
    using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding, new(8192, 262144, 3))) {
        try { foreach (var record in Records()) writer.Record(record); } catch (IOException) { rejected = true; }
    }
    Need(rejected && File.ReadAllLines(path).Length == 4, "Record count bound not enforced."); Reject(path, Proof(path), "terminal");
});
Test("an append after seal poisons the terminal instead of hiding a late failure", () => {
    var path = FilePath("after-seal"); var rejected = false;
    using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding)) {
        Observe(writer.Record); writer.Seal();
        try { writer.Record(Records()[3] with { Sequence = 5, Kind = "catalog-failed", Observation = null, Failure = "late observer failure" }); } catch (InvalidOperationException) { rejected = true; }
    }
    Need(rejected, "Write after seal accepted."); Reject(path, Proof(path), "terminal");
});
Test("later catalog-failed overrides a durable ready record", () => {
    var path = FilePath("failure-after-ready"); using (var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding)) {
        Observe(writer.Record); writer.Record(Records()[3] with { Sequence = 5, Kind = "catalog-failed", Observation = null, Failure = "late failure" }); writer.Seal();
    }
    Reject(path, Proof(path), "catalog-failed");
});
Test("active writer file cannot be consumed as final evidence", () => {
    var path = FilePath("active-writer"); using var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding); Observe(writer.Record); writer.Seal();
    Reject(path, null, "read");
});
Test("disposed evidence sink makes the actual coordinator fail", () => {
    var path = FilePath("disposed-sink"); var writer = CatalogEvidenceWriter.CreateNew(path, identity, binding); writer.Dispose();
    var gate = new CatalogReadiness(identity.Binding, writer.Record); var stamp = Records()[0].Stamp;
    gate.StartEntered(stamp); Need(!gate.Ready && gate.Failure is not null, "Disposed sink accepted a callback."); Reject(path, Proof(path), "terminal");
});

var mutations = new (string Name, Action<List<JsonNode>> Change, string Reason)[] {
    ("duplicate sequence", r => r[2]["Evidence"]!["Sequence"] = 1, "sequence"),
    ("sequence gap", r => r[2]["Evidence"]!["Sequence"] = 3, "sequence"),
    ("mixed envelope run", r => r[2]["Evidence"]!["RunId"] = "2ca4b8f4-687b-4210-a2d2-21ca586d5637", "identity"),
    ("mixed stamp run", r => r[2]["Evidence"]!["Stamp"]!["RunId"] = "2ca4b8f4-687b-4210-a2d2-21ca586d5637", "identity"),
    ("mixed PID", r => r[2]["Evidence"]!["Stamp"]!["ProcessId"] = 457, "identity"),
    ("mixed managed thread", r => r[2]["Evidence"]!["Stamp"]!["ManagedThreadId"] = 2, "identity"),
    ("mixed native thread", r => r[2]["Evidence"]!["Stamp"]!["NativeThreadId"] = 43, "identity"),
    ("mixed catalog owner", r => r[2]["Evidence"]!["Stamp"]!["CatalogOwner"] = 101, "owner"),
    ("zero catalog owner", r => r[1]["Evidence"]!["Stamp"]!["CatalogOwner"] = 0, "owner"),
    ("clock reversal", r => r[2]["Evidence"]!["Stamp"]!["Milliseconds"] = 9, "clock"),
    ("frame reversal", r => r[2]["Evidence"]!["Stamp"]!["Frame"] = 9, "clock"),
    ("readiness deadline", r => { r[4]["Evidence"]!["Stamp"]!["Milliseconds"] = 1001; r[4]["Evidence"]!["Observation"]!["Stamp"]!["Milliseconds"] = 1001; }, "deadline"),
    ("same frame ready", r => { r[4]["Evidence"]!["Stamp"]!["Frame"] = 10; r[4]["Evidence"]!["Observation"]!["Stamp"]!["Frame"] = 10; }, "later frame"),
    ("missing lifecycle return", r => { r.RemoveAt(2); for (var i = 1; i < 4; i++) r[i]["Evidence"]!["Sequence"] = i; r[^1]["RecordCount"] = 3; }, "lifecycle"),
    ("unknown event", r => r[2]["Evidence"]!["Kind"] = "catalog-made-up", "lifecycle"),
    ("ready contains failure", r => r[4]["Evidence"]!["Failure"] = "failure", "failure"),
    ("ready has no observation", r => r[4]["Evidence"]!["Observation"] = null, "observation"),
    ("ready observation stamp diverges", r => r[4]["Evidence"]!["Observation"]!["Stamp"]!["ProcessId"] = 999, "observation"),
    ("foreign process start", r => r[0]["Identity"]!["ProcessStartUtc"] = "2026-11-13T00:00:00.0000001+00:00", "identity"),
    ("non UTC header process start", r => r[0]["Identity"]!["ProcessStartUtc"] = "2026-11-13T01:00:00+01:00", "identity"),
    ("duplicate terminal", r => r.Add(JsonNode.Parse(r[^1].ToJsonString())!), "terminal"),
    ("terminal count mismatch", r => r[^1]["RecordCount"] = 3, "count"),
    ("unknown schema", r => r[0]["SchemaVersion"] = 2, "schema"),
    ("unknown JSON field", r => r[2]["Unexpected"] = "untrusted", "canonical")
};
foreach (var item in mutations) Test("consumer rejects " + item.Name, () => {
    var path = Complete(item.Name.Replace(' ', '-')); Rewrite(path, item.Change); Reject(path, Proof(path), item.Reason);
});
foreach (var field in new[] { "CliActive", "TransportPresent", "Preloading", "ControlOpening" })
    Test("consumer rejects ready " + field, () => { var path = Complete(field); Rewrite(path, r => r[4]["Evidence"]!["Observation"]![field] = true); Reject(path, Proof(path), "ready predicates"); });
foreach (var field in new[] { "SettingsReady", "ResourcesReady" })
    Test("consumer rejects unready " + field, () => { var path = Complete(field); Rewrite(path, r => r[4]["Evidence"]!["Observation"]![field] = false); Reject(path, Proof(path), "ready predicates"); });
foreach (var field in new[] { "WorkbenchOwner", "SettingsOwner", "ResourceOwner" })
    Test("consumer rejects absent " + field, () => { var path = Complete(field); Rewrite(path, r => r[4]["Evidence"]!["Observation"]![field] = 0); Reject(path, Proof(path), "ready predicates"); });
foreach (var field in new[] { "LoadingCount", "CurrentSession" })
    Test("consumer rejects active " + field, () => { var path = Complete(field); Rewrite(path, r => r[4]["Evidence"]!["Observation"]![field] = 1); Reject(path, Proof(path), "ready predicates"); });
Test("consumer rejects wrong scene", () => { var path = Complete("wrong-scene"); Rewrite(path, r => r[4]["Evidence"]!["Observation"]!["Scene"] = "EditorScene"); Reject(path, Proof(path), "ready predicates"); });

var closeChanges = new (string Name, Func<ExternalCatalogCloseProof, ExternalCatalogCloseProof> Change)[] {
    ("foreign run", p => p with { Identity = identity with { Binding = identity.Binding with { RunId = Guid.NewGuid().ToString("D") } } }),
    ("reused PID start time", p => p with { Identity = identity with { ProcessStartUtc = identity.ProcessStartUtc.AddTicks(1) } }),
    ("non UTC target process start", p => p with { Identity = identity with { ProcessStartUtc = identity.ProcessStartUtc.ToOffset(TimeSpan.FromHours(1)) } }),
    ("foreign owner", p => p with { CatalogOwner = 101 }),
    ("in-process monitor", p => p with { MonitorProcessId = identity.Binding.ProcessId }),
    ("no monitor", p => p with { MonitorProcessId = 0 }),
    ("no monitor start", p => p with { MonitorProcessStartUtc = default }),
    ("late monitor start", p => p with { MonitorProcessStartUtc = p.CloseRequestedUtc.AddSeconds(1) }),
    ("close before ready", p => p with { CloseRequestedUtc = identity.ObserverStartedUtc }),
    ("exit before close", p => p with { ExitObservedUtc = p.CloseRequestedUtc.AddTicks(-1) }),
    ("digest observation before exit", p => p with { EvidenceObservedUtc = p.ExitObservedUtc.AddTicks(-1) }),
    ("deadline exceeded", p => p with { ExitObservedUtc = p.CloseRequestedUtc.AddSeconds(6), EvidenceObservedUtc = p.CloseRequestedUtc.AddSeconds(7) }),
    ("unknown timeout", p => p with { CloseTimeoutMilliseconds = 0 }),
    ("nonzero exit", p => p with { ExitCode = 1 }),
    ("normal close absent", p => p with { NormalCloseRequestSucceeded = false }),
    ("timeout observed", p => p with { TimedOut = true }),
    ("forced termination", p => p with { ForceTerminated = true }),
    ("observer failure", p => p with { ObserverFailureObserved = true }),
    ("whole file digest differs", p => p with { EvidenceSha256 = new string('a', 64) }),
    ("whole file length differs", p => p with { EvidenceBytes = p.EvidenceBytes - 1 })
};
foreach (var item in closeChanges) Test("external proof rejects " + item.Name, () => {
    var path = Complete("proof-" + item.Name.Replace(' ', '-')); Reject(path, item.Change(Proof(path)), "external close");
});
Test("consumer rejects byte truncation", () => { var path = Complete("truncated"); var b = File.ReadAllBytes(path); File.WriteAllBytes(path, b[..^1]); Reject(path, Proof(path), "truncated"); });
Test("consumer rejects terminal digest corruption", () => { var path = Complete("digest-corrupt"); Rewrite(path, r => r[^1]["PayloadSha256"] = new string('b', 64), false); Reject(path, Proof(path), "digest"); });
Test("consumer rejects invalid UTF8", () => { var path = Complete("utf8"); var b = File.ReadAllBytes(path); b[2] = 255; File.WriteAllBytes(path, b); Reject(path, Proof(path), "UTF8"); });
Test("consumer rejects appended failure bytes", () => { var path = Complete("appended-bytes"); File.AppendAllText(path, "{\"Kind\":\"catalog-failed\"}\n"); Reject(path, Proof(path), "terminal"); });
Test("consumer bounds file before decoding", () => { var path = FilePath("reader-total"); File.WriteAllBytes(path, new byte[262145]); Reject(path, Proof(path), "bound"); });
Test("consumer bounds line before parsing", () => { var path = FilePath("reader-line"); File.WriteAllText(path, new string('x', 8193) + "\n"); Reject(path, Proof(path), "bound"); });
Test("consumer bounds record count independently of writer", () => { var path = Complete("reader-count"); var result = CatalogEvidenceConsumer.Evaluate(path, identity, Origin(path), Proof(path), new(8192, 262144, 3)); Need(!result.EligibleForCatalogOnlyEvidence && result.Reason.Contains("count"), "Reader count bound bypassed."); });
Test("consumer rejects missing JSON fields rather than defaulting safe state", () => {
    var path = Complete("missing-field"); Rewrite(path, r => r[4]["Evidence"]!["Observation"]!.AsObject().Remove("CurrentSession")); Reject(path, Proof(path), "canonical");
});
Test("consumer rejects duplicate JSON fields rather than choosing last value", () => {
    var path = Complete("duplicate-field"); var text = File.ReadAllText(path); File.WriteAllText(path, text.Replace("\"CliActive\":false", "\"CliActive\":true,\"CliActive\":false")); Reject(path, Proof(path), "canonical");
});
Test("consumer refuses non C path without external lookup", () => Reject("F:\\forbidden.jsonl", null, "C-only"));
Test("writer header records exact binding output and original file path", () => {
    var path = Complete("origin-header"); var header = JsonNode.Parse(File.ReadLines(path).First())!;
    Need((string?)header["Origin"]?["RunOutputDirectory"] == paths.Output && (string?)header["Origin"]?["OriginalEvidencePath"] == path, "Original run-output provenance absent.");
});
foreach (var item in new (string Name, NormalBinding Binding)[] {
    ("run", binding with { RunId = Guid.NewGuid().ToString("D") }),
    ("PID", binding with { ProcessId = 457 }),
    ("native-thread", binding with { NativeThreadId = 43 }),
    ("foreign-run-path", binding with { Paths = paths with { Run = Path.Combine(root, "runs", Guid.NewGuid().ToString("D")) } }),
    ("run-outside-lab", binding with { Paths = paths with { Lab = Path.Combine(root, "different-lab") } })
}) Test("writer rejects binding mismatch " + item.Name, () => {
    var path = FilePath("binding-" + item.Name); var rejected = false;
    try { using var writer = CatalogEvidenceWriter.CreateNew(path, identity, item.Binding); } catch (ArgumentException) { rejected = true; }
    Need(rejected && !File.Exists(path), "Foreign binding reached file creation.");
});
foreach (var item in new[] { ("outside-output", Path.Combine(root, "outside.jsonl")), ("sibling-output", Path.Combine(paths.Run, "other", "evidence.jsonl")), ("nested-output", Path.Combine(paths.Output, "nested", "evidence.jsonl")) })
    Test("writer restricts supplied path to direct output leaf " + item.Item1, () => {
        Directory.CreateDirectory(Path.GetDirectoryName(item.Item2)!); var rejected = false;
        try { using var writer = CatalogEvidenceWriter.CreateNew(item.Item2, identity, binding); } catch (ArgumentException) { rejected = true; }
        Need(rejected && !File.Exists(item.Item2), "Out-of-output target reached file creation.");
    });
Test("consumer reads C archive with matching independent original path", () => {
    var original = Complete("archive-original"); var archive = Path.Combine(root, "archived-evidence.jsonl"); File.Copy(original, archive);
    var result = CatalogEvidenceConsumer.Evaluate(archive, identity, Origin(original), Proof(original));
    Need(result.EligibleForCatalogOnlyEvidence, "Valid C archive correspondence rejected: " + result.Reason);
});
Test("consumer rejects independently expected wrong original file", () => {
    var path = Complete("expected-origin-mismatch"); var result = CatalogEvidenceConsumer.Evaluate(path, identity, Origin(FilePath("another-original")), Proof(path));
    Need(!result.EligibleForCatalogOnlyEvidence && result.Reason.Contains("origin", StringComparison.OrdinalIgnoreCase), "Expected origin was ignored.");
});
Test("consumer rejects header original path outside run output", () => {
    var path = Complete("header-origin-outside"); Rewrite(path, r => r[0]["Origin"]!["OriginalEvidencePath"] = Path.Combine(root, "outside.jsonl")); Reject(path, Proof(path), "origin");
});
Test("consumer rejects close proof for another original output file", () => {
    var path = Complete("close-origin-mismatch"); Reject(path, Proof(path) with { Origin = Origin(FilePath("different-file")) }, "external close");
});
Test("consumer validates synthetic F origin strings using only C archive bytes", () => {
    var path = Complete("synthetic-external-origin");
    var externalOrigin = new CatalogEvidenceOrigin("F:\\AzuriteLabs\\Editor-20261108-normal\\runs\\" + identity.Binding.RunId + "\\output", "F:\\AzuriteLabs\\Editor-20261108-normal\\runs\\" + identity.Binding.RunId + "\\output\\catalog.jsonl");
    Rewrite(path, r => { r[0]["Origin"]!["RunOutputDirectory"] = externalOrigin.RunOutputDirectory; r[0]["Origin"]!["OriginalEvidencePath"] = externalOrigin.OriginalEvidencePath; });
    var result = CatalogEvidenceConsumer.Evaluate(path, identity, externalOrigin, Proof(path) with { Origin = externalOrigin });
    Need(result.EligibleForCatalogOnlyEvidence, "Consumer attempted original-path lookup or rejected valid origin syntax: " + result.Reason);
});
Console.WriteLine($"RESULT passed={passed} failed={failed} nativeExecuted=false fixtures={root}");
return failed == 0 ? 0 : 1;
