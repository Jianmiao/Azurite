using Azurite.NormalEditor;

namespace Azurite.NormalEditor.Adapter;

public enum NativeKind { Other, Settings, Init, Read, Save, Resource }
public enum CallbackKind { Awake, Coroutine, Init, Read, Save, Write, Resource }
public enum CallbackDisposition { Tracked, UnrelatedShared }
public enum RouteStage { Awake, InitReadYield }
public sealed record AdapterBinding(NormalBinding Policy, string ExpectedNativeSettingsPath)
{
    public string? HostAssemblySha256 {get;init;}
    public string? CoreAssemblySha256 {get;init;}
}
public sealed record NativeSettings(long Owner, long Data, bool Ready, bool Dirty, string? SettingsPath,
    string? CachedWorkspace, string? DataWorkspace, string? DataCache, string? Marker, bool CliActive, bool TransportPresent);
public sealed record NativeIterator(long Pointer, long Owner, NativeKind Kind, int State, long Current);
public sealed record RouteRecord(RouteStage Stage, string Field, string Target, NativeSettings Before, NativeSettings? After);

// No implementation in a shipping/preparation library grants these permissions.
public interface IAdapterAdmission : INormalAdmissionLease
{
    void RequireInstallation(AdapterBinding binding);
    void RequireCallback(AdapterBinding binding, CallbackKind kind, long instance);
    void RequireRouting(AdapterBinding binding, RouteRecord intent);
    void RecordRouting(RouteRecord record);
    void RequireResources(AdapterBinding binding, long resource, NativeSettings settings);
}
public interface INativeAccess
{
    uint NativeThreadId { get; }
    NativeSettings ReadSettings(long owner);
    NativeIterator ReadIterator(long iterator);
    NativeKind KindOf(long pointer);
    void SetSettingsPath(long owner, string path);
    void SetCachedWorkspace(long owner, string path);
}
public interface IFatalSink { void Terminate(string reason); }

public sealed class AdapterFrame
{
    internal AdapterFrame(long ordinal, CallbackKind kind, long instance, InitInvocation? init = null, SaveInvocation? save = null, WritePermit? write = null, CallbackDisposition disposition = CallbackDisposition.Tracked)
    { Ordinal=ordinal; Kind=kind; Instance=instance; Init=init; Save=save; Write=write; Disposition=disposition; ManagedThreadId=Environment.CurrentManagedThreadId; }
    public long Ordinal { get; }
    public CallbackKind Kind { get; }
    public long Instance { get; }
    public CallbackDisposition Disposition { get; }
    public int ManagedThreadId { get; }
    internal InitInvocation? Init { get; }
    internal SaveInvocation? Save { get; }
    internal WritePermit? Write { get; }
}
