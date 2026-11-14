using System.Text.Json;
using System.Text.Json.Nodes;

public class NativeObject { static long next; public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next); public bool isActiveAndEnabled=true,enabled=true;public UnityEngine.GameObject gameObject=new();public UnityEngine.Transform transform=>gameObject.transform; }
namespace UnityEngine
{
    public class Object {public static void Destroy(Texture2D value){} public static void Destroy(GameObject value){if(EventDelegate.Depth!=0)throw new Exception("Destroyed during UI callback");value.Destroy();}}
    public class GameObject
    {
        public static List<GameObject> All=new();public static int Added,FailOnAdd=-1,Destroyed;public string name="";private bool activeSelf=true;public bool activeInHierarchy{get=>activeSelf&&(transform.parent?.gameObject.activeInHierarchy??true)&&!IsDestroyed;set=>activeSelf=value;}public bool IsDestroyed;static long nextPointer;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref nextPointer);public int layer;
        public bool HasCollider;public Transform transform;readonly Dictionary<Type,object> components=new();
        public GameObject(){transform=new(this);All.Add(this);}
        public T AddComponent<T>() where T:NativeObject,new(){if(++Added==FailOnAdd)throw new InvalidOperationException("fixture component creation failure");var c=new T{gameObject=this};components[typeof(T)]=c;return c;}
        public T? GetComponent<T>() where T:class=>components.Values.OfType<T>().FirstOrDefault();
        public T? GetComponentInParent<T>() where T:class=>GetComponent<T>()??transform.parent?.gameObject.GetComponentInParent<T>();
        public T? GetComponentInChildren<T>() where T:class => GetComponent<T>()??transform.Children.Select(c=>c.gameObject.GetComponentInChildren<T>()).FirstOrDefault(c=>c!=null);
        public void SetActive(bool value){activeInHierarchy=value;}
        internal void Destroy(){if(IsDestroyed)return;foreach(var child in transform.Children.ToArray())child.gameObject.Destroy();transform.parent?.Children.Remove(transform);IsDestroyed=true;Destroyed++;}
    }
    public class Transform {public GameObject gameObject;public Transform? parent;public List<Transform> Children=new();public Vector3 localPosition,localScale=Vector3.one;public Transform(GameObject g){gameObject=g;}public void SetParent(Transform p,bool worldPositionStays){parent=p;p.Children.Add(this);}public Transform GetChild(int index)=>Children[index];public Vector3 TransformPoint(Vector3 p)=>new(p.x*localScale.x+localPosition.x,p.y*localScale.y+localPosition.y,p.z*localScale.z+localPosition.z);public Vector3 InverseTransformPoint(Vector3 p)=>new((p.x-localPosition.x)/localScale.x,(p.y-localPosition.y)/localScale.y,(p.z-localPosition.z)/localScale.z);}
    public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new(1,1,1);}
    public struct Vector4 {public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
    public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color white=>new(1,1,1);}
    public class Texture { } public class Texture2D:Texture {public static Texture2D whiteTexture=new();public Texture2D(){} public Texture2D(int width,int height,TextureFormat format,bool mipChain){} public void SetPixel(int x,int y,Color color){} public void Apply(bool updateMipmaps,bool makeNoLongerReadable){} } public class Font { }
    public enum FontStyle {Normal,Bold,Italic}
}
public class UIWidget:NativeObject { public static int CornerReads;private UnityEngine.Vector3[] corners=Array.Empty<UnityEngine.Vector3>();public UnityEngine.Vector3[] worldCorners{get{CornerReads++;return corners;}set=>corners=value;} public enum Pivot{TopLeft,Left,Center}public int width,height,depth;public UnityEngine.Color color;public Pivot pivot;}
public class UILabel:UIWidget {public enum Overflow{ClampContent,ResizeHeight,ShrinkContent}public string text="";public bool supportEncoding=true;public object? bitmapFont;public UnityEngine.Font? trueTypeFont;public int fontSize=16,spacingY,maxLineCount;public UnityEngine.FontStyle fontStyle;public NGUIText.Alignment alignment;public Overflow overflowMethod;}
public class UITexture:UIWidget {public UnityEngine.Texture? mainTexture;}
public class UIButton:NativeObject {public bool isEnabled=true;public UnityEngine.GameObject? tweenTarget;public UnityEngine.Color defaultColor,hover,pressed,disabledColor;public List<EventDelegate> onClick=new();public void Click(){if(isEnabled&&!gameObject.IsDestroyed)EventDelegate.Fire(onClick);}}
public class UIInput:NativeObject
{
    public static UIInput? current,selection;public static string mLastIME="";
    public enum Validation{None}public enum OnReturnKey{Submit,NewLine}
    public UILabel label=null!;public Validation validation;public OnReturnKey onReturnKey;public int characterLimit;public UnityEngine.Color defaultColor;public List<EventDelegate> onChange=new(),onSubmit=new();string text="";
    public string value{get=>text;set{if(text==value)return;text=value;if(label!=null)label.text=value;EventDelegate.Fire(onChange);}}
}
public static class NGUIText {public enum Alignment{Left,Center,Right}}
public static class UIDrawCall {public enum Clipping{None,SoftClip}}
public class UIPanel:NativeObject {public UnityEngine.Vector4 finalClipRegion=new(100,900,200,200);public int depth;public UIDrawCall.Clipping clipping;public UnityEngine.Vector4 baseClipRegion;public UnityEngine.Vector3[] worldCorners=new[]{new UnityEngine.Vector3(-600,-650,0),new UnityEngine.Vector3(-600,650,0),new UnityEngine.Vector3(600,650,0),new UnityEngine.Vector3(600,-650,0)};}
public class UIScrollView:NativeObject {public enum Movement{Vertical}public Movement movement;public int Resets;public void ResetPosition(){Resets++;}public void UpdateScrollbars(bool value){} }
public class UIDragScrollView:NativeObject {public UIScrollView scrollView=null!;public UIScrollView? draggablePanel;public void OnDrag(UnityEngine.Vector2 delta){} }
public static class NGUITools
{
    public static UnityEngine.GameObject AddChild(UnityEngine.GameObject parent){var child=new UnityEngine.GameObject{layer=parent.layer};child.transform.SetParent(parent.transform,false);return child;}
    public static void AddWidgetCollider(UnityEngine.GameObject obj) { obj.HasCollider=true; }
}
public sealed class EventDelegate
{
    public delegate void Callback();public Callback CallbackValue=null!;public static int Depth;
    public static void Add(List<EventDelegate> list,Callback callback)=>list.Add(new(){CallbackValue=callback});
    public static void Remove(List<EventDelegate> list,Callback callback)=>list.RemoveAll(x=>x.CallbackValue==callback);
    public static void Fire(List<EventDelegate> list){Depth++;try{foreach(var e in list.ToArray())e.CallbackValue();}finally{Depth--;}}
}
namespace Il2CppInterop.Runtime {public static class DelegateSupport {public static T? ConvertDelegate<T>(Action action) where T:Delegate=>(T)(Delegate)new EventDelegate.Callback(action);}}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {public class Il2CppStringArray { } }
namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T>
    {
        static long next; public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);
    }
}
namespace Studio.Scripts.Window { public class WindowManager { public object? activeWindow;public UnityEngine.GameObject? blocking; } }
public class Singleton<T> where T:new() { public static T Instance=new(); }
public class CenterableUIScrollView:UIScrollView { public UIPanel panel=new(); }
public class Dragger:NativeObject { }
public class UIScrollBar:NativeObject { }
public class UICamera
{
    public static bool inputHasFocus,isDragging;public static UnityEngine.GameObject? hoveredObject;
    public UnityEngine.Camera cachedCamera=UnityEngine.Camera.main;public static UICamera FindCameraForLayer(int layer)=>new();
}
namespace UnityEngine
{
    public class Camera { public static Camera main=new();public Vector3 WorldToScreenPoint(Vector3 value)=>value; }
    public static class Application { public static bool isFocused=true; }
    public enum KeyCode { LeftControl,RightControl,LeftShift,RightShift,Escape }
    public static class Input
    {
        public static bool Down,Held,Up;public static Vector3 mousePosition;public static HashSet<KeyCode> Keys=new();
        public static bool GetMouseButtonDown(int i)=>Down;public static bool GetMouseButton(int i)=>Held;public static bool GetMouseButtonUp(int i)=>Up;
        public static bool GetKey(KeyCode key)=>Keys.Contains(key);public static bool GetKeyDown(KeyCode key)=>Keys.Contains(key);
    }
    public static class Time { public static int frameCount; }
    public static class Screen { public static int height=1000; }
    public enum TextureFormat { RGBA32 }
    public enum ScaleMode { StretchToFill }
    public struct Rect
    {
        public float x,y,width,height;public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
        public float xMin=>x;public float xMax=>x+width;public float yMin=>y;public float yMax=>y+height;
        public bool Contains(Vector2 p)=>p.x>=xMin&&p.x<=xMax&&p.y>=yMin&&p.y<=yMax;
        public static Rect MinMaxRect(float a,float b,float c,float d)=>new(a,b,c-a,d-b);
    }
    public static class GUI
    {
        public static void BeginGroup(Rect value){}public static void EndGroup(){}
        public static void DrawTexture(Rect position,Texture2D image,ScaleMode scaleMode,bool alphaBlend,float imageAspect,Color color,float borderWidth,float borderRadius){}
    }
}
public class Script : NativeObject { public string LineId="anchor"; public string StageJson="[]"; }
public class ScriptData : NativeObject
{
    public string StageJson="[]";
    public static implicit operator Script(ScriptData data)=>new(){LineId="detached-only",StageJson=data.StageJson};
}
namespace Newtonsoft.Json.Linq
{
    public sealed class JObject
    {
        public string Raw="{}";
        public static JObject Parse(string json){using var d=JsonDocument.Parse(json);return new(){Raw=json};}
        public override string ToString()=>Raw;
    }
}
namespace Studio.Scripts.Nodes { public class Node:NativeObject {public Guid guid=Guid.NewGuid();} public class ScriptNode:Node { public Il2CppSystem.Collections.Generic.List<Script> scripts=new(); } }
namespace Studio.Scripts
{
    public class StudioCommon:NativeObject { public static StudioCommon? instance;public object? entryNode=new();public string sessionInitializationError=""; }
    public class ScriptListItem:NativeObject { public int index; public bool selected=true; public ScriptNodeInspector inspector=null!; public Nodes.ScriptNode scriptNode=null!;public UIWidget? child;public UnityEngine.GameObject? insertBtn;public void OnClick(){}public void OnDraggerDragStart(){}public void Refresh(){}public void SetIndexAndSiblingIndex(int value){index=value;}public void Select(){selected=true;inspector.selectedScriptItem=this;} }
    public class ScriptNodeInspector:NativeObject
    {
        public static ScriptNodeInspector? instance;public Nodes.ScriptNode scriptNode=null!;public ScriptListItem selectedScriptItem=null!;
        public CenterableUIScrollView scriptListScroll=new();public Il2CppSystem.Collections.Generic.List<ScriptListItem> scriptNodeListItemsCache=new();public int selectedSlotNum=2;public bool loading,unloading,rearrangeScheduled;public UIInput? contentInput,nodeNameInput;public UILabel? nicknameLabel;public void SyncScriptList(bool a,bool b,bool c){}public void Load(Nodes.Node node,bool flag){}public void Unload(bool flag){}
    }
}
namespace AzureArchive.Automation
{
    using Newtonsoft.Json.Linq;
    public class AuthoringEditorSession:NativeObject
    {
        public static AuthoringEditorSession? Current;public string ProjectId="fixture-project",EditSession="fixture-session",Revision="rev:1";public Studio.Scripts.StudioCommon studio=null!;
        public bool IsApplying,SaveInProgress,disposed;
    }
    public static class AuthoringApiSchema { public static string Schema="{}";public static int Calls;public static JObject GetSchema(){Calls++;return JObject.Parse(Schema);} }
    public static class AuthoringResourceCatalog
    {
        public static string CatalogVersion="fixture-catalog";
        public static Dictionary<string,string> Resources=new();public static List<(string Id,string Kind,string Version,bool Capabilities)> Calls=new();public static Action? AfterResolve;
        public static Queue<string> SearchResults=new();
        public static List<(string Kind,string Query,string? Cursor,int Limit,string Version)> Searches=new();public static Action? AfterSearch;
        public static bool NullSearch,NullResolve;
        public static JObject Search(string kind,string query,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray? ids,string? cursor,int limit,string expectedCatalogVersion)
        {
            if(ids!=null)throw new Exception("fixture expects query mode with null ids");
            Searches.Add((kind,query,cursor,limit,expectedCatalogVersion));
            if(NullSearch)return null!;
            if(expectedCatalogVersion!=CatalogVersion)throw new InvalidOperationException("CATALOG_CHANGED");
            if(SearchResults.Count==0)throw new InvalidOperationException("No explicit fixture page queued");
            string result=SearchResults.Dequeue();AfterSearch?.Invoke();return JObject.Parse(result);
        }
        public static JObject Resolve(string resourceId,string expectedKind,string expectedCatalogVersion,bool includeCapabilities)
        {
            Calls.Add((resourceId,expectedKind,expectedCatalogVersion,includeCapabilities));
            if(NullResolve)return null!;
            if(expectedCatalogVersion!=CatalogVersion)throw new InvalidOperationException("CATALOG_CHANGED");
            if(!Resources.TryGetValue(resourceId,out var json))throw new InvalidOperationException("RESOURCE_NOT_FOUND");
            AfterResolve?.Invoke();return JObject.Parse(json);
        }
    }
    public static class AuthoringLines
    {
        public static Queue<string> Stages=new();public static List<Script> Previous=new();public static int Creates,Reads;public static Action? AfterCreate;
        public static ScriptData Create(JObject input,Script previous,object? resolver,string catalogVersion)
        {
            using var d=JsonDocument.Parse(input.Raw);var p=d.RootElement;
            if(p.GetProperty("inherit").GetString()!="previous" || p.GetProperty("mode").GetString()!="control" || p.GetProperty("speaker").ValueKind!=JsonValueKind.Null || p.GetProperty("text").GetString()!="" || resolver!=null || catalogVersion!="fixture-catalog")throw new Exception("wrong native inheritance probe");
            Creates++;Previous.Add(previous);AfterCreate?.Invoke();return new(){StageJson=Stages.Count==0?previous.StageJson:Stages.Dequeue()};
        }
        public static JObject Read(ScriptData data,Guid nodeId)
        {
            Reads++;return JObject.Parse(JsonSerializer.Serialize(new{node_id=nodeId.ToString(),line_id="detached-id",stage=JsonDocument.Parse(data.StageJson).RootElement}));
        }
    }
}



namespace HarmonyLib
{
    public class HarmonyMethod { public HarmonyMethod(Type type,string name) { } }
    public class Patches { public System.Collections.Generic.List<string> Owners=new(); }
    public class Harmony
    {
        static readonly Dictionary<System.Reflection.MethodInfo,Patches> registered=new(); private readonly string owner;
        public Harmony(string owner) { this.owner=owner; }
        public void Patch(System.Reflection.MethodInfo target,HarmonyMethod? prefix,HarmonyMethod? postfix) { registered[target]=new Patches { Owners=new(){owner} }; }
        public void UnpatchSelf() { foreach(var key in registered.Keys.ToArray()) if(registered[key].Owners.Contains(owner)) registered.Remove(key); }
        public static Patches? GetPatchInfo(System.Reflection.MethodInfo target)=>registered.GetValueOrDefault(target);
    }
}
// Only needed to compile the unmodified legacy negative-control source.
namespace Azurite
{
    internal static class RuntimeMethodContract
    {
        internal static bool TryResolve(Type type,string name,Type[] args,out System.Reflection.MethodInfo? result)
        {
            result=type.GetMethod(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,args,null);
            return result!=null && result.ReturnType==typeof(void);
        }
    }
}
