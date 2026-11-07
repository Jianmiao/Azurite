using System.Reflection;
using Azurite;
using Studio.Scripts;
using Studio.Scripts.Window.BackgroundExplorer;
using CharacterExplorer = Studio.Scripts.Window.CharacterExplorer.CharacterExplorer;
using EmotionExplorer = Studio.Scripts.Window.EmotionExplorer.EmotionExplorer;
using UI;

var tests = new (string Name, Action Test)[]
{
    ("half user scale slows a native baseline", HalfNative),
    ("double user scale is applied after per-area clamps", DoubleEmotion),
    ("user scale updates before the next probe interval", Immediate),
    ("changing scale never compounds the last written factor", Baseline),
    ("global scale reaches all discovered areas", AllAreas),
    ("effective factor one retains emotion direction", EmotionOne),
    ("negative native direction remains consistent", NativeDirection),
    ("disable restores the original factor", Disable),
    ("external writers retain ownership after scale changes", ExternalWriter),
    ("invalid global scale normalizes safely", Invalid),
    ("global endpoints clamp independently of per-area limits", Clamp),
    ("legacy overload and default scale retain existing behavior", Legacy),
    ("reset to native scale restores a reduced baseline", ResetNative),
};
int failed = 0;
foreach (var (name, test) in tests)
{
    Reset();
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { ++failed; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Reset()
{
    UnityEngine.Object.Registry.Clear();
    Singleton<Catalog>.Instance = new();
    Singleton<Catalog>.Instance.uiProfiles.Add(new());
    ScriptNodeInspector.instance = new();
    UIPopupModManager.instance = new();
    Singleton<BackgroundExplorer>.Instance = new();
    Singleton<CharacterExplorer>.Instance = new();
    Singleton<CharacterExplorer>.Instance.Children.Add(new CenterableUIScrollView());
    Singleton<EmotionExplorer>.Instance = new();
    Singleton<EmotionExplorer>.Instance.Children.Add(new CenterableUIScrollView());
    var settings = new SettingPanel(); settings.Children.Add(new UIScrollView()); UnityEngine.Object.Registry.Add(settings);
}
// Reflection permits running the same behavior tests against the previous
// source: its final overload has no scale argument and therefore ignores it.
static void Update(EditorScrollTuning controller, float scale, double now = 0, bool allowed = true, float general = 3, float dialogue = 4.5f, float mod = 1, float background = 4.5f, float settings = 4.5f, float character = 9, float emotion = 11.25f)
{
    MethodInfo method = typeof(EditorScrollTuning).GetMethods().Where(x => x.Name == "Update").OrderByDescending(x => x.GetParameters().Length).First();
    object[] args = { now, allowed, general, dialogue, mod, background, settings, character, emotion, scale };
    method.Invoke(controller, args.Take(method.GetParameters().Length).ToArray());
}
static void Equal(float actual, float expected)
{ if (Math.Abs(actual - expected) > 0.0001f) throw new Exception($"expected {expected}, got {actual}"); }
static UIScrollView Emotion() => (UIScrollView)Singleton<EmotionExplorer>.Instance.Children[0];
static void HalfNative()
{
    using var controller = new EditorScrollTuning();
    Update(controller, 0.5f, general: 1, dialogue: 1, mod: 1, background: 1, settings: 1, character: 1, emotion: 1);
    Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 5);
    Equal(Emotion().scrollWheelFactor, -5);
}
static void DoubleEmotion()
{
    using var controller = new EditorScrollTuning(); Update(controller, 2);
    Equal(Emotion().scrollWheelFactor, -225);
    Update(controller, 2, now: 2, emotion: 99); Equal(Emotion().scrollWheelFactor, -240);
}
static void Immediate()
{
    using var controller = new EditorScrollTuning(); Update(controller, 1, now: 0); Update(controller, 2, now: 0.05);
    Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 90);
    Update(controller, 0.5f, now: 0.1); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 22.5f);
}
static void Baseline()
{
    using var controller = new EditorScrollTuning(); Update(controller, 2); Update(controller, 2, now: 2);
    Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 90);
    Update(controller, 0.5f, now: 4); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 22.5f);
    Update(controller, 1, now: 6); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 45);
}
static void AllAreas()
{
    using var controller = new EditorScrollTuning(); Update(controller, 2);
    Equal(Singleton<Catalog>.Instance.uiProfiles[0].scroll.scrollWheelFactor, 60);
    Equal(ScriptNodeInspector.instance.characterTabScroll.scrollWheelFactor, 60);
    Equal(ScriptNodeInspector.instance.environmentTabScroll.scrollWheelFactor, 60);
    Equal(UIPopupModManager.instance.scroll.scrollWheelFactor, 20);
    Equal(Singleton<BackgroundExplorer>.Instance.scroll.scrollWheelFactor, 90);
    Equal(Singleton<BackgroundExplorer>.Instance.hierarchy.rootTable.GetComponentInParent<UIScrollView>().scrollWheelFactor, 20);
    Equal(((UIScrollView)UnityEngine.Object.FindObjectsOfType<SettingPanel>()[0].Children[0]).scrollWheelFactor, 90);
    Equal(((UIScrollView)Singleton<CharacterExplorer>.Instance.Children[0]).scrollWheelFactor, 180);
}
static void EmotionOne()
{
    using var controller = new EditorScrollTuning(); Update(controller, 0.5f, emotion: 2);
    Equal(Emotion().scrollWheelFactor, -10);
}
static void NativeDirection()
{
    Emotion().scrollWheelFactor = -10;
    using var controller = new EditorScrollTuning(); Update(controller, 2); Equal(Emotion().scrollWheelFactor, 225);
    controller.Restore(); Equal(Emotion().scrollWheelFactor, -10);
}
static void Disable()
{
    using var controller = new EditorScrollTuning(); Update(controller, 0.5f);
    Update(controller, 2, now: 0.1, allowed: false);
    Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 10); Equal(Emotion().scrollWheelFactor, 10);
}
static void ExternalWriter()
{
    using var controller = new EditorScrollTuning(); Update(controller, 1);
    ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor = 99;
    Update(controller, 2, now: 0.1); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 99);
    controller.Restore(); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 99);
    Update(controller, 0.5f, now: 2); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 99);
}
static void Invalid()
{
    using var controller = new EditorScrollTuning(); Update(controller, float.NaN); Equal(Emotion().scrollWheelFactor, -112.5f);
    Update(controller, float.PositiveInfinity, now: 2); Equal(Emotion().scrollWheelFactor, -112.5f);
}
static void Clamp()
{
    using var controller = new EditorScrollTuning(); Update(controller, 0); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 22.5f);
    Update(controller, 20, now: 2); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 90);
}
static void Legacy()
{
    using var controller = new EditorScrollTuning(); controller.Update(0, true, 3);
    Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 30);
    Equal(Emotion().scrollWheelFactor, 10);
    controller.Update(2, true, 1); Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 10);
}
static void ResetNative()
{
    using var controller = new EditorScrollTuning();
    Update(controller, 0.5f, general: 1, dialogue: 1, mod: 1, background: 1, settings: 1, character: 1, emotion: 1);
    Update(controller, 1, now: 0.1, general: 1, dialogue: 1, mod: 1, background: 1, settings: 1, character: 1, emotion: 1);
    Equal(ScriptNodeInspector.instance.scriptListScroll.scrollWheelFactor, 10); Equal(Emotion().scrollWheelFactor, 10);
}
