using System.Security.Cryptography;
using System.Text.Json;
using Azurite;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using UnityEngine;

static class Program
{
    static string Schema="";
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action work,string message){try{work();}catch(InvalidOperationException){return;}catch(ArgumentException){return;}throw new Exception(message);}
    static T Find<T>(string suffix) where T:class=>GameObject.All.Single(x=>x.name=="AzuriteCard"+suffix&&!x.IsDestroyed).GetComponent<T>()??throw new Exception("Missing component: "+suffix);
    static void Click(string suffix)=>Find<UIButton>(suffix).Click();
    static void Input(string suffix,string value)=>Find<UIInput>(suffix).value=value;
    static string Page(string kind,string[] ids,string? cursor=null)=>JsonSerializer.Serialize(new{catalog_version="fixture-catalog",items=ids.Select(id=>new{resource_id=id,kind,name="[FF0000]Fixture name",source="project",available=true}),next_cursor=cursor,missing_ids=Array.Empty<string>()});
    sealed class Fixture:IDisposable
    {
        internal bool Allowed=true;internal readonly GameObject Parent;internal readonly UILabel Typography;
        internal readonly AuthoringCardPackHostResourcePicker Transition,Sound;internal readonly AuthoringCardPackHostDraft Draft;internal readonly Host Host;internal readonly CardPackV2Workflow Workflow;
        internal AuthoringCardPackHostView? View;
        internal Fixture()
        {
            GameObject.All.Clear();GameObject.Added=0;GameObject.FailOnAdd=-1;GameObject.Destroyed=0;EventDelegate.Depth=0;
            var studio=new StudioCommon();StudioCommon.instance=studio;AuthoringEditorSession.Current=new(){studio=studio};AuthoringResourceCatalog.CatalogVersion="fixture-catalog";
            var inspector=new ScriptNodeInspector{scriptNode=new ScriptNode()};ScriptNodeInspector.instance=inspector;
            inspector.scriptNode.scripts.Add(new Script{LineId="anchor",StageJson="[{\"slot\":2,\"resource_id\":\"fixture-character\",\"face_id\":\"00\",\"action\":\"Idle\",\"effect\":\"None\",\"emoticon\":\"None\",\"appear\":\"None\"}]"});
            var row=new ScriptListItem{inspector=inspector,scriptNode=inspector.scriptNode};inspector.selectedScriptItem=row;inspector.scriptNodeListItemsCache.Add(row);
            AuthoringApiSchema.Schema=Schema;AuthoringApiSchema.Calls=0;
            AuthoringResourceCatalog.Resources.Clear();AuthoringResourceCatalog.Calls.Clear();AuthoringResourceCatalog.Searches.Clear();AuthoringResourceCatalog.SearchResults.Clear();AuthoringResourceCatalog.AfterResolve=null;AuthoringResourceCatalog.AfterSearch=null;AuthoringResourceCatalog.NullSearch=AuthoringResourceCatalog.NullResolve=false;
            AuthoringLines.Stages.Clear();AuthoringLines.Previous.Clear();AuthoringLines.Creates=AuthoringLines.Reads=0;AuthoringLines.AfterCreate=null;
            foreach(var item in new[]{("fixture-transition","transition"),("fixture-sound","sound"),("fixture-character","character")})
            {
                var resource=new Dictionary<string,object?>{{"resource_id",item.Item1},{"kind",item.Item2},{"catalog_version","fixture-catalog"},{"available",true},{"name","Fixture"},{"source","project"}};
                if(item.Item2=="character")resource["capabilities"]=new{stage=true,faces_loaded=true,faces=new[]{"00"}};
                AuthoringResourceCatalog.Resources[item.Item1]=JsonSerializer.Serialize(resource);
            }
            Parent=new GameObject{name="NativeParent"};Parent.AddComponent<UIPanel>();Typography=Parent.AddComponent<UILabel>();Typography.trueTypeFont=new Font();Typography.fontSize=16;Typography.color=Color.white;
            Transition=new(Environment.CurrentManagedThreadId,()=>Allowed,"transition",CardResourceKind.Transition);Sound=new(Environment.CurrentManagedThreadId,()=>Allowed,"sound",CardResourceKind.Sound);
            Draft=new(Environment.CurrentManagedThreadId,()=>Allowed,Choices,"transition","sound");Host=new(this);Workflow=new(Host);
        }
        internal IReadOnlyList<AuthoringCardPackResourceChoice> Choices()=>new[]{Transition.ReadChoice(),Sound.ReadChoice()}.Where(x=>x!=null).Cast<AuthoringCardPackResourceChoice>().ToArray();
        internal void Create(){View=new(Environment.CurrentManagedThreadId,Parent.transform,Typography,new Vector2(0,0),()=>Allowed,Workflow,Transition,Sound,Draft);View.Update(true);}
        internal void SearchPick(string prefix,string kind,string id)
        {
            AuthoringResourceCatalog.SearchResults.Enqueue(Page(kind,new[]{id}));Click(prefix+"Search");View!.Update(true);Click(prefix+"Candidate0");View.Update(true);
        }
        public void Dispose(){View?.Dispose();}
    }
    sealed class Host:ICardPackV2Host
    {
        readonly Fixture owner;internal int Captures,Commits,Recovers;internal bool RecoveryPending;
        internal Host(Fixture f){owner=f;}
        public CardPackV2Snapshot Capture(string? locale)
        {
            Captures++;if(RecoveryPending)throw new CardPackRecoveryRequiredException("fixture persistent uncertainty");
            var s=AuthoringEditorSession.Current!;var i=ScriptNodeInspector.instance!;
            var id=new CardPackContextIdentity(s.ProjectId,s.EditSession,s.Revision,AuthoringResourceCatalog.CatalogVersion,i.scriptNode.guid.ToString(),"anchor",i.selectedSlotNum);
            return new(new AuthoringCardPackHostContent(Environment.CurrentManagedThreadId,owner.Choices).Capture(id,locale),7);
        }
        public CardPackV2CommitResult Commit(CardPackNativePlan plan,long revision){Commits++;return new(CardPackV2CommitDisposition.Applied,"fixture applied");}
        public CardPackV2RecoveryResult Recover(){Recovers++;RecoveryPending=false;return new(true,"fixture recovered");}
    }
    static void With(Action<Fixture> body){using var f=new Fixture();f.Create();body(f);}
    static readonly (string Name,Action Test)[] Cases=
    {
        ("real owned controls preserve native typography and plain text",()=>With(f=>{foreach(string n in new[]{"Preview","Confirm","Cancel","Recover"})Check(Find<UIButton>(n).onClick.Count==1,"dedicated button callback missing: "+n);var labels=GameObject.All.Where(x=>x.name.StartsWith("AzuriteCard",StringComparison.Ordinal)&&!x.IsDestroyed).Select(x=>x.GetComponent<UILabel>()).Where(x=>x!=null).ToArray();Check(labels.Length>=15&&labels.All(x=>!x!.supportEncoding&&ReferenceEquals(x.trueTypeFont,f.Typography.trueTypeFont)),"owned labels lost font or enabled markup");Check(Find<UILabel>("PreviewText").maxLineCount==0&&Find<UIScrollView>("PreviewViewport").movement==UIScrollView.Movement.Vertical,"preview is not a multiline native scroll surface");})),
        ("construction and idle ticks do not search resolve or capture",()=>With(f=>{f.View!.Update(true);f.View.Update(true);Check(AuthoringResourceCatalog.Searches.Count==0&&AuthoringResourceCatalog.Calls.Count==0&&f.Host.Captures==0,"idle view performed native resource work");})),
        ("preview pointer surface stays outside movable scroll content",()=>With(f=>{var background=Find<UITexture>("PreviewBackground");var viewport=Find<UIScrollView>("PreviewViewport");Check(background.transform.parent!=viewport.transform&&background.gameObject.GetComponent<UIDragScrollView>()!.scrollView==viewport,"scrolling text would move its only pointer hit surface away");})),
        ("resource search defers rendering and requires explicit selection",()=>With(f=>{int created=GameObject.Added;Input("TransitionQuery","fade");AuthoringResourceCatalog.SearchResults.Enqueue(Page("transition",new[]{"fixture-transition"},"native-next"));Click("TransitionSearch");Check(f.Transition.ReadChoice()==null&&AuthoringResourceCatalog.Calls.Count==0,"search auto selected");Check(!Find<UIButton>("TransitionCandidate0").isEnabled,"candidate rendered inside callback");f.View!.Update(true);Check(Find<UIButton>("TransitionCandidate0").isEnabled&&GameObject.Added==created,"deferred candidate refresh rebuilt native objects");Click("TransitionCandidate0");Check(f.Transition.ReadChoice()!.ResourceId=="fixture-transition"&&AuthoringResourceCatalog.Calls.Count==1,"explicit candidate did not reach Resolve");Check(AuthoringResourceCatalog.Searches[0]==("transition","fade",(string?)null,3,"fixture-catalog"),"search did not use actual parameters");})),
        ("next page passes opaque native cursor and displays returned IDs",()=>With(f=>{AuthoringResourceCatalog.SearchResults.Enqueue(Page("transition",new[]{"fixture-transition"},"opaque-next"));Click("TransitionSearch");f.View!.Update(true);AuthoringResourceCatalog.SearchResults.Enqueue(Page("transition",new[]{"fixture-transition-two"}));Click("TransitionNext");f.View.Update(true);Check(AuthoringResourceCatalog.Searches.Last().Cursor=="opaque-next","cursor was fabricated");Check(Find<UILabel>("TransitionCandidate0Label").text.Contains("fixture-transition-two"),"new page hidden");})),
        ("resource changes immediately revoke visible preview without callback destruction",()=>With(f=>{f.SearchPick("Transition","transition","fixture-transition");Click("FamilyTransition");Click("Preview");Check(Find<UIButton>("Confirm").isEnabled,"valid preview missing");int destroyed=GameObject.Destroyed;Input("TransitionQuery","different");Check(!Find<UIButton>("Confirm").isEnabled&&GameObject.Destroyed==destroyed&&f.Host.Commits==0,"resource callback kept confirmation or destroyed controls");f.View!.Update(true);Check(!Find<UIButton>("Confirm").isEnabled,"Update reopened stale confirmation");})),
        ("catalog change clears visible selection on next update without search",()=>With(f=>{f.SearchPick("Transition","transition","fixture-transition");int searches=AuthoringResourceCatalog.Searches.Count;AuthoringResourceCatalog.CatalogVersion="new-version";f.View!.Update(true);Check(!Find<UIButton>("TransitionCandidate0").isEnabled&&Find<UILabel>("TransitionSelected").text=="尚未选择","catalog changed but stale choices remained visible");Check(AuthoringResourceCatalog.Searches.Count==searches,"context check searched automatically");})),
        ("disposal requested inside own input callback is deferred",()=>With(f=>{f.Transition.Changed+=_=>f.View!.Dispose();int destroyed=GameObject.Destroyed;Input("TransitionQuery","dispose now");Check(GameObject.Destroyed==destroyed,"disposed native controls inside input callback");f.View!.Update(true);Check(GameObject.All.Where(x=>x.name.StartsWith("AzuriteCard")).All(x=>x.IsDestroyed),"deferred owned cleanup did not finish");})),
        ("five-family example is labeled and compiles only after explicit resources",()=>With(f=>{f.SearchPick("Transition","transition","fixture-transition");f.SearchPick("Sound","sound","fixture-sound");Click("FamilyExample");Click("Preview");Check(Find<UIButton>("Confirm").isEnabled&&Find<UILabel>("PreviewText").text.Contains("五类内置示例"),"example preview missing clear labeling");})),
        ("only applicable template parameters remain editable",()=>With(f=>{Check(!Find<UIInput>("Action").enabled,"fixed five-family example exposed an ignored action editor");Click("FamilyAction");f.View!.Update(true);Check(Find<UIInput>("Action").enabled&&!Find<UIInput>("ChineseText").enabled,"unrelated language fields remained editable for action template");})),
        ("unused extra language fields do not block transition template",()=>With(f=>{f.SearchPick("Transition","transition","fixture-transition");Input("ExtraText","unfinished additional text");Click("FamilyTransition");Click("Preview");Check(Find<UIButton>("Confirm").isEnabled,"unused language input blocked transition preview");})),
        ("extra editable locale preserves exact literal user dialogue",()=>With(f=>{Click("FamilyLanguage");Input("ExtraLocale","ja");Input("ExtraText","[FF0000]先生、行きましょう。\n二行目");Input("SelectedLocale","ja");Click("Preview");Check(Find<UIButton>("Confirm").isEnabled&&Find<UILabel>("PreviewText").text.Contains("[FF0000]先生、行きましょう。\n二行目"),"extra locale missing or transformed");Check(!Find<UILabel>("PreviewText").supportEncoding,"dialogue markup enabled");})),
        ("editing dialogue revokes preview and requires new confirmation",()=>With(f=>{Click("FamilyLanguage");Click("Preview");Check(Find<UIButton>("Confirm").isEnabled,"language preview failed");Input("ChineseText","changed");Check(!Find<UIButton>("Confirm").isEnabled,"text edit retained token");Click("Confirm");Check(f.Host.Commits==0,"stale token committed");})),
        ("disabled view remains disabled after Panel.Update",()=>With(f=>{Click("FamilyLanguage");Click("Preview");f.Allowed=false;f.View!.Update(true);f.View.Update(true);Check(!Find<UIButton>("Preview").isEnabled&&!Find<UIButton>("Confirm").isEnabled&&!Find<UIButton>("TransitionSearch").isEnabled,"Panel.Update reopened disabled operation");})),
        ("Recover works with no resources and closed ordinary context",()=>{using var f=new Fixture();f.Host.RecoveryPending=true;var data=new CardPackV2Draft(CardPackV2Json.Serialize(new("2","recovery","Recovery",new[]{new CardV2("action",PresetCardKind.Action,TargetSlot:2,Action:"Greeting")})),null);f.Workflow.Preview(data);f.Create();f.Allowed=false;f.View!.Update(true);Check(Find<UIButton>("Recover").isEnabled,"Recover incorrectly needs draft resources");Click("Recover");Check(f.Host.Recovers==1&&!f.Workflow.RecoveryRequired&&f.Host.Commits==0,"Recover did not use preserved Workflow");}),
        ("actual selected slot is readonly and refreshed without model mutation",()=>With(f=>{ScriptNodeInspector.instance!.selectedSlotNum=4;f.View!.Update(true);Check(Find<UILabel>("TargetSlot").text.Contains("4")&&ScriptNodeInspector.instance.selectedSlotNum==4,"slot was synthesized or changed");})),
        ("Dispose removes owned callbacks then destroys only owned hierarchy",()=>{var f=new Fixture();f.Create();var buttons=GameObject.All.Where(x=>x.name.StartsWith("AzuriteCard")).Select(x=>x.GetComponent<UIButton>()).Where(x=>x!=null).ToArray();var inputs=GameObject.All.Where(x=>x.name.StartsWith("AzuriteCard")).Select(x=>x.GetComponent<UIInput>()).Where(x=>x!=null).ToArray();f.Dispose();Check(buttons.All(x=>x!.onClick.Count==0)&&inputs.All(x=>x!.onChange.Count==0&&x.onSubmit.Count==0),"owned delegates retained");Check(!f.Parent.IsDestroyed&&GameObject.All.Where(x=>x.name.StartsWith("AzuriteCard")).All(x=>x.IsDestroyed),"native parent destroyed or owned child leaked");}),
        ("partial construction failure cleans only owned objects",()=>{using var f=new Fixture();GameObject.FailOnAdd=GameObject.Added+12;Reject(()=>f.Create(),"partial creation did not fail");Check(!f.Parent.IsDestroyed&&GameObject.All.Where(x=>x.name.StartsWith("AzuriteCard")).All(x=>x.IsDestroyed),"partial construction leaked or deleted parent");}),
        ("wrong-thread construction creates no owned native objects",()=>{using var f=new Fixture();int thread=Environment.CurrentManagedThreadId;Task.Run(()=>Reject(()=>new AuthoringCardPackHostView(thread,f.Parent.transform,f.Typography,new(0,0),()=>true,f.Workflow,f.Transition,f.Sound,f.Draft),"wrong thread accepted")).GetAwaiter().GetResult();Check(!GameObject.All.Any(x=>x.name.StartsWith("AzuriteCard")),"wrong thread created controls");})
    };
    static int Main()
    {
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NativeSchemaResponse.json"));Check(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()=="b97d7bb5da9a86738a7d43eec79e5b26547bccb50d68400d92891741258f978d","schema fixture changed");Schema=JsonDocument.Parse(bytes).RootElement.GetProperty("data").GetRawText();int failed=0;
        foreach(var c in Cases)try{c.Test();Console.WriteLine("PASS "+c.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+c.Name+": "+e.Message);}
        Console.WriteLine($"RESULT {Cases.Length-failed} passed / {failed} failed");return failed==0?0:1;
    }
}
