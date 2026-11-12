namespace Azurite.NormalEditor.Entry.Evidence;

public static class CatalogEvidenceConsumer
{
    public static CatalogEvidenceAssessment Evaluate(string path, CatalogRunIdentity expected, CatalogEvidenceOrigin expectedOrigin, ExternalCatalogCloseProof? closeProof, CatalogEvidenceLimits? limits = null)
    {
        var ready = false;
        try {
            JournalFormat.Identity(expected);
            var bounded = JournalFormat.Limits(limits);
            byte[] bytes;
            // Deny an active writer or replacement while reading the complete file.
            using (var input = new FileStream(JournalFormat.PathOnC(path), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                JournalFormat.Require(input.Length > 0 && input.Length <= bounded.MaxFileBytes, "Evidence total byte bound exceeded or file empty.");
                bytes = new byte[(int)input.Length];
                var offset = 0;
                while (offset < bytes.Length) {
                    var read = input.Read(bytes, offset, bytes.Length - offset);
                    JournalFormat.Require(read > 0, "Evidence read truncated."); offset += read;
                }
            }
            JournalFormat.Require(bytes[^1] == (byte)'\n', "Evidence terminal truncated or appended bytes present.");
            var rows = new List<(JournalRow Row, int Offset)>();
            var start = 0;
            for (var i = 0; i < bytes.Length; i++) {
                JournalFormat.Require(i - start <= bounded.MaxLineBytes, "Evidence line byte bound exceeded.");
                if (bytes[i] != (byte)'\n') continue;
                JournalFormat.Require(rows.Count < bounded.MaxRecords + 2, "Evidence record count bound exceeded.");
                if (rows.Count > 0 && rows[^1].Row.Kind == "sealed") throw new InvalidDataException("Evidence follows terminal.");
                rows.Add((JournalFormat.Decode(bytes.AsSpan(start, i - start)), start)); start = i + 1;
            }
            JournalFormat.Require(rows.Count >= 2 && rows[0].Row.Kind == "header" && rows[^1].Row.Kind == "sealed", "Evidence terminal is missing.");
            var header = rows[0].Row;
            JournalFormat.Require(header.Identity == expected && header.Evidence is null && header.RecordCount == 0 && header.PayloadSha256 is null, "Journal header identity mismatch.");
            JournalFormat.Require(JournalFormat.Utc(header.Identity!.ProcessStartUtc) && JournalFormat.Utc(header.Identity.ObserverStartedUtc), "Journal header identity requires UTC timestamps.");
            JournalFormat.Origin(expectedOrigin, expected);
            JournalFormat.Require(header.Origin == expectedOrigin, "Journal origin differs from independently expected output/file.");
            var terminal = rows[^1].Row;
            JournalFormat.Require(terminal.Identity is null && terminal.Evidence is null && terminal.Origin is null && terminal.RecordCount == rows.Count - 2, "Terminal count or shape mismatch.");
            JournalFormat.Require(terminal.PayloadSha256 == JournalFormat.Digest(bytes.AsSpan(0, rows[^1].Offset)), "Terminal payload digest mismatch.");
            var b = expected.Binding;
            long owner = 0, lastMilliseconds = -1, lastFrame = -1, completedFrame = -1, readyMilliseconds = -1;
            var lifecycle = new[] { "catalog-start-enter", "catalog-start-return", "catalog-start-complete", "catalog-ready" };
            for (var i = 1; i < rows.Count - 1; i++) {
                var row = rows[i].Row;
                JournalFormat.Require(row.Kind == "record" && row.Identity is null && row.Origin is null && row.Evidence is not null && row.RecordCount == 0 && row.PayloadSha256 is null, "Invalid record or terminal shape.");
                var e = row.Evidence!;
                JournalFormat.Require(e.Sequence == i, "Evidence sequence is missing or duplicated.");
                // Failure remains authoritative even if its fallback stamp is incomplete.
                JournalFormat.Require(e.Kind != "catalog-failed", "A catalog-failed event invalidates the run.");
                var s = e.Stamp;
                JournalFormat.Require(s is not null && e.RunId == b.RunId && s.RunId == b.RunId && s.ProcessId == b.ProcessId && s.ManagedThreadId == b.ManagedThreadId && s.NativeThreadId == b.NativeThreadId, "Evidence run/process/thread identity mismatch.");
                JournalFormat.Require(s!.CatalogOwner > 0 && (owner == 0 || owner == s.CatalogOwner), "Evidence catalog owner mismatch."); owner = s.CatalogOwner;
                JournalFormat.Require(s.Milliseconds >= 0 && s.Frame >= 0 && s.Milliseconds >= lastMilliseconds && s.Frame >= lastFrame, "Evidence clock/frame reversed.");
                JournalFormat.Require(s.Milliseconds <= b.TimeoutMilliseconds, "Readiness deadline elapsed.");
                lastMilliseconds = s.Milliseconds; lastFrame = s.Frame;
                JournalFormat.Require(i <= lifecycle.Length && e.Kind == lifecycle[i - 1], "Catalog lifecycle order is incomplete or unknown.");
                JournalFormat.Require(e.Failure is null, "A success record contains failure data.");
                if (i == 3) completedFrame = s.Frame;
                if (i != 4) { JournalFormat.Require(e.Observation is null, "Unexpected lifecycle observation."); continue; }
                JournalFormat.Require(s.Frame > completedFrame, "Ready requires a later frame than Start completion.");
                var o = e.Observation;
                JournalFormat.Require(o is not null && o.Stamp == s, "Ready observation stamp is absent or inconsistent.");
                JournalFormat.Require(o!.WorkbenchOwner > 0 && o.SettingsOwner > 0 && o.ResourceOwner > 0 && o.Scene == "CatalogScene" &&
                    !o.CliActive && !o.TransportPresent && o.SettingsReady && o.ResourcesReady && !o.Preloading && o.LoadingCount == 0 && o.CurrentSession == 0 && !o.ControlOpening,
                    "Ready predicates are not satisfied.");
                ready = true; readyMilliseconds = s.Milliseconds;
            }
            JournalFormat.Require(ready, "Complete ready lifecycle is absent.");
            ValidateClose(closeProof, expected, expectedOrigin, owner, readyMilliseconds, bytes);
            return new(true, true, "Record set and supplied external close evidence are consistent; physical provenance and native acceptance require independent review.");
        } catch (System.Text.DecoderFallbackException) { return new(false, ready, "Evidence UTF8 is invalid."); }
        catch (IOException e) { return new(false, ready, "Evidence read/validation rejected: " + e.Message); }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or System.Text.Json.JsonException or UnauthorizedAccessException or OverflowException) {
            return new(false, ready, "Evidence validation rejected: " + e.Message);
        }
    }

    private static void ValidateClose(ExternalCatalogCloseProof? p, CatalogRunIdentity expected, CatalogEvidenceOrigin origin, long owner, long readyMilliseconds, byte[] bytes)
    {
        JournalFormat.Require(p is not null, "Independent external close proof is required.");
        JournalFormat.Require(p!.Identity == expected && JournalFormat.Utc(p.Identity.ProcessStartUtc) && JournalFormat.Utc(p.Identity.ObserverStartedUtc) &&
            p.CatalogOwner == owner && p.MonitorProcessId > 0 && p.MonitorProcessId != expected.Binding.ProcessId,
            "External close process/start/thread/owner identity mismatch.");
        JournalFormat.Require(p.Origin == origin, "External close origin differs from the expected original output file.");
        JournalFormat.Require(JournalFormat.Utc(p.MonitorProcessStartUtc) && JournalFormat.Utc(p.CloseRequestedUtc) && JournalFormat.Utc(p.ExitObservedUtc) && JournalFormat.Utc(p.EvidenceObservedUtc) &&
            p.MonitorProcessStartUtc <= p.CloseRequestedUtc && p.CloseRequestedUtc >= expected.ObserverStartedUtc.AddMilliseconds(readyMilliseconds) &&
            p.ExitObservedUtc >= p.CloseRequestedUtc && p.EvidenceObservedUtc >= p.ExitObservedUtc &&
            p.CloseTimeoutMilliseconds is >= 1 and <= 300000 && (p.ExitObservedUtc - p.CloseRequestedUtc).TotalMilliseconds <= p.CloseTimeoutMilliseconds,
            "External close UTC chronology/deadline is incomplete or inconsistent.");
        JournalFormat.Require(p.NormalCloseRequestSucceeded && !p.TimedOut && !p.ForceTerminated && !p.ObserverFailureObserved && p.ExitCode == 0,
            "External close reports missing normal request, timeout, force, observer failure or nonzero exit.");
        JournalFormat.Require(p.EvidenceBytes == bytes.Length && p.EvidenceSha256 == JournalFormat.Digest(bytes), "External close whole-file length/digest mismatch.");
    }
}
