using System.Text.Json;
using System.Text.Json.Nodes;

public class NativeObject { static long next; public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next); }
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
namespace Studio.Scripts.Nodes { public class ScriptNode:NativeObject { public Guid guid=Guid.NewGuid(); public List<Script> scripts=new(); } }
namespace Studio.Scripts
{
    public class StudioCommon:NativeObject { public static StudioCommon? instance; }
    public class ScriptListItem:NativeObject { public int index; public bool selected=true; public ScriptNodeInspector inspector=null!; public Nodes.ScriptNode scriptNode=null!; }
    public class ScriptNodeInspector:NativeObject
    {
        public static ScriptNodeInspector? instance;public Nodes.ScriptNode scriptNode=null!;public ScriptListItem selectedScriptItem=null!;
        public List<ScriptListItem> scriptNodeListItemsCache=new();public int selectedSlotNum=2;
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
        public static JObject Resolve(string resourceId,string expectedKind,string expectedCatalogVersion,bool includeCapabilities)
        {
            Calls.Add((resourceId,expectedKind,expectedCatalogVersion,includeCapabilities));
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
