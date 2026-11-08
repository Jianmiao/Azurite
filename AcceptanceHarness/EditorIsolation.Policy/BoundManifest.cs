using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Azurite.EditorIsolation;

public sealed class BoundManifest
{
    public string RunId { get; private set; } = "";
    public string RunRoot { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    private byte[] bytes = Array.Empty<byte>();
    private string labRoot = "";
    public static BoundManifest ReadAt(string approvedLabRoot, string manifestPath)
    {
        RunPaths.RequireSafeExisting(approvedLabRoot);
        if (!RunPaths.Below(manifestPath, Path.Combine(approvedLabRoot, "runs"))) throw new InvalidOperationException("Manifest location is outside the approved run tree.");
        RunPaths.RequireSafeExisting(manifestPath);
        var run = Path.GetDirectoryName(manifestPath) ?? throw new InvalidOperationException("Manifest has no parent run.");
        var name = Path.GetFileName(run);
        if (!Guid.TryParseExact(name, "D", out var guid) || !RunPaths.Same(manifestPath, Path.Combine(approvedLabRoot, "runs", name, "manifest.json"))) throw new InvalidOperationException("Manifest location must be exactly runs/GUID-D/manifest.json.");
        var output = Path.Combine(run, "output"); RunPaths.RequireSafeExisting(output);
        if ((File.GetAttributes(output) & FileAttributes.Directory) == 0) throw new InvalidOperationException("Run output is not a directory.");
        WindowsFileIdentity.RequireSingleLink(manifestPath);
        using var input = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 16384) throw new InvalidOperationException("Manifest is too large.");
        var raw = new byte[checked((int)input.Length)];
        var offset = 0;
        while (offset < raw.Length) { var read = input.Read(raw, offset, raw.Length - offset); if (read == 0) throw new IOException("Manifest changed during bounded read."); offset += read; }
        return new BoundManifest { RunId = guid.ToString("D"), RunRoot = run, labRoot = approvedLabRoot, bytes = raw, Sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() };
    }
    public RunManifest Parse()
    {
        var manifest = JsonSerializer.Deserialize<RunManifest>(bytes) ?? throw new InvalidOperationException("Manifest content is missing.");
        if (!string.Equals(manifest.RunId, RunId, StringComparison.OrdinalIgnoreCase) || !RunPaths.Same(manifest.LabRoot, labRoot) ||
            !RunPaths.Same(manifest.RunRoot, RunRoot) || !RunPaths.Same(manifest.InstallRoot, Path.Combine(labRoot, "install"))) throw new InvalidOperationException("Manifest content disagrees with its independently validated location.");
        return manifest;
    }
}

public sealed class CorrelatedEvidence : IDisposable
{
    private readonly BoundManifest manifest;
    private readonly StreamWriter writer;
    private readonly int pid = Environment.ProcessId;
    private long sequence;
    public CorrelatedEvidence(BoundManifest manifest, string fileName)
    {
        if (fileName is not ("editor-isolation-guardian.jsonl" or "editor-isolation-evidence.jsonl")) throw new InvalidOperationException("Unknown evidence stream.");
        this.manifest = manifest;
        var output = Path.Combine(manifest.RunRoot, "output"); RunPaths.RequireSafeExisting(output);
        writer = new StreamWriter(new FileStream(Path.Combine(output, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }
    public void Emit(string kind, object data)
    {
        lock (writer) writer.WriteLine(JsonSerializer.Serialize(new { sequence = ++sequence, utc = DateTime.UtcNow, monotonicTicks = Stopwatch.GetTimestamp(), runId = manifest.RunId, pid, manifestSha256 = manifest.Sha256, kind, data }));
    }
    public void Flush() => writer.Flush();
    public void Dispose() => writer.Dispose();
}
