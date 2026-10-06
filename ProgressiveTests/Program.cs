using System.Collections;
using Azurite;

var tests = new (string Name, Action Body)[]
{
    ("editor unlocks structure editing before resources settle", EditorUnlocksBeforeResources),
    ("resource dependent work stays gated until preload completion", ResourceWorkGate),
    ("export handoff blocks edits without cancelling preload", ExportHandoff),
    ("load failure releases the operation gate", FailureDoesNotDeadlock),
    ("close and cancel are terminal and idempotent", CloseAndCancelAreIdempotent),
    ("completion releases active slot exactly once", CompletionReleases),
    ("four millisecond budget yields without advancing native work", BudgetYields),
    ("four active resources cap new starts per frame", ActiveResourceCap),
    ("nested child is passed through the wrapper", NestedChild),
    ("cancellation disposes the native enumerator once", CancellationDisposes)
};

static void EditorUnlocksBeforeResources()
{
    var gate = new ProgressiveEditorGate();
    Check(gate.Begin(), "load enters the loading phase");
    Check(!gate.AllowsStructureEditing && gate.BlocksEditorOperations, "loading protects the editor before its shell exists");
    Check(gate.EditorShown(), "editor shell transition is accepted");
    Check(gate.AllowsStructureEditing && !gate.BlocksEditorOperations, "structure and dialogue editing unlocks at shell readiness");
    Check(!gate.AllowsResourceDependentWork && gate.ContinuePreload, "resource work remains gated while preload continues");
}

static void ResourceWorkGate()
{
    var gate = new ProgressiveEditorGate();
    gate.Begin();
    gate.EditorShown();
    Check(!gate.AllowsResourceDependentWork && gate.ContinuePreload, "resource-dependent work stays gated before preload settles");
    Check(gate.Phase == ProgressiveEditorPhase.EditorReady, "editor stays in the resource-loading phase");
    Check(gate.MarkResourcesReady(), "resource completion is accepted");
    Check(gate.Phase == ProgressiveEditorPhase.Ready && gate.AllowsResourceDependentWork, "preview and background work unlock after preload");
}

static void ExportHandoff()
{
    var gate = new ProgressiveEditorGate();
    gate.Begin();
    gate.EditorShown();
    Check(gate.AcquireExport(), "export can acquire rendering ownership during background preload");
    Check(gate.ExportOwned && gate.BlocksEditorOperations && gate.ContinuePreload, "export blocks edits but keeps preload alive");
    Check(gate.ReleaseExport(resourcesReady: false), "export release restores the editor-ready phase");
    Check(gate.AllowsStructureEditing && !gate.AllowsResourceDependentWork, "resource gate remains conservative after early export");
    Check(gate.AcquireExport(), "export can reacquire after release");
    Check(!gate.ResourcesCompleted, "export does not falsely mark resources complete");
    Check(gate.MarkResourcesReady(), "preload may complete while export owns rendering");
    Check(gate.ReleaseExport(gate.ResourcesCompleted) && gate.AllowsResourceDependentWork, "completed preload is remembered across export");
}

static void FailureDoesNotDeadlock()
{
	var beforeShell = new ProgressiveEditorGate();
	beforeShell.Begin();
	Check(beforeShell.Fail(), "pre-shell failure transitions to failed");
	Check(beforeShell.BlocksEditorOperations && !beforeShell.IsEditorVisible, "pre-shell failure does not expose an editor that never started");
    var gate = new ProgressiveEditorGate();
    gate.Begin();
    gate.EditorShown();
    Check(gate.Fail(), "load failure transitions to failed");
    Check(!gate.BlocksEditorOperations && !gate.ContinuePreload && !gate.ExportOwned, "failure does not leave a permanent loading operation gate");
    Check(!gate.Fail(), "repeated failure is idempotent");
}

static void CloseAndCancelAreIdempotent()
{
    var cancelled = new ProgressiveEditorGate();
    cancelled.Begin();
    Check(cancelled.Cancel(), "cancel transitions the preload");
    Check(!cancelled.Cancel() && !cancelled.Begin(), "cancel is terminal until a fresh session is created");
    var closed = new ProgressiveEditorGate();
    closed.Begin();
    Check(closed.Close(), "close transitions the editor");
    Check(!closed.Close() && !closed.Begin(), "close is idempotent and terminal");
}

var failed = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
}

Console.WriteLine($"RESULT {tests.Length - failed}/{tests.Length}; source-linked fake coroutine tests only; Unity/IL2CPP lifecycle is not executed.");
return failed == 0 ? 0 : 1;

static void CompletionReleases()
{
    var frame = 0;
    var now = 0d;
    var budget = new ProgressivePreloadBudget(() => frame, () => now);
    var native = new FakeEnumerator(1);
    using var runner = new ProgressivePreloadEnumerator(native, budget, value => value);
    Check(runner.MoveNext(), "first resource step should yield");
    Check(budget.Active == 1 && budget.Completed == 0, "resource should be active before completion");
    Check(!runner.MoveNext(), "second step should complete the resource");
    Check(budget.Active == 0 && budget.Completed == 1, "completion should release and count once");
    runner.Dispose();
    Check(native.DisposeCount == 1, "disposing completed runner must be idempotent");
}

static void BudgetYields()
{
    var frame = 0;
    var now = 0d;
    var budget = new ProgressivePreloadBudget(() => frame, () => now);
    var first = new FakeEnumerator(2, () => now += 5);
    var second = new FakeEnumerator(1);
    using var firstRunner = new ProgressivePreloadEnumerator(first, budget, value => value);
    using var secondRunner = new ProgressivePreloadEnumerator(second, budget, value => value);
    Check(firstRunner.MoveNext(), "first resource should yield");
    Check(first.MoveCount == 1, "first native step should run once");
    Check(secondRunner.MoveNext(), "budget exhaustion should yield a null wait step");
    Check(second.MoveCount == 0 && secondRunner.Current == null, "budget wait must not advance native work");
    frame++;
    Check(secondRunner.MoveNext(), "next frame should resume the second resource");
    Check(second.MoveCount == 1, "second resource should advance after the frame reset");
}

static void ActiveResourceCap()
{
    var frame = 0;
    var now = 0d;
    var budget = new ProgressivePreloadBudget(() => frame, () => now);
	var natives = Enumerable.Range(0, 5).Select(_ => new FakeEnumerator(1)).ToArray();
	var runners = natives
		.Select(native => new ProgressivePreloadEnumerator(native, budget, value => value))
		.ToArray();
    try
    {
        foreach (var runner in runners) Check(runner.MoveNext(), "each runner should yield while the batch is pending");
        Check(budget.Active == 4, "only four resources may be active");
		Check(natives[4].MoveCount == 0, "fifth resource must wait before native start");
        frame++;
        for (var i = 0; i < 4; i++) Check(!runners[i].MoveNext(), "first four resources should complete");
        Check(budget.Active == 0, "completed resources should release all active slots");
        Check(runners[4].MoveNext(), "the fifth resource may start after a slot is released");
        Check(budget.Active == 1 && natives[4].MoveCount == 1, "the fifth resource should start only after retry");
    }
    finally { foreach (var runner in runners) runner.Dispose(); }
}

static void NestedChild()
{
    var frame = 0;
    var now = 0d;
    var budget = new ProgressivePreloadBudget(() => frame, () => now);
    var child = new FakeEnumerator(1);
    var parent = new FakeEnumerator(1, current: child);
    object? wrapped = null;
    using var runner = new ProgressivePreloadEnumerator(parent, budget, value => wrapped = value);
    Check(runner.MoveNext(), "parent should yield");
    Check(ReferenceEquals(wrapped, child), "nested enumerator must be exposed to the parent flattener");
}

static void CancellationDisposes()
{
    var frame = 0;
    var now = 0d;
    var budget = new ProgressivePreloadBudget(() => frame, () => now);
    var native = new FakeEnumerator(5);
    var runner = new ProgressivePreloadEnumerator(native, budget, value => value);
    Check(runner.MoveNext(), "resource should start before cancellation");
    budget.Cancel();
    Check(!runner.MoveNext(), "cancelled resource should stop");
    runner.Dispose();
    Check(native.DisposeCount == 1 && budget.Active == 0, "cancellation should release and dispose exactly once");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeEnumerator : IEnumerator, IDisposable
{
    private int _remaining;
    private readonly Action? _onMove;
    private readonly object? _current;
    public int MoveCount { get; private set; }
    public int DisposeCount { get; private set; }
    public object? Current { get; private set; }

    public FakeEnumerator(int remaining, Action? onMove = null, object? current = null)
    {
        _remaining = remaining;
        _onMove = onMove;
        _current = current ?? new object();
    }

    public bool MoveNext()
    {
        MoveCount++;
        if (_remaining-- <= 0) { Current = null; return false; }
        _onMove?.Invoke();
        Current = _current;
        return true;
    }

    public void Reset() => throw new NotSupportedException();
    public void Dispose() => DisposeCount++;
}
