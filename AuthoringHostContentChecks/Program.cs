using Azurite;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

static class Program
{
    static int passed,failed;static string originalSchema="";static List<AuthoringCardPackResourceChoice> choices=new();
    const string Stage="[{\"slot\":2,\"resource_id\":\"fixture:actor\",\"face_id\":\"00\",\"starting_position\":4,\"ending_position\":4,\"display_order\":3,\"action\":\"Idle\",\"emoticon\":\"None\",\"effect\":\"None\",\"appear\":\"None\",\"shape_overrides\":[\"Closeup\"],\"curve\":null}]";
    static void Check(bool v,string m){if(!v)throw new Exception(m);}
    static void Reject(Action a,string m){try{a();}catch{return;}throw new Exception(m);}
    static string Resource(string id,string kind,bool available=true)
    {
        var fields=new Dictionary<string,object?>{{"resource_id",id},{"kind",kind},{"name","Fixture resource"},{"source","native"},
            {"available",available},{"availability_check",kind=="character"?"exported_skeleton_metadata":"catalog_index"},
            {"aliases",Array.Empty<string>()},{"metadata",new Dictionary<string,object>()},{"native_key","not-the-resource-id"},{"catalog_version","fixture-catalog"}};
        if(kind=="character")fields.Add("capabilities",new{offstage=true,stage=true,faces_loaded=true,faces=new[]{"00","01"}});
        return JsonSerializer.Serialize(fields);
    }
    static void Reset()
    {
        var studio=new StudioCommon();StudioCommon.instance=studio;AuthoringEditorSession.Current=new(){studio=studio};
        var node=new Studio.Scripts.Nodes.ScriptNode {scripts=new(){new(){LineId="anchor",StageJson=Stage.Replace("\"Idle\"","\"Greeting\"",StringComparison.Ordinal).Replace("\"None\"","\"Shot\"",StringComparison.Ordinal)}}};
        var inspector=new ScriptNodeInspector{scriptNode=node};var row=new ScriptListItem{scriptNode=node,inspector=inspector,index=0};inspector.selectedScriptItem=row;inspector.scriptNodeListItemsCache.Add(row);ScriptNodeInspector.instance=inspector;
        AuthoringApiSchema.Schema=originalSchema;AuthoringApiSchema.Calls=0;AuthoringResourceCatalog.CatalogVersion="fixture-catalog";AuthoringResourceCatalog.AfterResolve=null;AuthoringResourceCatalog.Calls.Clear();
        AuthoringResourceCatalog.Resources=new(){["fixture:actor"]=Resource("fixture:actor","character"),["fixture:fade"]=Resource("fixture:fade","transition"),["fixture:hit"]=Resource("fixture:hit","sound")};
        AuthoringLines.Stages=new(new[]{Stage,Stage});AuthoringLines.Previous.Clear();AuthoringLines.Creates=AuthoringLines.Reads=0;AuthoringLines.AfterCreate=null;
        choices=new(){new("fade","fixture:fade",CardResourceKind.Transition),new("hit","fixture:hit",CardResourceKind.Sound)};
    }
    static CardPackContextIdentity Identity()=>new("fixture-project","fixture-session","rev:1","fixture-catalog",ScriptNodeInspector.instance!.scriptNode.guid.ToString(),"anchor",2);
    static AuthoringCardPackHostContent Source()=>new(Environment.CurrentManagedThreadId,()=>choices);
    static void Test(string name,Action action){Reset();try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static CardPackV2 Pack()=>new("2","five","Five",new[]{new CardV2("fade",PresetCardKind.Transition,ResourceKey:"fade"),new CardV2("hit",PresetCardKind.Combat,"hit",2,"Stiff"),new CardV2("greet",PresetCardKind.Action,TargetSlot:2,Action:"Greeting"),new CardV2("spoken",PresetCardKind.Language,TargetSlot:2,Variants:new Dictionary<string,string>{{"zh-CN","原文 [FF0000]"},{"en","Supplied English"}}),new CardV2("sound",PresetCardKind.SoundEffect,"hit")});
    static int Main()
    {
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NativeSchemaResponse.json"));
        Check(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()=="b97d7bb5da9a86738a7d43eec79e5b26547bccb50d68400d92891741258f978d","native schema fixture hash drift");
        originalSchema=JsonDocument.Parse(bytes).RootElement.GetProperty("data").GetRawText();
        Test("real native schema plus projected inheritance compiles all five card families",()=>{
            var context=Source().Capture(Identity(),"en");var result=CardPackV2Compiler.TryCompile(Pack(),context,Identity());
            Check(result.IsSuccess && result.Plan!.OperationCount==5,string.Join(";",result.Errors.Select(x=>x.Code)));
            using var operations=JsonDocument.Parse(result.Plan!.OperationsJson);Check(operations.RootElement[3].GetProperty("line").GetProperty("text").GetString()=="Supplied English","language text altered");
            Check(operations.RootElement[0].GetProperty("line").GetProperty("scene").GetProperty("transition").GetProperty("resource_id").GetString()=="fixture:fade","native_key or display name replaced the opaque resource ID");
            Check(context.Resources.Count==3 && context.Resources.Single(x=>x.Kind==CardResourceKind.Character).FaceIds.SequenceEqual(new[]{"00","01"}),"retained actor capabilities not resolved");
            Check(AuthoringResourceCatalog.Calls.Count==3 && AuthoringResourceCatalog.Calls.All(x=>x.Version=="fixture-catalog"&&x.Capabilities),"bindings did not use one verified catalog version");
        });
        Test("native factory output replaces raw transient stage and strips only readonly fields",()=>{
            var raw=ScriptNodeInspector.instance!.scriptNode.scripts[0].StageJson;var context=Source().Capture(Identity(),"zh-CN");var stage=context.InheritedStage[0];
            Check(AuthoringLines.Creates==2 && AuthoringLines.Reads==2 && !ReferenceEquals(AuthoringLines.Previous[0],AuthoringLines.Previous[1]),"native inheritance chain was not probed");
            Check(stage.GetProperty("action").GetString()=="Idle" && stage.GetProperty("effect").GetString()=="None" && !stage.TryGetProperty("starting_position",out _) && stage.GetProperty("ending_position").GetInt32()==4 && stage.GetProperty("shape_overrides")[0].GetString()=="Closeup","inherited writable data projection is wrong");
            Check(raw==ScriptNodeInspector.instance.scriptNode.scripts[0].StageJson,"capture mutated the existing line");
        });
        Test("unstable native inheritance across insertion chain rejects capture",()=>{AuthoringLines.Stages=new(new[]{Stage,Stage.Replace("\"ending_position\":4","\"ending_position\":1",StringComparison.Ordinal)});Reject(()=>Source().Capture(Identity(),"zh-CN"),"non-idempotent native inheritance was assumed stable");});
        Test("unknown inherited writable fields are rejected rather than dropped",()=>{var extra=Stage.Replace("\"slot\":2","\"unmapped_state\":true,\"slot\":2",StringComparison.Ordinal);AuthoringLines.Stages=new(new[]{extra,extra});Reject(()=>Source().Capture(Identity(),"zh-CN"),"unknown native state silently dropped");});
        Test("native capabilities are derived independently and never blanket enabled",()=>{
            var schema=JsonNode.Parse(originalSchema)!;schema["$defs"]!["SceneInput"]!["properties"]!.AsObject().Remove("sound");AuthoringApiSchema.Schema=schema.ToJsonString();
            var context=Source().Capture(Identity(),"zh-CN");Check((context.NativeCapabilities&CardNativeCapability.SceneSound)==0 && !context.Resources.Single(x=>x.ResourceKey=="hit").SceneSetSupported,"missing native sound capability was invented");
            Check(!CardPackV2Compiler.TryCompile(Pack(),context,Identity()).IsSuccess,"unsupported family compiled");
        });
        Test("native action enum changes restrict supported stage commands",()=>{
            var schema=JsonNode.Parse(originalSchema)!;schema["$defs"]!["StageCharacter"]!["properties"]!["action"]!["enum"]=JsonSerializer.SerializeToNode(new[]{"Idle"});AuthoringApiSchema.Schema=schema.ToJsonString();
            var context=Source().Capture(Identity(),"zh-CN");Check(context.Resources.Single(x=>x.Kind==CardResourceKind.Character).SupportedActions.SequenceEqual(new[]{"Idle"}),"actions were hardcoded");
            Check(!CardPackV2Compiler.TryCompile(Pack(),context,Identity()).IsSuccess,"undeclared action accepted");
        });
        Test("unavailable, wrong-kind or mismatched resource identity rejects capture",()=>{
            foreach(var json in new[]{Resource("fixture:fade","transition",false),Resource("wrong","transition"),Resource("fixture:fade","sound")}){Reset();AuthoringResourceCatalog.Resources["fixture:fade"]=json;Reject(()=>Source().Capture(Identity(),"zh-CN"),"invalid scene catalog binding admitted");Check(AuthoringResourceCatalog.Calls.Any(x=>x.Id=="fixture:fade"),"wrong negative-control path");}
        });
        Test("character stage, faces_loaded and explicit face IDs are mandatory",()=>{
            foreach(var field in new[]{"stage","faces_loaded","faces"}){Reset();var resource=JsonNode.Parse(Resource("fixture:actor","character"))!;if(field=="faces")resource["capabilities"]!.AsObject().Remove(field);else resource["capabilities"]![field]=false;AuthoringResourceCatalog.Resources["fixture:actor"]=resource.ToJsonString();Reject(()=>Source().Capture(Identity(),"zh-CN"),"unknown stage faces admitted");Check(AuthoringResourceCatalog.Calls.Any(x=>x.Id=="fixture:actor"),"wrong negative-control path");}
        });
        Test("native catalog drift and changed selected anchor invalidate projection",()=>{
            AuthoringResourceCatalog.AfterResolve=()=>AuthoringResourceCatalog.CatalogVersion="changed";Reject(()=>Source().Capture(Identity(),"zh-CN"),"catalog changed during projection");
            Reset();AuthoringLines.AfterCreate=()=>ScriptNodeInspector.instance!.scriptNode.scripts[0].LineId="other";Reject(()=>Source().Capture(Identity(),"zh-CN"),"anchor changed during native inheritance");
        });
        Test("duplicate logical or actual scene bindings are rejected explicitly",()=>{
            choices.Add(new("fade","other",CardResourceKind.Transition));Reject(()=>Source().Capture(Identity(),"zh-CN"),"duplicate logical key admitted");Reset();choices.Add(new("another","fixture:fade",CardResourceKind.Transition));Reject(()=>Source().Capture(Identity(),"zh-CN"),"duplicate actual ID admitted");
        });
        Test("copied IDs remain opaque and no schema-version fallback is assumed",()=>{
            var schema=JsonNode.Parse(originalSchema)!;schema["capabilities"]!["limits"]!["max_operations"]=499;AuthoringApiSchema.Schema=schema.ToJsonString();Reject(()=>Source().Capture(Identity(),"zh-CN"),"different native operation limit ignored");
        });
        Test("inherited fields missing from current writable schema are not emitted",()=>{
            var schema=JsonNode.Parse(originalSchema)!;schema["$defs"]!["StageCharacter"]!["properties"]!.AsObject().Remove("appear");AuthoringApiSchema.Schema=schema.ToJsonString();
            Reject(()=>Source().Capture(Identity(),"zh-CN"),"schema-undeclared inherited field survived projection");
        });
        Test("unexpected one-shot state in native inheritance is rejected instead of replayed",()=>{
            foreach(var alteration in new[]{("\"action\":\"Idle\"","\"action\":\"Greeting\""),("\"effect\":\"None\"","\"effect\":\"Shot\""),("\"curve\":null","\"curve\":{\"id\":\"unverified\"}")}){
                Reset();var transient=Stage.Replace(alteration.Item1,alteration.Item2,StringComparison.Ordinal);AuthoringLines.Stages=new(new[]{transient,transient});
                Reject(()=>Source().Capture(Identity(),"zh-CN"),"unexpected transient inheritance was marked verified");
            }
        });
        Test("wrong editor thread is rejected before any native content API",()=>{
            var source=new AuthoringCardPackHostContent(Environment.CurrentManagedThreadId+1,()=>choices);
            Reject(()=>source.Capture(Identity(),"zh-CN"),"wrong-thread capture was admitted");
            Check(AuthoringApiSchema.Calls==0 && AuthoringLines.Creates==0 && AuthoringResourceCatalog.Calls.Count==0,"wrong thread touched native content APIs");
        });
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");return failed==0?0:1;
    }
}
