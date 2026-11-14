using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts.OperationManagement;

static partial class Program
{
    static void AttemptRevocation()
    {
        Test("F1 rejected genuine older batch revokes old card confirmation", () => {
            var host=Host();var workflow=new CardPackV2Workflow(host);var draft=RecoveryDraft();
            var card=workflow.Preview(draft).Preview!;var older=host.CaptureSetText("first");host.CaptureSetText("newer");
            Check(host.CommitSetText(older).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"older genuine preview was accepted");
            Check(!workflow.Confirm(card.ConfirmationToken,draft).IsSuccess && AuthoringEditorSession.Current!.Applies==0,"older card token survived rejected genuine batch");
        });
        Test("F1 malformed copied batch revokes old card confirmation", () => {
            var host=Host();var workflow=new CardPackV2Workflow(host);var draft=RecoveryDraft();
            var card=workflow.Preview(draft).Preview!;var preview=host.CaptureSetText("changed");
            Check(host.CommitSetText(preview with {Plan=null!}).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"malformed batch was accepted");
            Check(!workflow.Confirm(card.ConfirmationToken,draft).IsSuccess && AuthoringEditorSession.Current!.Applies==0,"old card token survived malformed batch");
        });
        Test("F1 malformed copied batch revokes another Host capture", () => {
            var first=Host();var peer=Host();var one=first.CaptureSetText("one");var two=peer.CaptureSetText("two");
            Check(first.CommitSetText(one with {Plan=null!}).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"malformed batch was accepted");
            Check(peer.CommitSetText(two).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"peer capture survived early rejection");
        });
        Test("F1 closed qualification Recover revokes known scope without native mutation", () => {
            var host=Host();var workflow=new CardPackV2Workflow(host);var draft=RecoveryDraft();var card=workflow.Preview(draft).Preview!;
            var native=AuthoringEditorSession.Current!;var before=AuthoringEditorSession.Fingerprint(native.studio.Project);int history=OperationManager.history.Count;
            allowed=false;Check(!host.Recover().Recovered,"closed gate recovery succeeded");
            Check(native.Checks==0 && native.Applies==0 && OperationManager.history.Count==history && AuthoringEditorSession.Fingerprint(native.studio.Project)==before,"closed gate revocation entered native mutation/check");
            allowed=true;Check(!workflow.Confirm(card.ConfirmationToken,draft).IsSuccess && native.Applies==0,"old card token survived failed recovery");
        });
        Test("F1 epoch-rejected old capture also revokes newer peer capture", () => {
            var oldHost=Host();var old=oldHost.CaptureSetText("old");var other=Host();var first=other.CaptureSetText("first");
            var native=AuthoringEditorSession.Current!;native.OnCheck=()=>throw new Exception("busy");other.CommitSetText(first);native.OnCheck=null;
            var peer=Host();var fresh=peer.CaptureSetText("fresh");
            Check(oldHost.CommitSetText(old).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"old epoch was accepted");
            Check(peer.CommitSetText(fresh).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"epoch rejection did not revoke the newer peer capture");
        });
        Test("F1 missing capture rejection still revokes its retained qualified scope", () => {
            var host=Host();var old=host.CaptureSetText("old");host.CommitSetText(old with {Plan=null!});
            var peer=Host();var fresh=peer.CaptureSetText("fresh");
            Check(host.CommitSetText(old).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"already consumed capture accepted");
            Check(peer.CommitSetText(fresh).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"missing-capture rejection lost its qualified revocation scope");
        });
        Test("F1 invalid card kind revokes peer batch before payload validation", () => {
            var host=Host();host.Capture("zh-CN");var peer=Host();var preview=peer.CaptureSetText("peer");
            Check(host.Commit(null!,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"null card payload accepted");
            Check(peer.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"kind validation skipped session revocation");
        });
        Test("F1 failed recovery retains revocation scope after local captures were consumed", () => {
            var host=Host();var first=host.CaptureSetText("first");host.CommitSetText(first with {Plan=null!});
            var peer=Host();var fresh=peer.CaptureSetText("fresh");allowed=false;Check(!host.Recover().Recovered,"closed gate recovery succeeded");allowed=true;
            Check(peer.CommitSetText(fresh).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"consumed captures erased the known recovery scope");
        });
        Test("F1 caller supplied foreign session cannot redirect owned revocation", () => {
            var first=Host();var one=first.CaptureSetText("session A");
            Fixture.Reset();var nativeB=AuthoringEditorSession.Current!;var peer=Host();var two=peer.CaptureSetText("session B");
            Check(first.CommitSetText(one with {Identity=two.Identity,Plan=null!}).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"forged identity accepted");
            Check(nativeB.Checks==0 && nativeB.Applies==0,"foreign supplied identity caused native access");
            Check(peer.CommitSetText(two).Disposition==CardPackV2CommitDisposition.Applied && nativeB.Applies==1,"unrelated session was revoked by caller supplied identity");
        });
        Test("F1 unqualified new Host cannot revoke a foreign supplied preview", () => {
            var peer=Host();var preview=peer.CaptureSetText("peer");var unbound=Host();
            Check(unbound.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"uncaptured preview was accepted");
            Check(peer.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current!.Applies==1,"unbound Host trusted a caller session for revocation");
        });
        Test("F1 closed unbound Recover does not infer scope from unqualified Current", () => {
            var peer=Host();var preview=peer.CaptureSetText("peer");var fresh=Host();var native=AuthoringEditorSession.Current!;
            allowed=false;Check(!fresh.Recover().Recovered && native.Checks==0 && native.Applies==0,"unbound closed recovery entered native boundary");allowed=true;
            Check(peer.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.Applied,"unbound recovery revoked an unqualified current scope");
        });
        Test("F1 off-thread submission and recovery cannot revoke qualified previews", () => {
            var host=Host();var first=host.CaptureSetText("first");var peer=Host();var preview=peer.CaptureSetText("peer");
            Task.Run(()=>{Check(host.CommitSetText(first).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"off-thread submit accepted");Check(!host.Recover().Recovered,"off-thread recovery accepted");}).GetAwaiter().GetResult();
            Check(peer.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current!.Applies==1,"off-thread refusal changed session generation");
        });
        Test("F1 reentrant refusals do not mutate the shared revocation generation", () => {
            var host=Host();var peer=Host();var preview=peer.CaptureSetText("peer");var captured=host.Capture("zh-CN");var plan=Plan(host);var native=AuthoringEditorSession.Current!;
            native.OnApply=()=>{Check(peer.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"reentrant submit accepted");Check(!peer.Recover().Recovered,"reentrant recovery accepted");};
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.Applied,"outer owner lost its submission");native.OnApply=null;
            Check(host.Capture("zh-CN").SubmissionVersion==captured.SubmissionVersion+1 && native.Checks==1 && native.Applies==1,"reentrant refusal advanced owned generation or entered native boundary");
        });
        Test("F1 first qualified Recover revokes its freshly verified session", () => {
            var peer=Host();var preview=peer.CaptureSetText("peer");Check(Host().Recover().Recovered,"first qualified recovery failed");
            Check(peer.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"qualified first recovery did not revoke current session");
        });
    }
}
