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
public class Script : NativeObject
{
    public string LineId = "", text = ""; public int speakerSlotNum;
    public class CharacterRecord
    {
        public string? name,faceId,curve;public int startingPos,endingPos,displayOrder,emoticon,action,effect,appear,shapeOverride;
        // Pinned 0x7e5a50 constructor and independently decoded metadata literals: name="", face="00".
        public CharacterRecord() { name="";faceId="00";displayOrder=1;emoticon=-1; }
    }
}
public class NativeList<T> : List<T>
{
    static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);
    public NativeList() { } public NativeList(IEnumerable<T> items):base(items) { }
}
public class ScriptData : NativeObject
{
    public string LineId = "", text = "", voice = "voice", bgFriendlyName = "background", additionalPrompt = "prompt", StageJson = "[{\"curve\":{\"extension\":2}}]";
    public int speakerSlotNum = 2; public long selectionGroup = 42; public bool isDialogScript=true;
    public NativeList<CharacterRecordData> characters=new(Enumerable.Range(0,6).Select(_=>new CharacterRecordData()));
    public ScriptData Clone() => new() { LineId=LineId,text=text,voice=voice,bgFriendlyName=bgFriendlyName,additionalPrompt=additionalPrompt,StageJson=StageJson,speakerSlotNum=speakerSlotNum,selectionGroup=selectionGroup,isDialogScript=isDialogScript,characters=new(characters.Select(c=>c.Clone())) };
    public class CharacterRecordData : NativeObject
    {
        public string? name,faceId,curve;public int startingPos,endingPos,displayOrder=1,emoticon,action,effect,appear,shapeOverride;
        public CharacterRecordData() { }
        public CharacterRecordData(Script.CharacterRecord r){name=r.name;faceId=r.faceId;curve=r.curve;startingPos=r.startingPos;endingPos=r.endingPos;displayOrder=r.displayOrder;emoticon=r.emoticon;action=r.action;effect=r.effect;appear=r.appear;shapeOverride=r.shapeOverride;}
        public CharacterRecordData Clone()=>new(){name=name,faceId=faceId,curve=curve,startingPos=startingPos,endingPos=endingPos,displayOrder=displayOrder,emoticon=emoticon,action=action,effect=effect,appear=appear,shapeOverride=shapeOverride};
    }
}
public class NodeData : NativeObject { public Guid Guid; }
public class ScriptNodeData : NodeData { public List<ScriptData> Scripts = new(); }
public class ProjectData : NativeObject
{
    public string ProjectId = "fixture-project", ProjectName = "full-project-residue", PreviewHeader = "fixture-header"; public List<NodeData> nodes = new();
    public ProjectData Clone() => new() { ProjectId = ProjectId, ProjectName = ProjectName, PreviewHeader = PreviewHeader, nodes = nodes.Select(n => (NodeData)new ScriptNodeData { Guid = n.Guid, Scripts = ((ScriptNodeData)n).Scripts.Select(x => x.Clone()).ToList() }).ToList() };
}
namespace Il2CppSystem.Collections.Generic { public class Stack<T> : System.Collections.Generic.Stack<T> { } }
namespace Newtonsoft.Json.Linq
{
    public class JObject { public string Json = "{\"native_receipt\":true}"; public override string ToString() => Json; public static JObject Parse(string value){using var _=JsonDocument.Parse(value);return new(){Json=value};} }
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
    public class Operation : NativeObject { public Operation? linkedPrev, linkedNext; public bool IsMajor = true; }
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
    // Explicit native boundary double. Tests exercise real Host calls and result proof;
    // these data operations do not claim that actual native cloning/Apply has run.
    public static class ProjectFileStore
    {
        public static Func<ProjectData, ProjectData>? CloneOverride;
        public static ProjectData Clone(ProjectData value) => CloneOverride?.Invoke(value) ?? value.Clone();
    }
    public static class AuthoringResourceCatalog
    {
        public static string CatalogVersion = "fixture-catalog";
        public static List<string> Resolved = new(); public static bool Missing; public static string? ResponseOverride;
        public static Dictionary<string,string> CharacterResources=new();public static Action? AfterResolve;
        public static JObject Resolve(string id, string kind, string version, bool includeCapabilities)
        {
            if (version != CatalogVersion || Missing) throw new Exception("CATALOG_CHANGED or missing resource");
            if (!includeCapabilities || kind is not ("transition" or "character")) throw new Exception("wrong native Resolve arguments");
            if(kind=="character") {Resolved.Add(id);if(!CharacterResources.TryGetValue(id,out var value))throw new Exception("character missing");AfterResolve?.Invoke();return JObject.Parse(value);}
            Resolved.Add(id); return new() { Json = ResponseOverride ?? JsonSerializer.Serialize(new { resource_id = id, kind, catalog_version = version, available = true }) };
        }
    }
    public static class AuthoringLines
    {
        public static Action<ScriptData>? ExtraPatch;public static Func<ScriptData,string,string>? ReadOverride;public static int Reads,DetachedPatches;
        public static JObject Read(ScriptData data,Guid node)
        {
            Reads++;string mode=!data.isDialogScript?"control":data.speakerSlotNum>0||!string.IsNullOrEmpty(data.characters[0].name)?"dialogue":"narration";
            object? speaker=mode!="dialogue"?null:data.speakerSlotNum>0?new{slot=data.speakerSlotNum}:new{offstage_resource_id="fixture:"+data.characters[0].name};
            var stage=Enumerable.Range(1,5).Where(i=>!string.IsNullOrEmpty(data.characters[i].name)).Select(i=>new{slot=i,resource_id="fixture:"+data.characters[i].name,face_id=data.characters[i].faceId,starting_position=data.characters[i].startingPos,ending_position=data.characters[i].endingPos,display_order=data.characters[i].displayOrder,action="Idle",emoticon="None",effect="None",appear="None",shape_overrides=Array.Empty<string>(),curve=data.characters[i].curve});
            string json=JsonSerializer.Serialize(new{line_id=data.LineId,node_id=node.ToString(),text=data.text,mode,speaker,stage,highlighted_slots=Array.Empty<int>(),place="fixture-place",extra_script=data.additionalPrompt,scene=new{background=new{action="inherit"},background_effect=new{action="none"},transition=new{action="none"},bgm=new{action="inherit"},sound=new{action="none"},popup=new{action="none"},voice=new{action="none"}}});
            return JObject.Parse(ReadOverride?.Invoke(data,json)??json);
        }
        public static void Patch(ScriptData data,JObject set,JArray? clear,string version)
        {
            if(clear!=null||version!=AuthoringResourceCatalog.CatalogVersion)throw new Exception("wrong detached Patch arguments");
            using var json=JsonDocument.Parse(set.Json);var root=json.RootElement;if(root.EnumerateObject().Count()!=1)throw new Exception("speaker-only patch required");
            DetachedPatches++;data.characters[0]=new(new Script.CharacterRecord());data.speakerSlotNum=root.GetProperty("speaker").GetProperty("slot").GetInt32();ExtraPatch?.Invoke(data);
        }
    }
    public static class AuthoringGraph
    {
        public static Action<ProjectData>? OnNormalize;public static int Normalizes;
        public static void Normalize(ProjectData project){Normalizes++;OnNormalize?.Invoke(project);}
    }
    public class AuthoringEditorSession : NativeObject
    {
        public static AuthoringEditorSession? Current;
        public StudioCommon studio = null!; public string ProjectId => studio.Project.ProjectId;
        public string EditSession = Guid.NewGuid().ToString(), Revision = "r1", FileVersion = "file:v1";
        public string savedFingerprint = "saved-baseline";
        public bool IsApplying, SaveInProgress, disposed; public bool Dirty => OperationManager.saveDelta != 0;
        public ProjectData Snapshot = null!; public bool FreezeSnapshot;
        public Action? OnCheck, OnApply, OnAfterApply; public bool ThrowBeforeApply, ThrowAfterHistory, PartialMutation, NullReceipt;
        public string? LastOperations;
        public int Checks, Applies; public string? CheckedSession, CheckedRevision; public bool CheckedInput, CheckedResources;
        public List<string> Events = new();
        public ProjectData Capture() => studio.CaptureProject();
        public void Refresh(bool checkResources) { if (!checkResources) throw new Exception("resources must be refreshed"); if (!FreezeSnapshot) Snapshot = studio.CaptureProject(); }
        public static string Fingerprint(ProjectData project) => JsonSerializer.Serialize(new { project.ProjectId, project.ProjectName, project.PreviewHeader, nodes = project.nodes.Select(n => new { n.Guid, lines = ((ScriptNodeData)n).Scripts.Select(x => new { x.LineId, x.text,x.voice,x.bgFriendlyName,x.additionalPrompt,x.StageJson,x.speakerSlotNum,x.selectionGroup,x.isDialogScript,characters=x.characters.Select(c=>new{c.name,c.faceId,c.curve,c.startingPos,c.endingPos,c.displayOrder,c.emoticon,c.action,c.effect,c.appear,c.shapeOverride}) }) }) });
        public void Check(string session, string expectedRevision, bool inputGuard, bool checkResources)
        {
            Checks++; Events.Add("Check"); CheckedSession = session; CheckedRevision = expectedRevision; CheckedInput = inputGuard; CheckedResources = checkResources;
            if (session != EditSession || expectedRevision != Revision) throw new Exception("REVISION_CONFLICT");
            OnCheck?.Invoke();
        }
        public JObject? Apply(JArray operations, string catalogVersion)
        {
            Applies++; Events.Add("Apply"); LastOperations=operations.Raw; OnApply?.Invoke();
            if (ThrowBeforeApply) throw new Exception("native failed before mutation");
            if (catalogVersion != AuthoringResourceCatalog.CatalogVersion) throw new Exception("wrong catalog argument");
            var before = studio.Project.Clone(); var node = (ScriptNodeData)studio.Project.nodes[0];
            using var json = JsonDocument.Parse(operations.Raw); int insertion = 1;
            foreach (var op in json.RootElement.EnumerateArray())
            {
                if(op.GetProperty("op").GetString()=="line.update")
                {
                    var line=node.Scripts.Single(line=>line.LineId==op.GetProperty("line").GetProperty("id").GetString());var patch=op.GetProperty("set");
                    if(patch.TryGetProperty("text",out var text))line.text=text.GetString()!;
                    else {line.characters[0]=new(new Script.CharacterRecord());line.speakerSlotNum=patch.GetProperty("speaker").GetProperty("slot").GetInt32();}
                }
                else node.Scripts.Insert(insertion++, new() { LineId = "native-created:" + Guid.NewGuid(), text = op.GetProperty("line").GetProperty("text").GetString()! });
            }
            AuthoringGraph.Normalize(studio.Project);
            Revision = "r" + (Applies + 1); Snapshot = studio.Project.Clone(); Fixture.Rebind();
            if (PartialMutation) throw new Exception("native restore failed before history");
            OperationManager.history.Push(new ProjectSnapshotOperation { session = this, before = before, after = studio.Project.Clone(), IsMajor=Fixture.NextIsMajor });
            OperationManager.future.Clear();
            // Literal native fixture behavior from pinned WriteHistory: new isolated, uncombined entry.
            OperationManager.saveDelta=OperationManager.saveDelta<0?114514:unchecked(OperationManager.saveDelta+1);
            if(Fixture.NextIsMajor)OperationManager.compileDelta=OperationManager.compileDelta<0?114514:unchecked(OperationManager.compileDelta+1);
            OperationManager.needToLinkWithPrev = false;
            if (ThrowAfterHistory) throw new Exception("native failed after history");
            OnAfterApply?.Invoke(); Snapshot=studio.Project.Clone(); Fixture.Rebind();
            return NullReceipt ? null : new JObject();
        }
        public class ProjectSnapshotOperation : Operation { public AuthoringEditorSession session = null!; public ProjectData before = null!, after = null!; }
    }
}
public static class Fixture
{
    public static int[] BatchIndices = new[] { 0, 1 };
    public static bool NextIsMajor = true;
    public static void Reset()
    {
        BatchIndices=new[]{0,1}; NextIsMajor=true; AzureArchive.Automation.ProjectFileStore.CloneOverride=null;
        AzureArchive.Automation.AuthoringLines.ExtraPatch=null;AzureArchive.Automation.AuthoringLines.ReadOverride=null;AzureArchive.Automation.AuthoringLines.Reads=AzureArchive.Automation.AuthoringLines.DetachedPatches=0;
        AzureArchive.Automation.AuthoringGraph.OnNormalize=null;AzureArchive.Automation.AuthoringGraph.Normalizes=0;
        AzureArchive.Automation.AuthoringResourceCatalog.CharacterResources.Clear();AzureArchive.Automation.AuthoringResourceCatalog.AfterResolve=null;
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
        var node = new Studio.Scripts.Nodes.ScriptNode { guid = data.Guid, scripts = data.Scripts.Select(x => new Script { LineId = x.LineId, text = x.text, speakerSlotNum=x.speakerSlotNum }).ToList() };
        var inspector = new Studio.Scripts.ScriptNodeInspector { scriptNode = node };
        inspector.scriptNodeListItemsCache = node.scripts.Select((_, i) => new Studio.Scripts.ScriptListItem { index = i, scriptNode = node, inspector = inspector, selected = i == 0 }).ToList();
        inspector.selectedScriptItem = inspector.scriptNodeListItemsCache[0]; Studio.Scripts.ScriptNodeInspector.instance = inspector;
    }
}
