using Azurite;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;
using System.Text.Json;

static partial class Program
{
    static void BatchSetText()
    {
        Test("shared post-proof rejects wrong save counter even with complete native text and history", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;
            native.OnAfterApply=()=>OperationManager.saveDelta=0;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"wrong save bookkeeping passed complete-commit proof");
        });
        Test("shared post-proof rejects wrong major compile counter for card insertion", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.OnAfterApply=()=>OperationManager.compileDelta=0;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"card path ignored native compile bookkeeping");
        });
        Test("isolated write keeps native negative delta sentinel and positive increment semantics", () => {
            OperationManager.saveDelta=-3;OperationManager.compileDelta=-1;var host=Host();var preview=host.CaptureSetText("changed");
            var result=host.CommitSetText(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && OperationManager.saveDelta==114514 && OperationManager.compileDelta==114514,result.Message);
            OperationManager.saveDelta=2;OperationManager.compileDelta=4;var plan=Plan(host);result=host.Commit(plan,7);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && OperationManager.saveDelta==3 && OperationManager.compileDelta==5,result.Message);
        });
        Test("isolated nonmajor history preserves compile delta exactly", () => {
            OperationManager.saveDelta=2;OperationManager.compileDelta=-7;Fixture.NextIsMajor=false;var host=Host();var preview=host.CaptureSetText("changed");
            var result=host.CommitSetText(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && OperationManager.saveDelta==3 && OperationManager.compileDelta==-7,result.Message);
        });
        Test("shared post-proof rejects compile drift for a nonmajor operation", () => {
            Fixture.NextIsMajor=false;var host=Host();var preview=host.CaptureSetText("changed");AuthoringEditorSession.Current!.OnAfterApply=()=>OperationManager.compileDelta=1;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"nonmajor compile drift accepted");
        });
        Test("isolated native int counters wrap instead of using checked arithmetic", () => {
            OperationManager.saveDelta=int.MaxValue;OperationManager.compileDelta=int.MaxValue;var host=Host();var preview=host.CaptureSetText("changed");
            var result=host.CommitSetText(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && OperationManager.saveDelta==int.MinValue && OperationManager.compileDelta==int.MinValue,result.Message);
        });
        Test("shared post-proof rejects remaining link-next flag on isolated history", () => {
            var host=Host();var preview=host.CaptureSetText("changed");AuthoringEditorSession.Current!.OnAfterApply=()=>OperationManager.needToLinkWithPrev=true;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"linked bookkeeping admitted after isolated history");
        });
        Test("SetText maps UI row objects to native persistent IDs and uses shared Check then Apply", () => {
            var host=Host();var native=AuthoringEditorSession.Current!;string before=AuthoringEditorSession.Fingerprint(native.studio.Project);
            var inspector=ScriptNodeInspector.instance!;string uiKey=$"node:{inspector.scriptNode.Pointer:X16}/script:{inspector.scriptNode.scripts[0].Pointer:X16}";
            var preview=host.CaptureSetText("  完整 😀\r\n文本  ");
            Check(preview.Plan.Selection.Rows.Select(r=>r.StableId).SequenceEqual(new[]{"opaque-anchor","opaque-tail"}),"native persistent targets lost");
            Check(!preview.Plan.OperationsJson.Contains(uiKey,StringComparison.Ordinal),"UI pointer cache key leaked into native operation IDs");
            Check(preview.Plan.Selection.Rows[0].Text=="before" && preview.Plan.Selection.Rows[0].SpeakerSlot==2,"native row content lost");
            Check(AuthoringEditorSession.Fingerprint(native.studio.Project)==before && native.Applies==0 && native.Checks==0,"capture changed live model or entered transaction");
            var result=host.CommitSetText(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(native.Events.SequenceEqual(new[]{"Check","Apply"}) && native.Applies==1 && native.CheckedInput && native.CheckedResources && native.CheckedSession==preview.Identity.EditSession && native.CheckedRevision==preview.Identity.Revision,"shared native boundary wrong");
            Check(((ScriptNodeData)native.studio.Project.nodes[0]).Scripts.All(x=>x.text=="  完整 😀\r\n文本  ") && OperationManager.history.Count==2,"update or history missing");
            using var json=JsonDocument.Parse(native.LastOperations!);Check(json.RootElement.GetArrayLength()==2 && json.RootElement[0].GetProperty("op").GetString()=="line.update","wrong actual payload");
        });
        Test("SetText preserves complete project and unselected row state", () => {
            Fixture.BatchIndices=new[]{1};var native=AuthoringEditorSession.Current!;
            native.studio.Project.nodes.Add(new ScriptNodeData{Guid=Guid.NewGuid(),Scripts=new(){new(){LineId="other-node",text="other"}}});
            var original=native.studio.Project.Clone();var host=Host();var preview=host.CaptureSetText("tail changed");
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.Applied,"complete update rejected");
            ((ScriptNodeData)original.nodes[0]).Scripts[1].text="tail changed";
            Check(AuthoringEditorSession.Fingerprint(original)==AuthoringEditorSession.Fingerprint(native.studio.Project),"unexpected full project change");
        });
        Test("SetText selected membership drift with unchanged revision rejects before Check", () => {
            var host=Host();var preview=host.CaptureSetText("changed");Fixture.BatchIndices=new[]{1};
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Checks==0 && AuthoringEditorSession.Current.Applies==0,"stale selection entered native boundary");
        });
        Test("SetText revalidates text and all project state before mutation", () => {
            var host=Host();var preview=host.CaptureSetText("changed");AuthoringEditorSession.Current!.studio.Project.ProjectName="external edit";
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current.Applies==0,"full-state drift admitted");
        });
        Test("SetText rejects selected row from a stale native cache or divergent live text", () => {
            var inspector=ScriptNodeInspector.instance!;inspector.scriptNodeListItemsCache[1].scriptNode=new();
            Reject(()=>Host().CaptureSetText("changed"),"stale selected cache row admitted");Fixture.Rebind();
            ScriptNodeInspector.instance!.scriptNode.scripts[1].text="live-only divergence";
            Reject(()=>Host().CaptureSetText("changed"),"live/snapshot text mismatch admitted");Check(AuthoringEditorSession.Current!.Applies==0,"invalid native row selection mutated");
        });
        Test("SetText Check replacement and reentrant selection drift never Apply", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;native.OnCheck=()=>Fixture.BatchIndices=new[]{1};
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"selection change inside Check admitted");
        });
        Test("SetText preview is bound to one host capture and consumed after rejection", () => {
            var host=Host();var preview=host.CaptureSetText("changed");
            Check(Host().CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"foreign host accepted preview");
            allowed=false;Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"closed gate admitted");allowed=true;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"rejected capture reused");
        });
        Test("malformed copied SetText preview is rejected within shared submission boundary", () => {
            var host=Host();var preview=host.CaptureSetText("changed");
            Check(host.CommitSetText(preview with { Plan=null! }).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"malformed copied preview escaped controlled rejection");
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"malformed attempt retained a reusable capture or mutated");
        });
        Test("SetText rejects shallow clone aliases before modifying expected text", () => {
            var native=AuthoringEditorSession.Current!;var before=AuthoringEditorSession.Fingerprint(native.studio.Project);
            ProjectFileStore.CloneOverride=p=>p;
            Reject(()=>Host().CaptureSetText("changed"),"aliased native clone was modified");
            Check(AuthoringEditorSession.Fingerprint(native.studio.Project)==before && native.Applies==0,"clone preflight changed live data");
        });
        Test("SetText full snapshot clone cannot silently omit project residue", () => {
            ProjectFileStore.CloneOverride=p=>{var clone=p.Clone();clone.ProjectName="dropped";return clone;};
            Reject(()=>Host().CaptureSetText("changed"),"lossy clone used as complete-state proof");Check(AuthoringEditorSession.Current!.Applies==0,"lossy clone applied");
        });
        Test("SetText post-proof rejects unrelated selected-line state mutation", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;
            native.OnAfterApply=()=>((ScriptNodeData)native.studio.Project.nodes[0]).Scripts[0].voice="corrupted";
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"voice corruption accepted");
            Check(!Host().Recover().Recovered && native.Applies==1,"incorrect full-state commit recovered");
        });
        Test("SetText post-proof rejects target text mismatch despite one history entry", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;
            native.OnAfterApply=()=>((ScriptNodeData)native.studio.Project.nodes[0]).Scripts[1].text="wrong";
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"wrong exact text passed history-only proof");
        });
        Test("SetText full-project post-proof rejects unrelated metadata changes", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;native.OnAfterApply=()=>native.studio.Project.ProjectName="changed outside batch";
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"foreign project field ignored by text-only proof");
        });
        Test("SetText post-proof rejects reordered IDs and foreign project edits", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;
            native.OnAfterApply=()=>((ScriptNodeData)native.studio.Project.nodes[0]).Scripts.Reverse();
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"line reorder accepted");
        });
        Test("SetText uses single snapshot history proof, not only correct final text", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;
            native.OnAfterApply=()=>OperationManager.history.Push(new Operation());
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"extra history accepted");
        });
        Test("SetText entered failure locks both clients across replacement and recovers only unchanged state", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"entered failure misreported");
            Reject(()=>Host().Capture("zh-CN"),"card capture bypassed batch fault");Reject(()=>Host().CaptureSetText("new"),"batch capture bypassed persistent fault");
            Check(Host().Recover().Recovered && native.Applies==1,"unchanged recovery failed or retried");
        });
        Test("card fault blocks SetText and shared recovery never retries", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;host.Commit(plan,7);
            Reject(()=>Host().CaptureSetText("changed"),"SetText bypassed card fault");Check(Host().Recover().Recovered && native.Applies==1,"card fault recovery did not stay shared");
        });
        Test("SetText missing receipt and throw-after-history cannot be recovered as success", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;native.NullReceipt=true;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered && native.Applies==1,"no-receipt update became success");
        });
        Test("SetText thrown Apply with correct model and history remains locked", () => {
            var host=Host();var preview=host.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;native.ThrowAfterHistory=true;
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered && native.Applies==1,"thrown update became success from history alone");
        });
        Test("SetText successful native return with failed UI refresh uses update-aware recovery", () => {
            var host=Host();var preview=host.CaptureSetText("changed");invalidate=()=>throw new Exception("UI refresh failed");
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"UI failure ignored");invalidate=null;
            Check(Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"verified update could not recover without retry");
        });
        Test("shared gate rejects batch reentry from card and card reentry from batch", () => {
            var card=Host();var batch=Host();var cardPlan=Plan(card);var preview=batch.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;
            native.OnApply=()=>Check(batch.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"batch reentered card Apply");
            Check(card.Commit(cardPlan,7).Disposition==CardPackV2CommitDisposition.Applied && native.Applies==1,"card outer submit failed");
            native.OnApply=null;preview=batch.CaptureSetText("next");cardPlan=Plan(card);
            native.OnApply=()=>Check(card.Commit(cardPlan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"card reentered batch Apply");
            Check(batch.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.Applied && native.Applies==2,"batch outer submit failed");
        });
        Test("batch pre-Apply rejection revokes a different card workflow token", () => {
            var card=Host();var workflow=new CardPackV2Workflow(card);var draft=RecoveryDraft();var cardPreview=workflow.Preview(draft).Preview!;
            var batch=Host();var preview=batch.CaptureSetText("changed");var native=AuthoringEditorSession.Current!;native.OnCheck=()=>throw new Exception("busy");
            Check(batch.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"expected guarded refusal missing");native.OnCheck=null;
            var result=workflow.Confirm(cardPreview.ConfirmationToken,draft);
            Check(!result.IsSuccess && native.Applies==0,"old card token survived another client submission attempt");
        });
        Test("explicit no-fault recovery revokes preexisting batch capture", () => {
            var host=Host();var preview=host.CaptureSetText("changed");Check(Host().Recover().Recovered,"no-fault recovery failed");
            Check(host.CommitSetText(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"recovery left old cross-host capture usable");
        });
        Test("batch 500 operations call shared Apply once and preserve all IDs", () => {
            var native=AuthoringEditorSession.Current!;var data=(ScriptNodeData)native.studio.Project.nodes[0];
            data.Scripts=new(Enumerable.Range(0,500).Select(i=>new ScriptData{LineId=i==0?"opaque-anchor":"row-"+i,text="old"}));Fixture.Rebind();Fixture.BatchIndices=Enumerable.Range(0,500).ToArray();
            var host=Host();var preview=host.CaptureSetText("changed");var result=host.CommitSetText(preview);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && native.Applies==1 && data.Scripts.Count==500 && OperationManager.history.Count==2,result.Message);
        });
        Test("batch 501 changed native rows reject before Check or Apply", () => {
            var native=AuthoringEditorSession.Current!;var data=(ScriptNodeData)native.studio.Project.nodes[0];
            data.Scripts=new(Enumerable.Range(0,501).Select(i=>new ScriptData{LineId=i==0?"opaque-anchor":"row-"+i,text="old"}));Fixture.Rebind();Fixture.BatchIndices=Enumerable.Range(0,501).ToArray();
            Reject(()=>Host().CaptureSetText("changed"),"oversize host capture admitted");Check(native.Checks==0 && native.Applies==0,"501 entered native transaction");
        });
    }
}
