namespace Azurite.EditorIsolation;

public enum ProbeMode { RouteSelfTest, NormalObserverCatalogOnly }
public sealed record RunPaths(string Lab, string Run, string Install)
{
    public string Settings => Path.Combine(Run, "settings", "user_settings.json");
    public string Control => Path.Combine(Run, "settings", "miss_control.json");
    public string Workspace => Path.Combine(Run, "workspace");
    public string Cache => Path.Combine(Run, "cache");
    public string Persistent => Path.Combine(Run, "persistent");
    public string Discovery => Path.Combine(Run, "discovery");
    public string Output => Path.Combine(Run, "output");
    public void Validate()
    {
        foreach (var p in new[] { Lab, Run, Install }) RequireSafeExisting(p);
        if (!Below(Run, Lab) || !Below(Install, Lab) || Below(Run, Install) || Below(Install, Run) || Same(Run, Install))
            throw new InvalidOperationException("Run and installation must be separate descendants of the lab root.");
        foreach (var p in new[] { Settings, Control, Workspace, Cache, Persistent, Discovery, Output }) RequireContained(p);
    }
    public string RequireContained(string path)
    {
        RequireSafeExisting(Run);
        if (!Below(path, Run)) throw new InvalidOperationException("Path escapes the run root.");
        RequireSafeExisting(path);
        return path;
    }
    public void RequireSettingsTarget(string path)
    {
        if (!Same(path, Settings)) throw new InvalidOperationException("Native write target is not the fixed lab settings file.");
        RequireContained(path);
    }
    public static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool Below(string child, string parent) => child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void RequireSafeExisting(string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Length < 3 || path[1] != ':' || path.AsSpan(2).Contains(':') || path.Contains('/') ||
            path.Split('\\').Skip(1).Any(x => x is "." or ".." || x.EndsWith(' ') || x.EndsWith('.')) ||
            !Same(Path.GetFullPath(path), path)) throw new InvalidOperationException("Path must be a canonical local drive path without aliases or streams.");
        string? current = path;
        while (!string.IsNullOrEmpty(current))
        {
            // GetAttributes (not Exists) fails closed for missing/inaccessible paths and dangling links.
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse points are not admitted.");
            current = Path.GetDirectoryName(current);
        }
    }
}
public sealed class ProbeLedger
{
    private readonly ProbeMode mode;
    private bool armed, readEntered, readComplete, writeEntered, writeComplete;
    private long boundOwner;
    public string? Failure { get; private set; }
    public ProbeLedger(ProbeMode mode) => this.mode = mode;
    public void Arm(bool cliActive, bool beforeFirstAwake)
    {
        if (armed || !beforeFirstAwake || mode != ProbeMode.RouteSelfTest || !cliActive)
            Reject("Only the resource-free CLI selftest is admitted; normal startup/order/resource review remains closed.");
        armed = true;
    }
    public long ReadPrefix(long owner, int state)
    {
        RequireLive();
        if (readEntered || owner == 0 || state != 0) Reject("Unexpected read iterator identity/state/order.");
        readEntered = true; boundOwner = owner;
        return 1;
    }
    public void ReadCompleted(long owner, bool yielded, string observed, string expected)
    {
        RequireLive();
        if (!readEntered || readComplete || owner != boundOwner || yielded || observed != expected) Reject("Missing native hit, fallback, owner or deserialized seed mismatch.");
        readComplete = true;
    }
    public void AdmitWrite(long owner)
    {
        RequireLive();
        if (!readComplete || writeEntered || owner != boundOwner) Reject("Write before successful original read, repeated write or wrong owner.");
        writeEntered = true;
    }
    public void WriteCompleted(long owner)
    {
        RequireLive();
        if (!writeEntered || writeComplete || owner != boundOwner) Reject("Write completion lacks matching entry evidence.");
        writeComplete = true;
    }
    public void Fail(string reason) => Failure ??= reason;
    public bool RouteProofComplete => Failure is null && readComplete && writeComplete;
    private void RequireLive() { if (!armed || Failure is not null) Reject("Probe is unarmed or failed."); }
    private void Reject(string reason) { Fail(reason); throw new InvalidOperationException(reason); }
}
