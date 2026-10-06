using Azurite;
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var preview = new Test();
Check(!CharacterActivity.IsDynamic(preview), "resident editor controller without preview characters is not synthetic work");
preview.previewMode = true;
Check(CharacterActivity.IsDynamic(preview), "unknown embedded character slots remain protected");
var character = new Character(); preview.slots = new(new[] { character });
Check(!CharacterActivity.IsDynamic(preview), "a confirmed static character can reuse the preview");
character.anim = new() { timeScale = 1 };
Check(CharacterActivity.IsDynamic(preview), "Spine idle loops remain protected after scenario animations end");
character.anim.timeScale = 0;
character.isInitialized = true; character.anim.valid = true; character.anim.state = new(); character.anim.skeleton = new();
Check(!CharacterActivity.IsDynamic(preview), "explicitly paused skeleton does not imply ongoing work");
character.anim.timeScale = float.NaN;
Check(CharacterActivity.IsDynamic(preview), "unknown skeleton timing is not treated as static");
character.anim.timeScale = 0; character.blinkTask = new();
Check(CharacterActivity.IsDynamic(preview), "native blink coroutine prevents static caching");
character.blinkTask = null; character.animator = new();
Check(CharacterActivity.IsDynamic(preview), "active Unity animator remains protected");
character.animator.isActiveAndEnabled = false; character.posTweener = new();
Check(CharacterActivity.IsDynamic(preview), "active character positioning remains protected");
character.posTweener.isActiveAndEnabled = false; character.animationList = new() { new() { hasCompleted = true } };
Check(!CharacterActivity.IsDynamic(preview), "completed character animation records can remain resident");
character.animationList.Add(new());
Check(CharacterActivity.IsDynamic(preview), "pending character animation remains protected");
character.animationList[1].isCancelled = true; character.animationQueue = new() { new() };
Check(CharacterActivity.IsDynamic(preview), "queued character work stays active before starting");
character.isActiveAndEnabled = false;
Check(!CharacterActivity.IsDynamic(preview), "inactive character does not keep its old animation queue drawing");
preview.slots = new(new Character[65]);
Check(CharacterActivity.IsDynamic(preview), "unbounded character slot registry remains protected");
preview.slots = new(new[] { new Character { isInitialized = true, anim = new() {
    timeScale = 1, valid = true, state = new(), skeleton = new()
} } });
Check(!CharacterActivity.IsDynamic(preview), "initialized skeleton with no animation tracks or physics must not force full render cadence");
character = preview.slots[0];
var spine = character.anim!;
spine.state!.Tracks = new() { Count = 1, Items = new(new[] { new Spine.TrackEntry() }) };
Check(CharacterActivity.Observe(preview) == CharacterAnimationActivity.Protected, "ambient opt-in is off by default");
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Ambient, "idle track is ambient rather than static or full-rate protected");
preview.previewMode = false;
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "appreciation playback is never ambient optimized");
preview.previewMode = true;
spine.state.Tracks.Items![0].Next = new();
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "queued track transitions retain full cadence");
spine.state.Tracks.Items[0].Next = null;
spine.state.Tracks.Items[0].MixingFrom = new();
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "mixing animation retains full cadence");
spine.state.Tracks = new(); character.blinkTask = new();
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Ambient, "resident blink coroutine remains moving and cannot use a static cache");
spine.skeleton!.PhysicsConstraints!.Count = 1;
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "physics without tracks retains full cadence");
spine.timeScale = 0;
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "paused track time is not proof that physics or transforms are stopped");
spine.timeScale = 1;
spine.skeleton.PhysicsConstraints = null;
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "missing physics metadata fails closed");
spine.skeleton.PhysicsConstraints = new();
spine.OnMeshAndMaterialsUpdated = new();
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "custom mesh callbacks retain full cadence");
spine.OnMeshAndMaterialsUpdated = null; spine._UpdateWorld = new();
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "custom world transform producer cannot be treated as idle");
spine._UpdateWorld = null; spine.state.TimeScale = float.NaN;
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "unknown state time scale fails closed");
spine.state.TimeScale = 1; spine.updateTiming = Spine.Unity.UpdateTiming.ManualUpdate;
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "manual animation ownership is not intercepted");
spine.updateTiming = Spine.Unity.UpdateTiming.InUpdate;
character.actionQueue = new() { new() };
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "character actions remain protected before scenario animation is enqueued");
character.actionQueue = null;
spine.state.Tracks = new() { Count = 2, Items = new(new Spine.TrackEntry[1]) };
Check(CharacterActivity.Observe(preview, true) == CharacterAnimationActivity.Protected, "malformed track count fails closed");
spine.state.Tracks = new();

var inspector = new Studio.Scripts.ScriptNodeInspector { preview = preview };
Studio.Scripts.ScriptNodeInspector.instance = inspector;
bool guard = true;
using var cadence = new CharacterAnimationMeshCadence(new HarmonyLib.Harmony("test"), () => guard);
Check(cadence.IsInstalled, "mesh hook registration is required before optimization");
using (var duplicate = new CharacterAnimationMeshCadence(new HarmonyLib.Harmony("duplicate"), () => true))
    Check(!duplicate.IsInstalled, "second mesh owner is rejected without disturbing the first");
Check(cadence.ShouldRun(spine, false), "mesh gate defaults off");
cadence.Update(true);
Check(!cadence.ShouldRun(spine, false), "exact editor slot mesh is omitted only on non-render frame");
Check(cadence.ActivityCacheBuilds == 1, "first non-render mesh builds one activity snapshot");
Check(!cadence.ShouldRun(spine, false), "same-frame mesh call reuses the activity snapshot");
Check(cadence.ActivityCacheBuilds == 1, "same-frame mesh calls do not rescan activity or slots");
UnityEngine.Time.frameCount++;
Check(!cadence.ShouldRun(spine, false), "next frame refreshes the mesh gate without changing the native state");
Check(cadence.ActivityCacheBuilds == 2, "a new Unity frame rebuilds the activity snapshot");
Check(cadence.ShouldRun(spine, true), "every render frame rebuilds the latest pose");
Check(System.Threading.Tasks.Task.Run(() => cadence.ShouldRun(spine, false)).Result, "foreign thread never inspects or mutates Unity objects");
Check(cadence.ShouldRun(new Spine.Unity.SkeletonAnimation { valid = true }, false), "unrelated character renderer is not omitted");
guard = false;
Check(cadence.ShouldRun(spine, false), "export or global disable guard takes effect at mesh call");
guard = true;
UnityEngine.Input.anyKey = true;
Check(cadence.ShouldRun(spine, false), "new keyboard or button input wakes mesh immediately");
UnityEngine.Input.anyKey = false; UnityEngine.Input.mouseScrollDelta = new() { sqrMagnitude = 1 };
Check(cadence.ShouldRun(spine, false), "new wheel input wakes mesh immediately");
UnityEngine.Input.mouseScrollDelta = new();
inspector.loading = true;
Check(cadence.ShouldRun(spine, false), "loading restores the original mesh path");
inspector.loading = false;
preview.hasVoice = true;
Check(cadence.ShouldRun(spine, false), "voice playback restores original mesh path");
preview.hasVoice = false;
spine.OnPostProcessVertices = new();
Check(cadence.ShouldRun(spine, false), "mesh callback installed after Update cannot be skipped");
spine.OnPostProcessVertices = null;
preview.currentAnims = new() { new() };
Check(cadence.ShouldRun(spine, false), "new scenario producer cannot be skipped");
preview.currentAnims = null;
Studio.Scripts.ScriptNodeInspector.instance = null;
Check(cadence.ShouldRun(spine, false), "closing inspector releases immediately");
Studio.Scripts.ScriptNodeInspector.instance = inspector;
cadence.Suspend();
Check(cadence.ShouldRun(spine, false), "handoff suspension releases without changing native state");
cadence.Update(true);
var stateBefore = spine.state; var timeBefore = spine.timeScale; var modeBefore = spine.updateMode;
for (int frame = 0; frame < 120; frame++) {
    // Simulate native logical Update, including one event per logical tick.
    spine.LogicalUpdateCount++;
    if (cadence.ShouldRun(spine, frame % 4 == 0)) spine.MeshBuildCount++;
}
Check(spine.LogicalUpdateCount == 120 && spine.MeshBuildCount == 30, "mesh sampling preserves all logical updates while omitting three of four unused uploads");
Check(ReferenceEquals(stateBefore, spine.state) && timeBefore == spine.timeScale && modeBefore == spine.updateMode && spine.isActiveAndEnabled,
    "cadence does not pause, replace, or disable the skeleton");
cadence.Dispose();
Check(cadence.ShouldRun(spine, false), "dispose immediately restores original mesh execution");
bool throwGuard = true;
using var failing = new CharacterAnimationMeshCadence(new HarmonyLib.Harmony("failing"), () => throwGuard ? throw new Exception("guard read failed") : true);
failing.Update(true);
Check(failing.ShouldRun(spine, false), "unknown ownership exception runs original mesh");
throwGuard = false; failing.Update(true);
Check(failing.ShouldRun(spine, false), "failed native ownership session cannot silently re-enable optimization");
Console.WriteLine($"RESULT {passed}/{passed}; production character activity with host stubs, not a Spine runtime test.");

public class Test {
    public bool previewMode, auto, hasVoice;
    public bool isActiveAndEnabled = true;
    public IntPtr Pointer = new(1234);
    public NativeArray<Character>? slots;
    public object? delayedAdvanceTask, currentBGEffectInstance;
    public Il2CppSystem.Collections.Generic.List<ScenarioAnimation.ScenarioAnimation>? currentAnims, backgroundAnimations, currentSTs;
}
public class Character
{
    public bool isActiveAndEnabled = true;
    public bool isInitialized;
    public object? blinkTask;
    public ActivityComponent? animator, posTweener;
    public Spine.Unity.SkeletonAnimation? anim;
    public object? currentQueuedAnimation;
    public Il2CppSystem.Collections.Generic.List<object>? actionQueue;
    public Il2CppSystem.Collections.Generic.List<ScenarioAnimation.ScenarioAnimation>? animationList, animationQueue;
}
public class ActivityComponent { private static int next; public IntPtr Pointer = new(++next); public bool isActiveAndEnabled = true; }
public class NativeArray<T> { private readonly T[] _values; public NativeArray(T[] values) => _values = values; public int Length => _values.Length; public T this[int i] => _values[i]; }
namespace ScenarioAnimation { public class ScenarioAnimation { public bool hasCompleted, isCancelled; } }
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { } }
namespace Spine {
    public class ExposedList<T> { public NativeArray<T>? Items = new(Array.Empty<T>()); public int Count; }
    public class AnimationState { public ExposedList<TrackEntry>? Tracks = new(); public float TimeScale = 1; }
    public class TrackEntry { public TrackEntry? Next, MixingFrom, MixingTo; }
    public class Skeleton { public ExposedList<PhysicsConstraint>? PhysicsConstraints = new(); }
    public class PhysicsConstraint { }
}
namespace Spine.Unity {
    public enum UpdateTiming { ManualUpdate, InUpdate, InFixedUpdate, InLateUpdate }
    public enum UpdateMode { Nothing, OnlyAnimationStatus, EverythingExceptMesh, FullUpdate }
    public class SkeletonRenderer : ActivityComponent {
        public bool valid;
        public Spine.Skeleton? skeleton;
        public Spine.Skeleton? Skeleton => skeleton;
        public object? generateMeshOverride, OnPostProcessVertices, OnMeshAndMaterialsUpdated;
        public UpdateMode updateMode = UpdateMode.FullUpdate;
        public void LateUpdateMesh() { }
    }
    public class SkeletonAnimation : SkeletonRenderer {
        public int LogicalUpdateCount, MeshBuildCount;
        public float timeScale;
        public Spine.AnimationState? state;
        public Spine.AnimationState? AnimationState => state;
        public object? _BeforeApply, _UpdateLocal, _UpdateWorld, _UpdateComplete;
        public UpdateTiming updateTiming = UpdateTiming.InUpdate;
    }
}
namespace Studio.Scripts { public class ScriptNodeInspector : ActivityComponent { public static ScriptNodeInspector? instance; public bool loading, unloading; public Test? preview; } }
namespace Azurite { internal static class OptionalHostActivity { public static bool CurrentEffectActive(Test preview) => preview.currentBGEffectInstance != null; public static bool CustomEffectActive(Test _) => false; } }
namespace UnityEngine { public class Vector2 { public float sqrMagnitude; } public static class Input { public static bool anyKey; public static Vector2 mouseScrollDelta = new(); } public static class Time { public static int frameCount; } }
namespace UnityEngine.Rendering { public static class OnDemandRendering { public static bool willCurrentFrameRender; } }
namespace HarmonyLib {
    public sealed class Harmony {
        public string Id;
        private static Patches patches = new();
        public Harmony(string id) => Id = id;
        public void Patch(System.Reflection.MethodInfo target, HarmonyMethod prefix) => patches.Owners.Add(Id);
        public void Unpatch(System.Reflection.MethodInfo target, System.Reflection.MethodInfo prefix) => patches.Owners.Remove(Id);
        public static Patches GetPatchInfo(System.Reflection.MethodInfo target) => patches;
    }
    public sealed class Patches { public System.Collections.Generic.List<string> Owners = new(); }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools {
        public static System.Reflection.MethodInfo? DeclaredMethod(Type type, string name, Type[] args) => type.GetMethod(name, args);
        public static System.Reflection.MethodInfo Method(Type type, string name) => type.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    }
}
