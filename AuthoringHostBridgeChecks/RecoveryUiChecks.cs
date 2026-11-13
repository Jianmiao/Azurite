using Azurite;
using Azurite.Core;
using AzureArchive.Automation;

static partial class Program
{
    static CardPackV2Draft RecoveryDraft()=>new(CardPackV2Json.Serialize(new("2","recovery.pack","Recovery",new[]{new CardV2("fade",PresetCardKind.Transition,ResourceKey:"fade")})),"zh-CN");
    static void RecoveryUi()
    {
        Test("new Workflow receives persistent Host recovery state and explicitly verifies it without retry",()=>{
            var first=Host();var plan=Plan(first);var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;
            Check(first.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && native.Applies==1,"native uncertainty not established");
            var workflow=new CardPackV2Workflow(Host());var draft=RecoveryDraft();var preview=workflow.Preview(draft);
            Check(!preview.IsSuccess && preview.Code=="RECOVERY_REQUIRED" && workflow.RecoveryRequired,"new workflow did not expose the persistent Host recovery requirement");
            Check(native.Applies==1 && native.Checks==1,"preview automatically retried or recovered native state");
            Check(workflow.Recover().IsSuccess && !workflow.RecoveryRequired && native.Applies==1 && native.Checks==2,"explicit unchanged-before recovery did not clear the workflow or retried Apply");
            Check(workflow.Preview(draft).IsSuccess && native.Applies==1,"verified recovery did not permit a fresh preview without replay");
        });
        Test("production Panel enables recovery after rebuilding over a faulted native Host",()=>{
            var first=Host();var plan=Plan(first);var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;first.Commit(plan,7);
            var workflow=new CardPackV2Workflow(Host());var preview=new UIButton();var confirm=new UIButton();var cancel=new UIButton();var recover=new UIButton();var status=new UILabel();
            using var panel=new AuthoringCardPackPanel(workflow,RecoveryDraft,preview,confirm,cancel,recover,new(),status);
            preview.Click();
            Check(recover.isEnabled && !preview.isEnabled && !confirm.isEnabled && workflow.RecoveryRequired && native.Checks==1,"rebuilt Panel stranded or automatically recovered the persistent native fault");
            recover.Click();
            Check(!workflow.RecoveryRequired && !recover.isEnabled && preview.isEnabled && !confirm.isEnabled && native.Applies==1 && native.Checks==2,"Panel recovery retried native Apply or failed to restore new-operation controls");
        });
        Test("generic capture error with recovery-looking text does not set a recovery latch",()=>{
            var host=new AuthoringCardPackHost(Environment.CurrentManagedThreadId,()=>true,()=>7,
                (identity,locale)=>throw new InvalidOperationException("RECOVERY_REQUIRED: ordinary projection failure text"),(_,_)=>{});
            var workflow=new CardPackV2Workflow(host);var result=workflow.Preview(RecoveryDraft());
            Check(!result.IsSuccess && result.Code=="CAPTURE_FAILED" && !workflow.RecoveryRequired && AuthoringEditorSession.Current!.Applies==0,"workflow inferred recovery state from an error string");
        });
    }
}
