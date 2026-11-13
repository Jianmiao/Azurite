using Azurite;
using Azurite.Core;

static partial class Program
{
    static void Panel()
    {
        Test("native panel buttons bind preview and explicit single confirmation", () => {
            var host = new CardHost(); var draft = Draft(Transition(), Language());
            var preview = new UIButton(); var confirm = new UIButton(); var cancel = new UIButton(); var recover = new UIButton(); var text = new UILabel(); var status = new UILabel();
            using var panel = new AuthoringCardPackPanel(new(host), () => draft, preview, confirm, cancel, recover, text, status);
            Require(!confirm.isEnabled && !recover.isEnabled, "panel allowed confirm or recovery before preview");
            preview.Click();
            Require(host.Commits == 0 && confirm.isEnabled && text.text.Contains("老师，我们出发吧。"), "preview click failed to render production preview");
            confirm.Click(); confirm.Click();
            Require(host.Commits == 1 && status.text.Contains("APPLIED") && !confirm.isEnabled, "confirm click did not consume token exactly once");
        });
        Test("panel draft changes and cancellation revoke the displayed confirmation", () => {
            var host = new CardHost(); var draft = Draft(Language());
            var preview = new UIButton(); var confirm = new UIButton(); var cancel = new UIButton(); var recover = new UIButton(); var text = new UILabel(); var status = new UILabel();
            using var panel = new AuthoringCardPackPanel(new(host), () => draft, preview, confirm, cancel, recover, text, status);
            preview.Click(); draft = draft with { SelectedLocale = "en" }; panel.Update();
            Require(!confirm.isEnabled && status.text.Contains("DRAFT_CHANGED"), "changed UI input retained confirmation");
            confirm.Click(); Require(host.Commits == 0, "changed draft committed from UI");
            preview.Click(); Require(text.text.Contains("Sensei, let's go."), "new locale not previewed");
            cancel.Click(); confirm.Click(); Require(host.Commits == 0 && !confirm.isEnabled, "cancel click retained confirmation");
        });
        Test("panel recovery button requires verified host recovery and never retries", () => {
            var host = new CardHost { CommitError = new Exception("Apply uncertain") }; var draft = Draft(Transition());
            var preview = new UIButton(); var confirm = new UIButton(); var cancel = new UIButton(); var recover = new UIButton(); var text = new UILabel(); var status = new UILabel();
            using var panel = new AuthoringCardPackPanel(new(host), () => draft, preview, confirm, cancel, recover, text, status);
            preview.Click(); confirm.Click();
            Require(host.Commits == 1 && recover.isEnabled && !preview.isEnabled && !confirm.isEnabled, "uncertain UI state still allowed writes");
            recover.Click(); Require(recover.isEnabled && !preview.isEnabled && host.Commits == 1, "failed recovery unlocked/retried");
            host.CanRecover = true; recover.Click();
            Require(preview.isEnabled && !confirm.isEnabled && !recover.isEnabled && host.Commits == 1, "successful recovery replayed or did not request new preview");
        });
        Test("panel keeps supplied dialogue literal and detaches every owned callback", () => {
            var host = new CardHost(); var draft = Draft(Language() with { Variants = new Dictionary<string,string> { ["zh-CN"] = "[FF0000]原文[-]" } });
            var preview = new UIButton(); var confirm = new UIButton(); var cancel = new UIButton(); var recover = new UIButton(); var text = new UILabel(); var status = new UILabel();
            var panel = new AuthoringCardPackPanel(new(host), () => draft, preview, confirm, cancel, recover, text, status);
            preview.Click(); Require(!text.supportEncoding && !status.supportEncoding && text.text.Contains("[FF0000]原文[-]"), "NGUI markup changed supplied preview text");
            var captures = host.Captures; panel.Dispose(); panel.Dispose(); preview.Click(); confirm.Click();
            Require(host.Captures == captures && host.Commits == 0 && new[] { preview, confirm, cancel, recover }.All(x => x.onClick.Count == 0), "disposed panel retained callback or token");
        });
        Test("panel rejects reused native callback controls before binding anything", () => {
            var host = new CardHost(); var preview = new UIButton(); var confirm = new UIButton(); var cancel = new UIButton(); var recover = new UIButton();
            EventDelegate.Add(confirm.onClick, () => throw new Exception("native inherited callback must not be invoked"));
            bool rejected = false;
            try { using var panel = new AuthoringCardPackPanel(new(host), () => Draft(Transition()), preview, confirm, cancel, recover, new(), new()); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && preview.onClick.Count == 0 && confirm.onClick.Count == 1, "panel mixed with native callbacks or partly bound on rejection");
        });
    }
}

// Only the external NGUI/IL2CPP event boundary is substituted. The linked panel
// implementation, controller, JSON parser and native plan compiler run unchanged.
public sealed class UILabel { public string text = ""; public bool supportEncoding = true; }
public sealed class UIButton
{
    private static int _nextPointer;
    public IntPtr Pointer = (IntPtr)(++_nextPointer);
    public List<EventDelegate> onClick = new();
    public bool isEnabled = true;
    public void Click() { if (isEnabled) foreach (var item in onClick.ToArray()) item.callback(); }
}
public sealed class EventDelegate
{
    public delegate void Callback();
    public Callback callback = null!;
    public static void Add(List<EventDelegate> list, Callback callback) => list.Add(new() { callback = callback });
    public static void Remove(List<EventDelegate> list, Callback callback) => list.RemoveAll(item => item.callback == callback);
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(Action action) where T : Delegate => (T)(Delegate)new EventDelegate.Callback(action);
    }
}
