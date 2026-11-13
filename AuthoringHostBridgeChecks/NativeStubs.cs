using System.Text.Json;

public class NativeObject
{
    static long next;
    public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref next);
    public T? TryCast<T>() where T : class => this as T;
}
namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; }
    public class Behaviour : NativeObject { public bool isActiveAndEnabled = true; public GameObject gameObject = new(); }
    public static class Application { public static bool isFocused = true; }
}
public static class UICamera { public static bool inputHasFocus, isDragging; }
public class UIInput { public static UIInput? current, selection; public static string mLastIME = ""; }
public class Script : NativeObject { public string LineId = "", text = ""; }
public class ScriptData : NativeObject { public string LineId = "", text = ""; }
public class NodeData : NativeObject { public Guid Guid; }
public class ScriptNodeData : NodeData { public List<ScriptData> Scripts = new(); }
public class ProjectData : NativeObject
{
    public string ProjectId = "fixture-project"; public List<NodeData> nodes = new();
    public ProjectData Clone() => new() { ProjectId = ProjectId, nodes = nodes.Select(n => (NodeData)new ScriptNodeData { Guid = n.Guid, Scripts = ((ScriptNodeData)n).Scripts.Select(x => new ScriptData { LineId = x.LineId, text = x.text }).ToList() }).ToList() };
}
namespace Il2CppSystem.Collections.Generic { public class Stack<T> : System.Collections.Generic.Stack<T> { } }
namespace Newtonsoft.Json.Linq
{
    public class JObject { public string Json = "{\"native_receipt\":true}"; public override string ToString() => Json; }
    public class JArray
    {
        public string Raw = "[]"; public int Count => JsonDocument.Parse(Raw).RootElement.GetArrayLength();
        public static JArray Parse(string value) { using var _ = JsonDocument.Parse(value); return new() { Raw = value }; }
    }
}
namespace Studio.Scripts.Nodes
{
    public class ScriptNode : UnityEngine.Behaviour { public Guid guid; public List<Script> scripts = new(); }
}
namespace Studio.Scripts
{
    public class StudioCommon : UnityEngine.Behaviour
    {
        public static StudioCommon? instance; public object entryNode = new(); public string sessionInitializationError = "";
        public bool autoSavePending, autoCompilePending;
        public ProjectData Project = new();
        public ProjectData CaptureProject() => Project.Clone();
    }
    public class ScriptListItem : UnityEngine.Behaviour
    {
        public int index; public bool selected = true;
        public Nodes.ScriptNode scriptNode = null!; public ScriptNodeInspector inspector = null!;
    }
    public class ScriptNodeInspector : UnityEngine.Behaviour
    {
        public static ScriptNodeInspector? instance;
        public Nodes.ScriptNode scriptNode = null!;
        public ScriptListItem? selectedScriptItem;
        public List<ScriptListItem> scriptNodeListItemsCache = new();
        public int selectedSlotNum = 2; public bool loading, unloading, rearrangeScheduled;
    }
}
namespace Studio.Scripts.OperationManagement
{
    public class Operation : NativeObject { public Operation? linkedPrev, linkedNext; }
    public static class OperationManager
    {
        public static Il2CppSystem.Collections.Generic.Stack<Operation> history = new(), future = new();
        public static int saveDelta, compileDelta; public static bool Working, needToLinkWithPrev;
    }
}
namespace AzureArchive.Automation
{
    using Studio.Scripts;
    using Studio.Scripts.OperationManagement;
    using Newtonsoft.Json.Linq;
    public static class AuthoringResourceCatalog
    {
        public static string CatalogVersion = "fixture-catalog";
        public static List<string> Resolved = new(); public static bool Missing; public static string? ResponseOverride;
        public static JObject Resolve(string id, string kind, string version, bool includeCapabilities)
        {
            if (version != CatalogVersion || Missing) throw new Exception("CATALOG_CHANGED or missing resource");
            if (!includeCapabilities || kind != "transition") throw new Exception("wrong native Resolve arguments");
            Resolved.Add(id); return new() { Json = ResponseOverride ?? JsonSerializer.Serialize(new { resource_id = id, kind, catalog_version = version, available = true }) };
        }
    }
    public class AuthoringEditorSession : NativeObject
    {
        public static AuthoringEditorSession? Current;
        public StudioCommon studio = null!; public string ProjectId => studio.Project.ProjectId;
        public string EditSession = Guid.NewGuid().ToString(), Revision = "r1", FileVersion = "file:v1";
        public string savedFingerprint = "saved-baseline";
        public bool IsApplying, SaveInProgress, disposed; public bool Dirty => OperationManager.saveDelta != 0;
        public ProjectData Snapshot = null!; public bool FreezeSnapshot;
        public Action? OnCheck, OnApply; public bool ThrowBeforeApply, ThrowAfterHistory, PartialMutation, NullReceipt;
        public int Checks, Applies; public string? CheckedSession, CheckedRevision; public bool CheckedInput, CheckedResources;
        public List<string> Events = new();
        public ProjectData Capture() => studio.CaptureProject();
        public void Refresh(bool checkResources) { if (!checkResources) throw new Exception("resources must be refreshed"); if (!FreezeSnapshot) Snapshot = studio.CaptureProject(); }
        public static string Fingerprint(ProjectData project) => JsonSerializer.Serialize(new { project.ProjectId, nodes = project.nodes.Select(n => new { n.Guid, lines = ((ScriptNodeData)n).Scripts.Select(x => new { x.LineId, x.text }) }) });
        public void Check(string session, string expectedRevision, bool inputGuard, bool checkResources)
        {
            Checks++; Events.Add("Check"); CheckedSession = session; CheckedRevision = expectedRevision; CheckedInput = inputGuard; CheckedResources = checkResources;
            if (session != EditSession || expectedRevision != Revision) throw new Exception("REVISION_CONFLICT");
            OnCheck?.Invoke();
        }
        public JObject? Apply(JArray operations, string catalogVersion)
        {
            Applies++; Events.Add("Apply"); OnApply?.Invoke();
            if (ThrowBeforeApply) throw new Exception("native failed before mutation");
            if (catalogVersion != AuthoringResourceCatalog.CatalogVersion) throw new Exception("wrong catalog argument");
            var before = studio.Project.Clone(); var node = (ScriptNodeData)studio.Project.nodes[0];
            using var json = JsonDocument.Parse(operations.Raw); int insertion = 1;
            foreach (var op in json.RootElement.EnumerateArray()) node.Scripts.Insert(insertion++, new() { LineId = "native-created:" + Guid.NewGuid(), text = op.GetProperty("line").GetProperty("text").GetString()! });
            Revision = "r2"; Snapshot = studio.Project.Clone(); Fixture.Rebind();
            if (PartialMutation) throw new Exception("native restore failed before history");
            OperationManager.history.Push(new ProjectSnapshotOperation { session = this, before = before, after = studio.Project.Clone() });
            OperationManager.future.Clear(); OperationManager.saveDelta++; OperationManager.compileDelta++; OperationManager.needToLinkWithPrev = false;
            if (ThrowAfterHistory) throw new Exception("native failed after history");
            return NullReceipt ? null : new JObject();
        }
        public class ProjectSnapshotOperation : Operation { public AuthoringEditorSession session = null!; public ProjectData before = null!, after = null!; }
    }
}
public static class Fixture
{
    public static void Reset()
    {
        var node = new ScriptNodeData { Guid = Guid.NewGuid(), Scripts = new() { new() { LineId = "opaque-anchor", text = "before" }, new() { LineId = "opaque-tail", text = "tail" } } };
        var studio = new Studio.Scripts.StudioCommon { Project = new() { nodes = new() { node } } };
        Studio.Scripts.StudioCommon.instance = studio;
        var session = new AzureArchive.Automation.AuthoringEditorSession { studio = studio, Snapshot = studio.Project.Clone() };
        AzureArchive.Automation.AuthoringEditorSession.Current = session;
        Studio.Scripts.OperationManagement.OperationManager.history = new(); Studio.Scripts.OperationManagement.OperationManager.future = new();
        Studio.Scripts.OperationManagement.OperationManager.history.Push(new());
        Studio.Scripts.OperationManagement.OperationManager.saveDelta = Studio.Scripts.OperationManagement.OperationManager.compileDelta = 0;
        Studio.Scripts.OperationManagement.OperationManager.Working = Studio.Scripts.OperationManagement.OperationManager.needToLinkWithPrev = false;
        AzureArchive.Automation.AuthoringResourceCatalog.CatalogVersion = "fixture-catalog"; AzureArchive.Automation.AuthoringResourceCatalog.Missing = false; AzureArchive.Automation.AuthoringResourceCatalog.ResponseOverride = null; AzureArchive.Automation.AuthoringResourceCatalog.Resolved.Clear();
        UnityEngine.Application.isFocused = true; UICamera.inputHasFocus = UICamera.isDragging = false; UIInput.current = UIInput.selection = null; UIInput.mLastIME = "";
        Rebind();
    }
    public static void Rebind()
    {
        var data = (ScriptNodeData)Studio.Scripts.StudioCommon.instance!.Project.nodes[0];
        var node = new Studio.Scripts.Nodes.ScriptNode { guid = data.Guid, scripts = data.Scripts.Select(x => new Script { LineId = x.LineId, text = x.text }).ToList() };
        var inspector = new Studio.Scripts.ScriptNodeInspector { scriptNode = node };
        inspector.scriptNodeListItemsCache = node.scripts.Select((_, i) => new Studio.Scripts.ScriptListItem { index = i, scriptNode = node, inspector = inspector, selected = i == 0 }).ToList();
        inspector.selectedScriptItem = inspector.scriptNodeListItemsCache[0]; Studio.Scripts.ScriptNodeInspector.instance = inspector;
    }
}
