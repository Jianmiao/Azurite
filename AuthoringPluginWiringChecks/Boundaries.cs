// Explicit boundary doubles: these tests execute the production Plugin partial,
// not Unity, BepInEx, or the separately tested real HostRoot.
using Azurite;
namespace BepInEx.Configuration
{
    internal sealed class ConfigEntry<T> { public T Value { get; set; } public ConfigEntry(T value) => Value=value; }
    internal sealed class ConfigFile
    {
        internal readonly Dictionary<string,object> Entries=new();
        public ConfigEntry<T> Bind<T>(string section,string key,T defaultValue,string description)
        { var entry=new ConfigEntry<T>(defaultValue); Entries.Add(section+"/"+key,entry); return entry; }
    }
}
namespace Azurite
{
    internal class TestLog { internal readonly List<string> Entries=new(); public void LogWarning(object message)=>Entries.Add(message.ToString()!); }
    public class TestBase { internal BepInEx.Configuration.ConfigFile Config=new(); internal TestLog Log=new(); }
    public sealed partial class Plugin : TestBase
    {
        internal BepInEx.Configuration.ConfigEntry<bool> _enabled=new(true);
        internal BepInEx.Configuration.ConfigEntry<double> _mutationSettleSeconds=new(.35);
        internal HostCompatibility? _host=new(); internal HostActivity? _activity=new();
        internal ProgressiveEditorLoading? _progressiveLoading;
        internal DialogueMutationCoordinator? _mutation;
        internal bool _disabledForSession, _exportSuspended;
        internal Azurite.Core.RenderIntervalLease? _intervalLease;
        internal FpsController? _fps;
        internal EditorScrollTuning? _scroll;
        private static double Now=>10;
    }
    internal sealed class AzuriteDriver { internal Action? GuiTick; }
    internal sealed class HostCompatibility
    {
        internal HostProfile Profile=HostProfile.ResolvePortable("0776516C59811C724C450E205930546C5D102975F279E33C46C96B878FA06C8B", "B4AEEE165E7E10E0F46F85E095FD94487FDB041215F6DCB7AE9DE5D28A364F43",true);
        internal bool PublicRenderingBlocked; internal int ProbeCalls; internal bool ThrowProbe;
        internal Action? DuringProbe;
        internal HostCompatibilitySnapshot Snapshot=HostCompatibilitySnapshot.NoExport("fixture no exporter");
        internal HostCompatibilitySnapshot Probe(){ProbeCalls++;DuringProbe?.Invoke();if(ThrowProbe)throw new InvalidOperationException("probe failed");return Snapshot;}
    }
    internal sealed class HostActivity { internal bool Allowed=true; internal int Calls; internal bool CanUseAuthoringContext(){Calls++;return Allowed;} }
    internal sealed class ProgressiveEditorLoading { internal bool BlocksEditorOperations; internal bool ResourceDependentWorkAllowed=true; }
    internal sealed class DialogueMutationCoordinator { internal bool Blocking; internal bool IsBlocking(double now,double settle)=>Blocking; }
    internal sealed class AuthoringCardPackHostRoot
    {
        internal static readonly List<AuthoringCardPackHostRoot> Created=new();
        internal readonly int Thread; internal readonly Func<bool> Ownership,Context;
        internal readonly List<bool> Updates=new(); internal int Draws,Suspends,Resumes,Disposals,Abandons;
        internal bool ThrowUpdate,ThrowDispose,IsDisposed;
        internal AuthoringCardPackHostRoot(int thread,Func<bool> ownership,Func<bool> context,Action<string> log)
        {Thread=thread;Ownership=ownership;Context=context;Created.Add(this);}
        internal void Update(bool enabled){Updates.Add(enabled);if(ThrowUpdate)throw new InvalidOperationException("update failed");}
        internal void DrawSelection(){Draws++;}
        internal void SuspendForExport(){Suspends++;}
        internal void ResumeAfterExport(){Resumes++;}
        internal void DisposeAtSafePoint(){Disposals++;if(ThrowDispose)throw new InvalidOperationException("dispose failed");IsDisposed=true;}
        internal void AbandonForShutdown(){Abandons++;if(ThrowDispose)throw new InvalidOperationException("shutdown failed");IsDisposed=true;}
    }
}
