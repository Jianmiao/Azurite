using System;
using Azurite;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.Window;
using UnityEngine;

var tests = new (string Name, Action Test)[]
{
    ("pending Alt input does not block a safe authoring surface", RawInputAllowed),
    ("inactive or stale editor and lost focus block mutation", EditorIdentity),
    ("known resource windows and modal blockers own keyboard focus", WindowFocus),
    ("loading, saving, compiling and unavailable resources block mutation", BusyResources),
    ("actual scenario and embedded preview producers block mutation", Playback),
    ("resident static Test objects do not masquerade as playback", ResidentStatic),
    ("text input, IME and drag block mutation", InputFocus),
};
int failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Require(bool value, string message)
{
    if (!value) throw new Exception(message);
}
static void Block(Action<Fixture> change, string reason)
{
    var f = new Fixture(); change(f);
    Require(!f.Activity.CanUseSingleSpeakerShortcut(), reason + " was admitted");
}
static void RawInputAllowed()
{
    var f = new Fixture(); Input.anyKey = true; Input.anyKeyDown = true;
    var rendering = f.Activity.Observe();
    Require(rendering.HasInteraction && !rendering.CanThrottle, "fixture should model raw-key render protection");
    Require(f.Activity.CanUseSingleSpeakerShortcut(), "raw Alt input incorrectly blocked a safe authoring chord");
}
static void EditorIdentity()
{
    Block(_ => Application.isFocused = false, "lost focus");
    Block(_ => StudioCommon.instance = null, "missing studio");
    Block(f => f.Studio.isActiveAndEnabled = false, "inactive studio");
    Block(f => f.Studio.gameObject.activeInHierarchy = false, "hidden studio");
    Block(f => f.Studio.entryNode = null, "missing entry node");
    Block(f => f.Studio.sessionInitializationError = "failed", "failed editor session");
    Block(_ => ScriptNodeInspector.instance = null, "missing inspector");
    Block(f => f.Inspector.isActiveAndEnabled = false, "inactive inspector");
    Block(f => f.Inspector.gameObject.activeInHierarchy = false, "hidden inspector");
    Block(f => f.Inspector.scriptNode = null, "missing script node");
    Block(f => f.Inspector.loading = true, "loading inspector");
    Block(f => f.Inspector.unloading = true, "unloading inspector");
    Block(f => f.Inspector.rearrangeScheduled = true, "rearranging inspector");
}
static void WindowFocus()
{
    Block(f => f.Windows.activeWindow = new WindowStub((IntPtr)111), "unknown editor window");
    Block(f => f.Windows.blocking = new GameObject { activeInHierarchy = true }, "modal blocker");
    Block(_ => Singleton<WindowManager>.Instance = null, "missing window manager");
    Block(f =>
    {
        var background = new Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer();
        Singleton<Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer>.Instance = background;
        f.Windows.activeWindow = new WindowStub(background.Pointer);
    }, "known static resource window");
}
static void BusyResources()
{
    Block(f => f.Loading.loadingCount = 1, "global loading count");
    Block(_ => Loading.LoadingTags!.Add("assets"), "global loading tag");
    Block(_ => Loading.LoadingTags = null, "unknown loading tags");
    Block(_ => Singleton<Loading>.Instance = null, "missing loading manager");
    Block(f => f.Studio.autoSavePending = true, "pending save");
    Block(f => f.Studio.autoCompilePending = true, "pending compile");
    Block(_ => AuthoringEditorSession.Current = null, "missing authoring session");
    Block(_ => AuthoringEditorSession.Current!.SaveInProgress = true, "active save");
    Block(_ => AuthoringEditorSession.Current!.IsApplying = true, "active apply");
    Block(_ => Singleton<ScenarioResourceManager>.Instance = null, "missing resources");
    Block(f => f.Resources.Preloading = true, "resource preload");
    Block(f => f.Resources.AreDbsLoaded = false, "unloaded databases");
}
static void Playback()
{
    Block(_ => Singleton<Test>.Instance = new Test { auto = true }, "scenario auto playback");
    Block(_ => Singleton<Test>.Instance = new Test { currentAnims = 1 }, "scenario animation");
    Block(f => f.Inspector.preview = new Test { previewMode = true, hasVoice = true }, "embedded preview voice");
    Block(f => f.Inspector.preview = new Test { previewMode = true, backgroundAnimations = 1 }, "embedded preview animation");
}
static void ResidentStatic()
{
    var f = new Fixture(); Singleton<Test>.Instance = new Test();
    Require(f.Activity.CanUseSingleSpeakerShortcut(), "resident idle non-preview Test was treated as playback");
    f.Inspector.preview = new Test { previewMode = true };
    Require(f.Activity.CanUseSingleSpeakerShortcut(), "static embedded preview was treated as playback");
}
static void InputFocus()
{
    Block(_ => UICamera.inputHasFocus = true, "UI input focus");
    Block(_ => UICamera.isDragging = true, "UI drag");
    Block(_ => UIInput.current = new UIInput { isSelected = true }, "selected current input");
    Block(_ => UIInput.selection = new UIInput { isSelected = true }, "selected input");
    Block(_ => UIInput.mLastIME = "composition", "IME composition");
}

sealed class WindowStub : IWindow
{
    public IntPtr Pointer { get; }
    public WindowStub(IntPtr pointer) => Pointer = pointer;
}
sealed class Fixture
{
    public readonly StudioCommon Studio = new();
    public readonly ScriptNodeInspector Inspector = new();
    public readonly WindowManager Windows = new();
    public readonly Loading Loading = new();
    public readonly ScenarioResourceManager Resources = new();
    public readonly HostActivity Activity = new();
    public Fixture()
    {
        Application.isFocused = true; Input.anyKey = false; Input.anyKeyDown = false;
        Input.mousePosition = default; Input.mouseScrollDelta = default;
        UICamera.inputHasFocus = false; UICamera.isDragging = false;
        UIInput.current = null; UIInput.selection = null; UIInput.mLastIME = "";
        StudioCommon.instance = Studio; ScriptNodeInspector.instance = Inspector;
        Singleton<WindowManager>.Instance = Windows;
        Singleton<Loading>.Instance = Loading; Loading.LoadingTags = new();
        Singleton<ScenarioResourceManager>.Instance = Resources;
        Singleton<Test>.Instance = null;
        AuthoringEditorSession.Current = new();
        Singleton<Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer>.Instance = null;
    }
}
