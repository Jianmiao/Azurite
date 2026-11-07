namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppArrayBase<T> : System.Collections.Generic.List<T>
    { public Il2CppArrayBase(IEnumerable<T> source) : base(source) { } }
}
namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T> { }
}
namespace UnityEngine
{
    public class Object
    {
        private static long _next;
        public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _next);
        public static readonly System.Collections.Generic.List<Object> Registry = new();
        public static T[] FindObjectsOfType<T>() => Registry.OfType<T>().ToArray();
    }
    public class Component : Object
    {
        public readonly System.Collections.Generic.List<Component> Children = new();
        public Component Parent;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> GetComponentsInChildren<T>(bool includeInactive) => new(Children.OfType<T>());
        public T GetComponentInParent<T>() where T : class => Parent as T;
    }
}
public class UIScrollView : UnityEngine.Component { public float scrollWheelFactor = 10; }
public class UITable : UnityEngine.Component { }
public class SettingPanel : UnityEngine.Component { }
public static class Singleton<T> { public static T Instance; }
public class Catalog
{
    public class UIProfile { public UIScrollView scroll = new(); }
    public Il2CppSystem.Collections.Generic.List<UIProfile> uiProfiles = new();
}
namespace UI
{
    public class CenterableUIScrollView : UIScrollView { }
    public class UIPopupModManager { public static UIPopupModManager instance; public UIScrollView scroll = new(); }
}
namespace Studio.Scripts
{
    public class ScriptNodeInspector
    {
        public static ScriptNodeInspector instance;
        public UIScrollView scriptListScroll = new();
        public UIScrollView characterTabScroll = new();
        public UIScrollView environmentTabScroll = new();
    }
}
namespace Studio.Scripts.Window.BackgroundExplorer
{
    public class BackgroundExplorer { public UIScrollView scroll = new(); public BgSortingHierarchy hierarchy = new(); }
    public class BgSortingHierarchy { public UITable rootTable = new() { Parent = new UIScrollView() }; }
}
namespace Studio.Scripts.Window.CharacterExplorer { public class CharacterExplorer : UnityEngine.Component { } }
namespace Studio.Scripts.Window.EmotionExplorer { public class EmotionExplorer : UnityEngine.Component { } }
