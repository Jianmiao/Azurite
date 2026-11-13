using Azurite.Core;

internal static class SelectionSnapshotTests
{
    public static int Run()
    {
        (string Name, Action Test)[] tests =
        {
            (nameof(RejectsStalePlanAfterSourceListReplacement), RejectsStalePlanAfterSourceListReplacement),
            (nameof(RetainsRowsAfterSourceListClear), RetainsRowsAfterSourceListClear),
            (nameof(RetainsRowsAfterSourceArrayReplacement), RetainsRowsAfterSourceArrayReplacement),
            (nameof(MatchesEquivalentIndependentSnapshots), MatchesEquivalentIndependentSnapshots),
            (nameof(RejectsMutationThroughExposedCollection), RejectsMutationThroughExposedCollection)
        };
        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"SelectionSnapshotTests: {tests.Length - failed}/{tests.Length} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void RejectsStalePlanAfterSourceListReplacement()
    {
        var rows = new List<AuthoringBatchRow> { new("line-old", 0, "old text", 1) };
        var expected = new AuthoringBatchSelection(7, "node", rows);
        AuthoringBatchPlanResult result = AuthoringBatchPlanner.TryPlan(expected, AuthoringBatchAction.SetText, "replacement");
        Check(result.IsValid && result.Plan is not null, "initial SetText plan must be valid");

        rows[0] = new AuthoringBatchRow("line-new", 1, "new text", 2);
        var current = new AuthoringBatchSelection(7, "node", rows);

        Check(result.Plan!.Edits.Count == 1 && result.Plan.Edits[0].StableId == "line-old" &&
              result.Plan.Edits[0].SourceIndex == 0 && result.Plan.Edits[0].Text == "replacement",
              "compiled plan must retain the previewed target and text");
        Check(!AuthoringBatchPlanner.Matches(expected, current),
              "changed selection must invalidate the old plan even when node and revision are unchanged");
    }

    private static void RetainsRowsAfterSourceListClear()
    {
        var rows = new List<AuthoringBatchRow> { new("line-old", 0, "old text", 1) };
        var snapshot = new AuthoringBatchSelection(7, "node", rows);

        rows.Clear();

        Check(snapshot.Rows.Count == 1, "clearing the source list must not clear the captured selection");
        Check(snapshot.Rows[0].StableId == "line-old", "captured row identity must survive source clear");
    }

    private static void RetainsRowsAfterSourceArrayReplacement()
    {
        var rows = new[] { new AuthoringBatchRow("line-old", 0, "old text", 1) };
        var snapshot = new AuthoringBatchSelection(7, "node", rows);

        rows[0] = new AuthoringBatchRow("line-new", 1, "new text", 2);

        Check(snapshot.Rows.Count == 1 && snapshot.Rows[0].StableId == "line-old" &&
              snapshot.Rows[0].Index == 0 && snapshot.Rows[0].Text == "old text" && snapshot.Rows[0].SpeakerSlot == 1,
              "replacing a source array entry must not change captured row values");
    }

    private static void MatchesEquivalentIndependentSnapshots()
    {
        var expected = new AuthoringBatchSelection(7, "node",
            new[] { new AuthoringBatchRow("line-old", 0, "old text", 1) });
        var current = new AuthoringBatchSelection(7, "node",
            new List<AuthoringBatchRow> { new("line-old", 0, "old text", 1) });

        Check(AuthoringBatchPlanner.Matches(expected, current),
              "independently captured equal row values must still match");
    }

    private static void RejectsMutationThroughExposedCollection()
    {
        var snapshot = new AuthoringBatchSelection(7, "node",
            new List<AuthoringBatchRow> { new("line-old", 0, "old text", 1) });

        if (snapshot.Rows is IList<AuthoringBatchRow> writable)
        {
            bool rejected = false;
            try
            {
                writable[0] = new AuthoringBatchRow("line-new", 1, "new text", 2);
            }
            catch (NotSupportedException)
            {
                rejected = true;
            }
            Check(rejected, "public Rows must reject mutation through IList");
        }

        Check(snapshot.Rows.Count == 1 && snapshot.Rows[0].StableId == "line-old",
              "the exposed collection must preserve captured rows");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
