using Azurite;
using UnityEngine;

var tests = new (string Name, Action Test)[]
{
    ("five-character caption survives a short source font-name box", VisibleCaption),
    ("percentage is a separate live label", SeparatePercentage),
    ("new caption owns its layout without inherited localization", IndependentCaption),
    ("caption and percentage retain a nonempty native bitmap font", CopyNativeFont),
    ("runtime font and size changes refresh both labels", RefreshFont),
    ("caption slider and percentage occupy distinct columns", DistinctColumns),
    ("wheel slider stays in the native AUTO slider column", NativeSliderColumn),
    ("shifted parent and centered anchor retain separate columns", ShiftedCenteredLayout),
    ("slider endpoints map to fifty and two hundred percent", UserMapping),
    ("external scale changes update display without invoking setter", ExternalMapping),
    ("native font row slider and callbacks remain untouched", PreserveSource),
    ("AUTO time ticks are hidden only on the new slider", HideCopiedTicks),
    ("repeated discovery does not duplicate the control", NoDuplicates),
    ("dispose destroys only the control-owned objects", OwnedCleanup),
};
int failed = 0;
foreach (var (name, test) in tests)
{
    UnityEngine.Object.Registry.Clear(); Time.unscaledTime = 0;
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Near(float actual, float expected) => Require(Math.Abs(actual - expected) < 0.001f, $"expected {expected}, got {actual}");
static UILabel[] Labels() => UnityEngine.Object.FindObjectsOfType<UILabel>().Where(x => x.gameObject.name.StartsWith("AzuriteWheelSensitivity", StringComparison.Ordinal)).ToArray();
static UILabel Caption() => Labels().SingleOrDefault(x => x.text == "滚动灵敏度") ?? throw new Exception("the exact five-character caption 滚动灵敏度 is missing");
static UILabel Percentage() => Labels().SingleOrDefault(x => System.Text.RegularExpressions.Regex.IsMatch(x.text, @"^\d+%$")) ?? throw new Exception("a separate percentage label is missing");
static UISlider Slider() => UnityEngine.Object.FindObjectsOfType<UISlider>().Single(x => x.gameObject.name == "AzuriteWheelSensitivitySlider");
static (float Left, float Right) Bounds(UIWidget widget)
{
    float fraction = widget.pivot is UIWidget.Pivot.TopLeft or UIWidget.Pivot.Left or UIWidget.Pivot.BottomLeft ? 0 : widget.pivot is UIWidget.Pivot.TopRight or UIWidget.Pivot.Right or UIWidget.Pivot.BottomRight ? 1 : 0.5f;
    float left = widget.transform.position.x - widget.width * fraction;
    return (left, left + widget.width);
}
static (float Left, float Right) SliderBounds(UISlider slider)
{
    var bars = slider.GetComponentsInChildren<UIWidget>(true).Where(x => x is not UILabel).Select(Bounds).ToArray();
    return (bars.Min(x => x.Left), bars.Max(x => x.Right));
}
static void VisibleCaption()
{
    using var fixture = new Fixture();
    var caption = Labels().FirstOrDefault(x => !System.Text.RegularExpressions.Regex.IsMatch(x.text, @"^\d+%$")) ?? throw new Exception("caption label was not constructed");
    Require(caption.enabled && caption.gameObject.activeInHierarchy && caption.color.a > 0, "caption is not enabled and opaque");
    Require(caption.height >= caption.fontSize, $"caption height {caption.height} cannot hold its {caption.fontSize}-unit first line");
    Require(caption.width >= caption.fontSize * 5, "five full-width characters cannot fit the caption box");
    Require(caption.maxLineCount == 1, "caption must remain one line");
    Require(caption.text == "滚动灵敏度", $"caption is '{caption.text}', expected 滚动灵敏度");
}
static void SeparatePercentage()
{
    using var fixture = new Fixture();
    var caption = Caption(); var value = Percentage();
    Require(value.text == "100%", "default percentage is wrong");
    Require(!ReferenceEquals(caption.gameObject, value.gameObject), "caption and percentage share a clipping box");
    fixture.Scale = 2; fixture.Control.Update(); Require(Percentage().text == "200%", "percentage did not follow scale");
    Require(caption.text == "滚动灵敏度", "changing value altered the caption");
}
static void IndependentCaption()
{
    using var fixture = new Fixture(); var label = Labels().First();
    Require(label.GetType() == typeof(UILabel), "caption inherited a localized label type");
    Require(label.GetComponent<FontAdapter>() == null, "caption inherited source font adapter lifecycle");
    Require(new[] { label.leftAnchor, label.rightAnchor, label.topAnchor, label.bottomAnchor }.All(x => x.target == null), "caption retained native row anchor targets");
}
static void CopyNativeFont()
{
    using var fixture = new Fixture(useBitmapFont: true);
    var font = fixture.Anchor.bitmapFont ?? throw new Exception("fixture requires a nonempty native font reference");
    Require(Labels().Length == 2 && Labels().All(x => ReferenceEquals(x.bitmapFont, font)), "caption or percentage lost the source native font");
    Require(Labels().All(x => x.fontSize == 32), "caption or percentage lost the source font size");
    Require(fixture.Anchor.bitmapFont == font && !font.Destroyed, "creating wheel labels changed or destroyed the source font");
}
static void RefreshFont()
{
    using var fixture = new Fixture();
    var firstFont = fixture.Anchor.trueTypeFont;
    Require(firstFont != null && Labels().All(x => ReferenceEquals(x.trueTypeFont, firstFont)), "initial TrueType font was not copied");
    var nextFont = new Font();
    fixture.Anchor.trueTypeFont = nextFont;
    fixture.Anchor.fontSize = 40;
    fixture.Anchor.fontStyle = FontStyle.Bold;
    fixture.Anchor.spacingY = 12;
    Time.unscaledTime = 0.6f;
    fixture.Control.Update();
    Require(Labels().Length == 2 && Labels().All(x => ReferenceEquals(x.trueTypeFont, nextFont)), "caption or percentage kept the stale font");
    Require(Labels().All(x => x.fontSize == 40 && x.fontStyle == FontStyle.Bold), "font size or style did not follow the source update");
    Require(Labels().All(x => x.height >= 52 && x.maxLineCount == 1), "updated font and spacing exceed a label's first-line capacity");
    Require(Caption().text == "滚动灵敏度" && Percentage().text == "100%", "font refresh replaced the user-visible text");
    Require(fixture.SetCalls == 0 && fixture.NativeCalls == 0, "font refresh invoked a setting callback");
}
static void DistinctColumns()
{
    using var fixture = new Fixture(); var caption = Caption(); var value = Percentage();
    var titleBounds = Bounds(caption); var sliderBounds = SliderBounds(Slider()); var valueBounds = Bounds(value);
    Require(titleBounds.Right < sliderBounds.Left, "caption overlaps slider");
    Require(sliderBounds.Right < valueBounds.Left, "slider overlaps percentage");
    Near(caption.transform.position.y, Slider().transform.position.y); Near(value.transform.position.y, Slider().transform.position.y);
}
static void NativeSliderColumn()
{
    using var fixture = new Fixture(); var original = SliderBounds(fixture.NativeSlider); var inserted = SliderBounds(Slider());
    Near(inserted.Left, original.Left); Near(inserted.Right, original.Right);
}
static void ShiftedCenteredLayout()
{
    using var fixture = new Fixture();
    fixture.Parent.transform.localPosition = new Vector3(120, 80, 0);
    fixture.Anchor.pivot = UIWidget.Pivot.Center;
    fixture.Anchor.transform.localPosition = new Vector3(360, 30, 0);
    fixture.NativeSlider.transform.localPosition = new Vector3(610, 140, 0);
    Time.unscaledTime = 1;
    fixture.Control.Update();
    var caption = Bounds(Caption()); var inserted = SliderBounds(Slider()); var native = SliderBounds(fixture.NativeSlider); var value = Bounds(Percentage());
    Require(caption.Right < inserted.Left && inserted.Right < value.Left, "shifted layout overlaps the caption, slider or percentage");
    Near(inserted.Left, native.Left); Near(inserted.Right, native.Right);
    Near(Caption().transform.position.y, Slider().transform.position.y);
    Near(Percentage().transform.position.y, Slider().transform.position.y);
}
static void UserMapping()
{
    using var fixture = new Fixture(); Slider().value = 0; Near(fixture.Scale, 0.5f);
    Slider().value = 1; Near(fixture.Scale, 2); Slider().value = 1f / 3; Near(fixture.Scale, 1);
    Require(fixture.SetCalls == 3, "one setter call is required per user value change");
    Require(fixture.NativeCalls == 0, "wheel slider invoked native AUTO callback");
}
static void ExternalMapping()
{
    using var fixture = new Fixture(); fixture.Scale = 0.5f; fixture.Control.Update(); Near(Slider().value, 0);
    fixture.Scale = 2; fixture.Control.Update(); Near(Slider().value, 1);
    Require(fixture.SetCalls == 0 && fixture.NativeCalls == 0, "reading configuration wrote back through slider callbacks");
}
static void PreserveSource()
{
    using var fixture = new Fixture();
    Require(fixture.Anchor.text == "Noto Sans" && fixture.Anchor.height == 16 && fixture.Anchor.width == 300 && fixture.Anchor.fontSize == 32, "font-name source was modified");
    Require(fixture.Anchor.leftAnchor.target == fixture.Parent.transform, "source font anchor was modified");
    Near(fixture.NativeSlider.value, 0.4f); Require(fixture.NativeSlider.onChange.Count == 1, "source AUTO callbacks changed");
    fixture.NativeSlider.value = 0.6f; Require(fixture.NativeCalls == 1 && fixture.SetCalls == 0, "native callback ownership changed");
}
static void HideCopiedTicks()
{
    using var fixture = new Fixture();
    Require(Slider().GetComponentsInChildren<UILabel>(true).All(x => !x.enabled), "new wheel slider contains visible AUTO time ticks");
    Require(fixture.NativeSlider.GetComponentsInChildren<UILabel>(true).All(x => x.enabled), "source AUTO ticks were hidden");
}
static void NoDuplicates()
{
    using var fixture = new Fixture(); int initial = Labels().Length;
    Time.unscaledTime = 1; fixture.Control.Update(); Time.unscaledTime = 2; fixture.Control.Update();
    Require(Labels().Length == initial, "repeated settings discovery duplicated labels");
    Require(UnityEngine.Object.FindObjectsOfType<UISlider>().Count(x => x.gameObject.name == "AzuriteWheelSensitivitySlider") == 1, "discovery duplicated slider");
}
static void OwnedCleanup()
{
    using var fixture = new Fixture(); var owned = UnityEngine.Object.FindObjectsOfType<GameObject>().Where(x => x.name.StartsWith("AzuriteWheelSensitivity", StringComparison.Ordinal)).ToArray();
    fixture.Control.Dispose();
    Require(owned.All(x => x.Destroyed), "dispose left a control-owned object alive");
    Require(!fixture.Parent.Destroyed && !fixture.Panel.Destroyed && !fixture.Anchor.Destroyed && !fixture.NativeSlider.Destroyed, "dispose destroyed native host objects");
    Require(fixture.NativeSlider.onChange.Count == 1, "dispose changed native callback list");
}

sealed class Fixture : IDisposable
{
    public readonly GameObject Parent = new("SettingsContent");
    public readonly SettingPanel Panel;
    public readonly UI.LocalizedUILabel Anchor;
    public readonly UISlider NativeSlider;
    public readonly WheelSettingsControl Control;
    public float Scale = 1;
    public int SetCalls, NativeCalls;
    public Fixture(bool useBitmapFont = false)
    {
        Panel = Parent.AddComponent<SettingPanel>();
        var font = new GameObject("NativeFontName"); font.transform.SetParent(Parent.transform); font.transform.localPosition = new Vector3(240, 0, 0);
        Anchor = font.AddComponent<UI.LocalizedUILabel>(); font.AddComponent<FontAdapter>();
        Anchor.text = "Noto Sans"; Anchor.width = 300; Anchor.height = 16; Anchor.fontSize = 32; Anchor.pivot = UIWidget.Pivot.Left;
        if (useBitmapFont) Anchor.bitmapFont = new INGUIFont();
        else Anchor.trueTypeFont = new Font();
        Anchor.overflowMethod = UILabel.Overflow.ShrinkContent; Anchor.leftAnchor.target = Parent.transform; Anchor.topAnchor.target = Parent.transform;
        var sliderObject = new GameObject("NativeAutoDelaySlider"); sliderObject.transform.SetParent(Parent.transform); sliderObject.transform.localPosition = new Vector3(500, 100, 0);
        NativeSlider = sliderObject.AddComponent<UISlider>(); NativeSlider.SetWithoutNotify(0.4f); NativeSlider.onChange.Add(() => NativeCalls++);
        var bar = new GameObject("Background"); bar.transform.SetParent(sliderObject.transform);
        var barWidget = bar.AddComponent<UIWidget>(); barWidget.width = 500; barWidget.height = 8; barWidget.pivot = UIWidget.Pivot.Center; NativeSlider.backgroundWidget = barWidget;
        for (int i = 0; i < 5; i++)
        {
            var tick = new GameObject("NativeTick" + i); tick.transform.SetParent(sliderObject.transform); tick.transform.localPosition = new Vector3(-250 + i * 125, 25, 0);
            var label = tick.AddComponent<UILabel>(); label.text = i + "s"; label.width = 30; label.height = 20;
        }
        Panel.widgets.fontNameLabel = Anchor; Panel.widgets.autoDelaySlider = NativeSlider;
        Control = new WheelSettingsControl(() => Scale, value => { Scale = value; SetCalls++; }); Control.Update();
    }
    public void Dispose() => Control.Dispose();
}
