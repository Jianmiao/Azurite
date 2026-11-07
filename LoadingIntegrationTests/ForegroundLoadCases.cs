using Azurite;

internal static class ForegroundLoadCases
{
    public static void InputGetsFirstSliceWithoutStarvation()
    {
        var b = new ForegroundLoadBudget();
        if (!b.CanAdmit(0, false) || b.CanAdmit(10, true) || b.CanAdmit(109, true))
            throw new Exception("new input did not reserve its first 100ms");
        if (!b.CanAdmit(110, true)) throw new Exception("continuous input starved resource admission");
        b.Admitted(110, true);
        if (b.CanAdmit(234, true) || !b.CanAdmit(235, true)) throw new Exception("interactive admission budget lost");
        if (!b.CanAdmit(236, false)) throw new Exception("idle resource work did not resume immediately");
        if (b.CanAdmit(237, true)) throw new Exception("next distinct interaction did not get priority");
    }
    public static void PendingTasksStillCompleteDuringInput()
    {
        using var f = new Fixture(); f.Capture(); f.Studio.Start(); f.Advance(); f.Advance();
        int submitted = NativeTasks.Calls.Count;
        if (submitted == 0) throw new Exception("test has no in-flight work");
        for (int n = 0; n < 12; n++)
        {
            UnityEngine.Time.frameCount++;
            NativeTasks.Tick();
            f.Plugin.Update(f.Host, false, true);
        }
        if (f.Plugin.Busy) throw new Exception("active input blocked completion of previously admitted work");
    }
}
