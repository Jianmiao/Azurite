using System;
using System.Threading;

namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T> { }
}
namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; }
    public class Component
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
        public GameObject gameObject = new();
        public bool isActiveAndEnabled = true;
    }
    public readonly record struct Vector2(float x, float y)
    {
        public float sqrMagnitude => x * x + y * y;
    }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public float sqrMagnitude => x * x + y * y + z * z;
    }
    public static class Application { public static bool isFocused = true; }
    public static class Screen { public static int width = 1920, height = 1080; }
    public static class Input
    {
        public static Vector3 mousePosition;
        public static Vector2 mouseScrollDelta;
        public static bool anyKey, anyKeyDown;
        public static bool GetMouseButton(int button) => false;
    }
    public class SpringPanel : Component { }
}
namespace UnityEngine.SceneManagement
{
    public readonly record struct Scene(int handle);
    public static class SceneManager { public static Scene GetActiveScene() => new(1); }
}
public class UIScrollView : UnityEngine.Component
{
    public float mScroll;
    public UnityEngine.Vector3 currentMomentum;
    public bool isDragging;
}
public class UITable : UnityEngine.Component { public bool mReposition; }
public static class UICamera { public static bool inputHasFocus, isDragging; }
public class UIInput
{
    public static UIInput? current, selection;
    public static string mLastIME = "";
    public bool isSelected;
}
public static class Singleton<T> where T : class { public static T? Instance; }
public class Loading : UnityEngine.Component
{
    public static Il2CppSystem.Collections.Generic.List<string>? LoadingTags = new();
    public int loadingCount;
}
public class ScenarioResourceManager
{
    public bool Preloading, AreDbsLoaded = true;
}
public class Catalog : UnityEngine.Component
{
    public class UIProfile
    {
        public UnityEngine.GameObject root = new();
        public UIScrollView scroll = new();
        public UITable table = new();
    }
    public Il2CppSystem.Collections.Generic.List<UIProfile> uiProfiles = new();
    public int currentProfileIndex;
}
public class Test : UnityEngine.Component
{
    public bool previewMode, auto, hasVoice, protectedActivity;
    public object? delayedAdvanceTask;
    public int currentAnims, backgroundAnimations, currentSTs;
}
namespace AzureArchive.Automation
{
    public class AuthoringEditorSession
    {
        public static AuthoringEditorSession? Current = new();
        public bool SaveInProgress, IsApplying;
    }
}
namespace Studio.Scripts
{
    public class StudioCommon : UnityEngine.Component
    {
        public static StudioCommon? instance;
        public object? entryNode = new();
        public string sessionInitializationError = "";
        public bool autoSavePending, autoCompilePending;
    }
    public class CenterableUIScrollView : UIScrollView { public UnityEngine.SpringPanel? spring; }
    public class ScriptNodeInspector : UnityEngine.Component
    {
        public static ScriptNodeInspector? instance;
        public bool loading, unloading, rearrangeScheduled;
        public object? scriptNode = new();
        public Test? preview;
        public CenterableUIScrollView scriptListScroll = new(), characterTabScroll = new(), environmentTabScroll = new();
    }
}
namespace Studio.Scripts.Window
{
    public interface IWindow { IntPtr Pointer { get; } }
    public class WindowManager
    {
        public IWindow? activeWindow;
        public UnityEngine.GameObject? blocking;
    }
}
namespace Studio.Scripts.Window.BackgroundExplorer { public class BackgroundExplorer : UnityEngine.Component { } }
namespace Studio.Scripts.Window.BGMExplorer { public class BGMExplorer : UnityEngine.Component { } }
namespace Studio.Scripts.Window.CharacterExplorer { public class CharacterExplorer : UnityEngine.Component { } }
namespace Studio.Scripts.Window.EmotionExplorer { public class EmotionExplorer : UnityEngine.Component { } }
namespace Studio.Scripts.Window.PopupImageExplorer { public class PopupImageExplorer : UnityEngine.Component { } }
namespace Studio.Scripts.Window.SoundExplorer { public class SoundExplorer : UnityEngine.Component { } }
namespace Azurite
{
    internal enum CharacterAnimationActivity { Static, Ambient, Protected }
    internal static class AnimationActivity { internal static int CountPending(int count) => count; }
    internal static class CharacterActivity
    {
        internal static CharacterAnimationActivity Observe(Test controller, bool allowAmbient) =>
            controller.protectedActivity ? CharacterAnimationActivity.Protected : CharacterAnimationActivity.Static;
    }
    internal static class OptionalHostActivity
    {
        internal static bool CurrentEffectActive(Test controller) => false;
        internal static bool CustomEffectActive(Test controller) => false;
        internal static bool SearchPending(Catalog catalog) => false;
    }
}
