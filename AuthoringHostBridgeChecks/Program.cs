using Azurite;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;
using System.Text.Json;

static partial class Program
{
    static int passed, failed;
    static long selectionRevision; static bool allowed; static string resourceId = "fixture:transition"; static Action? invalidate;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
    static void Test(string name, Action action) { Fixture.Reset(); selectionRevision = 7; allowed = true; resourceId = "fixture:transition"; invalidate = null; try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    static AuthoringCardPackHost Host(int? thread = null) => new(thread ?? Environment.CurrentManagedThreadId, () => allowed, () => selectionRevision,
        (id, locale) => new(id, JsonDocument.Parse("[]").RootElement.Clone(), true, new[] { new CardResourceBinding("fade", resourceId, CardResourceKind.Transition, true, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()) }, CardNativeCapability.LineInsert | CardNativeCapability.SceneTransition, locale),
        (node, ids) => { Check(node == ScriptNodeInspector.instance!.scriptNode.guid.ToString() && ids.Contains("opaque-anchor"), "selection refresh did not use current persistent IDs"); invalidate?.Invoke(); });
    static CardPackNativePlan Plan(AuthoringCardPackHost host, int count = 1)
    {
        var context = host.Capture("zh-CN").Context;
        return CardPackV2Compiler.TryCompile(new("2", "pack", "Test", Enumerable.Range(0,count).Select(i => new CardV2("c" + i, PresetCardKind.Transition, ResourceKey: "fade")).ToArray()), context, context.Identity).Plan!;
    }
    static int Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--recovery-ui"}))
        {
            RecoveryUi();Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");return failed==0?0:1;
        }
        if(args.SequenceEqual(new[]{"--resource-availability"}))
        {
            ResourceAvailability();Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");return failed==0?0:1;
        }
        if(args.Length!=0){Console.Error.WriteLine("Use --recovery-ui, --resource-availability or no arguments.");return 2;}
        Test("capture maps native opaque identities and verifies live/snapshot ownership", () => {
            var host=Host(); var s=host.Capture("zh-CN"); var native=AuthoringEditorSession.Current!;
            Check(s.Context.Identity.ProjectId==native.ProjectId && s.Context.Identity.EditSession==native.EditSession && s.Context.Identity.Revision=="r1" && s.Context.Identity.AnchorLineId=="opaque-anchor" && s.SelectionRevision==7, "native identity lost");
            Fixture.Rebind(); Check(host.Capture("zh-CN").Context.Identity==s.Context.Identity, "pointer recreation changed opaque IDs");
            native.studio=new(); Reject(()=>host.Capture("zh-CN"),"foreign owner accepted");
        });
        Test("commit calls native Check with both guards then exactly one Apply and one history unit", () => {
            var host=Host(); var plan=Plan(host); var native=AuthoringEditorSession.Current!; var original=OperationManager.history.Peek().Pointer;
            var result=host.Commit(plan,7);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied, result.Message);
            Check(native.Events.SequenceEqual(new[]{"Check","Apply"}) && native.Applies==1 && native.Checks==1 && native.CheckedInput && native.CheckedResources && native.CheckedSession==plan.Identity.EditSession && native.CheckedRevision=="r1", "native boundary arguments/order wrong");
            Check(OperationManager.history.Count==2 && OperationManager.history.ToArray()[1].Pointer==original && ((ScriptNodeData)native.studio.Project.nodes[0]).Scripts.Count==3, "native model/history not changed once");
            Check(AuthoringResourceCatalog.Resolved.Contains("fixture:transition"), "native resource was not resolved again");
        });
        Test("wrong thread and uncertified guard reject before native access", () => {
            Reject(()=>Host(Environment.CurrentManagedThreadId+123).Capture("zh-CN"),"wrong thread admitted");
            var host=Host(); var plan=Plan(host); allowed=false;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"closed eligibility mutated");
        });
        Test("changed selection, revision, catalog and content reject without Apply", () => {
            var host=Host();var plan=Plan(host);selectionRevision=8;Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"stale selection admitted");selectionRevision=7;
            plan=Plan(host);AuthoringEditorSession.Current!.Revision="changed";Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"stale revision admitted");
            plan=Plan(host);resourceId="rebound";Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"rebound content admitted");
            plan=Plan(host);AuthoringResourceCatalog.CatalogVersion="changed";Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"stale catalog admitted");
        });
        Test("native Check failure or selection change during Check never enters Apply", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;native.OnCheck=()=>throw new Exception("EDITOR_BUSY");
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"Check failure applied");
            native.OnCheck=()=>selectionRevision++; plan=Plan(host);
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"post-Check selection drift applied");
        });
        Test("native resource resolution failure rejects before Check or Apply", () => {
            var host=Host();var plan=Plan(host);AuthoringResourceCatalog.Missing=true;var native=AuthoringEditorSession.Current!;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Checks==0 && native.Applies==0,"missing resource entered mutation boundary");
        });
        Test("entered Apply exception persists across replacement host and recovery proves unchanged state", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && native.Applies==1,"entered Apply misreported unmodified");
            var replacement=Host();Reject(()=>replacement.Capture("zh-CN"),"new panel bypassed session uncertainty");
            Check(replacement.Recover().Recovered && native.Applies==1,"unchanged model/history recovery failed or retried");
            native.ThrowBeforeApply=false;Check(replacement.Commit(Plan(replacement),7).Disposition==CardPackV2CommitDisposition.Applied && native.Applies==2,"explicitly fresh edit blocked after proven recovery");
        });
        Test("partial model mutation without matching native snapshot history cannot recover", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;native.PartialMutation=true;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"partial mutation hidden");
            Check(!Host().Recover().Recovered && native.Applies==1,"capture-only recovery cleared unknown partial state");
        });
        Test("thrown Apply remains uncertain even when native snapshot history was already written", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;native.ThrowAfterHistory=true;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"throw after history became success");
            var recovery=Host().Recover();Check(!recovery.Recovered && native.Applies==1,"history-only recovery assumed native completion after an exception");
        });
        Test("history tampering defeats unchanged-model recovery", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.ThrowBeforeApply=true;host.Commit(plan,7);
            OperationManager.future.Push(new());Check(!Host().Recover().Recovered,"changed history accepted just because model matched");
        });
        Test("UI refresh failure preserves fault until explicit refresh and history proof succeed", () => {
            var host=Host();var plan=Plan(host);invalidate=()=>throw new Exception("pointer refresh failed");
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"UI refresh failure misreported stable success");
            Check(!Host().Recover().Recovered,"failed rebind cleared uncertainty");invalidate=null;
            Check(Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"verified new pointers did not recover known commit");
        });
        Test("reentrant host instance cannot commit while native Apply owns the boundary", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;native.OnApply=()=>Check(Host().Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"reentrant native commit admitted");
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.Applied && native.Applies==1,"reentrant attempt affected atomic commit");
        });
        Test("501 operations and non-insertion payloads never enter native Apply", () => {
            var host=Host();var plan=Plan(host);var oversized=plan with {OperationCount=501};
            Check(host.Commit(oversized,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"oversize accepted");
            plan=Plan(host);var wrong=plan with {OperationsJson=plan.OperationsJson.Replace("line.insert","line.delete",StringComparison.Ordinal)};
            Check(host.Commit(wrong,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"foreign mutation payload accepted");
        });
        Test("500 insertions remain one native Apply and one isolated history unit", () => {
            var host=Host();var result=host.Commit(Plan(host,500),7);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current!.Applies==1 && OperationManager.history.Count==2 && ScriptNodeInspector.instance!.scriptNode.scripts.Count==502,result.Message);
        });
        Test("missing snapshot synchronization and stale selected row pointers reject capture", () => {
            var native=AuthoringEditorSession.Current!;native.FreezeSnapshot=true;((ScriptNodeData)native.Snapshot.nodes[0]).Scripts[0].text="stale";
            Reject(()=>Host().Capture("zh-CN"),"stale session snapshot admitted");native.FreezeSnapshot=false;
            ScriptNodeInspector.instance!.selectedScriptItem=new(){index=0,scriptNode=ScriptNodeInspector.instance.scriptNode,inspector=ScriptNodeInspector.instance};
            Reject(()=>Host().Capture("zh-CN"),"unmapped row pointer admitted");Check(native.Applies==0,"capture mutated");
        });
        Test("Current replacement inside Check is rejected before Apply", () => {
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;
            native.OnCheck=()=>AuthoringEditorSession.Current=new(){studio=native.studio,Snapshot=native.studio.Project.Clone()};
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0 && AuthoringEditorSession.Current!.Applies==0,"foreign post-Check Current applied");
        });
        Test("null native receipt stays unknown despite the presence of snapshot history", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.NullReceipt=true;
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"missing native receipt became success");
            Reject(()=>Host().Capture("zh-CN"),"missing receipt fault was not retained");Check(!Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"null native receipt was replaced by a weaker history-only proof");
        });
        Test("wrong native history before snapshot prevents recovery of a changed model", () => {
            var host=Host();var plan=Plan(host);invalidate=()=>throw new Exception("UI refresh failed after native completion");host.Commit(plan,7);invalidate=null;
            var top=(AuthoringEditorSession.ProjectSnapshotOperation)OperationManager.history.Peek();((ScriptNodeData)top.before.nodes[0]).Scripts[0].text="not the captured before";
            Check(!Host().Recover().Recovered,"count-only history check accepted mismatched before snapshot");
        });
        Test("changed save and compile bookkeeping prevents unchanged-model recovery", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.ThrowBeforeApply=true;host.Commit(plan,7);
            OperationManager.saveDelta=1;Check(!Host().Recover().Recovered,"dirty/save state ignored");OperationManager.saveDelta=0;OperationManager.compileDelta=1;
            Check(!Host().Recover().Recovered,"compile history state ignored");
        });
        Test("snapshot history alone cannot prove native bookkeeping completed after Apply threw", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.ThrowAfterHistory=true;host.Commit(plan,7);
            OperationManager.saveDelta=0;OperationManager.compileDelta=0;
            Check(!Host().Recover().Recovered,"history-only evidence unlocked possibly incomplete native bookkeeping");
        });
        Test("recovery rejects changed bookkeeping after a successful native return and failed UI callback", () => {
            var host=Host();var plan=Plan(host);invalidate=()=>{OperationManager.saveDelta=0;throw new Exception("callback changed bookkeeping");};
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"callback failure not retained");invalidate=null;
            Check(!Host().Recover().Recovered,"recovery ignored bookkeeping drift after the native return");
        });
        Test("unchanged live model cannot hide a changed saved-project fingerprint during recovery", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.ThrowBeforeApply=true;host.Commit(plan,7);
            AuthoringEditorSession.Current.savedFingerprint="changed-saved-baseline";
            Check(!Host().Recover().Recovered,"saved project baseline drift was ignored");
        });
        Test("closing the write guard after Apply does not block readonly completion verification", () => {
            var host=Host();var plan=Plan(host);AuthoringEditorSession.Current!.OnApply=()=>{allowed=false;StudioCommon.instance!.autoSavePending=true;};
            Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.Applied,"new writes being blocked after Apply prevented readonly completion proof");
            Reject(()=>Host().Capture("zh-CN"),"closed write guard was bypassed for a new preview");
        });
        RecoveryUi();ResourceAvailability();Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");return failed==0?0:1;
    }
}
