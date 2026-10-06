using Azurite;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using Studio.Scripts.OperationManagement;
using UnityEngine;

var tests = new (string, Action)[] {
 ("failed startup selection refresh restores loading without rebuilding rows", SelectionLifecycleCases.FailedSelectionRefresh),
 ("late Start restores original tail owner, pane and preview without selecting twice", SelectionLifecycleCases.LoadBeforeStart),
 ("unpatched native lifecycle reproduces undeletable tail and two selected rows", SelectionLifecycleCases.NativeBaselineReproduces),
 ("Start before Load retains exactly one native selection", SelectionLifecycleCases.StartBeforeLoad),
 ("late Start does not resurrect changed, destroyed or deselected rows", SelectionLifecycleCases.InvalidatedDuringStart),
 ("failed Start does not replay selection into an incomplete inspector", SelectionLifecycleCases.FailedStart),
 ("pending row fill supports original tail and inserted row reselection and deletion", SelectionLifecycleCases.PendingEditsAndReselection),
 ("muted and nonmuted deselection retain verified native owner semantics", SelectionLifecycleCases.MutedDeselectionSemantics),
 ("tail row reaches the fixed camera viewport, not just the moved clip", TailWorldVisibility),
 ("NGUI scrollbar-only phase cannot substitute for content movement", ScrollPhaseSemantics),
 ("not-ready native scroll is bottom anchored when script Load returns", DeferredNativeCenter),
 ("native Load selects and shows last dialogue for continued writing", OpenAtLast),
 ("small scripts open at last row while retaining native list", SmallOpenAtLast),
 ("reopening a node does not materialize its old sparse rows", ReopenBounded),
 ("application quit does not initialize or refresh missing rows", ShutdownDoesNotMaterialize),
 ("node load reports total and row binding durations", LoadDiagnostics),
 ("initial native Load returns before all visible rows are bound", IncrementalInitial),
 ("visible rows fill bottom-to-top before offscreen overscan", BottomUpOrder),
 ("all same-frame hooks share the two-row fill budget", SharedFrameBudget),
 ("slow native row consumes time budget without a second row", SlowRowBudget),
 ("scroll cancels pending old viewport and keeps bindings correct", RapidPendingScroll),
 ("insert delete and restore stay usable while visible rows are pending", PendingMutations),
 ("selected visible row stays immediately editable while others load", PendingSelection),
 ("newly bound rows request an NGUI panel rebuild", PresentationRefresh),
 ("nested scroll notification cannot refill inside Init", NestedScroll),
 ("dense fallback refreshes pending text and preserves logical order", PendingDenseText),
 ("unload while fill pending leaves no deferred writes", PendingUnload),
 ("prefab child becomes available only after native Init", NativeChildLifetime),
 ("640 row Load bounds real objects and text refreshes", () => Initial(640)),
 ("5000 row Load bounds real objects and text refreshes", () => Initial(5000)),
 ("native baseline proves regression harness detects full creation", Baseline),
 ("restore selection inlined by Load pins index 4999", RestoreOnLoad),
 ("scroll all 5000 indices reuses bounded pool and correct text", ScrollAll),
 ("selected offscreen object is never rebound", SelectedIdentity),
 ("insert first, middle, last keeps native target selectable", Inserts),
 ("delete first, middle, last keeps previous target selectable", Deletes),
 ("one hundred mutations preserve data and avoid full row refresh", RepeatedEdits),
 ("standalone sync refreshes changed visible content", ChangedText),
 ("standalone sync keepSelected retains object and index", KeepSelected),
 ("restore arbitrary status materializes target on demand", RestoreStatus),
 ("full scroll extent survives pool recycling", Extent),
 ("drag restores dense list without destroying original dragged object", Drag),
 ("reorder callbacks see dense native cache", Reorder),
 ("undo and redo see dense native cache", UndoRedo),
 ("audited Capture Save APIs keep sparse presentation; Apply Restore stay dense", Authoring),
 ("export suspension restores dense list", Suspend),
 ("native unload drops row pool without full-list construction", Unload),
 ("host unsupported geometry retains native path", Unsupported),
 ("hook failure removes partial hooks", FailedHook),
 ("runtime text failure restores full list and disables experiment", BindFailure),
 ("selected offscreen identity survives bind failure and dense recovery", SelectedBindFailure),
 ("viewport pool bound falls back safely", OversizedViewport),
 ("disabled experiment keeps native code", Disabled),
};
int failed=0;
foreach(var(name,run) in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}
Console.WriteLine($"RESULT {tests.Length-failed}/{tests.Length}; actual production hooks linked with verified-call-chain stubs; not Unity timing or rendered-image validation.");
return failed==0?0:1;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void ScrollPhaseSemantics()
{
 using var f=new Fixture(640,enabled:false);
 var scroll=f.Host.scriptListScroll;
 float before=scroll.transform.localPosition.y;
 scroll.SetDragAmount(0,1,true);
 Check(scroll.transform.localPosition.y==before,"true must not translate the content transform");
 scroll.panel.finalClipRegion=new Vector4(300,-400,600,800);
 scroll.SetDragAmount(0,1,false);scroll.SetDragAmount(0,1,true);
 Check(scroll.transform.localPosition.y>60000,"native false phase must physically move the content");
}
static void TailWorldVisibility()
{
 foreach(int count in new[]{12,640,5000})
 {
  using var f=new Fixture(count,load:false);
  f.Host.scriptListScroll.transform.localPosition=new Vector3(35,180,0);
  var scroll=f.Host.scriptListScroll;
  var initial=scroll.panel.finalClipRegion;
  var viewportCenter=scroll.panel.transform.TransformPoint(new Vector3(initial.x,initial.y,0));
  f.Load();f.Drain();
  for(int frame=0;frame<8;frame++)
  {
   var clip=scroll.panel.finalClipRegion;
   var actualCenter=scroll.panel.transform.TransformPoint(new Vector3(clip.x,clip.y,0));
   Check(Math.Abs(actualCenter.y-viewportCenter.y)<.1,"clip moved off camera while claiming tail visible; world Y="+actualCenter.y);
   var row=f.Host.scriptNodeListItemsCache[count-1];
   var center=row.transform.TransformPoint(new Vector3(300,-50,0));
   Check(center.y>=viewportCenter.y-initial.w*.5f&&center.y<=viewportCenter.y+initial.w*.5f,"selected tail is outside camera viewport");
   Check(f.Host.selectedScriptItem==row,"selected tail identity changed");
   f.Tick();
  }
 }
}
static void DeferredNativeCenter(){using var f=new Fixture(640,load:false);f.Host.scriptListScroll.ready=false;f.Load();var clip=f.Host.scriptListScroll.panel.finalClipRegion;float expected=f.Host.scriptListScroll.bounds.min.y;Check(Math.Abs(clip.y-clip.w*.5f-expected)<.1,$"actual viewport bottom must equal logical bottom even before Centerable ready (clipY={clip.y} w={clip.w} min={expected} max={f.Host.scriptListScroll.bounds.max.y} calc={f.Host.scriptListScroll.mCalculatedBounds} logs={string.Join("|",f.Logs)})");Check(f.Host.scriptListScroll.centeringChild==null,"tail anchor clears stale deferred center target");
var tail=f.Host.selectedScriptItem!.transform;f.Host.scriptListScroll.Deferred.Add(()=>f.Host.scriptListScroll.CenterOn(tail,false,true));Time.frameCount++;f.Host.scriptListScroll.ready=true;f.Host.scriptListScroll.RunDeferred();clip=f.Host.scriptListScroll.panel.finalClipRegion;Check(-639*110>=clip.y-clip.w*.5f&&-639*110<=clip.y+clip.w*.5f,"deferred native tail CenterOn must not spring back from the opening anchor");Check(f.Host.scriptListScroll.centeringChild==null,"suppressed deferred callback must clear center target");var first=f.Host.scriptNodeListItemsCache[0];Check(first!=null,"first row remains available as a user selection target");f.Host.scriptListScroll.CenterOn(first.transform,false,true);Check(f.Host.scriptListScroll.centeringChild==first.transform,"post-open user CenterOn remains native");}
static void OpenAtLast(){foreach(int saved in new[]{-1,0,123}){using var f=new Fixture(640,load:false,restored:saved);var data=f.Data();f.Load();Check(f.Host.selectedScriptItem?.index==639,"open must select final row before native Load returns");var clip=f.Host.scriptListScroll.panel.finalClipRegion;Check(-639*110>=clip.y-clip.w*.5f&&-639*110<=clip.y+clip.w*.5f,"last row visible immediately");Check(f.Data().SequenceEqual(data),"opening at tail must preserve script order");Check(f.Mod.HasPendingRows,"other rows continue filling after editor returns");}}
static void SmallOpenAtLast(){foreach(int count in new[]{0,1,12,31}){using var f=new Fixture(count,load:false);var data=f.Data();f.Load();Check(!f.Mod.IsActive,"small list construction stays native");Check(f.Host.selectedScriptItem?.index==(count==0?(int?)null:count-1),"small-node tail selected by native Load");Check(f.Data().SequenceEqual(data),"small-node model unchanged");HostStats.AssertDense(f.Host);if(count>0){Check(f.Host.scriptListScroll.FirstOnTopCalls==0,"no deferred top jump");var clip=f.Host.scriptListScroll.panel.finalClipRegion;var bounds=f.Host.scriptListScroll.bounds;if (bounds.size.y>clip.w) Check(Math.Abs(clip.y-clip.w*.5f-bounds.min.y)<.1,$"small tail is bottom anchored count={count} clip={clip.y} min={bounds.min.y} max={bounds.max.y}");}Check(HostStats.Selections==(count==0?0:1),"one native selection only; no extra history event");}}
static void ReopenBounded(){using var f=new Fixture(640);int created=HostStats.Created;f.Load();Check(HostStats.Created-created<24,"node reentry must not recreate missing640 rows just to destroy them");f.Drain();f.Assert();}
static void IncrementalInitial(){using var f=new Fixture(640,load:false);f.Load();Check(HostStats.Inits<=5,"initial Load binds two calibration rows, immediate tail and at most two deferred rows; actual="+HostStats.Inits);Check(f.Host.scriptNodeListItemsCache[633]==null,"upper visible row waits for next frame while inspector is usable");Check(!f.Host.loading,"Load returns immediately");}
static void BottomUpOrder(){using var f=new Fixture(640,load:false);var data=f.Data();f.Load();f.Drain();int[] presentation=HostStats.InitIndices.Skip(2).ToArray();Check(presentation.SequenceEqual(Enumerable.Range(629,11).Reverse()),"tail visible indices must descend before overscan: "+string.Join(",",presentation)+" clip="+f.Host.scriptListScroll.panel.finalClipRegion.y+" bounds="+f.Host.scriptListScroll.bounds.min.y+" logs="+string.Join("|",f.Logs));Check(f.Data().SequenceEqual(data),"model order unchanged");Check(f.Host.scriptList.transform.Children[0].gameObject.Row.index==0,"FirstOnTop anchor remains first physical child");f.Assert();}
static void SharedFrameBudget(){using var f=new Fixture(5000,load:false);f.Load();for(int frame=0;frame<12;frame++){Time.frameCount++;int before=HostStats.Inits;for(int call=0;call<10;call++){f.Mod.Update(Time.frameCount/60d,true);f.Host.scriptListScroll.LateUpdate();f.Scroll(0,false);}Check(HostStats.Inits-before<=2,"hooks cannot each spend a fresh budget");}f.Drain();f.Assert();}
static void SlowRowBudget(){using var f=new Fixture(640);f.Scroll(400,false);Time.frameCount++;int before=HostStats.Inits;HostStats.AfterRefresh=()=>Thread.Sleep(12);f.Mod.Update(2,true);f.Host.scriptListScroll.LateUpdate();Check(HostStats.Inits-before==1,"after one slow Init budget must stop remaining rows");f.Drain();f.Assert();}
static void RapidPendingScroll(){using var f=new Fixture(5000,load:false);f.Load();int start=HostStats.InitIndices.Count;f.Scroll(4000,false);f.Tick();Check(HostStats.InitIndices.Skip(start).All(i=>i>=3997&&i<=4011),"no stale top-window queue");f.Scroll(2000,false);start=HostStats.InitIndices.Count;f.Tick();Check(HostStats.InitIndices.Skip(start).All(i=>i>=1997&&i<=2011),"new viewport supersedes earlier work");for(int n=0;n<50;n++){f.Scroll(n%2==0?4990:0,false);f.Tick();f.Assert();}f.Drain();Check(f.Mod.LiveRows<32,"rapid scrolling does not grow hidden pool");f.Assert();}
static void PendingMutations(){using var f=new Fixture(640,load:false);f.Load();Check(f.Mod.HasPendingRows,"test starts in progressive fill");f.Host.InsertScript(320);Check(f.Host.selectedScriptItem?.index==320,"native insertion target available synchronously");f.Host.DeleteScript();Check(f.Host.selectedScriptItem?.index==319,"native deletion target available synchronously");f.Host.RestoreStatus(new ScriptNode.ScriptNodeInspectorInfo{lastScriptIndex=600});Check(f.Host.selectedScriptItem?.index==600,"restore immediate during exhausted budget");f.Assert();f.Drain();f.Assert();}
static void PendingSelection(){using var f=new Fixture(640,load:false);f.Load();var row=f.Host.scriptNodeListItemsCache[639];Check(row!=null,"last visible row is interactive immediately");row.Select();f.Host.scriptNode.scripts[639].text="edited during loading";f.Host.SyncScriptList(false,true,false);Check(f.Host.selectedScriptItem==row&&row.scriptPhonetic.Text=="edited during loading","selected row refresh cannot wait for budget");f.Scroll(0,false);f.Tick();Check(f.Host.selectedScriptItem==row&&f.Host.scriptNodeListItemsCache[639]==row,"selected row pinned outside viewport");f.Assert();}
static void PresentationRefresh(){using var f=new Fixture(640,load:false);f.Load();Check(HostStats.PanelUpdates>0,"binding virtual rows must dirty their nested panel and viewport panel");f.Drain();Check(HostStats.PanelUpdates>1,"deferred visible rows must also refresh their panel");}
static void NestedScroll(){using var f=new Fixture(640);f.Scroll(400,false);Time.frameCount++;int before=HostStats.Inits;HostStats.AfterRefresh=()=>f.Host.scriptListScroll.LateUpdate();f.Mod.Update(2,true);Check(HostStats.Inits-before<=2,"nested native callback must not create recursive fill");f.Drain();f.Assert();}
static void PendingDenseText(){using var f=new Fixture(640);f.Scroll(0);var pending=f.Host.scriptNodeListItemsCache[3];f.Host.scriptNode.scripts[3].text="new before save";f.Host.SyncScriptList(false,false,false);Check(f.Mod.HasPendingRows,"dirty work pending");f.Mod.Suspend();HostStats.AssertDense(f.Host);Check(f.Host.scriptNodeListItemsCache[3]==pending&&pending.scriptPhonetic.Text=="new before save","dense consumer gets refreshed text without replacing bound row");for(int i=0;i<640;i++)Check(f.Host.scriptList.transform.Children[i].gameObject.Row.index==i,"dense child order");}
static void PendingUnload(){using var f=new Fixture(640,load:false);f.Load();int bound=HostStats.Inits;f.Host.Unload();Check(!f.Mod.HasPendingRows&&!f.Mod.IsActive,"unload releases presentation synchronously");f.Host.isActiveAndEnabled=false;f.Tick();f.Tick();Check(HostStats.Inits==bound,"no pending batch writes after unload");Check(f.Host.scriptNodeListItemsCache.Count==0,"inactive inspector has no stale rows");}
static void ShutdownDoesNotMaterialize(){using var f=new Fixture(640,load:false);f.Load();int created=HostStats.Created,inits=HostStats.Inits,refresh=HostStats.Refreshes;HostStats.Failure="Refresh";f.Mod.AbandonForShutdown();f.Tick();f.Mod.Dispose();Check(HostStats.Created==created&&HostStats.Inits==inits&&HostStats.Refreshes==refresh,"quit must not touch localized row widgets");Check(!f.Mod.IsActive&&!f.Mod.HasPendingRows,"shutdown drops scheduling references");HostStats.Failure=null;}
static void LoadDiagnostics(){using var f=new Fixture(640);Check(f.Logs.Any(s=>s.Contains("dialogue-open totalMs=")&&s.Contains("rowSyncMs=")&&s.Contains("tail=639")&&s.Contains("success=True")),"load duration and tail are observable");}
static void NativeChildLifetime(){using var f=new Fixture(640,load:false);var row=new ScriptListItem();Check(row.child==null,"prefab runtime child starts empty");row.Init(f.Host.scriptNode,0,f.Host);Check(row.child!=null,"native Init binds child");f.Load();Check(f.Mod.IsActive,"virtualization must accept native deferred child binding");Check(HostStats.Created<24,"no full-list fallback");f.Assert();}
static void Initial(int count){using var f=new Fixture(count);Check(f.Mod.IsActive,"virtual mode");Check(HostStats.Created<24,"bounded initial creation");Check(HostStats.Refreshes<40,"bounded initial layouts");Check(f.Host.scriptNodeListItemsCache.Count==count,"logical length");f.Assert();}
static void Baseline(){using var f=new Fixture(640,false);Check(!f.Mod.IsActive&&HostStats.Created==640,"baseline actual full native creation");}
static void RestoreOnLoad(){using var f=new Fixture(5000,restored:4999);Check(f.Host.selectedScriptItem?.index==4999,"inlined native restore");f.Assert();}
static void ScrollAll(){using var f=new Fixture(5000);for(int i=0;i<5000;i+=7){f.Scroll(i);f.Assert();Check(f.Host.scriptNodeListItemsCache[i]!=null,"visible index populated");}Check(f.Mod.LiveRows<32,"scroll pool bounded");Check(HostStats.Created<32,"no unbounded instantiation");}
static void SelectedIdentity(){using var f=new Fixture(640);f.Scroll(0);var row=f.Host.scriptNodeListItemsCache[3];row.Select();IntPtr data=f.Host.scriptNode.scripts[3].Pointer;f.Scroll(600);Check(f.Host.selectedScriptItem==row&&row.index==3&&f.Host.scriptNode.scripts[row.index].Pointer==data,"selected identity stable");Check(f.Host.scriptNodeListItemsCache[3]==row,"pinned cache");f.Assert();}
static void Inserts(){foreach(int index in new[]{0,320,640}){using var f=new Fixture(640);var before=f.Data();int refreshed=HostStats.Refreshes;f.Host.InsertScript(index);Check(f.Host.selectedScriptItem?.index==index,"native insert selection");Check(HostStats.Refreshes-refreshed<40,"bounded insert refresh");for(int i=0;i<before.Length;i++)Check(f.Host.scriptNode.scripts[i<index?i:i+1].Pointer==before[i],"unchanged model identity");f.Assert();}}
static void Deletes(){foreach(int index in new[]{0,320,639}){using var f=new Fixture(640);f.Scroll(index);f.Host.scriptNodeListItemsCache[index].Select();var before=f.Data();int refreshed=HostStats.Refreshes;f.Host.DeleteScript();Check(index==0?f.Host.selectedScriptItem==null:f.Host.selectedScriptItem?.index==index-1,"native delete selection");Check(HostStats.Refreshes-refreshed<40,"bounded delete refresh");for(int i=0;i<639;i++)Check(f.Host.scriptNode.scripts[i].Pointer==before[i<index?i:i+1],"unchanged data identity");f.Assert();}}
static void RepeatedEdits(){using var f=new Fixture(5000);for(int n=0;n<100;n++){int at=(n*47)%f.Host.scriptNode.scripts.Count;f.Host.InsertScript(at);f.Host.DeleteScript();f.Assert();}Check(f.Host.scriptNode.scripts.Count==5000,"no data loss");Check(f.Mod.LiveRows<40,"bounded mutations");}
static void ChangedText(){using var f=new Fixture(640);f.Scroll(0);f.Host.scriptNode.scripts[3].text="changed";f.Host.SyncScriptList(false,false,false);f.Drain();Check(f.Host.scriptNodeListItemsCache[3].scriptPhonetic.Text=="changed","text invalidation");}
static void KeepSelected(){using var f=new Fixture(640);f.Scroll(0);var row=f.Host.scriptNodeListItemsCache[3];row.Select();f.Scroll(620);f.Host.SyncScriptList(false,true,false);Check(f.Host.selectedScriptItem==row&&row.index==3,"keep selection");f.Assert();}
static void RestoreStatus(){using var f=new Fixture(640);f.Host.RestoreStatus(new ScriptNode.ScriptNodeInspectorInfo{lastScriptIndex=500});Check(f.Host.selectedScriptItem?.index==500,"restore target");f.Host.scriptNode.inspectorInfo.lastScriptIndex=400;f.Host.RestoreStatus();Check(f.Host.selectedScriptItem?.index==400,"private restore target");f.Assert();}
static void Extent(){using var f=new Fixture(5000);float height=f.Host.scriptListScroll.bounds.size.y;Check(Math.Abs(height-(4999*110+100))<.1,"logical height exact");f.Scroll(4000);Check(Math.Abs(f.Host.scriptListScroll.bounds.size.y-height)<.1,"not reduced to pooled rows");}
static void Drag(){using var f=new Fixture(640);f.Scroll(0);var row=f.Host.scriptNodeListItemsCache[2];row.Select();row.OnDraggerDragStart();Check(!f.Mod.IsActive,"dense on drag");Check(f.Host.scriptNodeListItemsCache[2]==row&&f.Host.selectedScriptItem==row,"drag object survives");HostStats.AssertDense(f.Host);}
static void Reorder(){foreach(string name in new[]{"RearrangeScriptList","ValidateNewArrangement","UpdateScriptListCache","Method_Private_Void_0"}){using var f=new Fixture(640);typeof(ScriptNodeInspector).GetMethod(name)!.Invoke(f.Host,null);Check(!f.Mod.IsActive,"native dense operation");}}
static void UndoRedo(){foreach(var op in new ScenarioScriptAddOperation[]{new(),new ScenarioScriptDeleteOperation(),new ScenarioScriptInspectorModifyOperation(),new ScriptNodeInspectorStateChangeOperation(),new ScriptNodeRearrangeOperation()})foreach(bool undo in new[]{true,false}){using var f=new Fixture(640);ScenarioScriptAddOperation.Current=f.Host;if(undo)op.Undo();else op.Redo();Check(!f.Mod.IsActive,"history dense");}}
static void Authoring(){foreach(string name in new[]{"Capture","Save","BeginSave","CheckSave","CompleteSave","ReleaseSave","Apply","Restore"}){using var f=new Fixture(640);var api=new AzureArchive.Automation.AuthoringEditorSession{Owner=f.Host};var method=api.GetType().GetMethod(name)!;var args=method.GetParameters().Select(p=>p.ParameterType==typeof(bool)?(object)false:p.ParameterType==typeof(string)?"1":new AzureArchive.Automation.AuthoringSaveOperation()).ToArray();int created=HostStats.Created;method.Invoke(api,args);bool dense=name is "Apply" or "Restore";Check(f.Mod.IsActive!=dense,"audited authoring ownership: "+name);if(!dense)Check(HostStats.Created==created,"model-only authoring must not construct rows");}}
static void Suspend(){using var f=new Fixture(640);f.Mod.Suspend();HostStats.AssertDense(f.Host);Check(!f.Mod.IsActive,"export dense");}
static void Unload(){using var f=new Fixture(640);var data=f.Data();int created=HostStats.Created;f.Host.Unload();Check(!f.Mod.IsActive&&HostStats.Created==created,"unload must not construct rows");Check(f.Host.selectedScriptItem==null&&f.Host.scriptNodeListItemsCache.Count==0,"selected native deselection completes before pool release");Check(f.Data().SequenceEqual(data),"unload does not mutate script order");}
static void Unsupported(){using var f=new Fixture(640,load:false);f.Host.scriptList.pivot=UIWidget.Pivot.Center;f.Load();Check(!f.Mod.IsActive&&HostStats.Created==640,"geometry fallback");}
static void FailedHook(){HarmonyLib.Harmony.RejectMethod="SetDragAmount";try{using var f=new Fixture(640,load:false);Check(!f.Mod.IsInstalled,"install fails closed");Check(HarmonyLib.Harmony.Patches.Count==0,"partial hooks removed");}finally{HarmonyLib.Harmony.RejectMethod=null;}}
static void BindFailure(){using var f=new Fixture(640);HostStats.Failure="Refresh";f.Scroll(400);Check(!f.Mod.IsActive,"failed session dense");HostStats.AssertDense(f.Host);}
static void SelectedBindFailure(){using var f=new Fixture(640);f.Scroll(600);var selected=f.Host.scriptNodeListItemsCache[600];selected.Select();HostStats.Failure="Refresh";f.Scroll(0);Check(!f.Mod.IsActive,"failed session dense");Check(f.Host.selectedScriptItem==selected,"selected object retained");Check(!selected.gameObject.Destroyed&&selected.gameObject.activeInHierarchy&&selected.index==600,"selected row remains live");Check(f.Host.scriptNodeListItemsCache[600]==selected,"selected cache identity retained");HostStats.AssertDense(f.Host);}
static void OversizedViewport(){using var f=new Fixture(640);f.Host.scriptListScroll.panel.finalClipRegion=new(300,-10000,600,30000);f.Mod.Update(2,true);Check(!f.Mod.IsActive,"pool overflow native recovery");HostStats.AssertDense(f.Host);}
static void Disabled(){using var f=new Fixture(640,false);Check(!f.Mod.IsActive,"disabled");}

sealed class Fixture:IDisposable{
 public ScriptNodeInspector Host=new(); public DialogueVirtualization Mod; public List<string> Logs=new(); private ScriptNode _node;
 public Fixture(int count,bool enabled=true,bool load=true,int restored=-1){HostStats.Reset();_node=Host.scriptNode;for(int i=0;i<count;i++)_node.scripts.Add(new Script{text="text "+i});_node.inspectorInfo.lastScriptIndex=restored;Mod=new DialogueVirtualization(Logs.Add){Enabled=enabled};Mod.Install();Mod.Update(0,true);if(load){Load();Drain();}}
 public void Load()=>Host.Load(_node);
 public void Scroll(int index,bool settle=true){var scroll=Host.scriptListScroll;float range=Math.Max(1,scroll.bounds.size.y-scroll.panel.finalClipRegion.w);float y=Math.Clamp(index*110f/range,0,1);scroll.SetDragAmount(0,y,false);scroll.SetDragAmount(0,y,true);if(settle)Drain();}
 public void Tick(){Time.frameCount++;Mod.Update(Time.frameCount/60d,true);Host.scriptListScroll.LateUpdate();}
 public void Drain(){for(int n=0;n<128&&Mod.HasPendingRows;n++)Tick();Check(!Mod.HasPendingRows,"bounded pending fill completes");}
 public IntPtr[] Data()=>Enumerable.Range(0,Host.scriptNode.scripts.Count).Select(i=>Host.scriptNode.scripts[i].Pointer).ToArray();
 public void Assert(){Check(Host.scriptNodeListItemsCache.Count==Host.scriptNode.scripts.Count,"count");var seen=new HashSet<IntPtr>();int live=0;for(int i=0;i<Host.scriptNodeListItemsCache.Count;i++){var row=Host.scriptNodeListItemsCache[i];if(row==null)continue;live++;Check(seen.Add(row.Pointer),"no row alias");Check(row.index==i&&row.scriptNode==Host.scriptNode&&row.inspector==Host,"binding");Check(row.scriptPhonetic.Text==Host.scriptNode.scripts[i].text,"text");Check(row.gameObject.activeInHierarchy,"active");Check(Math.Abs(row.transform.localPosition.y+i*110)<.1,"absolute logical position");}Check(live<128,"bounded active rows");}
 public void Dispose(){Mod.Dispose();}
 private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
