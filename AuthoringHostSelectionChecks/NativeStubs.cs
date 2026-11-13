// Explicit source-test boundary. No Unity or IL2CPP code is loaded or invoked.
using System.Reflection;
public class NativeObject
{
    static long next;
    public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref next);
}
namespace UnityEngine
{
    public class Object : NativeObject { public static void Destroy(Object value) { } }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true; public int layer;
        public Dictionary<Type, object> Components = new();
        public T? GetComponent<T>() where T : class => Components.GetValueOrDefault(typeof(T)) as T;
        public T? GetComponentInChildren<T>() where T : class => GetComponent<T>();
        public T? GetComponentInParent<T>() where T : class => GetComponent<T>();
    }
    public class Transform { public Vector3 TransformPoint(Vector3 point) => point; }
    public class Behaviour : Object { public bool isActiveAndEnabled = true; public GameObject gameObject = new(); public Transform transform = new(); }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x=x; this.y=y; } }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; } }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height) { this.x=x;this.y=y;this.width=width;this.height=height; }
        public float xMin => x; public float xMax => x+width; public float yMin => y; public float yMax => y+height;
        public bool Contains(Vector2 point) => point.x>=xMin && point.x<=xMax && point.y>=yMin && point.y<=yMax;
        public static Rect MinMaxRect(float a,float b,float c,float d) => new(a,b,c-a,d-b);
    }
    public class Camera : Object { public static Camera main = new(); public Vector3 WorldToScreenPoint(Vector3 p) => p; }
    public static class Application { public static bool isFocused = true; }
    public enum KeyCode { LeftControl, RightControl, LeftShift, RightShift, Escape }
    public static class Input
    {
        public static bool Down, Held, Up; public static Vector3 mousePosition;
        public static HashSet<KeyCode> Keys = new();
        public static bool GetMouseButtonDown(int i)=>Down; public static bool GetMouseButton(int i)=>Held; public static bool GetMouseButtonUp(int i)=>Up;
        public static bool GetKey(KeyCode key)=>Keys.Contains(key); public static bool GetKeyDown(KeyCode key)=>Keys.Contains(key);
    }
    public static class Time { public static int frameCount; }
    public static class Screen { public static int height = 1000; }
    public struct Color { public Color(float r,float g,float b,float a) { } public static Color white => new(); }
    public enum TextureFormat { RGBA32 }
    public enum ScaleMode { StretchToFill }
    public class Texture2D : Object
    {
        public Texture2D(int width,int height,TextureFormat format,bool mipChain) { }
        public void SetPixel(int x,int y,Color color) { } public void Apply(bool updateMipmaps,bool makeNoLongerReadable) { }
    }
    public static class GUI
    {
        public static int Draws;
        public static void BeginGroup(Rect r) { } public static void EndGroup() { }
        public static void DrawTexture(Rect position,Texture2D image,ScaleMode scaleMode,bool alphaBlend,float imageAspect,Color color,float borderWidth,float borderRadius) { Draws++; }
    }
}
public class UIWidget : UnityEngine.Behaviour
{
    public UnityEngine.Vector3[] worldCorners = Array.Empty<UnityEngine.Vector3>();
}
public class UIPanel : UnityEngine.Behaviour { public UnityEngine.Vector4 finalClipRegion = new(100,900,200,200); }
public class UICamera
{
    public static bool inputHasFocus,isDragging; public static UnityEngine.GameObject? hoveredObject;
    public UnityEngine.Camera cachedCamera = UnityEngine.Camera.main;
    public static UICamera FindCameraForLayer(int layer)=>new();
}
public class UIInput : UnityEngine.Behaviour { public static UIInput? current,selection; public static string mLastIME=""; }
public class Dragger : UnityEngine.Behaviour { }
public class UIScrollBar : UnityEngine.Behaviour { }
public class CenterableUIScrollView : UnityEngine.Behaviour { public UIPanel panel=new(); }
public class UIDragScrollView : UnityEngine.Behaviour
{
    public CenterableUIScrollView? scrollView,draggablePanel;
    public void OnDrag(UnityEngine.Vector2 delta) { }
}
public class Singleton<T> where T : new() { public static T Instance = new(); }
public class Script : NativeObject { public string LineId=""; }
namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T>
    {
        private readonly NativeObject identity = new(); public IntPtr Pointer => identity.Pointer;
    }
}
namespace Studio.Scripts.Nodes
{
    public class Node : NativeObject { public Guid guid=Guid.Parse("11111111-2222-3333-4444-555555555555"); }
    public class ScriptNode : Node { public Il2CppSystem.Collections.Generic.List<Script> scripts=new(); }
}
namespace Studio.Scripts.Window
{
    public class WindowManager { public object? activeWindow; public UnityEngine.GameObject? blocking; }
}
namespace Studio.Scripts
{
    public class ScriptListItem : UnityEngine.Behaviour
    {
        public int index; public ScriptNodeInspector inspector=null!; public Nodes.ScriptNode scriptNode=null!;
        public UIWidget? child; public UnityEngine.GameObject? insertBtn; public bool selected;
        public void OnClick() { } public void OnDraggerDragStart() { } public void Refresh() { }
        public void SetIndexAndSiblingIndex(int value) { index=value; }
        public void Select() { selected=true; inspector.selectedScriptItem=this; }
    }
    public class ScriptNodeInspector : UnityEngine.Behaviour
    {
        public static ScriptNodeInspector? instance; public Nodes.ScriptNode scriptNode=new();
        public ScriptListItem? selectedScriptItem; public bool loading,unloading,rearrangeScheduled;
        public Il2CppSystem.Collections.Generic.List<ScriptListItem> scriptNodeListItemsCache=new();
        public CenterableUIScrollView scriptListScroll=new();
        public void SyncScriptList(bool a,bool b,bool c) { } public void Load(Nodes.Node node,bool flag) { } public void Unload(bool flag) { }
    }
}
namespace HarmonyLib
{
    public class HarmonyMethod { public HarmonyMethod(Type type,string name) { } }
    public class Patches { public System.Collections.Generic.List<string> Owners=new(); }
    public class Harmony
    {
        static readonly Dictionary<MethodInfo,Patches> registered=new(); private readonly string owner;
        public Harmony(string owner) { this.owner=owner; }
        public void Patch(MethodInfo target,HarmonyMethod? prefix,HarmonyMethod? postfix) { registered[target]=new Patches { Owners=new(){owner} }; }
        public void UnpatchSelf() { foreach(var key in registered.Keys.ToArray()) if(registered[key].Owners.Contains(owner)) registered.Remove(key); }
        public static Patches? GetPatchInfo(MethodInfo target)=>registered.GetValueOrDefault(target);
    }
}
// Only needed to compile the unmodified legacy negative-control source.
namespace Azurite
{
    internal static class RuntimeMethodContract
    {
        internal static bool TryResolve(Type type,string name,Type[] args,out MethodInfo? result)
        {
            result=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null);
            return result!=null && result.ReturnType==typeof(void);
        }
    }
}
