using Azurite;
using Studio.Scripts.Window.BackgroundExplorer;
using UnityEngine;

var tests = new (string Name, Action Test)[]
{
    ("loaded visible thumbnails do not allocate a native callback every frame", LoadedThumbnails),
    ("gallery burst admits at most two requests per frame", Burst),
    ("at most eight loads remain in flight", InFlight),
    ("completion admits the next visible items", Completion),
    ("cancelled generations do not occupy capacity", Cancelled),
    ("stale callbacks cannot release newer generation capacity", StaleCallback),
    ("offscreen native unload retains texture enabled state", Offscreen),
    ("disabled culling leaves native behavior intact", Disabled),
    ("export suspension leaves native behavior intact", Suspended),
    ("same-frame direct native calls obey admission budget", DirectCall),
    ("synchronous cache hits do not leak capacity", Synchronous),
    ("failed callbacks release capacity without rewriting native state", FailedCallback),
    ("lost callbacks cannot permanently block the gallery", MissingCallback),
    ("re-enable resumes admission without touching completed textures", Resume),
    ("disposing removes all admission hooks", Disposed),
};
int failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { ++failed; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static BackgroundGalleryLoadCulling Start()
{
    Time.frameCount = 0;
    var result = new BackgroundGalleryLoadCulling(_ => { });
    Check(result.Install(), "hooks install");
    result.Update(0, true);
    return result;
}
static BackgroundItem[] Items(int count) => Enumerable.Range(0, count).Select(_ => new BackgroundItem()).ToArray();
static void Frame(IEnumerable<BackgroundItem> items)
{
    ++Time.frameCount;
    foreach (var item in items) item.Update();
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void LoadedThumbnails()
{
    using var controller = Start();
    var items = Items(80);
    foreach (var item in items) { item.unloaded = false; item.texture.mainTexture = new Texture(); }
    for (int i = 0; i < 120; ++i) Frame(items);
    Check(items.Sum(x => x.NativeClosures) == 0, "loaded thumbnails invoked allocating LoadBGAsync");
    Check(items.Sum(x => x.texture.CornerReads) == 0, "culling recalculated world corners for loaded thumbnails");
}
static void Burst()
{
    using var controller = Start(); var items = Items(40); Frame(items);
    Check(items.Sum(x => x.Requests) is > 0 and <= 2, "visible opening burst was unbounded");
    Check(items.All(x => x.texture.enabled), "admission changed texture rendering flags");
}
static void InFlight()
{
    using var controller = Start(); var items = Items(40);
    for (int i = 0; i < 20; ++i) Frame(items);
    Check(items.Sum(x => x.Requests) == 8, "in-flight request bound was not enforced");
}
static void Completion()
{
    using var controller = Start(); var items = Items(20);
    for (int i = 0; i < 4; ++i) Frame(items);
    foreach (var item in items.Take(8)) item.Callbacks.Single()._LoadBGAsync_b__0(new Texture());
    Frame(items);
    Check(items.Sum(x => x.Requests) == 10, "completed requests did not free capacity");
    Check(items.Take(8).All(x => x.texture.mainTexture != null), "native completion was suppressed");
}
static void Cancelled()
{
    using var controller = Start(); var items = Items(20);
    for (int i = 0; i < 4; ++i) Frame(items);
    foreach (var item in items.Take(8)) { item.texture.InViewport = false; item.UnloadBg(); }
    Frame(items);
    Check(items.Sum(x => x.Requests) == 10, "offscreen cancellation did not free capacity");
}
static void StaleCallback()
{
    using var controller = Start(); var items = Items(20);
    for (int i = 0; i < 4; ++i) Frame(items);
    var old = items[0].Callbacks.Single(); items[0].UnloadBg(); Frame(items);
    old._LoadBGAsync_b__0(new Texture()); Frame(items);
    Check(items.Sum(x => x.Requests) == 9, "stale generation callback freed a current load's capacity");
    Check(items[0].texture.mainTexture == null && items[0].Releases == 1, "native generation rejection changed");
}
static void Offscreen()
{
    using var controller = Start(); var item = new BackgroundItem { unloaded = false };
    item.texture.mainTexture = new Texture(); item.texture.InViewport = false;
    Frame(new[] { item });
    Check(item.unloaded && item.texture.mainTexture == null && item.Releases == 1, "native unload did not run");
    Check(item.texture.enabled, "offscreen texture was disabled by Azurite");
    item.texture.InViewport = true; Frame(new[] { item });
    Check(item.Requests == 1, "returning visible thumbnail did not load");
}
static void Disabled()
{
    using var controller = Start(); controller.Enabled = false; var items = Items(12); Frame(items);
    Check(items.Sum(x => x.Requests) == 12, "disabled admission still intercepted native requests");
}
static void Suspended()
{
    using var controller = Start(); controller.Update(1, false); var items = Items(12); Frame(items);
    Check(items.Sum(x => x.Requests) == 12, "export suspension still intercepted native requests");
    var outside = new BackgroundItem { unloaded = false }; outside.texture.InViewport = false; outside.texture.mainTexture = new Texture();
    Frame(new[] { outside }); Check(outside.texture.enabled, "suspension left a disabled texture");
}
static void DirectCall()
{
    using var controller = Start(); var items = Items(20);
    foreach (var item in items) item.UpdateLoadState();
    foreach (var item in items) item.UpdateLoadState();
    Check(items.Sum(x => x.Requests) is > 0 and <= 2, "direct UpdateLoadState bypassed frame budget");
}
static void Synchronous()
{
    using var controller = Start(); var items = Items(20); foreach (var item in items) item.CompleteSynchronously = true;
    for (int i = 0; i < 20; ++i) Frame(items);
    Check(items.All(x => x.Requests == 1 && x.texture.mainTexture != null), "synchronous cache callbacks stalled the queue");
}
static void FailedCallback()
{
    using var controller = Start(); var items = Items(20);
    for (int i = 0; i < 4; ++i) Frame(items);
    items[0].Callbacks.Single()._LoadBGAsync_b__0(null); Frame(items);
    Check(items.Sum(x => x.Requests) == 9, "failed callback occupied admission capacity");
    Check(!items[0].unloaded && items[0].texture.mainTexture == null, "native failure state was rewritten");
}
static void MissingCallback()
{
    using var controller = Start(); var items = Items(20);
    for (int i = 0; i < 4; ++i) Frame(items);
    controller.Update(11, true); Frame(items);
    Check(items.Sum(x => x.Requests) == 10, "lost callbacks permanently occupied admission capacity");
    Check(items.Take(8).All(x => !x.unloaded && x.loadGeneration == 1), "watchdog cancelled native loads");
}
static void Resume()
{
    using var controller = Start(); var items = Items(20); Frame(items);
    foreach (var item in items.Take(2)) item.Callbacks.Single()._LoadBGAsync_b__0(new Texture());
    controller.Update(1, false); controller.Update(2, true); Frame(items);
    Check(items.Sum(x => x.Requests) == 4, "resumed admission did not respect frame budget");
    Check(items.Take(2).All(x => x.texture.enabled && x.texture.mainTexture != null), "resume discarded completed textures");
}
static void Disposed()
{
    var controller = Start(); controller.Dispose(); var items = Items(20); Frame(items);
    Check(items.Sum(x => x.Requests) == 20, "disposed admission hooks remained installed");
}
