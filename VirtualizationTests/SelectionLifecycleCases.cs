using Studio.Scripts;
using Studio.Scripts.Nodes;

// Source-linked lifecycle regressions. These exercise native selection ownership,
// row highlight and selected model identity separately; pixels remain a host check.
static class SelectionLifecycleCases
{
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static int Highlights(Fixture f) => f.Host.scriptList.transform.Children.Count(t => t.gameObject.Row?.selected == true && t.gameObject.activeInHierarchy);
    private static void AssertSelection(Fixture f, ScriptListItem row)
    {
        Check(f.Host.selectedScriptItem == row && row.selected, "inspector owner and row highlight disagree");
        var model = f.Host.scriptNode.scripts[row.index];
        Check(f.Host.PaneScript == model && f.Host.PreviewScript == model, "right pane and preview must target the exact selected model");
        Check(Highlights(f) == 1, "exactly one row may retain the selected highlight");
    }

    public static void LoadBeforeStart()
    {
        foreach (int count in new[] { 21, 640, 5000 })
        foreach (bool alreadyLoading in new[] { false, true })
        {
            using var f = new Fixture(count, load: false);
            f.Load();
            var row = f.Host.selectedScriptItem!;
            int selects = HostStats.Selections, callbacks = HostStats.ChildSelections, created = HostStats.Created;
            f.Host.loading = alreadyLoading;
            f.Host.Start();
            Check(f.Host.selectedScriptItem == row, $"Start cleared the original tail selection for {count} rows");
            AssertSelection(f, row);
            Check(HostStats.Selections == selects, "repair must not rerun Select");
            Check(HostStats.ChildSelections == callbacks + 1, "initialized pane must be restored once through native OnChildSelect");
            Check(f.Host.LastSelectionLoading && f.Host.loading == alreadyLoading, "initialization sync must use native history guard and restore loading flag");
            f.Host.loading = false;
            Check(HostStats.Created == created, "selection repair must not recreate rows");
            var victim = f.Host.scriptNode.scripts[row.index];
            f.Host.DeleteScript();
            Check(f.Host.scriptNode.scripts.Count == count - 1 && !f.Data().Contains(victim.Pointer), "original tail can be deleted immediately after Start");
            AssertSelection(f, f.Host.selectedScriptItem!);
        }
    }

    public static void NativeBaselineReproduces()
    {
        using var f = new Fixture(21, enabled: false, restored: 20);
        var tail = f.Host.selectedScriptItem!;
        f.Host.Start();
        Check(tail.selected && f.Host.selectedScriptItem == null, "native baseline must reproduce owner/highlight split");
        f.Host.DeleteScript();
        Check(f.Host.scriptNode.scripts.Count == 21, "ownerless tail cannot be deleted");
        tail.Select();
        Check(f.Host.selectedScriptItem == null, "already-selected native tail ignores the click");
        var previous = f.Host.scriptNodeListItemsCache[19];
        previous.Select();
        Check(f.Host.selectedScriptItem == previous && Highlights(f) == 2, "native owner-only deselection leaves two gold rows");
        tail.Select();
        Check(f.Host.selectedScriptItem == previous && f.Host.PaneScript == f.Host.scriptNode.scripts[19], "tail click leaves right pane on the other row");
    }

    public static void StartBeforeLoad()
    {
        foreach (int count in new[] { 21, 640, 5000 })
        {
            using var f = new Fixture(count, load: false);
            f.Host.Start();
            Check(HostStats.ChildSelections == 0 && HostStats.Selections == 0, "empty Start must not invent a selection");
            f.Load();
            AssertSelection(f, f.Host.scriptNodeListItemsCache[count - 1]);
            Check(HostStats.ChildSelections == 1 && HostStats.Selections == 1, "Start-before-Load retains its one native selection");
        }
    }

    public static void InvalidatedDuringStart()
    {
        foreach (string change in new[] { "deselected", "destroyed", "node replaced", "selection replaced" })
        {
            using var f = new Fixture(640, load: false);
            f.Load();
            var tail = f.Host.selectedScriptItem!;
            var other = f.Host.scriptNodeListItemsCache[0];
            int callbacks = HostStats.ChildSelections;
            HostStats.DuringStart = () =>
            {
                switch (change)
                {
                    case "deselected": tail.Deselect(true); break;
                    case "destroyed": NGUITools.Destroy(tail.gameObject); break;
                    case "node replaced": f.Host.scriptNode = new ScriptNode(); break;
                    case "selection replaced": tail.Deselect(true); other.Select(); break;
                }
            };
            f.Host.Start();
            Check(f.Host.selectedScriptItem != tail, "Start repair resurrected a stale row after " + change);
            if (change == "selection replaced") AssertSelection(f, other);
            else Check(f.Host.selectedScriptItem == null && f.Host.PaneScript == null, "invalidated Start must remain unselected after " + change);
            Check(HostStats.ChildSelections == callbacks + (change == "selection replaced" ? 1 : 0), "no repair callback may run for invalidated selection after " + change);
        }
    }

    public static void FailedStart()
    {
        using var f = new Fixture(640, load: false);
        f.Load();
        int callbacks = HostStats.ChildSelections;
        HostStats.Failure = "Start";
        bool failed = false;
        try { f.Host.Start(); } catch (InvalidOperationException) { failed = true; }
        Check(failed, "native Start exception must propagate");
        Check(f.Host.selectedScriptItem == null && HostStats.ChildSelections == callbacks, "failed initialization must not replay selection into an incomplete pane");
    }

    public static void FailedSelectionRefresh()
    {
        foreach (bool alreadyLoading in new[] { false, true })
        {
            using var f = new Fixture(640, load: false);
            f.Load();
            var row = f.Host.selectedScriptItem;
            f.Host.loading = alreadyLoading;
            int created = HostStats.Created;
            HostStats.Failure = "OnChildSelect";
            f.Host.Start();
            Check(f.Host.loading == alreadyLoading, "failed refresh must restore loading flag");
            Check(f.Host.selectedScriptItem == row && row!.selected, "native owner restoration survives later pane refresh failure");
            Check(f.Mod.IsActive && HostStats.Created == created, "refresh failure must not rebuild a dense list");
            Check(f.Logs.Any(s => s.Contains("startup selection refresh failed")), "refresh failure must be diagnosable");
        }
    }

    public static void PendingEditsAndReselection()
    {
        foreach (int count in new[] { 640, 5000 })
        {
            using var f = new Fixture(count, load: false);
            f.Load();
            var originalModel = f.Host.scriptNode.scripts[count - 1];
            f.Host.Start();
            Check(f.Mod.HasPendingRows, "mutation regression begins before visible row filling completes");
            AssertSelection(f, f.Host.scriptNodeListItemsCache[count - 1]);
            f.Host.InsertScript();
            var newModel = f.Host.scriptNode.scripts[count];
            AssertSelection(f, f.Host.scriptNodeListItemsCache[count]);
            f.Host.InsertScript();
            AssertSelection(f, f.Host.scriptNodeListItemsCache[count + 1]);
            f.Scroll(count - 5);
            var original = f.Host.scriptNodeListItemsCache[count - 1];
            original.Select();
            AssertSelection(f, original);
            Check(f.Host.PaneScript == originalModel, "original tail still routes to its authored data");
            var added = f.Host.scriptNodeListItemsCache[count];
            added.Select();
            AssertSelection(f, added);
            Check(f.Host.PreviewScript == newModel, "new row preview stays bound after original tail reselection");
            original.Select();
            f.Host.DeleteScript();
            Check(!f.Data().Contains(originalModel.Pointer) && f.Data().Contains(newModel.Pointer), "delete removes original selected tail without deleting inserted data");
            AssertSelection(f, f.Host.selectedScriptItem!);
            f.Scroll(0); f.Scroll(count - 4);
            var moved = f.Host.scriptNodeListItemsCache[count - 1];
            moved.Select();
            AssertSelection(f, moved);
            Check(f.Host.PaneScript == newModel, "recycled scrolling and deletion preserve new row model identity");
            f.Assert();
        }
    }

    public static void MutedDeselectionSemantics()
    {
        using var f = new Fixture(21, enabled: false, restored: 20);
        var tail = f.Host.selectedScriptItem!;
        tail.Deselect(true);
        Check(!tail.selected && f.Host.selectedScriptItem == tail, "muted native deselection must leave owner unchanged");
        tail.Select();
        Check(tail.selected && f.Host.selectedScriptItem == tail, "deselected row can be selected again");
        var next = f.Host.scriptNodeListItemsCache[19];
        f.Host.OnChildSelect(next);
        tail.Deselect(false);
        Check(f.Host.selectedScriptItem == null, "nonmuted native OnChildDeselect unconditionally clears owner, even a different owner");
    }
}
