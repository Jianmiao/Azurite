using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azurite.CacheRootLab;

/// <summary>
/// Explicit forward-call component; no BepInEx plugin/patcher/initializer attribute and no automatic execution.
/// Preparing binds original code before any future independent consumer detour installation.
/// A separately validated live pre-use lease is mandatory; this library does not manufacture that proof.
/// </summary>
public sealed class PreparedPathProbe
{
    private static int processAttemptClaimed;
    private readonly CacheProbeRequest request;
    private readonly CacheProbeContext context;
    private readonly LoadedBindings native;
    private readonly IPreUseAdmissionLease admission;
    private readonly Action<CacheProbeEvent> evidence;
    private readonly ProbeLedger ledger;
    private long sequence;
    private PathProbeOutcome? outcome;
    public CacheProbeContext Context => context;
    public ulong ConsumerInitializerAddress => native.ConsumerEntry;

    private PreparedPathProbe(CacheProbeRequest request, CacheProbeContext context, LoadedBindings native, IPreUseAdmissionLease admission, Action<CacheProbeEvent> evidence)
    {
        this.request = request; this.context = context; this.native = native; this.admission = admission; this.evidence = evidence;
        ledger = new ProbeLedger(request.Mode, SeedPathPolicy.ValidateText(context.SeedRoot), context.NativeThreadId);
    }
    public static PreparedPathProbe Prepare(CacheProbeRequest request, IPreUseAdmissionLease admission, Action<CacheProbeEvent> evidence)
    {
        ProbeAdmission.RequireOwnedHost(); // Outside the approved child: reject without terminating an unrelated caller.
        CacheProbeContext? context = null;
        try
        {
            if (admission is null || evidence is null || admission.EvidenceSha256.Length != 64 || !admission.EvidenceSha256.All(Uri.IsHexDigit)) throw new InvalidOperationException("An explicit validated pre-use lease and correlated evidence sink are required.");
            context = ProbeAdmission.ReadRequest(request);
            admission.AssertPermitted(context, OriginalCall.Bind);
            var bindings = new LoadedBindings(context);
            ProbeAdmission.CheckLiveCli(context);
            var prepared = new PreparedPathProbe(request, context, bindings, admission, evidence);
            prepared.Emit("cache-root-original-binding-verified", new { bindings.Base, root = bindings.Root, initializer = bindings.ConsumerEntry, admissionEvidence = admission.EvidenceSha256, normalQualified = false, consumerInterceptionQualified = false });
            return prepared;
        }
        catch (Exception e)
        {
            if (context is not null) try { evidence?.Invoke(new CacheProbeEvent(1, DateTime.UtcNow, Stopwatch.GetTimestamp(), context.RunId, context.Pid, context.ManifestSha256, "cache-root-binding-rejected", new { reason = e.Message, nativeCacheForwardCallMade = false })); } catch { }
            ProbeAdmission.AbortOwnedChild(); throw;
        }
    }

    public PathProbeOutcome RunPathOnly()
    {
        try
        {
            if (Interlocked.CompareExchange(ref processAttemptClaimed, 1, 0) != 0) throw new InvalidOperationException("This child has already consumed its one-shot cache-root attempt.");
            Check(OriginalCall.Bind);
            ledger.Admit(native.Observe());
            Emit("cache-root-path-only-admitted", new { mode = request.Mode.ToString(), nativeThread = context.NativeThreadId, observationsAreNotExclusivityProof = true, admissionEvidence = admission.EvidenceSha256 });
            if (request.Mode == CacheProbeMode.SeededPathOnly) SeedOnce();

            Check(OriginalCall.BuildPath);
            ledger.BeginBuilder();
            var result = new NativeResultStorage(); // No native constructor: original builder constructs this slot.
            var suffixByte = Marshal.AllocHGlobal(1); Marshal.WriteByte(suffixByte, 0);
            var suffixSpan = Marshal.AllocHGlobal(16); Marshal.WriteIntPtr(suffixSpan, suffixByte); Marshal.WriteInt64(suffixSpan, 8, 0);
            admission.AssertPermitted(context, OriginalCall.BuildPath);
            var rootBeforeBuild = native.Strings.Read(native.Root, expectedLabel: 0x49);
            if (native.Manager != 0 || (request.Mode == CacheProbeMode.SeededPathOnly ? rootBeforeBuild.Text != SeedPathPolicy.ValidateText(context.SeedRoot) : !native.Observe().ExactInitialRoot)) throw new InvalidOperationException("Pre-builder root/manager drift.");
            // On any abnormal call/object outcome, the catch terminates the child without guessing cleanup.
            var returned = native.Build((IntPtr)(long)result.Address, suffixSpan, 0);
            var canaries = result.CanariesIntact(native.Memory);
            if ((ulong)returned.ToInt64() != result.Address || !canaries) throw new InvalidOperationException("Native builder returned an invalid result pointer or corrupted storage.");
            var value = native.Strings.Read(result.Address, expectedLabel: 1);
            if (value.Kind == NativeStringKind.Owned && (MemoryRange.Overlaps(value.DataAddress, (ulong)value.ByteLength + 1, result.AllocationAddress, (ulong)result.AllocationBytes) ||
                MemoryRange.Overlaps(value.DataAddress, (ulong)value.ByteLength + 1, (ulong)suffixSpan.ToInt64(), 16) ||
                MemoryRange.Overlaps(value.DataAddress, (ulong)value.ByteLength + 1, (ulong)suffixByte.ToInt64(), 1))) throw new InvalidOperationException("Builder owned data aliases harness input/storage.");
            var root = native.Strings.Read(native.Root, expectedLabel: 0x49);
            if (value.Kind == NativeStringKind.Owned && (MemoryRange.Overlaps(value.DataAddress, (ulong)value.ByteLength + 1, native.Root, 0x28) ||
                MemoryRange.Overlaps(value.DataAddress, (ulong)value.ByteLength + 1, root.DataAddress, (ulong)root.ByteLength + 1))) throw new InvalidOperationException("Builder result shares global-owned storage.");
            ledger.ValidateResult(true, canaries, value, root, native.Manager);
            Emit("cache-root-original-builder-returned", new { returned = returned.ToInt64(), copiedPath = value.Text, value.Tag, value.Label, root = root.Text, manager = native.Manager, transform = 0, emptySuffix = true });

            Check(OriginalCall.DestroyResult);
            admission.AssertPermitted(context, OriginalCall.DestroyResult);
            native.Destroy((IntPtr)(long)result.Address); // Exactly once, only the validated local builder result.
            ledger.Destroyed();
            if (!result.CanariesIntact(native.Memory)) throw new InvalidOperationException("Native destructor changed outside its local slot.");
            result.Dispose(); Marshal.FreeHGlobal(suffixSpan); Marshal.FreeHGlobal(suffixByte);
            Check(OriginalCall.Finish);
            var finalRoot = native.Strings.Read(native.Root, expectedLabel: 0x49);
            if (finalRoot.Text != root.Text || native.Manager != 0) throw new InvalidOperationException("Root changed or manager initialized after local-result cleanup.");
            ledger.Complete();
            outcome = new PathProbeOutcome(request.Mode, finalRoot.Text, value.Text, admission.EvidenceSha256, false, false);
            Emit("cache-root-path-only-complete", new { outcome, originalLocalDestructorCalls = 1, globalDestroyed = false, consumerEntryInvoked = false, normalQualified = false });
            return outcome;
        }
        catch (Exception e) { RejectAndAbort(e); throw; }
    }

    private void SeedOnce()
    {
        Check(OriginalCall.SeedRoot); ledger.BeginSeed();
        var inputText = context.SeedRoot; _ = SeedPathPolicy.ValidateText(inputText);
        var input = Marshal.StringToCoTaskMemUni(inputText); var inputBytes = checked((inputText.Length + 1) * 2);
        admission.AssertPermitted(context, OriginalCall.SeedRoot);
        var preSeed = native.Observe();
        if (!preSeed.ExactInitialRoot || preSeed.Manager != 0 || preSeed.CrtState != 2 || preSeed.Allocator == 0 || preSeed.NativeThreadId != context.NativeThreadId) throw new InvalidOperationException("Immediate pre-seed native state drifted; no overwrite/retry allowed.");
        native.Seed(input, (IntPtr)(long)native.Root); // Destination is fixed to the verified module's existing object.
        var root = native.Strings.Read(native.Root, expectedLabel: 0x49);
        var aliasesInput = MemoryRange.Overlaps(root.DataAddress, (ulong)root.ByteLength + 1, (ulong)input.ToInt64(), (ulong)inputBytes);
        ledger.FinishSeed(root, aliasesInput, native.Manager);
        Marshal.FreeCoTaskMem(input); // Original helper copies; it never acquires ownership of this buffer.
        var afterFree = native.Strings.Read(native.Root, expectedLabel: 0x49);
        if (afterFree.Text != root.Text || afterFree.Label != 0x49 || native.Manager != 0) throw new InvalidOperationException("Seed did not survive release of its owned input.");
        Emit("cache-root-original-helper-readback", new { copiedRoot = afterFree.Text, afterFree.Tag, afterFree.Label, inputReleased = true, globalDestination = native.Root, manager = native.Manager });
    }

    /// <summary>Policy/decoder for a later independent initializer hook; this does not install or prove interception.</summary>
    public NativeStringValue InspectInitializerForOwnedQualification(IntPtr rawNativeCache, byte readIndex, string expectedCachePath)
    {
        try
        {
            Check(OriginalCall.Finish);
            if (outcome is null || outcome.Mode != CacheProbeMode.SeededPathOnly || ledger.Stage != ProbeStage.Complete) throw new InvalidOperationException("Original positive builder proof is absent.");
            var normalized = SeedPathPolicy.ValidateText(expectedCachePath);
            if (!expectedCachePath.StartsWith(context.RunRoot + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Consumer qualification path escapes the bound run.");
            SeedPathPolicy.RequireExistingNoReparse(expectedCachePath);
            var cacheAddress = (ulong)rawNativeCache.ToInt64(); MemoryRange.Check(cacheAddress, 0x60);
            var path = native.Strings.Read(cacheAddress + 0x38, expectedLabel: 9);
            var root = native.Strings.Read(native.Root, expectedLabel: 0x49);
            ConsumerGuardPolicy.Validate(ConsumerPhase.OwnedConsumerQualification, root, path, outcome.Root, normalized, true, readIndex);
            Emit("cache-root-consumer-policy-admitted", new { cache = cacheAddress, copiedPath = path.Text, readIndex, originalEntryInvoked = false, interceptionProved = false });
            return path;
        }
        catch (Exception e) { RejectAndAbort(e); throw; }
    }
    private void Check(OriginalCall call)
    {
        admission.AssertPermitted(context, call);
        ProbeAdmission.CheckLiveCli(context);
        SeedPathPolicy.RequireExistingNoReparse(request.ManifestPath);
        if (new FileInfo(request.ManifestPath).Length > 16384) throw new InvalidOperationException("Bound manifest size changed.");
        if (PinContract.Hash(File.ReadAllBytes(request.ManifestPath)) != context.ManifestSha256) throw new InvalidOperationException("Bound manifest changed.");
        native.VerifyCode(allWindows: false); // Future independent initializer detour may differ; forward entries may not.
    }
    private void Emit(string kind, object data) => evidence(new CacheProbeEvent(++sequence, DateTime.UtcNow, Stopwatch.GetTimestamp(), context.RunId, context.Pid, context.ManifestSha256, kind, data));
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void RejectAndAbort(Exception e)
    {
        try { Emit("cache-root-rejected", new { reason = e.Message, normalQualified = false, continueNativeStartup = false }); } catch { }
        ProbeAdmission.AbortOwnedChild();
    }
}
