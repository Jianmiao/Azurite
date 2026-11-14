using System.Reflection;
using Azurite;
using Azurite.Core;
using Studio.Scripts;
using Studio.Scripts.Window;
using UnityEngine;
using UInput=UnityEngine.Input;

static partial class Program
{
    static readonly List<object> SelectionMeasurements=new();
    static readonly BindingFlags SelectionFlags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
    static void ResetPointer()
    {
        Application.isFocused=true;UICamera.inputHasFocus=UICamera.isDragging=false;UICamera.hoveredObject=null;
        UIInput.current=UIInput.selection=null;UIInput.mLastIME="";UInput.Down=UInput.Held=UInput.Up=false;UInput.Keys.Clear();
        Singleton<WindowManager>.Instance=new();Time.frameCount++;
    }
    static AuthoringCardPackHostSelection ActualSelection(RootViewFixture f)=>(AuthoringCardPackHostSelection)typeof(AuthoringCardPackHostRoot).GetField("_selection",SelectionFlags)!.GetValue(f.Root)!;
    static object? SelectionHook(string method, params object?[] args)=>typeof(AuthoringCardPackHostSelection).GetMethod(method,SelectionFlags)!.Invoke(null,args);
    static void PointerFrame(RootViewFixture f,int row=1,bool down=false,bool held=false,bool up=false,int x=10)
    {
        Time.frameCount++;UInput.mousePosition=new(x,Screen.height-(row*20+5),0);UInput.Down=down;UInput.Held=held;UInput.Up=up;f.Root.Update(true);
    }
    static void ListClick(RootViewFixture f,int row,bool ctrl=false)
    {
        UICamera.hoveredObject=ScriptNodeInspector.instance!.scriptNodeListItemsCache[row].gameObject;
        UInput.Keys.Clear();if(ctrl)UInput.Keys.Add(KeyCode.LeftControl);
        PointerFrame(f,row,true,true);PointerFrame(f,row,up:true);UInput.Keys.Clear();UInput.Up=false;
    }
    static int[] ActualSelected(RootViewFixture f)=>f.Host.Rows().Select(row=>row.index).ToArray();
    static void SelectOuterRows(RootViewFixture f){ListClick(f,0);ListClick(f,2,true);Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"multi-selection setup failed");}
    static void OwnedPointer(RootViewFixture f,GameObject owned,bool execute=false)
    {
        UICamera.hoveredObject=owned;PointerFrame(f,1,true,true);PointerFrame(f,1,up:true);UInput.Up=false;
        if(execute)owned.GetComponent<UIButton>()?.Click();
    }
    static readonly (string Name,Action Test)[] SelectionSurfaceCases=
    {
        ("full backplates have lower depth than every same-panel interactive hit surface",()=>
        {
            using var f=new RootViewFixture(3);
            foreach(string prefix in new[]{"AzuriteBatch","AzuriteCard"})
            {
                if(prefix=="AzuriteBatch")f.OpenBatch();else f.OpenCard();
                var background=GameObject.All.Single(x=>x.name==prefix+"Background"&&!x.IsDestroyed).GetComponent<UITexture>()!;
                var panel=background.gameObject.GetComponentInParent<UIPanel>()!;
                var view=background.transform.parent!;
                bool WithinView(GameObject obj){for(var t=obj.transform;t!=null;t=t.parent)if(ReferenceEquals(t,view))return true;return false;}
                var hits=GameObject.All.Where(obj=>obj.HasCollider&&!obj.IsDestroyed&&obj.Pointer!=background.gameObject.Pointer&&WithinView(obj)&&
                    obj.GetComponentInParent<UIPanel>()?.Pointer==panel.Pointer).Select(obj=>obj.GetComponent<UIWidget>()!).ToArray();
                Check(background.gameObject.HasCollider&&hits.Length>=20,"actual widget surfaces missing");
                Check(hits.Any(x=>x.gameObject.name==prefix+"Preview")&&hits.Any(x=>x.gameObject.name==prefix+"Confirm")&&
                    hits.Any(x=>x.gameObject.GetComponent<UIInput>()!=null)&&hits.Any(x=>x.gameObject.name==prefix+"PreviewBackground"),"required interaction surfaces were not measured");
                Check(hits.All(x=>x.depth>background.depth),prefix+" full background ties or covers an interactive collider");
                SelectionMeasurements.Add(new{caseName="constructed_collider_priority",surface=prefix,backgroundDepth=background.depth,
                    minimumInteractiveDepth=hits.Min(x=>x.depth),interactiveHitSurfaces=hits.Length,nativeRaycastExecuted=false});
            }
        }),
        ("real adapter preserves multi-selection on actual owned batch controls",()=>
        {
            using var f=new RootViewFixture(3);SelectOuterRows(f);f.OpenBatch();
            foreach(string suffix in new[]{"ActionSpeaker","Slot3","Preview"})
            {
                OwnedPointer(f,BatchFind<UIButton>(suffix).gameObject,true);
                Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"owned batch "+suffix+" changed selected rows");
            }
            Check(BatchFind<UIButton>("Confirm").isEnabled,"owned capture lost preview");
            OwnedPointer(f,BatchFind<UIButton>("Confirm").gameObject,true);
            Check(ActualSelected(f).SequenceEqual(new[]{0,2})&&f.Host.BatchCommits==1,"confirm changed selection or failed to commit");
            SelectionMeasurements.Add(new {caseName="actual_root_adapter_batch_controls",before=new[]{0,2},after=ActualSelected(f),commits=f.Host.BatchCommits});
        }),
        ("real adapter isolates both launchers and actual card control ancestry",()=>
        {
            using var f=new RootViewFixture(3);SelectOuterRows(f);
            var launcher=GameObject.All.Single(x=>x.name=="AzuriteCardPackLauncher"&&!x.IsDestroyed);OwnedPointer(f,launcher,true);f.Root.Update(true);
            OwnedPointer(f,Find<UILabel>("FamilyLanguageLabel").gameObject);Click("FamilyLanguage");
            OwnedPointer(f,Find<UIButton>("Preview").gameObject,true);
            Check(ActualSelected(f).SequenceEqual(new[]{0,2})&&Find<UIButton>("Confirm").isEnabled,"card surface leaked pointer");
            var batch=GameObject.All.Single(x=>x.name=="AzuriteBatchLauncher"&&!x.IsDestroyed);OwnedPointer(f,batch,true);f.Root.Update(true);
            Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"batch launcher changed selection");
        }),
        ("owned preview scrolling and gesture release outside retain selection",()=>
        {
            using var f=new RootViewFixture(3);SelectOuterRows(f);f.OpenBatch();
            UICamera.hoveredObject=BatchFind<UITexture>("PreviewBackground").gameObject;PointerFrame(f,1,true,true);
            Check((bool)SelectionHook("BeforeDialogueScrollDrag",BatchFind<UITexture>("PreviewBackground").gameObject.GetComponent<UIDragScrollView>()!,new Vector2(0,5))!,"owned scrolling was intercepted");
            UICamera.hoveredObject=ScriptNodeInspector.instance!.scriptNodeListItemsCache[1].gameObject;PointerFrame(f,2,held:true,x:80);PointerFrame(f,1,up:true);
            Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"owned drag/release selected underneath");
            Check(!(bool)SelectionHook("BeforeRowClick",ScriptNodeInspector.instance.scriptNodeListItemsCache[1])!,"owned release let underlying native row callback through");
        }),
        ("native row UIButton click Ctrl box and drag remain available",()=>
        {
            using var f=new RootViewFixture(3);f.OpenBatch();ListClick(f,1);Check(ActualSelected(f).SequenceEqual(new[]{1}),"all UIButton controls were blocked");
            ListClick(f,2,true);Check(ActualSelected(f).SequenceEqual(new[]{1,2}),"native Ctrl selection lost");
            UICamera.hoveredObject=ScriptNodeInspector.instance!.scriptNodeListItemsCache[0].gameObject;
            PointerFrame(f,0,true,true);PointerFrame(f,2,held:true,x:80);PointerFrame(f,2,up:true,x:80);
            Check(ActualSelected(f).SequenceEqual(new[]{0,1,2}),"native box selection lost");
            SelectionHook("BeforeNativeRowDrag",ScriptNodeInspector.instance.scriptNodeListItemsCache[1]);Check(ActualSelected(f).Length==0,"native reorder drag was blocked");
        }),
        ("native input negative control and owned blank surfaces preserve selection",()=>
        {
            using var f=new RootViewFixture(3);SelectOuterRows(f);f.OpenBatch();
            OwnedPointer(f,BatchFind<UIInput>("Text").gameObject);Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"UIInput lost selection");
            Check(BatchFind<UITexture>("Background").gameObject.HasCollider,"blank batch surface is not an NGUI hit target");
            OwnedPointer(f,BatchFind<UITexture>("Background").gameObject);f.OpenCard();
            Check(Find<UITexture>("Background").gameObject.HasCollider,"blank card surface is not an NGUI hit target");
            OwnedPointer(f,Find<UITexture>("Background").gameObject);Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"blank owned surface leaked input");
        }),
        ("actual Root idle stamp does not rescan 5000 rows closed or pending",()=>
        {
            using var f=new RootViewFixture(5000);ResetPointer();f.Root.Update(true);UIWidget.CornerReads=0;
            for(int i=0;i<5;i++)PointerFrame(f);
            int closed=UIWidget.CornerReads;Check(closed<=30,"closed Root performed full-row projection: "+closed);
            f.OpenBatch();BatchClick("Preview");Check(BatchFind<UIButton>("Confirm").isEnabled,"preview setup failed");ResetPointer();UIWidget.CornerReads=0;
            for(int i=0;i<5;i++)PointerFrame(f);
            int pending=UIWidget.CornerReads;Check(pending<=30&&BatchFind<UIButton>("Confirm").isEnabled,"pending Root rescanned/cancelled: "+pending);
            Console.WriteLine("MEASURE root idle rows=5000 ticks=5 closedCorners="+closed+" pendingCorners="+pending+" nativeTiming=false");
            SelectionMeasurements.Add(new {caseName="actual_root_idle",rows=5000,ticks=5,closedCornerReads=closed,pendingCornerReads=pending,nativeTimingMeasured=false});
        }),
        ("strong Host selection getter still scans and detects unseen stale rows",()=>
        {
            using var f=new RootViewFixture(5000);ResetPointer();f.Root.Update(true);var selection=ActualSelection(f);
            long original=selection.SelectionRevision;UIWidget.CornerReads=0;_ = selection.SelectionRevision;
            Check(UIWidget.CornerReads>=30000,"strong getter became cached");
            ScriptNodeInspector.instance!.scriptNode.scripts[4999]=new Script {LineId="external-unseen"};
            Check(selection.SelectionRevision>original,"strong capture/commit getter missed unseen external replacement");
            ScriptNodeInspector.instance.scriptNode.scripts[4998].LineId="external-unseen";
            Check(selection.SelectionRevision==-1&&!selection.TryGetSelectedRows(out _,out _),"strong getter/handoff accepted duplicate persistent identity");
        }),
        ("actual Root Host capture still rejects unseen invalid selection",()=>
        {
            using var f=new RootViewFixture(5000);f.OpenBatch();ResetPointer();f.Root.Update(true);
            ScriptNodeInspector.instance!.scriptNode.scripts[4999].LineId="anchor";
            BatchClick("Preview");Check(f.Host.BatchCaptures==0&&!BatchFind<UIButton>("Confirm").isEnabled&&BatchFind<UILabel>("Status").text.StartsWith("CAPTURE_FAILED"),"capture used cached presentation revision");
        }),
        ("actual Root Host commit still rejects unseen replacement without a Tick",()=>
        {
            using var f=new RootViewFixture(5000);f.OpenBatch();BatchClick("Preview");Check(BatchFind<UIButton>("Confirm").isEnabled,"preview missing");
            ScriptNodeInspector.instance!.scriptNode.scripts[4999]=new Script {LineId="unseen-external"};
            BatchClick("Confirm");Check(f.Host.BatchCommits==0&&BatchFind<UILabel>("Status").text.StartsWith("HOST_REJECTED"),"commit used cached presentation revision");
            SelectionMeasurements.Add(new{caseName="actual_strong_delegate_commit",rejectedBeforeMutation=true,commits=f.Host.BatchCommits});
        }),
        ("cache node and selected-row changes revoke visible preview on cheap Tick",()=>
        {
            foreach(int kind in new[]{0,1,2})
            {
                using var f=new RootViewFixture(3);f.OpenBatch();BatchClick("Preview");var inspector=ScriptNodeInspector.instance!;
                if(kind==0){var cache=new Il2CppSystem.Collections.Generic.List<ScriptListItem>();cache.AddRange(inspector.scriptNodeListItemsCache);inspector.scriptNodeListItemsCache=cache;}
                else if(kind==1)inspector.scriptNode.guid=Guid.NewGuid();
                else inspector.scriptNode.scripts[0]=new Script {LineId="selected-changed"};
                ResetPointer();f.Root.Update(true);Check(!BatchFind<UIButton>("Confirm").isEnabled,"cheap Tick ignored selection identity change "+kind);
            }
        }),
        ("a native list drag entering owned UI cancels only that gesture",()=>
        {
            using var f=new RootViewFixture(3);SelectOuterRows(f);f.OpenBatch();UICamera.hoveredObject=ScriptNodeInspector.instance!.scriptNodeListItemsCache[1].gameObject;
            PointerFrame(f,1,true,true);PointerFrame(f,2,held:true,x:80);
            UICamera.hoveredObject=BatchFind<UIButton>("Preview").gameObject;PointerFrame(f,1,held:true);PointerFrame(f,1,up:true);
            Check(ActualSelected(f).SequenceEqual(new[]{0,2}),"UI entry did not restore pointer-down selection");ListClick(f,1);Check(ActualSelected(f).SequenceEqual(new[]{1}),"later native list input stayed blocked");
        })
    };
}
