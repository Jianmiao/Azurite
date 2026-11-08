namespace Azurite.EditorIsolation;

public sealed class StartupGuardState
{
    private string? boundIdentity;
    private bool ready, completed;
    public static readonly string[] RequiredRoutes = { "awake", "read", "write", "fallback", "cache", "workspace", "resources", "discovery", "offline", "addressSet", "addressClear", "addressEvaluate" };
    public void Arm(string identity)
    {
        if (boundIdentity is not null || string.IsNullOrWhiteSpace(identity)) throw new InvalidOperationException("Duplicate or empty guardian identity.");
        boundIdentity = identity;
    }
    public void RequireArmed(string identity)
    {
        if (boundIdentity is null || boundIdentity != identity) throw new InvalidOperationException("Guardian is absent or bound to another manifest.");
    }
    public void RoutesReady(string identity, IEnumerable<string> routeIds)
    {
        RequireArmed(identity);
        var ids = routeIds.ToArray();
        if (ready || ids.Length != RequiredRoutes.Length || !ids.ToHashSet(StringComparer.Ordinal).SetEquals(RequiredRoutes)) throw new InvalidOperationException("Missing, duplicate or mismatched route registrations.");
        ready = true;
    }
    public void Complete(Exception? failure)
    {
        if (failure is not null || boundIdentity is null || !ready) throw new InvalidOperationException("Chainloader cannot resume native startup without complete isolation hooks.", failure);
        completed = true;
    }
    public void RequireCompleted(string identity) { RequireArmed(identity); if (!completed) throw new InvalidOperationException("Native entry preceded guarded chainloader completion."); }
}
