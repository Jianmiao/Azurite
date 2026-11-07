// The host doubles preserve the native operation's relevant semantics:
// construction stores snapshots, Redo writes the node row, and WriteHistory
// only records the operation. Sync methods observe the model without writes.
namespace UnityEngine
{
    public enum KeyCode { LeftAlt, RightAlt, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5 }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Held = new(), Pressed = new();
        public static bool GetKey(KeyCode key) => Held.Contains(key);
        public static bool GetKeyDown(KeyCode key) => Pressed.Contains(key);
        public static void Reset() { Held.Clear(); Pressed.Clear(); }
    }
    public sealed class GameObject { public bool activeInHierarchy = true; }
}
public static class UICamera { public static bool isDragging; }
public sealed class UIInput
{
    public static UIInput? current, selection;
}
public sealed class Script
{
    private static long _next;
    public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
    public int speakerSlotNum;
}
public sealed class ScriptData
{
    public int speakerSlotNum;
    public ScriptData(Script script) => speakerSlotNum = script.speakerSlotNum;
}
namespace Studio.Scripts.Nodes
{
    public sealed class ScriptNode
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
        public List<Script> scripts = new();
    }
}
namespace Studio.Scripts
{
    using Studio.Scripts.Nodes;
    public sealed class ScriptListItem
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
        public ScriptNode scriptNode = null!;
        public ScriptNodeInspector inspector = null!;
        public int index;
        public bool selected = true, isActiveAndEnabled = true;
        public UnityEngine.GameObject gameObject = new();
    }
    public sealed class ScriptNodeInspector
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
        public static ScriptNodeInspector instance = null!;
        public ScriptNode scriptNode = null!;
        public ScriptListItem? selectedScriptItem;
        public List<ScriptListItem> scriptNodeListItemsCache = new();
        public bool isActiveAndEnabled = true, loading, unloading;
        public int NameSyncs, SlotSyncs;
        public readonly List<int> ObservedSlots = new();
        public void SyncScriptName() { NameSyncs++; ObservedSlots.Add(scriptNode.scripts[0].speakerSlotNum); }
        public void SyncSlots() { SlotSyncs++; ObservedSlots.Add(scriptNode.scripts[0].speakerSlotNum); }
    }
}
namespace Studio.Scripts.OperationManagement
{
    using Studio.Scripts.Nodes;
    public sealed class ScenarioScriptInspectorModifyOperation
    {
        public static int Constructions, Redos;
        public static bool ThrowOnRedo, ThrowAfterMutation;
        public static readonly List<string> Events = new();
        private readonly ScriptNode _node;
        private readonly int _index;
        public readonly Script Script;
        public readonly ScriptData Before, After;
        public ScenarioScriptInspectorModifyOperation(ScriptNode node, Script script, ScriptData before, ScriptData after)
        {
            Constructions++;
            _node = node;
            _index = node.scripts.FindIndex(x => x.Pointer == script.Pointer);
            if (_index < 0) throw new InvalidOperationException("Operation script is not in the active node.");
            Script = script; Before = before; After = after;
            Events.Add("construct");
        }
        public void Redo()
        {
            Redos++; Events.Add("redo");
            if (ThrowOnRedo) throw new InvalidOperationException("Native mutation failed.");
            _node.scripts[_index].speakerSlotNum = After.speakerSlotNum;
            if (ThrowAfterMutation) throw new InvalidOperationException("Native Redo failed after changing the model.");
        }
        public void Undo() => _node.scripts[_index].speakerSlotNum = Before.speakerSlotNum;
    }
    public static class OperationManager
    {
        public static readonly List<ScenarioScriptInspectorModifyOperation> History = new();
        public static readonly List<(bool Prev, bool Next)> Links = new();
        public static bool ThrowOnWrite;
        public static void WriteHistory(ScenarioScriptInspectorModifyOperation operation, bool linkedWithPrev, bool linkedWithNext)
        {
            ScenarioScriptInspectorModifyOperation.Events.Add("history");
            if (ThrowOnWrite) throw new InvalidOperationException("History registration failed.");
            History.Add(operation); Links.Add((linkedWithPrev, linkedWithNext));
        }
    }
}
