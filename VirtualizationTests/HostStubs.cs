using System.Reflection;
using System.Runtime.ExceptionServices;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using UnityEngine;

// This host intentionally replays the verified Fix native call chain, including
// IL2CPP's inlined Init in SyncScriptList. It is not a Unity or Harmony runtime.
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        private static int _next;
        public IntPtr Pointer { get; } = new(Interlocked.Increment(ref _next));
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public MethodInfo Method; public int priority;
        public HarmonyMethod(Type type, string name) => Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    }
    public static class Priority { public const int First = 800; }
    public static class AccessTools
    {
        public static MethodInfo? Method(Type type, string name, Type[] args) => type.GetMethod(name, args);
    }
    public sealed class PatchInfo { public List<string> Owners = new(); }
    public sealed class Harmony
    {
        private readonly string _owner;
        internal static readonly Dictionary<MethodInfo, (string Owner, HarmonyMethod? Prefix, HarmonyMethod? Postfix, HarmonyMethod? Finalizer)> Patches = new();
        public static string? RejectMethod;
        public Harmony(string owner) => _owner = owner;
        public void Patch(MethodInfo method, HarmonyMethod? prefix, HarmonyMethod? postfix, object? transpiler, HarmonyMethod? finalizer, object? ilmanipulator)
        {
            if (RejectMethod == method.Name) return;
            Patches[method] = (_owner, prefix, postfix, finalizer);
        }
        public static PatchInfo? GetPatchInfo(MethodInfo method) => Patches.TryGetValue(method, out var patch) ? new() { Owners = new() { patch.Owner } } : null;
        public void UnpatchSelf() { foreach (var key in Patches.Where(p => p.Value.Owner == _owner).Select(p => p.Key).ToArray()) Patches.Remove(key); }
    }
}
public static class Hooks
{
    public static T Call<T>(Type type, string method, Type[] types, object? instance, object?[] args, Func<T> original)
    {
        MethodInfo target = type.GetMethod(method, types)!;
        if (!HarmonyLib.Harmony.Patches.TryGetValue(target, out var hooks)) return original();
        object? state = null, result = default(T);
        Exception? error = null;
        try
        {
            object? allowed = Invoke(hooks.Prefix);
            if (allowed is not false) result = original();
            Invoke(hooks.Postfix);
        }
        catch (Exception failure) { error = failure; }
        if (hooks.Finalizer != null) error = (Exception?)Invoke(hooks.Finalizer);
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        return (T)result!;

        object? Invoke(HarmonyLib.HarmonyMethod? hook)
        {
            if (hook == null) return null;
            var parameters = hook.Method.GetParameters();
            object?[] inputs = parameters.Select(p => p.Name switch
            {
                "__instance" => instance,
                "__state" => state ?? Activator.CreateInstance(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType),
                "__exception" => error,
                "__result" => result,
                "__originalMethod" => target,
                _ => args[Array.FindIndex(target.GetParameters(), a => a.Name == p.Name)]
            }).ToArray();
            object? output;
            try { output = hook.Method.Invoke(null, inputs); }
            catch (TargetInvocationException failure) { ExceptionDispatchInfo.Capture(failure.InnerException!).Throw(); throw; }
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Name == "__state") state = inputs[i];
                if (parameters[i].Name == "__result") result = inputs[i];
                if (parameters[i].ParameterType.IsByRef && !parameters[i].Name!.StartsWith("__"))
                    args[Array.FindIndex(target.GetParameters(), p => p.Name == parameters[i].Name)] = inputs[i];
            }
            return output;
        }
    }
    public static void Call(Type type, string method, Type[] types, object? instance, object?[] args, Action original) =>
        Call(type, method, types, instance, args, () => { original(); return 0; });
}
namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public struct Vector2 { public float x,y; }
    public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; } }
    public struct Bounds { public Vector3 min,max; public Bounds(Vector3 center,Vector3 size){ min=new(center.x-size.x/2,center.y-size.y/2,center.z-size.z/2); max=new(center.x+size.x/2,center.y+size.y/2,center.z+size.z/2); } public Vector3 size=>new(max.x-min.x,max.y-min.y,max.z-min.z); public void Encapsulate(Vector3 p) { min=new(Math.Min(min.x,p.x),Math.Min(min.y,p.y),Math.Min(min.z,p.z)); max=new(Math.Max(max.x,p.x),Math.Max(max.y,p.y),Math.Max(max.z,p.z)); } }
    public struct Vector3 { public static Vector3 zero => new(0,0,0); public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public class GameObject : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        public bool Destroyed;
        public ScriptListItem? Row;
        public GameObject() => transform = new(this);
        public void SetActive(bool active) => activeInHierarchy = active;
        public T GetComponent<T>() where T : class => Row as T;
    }
    public class Transform : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public readonly GameObject gameObject;
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public Vector3 localPosition;
        public Vector3 TransformPoint(Vector3 p) { p = new(p.x+localPosition.x,p.y+localPosition.y,p.z+localPosition.z); return parent == null ? p : parent.TransformPoint(p); }
        public Vector3 InverseTransformPoint(Vector3 p) { if(parent != null)p=parent.InverseTransformPoint(p); return new(p.x-localPosition.x,p.y-localPosition.y,p.z-localPosition.z); }
        public int childCount => Children.Count;
        public Transform(GameObject owner) => gameObject = owner;
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public void SetParent(Transform? value, bool worldPositionStays) { parent?.Children.Remove(this); parent = value; value?.Children.Add(this); }
        public void SetAsLastSibling() { HostStats.Fail("SetAsLastSibling"); if (parent == null) return; parent.Children.Remove(this); parent.Children.Add(this); }
        public void SetSiblingIndex(int index) { HostStats.Fail("SetSiblingIndex"); HostStats.SiblingMoves++; if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), this); }
    }
    public class Behaviour : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public bool isActiveAndEnabled = true; public bool enabled = true;
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
    }
}
public class UIWidget : UnityEngine.Behaviour {
 public enum Pivot { TopLeft, Top, TopRight, Center }
 public int width = 600, height = 100; public UIPanel? panel;
 public Vector3[] worldCorners => new[]{transform.TransformPoint(new Vector3(0,0,0)),transform.TransformPoint(new Vector3(width,0,0)),transform.TransformPoint(new Vector3(width,-height,0)),transform.TransformPoint(new Vector3(0,-height,0))};
}
public class UILabel : UIWidget { public string text = ""; }
public class PhoneticText : UnityEngine.Behaviour { public string Text = ""; }
public class UIGrid : UnityEngine.Behaviour
{
    public enum Arrangement { Horizontal,Vertical } public Arrangement arrangement=Arrangement.Vertical; public UIWidget.Pivot pivot=UIWidget.Pivot.TopLeft; public int maxPerLine; public bool inverted; public object? onCustomSort, onReposition;
    public bool animateSmoothly, animateFadeIn;
    public List<object>? mSprings;
    public void Reposition() => Hooks.Call(typeof(UIGrid),nameof(Reposition),Type.EmptyTypes,this,Array.Empty<object?>(), () => { HostStats.Reflows++; for (int i = 0; i < transform.childCount; i++) transform.Children[i].localPosition = new(0, -110 * i, 0); });
}
public static class UICamera { public static bool isDragging; }
public class Script : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public string text = "";
    public int speakerSlotNum;
    public CharacterRecord[] characters = Array.Empty<CharacterRecord>();
    public class CharacterRecord { public string name = ""; }
}
public class NativeList<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    private readonly List<T> _values = new();
    public int Count => _values.Count;
    public T this[int i] { get => _values[i]; set => _values[i] = value; }
    public void Clear() => _values.Clear();
    public void Add(T value) => _values.Add(value);
    public void Insert(int index, T value) => _values.Insert(index, value);
    public void RemoveAt(int index) => _values.RemoveAt(index);
}
namespace Studio.Scripts.Nodes {
 public class Node : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }
 public class ScriptNode : Node { public NativeList<Script> scripts = new(); public string nodeName="script"; public ScriptNodeInspectorInfo inspectorInfo=new(); public class ScriptNodeInspectorInfo { public int lastScriptIndex=-1; } }
}
public static class HostStats
{
    public static readonly List<int> InitIndices = new();
    public static int Created, Removed, Refreshes, Inits, Reflows, Syncs, Selections, Deselections, SiblingMoves, PanelUpdates, ChildSelections, Starts;
    public static string? Failure;
    public static Action? BeforeDestroy, AfterDestroy, BeforeAdd, AfterRefresh, BeforeSyncReturn, DuringStart;
    public static bool InlineInit = true;
    public static void Fail(string point) { if (Failure != point) return; Failure = null; throw new InvalidOperationException("Injected " + point); }
    public static void Reset() { Created = Removed = Refreshes = Inits = Reflows = Syncs = Selections = Deselections = SiblingMoves = PanelUpdates = ChildSelections = Starts = 0; Failure = null; BeforeDestroy = AfterDestroy = BeforeAdd = AfterRefresh = BeforeSyncReturn = DuringStart = null; InlineInit = true; InitIndices.Clear(); Time.frameCount = 0; }
    public static void AssertDense(ScriptNodeInspector host) { if(host.scriptNodeListItemsCache.Count!=host.scriptNode.scripts.Count)throw new Exception("count");for(int i=0;i<host.scriptNodeListItemsCache.Count;i++)if(host.scriptNodeListItemsCache[i]==null||host.scriptNodeListItemsCache[i].index!=i)throw new Exception("sparse native cache"); }
    public static void Once(ref Action? callback) { var current = callback; callback = null; current?.Invoke(); }
}
public static class NGUITools
{
    public static void DestroyChildren(Transform t, int remain) => Hooks.Call(typeof(NGUITools), nameof(DestroyChildren), new[] { typeof(Transform), typeof(int) }, null, new object?[] { t, remain }, () =>
    { foreach (Transform child in t.Children.Skip(remain).ToArray()) Destroy(child.gameObject); });
    public static GameObject AddChild(Transform parent, GameObject prefab) => Hooks.Call(typeof(NGUITools), nameof(AddChild), new[] { typeof(Transform), typeof(GameObject) }, null, new object?[] { parent, prefab }, () =>
    {
        HostStats.Fail("AddChild");
        HostStats.Created++;
        ScriptListItem row = new();
        row.transform.SetParent(parent, false);
        return row.gameObject;
    });
    public static void Destroy(GameObject value)
    {
        HostStats.Fail("Destroy");
        HostStats.Removed++;
        value.SetActive(false); value.transform.SetParent(null, false); value.Destroyed = true;
    }
}
namespace Studio.Scripts
{
    public class Selectable : UnityEngine.Behaviour { public bool selected; }
    public class ScriptListItem : Selectable
    {
        public ScriptNodeInspector inspector = null!;
        public ScriptNode scriptNode = null!;
        public int index;
        // Native ScriptListItem.Init resolves this runtime reference from the
        // prefab's first child. It is not a populated serialized prefab field.
        public UIWidget child = null!;
        private readonly UIWidget _prefabChild = new();
        public PhoneticText scriptPhonetic = new();
        public UILabel nameLabel = new();
        public GameObject? mirage;
        public ScriptListItem() { gameObject.Row = this; _prefabChild.panel = new UIPanel(); gameObject.Row = this; _prefabChild.transform.SetParent(transform,false); }
        public void Init(ScriptNode node, int index, ScriptNodeInspector inspector) => Hooks.Call(typeof(ScriptListItem), nameof(Init), new[] { typeof(ScriptNode), typeof(int), typeof(ScriptNodeInspector) }, this, new object?[] { node, index, inspector }, () =>
        { HostStats.Inits++; HostStats.InitIndices.Add(index); NativeInitAssignments(node, index, inspector); Refresh(); });
        public void NativeInitAssignments(ScriptNode node, int index, ScriptNodeInspector owner) { child = _prefabChild; scriptNode = node; this.index = index; inspector = owner; }
        public void Refresh() => Hooks.Call(typeof(ScriptListItem), nameof(Refresh), Type.EmptyTypes, this, Array.Empty<object?>(), () =>
        {
            HostStats.Fail("Refresh"); HostStats.Refreshes++;
            var value = scriptNode.scripts[index];
            scriptPhonetic.Text = value.text;
            nameLabel.text = value.speakerSlotNum > 0 ? value.characters[value.speakerSlotNum].name : "";
            HostStats.Once(ref HostStats.AfterRefresh);
        });
        public void SetIndexAndSiblingIndex(int index) { transform.SetSiblingIndex(index); this.index = index; }
        // Native Selectable: an already selected row ignores a later click.
        // Muted deselection clears its highlight but deliberately skips owner notification.
        public void Deselect(bool mute=false) { if(!selected)return; HostStats.Deselections++; if(!mute)inspector.OnChildDeselect(this); selected=false; }
        public void Select(bool multi=false) { if(!isActiveAndEnabled||!gameObject.activeInHierarchy||gameObject.Destroyed||selected)return; HostStats.Selections++; if(!multi)inspector.DeselectAllChildren(true); inspector.OnChildSelect(this); selected=true; }
        public void OnDraggerDragStart() => Hooks.Call(typeof(ScriptListItem),nameof(OnDraggerDragStart),Type.EmptyTypes,this,Array.Empty<object?>(),()=> { HostStats.AssertDense(inspector); });
    }
    public class ScriptNodeInspector : UnityEngine.Behaviour
    {
        public CenterableUIScrollView scriptListScroll=new(); public UIInput nodeNameInput=new();
        public ScriptNodeInspector() { scriptList.transform.SetParent(scriptListScroll.transform,false); scriptListScroll.Owner=this; }
        // Verified native Start initializes preview and clears only the owner;
        // it does not walk rows or reset Selectable.selected.
        public void Start()=>Hooks.Call(typeof(ScriptNodeInspector),nameof(Start),Type.EmptyTypes,this,Array.Empty<object?>(),()=>{HostStats.Starts++; PreviewInitialized=true; selectedScriptItem=null; PaneScript=null; PreviewScript=null; HostStats.Once(ref HostStats.DuringStart); HostStats.Fail("Start");});
        public bool PreviewInitialized;
        public Script? PaneScript, PreviewScript;
        public bool LastSelectionLoading;
        public void DeselectAllChildren(bool mute=false) { selectedScriptItem?.Deselect(mute); }
        public void OnChildDeselect(Selectable child) { selectedScriptItem=null; PaneScript=null; PreviewScript=null; }
        public void OnChildSelect(Selectable child) { HostStats.ChildSelections++; LastSelectionLoading=loading; var row=(ScriptListItem)child; selectedScriptItem=row; scriptNode.inspectorInfo.lastScriptIndex=row.index; PaneScript=scriptNode.scripts[row.index]; HostStats.Fail("OnChildSelect"); PreviewScript=PaneScript; }
        public void RestoreStatus() => Hooks.Call(typeof(ScriptNodeInspector),nameof(RestoreStatus),Type.EmptyTypes,this,Array.Empty<object?>(),()=> { int i=scriptNode.inspectorInfo.lastScriptIndex;if(i>=0)scriptNodeListItemsCache[i].Select(); });
        public void RestoreStatus(ScriptNode.ScriptNodeInspectorInfo info) => Hooks.Call(typeof(ScriptNodeInspector),nameof(RestoreStatus),new[]{typeof(ScriptNode.ScriptNodeInspectorInfo)},this,new object?[]{info},()=> { if(info.lastScriptIndex>=0)scriptNodeListItemsCache[info.lastScriptIndex].Select(); });
        // Verified Load100689939 reads lastScriptIndex again AFTER Sync.
        public void Load(Node node,bool silent=false) => Hooks.Call(typeof(ScriptNodeInspector),nameof(Load),new[]{typeof(Node),typeof(bool)},this,new object?[]{node,silent},()=> { bool wasLoading=loading;loading=true;try{scriptNode=(ScriptNode)node;SyncScriptList(false,false,scriptNode.inspectorInfo.lastScriptIndex<0);int saved=scriptNode.inspectorInfo.lastScriptIndex;if(saved>=0)scriptNodeListItemsCache[saved].Select();}finally{loading=wasLoading;} });
        // Verified Unload100689942 never iterates the dialogue cache.
        public void Unload(bool silent=false) => Hooks.Call(typeof(ScriptNodeInspector),nameof(Unload),new[]{typeof(bool)},this,new object?[]{silent},()=>{unloading=true;selectedScriptItem?.Deselect();isActiveAndEnabled=false;unloading=false;});
        public void RearrangeScriptList() => NativeScope(nameof(RearrangeScriptList));
        public void ValidateNewArrangement() => NativeScope(nameof(ValidateNewArrangement));
        public void UpdateScriptListCache() => NativeScope(nameof(UpdateScriptListCache));
        public void Method_Private_Void_0() => NativeScope(nameof(Method_Private_Void_0));
        private void NativeScope(string name)=>Hooks.Call(typeof(ScriptNodeInspector),name,Type.EmptyTypes,this,Array.Empty<object?>(),()=>HostStats.AssertDense(this));

        public ScriptNode scriptNode = new();
        public UIGrid scriptList = new();
        public NativeList<ScriptListItem> scriptNodeListItemsCache = new();
        public GameObject scriptListItemPrefab = new();
        public ScriptListItem? selectedScriptItem;
        public bool loading, unloading, rearrangeScheduled;
        public Action? AfterDataChange;
        public void InsertScript() => Hooks.Call(typeof(ScriptNodeInspector), nameof(InsertScript), Type.EmptyTypes, this, Array.Empty<object?>(), () => InsertScript(selectedScriptItem == null ? scriptNode.scripts.Count : selectedScriptItem.index + 1));
        public void InsertScript(int index) => Hooks.Call(typeof(ScriptNodeInspector), nameof(InsertScript), new[] { typeof(int) }, this, new object?[] { index }, () =>
        {
            scriptNode.scripts.Insert(index, new Script { text = "new" });
            var callback = AfterDataChange; AfterDataChange = null; callback?.Invoke();
            SyncScriptList(false, false, false);
            scriptNodeListItemsCache[index].Select();
        });
        public void DeleteScript() => Hooks.Call(typeof(ScriptNodeInspector), nameof(DeleteScript), Type.EmptyTypes, this, Array.Empty<object?>(), () =>
        {
            if (selectedScriptItem == null) return;
            int index = selectedScriptItem.index;
            selectedScriptItem.Deselect();
            scriptNode.scripts.RemoveAt(index);
            var callback = AfterDataChange; AfterDataChange = null; callback?.Invoke();
            SyncScriptList(true, false, false);
            if (index == 0) selectedScriptItem = null; else scriptNodeListItemsCache[index - 1].Select();
        });
        public void SyncScriptList(bool mute, bool keepSelected, bool firstOnTop)
        {
          object?[] inputs = { mute, keepSelected, firstOnTop };
          Hooks.Call(typeof(ScriptNodeInspector), nameof(SyncScriptList), new[] { typeof(bool), typeof(bool), typeof(bool) }, this, inputs, () =>
        {
            HostStats.Syncs++;
            int selectedIndex = selectedScriptItem?.index ?? -1;
            selectedScriptItem?.Deselect();
            HostStats.Once(ref HostStats.BeforeDestroy);
            NGUITools.DestroyChildren(scriptList.transform, 0);
            scriptNodeListItemsCache.Clear();
            HostStats.Once(ref HostStats.AfterDestroy);
            for (int i = 0; i < scriptNode.scripts.Count; i++)
            {
                HostStats.Once(ref HostStats.BeforeAdd);
                ScriptListItem row = NGUITools.AddChild(scriptList.transform, scriptListItemPrefab).Row!;
                if (HostStats.InlineInit) { row.NativeInitAssignments(scriptNode, i, this); row.Refresh(); }
                else row.Init(scriptNode, i, this);
                scriptNodeListItemsCache.Add(row);
            }
            scriptList.Reposition();
            if (keepSelected && selectedIndex >= 0 && selectedIndex < scriptNodeListItemsCache.Count) scriptNodeListItemsCache[selectedIndex].Select();
            else if ((bool)inputs[2]!) scriptListScroll.FirstOnTop(false);
            HostStats.Once(ref HostStats.BeforeSyncReturn);
          });
        }
    }
}

public class UIInput { public string Value=""; public void Set(string value,bool notify){Value=value;} }
public static class NGUIMath {
 public static Bounds CalculateRelativeWidgetBounds(Transform relativeTo,Transform content,bool considerInactive,bool considerChildren=true) {
  var corners=content.gameObject.Row!.child.worldCorners;Bounds bounds=new(relativeTo.InverseTransformPoint(corners[0]),Vector3.zero);
  for(int i=1;i<corners.Length;i++)bounds.Encapsulate(relativeTo.InverseTransformPoint(corners[i]));return bounds;
 }
}
public class UIPanel : UnityEngine.Behaviour { public bool hasClipping=true; public Vector4 finalClipRegion=new(300,-400,600,800); public void SetDirty()=>HostStats.PanelUpdates++; public void UpdateSelf()=>HostStats.PanelUpdates++; }
public class UIScrollView : UnityEngine.Behaviour {
 public UIScrollView() { panel.gameObject = gameObject; }
 public UIPanel panel=new(); public Bounds mBounds; public bool mCalculatedBounds;
 public virtual Bounds bounds { get { var value=Hooks.Call(typeof(UIScrollView),"get_bounds",Type.EmptyTypes,this,Array.Empty<object?>(),()=>this is Studio.Scripts.CenterableUIScrollView centerable?centerable.NativeSmallBounds:new Bounds(new(0,-400,0),new(600,800,0))); if(!mCalculatedBounds){mBounds=value;mCalculatedBounds=true;} return value; } }
 public void InvalidateBounds(){mCalculatedBounds=false;}
 public void UpdateScrollbars(bool recalculateBounds){if(recalculateBounds){mBounds=bounds;mCalculatedBounds=true;}}
 public void LateUpdate()=>Hooks.Call(typeof(UIScrollView),nameof(LateUpdate),Type.EmptyTypes,this,Array.Empty<object?>(),()=>{});
 // Native token100663903: true skips Transform.localPosition; both phases
 // write the panel clip offset. A camera remains fixed in world coordinates.
 public void SetDragAmount(float x,float y,bool updateScrollbars)=>Hooks.Call(typeof(UIScrollView),nameof(SetDragAmount),new[]{typeof(float),typeof(float),typeof(bool)},this,new object?[]{x,y,updateScrollbars},()=> {
   var c=panel.finalClipRegion; var b=bounds;
   float newY=b.max.y-c.w*.5f-Math.Clamp(y,0f,1f)*(b.size.y-c.w);
   if(!updateScrollbars){var p=transform.localPosition;p.y+=c.y-newY;transform.localPosition=p;}
   c.y=newY;panel.finalClipRegion=c;
   if(updateScrollbars)UpdateScrollbars(false);
 });
 public void DisableSpring() { }
}
namespace Studio.Scripts { public class CenterableUIScrollView: UIScrollView { public ScriptNodeInspector? Owner; public Transform? centeringChild;public int FirstOnTopCalls;public bool LastCenterInstant;public bool ready=true;public readonly List<Action> Deferred=new();public Bounds NativeSmallBounds=>Owner!=null&&Owner.scriptNode.scripts.Count<32?new Bounds(new(0,-(Owner.scriptNode.scripts.Count*110-10)*.5f,0),new(600,Math.Max(0,Owner.scriptNode.scripts.Count*110-10),0)):Owner!=null?new Bounds(new(0,-(Owner.scriptNode.scripts.Count*110-10)*.5f,0),new(600,Math.Max(0,Owner.scriptNode.scripts.Count*110-10),0)):new Bounds(new(0,-400,0),new(600,800,0));public void FirstOnTop(bool instant){FirstOnTopCalls++;} public void CenterOn(Transform target,bool instant=false,bool restrain=true)=>Hooks.Call(typeof(CenterableUIScrollView),nameof(CenterOn),new[]{typeof(Transform),typeof(bool),typeof(bool)},this,new object?[]{target,instant,restrain},()=>NativeCenterOn(target,instant,restrain));public void CenterOn(Transform target,Vector3 panelCenter,bool instant=false)=>Hooks.Call(typeof(CenterableUIScrollView),nameof(CenterOn),new[]{typeof(Transform),typeof(Vector3),typeof(bool)},this,new object?[]{target,panelCenter,instant},()=>NativeCenterOn(target,instant,true));private void NativeCenterOn(Transform target,bool instant,bool restrain){if(!ready){Deferred.Add(()=>CenterOn(target,instant,restrain));return;}centeringChild=target;LastCenterInstant=instant;var clip=panel.finalClipRegion;var b=NativeSmallBounds;if(target.gameObject.Row?.inspector is {} owner&&owner.scriptNode.scripts.Count<32)b=NativeSmallBounds;float min=b.min.y+clip.w*.5f,max=b.max.y-clip.w*.5f;clip.y=min>max?max:Math.Clamp(target.localPosition.y-50,min,max);panel.finalClipRegion=clip;}public void RunDeferred(){foreach(var callback in Deferred.ToArray())callback();Deferred.Clear();} } }
namespace Utils { public static class Util { public static void AdaptWidgetToParent(GameObject go,GameObject parent){} } }
namespace Studio.Scripts.OperationManagement {
 public class ScenarioScriptAddOperation { public static ScriptNodeInspector? Current; public virtual void Undo()=>Run(nameof(Undo));public virtual void Redo()=>Run(nameof(Redo)); protected void Run(string name)=>Hooks.Call(GetType(),name,Type.EmptyTypes,this,Array.Empty<object?>(),()=>HostStats.AssertDense(Current!)); }
 public class ScenarioScriptDeleteOperation: ScenarioScriptAddOperation {}
 public class ScenarioScriptInspectorModifyOperation: ScenarioScriptAddOperation {}
 public class ScriptNodeInspectorStateChangeOperation: ScenarioScriptAddOperation {}
 public class ScriptNodeRearrangeOperation: ScenarioScriptAddOperation {}
}
namespace AzureArchive.Automation {
 public class AuthoringSaveOperation { }
 public class AuthoringEditorSession {
  public ScriptNodeInspector Owner=null!;
  public void Capture()=>ModelOnly(nameof(Capture));
  public void Save()=>ModelOnly(nameof(Save));
  public void BeginSave(bool inputGuard=false)=>ModelOnly(nameof(BeginSave),inputGuard);
  public void CheckSave(AuthoringSaveOperation operation)=>ModelOnly(nameof(CheckSave),operation);
  public void CompleteSave(AuthoringSaveOperation operation,string version)=>ModelOnly(nameof(CompleteSave),operation,version);
  public void ReleaseSave(AuthoringSaveOperation operation)=>ModelOnly(nameof(ReleaseSave),operation);
  public void Apply()=>Hooks.Call(typeof(AuthoringEditorSession),nameof(Apply),Type.EmptyTypes,this,Array.Empty<object?>(),()=>HostStats.AssertDense(Owner));
  public void Restore()=>Hooks.Call(typeof(AuthoringEditorSession),nameof(Restore),Type.EmptyTypes,this,Array.Empty<object?>(),()=>HostStats.AssertDense(Owner));
  private void ModelOnly(string name,params object[] args)=>Hooks.Call(typeof(AuthoringEditorSession),name,args.Select(a=>a.GetType()).ToArray(),this,args,()=>{for(int i=0;i<Owner.scriptNode.scripts.Count;i++)_ = Owner.scriptNode.scripts[i].text;});
 }
}
