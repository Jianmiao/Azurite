using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Azurite.NormalEditor.Entry.Evidence;

internal sealed record JournalRow(int SchemaVersion, string Kind, CatalogRunIdentity? Identity,
    CatalogEvidence? Evidence, int RecordCount, string? PayloadSha256, CatalogEvidenceOrigin? Origin = null);

internal static class JournalFormat
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static byte[] Encode(JournalRow row) => Utf8.GetBytes(JsonSerializer.Serialize(row) + "\n");
    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }

    internal static CatalogEvidenceLimits Limits(CatalogEvidenceLimits? limits)
    {
        var value = limits ?? new();
        if (value.MaxLineBytes < 512 || value.MaxLineBytes > 65536 || value.MaxFileBytes < value.MaxLineBytes ||
            value.MaxFileBytes > 4194304 || value.MaxRecords < 1 || value.MaxRecords > 1024)
            throw new ArgumentException("Invalid bounded journal limits.", nameof(limits));
        return value;
    }

    internal static void Identity(CatalogRunIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var b = identity.Binding;
        if (b is null || !Guid.TryParseExact(b.RunId, "D", out var id) || id == Guid.Empty || id.ToString("D") != b.RunId ||
            b.ProcessId <= 0 || b.ManagedThreadId <= 0 || b.NativeThreadId == 0 || b.TimeoutMilliseconds < 1 || b.TimeoutMilliseconds > 300000 ||
            !Utc(identity.ProcessStartUtc) || !Utc(identity.ObserverStartedUtc) || identity.ObserverStartedUtc < identity.ProcessStartUtc)
            throw new ArgumentException("Exact run/process/thread/deadline and UTC process/observer start identity required.", nameof(identity));
    }

    internal static bool Utc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;

    internal static string PathOnC(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An explicit canonical absolute C-only archive file path is required.", nameof(path));
        CanonicalLocalPath(path);
        ExistingParent(path);
        return path;
    }

    // This function is lexical only: a source origin on F never causes a lookup.
    private static void CanonicalLocalPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= 3 || !(path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') || path[1] != ':' || path[2] != '\\' ||
            path.AsSpan(2).Contains(':') || path.Contains('/') || !Path.IsPathFullyQualified(path) ||
            !string.Equals(path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ||
            path.Split('\\').Skip(1).Any(InvalidPathPart))
            throw new ArgumentException("A canonical local origin/file path without aliases, devices or streams is required.", nameof(path));
    }

    internal static void Origin(CatalogEvidenceOrigin origin, CatalogRunIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(origin);
        CanonicalLocalPath(origin.RunOutputDirectory);
        CanonicalLocalPath(origin.OriginalEvidencePath);
        var run = Path.GetDirectoryName(origin.RunOutputDirectory)!;
        if (!Same(Path.GetDirectoryName(origin.OriginalEvidencePath), origin.RunOutputDirectory) ||
            !Same(Path.GetFileName(origin.RunOutputDirectory), "output") || Path.GetFileName(run) != identity.Binding.RunId ||
            !Same(Path.GetFileName(Path.GetDirectoryName(run)), "runs"))
            throw new ArgumentException("Evidence origin must be a direct file under the bound runs/GUID/output directory.", nameof(origin));
    }

    internal static CatalogEvidenceOrigin BoundOutput(string path, CatalogRunIdentity identity, NormalBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Paths is null || binding.RunId != identity.Binding.RunId || binding.ProcessId != identity.Binding.ProcessId || binding.NativeThreadId != identity.Binding.NativeThreadId)
            throw new ArgumentException("Evidence identity does not match the supplied existing normal binding.", nameof(binding));
        CanonicalLocalPath(binding.Paths.Lab);
        CanonicalLocalPath(binding.Paths.Run);
        if (!Same(binding.Paths.Run, Path.Combine(binding.Paths.Lab, "runs", binding.RunId)))
            throw new ArgumentException("Supplied binding run path differs from its lab/run identity.", nameof(binding));
        var origin = new CatalogEvidenceOrigin(binding.Paths.Output, path);
        Origin(origin, identity); // No filesystem work before exact-route/identity validation.
        ExistingParent(path);
        return origin;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void ExistingParent(string path)
    {
        // Inspect ancestors from the volume root outward; never traverse a known
        // reparse ancestor before rejecting it. The caller still owns race safety.
        var ancestors = new Stack<string>();
        for (var current = Path.GetDirectoryName(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) ancestors.Push(current);
        while (ancestors.Count > 0) {
            var attributes = File.GetAttributes(ancestors.Pop());
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reparse evidence path is not allowed.", nameof(path));
            if ((attributes & FileAttributes.Directory) == 0) throw new DirectoryNotFoundException("Evidence parent must already be a directory.");
        }
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Reparse evidence file is not allowed.", nameof(path));
    }

    private static bool InvalidPathPart(string part)
    {
        if (part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789\u00b9\u00b2\u00b3".Contains(stem[3]));
    }

    internal static JournalRow Decode(ReadOnlySpan<byte> bytes)
    {
        var text = Utf8.GetString(bytes);
        var row = JsonSerializer.Deserialize<JournalRow>(text) ?? throw new InvalidDataException("Missing journal row.");
        using var actual = JsonDocument.Parse(text);
        using var canonical = JsonDocument.Parse(JsonSerializer.Serialize(row));
        Shape(actual.RootElement, canonical.RootElement);
        Require(row.SchemaVersion == 1, "Unsupported journal schema.");
        return row;
    }

    private static void Shape(JsonElement actual, JsonElement canonical)
    {
        Require(actual.ValueKind == canonical.ValueKind, "Noncanonical JSON value shape.");
        if (actual.ValueKind != JsonValueKind.Object) return;
        var fields = actual.EnumerateObject().ToArray();
        var expected = canonical.EnumerateObject().ToArray();
        Require(fields.Length == expected.Length && fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == fields.Length,
            "Noncanonical missing, duplicate or unknown JSON fields.");
        foreach (var field in fields) {
            Require(canonical.TryGetProperty(field.Name, out var other), "Noncanonical unknown JSON field.");
            Shape(field.Value, other);
        }
    }
}
