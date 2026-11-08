using System.Security.Cryptography;
using System.Text.Json;

namespace Azurite.EditorIsolation;

public sealed record RunManifest(int Version, string RunId, string Mode, string LabRoot, string RunRoot, string InstallRoot, string SeedSha256, string ControlSha256)
{
    public RunPaths Paths => new(LabRoot, RunRoot, InstallRoot);
    public ProbeMode ProbeMode => Mode switch { "route-selftest" => ProbeMode.RouteSelfTest, "normalObserverCatalogOnly" => ProbeMode.NormalObserverCatalogOnly, _ => throw new InvalidOperationException("Unknown mode.") };
    public void Validate(string manifestPath)
    {
        if (Version != 1 || !Guid.TryParseExact(RunId, "D", out _)) throw new InvalidOperationException("Manifest identity/version mismatch.");
        _ = ProbeMode;
        Paths.Validate();
        if (!RunPaths.Same(manifestPath, Path.Combine(RunRoot, "manifest.json"))) throw new InvalidOperationException("Manifest must belong to its exact run.");
        Paths.RequireContained(manifestPath);
        SettingsWriteAdmission.ValidateFresh(Paths);
        ValidateSeed(Paths.Settings, SeedSha256, Paths, "lab-read-" + RunId);
        ValidateSeed(Paths.Control, ControlSha256, Paths, "miss-control-" + RunId);
        if (SeedSha256 == ControlSha256) throw new InvalidOperationException("Hit and miss seeds must differ.");
    }
    public static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static string ValidateSeed(string path, string expectedHash, RunPaths paths, string marker)
    {
        paths.RequireContained(path);
        WindowsFileIdentity.RequireSingleLink(path);
        if (new FileInfo(path).Length > 65536) throw new InvalidOperationException("Settings seed exceeds the bounded size.");
        if (expectedHash is null || expectedHash.Length != 64 || !string.Equals(Sha256(path), expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Seed hash mismatch.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var data = document.RootElement;
        var booleans = new[] { "stEnabled", "autoSave", "autoCompile", "lobbyBgmEnabled", "checkForUpdate", "isAutoPlay" };
        var volumes = new[] { "masterVolume", "voiceVolume", "bgmVolume", "soundEffectVolume" };
        var numbers = new[] { "lastProfileIndex", "autoDelay", "fpsTier", "fontIndex", "completeResStructVer" };
        var arrays = new[] { "visitedFiles", "favoriteBgmIds", "favoriteSoundNames" };
        var texts = new[] { "catalogUrlOverride", "username", "goldText", "diamondText", "staminaText", "customSource", "cachePath", "workspacePath" };
        var keys = booleans.Concat(volumes).Concat(numbers).Concat(arrays).Concat(texts).ToHashSet(StringComparer.Ordinal);
        var actual = data.ValueKind == JsonValueKind.Object ? data.EnumerateObject().Select(p => p.Name).ToArray() : Array.Empty<string>();
        if (actual.Length != keys.Count || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(keys)) throw new InvalidOperationException("Settings seed must contain every current native property exactly once.");
        foreach (var key in booleans) if (data.GetProperty(key).ValueKind != JsonValueKind.False) throw new InvalidOperationException("Seed requests an active behavior: " + key);
        // NoSound is enforced by the reviewed outer launch contract, not by muting internal PCM settings.
        foreach (var key in volumes) if (!data.GetProperty(key).TryGetDouble(out var v) || v != 1) throw new InvalidOperationException("Seed volumes must preserve unit gain for the later NoSound acceptance path.");
        foreach (var key in numbers) if (!data.GetProperty(key).TryGetInt32(out var v) || v < 0) throw new InvalidOperationException("Invalid native integer setting.");
        foreach (var key in arrays) if (data.GetProperty(key).ValueKind != JsonValueKind.Array || data.GetProperty(key).GetArrayLength() != 0) throw new InvalidOperationException("Seed lists must be empty.");
        foreach (var key in texts) if (data.GetProperty(key).ValueKind != JsonValueKind.String) throw new InvalidOperationException("Invalid native string setting.");
        if (data.GetProperty("username").GetString() != marker || data.GetProperty("customSource").GetString() != "" || data.GetProperty("catalogUrlOverride").GetString() != "" ||
            !RunPaths.Same(data.GetProperty("workspacePath").GetString()!, paths.Workspace) || !RunPaths.Same(data.GetProperty("cachePath").GetString()!, paths.Cache)) throw new InvalidOperationException("Seed marker or lab path mismatch.");
        return marker;
    }
}
