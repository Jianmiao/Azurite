using Azurite.EditorIsolation;

namespace Azurite.NormalEditor;

public enum NormalStep { BindAwake, ReadEnter, ReadReturn, OnReadyEnter, ResourceStart, OnReadyReturn, Readiness, SaveTaskCreated, SaveMoveNextEnter, WriteEnter, WriteReturn, SaveMoveNextReturn, Close }

public sealed record NormalBinding(string RunId, long Generation, int ProcessId, uint NativeThreadId,
    RunPaths Paths, string SeedSha256, string ControlSha256, string SeedMarker);

public sealed record SettingsObservation(string RunId, long Generation, int ProcessId, uint NativeThreadId,
    long Sequence, long Owner, long Data, bool IsReady, string SettingsPath, string CachedWorkspacePath,
    string DataWorkspacePath, string DataCachePath);

/// <summary>Implemented only by a separately reviewed native-admission integration. No permitting implementation is supplied.</summary>
public interface INormalAdmissionLease
{
    string EvidenceSha256 { get; }
    void AssertPermitted(NormalBinding binding, NormalStep step, SettingsObservation observation);
}

public sealed class SaveInvocation
{
    internal SaveInvocation(long ordinal, long iterator, long generation, int state, bool dirty)
    { Ordinal = ordinal; Iterator = iterator; WriteGeneration = generation; State = state; Dirty = dirty; }
    public long Ordinal { get; }
    public long Iterator { get; }
    public long WriteGeneration { get; }
    internal int State { get; }
    internal bool Dirty { get; }
}

public sealed class WritePermit
{
    internal WritePermit(SaveInvocation invocation) => Invocation = invocation;
    internal SaveInvocation Invocation { get; }
    public long WriteGeneration => Invocation.WriteGeneration + 1;
}
