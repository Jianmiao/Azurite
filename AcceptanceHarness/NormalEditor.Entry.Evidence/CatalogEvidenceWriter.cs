namespace Azurite.NormalEditor.Entry.Evidence;

public sealed class CatalogEvidenceWriter : IDisposable
{
    private readonly FileStream stream;
    private readonly CatalogEvidenceLimits limits;
    private readonly MemoryStream payload = new();
    private readonly object sync = new();
    private int count;
    private bool faulted, sealedJournal, disposed;

    private CatalogEvidenceWriter(FileStream stream, CatalogEvidenceLimits limits) { this.stream = stream; this.limits = limits; }

    public static CatalogEvidenceWriter CreateNew(string path, CatalogRunIdentity identity, NormalBinding admittedBinding, CatalogEvidenceLimits? limits = null)
    {
        JournalFormat.Identity(identity);
        var bounded = JournalFormat.Limits(limits);
        // The caller must already hold the original admission. Binding is data,
        // not authority; this only restricts IO to that binding's output leaf.
        var origin = JournalFormat.BoundOutput(path, identity, admittedBinding);
        var writer = new CatalogEvidenceWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough), bounded);
        try { writer.Append(new(1, "header", identity, null, 0, null, origin)); return writer; }
        catch { writer.Dispose(); throw; }
    }

    public void Record(CatalogEvidence evidence)
    {
        lock (sync) {
            Writable();
            try {
                ArgumentNullException.ThrowIfNull(evidence);
                if (count >= limits.MaxRecords) throw new IOException("Journal record count bound exceeded.");
                // Bound caller-controlled strings before JSON escaping allocates a line.
                var strings = new[] { evidence.RunId, evidence.Kind, evidence.Failure, evidence.Stamp?.RunId, evidence.Observation?.Scene, evidence.Observation?.Stamp?.RunId };
                if (strings.Any(value => value is not null && value.Length > limits.MaxLineBytes)) throw new IOException("Journal string/line byte bound exceeded.");
                Append(new(1, "record", null, evidence, 0, null));
                count++;
            } catch { faulted = true; throw; }
        }
    }

    public CatalogJournalSeal Seal()
    {
        lock (sync) {
            Writable();
            try {
                Append(new(1, "sealed", null, null, count, JournalFormat.Digest(payload.GetBuffer().AsSpan(0, (int)payload.Length))));
                sealedJournal = true;
                return new(payload.Length, JournalFormat.Digest(payload.GetBuffer().AsSpan(0, (int)payload.Length)), count);
            } catch { faulted = true; PoisonTerminal(); throw; }
        }
    }

    private void Writable()
    {
        if (disposed) throw new ObjectDisposedException(nameof(CatalogEvidenceWriter));
        if (faulted) throw new InvalidOperationException("Evidence writer has failed; a terminal cannot be accepted.");
        if (sealedJournal) {
            faulted = true;
            PoisonTerminal();
            throw new InvalidOperationException("Evidence terminal already sealed; later writes invalidate it.");
        }
    }

    private void Append(JournalRow row)
    {
        var bytes = JournalFormat.Encode(row);
        // Keep one byte available for a post-terminal invalidation marker.
        if (bytes.Length - 1 > limits.MaxLineBytes || payload.Length + bytes.Length + 1 > limits.MaxFileBytes)
            throw new IOException("Journal line or total byte bound exceeded.");
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        payload.Write(bytes);
    }

    private void PoisonTerminal()
    {
        // A surviving marker makes a previously flushed seal unambiguously
        // nonterminal. Never remove failed evidence. If storage itself is lost,
        // external close evidence must report the observed writer failure.
        try { if (stream.Length < limits.MaxFileBytes) { stream.WriteByte((byte)'!'); stream.Flush(flushToDisk: true); } } catch { }
    }

    public void Dispose()
    {
        lock (sync) {
            if (disposed) return;
            disposed = true;
            try { stream.Dispose(); } finally { payload.Dispose(); }
        }
    }
}
