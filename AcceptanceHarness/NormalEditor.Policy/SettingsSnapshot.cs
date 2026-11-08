using System.Security.Cryptography;
using System.Text.Json;
using Azurite.EditorIsolation;

namespace Azurite.NormalEditor;

public sealed record SettingsSnapshot(string MainSha256, string ControlSha256, string? Backup1, string? Backup2, string? Backup3, string Marker)
{
    internal static bool Hash(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    internal bool Same(SettingsSnapshot other) => Hash(MainSha256, other.MainSha256) && Hash(ControlSha256, other.ControlSha256) && Hash(Backup1, other.Backup1) && Hash(Backup2, other.Backup2) && Hash(Backup3, other.Backup3);
    internal bool Follows(SettingsSnapshot previous) => Hash(ControlSha256, previous.ControlSha256) && Hash(Backup1, previous.MainSha256) && Hash(Backup2, previous.Backup1) && Hash(Backup3, previous.Backup2);
}

public interface ISettingsSnapshotReader
{
    SettingsSnapshot Capture(RunPaths paths);
}

/// <summary>Fresh bounded read-only snapshots; no transaction or native-writer-exclusion claim.</summary>
public sealed class SettingsSnapshotReader : ISettingsSnapshotReader
{
    public const long MaximumFileBytes = 1024 * 1024;
    public SettingsSnapshot Capture(RunPaths paths)
    {
        paths.Validate();
        paths.RequireSettingsTarget(paths.Settings);
        paths.RequireContained(paths.Control);
        var allowed = new[] { paths.Settings, paths.Control, paths.Settings + ".bk.1", paths.Settings + ".bk.2", paths.Settings + ".bk.3" };
        var directory = Path.GetDirectoryName(paths.Settings)!;
        string[] Entries() => Directory.EnumerateFileSystemEntries(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var before = Entries();
        var streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in before)
            {
                paths.RequireContained(path);
                if (!allowed.Any(item => RunPaths.Same(item, path)) || (File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    throw new InvalidOperationException("Unknown settings entry or directory.");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                streams.Add(path, stream);
                if (stream.Length > MaximumFileBytes) throw new InvalidOperationException("Settings snapshot file exceeds its bound.");
                WindowsFileIdentity.RequireSingleLink(path); // This path is pinned by the still-open no-delete handle.
            }
            if (!streams.ContainsKey(paths.Settings) || !streams.ContainsKey(paths.Control)) throw new InvalidOperationException("Both owned seeds must remain present.");
            for (int i = 3; i >= 2; i--)
                if (streams.ContainsKey(paths.Settings + ".bk." + i) && !streams.ContainsKey(paths.Settings + ".bk." + (i - 1)))
                    throw new InvalidOperationException("Noncontiguous backup lineage.");
            byte[] Read(string path)
            {
                using var copy = new MemoryStream();
                streams[path].CopyTo(copy);
                if (copy.Length != streams[path].Length || copy.Length > MaximumFileBytes) throw new InvalidOperationException("Snapshot size changed.");
                return copy.ToArray();
            }
            static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
            var main = Read(paths.Settings);
            using var document = JsonDocument.Parse(main);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("workspacePath", out var workspace) || !root.TryGetProperty("cachePath", out var cache) ||
                workspace.ValueKind != JsonValueKind.String || cache.ValueKind != JsonValueKind.String || !RunPaths.Same(workspace.GetString(), paths.Workspace) || !RunPaths.Same(cache.GetString(), paths.Cache))
                throw new InvalidOperationException("Persisted settings workspace/cache escaped the bound run.");
            if (!root.TryGetProperty("username", out var marker) || marker.ValueKind != JsonValueKind.String) throw new InvalidOperationException("Persisted settings marker is missing.");
            string? Backup(int number) => streams.ContainsKey(paths.Settings + ".bk." + number) ? Digest(Read(paths.Settings + ".bk." + number)) : null;
            var result = new SettingsSnapshot(Digest(main), Digest(Read(paths.Control)), Backup(1), Backup(2), Backup(3), marker.GetString()!);
            if (!Entries().SequenceEqual(before, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Settings directory changed during snapshot.");
            return result;
        }
        finally { foreach (var stream in streams.Values) stream.Dispose(); }
    }
}
