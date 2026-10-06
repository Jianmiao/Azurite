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
        public MethodInfo Method;
        public HarmonyMethod(Type type, string name) => Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    }
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
            }
            return output;
        }
    }
    public static void Call(Type type, string method, Type[] types, object? instance, object?[] args, Action original) =>
        Call(type, method, types, instance, args, () => { original(); return 0; });
}
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public class GameObject : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        public bool Destroyed;
        public ScriptListItem? Row;
        public GameObject() => transform = new(this);
        public void SetActive(bool active) => activeInHierarchy = active;
    }
    public class Transform : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public readonly GameObject gameObject;
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public Vector3 localPosition;
        public int childCount => Children.Count;
        public Transform(GameObject owner) => gameObject = owner;
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public void SetParent(Transform? value, bool worldPositionStays) { parent?.Children.Remove(this); parent = value; value?.Children.Add(this); }
        public void SetAsLastSibling() { HostStats.Fail("SetAsLastSibling"); if (parent == null) return; parent.Children.Remove(this); parent.Children.Add(this); }
        public void SetSiblingIndex(int index) { HostStats.Fail("SetSiblingIndex"); HostStats.SiblingMoves++; if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), this); }
    }
    public class Behaviour : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public bool isActiveAndEnabled = true;
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
    }
}
public class UIWidget : UnityEngine.Behaviour { public int width = 600, height = 100; }
public class UILabel : UIWidget { public string text = ""; }
public class PhoneticText : UnityEngine.Behaviour { public string Text = ""; }
public class UIGrid : UnityEngine.Behaviour
{
    public bool animateSmoothly, animateFadeIn;
    public List<object>? mSprings;
    public void Reposition() { HostStats.Reflows++; for (int i = 0; i < transform.childCount; i++) transform.Children[i].localPosition = new(0, -110 * i, 0); }
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
namespace Studio.Scripts.Nodes { public class ScriptNode : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { public NativeList<Script> scripts = new(); } }
public static class HostStats
{
    public static int Created, Removed, Refreshes, Inits, Reflows, Syncs, Selections, Deselections, SiblingMoves;
    public static string? Failure;
    public static Action? BeforeDestroy, AfterDestroy, BeforeAdd, AfterRefresh, BeforeSyncReturn;
    public static bool InlineInit = true;
    public static void Fail(string point) { if (Failure != point) return; Failure = null; throw new InvalidOperationException("Injected " + point); }
    public static void Reset() { Created = Removed = Refreshes = Inits = Reflows = Syncs = Selections = Deselections = SiblingMoves = 0; Failure = null; BeforeDestroy = AfterDestroy = BeforeAdd = AfterRefresh = BeforeSyncReturn = null; InlineInit = true; }
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
    public class ScriptListItem : UnityEngine.Behaviour
    {
        public ScriptNodeInspector inspector = null!;
        public ScriptNode scriptNode = null!;
        public int index;
        public bool selected;
        public UIWidget child = new();
        public PhoneticText scriptPhonetic = new();
        public UILabel nameLabel = new();
        public GameObject? mirage;
        public ScriptListItem() => gameObject.Row = this;
        public void Init(ScriptNode node, int index, ScriptNodeInspector inspector) => Hooks.Call(typeof(ScriptListItem), nameof(Init), new[] { typeof(ScriptNode), typeof(int), typeof(ScriptNodeInspector) }, this, new object?[] { node, index, inspector }, () =>
        { HostStats.Inits++; NativeInitAssignments(node, index, inspector); Refresh(); });
        public void NativeInitAssignments(ScriptNode node, int index, ScriptNodeInspector owner) { scriptNode = node; this.index = index; inspector = owner; }
        public void Refresh() => Hooks.Call(typeof(ScriptListItem), nameof(Refresh), Type.EmptyTypes, this, Array.Empty<object?>(), () =>
        {
            HostStats.Fail("Refresh"); HostStats.Refreshes++;
            var value = scriptNode.scripts[index];
            scriptPhonetic.Text = value.text;
            nameLabel.text = value.speakerSlotNum > 0 ? value.characters[value.speakerSlotNum].name : "";
            HostStats.Once(ref HostStats.AfterRefresh);
        });
        public void SetIndexAndSiblingIndex(int index) { transform.SetSiblingIndex(index); this.index = index; }
        public void Deselect() { HostStats.Deselections++; selected = false; }
        public void Select() { HostStats.Selections++; inspector.selectedScriptItem?.Deselect(); inspector.selectedScriptItem = this; selected = true; }
    }
    public class ScriptNodeInspector : UnityEngine.Behaviour
    {
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
        public void SyncScriptList(bool mute, bool keepSelected, bool firstOnTop) => Hooks.Call(typeof(ScriptNodeInspector), nameof(SyncScriptList), new[] { typeof(bool), typeof(bool), typeof(bool) }, this, new object?[] { mute, keepSelected, firstOnTop }, () =>
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
            HostStats.Once(ref HostStats.BeforeSyncReturn);
        });
    }
}
