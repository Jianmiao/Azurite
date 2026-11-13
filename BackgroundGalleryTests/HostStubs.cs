using System.Reflection;
using System.Runtime.ExceptionServices;

namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        internal MethodInfo Method;
        public HarmonyMethod(Type owner, string name) => Method = AccessTools.Method(owner, name);
    }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null) =>
            parameters == null ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) :
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
    }
    public sealed class Patches { public List<string> Owners = new(); }
    public sealed class Harmony
    {
        private record PatchRecord(string Owner, MethodInfo Prefix, MethodInfo Postfix, MethodInfo Finalizer);
        private static readonly Dictionary<MethodInfo, PatchRecord> Registered = new();
        private readonly string _owner;
        public Harmony(string owner) => _owner = owner;
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null) =>
            Registered[method] = new PatchRecord(_owner, prefix?.Method, postfix?.Method, finalizer?.Method);
        public static Patches GetPatchInfo(MethodInfo method) => Registered.TryGetValue(method, out var p) ? new Patches { Owners = new() { p.Owner } } : null;
        public void UnpatchSelf()
        {
            foreach (var key in Registered.Where(x => x.Value.Owner == _owner).Select(x => x.Key).ToArray()) Registered.Remove(key);
        }
        public static void Invoke(object instance, string name, Action original)
        {
            MethodInfo method = AccessTools.Method(instance.GetType(), name);
            if (!Registered.TryGetValue(method, out var patch)) { original(); return; }
            object state = null;
            Exception error = null;
            try
            {
                bool run = InvokePatch(patch.Prefix, instance, ref state, null) is not false;
                if (run) original();
                InvokePatch(patch.Postfix, instance, ref state, null);
            }
            catch (Exception caught) { error = caught; }
            if (patch.Finalizer != null) error = (Exception)InvokePatch(patch.Finalizer, instance, ref state, error);
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
        private static object InvokePatch(MethodInfo method, object instance, ref object state, Exception error)
        {
            if (method == null) return null;
            var parameters = method.GetParameters();
            object currentState = state;
            var args = parameters.Select(p => p.Name switch
            {
                "__instance" => instance,
                "__exception" => error,
                "__state" => currentState ?? (p.ParameterType.IsByRef ? Activator.CreateInstance(p.ParameterType.GetElementType()) : null),
                _ => throw new NotSupportedException(p.Name)
            }).ToArray();
            object result;
            try { result = method.Invoke(null, args); }
            catch (TargetInvocationException caught) { ExceptionDispatchInfo.Capture(caught.InnerException).Throw(); throw; }
            for (int i = 0; i < parameters.Length; ++i) if (parameters[i].Name == "__state") state = args[i];
            return result;
        }
    }
}

namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public class Texture { }
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; } }
    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
    public struct Matrix4x4 { public Vector3 MultiplyPoint3x4(Vector3 point) => point; }
    public class Transform { public Matrix4x4 worldToLocalMatrix; }
}
public class UITexture
{
    private static long _next;
    public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _next);
    public bool enabled = true;
    public bool InViewport = true;
    public int CornerReads;
    public UnityEngine.Texture mainTexture;
    public bool isVisible => enabled && InViewport;
    public UnityEngine.Vector3[] worldCorners
    {
        get
        {
            ++CornerReads;
            float x = InViewport ? 0 : 2000;
            return new[] { new UnityEngine.Vector3(x, 0), new UnityEngine.Vector3(x + 100, 0), new UnityEngine.Vector3(x + 100, 100), new UnityEngine.Vector3(x, 100) };
        }
    }
}
public class UIPanel { public UnityEngine.Vector4 finalClipRegion = new(0, 0, 1000, 1000); public UnityEngine.Transform transform = new(); }
public class UIScrollView { public UIPanel panel = new(); }

namespace Studio.Scripts.Window.BackgroundExplorer
{
    public class BackgroundExplorer { public UIScrollView scroll = new(); }
    public class BackgroundItem
    {
        private static long _next;
        public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _next);
        public BackgroundExplorer parent = new();
        public UnityEngine.GameObject gameObject = new();
        public UITexture texture = new();
        public bool unloaded = true;
        public int loadGeneration;
        public int NativeClosures, Requests, Releases;
        public bool CompleteSynchronously;
        // Fault injection after native flags/generation change, before the
        // resource provider accepts a callback. This is not measured host cost.
        public Exception LoadFailure;
        public Action AfterAccepted;
        public readonly List<__c__DisplayClass22_0> Callbacks = new();
        // Native AA: Update and UpdateLoadState share RVA 0x813b30.
        public void Update() => HarmonyLib.Harmony.Invoke(this, nameof(Update), UpdateLoadState);
        public void UpdateLoadState()
        {
            if (gameObject.activeInHierarchy && texture.isVisible) LoadBGAsync();
            else UnloadBg();
        }
        public void LoadBGAsync() => HarmonyLib.Harmony.Invoke(this, nameof(LoadBGAsync), () =>
        {
            // Native RVA 0x813740 allocates the callback display class before
            // the unloaded guard at 0x8137d6. Repeated calls are NOT free.
            ++NativeClosures;
            if (!unloaded || !gameObject.activeInHierarchy || texture.mainTexture != null) return;
            unloaded = false;
            ++loadGeneration;
            ++Requests;
            Exception failure = LoadFailure;
            AfterAccepted?.Invoke();
            if (failure != null) throw failure;
            var callback = new __c__DisplayClass22_0 { __4__this = this, generation = loadGeneration };
            Callbacks.Add(callback);
            if (CompleteSynchronously) callback._LoadBGAsync_b__0(new UnityEngine.Texture());
        });
        public void UnloadBg() => HarmonyLib.Harmony.Invoke(this, nameof(UnloadBg), () =>
        {
            if (unloaded) return;
            ++loadGeneration;
            if (texture.mainTexture != null) ++Releases;
            texture.mainTexture = null;
            unloaded = true;
        });
        public class __c__DisplayClass22_0
        {
            public BackgroundItem __4__this;
            public int generation;
            public void _LoadBGAsync_b__0(UnityEngine.Texture tex) => HarmonyLib.Harmony.Invoke(this, nameof(_LoadBGAsync_b__0), () =>
            {
                if (__4__this.unloaded || __4__this.loadGeneration != generation)
                { if (tex != null) ++__4__this.Releases; return; }
                if (tex != null) __4__this.texture.mainTexture = tex;
            });
        }
    }
}
