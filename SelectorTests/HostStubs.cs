using System.Reflection;
using System.Runtime.ExceptionServices;

// Native bodies model only the verified initialization/selection call order.
// They do not model Unity rendering, asset cost, or IL2CPP detour generation.
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo? Method(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
    }
    public sealed class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(Type type, string name) => Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    }
    public sealed class PatchInfo { public HashSet<string> Owners = new(); }
    public sealed class Harmony
    {
        internal static readonly Dictionary<MethodInfo, (string Owner, HarmonyMethod? Prefix, HarmonyMethod? Postfix, HarmonyMethod? Finalizer)> Patches = new();
        private readonly string _owner;
        public Harmony(string owner) => _owner = owner;
        public void Patch(MethodInfo method, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, object? transpiler = null, HarmonyMethod? finalizer = null) =>
            Patches[method] = (_owner, prefix, postfix, finalizer);
        public static PatchInfo? GetPatchInfo(MethodInfo method) => Patches.TryGetValue(method, out var p) ? new() { Owners = new() { p.Owner } } : null;
        public void UnpatchSelf() { foreach (var key in Patches.Where(p => p.Value.Owner == _owner).Select(p => p.Key).ToArray()) Patches.Remove(key); }
    }
}
internal static class Hooks
{
    internal static void Call(Type type, string name, Type[] parameters, object instance, Action body)
    {
        MethodInfo target = HarmonyLib.AccessTools.Method(type, name, parameters)!;
        if (!HarmonyLib.Harmony.Patches.TryGetValue(target, out var hooks)) { body(); return; }
        object? state = null;
        Exception? error = null;
        try { if (Invoke(hooks.Prefix) is not false) body(); Invoke(hooks.Postfix); }
        catch (Exception ex) { error = ex; }
        if (hooks.Finalizer != null) error = (Exception?)Invoke(hooks.Finalizer);
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        object? Invoke(HarmonyLib.HarmonyMethod? hook)
        {
            if (hook == null) return null;
            var ps = hook.Method.GetParameters();
            var values = ps.Select(p => p.Name switch
            {
                "__instance" => instance,
                "__exception" => error,
                "__state" => state ?? ((p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType).IsValueType ? Activator.CreateInstance(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) : null),
                _ => throw new NotSupportedException(p.Name)
            }).ToArray();
            object? result;
            try { result = hook.Method.Invoke(null, values); }
            catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
            for (int i = 0; i < ps.Length; i++) if (ps[i].Name == "__state") state = values[i];
            return result;
        }
    }
}
namespace UnityEngine
{
    public class GameObject
    {
        public Scene scene = new("ScenarioEditorScene", 1);
        public bool activeSelf = true;
        public void SetActive(bool value) => activeSelf = value;
    }
    public readonly record struct Scene(string name, int handle);
    public class MonoBehaviour
    {
        private static long _nextPointer;
        public IntPtr Pointer { get; } = new(++_nextPointer);
        public GameObject gameObject = new();
    }
}
public class Singleton<T> { public static T? Instance; }
public class Script { }
namespace Il2CppSystem { public class Action<T1,T2> { } }
namespace Studio.Scripts.Nodes { public class ScriptNode { } }
namespace Studio.Scripts
{
    public class StudioCommon : UnityEngine.MonoBehaviour
    {
        public void OnDestroy() => Hooks.Call(GetType(), nameof(OnDestroy), Type.EmptyTypes, this, () => { });
    }
    public class ScriptNodeInspector : UnityEngine.MonoBehaviour
    {
        public int OpenCalls;
        public void OpenCharacterSelector(int slotNum) => Hooks.Call(GetType(), nameof(OpenCharacterSelector), new[] { typeof(int) }, this, () =>
        {
            OpenCalls++;
            var view = Singleton<Window.CharacterExplorer.CharacterExplorer>.Instance!;
            view.Init(new Script(), slotNum, false); view.Show();
        });
    }
}
namespace Studio.Scripts.Window.BackgroundExplorer
{
    public class BackgroundExplorer : UnityEngine.MonoBehaviour
    {
        public int StartCalls, InitCalls, ShowCalls;
        public bool FailNext;
        public void Start() => Hooks.Call(GetType(), nameof(Start), Type.EmptyTypes, this, () =>
        { StartCalls++; if (FailNext) { FailNext = false; throw new InvalidOperationException("native failure"); } gameObject.SetActive(false); });
        public void Init(Script script, Il2CppSystem.Action<uint,string> callback) => Hooks.Call(GetType(), nameof(Init), new[] { typeof(Script), typeof(Il2CppSystem.Action<uint,string>) }, this, () => InitCalls++);
        public void Show() => Hooks.Call(GetType(), nameof(Show), Type.EmptyTypes, this, () => { ShowCalls++; gameObject.SetActive(true); });
    }
}
namespace Studio.Scripts.Window.PopupImageExplorer
{
    public class PopupImageExplorer : UnityEngine.MonoBehaviour
    {
        public int StartCalls, InitCalls;
        public void Start() => Hooks.Call(GetType(), nameof(Start), Type.EmptyTypes, this, () => { StartCalls++; gameObject.SetActive(false); });
        public void Init(Nodes.ScriptNode node, Script script) => Hooks.Call(GetType(), nameof(Init), new[] { typeof(Nodes.ScriptNode), typeof(Script) }, this, () => InitCalls++);
    }
}
namespace Studio.Scripts.Window.CharacterExplorer
{
    public class CharacterList : UnityEngine.MonoBehaviour
    {
        public int LoadCalls, Rows;
        public bool FailNext;
        public void LoadCharacters() => Hooks.Call(GetType(), nameof(LoadCharacters), Type.EmptyTypes, this, () =>
        { LoadCalls++; Rows = 0; if (FailNext) { FailNext = false; throw new InvalidOperationException("native list failure"); } Rows = 1000; });
    }
    public class CharacterExplorer : UnityEngine.MonoBehaviour
    {
        public CharacterList characterList = new();
        public int AwakeCalls, StartCalls, InitCalls, ShowCalls;
        public bool FailAwake;
        public Action? DuringAwake;
        public void Awake() => Hooks.Call(GetType(), nameof(Awake), Type.EmptyTypes, this, () =>
        {
            AwakeCalls++; Singleton<CharacterExplorer>.Instance = this;
            gameObject.SetActive(false); DuringAwake?.Invoke();
            characterList.LoadCharacters(); if (FailAwake) throw new InvalidOperationException("awake failure");
        });
        public void Start() { StartCalls++; }
        public void Init(Script script, int slotNum, bool spineless) => Hooks.Call(GetType(), nameof(Init), new[] { typeof(Script), typeof(int), typeof(bool) }, this, () =>
        { if (characterList.Rows == 0) throw new InvalidOperationException("native selection observed unready list"); InitCalls++; });
        public void Show() { ShowCalls++; gameObject.SetActive(true); }
    }
}
