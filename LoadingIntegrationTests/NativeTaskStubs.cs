// Native async work advances from the simulated engine clock independently of
// Azurite's controller. This is a contract fixture, not an IL2CPP runtime.
namespace Il2CppSystem.Threading
{
    public sealed class CancellationToken : Il2CppSystem.Object
    {
        internal CancellationTokenSource Source;
        public static CancellationToken None { get; } = new();
        public bool IsCancellationRequested => Source?.Cancelled == true;
    }
    public sealed class CancellationTokenSource : Il2CppSystem.Object, Il2CppSystem.IDisposable
    {
        public readonly CancellationToken token;
        public bool Cancelled;
        public int CancelCalls;
        public int DisposeCalls;
        public CancellationTokenSource() => token = new CancellationToken { Source = this };
        public CancellationToken Token => token;
        public void Cancel() { Cancelled = true; CancelCalls++; }
        public void Dispose() => DisposeCalls++;
    }
}

namespace Cysharp.Threading.Tasks
{
    public enum UniTaskStatus { Pending, Succeeded, Faulted, Canceled }
    public enum PlayerLoopTiming { Update }
    public static class EnumeratorAsyncExtensions
    {
        public static UniTask ToUniTask(this Il2CppSystem.Collections.IEnumerator enumerator,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            Il2CppSystem.Threading.CancellationToken cancellationToken = null)
            => NativeTasks.Start(enumerator, cancellationToken);
    }
    public static class UniTaskExtensions
    {
        public static void Forget(this UniTask task)
        {
            task.Call.Forgotten = true;
            NativeTasks.ObserveForgotten(task.Call);
        }
    }
    public sealed class UniTask : Il2CppSystem.Object
    {
        internal NativeTaskCall Call;
        public UniTaskStatus Status => Call.Status;
        public Awaiter GetAwaiter() => new() { Task = this };
        public sealed class Awaiter : Il2CppSystem.Object
        {
            internal UniTask Task;
            public bool IsCompleted => Task.Status != UniTaskStatus.Pending;
            public void GetResult()
            {
                if (Task.Status == UniTaskStatus.Pending) throw new InvalidOperationException("result consumed before native task completion");
                if (++Task.Call.ResultReads != 1) throw new InvalidOperationException("native UniTask result consumed twice");
                if (Task.Status == UniTaskStatus.Faulted) throw new InvalidOperationException("native task resource failure");
                if (Task.Status == UniTaskStatus.Canceled) throw new OperationCanceledException("native task cancelled");
            }
        }
    }
}

internal sealed class NativeTaskCall
{
    public readonly List<ScriptData> Scripts = new();
    public Il2CppSystem.Threading.CancellationToken Token;
    public int StartedFrame;
    public int ReadyFrame;
    public int ResultReads;
    public bool Fault;
    public bool NeverComplete;
    public bool Forgotten;
    public Il2CppSystem.Collections.IEnumerator Enumerator;
    public bool NativeCompleted;
    public int LastAdvancedFrame = -1;
    public Cysharp.Threading.Tasks.UniTaskStatus Status =>
        Token?.IsCancellationRequested == true ? Cysharp.Threading.Tasks.UniTaskStatus.Canceled :
        NeverComplete || UnityEngine.Time.frameCount < ReadyFrame || !NativeCompleted ? Cysharp.Threading.Tasks.UniTaskStatus.Pending :
        Fault ? Cysharp.Threading.Tasks.UniTaskStatus.Faulted : Cysharp.Threading.Tasks.UniTaskStatus.Succeeded;
}

internal static class NativeTasks
{
    public static readonly List<NativeTaskCall> Calls = new();
    public static int DelayFrames = 3;
    public static bool Fault;
    public static bool NeverComplete;
    public static bool CompleteSynchronously;
    public static bool IsNativeScheduler;
    public static void Reset()
    {
        Calls.Clear(); DelayFrames = 3; Fault = false; NeverComplete = false; CompleteSynchronously = false; IsNativeScheduler = false;
        EngineOwnedResource.UnsafeMoves = 0;
    }
    public static Cysharp.Threading.Tasks.UniTask Start(
        Il2CppSystem.Collections.Generic.IEnumerable<ScriptData> scripts,
        Il2CppSystem.Threading.CancellationToken cancellationToken)
    {
        var call = new NativeTaskCall
        {
            Token = cancellationToken,
            StartedFrame = UnityEngine.Time.frameCount,
            ReadyFrame = UnityEngine.Time.frameCount + DelayFrames,
            Fault = Fault,
            NeverComplete = NeverComplete
        };
        foreach (var script in scripts) call.Scripts.Add(script);
        Calls.Add(call);
        return new Cysharp.Threading.Tasks.UniTask { Call = call };
    }
    public static Cysharp.Threading.Tasks.UniTask Start(
        Il2CppSystem.Collections.IEnumerator enumerator,
        Il2CppSystem.Threading.CancellationToken cancellationToken)
    {
        var call = new NativeTaskCall
        {
            Token = cancellationToken, StartedFrame = UnityEngine.Time.frameCount,
            ReadyFrame = UnityEngine.Time.frameCount + DelayFrames,
            Fault = Fault, NeverComplete = NeverComplete, Enumerator = enumerator
        };
        if (enumerator is EngineOwnedResource resource) call.Scripts.Add(resource.Metadata);
        Calls.Add(call);
        Advance(call); // Native UniTask starts its first step synchronously.
        return new Cysharp.Threading.Tasks.UniTask { Call = call };
    }
    public static void Tick()
    {
        foreach (var call in Calls.ToArray()) { Advance(call); ObserveForgotten(call); }
    }
    private static void Advance(NativeTaskCall call)
    {
        if (call.LastAdvancedFrame == UnityEngine.Time.frameCount || call.NativeCompleted || call.Token?.IsCancellationRequested == true) return;
        call.LastAdvancedFrame = UnityEngine.Time.frameCount;
        bool previous = IsNativeScheduler;
        IsNativeScheduler = true;
        try
        {
            if (call.Enumerator == null || !call.Enumerator.MoveNext()) call.NativeCompleted = true;
        }
        finally { IsNativeScheduler = previous; }
    }
    public static void ObserveForgotten(NativeTaskCall call)
    {
        if (!call.Forgotten || call.ResultReads > 0 || call.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending) return;
        try { new Cysharp.Threading.Tasks.UniTask { Call = call }.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (call.Status == Cysharp.Threading.Tasks.UniTaskStatus.Faulted) { }
    }
}

internal sealed class EngineOwnedResource : Il2CppSystem.Collections.IEnumerator
{
    internal static int UnsafeMoves;
    internal readonly ScriptData Metadata;
    private int remaining;
    internal int Moves;
    internal EngineOwnedResource(ScriptData metadata) { Metadata = metadata; remaining = NativeTasks.CompleteSynchronously ? 0 : 3; }
    public override Il2CppSystem.Object Current => null;
    public override bool MoveNext()
    {
        if (!NativeTasks.IsNativeScheduler)
        {
            UnsafeMoves++;
            throw new InvalidOperationException("unsafe-managed-native-MoveNext: resource must be advanced only by native scheduler");
        }
        Moves++;
        return remaining-- > 0;
    }
}
