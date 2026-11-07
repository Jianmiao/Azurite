using Azurite.Core;

static class SelectionRevisionTests
{
    private static readonly BoxSelectionRow[] Rows = Enumerable.Range(1, 4)
        .Select(i => new BoxSelectionRow("line-" + i, new BoxSelectionRect(0, (i - 1) * 20, 100, i * 20))).ToArray();
    private static BoxSelectionPoint P(float x, float y) => new(x, y);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Selected(BoxSelectionModel model, params string[] expected) => Check(model.SelectedRowIds.SequenceEqual(expected), "selection differs: [" + string.Join(",", model.SelectedRowIds) + "]");
    private static void Click(BoxSelectionModel model, float y, BoxSelectionModifiers modifiers = BoxSelectionModifiers.None, bool blank = false, BoxSelectionRow[]? rows = null)
    {
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, P(blank ? 150 : 10, y), modifiers), "begin failed");
        model.End(rows ?? Rows);
    }
    private static void BeginPreview(BoxSelectionModel model)
    {
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, P(10, 25), BoxSelectionModifiers.None), "begin preview failed");
        Check(model.Move(P(90, 55), Rows), "preview did not activate");
        Selected(model, "line-2", "line-3");
    }
    private static void AdvancedOnce(BoxSelectionModel model, BoxSelectionSnapshot before)
    {
        var after = model.CreateSnapshot(Rows);
        Check(after.Revision == before.Revision + 1, $"expected one invalidation from {before.Revision}, got {after.Revision}");
        Check(before.Revision != model.SelectionRevision, "stale snapshot revision still accepted");
    }
    private static void Unchanged(BoxSelectionModel model, long revision) => Check(model.SelectionRevision == revision, $"no-op changed revision {revision}->{model.SelectionRevision}");

    private static readonly (string Name, Action Body)[] Cases =
    {
        ("Shift range invalidates snapshot once; repeated range is a no-op", () => {
            var m = new BoxSelectionModel(); Click(m, 5); var before = m.CreateSnapshot(Rows);
            Click(m, 45, BoxSelectionModifiers.Shift); Selected(m, "line-1", "line-2", "line-3"); AdvancedOnce(m, before);
            var revision = m.SelectionRevision; Click(m, 45, BoxSelectionModifiers.Shift); Unchanged(m, revision);
        }),
        ("blank clearing invalidates snapshot once; repeated blank is a no-op", () => {
            var m = new BoxSelectionModel(); Click(m, 5); var before = m.CreateSnapshot(Rows);
            Click(m, 100, blank: true); Selected(m); AdvancedOnce(m, before);
            var revision = m.SelectionRevision; Click(m, 100, blank: true); Unchanged(m, revision);
        }),
        ("Reconcile removal invalidates snapshot once; same rows are a no-op", () => {
            var m = new BoxSelectionModel(); Click(m, 5); Click(m, 45, BoxSelectionModifiers.Shift); var before = m.CreateSnapshot(Rows);
            m.Reconcile(new[] { Rows[0] }); Selected(m, "line-1"); AdvancedOnce(m, before);
            var revision = m.SelectionRevision; m.Reconcile(new[] { Rows[0] }); Unchanged(m, revision);
        }),
        ("Reconcile anchor-only invalidation changes revision and future Shift range", () => {
            var m = new BoxSelectionModel(); Click(m, 5); Click(m, 25, BoxSelectionModifiers.Control); Click(m, 25, BoxSelectionModifiers.Control);
            Selected(m, "line-1"); var before = m.CreateSnapshot(Rows);
            m.Reconcile(new[] { Rows[0], Rows[2], Rows[3] }); Selected(m, "line-1"); AdvancedOnce(m, before);
            Click(m, 65, BoxSelectionModifiers.Shift); Selected(m, "line-4");
        }),
        ("modified blank click prunes stale membership and anchor once", () => {
            var m = new BoxSelectionModel(); Click(m, 25); var before = m.CreateSnapshot(Rows);
            Click(m, 100, BoxSelectionModifiers.Control, blank:true, rows:new[] { Rows[0], Rows[2], Rows[3] }); Selected(m); AdvancedOnce(m, before);
            Click(m, 45, BoxSelectionModifiers.Shift); Selected(m, "line-3");
            Click(m, 65, BoxSelectionModifiers.Shift); Selected(m, "line-3", "line-4");
        }),
        ("drag preview increments only for effective changes and commit adds no duplicate", () => {
            var m = new BoxSelectionModel(); Click(m, 5); var before = m.CreateSnapshot(Rows); BeginPreview(m); AdvancedOnce(m, before);
            var revision = m.SelectionRevision; m.Move(P(90, 55), Rows); Unchanged(m, revision);
            m.End(Rows); Selected(m, "line-2", "line-3"); Unchanged(m, revision); m.End(Rows); Unchanged(m, revision);
        }),
        ("drag Cancel restores selection and anchor with one new revision", () => {
            var m = new BoxSelectionModel(); Click(m, 5); BeginPreview(m); var preview = m.CreateSnapshot(Rows);
            m.Cancel(); Selected(m, "line-1"); AdvancedOnce(m, preview);
            var revision = m.SelectionRevision; m.End(Rows); Unchanged(m, revision);
            Click(m, 45, BoxSelectionModifiers.Shift); Selected(m, "line-1", "line-2", "line-3");
        }),
        ("Cancel without an effective preview change does not invalidate", () => {
            var m = new BoxSelectionModel(); Click(m, 5); var revision = m.SelectionRevision;
            m.Begin(BoxSelectionPointerTarget.DialogueList, P(10, 5), BoxSelectionModifiers.None); m.Cancel(); Selected(m, "line-1"); Unchanged(m, revision);
            m.Cancel(); Unchanged(m, revision);
        }),
        ("Clear is change-detected while Refresh explicitly invalidates even an empty list", () => {
            var m = new BoxSelectionModel(); var revision = m.SelectionRevision; m.Clear(); Selected(m); Unchanged(m, revision);
            Click(m, 5); var before = m.CreateSnapshot(Rows); m.Clear(); Selected(m); AdvancedOnce(m, before);
            revision = m.SelectionRevision; m.Clear(); Unchanged(m, revision);
            before = m.CreateSnapshot(Rows); m.Refresh(); Selected(m); AdvancedOnce(m, before);
            before = m.CreateSnapshot(Rows); m.Refresh(); AdvancedOnce(m, before);
        }),
        ("Refresh during drag invalidates once and pending End cannot restore selection", () => {
            var m = new BoxSelectionModel(); Click(m, 5); BeginPreview(m); var before = m.CreateSnapshot(Rows);
            m.Refresh(Rows); Selected(m); AdvancedOnce(m, before); var revision = m.SelectionRevision;
            m.End(Rows); m.Cancel(); Selected(m); Unchanged(m, revision);
        }),
        ("Reconcile cancels preview back to valid pointer-down membership", () => {
            var m = new BoxSelectionModel(); Click(m, 5); BeginPreview(m); var before = m.CreateSnapshot(Rows);
            m.Reconcile(Rows); Selected(m, "line-1"); AdvancedOnce(m, before); var revision = m.SelectionRevision;
            m.End(Rows); Selected(m, "line-1"); Unchanged(m, revision);
        }),
        ("Reconcile cancellation filters invalid pointer-down IDs without resurrection", () => {
            var m = new BoxSelectionModel(); Click(m, 5); BeginPreview(m); var before = m.CreateSnapshot(Rows);
            m.Reconcile(new[] { Rows[1], Rows[2] }); Selected(m); AdvancedOnce(m, before); var revision = m.SelectionRevision;
            m.Cancel(); m.End(Rows); Selected(m); Unchanged(m, revision);
        }),
        ("new Begin cancels previous preview before starting a new gesture", () => {
            var m = new BoxSelectionModel(); Click(m, 5); BeginPreview(m); var before = m.CreateSnapshot(Rows);
            m.Begin(BoxSelectionPointerTarget.DialogueList, P(10, 65), BoxSelectionModifiers.None); Selected(m, "line-1"); AdvancedOnce(m, before);
            before = m.CreateSnapshot(Rows); m.End(Rows); Selected(m, "line-4"); AdvancedOnce(m, before);
        }),
        ("native-boundary Begin cancels prior preview without committing it", () => {
            var m = new BoxSelectionModel(); Click(m, 5); BeginPreview(m); var before = m.CreateSnapshot(Rows);
            Check(!m.Begin(BoxSelectionPointerTarget.InputField, P(10, 5), BoxSelectionModifiers.None), "native input consumed"); Selected(m, "line-1"); AdvancedOnce(m, before);
            var revision = m.SelectionRevision; m.End(Rows); Selected(m, "line-1"); Unchanged(m, revision);
        }),
        ("same membership with changed anchor invalidates exactly once", () => {
            var m = new BoxSelectionModel(); Click(m, 5); Click(m, 25, BoxSelectionModifiers.Control); var before = m.CreateSnapshot(Rows);
            m.Begin(BoxSelectionPointerTarget.DialogueList, P(10, 5), BoxSelectionModifiers.None); m.Move(P(90, 35), Rows); Selected(m, "line-1", "line-2"); AdvancedOnce(m, before);
            var revision = m.SelectionRevision; m.End(Rows); Unchanged(m, revision);
            Click(m, 65, BoxSelectionModifiers.Shift); Selected(m, "line-1", "line-2", "line-3", "line-4");
        }),
        ("plain repeated click and modified blank do not invalidate unchanged selection", () => {
            var m = new BoxSelectionModel(); Click(m, 5); var revision = m.SelectionRevision; Click(m, 5); Selected(m, "line-1"); Unchanged(m, revision);
            Click(m, 100, BoxSelectionModifiers.Control, blank:true); Unchanged(m, revision); Click(m, 100, BoxSelectionModifiers.Shift, blank:true); Unchanged(m, revision);
        })
    };
    public static int Run()
    {
        var failed = 0;
        foreach (var (name, body) in Cases) { try { body(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
        Console.WriteLine($"REVISION RESULT {Cases.Length - failed}/{Cases.Length} passed"); return failed == 0 ? 0 : 1;
    }
}
