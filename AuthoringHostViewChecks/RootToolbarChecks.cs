using Azurite;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using UnityEngine;

static partial class Program
{
    sealed class RootViewFixture : IDisposable
    {
        internal readonly Fixture Native = new();
        internal readonly AuthoringCardPackHostRoot Root;
        internal readonly List<string> Logs = new();
        internal RootViewHost Host => AuthoringCardPackHostComposition.Hosts.Single();
        internal RootViewFixture(int rows=1)
        {
            AuthoringCardPackHostComposition.Hosts.Clear(); ResetPointer();
            var inspector=ScriptNodeInspector.instance!; inspector.transform.SetParent(Native.Parent.transform,false);
            inspector.contentInput=new UIInput();inspector.contentInput.label=Native.Typography;
            for(int i=1;i<rows;i++)
            {
                inspector.scriptNode.scripts.Add(new Script {LineId="line-"+i,StageJson=inspector.scriptNode.scripts[0].StageJson});
                inspector.scriptNodeListItemsCache.Add(new ScriptListItem {index=i,inspector=inspector,scriptNode=inspector.scriptNode});
            }
            foreach(var row in inspector.scriptNodeListItemsCache)
            {
                float top=1000-row.index*20;row.child=new UIWidget {worldCorners=new[]{new Vector3(0,top,0),new Vector3(100,top,0),new Vector3(100,top-18,0),new Vector3(0,top-18,0)}};
                row.gameObject.AddComponent<UIButton>();
            }
            Root=new(Environment.CurrentManagedThreadId,()=>true,()=>Native.Allowed,Logs.Add);Root.Update(true);
            ListClick(this,0);
        }
        internal void OpenBatch() { GameObject.All.Single(x=>x.name=="AzuriteBatchLauncher"&&!x.IsDestroyed).GetComponent<UIButton>()!.Click();Root.Update(true); }
        internal void OpenCard() { GameObject.All.Single(x=>x.name=="AzuriteCardPackLauncher"&&!x.IsDestroyed).GetComponent<UIButton>()!.Click();Root.Update(true); }
        public void Dispose(){Root.DisposeAtSafePoint();Native.Dispose();}
    }
    static readonly (string Name,Action Test)[] RootToolbarCases =
    {
        ("live Root launcher and actual toolbar reach all five actions on its one Host",()=>
        {
            foreach(string action in new[]{"Speaker","Text","Duplicate","Delete","Multiline"})
            {
                using var f=new RootViewFixture();f.OpenBatch();BatchClick("Action"+action);
                if(action=="Text"||action=="Multiline")BatchFind<UIInput>("Text").value="new\r\n";
                BatchClick("Preview");Check(BatchFind<UIButton>("Confirm").isEnabled,action+": "+BatchFind<UILabel>("Status").text+string.Join(";",f.Logs));
                var captured=f.Host.Captured;f.Root.Update(true);BatchClick("Confirm");
                Check(f.Host.BatchCommits==1&&f.Host.BatchCaptures==1&&ReferenceEquals(f.Host.Committed,captured),"live root route recaptured or failed "+action);
                Check(ReferenceEquals(f.Host.Rows().Single(),ScriptNodeInspector.instance!.selectedScriptItem),"Root fabricated the selection row");
                f.OpenCard();Check(AuthoringCardPackHostComposition.Hosts.Count==1,"Root made a second Host");
            }
        }),
        ("actual Root switches forms and invalidates both visible confirmations",()=>
        {
            using var f=new RootViewFixture();f.OpenCard();Click("FamilyLanguage");Click("Preview");Check(Find<UIButton>("Confirm").isEnabled,"card preview failed");
            f.OpenBatch();Check(!Find<UIButton>("Confirm").isEnabled,"switch left card confirm enabled");BatchClick("Preview");Check(BatchFind<UIButton>("Confirm").isEnabled,"batch preview failed");
            f.OpenCard();Check(!BatchFind<UIButton>("Confirm").isEnabled,"switch left batch confirm enabled");Click("Preview");Click("Confirm");
            Check(f.Host.CardCommits==1&&f.Host.BatchCommits==0,"wrong client committed on shared Host");
        }),
        ("actual Root observes external selection and catalog changes without recapture",()=>
        {
            using var f=new RootViewFixture();f.OpenBatch();BatchClick("Preview");SelectionHook("AfterRowRefresh",ScriptNodeInspector.instance!.selectedScriptItem);f.Root.Update(true);
            Check(!BatchFind<UIButton>("Confirm").isEnabled&&f.Host.BatchCaptures==1,"selection did not revoke visible token");
            ListClick(f,0);BatchClick("Preview");AuthoringResourceCatalog.CatalogVersion="changed";f.Root.Update(true);
            Check(!BatchFind<UIButton>("Confirm").isEnabled&&f.Host.BatchCaptures==2,"catalog did not revoke visible token");
        }),
        ("actual Root fault and recovery controls share Host outcome",()=>
        {
            using var f=new RootViewFixture();f.OpenBatch();f.Host.Disposition=CardPackV2CommitDisposition.OutcomeUnknown;BatchClick("Preview");BatchClick("Confirm");
            Check(BatchFind<UIButton>("Recover").isEnabled,"batch recovery missing");f.OpenCard();Check(Find<UIButton>("Recover").isEnabled&&!Find<UIButton>("Preview").isEnabled,"shared fault invisible on card client");
            Click("Recover");Check(f.Host.BatchRecovers==1,"card recovery did not call same Host");f.OpenBatch();Check(!BatchFind<UIButton>("Recover").isEnabled&&BatchFind<UIButton>("Preview").isEnabled,"verified recovery not shared");
        }),
        ("actual Root retains forms but export clears pending confirmation",()=>
        {
            using var f=new RootViewFixture();f.OpenBatch();BatchClick("Preview");var view=GameObject.All.Single(x=>x.name=="AzuriteBatchView");
            f.Root.SuspendForExport();Check(!BatchFind<UIButton>("Confirm").isEnabled,"export did not revoke synchronously");f.Root.Update(true);f.Root.ResumeAfterExport();f.Root.Update(true);
            Check(!view.IsDestroyed&&f.Host.BatchCaptures==1&&!BatchFind<UIButton>("Confirm").isEnabled,"export lost form or resurrected token");
        })
    };
}
