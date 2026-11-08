using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using Azurite.EditorIsolation;

namespace Azurite.NormalEditor;

public sealed class NormalSettingsLifecycle
{
    private enum Phase { New, Awake, InitRead, AwaitingRead, Reading, ReadComplete, InitContinuation, Ready, Closed }
    private readonly object sync = new();
    private readonly NormalBinding binding;
    private readonly INormalAdmissionLease? admission;
    private readonly string? admissionHash;
    private readonly ISettingsSnapshotReader reader;
    private Phase phase;
    private bool inTransition, readyObserved, initializationCompleted, saveStarted, writeReturned;
    private long owner, data, initIterator, readIterator, saveIterator, lastSequence, initOrdinal, resumeOrdinal, writes, resumes, writeGeneration, resourceStarts;
    private int managedThread;
    private SettingsSnapshot? accepted, pending;
    private InitInvocation? initActive;
    private SaveInvocation? active;
    private WritePermit? activeWrite;
    public string? Failure { get; private set; }
    public long CompletedWrites { get { lock (sync) return writes; } }
    public long CompletedSaveResumes { get { lock (sync) return resumes; } }
    public long WriteGeneration { get { lock (sync) return writeGeneration; } }
    public long ResourceStartObservations { get { lock (sync) return resourceStarts; } }
    public bool ReadyObserved { get { lock (sync) return readyObserved; } }
    public bool InitializationCompleted { get { lock (sync) return initializationCompleted; } }
    public bool RepeatedSaveLineageObserved { get { lock (sync) return Failure is null && initializationCompleted && readyObserved && writes >= 2 && resumes >= 2 && active is null; } }

    public NormalSettingsLifecycle(NormalBinding binding, INormalAdmissionLease? admission = null, ISettingsSnapshotReader? reader = null)
    {
        if (!Guid.TryParseExact(binding.RunId, "D", out var id) || id.ToString("D") != binding.RunId || binding.Generation <= 0 || binding.ProcessId <= 0 || binding.NativeThreadId == 0 ||
            !RunPaths.Same(binding.Paths.Run, Path.Combine(binding.Paths.Lab, "runs", binding.RunId)) || !RunPaths.Same(binding.Paths.Install, Path.Combine(binding.Paths.Lab, "install")) ||
            !IsHash(binding.SeedSha256) || !IsHash(binding.ControlSha256) || string.IsNullOrWhiteSpace(binding.SeedMarker))
            throw new InvalidOperationException("Invalid bound normal run identity.");
        this.binding = binding; this.admission = admission; admissionHash = admission?.EvidenceSha256;
        this.reader = reader ?? new SettingsSnapshotReader();
    }

    public void BindAwake(SettingsObservation observation, bool cliActive, bool transportPresent) => Move(observation, NormalStep.BindAwake, () =>
    {
        Require(phase == Phase.New && observation.Owner > 0 && observation.Data == 0 && !observation.IsReady && !cliActive && !transportPresent, "Normal first Awake must be pristine, non-CLI and unbound.");
        binding.Paths.Validate(); owner = observation.Owner; managedThread = Environment.CurrentManagedThreadId; phase = Phase.Awake;
    });

    public void InitTaskCreated(SettingsObservation observation, long iterator, int state) => Move(observation, NormalStep.InitTaskCreated, () =>
    {
        Owner(observation, requireData: false);
        Require(phase == Phase.Awake && initIterator == 0 && iterator > 0 && state == 0 && observation.Data == 0 && !observation.IsReady, "InitTask must bind once from the actual state-zero coroutine argument during Awake.");
        initIterator = iterator;
    });

    public InitInvocation InitMoveNextEntered(SettingsObservation observation, long iterator, int state) => Move(observation, NormalStep.InitMoveNextEnter, () =>
    {
        Owner(observation, requireData: state != 0);
        Require(initIterator > 0 && iterator == initIterator && initActive is null && !initializationCompleted && !observation.IsReady, "Unbound, reentrant or already-ready InitTask entry.");
        if (state == 0)
        {
            Require(phase == Phase.Awake && observation.Data == 0 && readIterator == 0, "InitTask state zero must precede the original read.");
            phase = Phase.InitRead;
        }
        else
        {
            Require(state == 1 && phase == Phase.ReadComplete, "InitTask continuation requires the completed bound original read.");
            phase = Phase.InitContinuation;
        }
        initActive = new InitInvocation(++initOrdinal, iterator, state);
        return initActive;
    });

    // yieldedReadIterator describes an actual yield only; it is not the terminal iterator's retained current field.
    public void InitMoveNextReturned(SettingsObservation observation, InitInvocation invocation, long iterator, bool yielded, int nextState, long yieldedReadIterator = 0) => Move(observation, NormalStep.InitMoveNextReturn, () =>
    {
        InitFrame(invocation);
        Owner(observation, requireData: invocation.State != 0);
        Require(iterator == initIterator, "Original InitTask return belongs to another iterator.");
        if (invocation.State == 0)
        {
            Require(phase == Phase.InitRead && yielded && nextState == 1 && yieldedReadIterator > 0 && yieldedReadIterator != initIterator && readIterator == 0 && observation.Data == 0 && !observation.IsReady, "Original InitTask must yield its actual read child in state one.");
            readIterator = yieldedReadIterator; phase = Phase.AwaitingRead;
        }
        else
        {
            Require(phase == Phase.Ready && !yielded && nextState == -1 && yieldedReadIterator == 0 && observation.IsReady && readyObserved && saveIterator > 0 && active is null, "Original InitTask did not complete after readiness and natural saver capture.");
            initializationCompleted = true;
        }
        initActive = null;
    });

    public void ReadEntered(SettingsObservation observation, long iterator, int state) => Move(observation, NormalStep.ReadEnter, () =>
    {
        Owner(observation, requireData: false); Require(phase == Phase.AwaitingRead && initActive is null && readIterator > 0 && iterator == readIterator && state == 0 && observation.Data == 0 && !observation.IsReady, "Original read must enter through the actual child yielded by InitTask.");
        var snapshot = Capture();
        Require(SettingsSnapshot.Hash(snapshot.MainSha256, binding.SeedSha256) && SettingsSnapshot.Hash(snapshot.ControlSha256, binding.ControlSha256) && snapshot.Marker == binding.SeedMarker && snapshot.Backup1 is null && snapshot.Backup2 is null && snapshot.Backup3 is null, "Initial settings are not the exact fresh seeds.");
        accepted = snapshot; phase = Phase.Reading;
    });

    public void OriginalReadReturned(SettingsObservation observation, long iterator, bool yielded, string seedMarker) => Move(observation, NormalStep.ReadReturn, () =>
    {
        Owner(observation, requireData: false); Require(phase == Phase.Reading && iterator == readIterator && !yielded && observation.Data > 0 && !observation.IsReady && seedMarker == binding.SeedMarker, "Original read completion/owner/marker differs.");
        Require(Capture().Same(accepted!), "Settings changed across original read.");
        data = observation.Data; Owner(observation); phase = Phase.ReadComplete;
    });

    public void ResourceInitializationObserved(SettingsObservation observation, InitInvocation? continuation = null) => Move(observation, NormalStep.ResourceStart, () =>
    {
        Owner(observation); Require(resourceStarts == 0, "Repeated initial resource-start observation.");
        if (!observation.IsReady)
        {
            InitFrame(continuation);
            Require(continuation.State == 1 && phase == Phase.InitContinuation && !readyObserved, "False-ready resource entry requires the active InitTask continuation before true readiness.");
        }
        else { ReadyContext(continuation); LaterReady(observation); }
        resourceStarts++; // Observation only; not permission for OS/cache/resource effects.
    });

    public void ReadinessObserved(SettingsObservation observation, InitInvocation? continuation = null) => Move(observation, NormalStep.Readiness, () =>
    { ReadyContext(continuation); LaterReady(observation); });

    public void SaveTaskCreated(SettingsObservation observation, InitInvocation continuation, long iterator, int state) => Move(observation, NormalStep.SaveTaskCreated, () =>
    {
        InitFrame(continuation); Require(continuation.State == 1 && !initializationCompleted, "SaveTask capture must be inside the actual InitTask continuation.");
        LaterReady(observation);
        Require(saveIterator == 0 && iterator > 0 && iterator != initIterator && iterator != readIterator && state == 0, "SaveTask must bind once from its actual state-zero coroutine argument.");
        saveIterator = iterator;
    });

    public SaveInvocation SaveMoveNextEntered(SettingsObservation observation, long iterator, int state, bool dirty) => Move(observation, NormalStep.SaveMoveNextEnter, () =>
    {
        LaterReady(observation); Require(saveIterator != 0 && iterator == saveIterator && active is null, "Unbound or reentrant SaveTask resume.");
        Require(state == (saveStarted ? 1 : 0), "SaveTask must first yield state zero, then resume state one.");
        Require(state == 0 || initializationCompleted, "Later SaveTask resumes require original InitTask completion.");
        active = new SaveInvocation(++resumeOrdinal, iterator, writeGeneration, state, dirty); activeWrite = null; pending = null; writeReturned = false;
        return active;
    });

    public WritePermit AdmitWrite(SettingsObservation observation, SaveInvocation invocation) => Move(observation, NormalStep.WriteEnter, () =>
    {
        LaterReady(observation); Invocation(invocation);
        Require(initializationCompleted && invocation.State == 1 && invocation.Dirty && activeWrite is null && !writeReturned, "Write is not the unique nested dirty SaveTask write after InitTask completion.");
        Require(Capture().Same(accepted!), "Fresh pre-write snapshot differs from accepted run-owned lineage.");
        activeWrite = new WritePermit(invocation); return activeWrite;
    });

    public void OriginalWriteReturned(SettingsObservation observation, WritePermit permit) => Move(observation, NormalStep.WriteReturn, () =>
    {
        LaterReady(observation); Require(activeWrite is not null && ReferenceEquals(activeWrite, permit) && !writeReturned, "Stale/foreign/repeated original-write return.");
        Invocation(permit.Invocation);
        var snapshot = Capture();
        Require(snapshot.Follows(accepted!), "Original write did not preserve exact backup/control lineage.");
        pending = snapshot; writeReturned = true;
    });

    public void SaveMoveNextReturned(SettingsObservation observation, SaveInvocation invocation, long iterator, bool yielded, int nextState) => Move(observation, NormalStep.SaveMoveNextReturn, () =>
    {
        LaterReady(observation); Invocation(invocation);
        Require(iterator == saveIterator && yielded && nextState == 1, "Original SaveTask did not return its native wait/state-one continuation.");
        if (invocation.State == 0)
        {
            Require(activeWrite is null && !writeReturned, "Initial wait cannot contain a write."); saveStarted = true;
        }
        else
        {
            if (invocation.Dirty)
            {
                Require(activeWrite is not null && writeReturned && pending is not null, "Dirty native resume lacks its matched original write return.");
                Require(Capture().Same(pending), "Post-write lineage changed before original SaveTask completion.");
                accepted = pending; writes++; writeGeneration++;
            }
            else Require(activeWrite is null && !writeReturned, "Clean resume unexpectedly wrote.");
            resumes++;
        }
        active = null; activeWrite = null; pending = null; writeReturned = false;
    });

    public void Close(SettingsObservation observation) => Move(observation, NormalStep.Close, () =>
    { LaterReady(observation); Require(initializationCompleted && initActive is null && active is null, "Cannot retire owner before original InitTask completion or during a native save frame."); phase = Phase.Closed; });

    public void Fail(string reason) { lock (sync) Failure ??= reason; }
    private static bool IsHash(string? text) => text is not null && Regex.IsMatch(text, "\\A[0-9a-fA-F]{64}\\z");
    private void Require([DoesNotReturnIf(false)] bool condition, string reason) { if (!condition) { Failure ??= reason; throw new InvalidOperationException(reason); } }
    private void Owner(SettingsObservation observation, bool requireData = true)
    {
        Require(observation.Owner == owner && owner > 0, "Bound native owner differs.");
        Require(RunPaths.Same(observation.SettingsPath, binding.Paths.Settings) && RunPaths.Same(observation.CachedWorkspacePath, binding.Paths.Workspace), "Observed settings/cached-workspace path drift.");
        if (requireData || observation.Data > 0)
        {
            Require(observation.Data > 0 && (data == 0 || observation.Data == data), "Bound native data identity differs.");
            Require(RunPaths.Same(observation.DataWorkspacePath, binding.Paths.Workspace) && RunPaths.Same(observation.DataCachePath, binding.Paths.Cache), "Observed data path drift.");
        }
    }
    private void LaterReady(SettingsObservation observation)
    { Owner(observation); Require((phase == Phase.InitContinuation || phase == Phase.Ready) && (initActive?.State == 1 || initializationCompleted) && observation.IsReady, "Native readiness outside the bound continuation/completed initialization or readiness regression."); phase = Phase.Ready; readyObserved = true; }
    private void InitFrame([NotNull] InitInvocation? invocation)
    { Require(initActive is not null && invocation is not null && ReferenceEquals(initActive, invocation) && invocation.Iterator == initIterator, "Stale, foreign or missing InitTask invocation."); }
    private void ReadyContext(InitInvocation? continuation)
    {
        if (initActive is not null) { InitFrame(continuation); Require(continuation.State == 1, "Readiness cannot be observed in initial InitTask state zero."); }
        else Require(initializationCompleted && continuation is null, "Readiness context is incomplete or carries a retired continuation.");
    }
    private void Invocation(SaveInvocation invocation)
    { Require(active is not null && ReferenceEquals(active, invocation) && invocation.Iterator == saveIterator && invocation.WriteGeneration == writeGeneration, "Stale/foreign SaveTask invocation or write generation."); }
    private SettingsSnapshot Capture()
    {
        var snapshot = reader.Capture(binding.Paths);
        Require(Failure is null && snapshot is not null && IsHash(snapshot.MainSha256) && IsHash(snapshot.ControlSha256) &&
                (snapshot.Backup1 is null || IsHash(snapshot.Backup1)) && (snapshot.Backup2 is null || IsHash(snapshot.Backup2)) && (snapshot.Backup3 is null || IsHash(snapshot.Backup3)), "Invalid fresh snapshot or nested refusal.");
        return snapshot;
    }
    private void Move(SettingsObservation observation, NormalStep step, Action action) => Move(observation, step, () => { action(); return 0; });
    private T Move<T>(SettingsObservation observation, NormalStep step, Func<T> action)
    {
        lock (sync)
        {
            Require(Failure is null && phase != Phase.Closed, "Normal lifecycle is failed or closed.");
            Require(!inTransition, "Reentrant policy transition.");
            inTransition = true;
            try
            {
                Require(admission is not null && IsHash(admissionHash) && admission.EvidenceSha256 == admissionHash, "External normal admission is required and remains unprovided by default.");
                Require(observation.RunId == binding.RunId && observation.Generation == binding.Generation && observation.ProcessId == binding.ProcessId && binding.ProcessId == Environment.ProcessId &&
                        observation.NativeThreadId == binding.NativeThreadId && observation.Sequence > lastSequence && observation.Sequence > 0 &&
                        (managedThread == 0 || managedThread == Environment.CurrentManagedThreadId), "Run/generation/process/thread/sequence identity differs.");
                admission.AssertPermitted(binding, step, observation);
                Require(Failure is null, "External admission callback caused a refusal.");
                var result = action();
                Require(Failure is null, "Transition cannot complete after a refusal.");
                lastSequence = observation.Sequence;
                return result;
            }
            catch (Exception error)
            {
                Failure ??= error.Message;
                throw new InvalidOperationException(Failure, error);
            }
            finally { inTransition = false; }
        }
    }
}
