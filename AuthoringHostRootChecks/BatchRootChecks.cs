using Azurite;
using UnityEngine;
using Azurite.Core;
using AzureArchive.Automation;

static partial class Program
{
    static AuthoringCardPackHostBatchToolbar OpenBatch(Fixture f)
    {
        f.Root.Update(true); GameObject.All.Single(x => x.name == "AzuriteBatchLauncher" && !x.IsDestroyed).GetComponent<UIButton>()!.Click();
        f.Root.Update(true); return AuthoringCardPackHostBatchToolbar.Instances.Last();
    }
    static readonly (string Name, Action Test)[] BatchCases =
    {
        ("admission lost during capture consumes the pending workflow preview", () => With(f =>
        {
            var batch=OpenBatch(f);var host=RootBoundaryHost.Instances.Single();host.AfterCapture=()=>f.Context=false;
            var draft=new AuthoringBatchDraft(AuthoringBatchAction.Delete);var result=batch.Workflow.Preview(draft);f.Context=true;
            Check(result.Preview!=null&&batch.Workflow.Confirm(result.Preview.ConfirmationToken,draft).Code=="PREVIEW_REQUIRED", "capture completion allowed a closed-admission preview to survive");
        })),
        ("ordinary registered Root exposes a real batch launcher", () => With(f =>
        {
            f.Root.Update(true);
            var node = GameObject.All.SingleOrDefault(x => x.name == "AzuriteBatchLauncher" && !x.IsDestroyed);
            Check(node?.GetComponent<UIButton>()?.onClick.Count == 1, "registered batch launcher is absent");
        })),
        ("registered batch launcher is queued and shares existing Host with cards", () => With(f =>
        {
            var card = f.Open(); var batch = OpenBatch(f);
            Check(RootBoundaryHost.Instances.Count == 1 && !card.Enabled && batch.Enabled, "second Host or overlapping forms");
            var host = RootBoundaryHost.Instances.Single(); var draft = new AuthoringBatchDraft(AuthoringBatchAction.SetText,"root text");
            var p = batch.Workflow.Preview(draft).Preview!; f.Root.Update(true);
            Check(batch.Workflow.Confirm(p.ConfirmationToken,draft).Code == "APPLIED" && host.BatchCommits == 1 && host.Captures == 0, "root did not route batch action to same Host");
        })),
        ("card preview revokes a retained batch workflow token immediately", () => With(f =>
        {
            var card = f.Open(); var batch = OpenBatch(f); var draft = new AuthoringBatchDraft(AuthoringBatchAction.Delete);
            var p = batch.Workflow.Preview(draft).Preview!; card.Workflow.Preview(Data());
            Check(batch.Workflow.Confirm(p.ConfirmationToken,draft).Code == "PREVIEW_REQUIRED", "card left batch confirmation live");
        })),
        ("batch preview revokes a card workflow token immediately", () => With(f =>
        {
            var card = f.Open(); var batch = OpenBatch(f); var p = card.Workflow.Preview(Data()).Preview!;
            batch.Workflow.Preview(new(AuthoringBatchAction.Delete));
            Check(card.Workflow.Confirm(p.ConfirmationToken,Data()).Code == "PREVIEW_REQUIRED", "batch left card confirmation live");
        })),
        ("selection context epoch and catalog stamp changes revoke batch confirmation", () =>
        {
            foreach(var change in new Action<Fixture>[] { f => f.Session.Revision="changed", f => AuthoringResourceCatalog.CatalogVersion="new", f => f.Inspector.selectedSlotNum=4, f => RootBoundaryHost.Instances.Single().Epoch++, f => f.Context=false, f => f.Inspector.scriptNode.guid=Guid.NewGuid() })
                With(f => { var batch=OpenBatch(f);var draft=new AuthoringBatchDraft(AuthoringBatchAction.Delete);var p=batch.Workflow.Preview(draft).Preview!;change(f);f.Root.Update(true);Check(batch.Workflow.Confirm(p.ConfirmationToken,draft).Code=="PREVIEW_REQUIRED","stamp change retained token"); });
        }),
        ("capture refreshed revision is not mistaken for a later external edit", () => With(f =>
        {
            var batch=OpenBatch(f);var host=RootBoundaryHost.Instances.Single();host.AfterCapture=()=>f.Session.Revision="capture-refreshed";
            var draft=new AuthoringBatchDraft(AuthoringBatchAction.Delete);var p=batch.Workflow.Preview(draft).Preview!;f.Root.Update(true);
            Check(batch.Workflow.Confirm(p.ConfirmationToken,draft).Code=="APPLIED", "healthy next tick revoked refreshed preview");
        })),
        ("Root observes same-session Host fault in both retained clients", () => With(f =>
        {
            var card=f.Open();var batch=OpenBatch(f);var host=RootBoundaryHost.Instances.Single();host.Faulted=true;f.Root.Update(true);
            Check(card.Workflow.RecoveryRequired&&batch.Workflow.RecoveryRequired,"readonly Host fault not shared");
            host.Faulted=false;f.Root.Update(true);Check(batch.Workflow.RecoveryRequired,"cheap false status silently cleared recovery");
            Check(batch.Workflow.Recover().Code=="RECOVERED"&&!card.Workflow.RecoveryRequired,"actual recovery did not clear both clients");
        })),
        ("batch survives ordinary close and export but loses pending confirmation", () => With(f =>
        {
            var batch=OpenBatch(f);var draft=new AuthoringBatchDraft(AuthoringBatchAction.Delete);var p=batch.Workflow.Preview(draft).Preview!;
            f.Root.RequestBatchOpen(false);f.Root.Update(true);f.Root.RequestBatchOpen(true);f.Root.Update(true);
            Check(ReferenceEquals(batch,AuthoringCardPackHostBatchToolbar.Instances.Last())&&batch.Enabled,"ordinary close discarded form");
            Check(batch.Workflow.Confirm(p.ConfirmationToken,draft).Code=="PREVIEW_REQUIRED","close preserved token");
            p=batch.Workflow.Preview(draft).Preview!;f.Root.SuspendForExport();Check(batch.Workflow.Confirm(p.ConfirmationToken,draft).Code=="PREVIEW_REQUIRED","export preserved token");
        })),
        ("terminal batch cleanup reads no destroyed editor state", () => With(f =>
        {
            var batch=OpenBatch(f);AuthoringEditorSession.ThrowOnCurrentRead=true;int reads=AuthoringEditorSession.CurrentReads;
            f.Root.AbandonForShutdown();Check(batch.Disposed&&AuthoringEditorSession.CurrentReads==reads,"batch shutdown read Current or retained UI");
        }))
    };
}
