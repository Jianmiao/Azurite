using Azurite.Core;

static class BoxSelectionTests
{
    private static readonly BoxSelectionRow[] Rows =
    {
        Row("line-1", 0),
        Row("line-2", 20),
        Row("line-3", 40),
        Row("line-4", 60)
    };

    private static BoxSelectionRow Row(string id, float top, bool isValid = true) =>
        new(id, new BoxSelectionRect(0, top, 100, top + 20), isValid);

    private static BoxSelectionPoint Point(float x, float y) => new(x, y);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckSelection(BoxSelectionModel model, params string[] expected)
    {
        var actual = model.SelectedRowIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var sortedExpected = expected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Check(actual.SequenceEqual(sortedExpected),
            $"expected selection [{string.Join(",", sortedExpected)}], got [{string.Join(",", actual)}]");
    }

    private static void Drag(BoxSelectionModel model, BoxSelectionPoint start, BoxSelectionPoint end,
        BoxSelectionModifiers modifiers = BoxSelectionModifiers.None,
        BoxSelectionPointerTarget target = BoxSelectionPointerTarget.DialogueList)
    {
        Check(model.Begin(target, start, modifiers), "dialogue-list pointer should be consumed");
        model.Move(end, Rows);
        model.End(Rows);
    }

    private static void Click(BoxSelectionModel model, BoxSelectionPoint point,
        BoxSelectionModifiers modifiers = BoxSelectionModifiers.None)
    {
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, point, modifiers), "row click should be consumed");
        model.End(Rows);
    }

    private static void SmallMoveDoesNotStartBoxSelection()
    {
        var model = new BoxSelectionModel();
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, Point(10, 10), BoxSelectionModifiers.None), "begin");
        Check(!model.Move(Point(15, 13), Rows), "movement below six pixels must remain a click");
        Check(!model.IsDragging, "sub-threshold movement must not enter box selection");
        Check(model.SelectionRectangle is null, "sub-threshold movement has no selection rectangle");
        model.End(Rows);
        CheckSelection(model, "line-1");

        model.Clear();
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, Point(10, 10), BoxSelectionModifiers.None), "exact-threshold begin");
        Check(!model.Move(Point(16, 10), Rows), "exactly six pixels remains a click");
        Check(!model.IsDragging, "exact-threshold movement must not enter box selection");
        model.End(Rows);
    }

    private static void ForwardAndReverseRectanglesSelectIntersectingRows()
    {
        var model = new BoxSelectionModel();
        Drag(model, Point(10, 10), Point(90, 55));
        CheckSelection(model, "line-1", "line-2", "line-3");

        model.Clear();
        Drag(model, Point(90, 55), Point(10, 10));
        CheckSelection(model, "line-1", "line-2", "line-3");
    }

    private static void CtrlBoxSelectionAddsAndTogglesRows()
    {
        var model = new BoxSelectionModel();
        Drag(model, Point(10, 5), Point(90, 15));
        Drag(model, Point(10, 25), Point(90, 35), BoxSelectionModifiers.Control);
        CheckSelection(model, "line-1", "line-2");

        Drag(model, Point(10, 5), Point(90, 15), BoxSelectionModifiers.Control);
        CheckSelection(model, "line-2");
    }

    private static void PlainBoxSelectionReplacesAndBlankClickClears()
    {
        var model = new BoxSelectionModel();
        Drag(model, Point(10, 5), Point(90, 15));
        Drag(model, Point(10, 65), Point(90, 75));
        CheckSelection(model, "line-4");

        Click(model, Point(150, 100));
        CheckSelection(model);

        Click(model, Point(10, 25));
        Drag(model, Point(150, 100), Point(180, 130));
        CheckSelection(model);
        Click(model, Point(10, 65), BoxSelectionModifiers.Shift);
        CheckSelection(model, "line-4");
    }

    private static void ShiftClickSelectsContinuousRange()
    {
        var model = new BoxSelectionModel();
        Click(model, Point(10, 25));
        Click(model, Point(10, 65), BoxSelectionModifiers.Shift);
        CheckSelection(model, "line-2", "line-3", "line-4");
    }

    private static void InvalidRowsNeverEnterSelection()
    {
        var model = new BoxSelectionModel();
        var rows = new[] { Row("line-1", 0), Row("stale", 20, isValid: false), Row("line-3", 40) };
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, Point(10, 5), BoxSelectionModifiers.None), "begin");
        model.Move(Point(90, 55), rows);
        model.End(rows);
        CheckSelection(model, "line-1", "line-3");
    }

    private static void RefreshClearsSelectionAndCancelsPendingGesture()
    {
        var model = new BoxSelectionModel();
        Drag(model, Point(10, 5), Point(90, 15));
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, Point(10, 5), BoxSelectionModifiers.None), "begin second gesture");
        model.Refresh(Rows);
        CheckSelection(model);
        Check(!model.IsDragging && model.SelectionRectangle is null, "refresh cancels pending gesture");
        model.End(Rows);
        CheckSelection(model);
    }

    private static void ReconcileDropsOnlyInvalidRows()
    {
        var model = new BoxSelectionModel();
        Drag(model, Point(10, 5), Point(90, 35));
        CheckSelection(model, "line-1", "line-2");
        var rows = new[] { Row("line-1", 0), Row("line-2", 20, isValid: false), Row("line-3", 40) };
        model.Reconcile(rows);
        CheckSelection(model, "line-1");
        Check(!model.IsDragging && model.SelectionRectangle is null, "reconcile cancels a stale gesture");
    }

    private static void NativeBoundariesDoNotConsumePointer()
    {
        foreach (var target in new[]
        {
            BoxSelectionPointerTarget.InputField,
            BoxSelectionPointerTarget.Scrollbar,
            BoxSelectionPointerTarget.RowDragHandle,
            BoxSelectionPointerTarget.OutsideDialogueList
        })
        {
            var model = new BoxSelectionModel();
            Check(!model.Begin(target, Point(10, 10), BoxSelectionModifiers.None), $"{target} must remain native");
            Check(!model.IsDragging, $"{target} must not start a gesture");
            model.Move(Point(90, 90), Rows);
            model.End(Rows);
            CheckSelection(model);
        }
    }

    private static void SelectionDoesNotMutateDialogueRows()
    {
        var rows = Rows.ToArray();
        var snapshot = rows.ToArray();
        var model = new BoxSelectionModel();
        Drag(model, Point(10, 5), Point(90, 75));
        Check(rows.SequenceEqual(snapshot), "box selection must only change UI selection state");
    }

    private static void DragPreviewUpdatesBeforeReleaseAndCancelRestores()
    {
        var model = new BoxSelectionModel();
        Click(model, Point(10, 5));
        CheckSelection(model, "line-1");
        Check(model.Begin(BoxSelectionPointerTarget.DialogueList, Point(10, 25), BoxSelectionModifiers.None), "begin drag");
        Check(model.Move(Point(90, 55), Rows), "drag should activate");
        CheckSelection(model, "line-2", "line-3");
        model.Cancel();
        CheckSelection(model, "line-1");
    }

    private static readonly (string Name, Action Test)[] Cases =
    {
        ("sub-threshold move remains a click", SmallMoveDoesNotStartBoxSelection),
        ("forward and reverse rectangles intersect rows", ForwardAndReverseRectanglesSelectIntersectingRows),
        ("Ctrl box selection adds and toggles", CtrlBoxSelectionAddsAndTogglesRows),
        ("plain box selection replaces and blank click clears", PlainBoxSelectionReplacesAndBlankClickClears),
        ("Shift click selects a continuous range", ShiftClickSelectsContinuousRange),
        ("invalid rows never enter selection", InvalidRowsNeverEnterSelection),
        ("refresh clears selection and pending gesture", RefreshClearsSelectionAndCancelsPendingGesture),
        ("reconcile drops only invalid rows", ReconcileDropsOnlyInvalidRows),
        ("native input, scrollbar and drag boundaries do not consume", NativeBoundariesDoNotConsumePointer),
        ("selection does not mutate dialogue rows", SelectionDoesNotMutateDialogueRows)
        , ("drag preview updates and cancel restores", DragPreviewUpdatesBeforeReleaseAndCancelRestores)
    };

    public static int Run()
    {
        int failed = 0;
        foreach (var (name, test) in Cases)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + error.Message);
            }
        }

        Console.WriteLine($"RESULT {Cases.Length - failed}/{Cases.Length} passed");
        return failed == 0 ? 0 : 1;
    }
}

static class Program
{
    public static int Main() { var original = BoxSelectionTests.Run(); var revision = SelectionRevisionTests.Run(); return original == 0 && revision == 0 ? 0 : 1; }
}

