namespace Azurite.NormalEditor.Entry;

public sealed record CatalogBinding(string RunId,int ProcessId,int ManagedThreadId,uint NativeThreadId,long TimeoutMilliseconds);
public sealed record CatalogStamp(string RunId,int ProcessId,int ManagedThreadId,uint NativeThreadId,long Frame,long Milliseconds,long CatalogOwner);
public sealed record CatalogObservation(CatalogStamp Stamp,long WorkbenchOwner,long SettingsOwner,long ResourceOwner,string Scene,
    bool CliActive,bool TransportPresent,bool SettingsReady,bool ResourcesReady,bool Preloading,int LoadingCount,long CurrentSession,bool ControlOpening);
public sealed record CatalogEvidence(long Sequence,string Kind,string RunId,CatalogStamp Stamp,CatalogObservation? Observation,string? Failure);
public sealed class CatalogStartFrame
{
    internal CatalogStartFrame(CatalogStamp stamp){Stamp=stamp;}
    public CatalogStamp Stamp {get;}
}
