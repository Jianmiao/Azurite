using Azurite;
using UnityEngine;
using UnityEngine.SceneManagement;

int passed = 0, failed = 0;
void Check(bool value, string message)
{
    if (value) { passed++; Console.WriteLine("PASS " + message); }
    else { failed++; Console.WriteLine("FAIL " + message); }
}

static void ResetRegistry(int count)
{
    UIPanel.list.Clear();
    for (int i = 0; i < count; i++)
    {
        var panel = new UIPanel { name = "panel-" + i };
        panel.transform.localToWorldMatrix = new Matrix4x4(i + 1);
        UIPanel.list.Add(panel);
    }
    SceneManager.Handle++;
}

static void Tick(UiRepaintObserver observer, ref double now, int frames = 1)
{
    for (int i = 0; i < frames; i++)
    {
        now += 0.02;
        observer.Update(now);
    }
}

ResetRegistry(640);
var reports = new List<string>();
using (var observer = new UiRepaintObserver(reports.Add))
{
    double now = 0;
    observer.Update(now);
    Check(observer.IsHealthy && !observer.IsReady && observer.SynchronizationCursor == 32, "640-panel registry starts in a bounded batch and stays unready");
    Tick(observer, ref now, 18);
    Check(!observer.IsReady, "initial registry coverage remains unready before the last batch");
    Tick(observer, ref now);
    Check(observer.IsHealthy && observer.IsReady && observer.Metrics.PanelCount == 640, "640 panels become fully observed after 20 bounded updates");

    observer.ConsumeDirty();
    long generation = observer.Generation;
    bool alwaysReady = true;
    bool falselyDirty = false;
    for (int i = 0; i < 250; i++)
    {
        Tick(observer, ref now);
        alwaysReady &= observer.IsReady;
        falselyDirty |= observer.ConsumeDirty();
    }
    Check(alwaysReady, "periodic unchanged 640-panel scans never revoke Ready");
    Check(!falselyDirty && observer.Generation == generation, "periodic unchanged 640-panel scans never dirty static UI");

    var replacement = new UIPanel { name = "replacement" };
    replacement.transform.localToWorldMatrix = new Matrix4x4(9000);
    UIPanel.list[7] = replacement;
    bool mutationRevokedReady = false;
    bool mutationDirtied = false;
    for (int i = 0; i < 100; i++)
    {
        Tick(observer, ref now);
        mutationRevokedReady |= !observer.IsReady;
        mutationDirtied |= observer.ConsumeDirty();
    }
    Check(mutationRevokedReady && mutationDirtied, "same-size panel replacement invalidates coverage and requests repaint");
    Check(observer.IsHealthy && observer.IsReady && observer.Metrics.PanelCount == 640 && replacement.onGeometryUpdated != null,
        "same-size registry replacement is fully observed within two seconds");

    int foreignGeometry = 0, foreignClip = 0;
    var external = new UIPanel.OnGeometryUpdated(() => foreignGeometry++);
    var externalClip = new UIPanel.OnClippingMoved(_ => foreignClip++);
    replacement.onGeometryUpdated = external;
    replacement.onClipMove = externalClip;
    generation = observer.Generation;
    Tick(observer, ref now, 100);
    Check(observer.IsHealthy && observer.IsReady && observer.CallbackReplacements >= 2 && observer.Generation > generation,
        "external callback replacements are detected and re-subscribed within two seconds");
    observer.ConsumeDirty();
    replacement.onGeometryUpdated!.Invoke();
    replacement.onClipMove!.Invoke(replacement);
    Check(foreignGeometry == 1 && foreignClip == 1 && observer.ConsumeDirty(),
        "re-subscribed callbacks invoke both foreign callbacks and the repaint observer exactly once");

    replacement.onGeometryUpdated = null;
    generation = observer.Generation;
    Tick(observer, ref now, 100);
    Check(observer.IsReady && replacement.onGeometryUpdated != null && observer.Generation > generation,
        "callback cleared to null is detected, dirties once, and is re-subscribed");
    observer.ConsumeDirty();
    generation = observer.Generation;
    Tick(observer, ref now, 150);
    Check(observer.Generation == generation && !observer.ConsumeDirty(), "repaired subscriptions do not dirty later unchanged scans");

    UIPanel.list.Add(new UIPanel());
    Tick(observer, ref now);
    Check(!observer.IsReady && observer.ConsumeDirty(), "registry count mutation immediately invalidates coverage");
    Tick(observer, ref now, 20);
    Check(observer.IsReady && observer.Metrics.PanelCount == 641, "registry count mutation recovers after complete coverage");

    var disabled = UIPanel.list[0];
    disabled.isActiveAndEnabled = false;
    Tick(observer, ref now, 100);
    Check(observer.IsReady && observer.Metrics.PanelCount == 640 && disabled.onGeometryUpdated == null,
        "inactive panel is detached and coverage recovers");
    disabled.isActiveAndEnabled = true;
    Tick(observer, ref now, 100);
    Check(observer.IsReady && observer.Metrics.PanelCount == 641 && disabled.onGeometryUpdated != null,
        "reactivated panel is discovered without a registry count change");

    ResetRegistry(4097);
    Tick(observer, ref now);
    Check(observer.IsHealthy && observer.CapacityBlocked && !observer.IsReady, "over-capacity registry pauses observation without poisoning observer");
    ResetRegistry(640);
    Tick(observer, ref now, 20);
    Check(observer.IsHealthy && !observer.CapacityBlocked && observer.IsReady, "returning below capacity resumes observation");

    var tracked = UIPanel.list[0];
    tracked.onGeometryUpdated = new UIPanel.OnGeometryUpdated(() => foreignGeometry++);
    tracked.onClipMove = new UIPanel.OnClippingMoved(_ => foreignClip++);
    Tick(observer, ref now, 100);
    observer.Dispose();
    tracked.onGeometryUpdated!.Invoke();
    tracked.onClipMove!.Invoke(tracked);
    Check(!observer.IsReady && foreignGeometry == 2 && foreignClip == 2,
        "Dispose removes only observer callbacks and preserves foreign callbacks");
}

ResetRegistry(96);
using (var observer = new UiRepaintObserver(reports.Add))
{
    double now = 0;
    Tick(observer, ref now, 3);
    var inserted = new UIPanel();
    UIPanel.list.Insert(0, inserted);
    Tick(observer, ref now);
    // Restart the partial scan without introducing another unique binding.
    // The panel already discovered before restart still needs transform coverage.
    UIPanel.list.Add(UIPanel.list[1]);
    Tick(observer, ref now, 4);
    Check(observer.IsReady && observer.Metrics.PanelCount == 97, "registry restart with a duplicate entry completes unique coverage");
    long transforms = observer.Metrics.TransformChanges;
    inserted.transform.localToWorldMatrix = new Matrix4x4(12345);
    Tick(observer, ref now, 4);
    Check(observer.Metrics.TransformChanges == transforms + 1, "binding discovered before interrupted scan retains transform coverage");
}

ResetRegistry(4096);
using (var observer = new UiRepaintObserver(reports.Add))
{
    double now = 0;
    Tick(observer, ref now, 128);
    Check(observer.IsReady && observer.Metrics.PanelCount == 4096, "4096 panels reach full initial coverage in bounded batches");
    observer.ConsumeDirty();
    var last = UIPanel.list[^1];
    last.onGeometryUpdated!.Invoke();
    Check(observer.ConsumeDirty() && observer.LastDirtyKind == UiDirtyKind.Geometry, "last panel geometry callback immediately requests repaint");
    last.onClipMove!.Invoke(last);
    Check(observer.ConsumeDirty() && observer.LastDirtyKind == UiDirtyKind.ClipEvent, "last panel clip callback immediately requests repaint");

    long transforms = observer.Metrics.TransformChanges;
    last.transform.localToWorldMatrix = new Matrix4x4(99999);
    Tick(observer, ref now, 128);
    Check(observer.Metrics.TransformChanges == transforms + 1, "last panel transform change detected within one 4096-panel round robin across registry scans");
    transforms = observer.Metrics.TransformChanges;
    last.clipOffset = new Vector2(4, 8);
    Tick(observer, ref now, 128);
    Check(observer.Metrics.TransformChanges == transforms + 1, "last panel clip offset change detected within one 4096-panel round robin across registry scans");
    observer.ConsumeDirty();
    long generation = observer.Generation;
    bool alwaysReady = true;
    for (int i = 0; i < 300; i++)
    {
        Tick(observer, ref now);
        alwaysReady &= observer.IsReady;
    }
    Check(alwaysReady && observer.Generation == generation, "steady 4096-panel rescans preserve readiness and do not dirty static UI");

    int foreignCount = 0;
    last.onGeometryUpdated = new UIPanel.OnGeometryUpdated(() => foreignCount++);
    Tick(observer, ref now, 310);
    observer.ConsumeDirty();
    last.onGeometryUpdated!.Invoke();
    Check(observer.IsReady && foreignCount == 1 && observer.ConsumeDirty(),
        "last panel lost subscription is repaired within two bounded registry passes without replacing foreign callback");
}
Check(reports.Count == 1 && reports[0].Contains("exceeds"), "only expected capacity warning is emitted");
foreach (int count in new[] { 1, 8, 16, 31, 32, 640 })
{
    ResetRegistry(count);
    using var observer = new UiRepaintObserver();
    double now = 0;
    do { Tick(observer, ref now); } while (!observer.IsReady);
    Transform.MatrixReads = 0;
    Tick(observer, ref now);
    Check(Transform.MatrixReads == Math.Min(32, count), $"{count} panels are checked at most once per transform poll");
}
ResetRegistry(8);
using (var observer = new UiRepaintObserver())
{
    foreach (var panel in UIPanel.list)
    {
        panel.onGeometryUpdated = new UIPanel.OnGeometryUpdated(() => { });
        panel.onClipMove = new UIPanel.OnClippingMoved(_ => { });
    }
    double now = 0;
    Tick(observer, ref now);
    Il2CppSystem.Delegate.InvocationListReads = 0;
    Tick(observer, ref now, 150);
    Check(Il2CppSystem.Delegate.InvocationListReads == 0, "unchanged multicast subscriptions avoid invocation-list allocation during audits");
    Check(!observer.HasOnlyOwnGeometryCallback(UIPanel.list[0]), "foreign geometry callbacks remain protected");
    UIPanel.list[0].onGeometryUpdated = null;
    Tick(observer, ref now, 100);
    Check(observer.HasOnlyOwnGeometryCallback(UIPanel.list[0]), "single observation-only geometry callback is distinguishable from foreign callbacks");
}
ResetRegistry(2);
UIPanel.list[1].gameObject.scene = new Scene(SceneManager.Handle, "PreviewScene");
using (var observer = new UiRepaintObserver())
{
    double now = 0; Tick(observer, ref now);
    long previewGeneration = observer.PreviewGeneration;
    UIPanel.list[0].onClipMove!.Invoke(UIPanel.list[0]);
    Check(observer.PreviewGeneration == previewGeneration, "editor list clipping wakes UI without invalidating static preview content");
    UIPanel.list[1].onGeometryUpdated!.Invoke();
    Check(observer.PreviewGeneration == previewGeneration + 1, "any PreviewScene panel geometry invalidates cached preview content");
    UIPanel.list[1].onClipMove!.Invoke(UIPanel.list[1]);
    Check(observer.PreviewGeneration == previewGeneration + 2, "PreviewScene clipping invalidates its content independently of UI scrolling");
    UIPanel.list[1].transform.localToWorldMatrix = new(1000); Tick(observer, ref now);
    Check(observer.PreviewGeneration == previewGeneration + 3, "PreviewScene transform movement invalidates its cached frame");
}
Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
