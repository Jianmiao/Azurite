namespace Azurite.EditorIsolation;

public static class LabEntryAdmission
{
    public static bool ShouldActivate(string? manifestPath, string? executablePath, string assemblyPath, string assemblyRelativePath)
    {
        var install = Path.Combine(HostContract.ApprovedLabRoot, "install");
        var expectedExecutable = Path.Combine(install, "AzureArchive.exe");
        var expectedAssembly = Path.Combine(install, assemblyRelativePath);
        var inLabExecutable = RunPaths.Same(executablePath, expectedExecutable);
        var inLabAssembly = RunPaths.Same(assemblyPath, expectedAssembly);
        var touchesLab = inLabExecutable || inLabAssembly ||
            (executablePath is not null && RunPaths.Below(executablePath, install)) || RunPaths.Below(assemblyPath, install);
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            if (touchesLab) throw new InvalidOperationException("A loaded dedicated lab harness requires its per-child manifest; startup must abort.");
            return false; // Outside both pinned boundaries: explicitly unprotected, never reported as success.
        }
        if (!inLabExecutable || !inLabAssembly) throw new InvalidOperationException("Requested isolation is not executing from the exact approved lab paths.");
        return true;
    }
}
