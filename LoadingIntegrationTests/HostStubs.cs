using System.Reflection;
using NativeEnumerator = Il2CppSystem.Collections.IEnumerator;

namespace Il2CppSystem
{
    public class Object
    {
        private static long next;
        public IntPtr Pointer { get; }
        public Object() => Pointer = new IntPtr(Interlocked.Increment(ref next));
        protected Object(IntPtr pointer) => Pointer = pointer;
        public T TryCast<T>() where T : class => this as T;
        public T Cast<T>() where T : class => this as T ?? throw new InvalidCastException();
    }
    public interface IDisposable { void Dispose(); }
}
namespace Il2CppSystem.Collections
{
    public abstract class IEnumerator : Il2CppSystem.Object
    {
        protected IEnumerator() { }
        protected IEnumerator(IntPtr pointer) : base(pointer) { }
        public abstract Il2CppSystem.Object Current { get; }
        public abstract bool MoveNext();
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public class IEnumerable<T> : Il2CppSystem.Object where T : Il2CppSystem.Object
    {
        protected readonly System.Collections.Generic.List<T> values = new();
        public IEnumerator<T> GetEnumerator() => new(values);
    }
    public class List<T> : IEnumerable<T> where T : Il2CppSystem.Object
    {
        public int Count => values.Count;
        public T this[int index] { get => values[index]; set => values[index] = value; }
        public void Add(T value) => values.Add(value);
        public void Clear() => values.Clear();
    }
    public class IEnumerator<T> : NativeEnumerator, Il2CppSystem.IDisposable where T : Il2CppSystem.Object
    {
        private readonly System.Collections.Generic.List<T> values;
        private int index = -1;
        public int Disposals;
        internal IEnumerator(System.Collections.Generic.List<T> values) => this.values = values;
        public override T Current => values[index];
        public override bool MoveNext() => ++index < values.Count;
        public void Dispose() => Disposals++;
    }
}
namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public class Object : Il2CppSystem.Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed
                : !ReferenceEquals(b, null) && !b.Destroyed && a.Pointer == b.Pointer;
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => other is Object o && this == o;
        public override int GetHashCode() => Pointer.GetHashCode();
    }
    public sealed class GameObject : Object
    {
        public bool activeSelf;
        public bool activeInHierarchy => activeSelf;
        public void SetActive(bool active) => activeSelf = active;
    }
    public sealed class Coroutine : Object
    {
        internal readonly NativeEnumerator Enumerator;
        internal readonly Stack<NativeEnumerator> Stack = new();
        internal bool Done;
        internal Coroutine(NativeEnumerator e) { Enumerator = e; Stack.Push(e); }
        internal void Advance()
        {
            // Model Unity's nested-IEnumerator dispatch. A self-yielding native
            // polling object is a wait for a future frame, not a child coroutine.
            int steps = 0;
            while (Stack.Count > 0)
            {
                if (++steps > 256) throw new InvalidOperationException("test coroutine scheduler exceeded bounded same-frame work");
                var current = Stack.Peek();
                if (!current.MoveNext()) { Stack.Pop(); continue; }
                var child = current.Current as NativeEnumerator;
                if (child == null || Stack.Any(e => e.Pointer == child.Pointer)) return;
                Stack.Push(child);
            }
            Done = true;
        }
    }
    public class MonoBehaviour : Object
    {
        public readonly GameObject gameObject = new();
        public bool isActiveAndEnabled = true;
        public readonly List<Coroutine> Running = new();
        public Coroutine StartCoroutine(NativeEnumerator e)
        {
            var c = new Coroutine(e);
            Running.Add(c);
            c.Advance(); // Unity runs through the first yield immediately.
            return c;
        }
        public void StopCoroutine(Coroutine c) { c.Done = true; Running.Remove(c); }
        public void Tick()
        {
            foreach (var c in Running.ToArray()) if (!c.Done) c.Advance();
            Running.RemoveAll(c => c.Done);
        }
    }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    public static class Extensions
    {
        // The real stock wrapper has no native IDisposable. This is intentional.
        private sealed class Wrapped : NativeEnumerator
        {
            private readonly System.Collections.IEnumerator inner;
            internal Wrapped(System.Collections.IEnumerator e) => inner = e;
            public override Il2CppSystem.Object Current => inner.Current as Il2CppSystem.Object;
            public override bool MoveNext() => inner.MoveNext();
        }
        public static NativeEnumerator WrapToIl2Cpp(this System.Collections.IEnumerator e) => new Wrapped(e);
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public readonly MethodInfo method;
        public HarmonyMethod(Type type, string name) => method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(type.FullName, name);
    }
    public sealed class Patches { public readonly List<string> Owners = new(); }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, parameters ?? Type.EmptyTypes, null);
    }
    public sealed class Harmony
    {
        internal sealed record Hook(string Owner, MethodInfo Prefix, MethodInfo Postfix);
        internal static readonly Dictionary<MethodInfo, Hook> Hooks = new();
        internal static readonly List<MethodInfo> Attempts = new();
        internal static HostMetadata Metadata;
        private readonly string id;
        public Harmony(string id) => this.id = id;
        public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            Metadata.Verify(original);
            Attempts.Add(original);
            if (Hooks.TryGetValue(original, out var existing) && existing.Owner == id)
                Hooks[original] = new Hook(id, prefix?.method ?? existing.Prefix, postfix?.method ?? existing.Postfix);
            else
                Hooks[original] = new Hook(id, prefix?.method, postfix?.method);
        }
        public static Patches GetPatchInfo(MethodInfo method)
        {
            if (!Hooks.TryGetValue(method, out var hook)) return null;
            var patches = new Patches(); patches.Owners.Add(hook.Owner); return patches;
        }
        public void UnpatchSelf() { foreach (var entry in Hooks.Where(p => p.Value.Owner == id).ToArray()) Hooks.Remove(entry.Key); }
        public static object Invoke(object instance, MethodInfo target, object[] args, Func<object> original)
        {
            if (!Hooks.TryGetValue(target, out var hook)) return original();
            object result = target.ReturnType == typeof(bool) ? false : null;
            if (hook.Prefix == null || InvokeHook(hook.Prefix, instance, args, ref result) is not false) result = original();
            if (hook.Postfix != null) InvokeHook(hook.Postfix, instance, args, ref result);
            return result;
        }
        private static object InvokeHook(MethodInfo method, object instance, object[] args, ref object result)
        {
            var p = method.GetParameters();
            object incomingResult = result;
            var values = p.Select(x => x.Name == "__instance" ? instance : x.Name == "__result" ? incomingResult : x.Name?.StartsWith("__") == true && int.TryParse(x.Name[2..], out int n) ? args[n] : throw new InvalidOperationException("Unknown Harmony parameter " + x.Name)).ToArray();
            object answer;
            try { answer = method.Invoke(null, values); }
            catch (TargetInvocationException e) { throw e.InnerException; }
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i].Name == "__result") result = values[i];
                if (p[i].ParameterType.IsByRef && p[i].Name?.StartsWith("__") == true && int.TryParse(p[i].Name[2..], out int index))
                    args[index] = values[i];
            }
            return answer;
        }
    }
}
public class Singleton<T> where T : class { public static T Instance; }
public sealed class Loading : UnityEngine.MonoBehaviour
{
    public static readonly HashSet<string> LoadingTags = new();
    public int loadingCount;
    public int Resets;
    // Matches native Loading: shared tags, scene-local visibility, and a count
    // which is incremented by Start but not decremented by Stop.
    public void Awake() { Singleton<Loading>.Instance = this; gameObject.SetActive(LoadingTags.Count > 0); }
    public void StartLoading(string tag) { LoadingTags.Add(tag); loadingCount++; gameObject.SetActive(true); }
    public void StopLoading(string tag)
    {
        if (Destroyed) throw new InvalidOperationException("access to destroyed loading overlay");
        if (LoadingTags.Remove(tag) && LoadingTags.Count == 0) gameObject.SetActive(false);
    }
    public void ResetLoading() { Resets++; loadingCount = 0; gameObject.SetActive(false); }
}
public class ProjectData : Il2CppSystem.Object
{
    public Il2CppSystem.Collections.Generic.List<NodeData> nodes { get; set; } = new();
}
public class NodeData : Il2CppSystem.Object { }
public class ScriptNodeData : NodeData
{
    public Il2CppSystem.Collections.Generic.List<ScriptData> Scripts { get; set; } = new();
}
public class ProjectFileRead : Il2CppSystem.Object { }
public class NotificationManager { public void Notify(string message) { } }
public class Script : Il2CppSystem.Object { public class CharacterRecord : Il2CppSystem.Object { } }
public static class CatalogFileInfo
{
    public class _CoLoadProject_d__17 : Il2CppSystem.Object
    {
        public int __1__state;
        public Il2CppSystem.Object __2__current;
        public bool MoveNext() => true;
    }
    public class _CoLoadSave_d__19 : Il2CppSystem.Object
    {
        public int __1__state;
        public Il2CppSystem.Object __2__current;
        public bool MoveNext() => true;
    }
}
public class ScenarioResourceManager
{
    public static ScenarioResourceManager Instance => Singleton<ScenarioResourceManager>.Instance;
    public NativeEnumerator Next;
    public NativeEnumerator CoTryPreloadBackgroundTexture(uint id) => (NativeEnumerator)HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), new object[] { id }, () => Next ?? new EngineOwnedResource(new ScriptData { bgName = id }));
    public NativeEnumerator CoTryPreloadVoice(string id) => Next ?? new EngineOwnedResource(new ScriptData { voice = id });
    public NativeEnumerator CoTryPreloadPopupImage(string id) => Next ?? new EngineOwnedResource(new ScriptData { popup = id });
    public NativeEnumerator CoTryPreloadBGMClip(long id) => Next ?? new EngineOwnedResource(new ScriptData { bgmId = id });
    public NativeEnumerator CoTryPreloadSound(string id) => Next ?? new EngineOwnedResource(new ScriptData { sound = id });
}
public class CharacterManager
{
    public static CharacterManager Instance => Singleton<CharacterManager>.Instance;
    public NativeEnumerator CoTryPreloadSpine(string id)
    {
        var characters = new Il2CppSystem.Collections.Generic.List<ScriptData.CharacterRecordData>();
        characters.Add(new ScriptData.CharacterRecordData { name = id });
        return new EngineOwnedResource(new ScriptData { characters = characters });
    }
}
namespace AzureArchive.Automation
{
    public sealed class AuthoringEditorSession
    {
        public static AuthoringEditorSession Current { get; private set; }
        public int RefreshCalls, CatalogVersionCalls;
        public static ProjectFileRead PrepareOpen(string path, bool migrate) => null;
        public static void Begin(Studio.Scripts.StudioCommon studio, ProjectData data, string path)
        {
            Current = new AuthoringEditorSession();
            var refresh = typeof(AuthoringEditorSession).GetMethod(nameof(Refresh));
            var refreshArgs = new object[] { true };
            HarmonyLib.Harmony.Invoke(Current, refresh, refreshArgs, () => { Current.Refresh((bool)refreshArgs[0]); return null; });
        }
        public void Refresh(bool checkResources)
        {
            var args = new object[] { checkResources };
            HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), args, () =>
            {
                RefreshCalls++;
                if ((bool)args[0]) CatalogVersionCalls++;
                return null;
            });
        }
        public void BeginSave(bool inputGuard = false) => Refresh(true);
    }
    public class AuthoringWorkbench
    {
        public sealed class _Load_d__47 : Il2CppSystem.Object
        {
            public int __1__state { get; set; }
            public Il2CppSystem.Object __2__current { get; set; }
            public ProjectData project { get; set; }
            public bool StepResult = true;
            public bool MoveNext() => (bool)HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () => StepResult);
        }
    }
}
namespace Studio.Scripts
{
    // The native inspector owns an embedded Test preview component. The
    // production controller only needs its Unity activity state here; keeping
    // that seam in the fixture exercises the preview readiness guard.
    public class Test : UnityEngine.MonoBehaviour { }
    public class StudioCommon : UnityEngine.MonoBehaviour
    {
        public void Start() => HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () => { AzureArchive.Automation.AuthoringEditorSession.Begin(this, null, "test"); return null; });
        public void OnChangesMade(bool major)
        {
            var args = new object[] { major };
            HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), args, () =>
            {
                var current = AzureArchive.Automation.AuthoringEditorSession.Current;
                if (current != null)
                {
                    var refresh = typeof(AzureArchive.Automation.AuthoringEditorSession).GetMethod(nameof(AzureArchive.Automation.AuthoringEditorSession.Refresh));
                    var refreshArgs = new object[] { true };
                    HarmonyLib.Harmony.Invoke(current, refresh, refreshArgs, () => { current.Refresh((bool)refreshArgs[0]); return null; });
                }
                return null;
            });
        }
        public void Load(ProjectData data) => HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), new object[] { data }, () => null);
        public void OnDestroy() => HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () => null);
    }
    public class ScriptNodeInspector : UnityEngine.MonoBehaviour
    {
        public int Played;
        public int ResourceOperations;
        public bool loading;
        public bool unloading;
        public Test preview = new();
        public ScriptListItem selectedScriptItem = new();
        // Small native-call-shape fixture. Test* members are not AA API
        // declarations or a replacement for its UI/data/history implementation.
        public Nodes.ScriptNode TestNode;
        public string TestInput;
        public readonly List<string> TestActions = new();
        public void Load(Nodes.Node node, bool silent = false) => ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), new object[] { node, silent }, () =>
        {
            loading = true;
            try
            {
                TestNode = (Nodes.ScriptNode)node;
                TestActions.Add("load");
                SyncScriptList(false, false, true);
                if (TestNode.TestDialogue.Count > 0) OnChildSelect(new ScriptListItem { TestIndex = 0 });
            }
            finally { loading = false; }
        });
        public void SyncScriptList(bool mute, bool keepSelected, bool firstOnTop) =>
            ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), new object[] { mute, keepSelected, firstOnTop }, () => TestActions.Add("list"));
        public void OnChildSelect(Selectable item) => ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), new object[] { item }, () =>
        {
            selectedScriptItem = (ScriptListItem)item;
            TestActions.Add("select"); SyncAll(); PlayPreview();
        });
        public void SyncAll() => ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () =>
        {
            // Native SyncAll writes text before its resource sync calls.
            TestInput = TestNode.TestDialogue[selectedScriptItem.TestIndex];
            TestActions.Add("text-visible");
            SyncSlots(); SyncEnvironmentProperties(); SyncOrClearSlotProperties(false);
        });
        public void ApplyScriptText() => ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () =>
        {
            TestNode.TestDialogue[selectedScriptItem.TestIndex] = TestInput;
            TestActions.Add("text-edited");
        });
        public void InsertScript(int index) => ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), new object[] { index }, () =>
        {
            TestNode.TestDialogue.Insert(index, "");
            TestActions.Add("insert"); SyncScriptList(false, false, false);
            OnChildSelect(new ScriptListItem { TestIndex = index });
        });
        public void DeleteScript() => ActionDispatch((MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () =>
        {
            int index = selectedScriptItem.TestIndex;
            TestNode.TestDialogue.RemoveAt(index);
            TestActions.Add("delete"); SyncScriptList(false, false, false);
            if (TestNode.TestDialogue.Count > 0) OnChildSelect(new ScriptListItem { TestIndex = Math.Max(0, index - 1) });
        });
        private void ActionDispatch(MethodInfo method, object[] args, Action body)
        {
            HarmonyLib.Harmony.Metadata.Verify(method);
            HarmonyLib.Harmony.Invoke(this, method, args, () => { body(); return null; });
        }
        public void PlayPreview() => HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), Array.Empty<object>(), () => { Played++; return null; });
        public void SyncSlots() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        public void SyncOrClearSlotProperties(bool force) => Dispatch((MethodInfo)MethodBase.GetCurrentMethod(), force);
        public void SyncEnvironmentProperties() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        public void SetBGM() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        // Native SetCharacter mutates the model, then synchronizes resource
        // dependent slots/properties. The progressive prefix must allow the
        // model mutation while blocking only those nested resource calls.
        public void SetCharacter(int slot, string name) => HarmonyLib.Harmony.Invoke(this, (MethodInfo)MethodBase.GetCurrentMethod(), new object[] { slot, name }, () =>
        {
            ResourceOperations++;
            SyncSlots(); SyncOrClearSlotProperties(false);
            return null;
        });
        public void OpenCharacterSelector(int slot) => Dispatch((MethodInfo)MethodBase.GetCurrentMethod(), slot);
        public void OpenEmotionSelector(Script.CharacterRecord record, Script script) => Dispatch((MethodInfo)MethodBase.GetCurrentMethod(), record, script);
        public void OpenBackgroundSelector() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        public void OpenPopupImageSelector() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        public void OpenBGMSelector() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        public void OpenSoundSelector() => Dispatch((MethodInfo)MethodBase.GetCurrentMethod());
        private void Dispatch(MethodInfo method, params object[] args) => HarmonyLib.Harmony.Invoke(this, method, args, () => { ResourceOperations++; return null; });
    }
    public class Selectable : UnityEngine.MonoBehaviour { }
    public class ScriptListItem : Selectable { public int TestIndex; }
}
namespace Studio.Scripts.Nodes
{
    public class Node : UnityEngine.MonoBehaviour { }
    public class ScriptNode : Node { public readonly List<string> TestDialogue = new(); }
}
    public class ScriptData : Il2CppSystem.Object
    {
        public Cysharp.Threading.Tasks.UniTask PreloadAssetsAsync(Il2CppSystem.Threading.CancellationToken cancellationToken = null)
        {
            var scripts = new Il2CppSystem.Collections.Generic.List<ScriptData>(); scripts.Add(this);
            return NativeTasks.Start(scripts, cancellationToken);
        }
        public static Cysharp.Threading.Tasks.UniTask PreloadAssetsAsync(
            Il2CppSystem.Collections.Generic.IEnumerable<ScriptData> scripts,
            Il2CppSystem.Threading.CancellationToken cancellationToken)
            => NativeTasks.Start(scripts, cancellationToken);
        public uint bgName { get; set; }
        public string voice { get; set; }
        public string popup { get; set; }
        public long bgmId { get; set; }
        public string sound { get; set; }
        public Il2CppSystem.Collections.Generic.List<CharacterRecordData> characters { get; set; }
        public sealed class CharacterRecordData : Il2CppSystem.Object { public string name { get; set; } }
        public sealed class _CoPreloadAssets_d__74 : NativeEnumerator, Il2CppSystem.IDisposable
        {
            public Il2CppSystem.Collections.Generic.IEnumerable<ScriptData> scripts { get; set; } = new Il2CppSystem.Collections.Generic.List<ScriptData>();
            public int Moves;
            public int Disposals;
            public int Remaining = 4;
            public bool Throw;
            public Func<NativeEnumerator> FirstChild;
            private Il2CppSystem.Object current;
            public override Il2CppSystem.Object Current => current;
            public override bool MoveNext()
            {
                Moves++;
                if (Throw) throw new InvalidOperationException("asset fixture failure");
                current = Moves == 1 ? FirstChild?.Invoke() : null;
                return Remaining-- > 0;
            }
            public void Dispose() => Disposals++;
            public void System_IDisposable_Dispose() => Dispose();
        }
    }
internal sealed class NativeResource : NativeEnumerator, Il2CppSystem.IDisposable
{
    public int Moves;
    public int Disposals;
    public int Remaining = 4;
    public override Il2CppSystem.Object Current => null;
    public override bool MoveNext() { Moves++; return Remaining-- > 0; }
    public void Dispose() => Disposals++;
}

// Native Addressables-style waits can return themselves as Current while
// completion is driven by subsequent PlayerLoop frames. The bounded watchdog
// turns a production same-frame spin into a deterministic assertion failure.
internal sealed class NativeSelfYieldWait : NativeEnumerator, Il2CppSystem.IDisposable
{
    private sealed class Alias : NativeEnumerator, Il2CppSystem.IDisposable
    {
        private readonly NativeSelfYieldWait owner;
        internal Alias(NativeSelfYieldWait owner) : base(owner.Pointer) => this.owner = owner;
        public override Il2CppSystem.Object Current => owner.Current;
        public override bool MoveNext() => owner.MoveNext();
        public void Dispose() => owner.Dispose();
    }
    private readonly Il2CppSystem.Object _current;
    private int _startedFrame = -1;
    private int _lastFrame = -1;
    private int _callsInFrame;
    public int MaximumCallsInOneFrame;
    public int Disposals;
    public bool Completed;
    public NativeSelfYieldWait(bool aliasCurrent = false) => _current = aliasCurrent ? new Alias(this) : this;
    public override Il2CppSystem.Object Current => _current;
    public override bool MoveNext()
    {
        int frame = UnityEngine.Time.frameCount;
        if (_startedFrame < 0) _startedFrame = frame;
        if (_lastFrame != frame) { _lastFrame = frame; _callsInFrame = 0; }
        MaximumCallsInOneFrame = Math.Max(MaximumCallsInOneFrame, ++_callsInFrame);
        if (_callsInFrame > 4)
            throw new InvalidOperationException("bounded-self-yield-watchdog: native wait repeatedly polled without Time.frameCount advancing");
        Completed = frame - _startedFrame >= 3;
        return !Completed;
    }
    public void Dispose() => Disposals++;
}
