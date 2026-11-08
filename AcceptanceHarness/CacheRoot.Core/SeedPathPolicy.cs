using System.Text;

namespace Azurite.CacheRootLab;

public static class SeedPathPolicy
{
    public static string ValidateText(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Contains('\0') || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains('/') || path.Length < 3 || path[1] != ':' || path.AsSpan(2).Contains(':') ||
            path.Split('\\').Skip(1).Any(s => s is "." or ".." || s.EndsWith(' ') || s.EndsWith('.')) || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Seed must be a canonical bounded local drive path.");
        try { if (new UTF8Encoding(false, true).GetByteCount(path) > NativeStringReader.MaximumBytes - "/Unity".Length) throw new InvalidOperationException("Seed leaves no bounded room for the original builder's Unity suffix."); }
        catch (EncoderFallbackException e) { throw new InvalidOperationException("Seed contains ill-formed UTF-16.", e); }
        return path.Replace('\\', '/');
    }
    public static void RequireExistingNoReparse(string path)
    {
        _ = ValidateText(path);
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse path is not admitted.");
    }
}
