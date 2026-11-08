namespace Azurite.CacheRootLab;

public enum CacheProbeMode { SeededPathOnly, OmittedSeedPathOnly, NormalCatalog }
public enum ProbeStage { New, Admitted, Seeding, Seeded, Building, ResultValidated, Destroyed, Complete, Failed }
public sealed record PreUseState(uint CrtState, ulong Allocator, ulong Manager, bool ExactInitialRoot, uint NativeThreadId);
public sealed class ProbeLedger
{
    private readonly CacheProbeMode mode;
    private readonly string expectedRoot;
    private readonly uint thread;
    public ProbeLedger(CacheProbeMode mode, string expectedRoot, uint thread) { this.mode = mode; this.expectedRoot = expectedRoot; this.thread = thread; }
    public ProbeStage Stage { get; private set; }
    public void Admit(PreUseState state)
    {
        Require(ProbeStage.New);
        if (mode == CacheProbeMode.NormalCatalog || state.CrtState != 2 || state.Allocator == 0 || state.Manager != 0 || !state.ExactInitialRoot || state.NativeThreadId != thread || thread == 0) Fail("Invalid path-only startup observation; it is not exclusivity proof.");
        Stage = ProbeStage.Admitted;
    }
    public void BeginSeed() { Require(ProbeStage.Admitted); if (mode != CacheProbeMode.SeededPathOnly) Fail("Omitted control cannot seed."); Stage = ProbeStage.Seeding; }
    public void FinishSeed(NativeStringValue root, bool inputAlias, ulong manager)
    {
        Require(ProbeStage.Seeding);
        if (!PathForm(root, 0x49) || root.Text != expectedRoot || inputAlias || manager != 0) Fail("Original helper readback/ownership/manager postcondition failed.");
        Stage = ProbeStage.Seeded;
    }
    public void BeginBuilder()
    {
        Require(mode == CacheProbeMode.SeededPathOnly ? ProbeStage.Seeded : ProbeStage.Admitted);
        Stage = ProbeStage.Building;
    }
    public void ValidateResult(bool sameResultPointer, bool canaries, NativeStringValue result, NativeStringValue root, ulong manager)
    {
        Require(ProbeStage.Building);
        if (!sameResultPointer || !canaries || !PathForm(result, 1) || !PathForm(root, 0x49) || manager != 0 || result.Text != root.Text.TrimEnd('/') + "/Unity") Fail("Original builder ABI/path/lifetime postcondition failed.");
        if (mode == CacheProbeMode.SeededPathOnly ? root.Text != expectedRoot : root.Text == expectedRoot) Fail("Seeded/omitted control was not distinguished.");
        Stage = ProbeStage.ResultValidated;
    }
    public void Destroyed() { Require(ProbeStage.ResultValidated); Stage = ProbeStage.Destroyed; }
    public void Complete() { Require(ProbeStage.Destroyed); Stage = ProbeStage.Complete; }
    private static bool PathForm(NativeStringValue v, ushort label) => v.Label == label && v.Kind is NativeStringKind.Inline or NativeStringKind.Owned && v.ByteLength is > 0 and <= NativeStringReader.MaximumBytes && !string.IsNullOrEmpty(v.Text) && !v.Text.Contains('\0');
    private void Require(ProbeStage expected) { if (Stage != expected) Fail("Invalid or repeated probe lifecycle operation."); }
    private void Fail(string reason) { Stage = ProbeStage.Failed; throw new InvalidOperationException(reason); }
}
