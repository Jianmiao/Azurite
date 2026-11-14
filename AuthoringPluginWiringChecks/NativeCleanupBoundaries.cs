// Only the external Unity/IL2CPP/logging surface is doubled. The FPS and
// scroll controllers, their cleanup logic and Plugin aggregation are real.
using Azurite;

static class CleanupNative
{
    internal static int Target=-1, Sync=2;
    internal static bool ThrowTargetRead, ThrowTargetWrite, IgnoreTargetWrite, ThrowSyncRead, ThrowSyncWrite, IgnoreSyncWrite, ThrowUnsubscribe;
    internal static readonly List<string> Calls=new();
    internal static void Reset()
    {
        Target=-1;Sync=2;ThrowTargetRead=ThrowTargetWrite=IgnoreTargetWrite=ThrowSyncRead=ThrowSyncWrite=IgnoreSyncWrite=ThrowUnsubscribe=false;
        Calls.Clear();Singleton<UserSettings>.Instance=new();Singleton<Catalog>.Instance=null!;
        Singleton<Studio.Scripts.Window.BackgroundExplorer.BackgroundExplorer>.Instance=null!;
        Singleton<Studio.Scripts.Window.CharacterExplorer.CharacterExplorer>.Instance=null!;
        Singleton<Studio.Scripts.Window.EmotionExplorer.EmotionExplorer>.Instance=null!;
        Studio.Scripts.ScriptNodeInspector.instance=null!;UI.UIPopupModManager.instance=null!;UnityEngine.Object.Registry.Clear();
    }
}
namespace UnityEngine
{
    public static class Application
    {
        public static int targetFrameRate
        {
            get { CleanupNative.Calls.Add("read-target");if(CleanupNative.ThrowTargetRead)throw new InvalidOperationException("target read failed");return CleanupNative.Target; }
            set { CleanupNative.Calls.Add("write-target:"+value);if(CleanupNative.ThrowTargetWrite)throw new InvalidOperationException("target write failed");if(!CleanupNative.IgnoreTargetWrite)CleanupNative.Target=value; }
        }
    }
    public static class QualitySettings
    {
        public static int vSyncCount
        {
            get { CleanupNative.Calls.Add("read-sync");if(CleanupNative.ThrowSyncRead)throw new InvalidOperationException("sync read failed");return CleanupNative.Sync; }
            set { CleanupNative.Calls.Add("write-sync:"+value);if(CleanupNative.ThrowSyncWrite)throw new InvalidOperationException("sync write failed");if(!CleanupNative.IgnoreSyncWrite)CleanupNative.Sync=value; }
        }
    }
    public class Object
    {
        private static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);
        public static readonly List<Object> Registry=new();
        public static T[] FindObjectsOfType<T>()=>Registry.OfType<T>().ToArray();
    }
    public class Component:Object
    {
        public readonly List<Component> Children=new();public Component Parent=null!;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> GetComponentsInChildren<T>(bool includeInactive)=>new(Children.OfType<T>());
        public T GetComponentInParent<T>() where T:class => (Parent as T)!;
    }
}
public class UIScrollView:UnityEngine.Component
{
    public float RawFactor=10;public bool ThrowRead,ThrowWrite,IgnoreWrite;public int Reads,Writes;
    public float scrollWheelFactor
    {
        get { Reads++;CleanupNative.Calls.Add("read-scroll:"+Pointer);if(ThrowRead)throw new InvalidOperationException("scroll read failed");return RawFactor; }
        set { Writes++;CleanupNative.Calls.Add("write-scroll:"+Pointer+":"+value);if(ThrowWrite)throw new InvalidOperationException("scroll write failed");if(!IgnoreWrite)RawFactor=value; }
    }
}
public class UITable:UnityEngine.Component { }
public class SettingPanel:UnityEngine.Component { }
public static class Singleton<T> where T:class { public static T Instance=null!; }
public class Catalog
{
    public class UIProfile { public UIScrollView scroll=new(); }
    public Il2CppSystem.Collections.Generic.List<UIProfile> uiProfiles=new();
}
public class UserSettings
{
    private static int next;public IntPtr Pointer=new(++next);public bool isReady=true;public int fpsTier;
    private Il2CppSystem.Action<int>? changed;
    public void add_OnFpsTierChanged(Il2CppSystem.Action<int> callback){CleanupNative.Calls.Add("subscribe");changed=callback;}
    public void remove_OnFpsTierChanged(Il2CppSystem.Action<int> callback){CleanupNative.Calls.Add("unsubscribe");if(CleanupNative.ThrowUnsubscribe)throw new InvalidOperationException("unsubscribe failed");if(changed==callback)changed=null;}
}
namespace Il2CppSystem { public class Action<T> { internal readonly System.Action<T> Callback;public Action(System.Action<T> callback)=>Callback=callback; } }
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport { public static T ConvertDelegate<T>(System.Delegate callback) where T:class => (T)(object)new Il2CppSystem.Action<int>((System.Action<int>)callback); }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppArrayBase<T>:List<T> { public Il2CppArrayBase(IEnumerable<T> source):base(source) { } }
}
namespace Il2CppSystem.Collections.Generic { public class List<T>:System.Collections.Generic.List<T> { } }
namespace BepInEx.Logging
{
    public class ManualLogSource { public readonly List<string> Warnings=new();public void LogInfo(object value){} public void LogWarning(object value)=>Warnings.Add(value.ToString()!); }
}
namespace BepInEx.Core.Logging.Interpolation
{
    public struct BepInExInfoLogInterpolatedStringHandler { public BepInExInfoLogInterpolatedStringHandler(int literal,int formatted,out bool enabled)=>enabled=true;public void AppendLiteral(string value){}public void AppendFormatted<T>(T value){} }
    public struct BepInExWarningLogInterpolatedStringHandler { public BepInExWarningLogInterpolatedStringHandler(int literal,int formatted,out bool enabled)=>enabled=true;public void AppendLiteral(string value){}public void AppendFormatted<T>(T value){} }
}
namespace Azurite { internal static class CurrentDisplayRate { internal static bool TryRead(out DisplayRatePolicy display)=>DisplayRatePolicy.TryCreate(240,out display); } }
namespace UI
{
    public class CenterableUIScrollView:UIScrollView { }
    public class UIPopupModManager { public static UIPopupModManager instance=null!;public UIScrollView scroll=new(); }
}
namespace Studio.Scripts
{
    public class ScriptNodeInspector { public static ScriptNodeInspector instance=null!;public UIScrollView scriptListScroll=new(),characterTabScroll=new(),environmentTabScroll=new(); }
}
namespace Studio.Scripts.Window.BackgroundExplorer
{
    public class BackgroundExplorer { public UIScrollView scroll=new();public BgSortingHierarchy hierarchy=new(); }
    public class BgSortingHierarchy { public UITable rootTable=new(){Parent=new UIScrollView()}; }
}
namespace Studio.Scripts.Window.CharacterExplorer { public class CharacterExplorer:UnityEngine.Component { } }
namespace Studio.Scripts.Window.EmotionExplorer { public class EmotionExplorer:UnityEngine.Component { } }
