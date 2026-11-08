namespace Azurite.EditorIsolation;

public static class SettingsWriteAdmission
{
    public static void ValidateFresh(RunPaths paths)
    {
        paths.RequireSettingsTarget(paths.Settings);
        paths.RequireContained(paths.Control);
        WindowsFileIdentity.RequireSingleLink(paths.Settings);
        WindowsFileIdentity.RequireSingleLink(paths.Control);
        var directory = Path.GetDirectoryName(paths.Settings)!;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            paths.RequireContained(entry);
            if (!RunPaths.Same(entry, paths.Settings) && !RunPaths.Same(entry, paths.Control)) throw new InvalidOperationException("Fresh settings admission rejects every pre-existing SafeSave sidecar or other entry.");
        }
    }
    public static void Dispatch(RunPaths paths, Action nativeSave)
    {
        ValidateFresh(paths); // Outside and before the unproved native interception boundary.
        nativeSave();
    }
}
