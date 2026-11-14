using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;
using System.Text.Json;

static partial class Program
{
    static ScriptNodeData PrepareDeletionRows(int count=2)
    {
        var node=(ScriptNodeData)AuthoringEditorSession.Current!.studio.Project.nodes[0];
        node.Scripts=new(Enumerable.Range(0,count).Select(i=>new ScriptData{LineId=i==0?"opaque-anchor":"line-"+i,text="text-"+i,speakerSlotNum=0,isDialogScript=false}));
        Fixture.Rebind();return node;
    }
    static void SetDeleteAnchor(int index)
    {
        var inspector=ScriptNodeInspector.instance!;foreach(var row in inspector.scriptNodeListItemsCache)row.selected=row.index==index;
        inspector.selectedScriptItem=inspector.scriptNodeListItemsCache[index];
    }
    static void DeleteBatch()
    {
        Test("delete compiler uses stable-ID-only operations in descending source order", () => {
            var selection=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("a",1,"first",1),new AuthoringBatchRow("b",4,"middle",2),new AuthoringBatchRow("c",9,"last",3)});
            var result=AuthoringBatchDeleteCompiler.TryCompile(selection);Check(result.IsValid && result.Plan!=null,result.Message);
            using var json=JsonDocument.Parse(result.Plan!.OperationsJson);Check(result.Plan.RemovedRows.Select(r=>r.StableId).SequenceEqual(new[]{"c","b","a"}),"delete operation ordering is wrong");
            int i=0;foreach(var op in json.RootElement.EnumerateArray()){Check(op.GetProperty("op").GetString()=="line.delete" && op.EnumerateObject().Count()==2 && op.GetProperty("line").EnumerateObject().Count()==1 && op.GetProperty("line").GetProperty("id").GetString()==new[]{"c","b","a"}[i++],"delete emitted indexes/node deletion or wrong IDs");}
            Check(result.Plan.RemovedRows[1].Text=="middle" && result.Plan.RemovedRows[1].Index==4,"exact removed-row preview lost");
        });
        Test("delete compiler preserves logical1024 and native500 without splitting", () => {
            AuthoringBatchSelection Many(int count)=>new(7,"node",Enumerable.Range(0,count).Select(i=>new AuthoringBatchRow("row-"+i,i,"text",1)).ToArray());
            Check(AuthoringBatchDeleteCompiler.TryCompile(Many(500)).Plan?.OperationCount==500,"500 deletions rejected");
            var tooMany=AuthoringBatchDeleteCompiler.TryCompile(Many(501));Check(!tooMany.IsValid && tooMany.Plan==null && tooMany.Failure==AuthoringBatchNativeFailure.NativeOperationLimit,"501 deleted a prefix or split batches");
            Check(AuthoringBatchPlanner.TryPlan(Many(1024),AuthoringBatchAction.Delete).IsValid,"logical1024 was lowered");
            Check(AuthoringBatchDeleteCompiler.TryCompile(Many(1025)).LogicalFailure==AuthoringBatchFailure.TooManyOperations,"logical1025 accepted");
        });
        Test("delete compiler rejects malformed selection and unencodable ID atomically", () => {
            Check(AuthoringBatchDeleteCompiler.TryCompile(null).LogicalFailure==AuthoringBatchFailure.NullSelection,"null selection accepted");
            Check(AuthoringBatchDeleteCompiler.TryCompile(new(7,"node",Array.Empty<AuthoringBatchRow>())).LogicalFailure==AuthoringBatchFailure.EmptySelection,"empty deletion published");
            foreach(var rows in new[]{new[]{new AuthoringBatchRow("dup",0,"a",1),new AuthoringBatchRow("dup",1,"b",1)},new[]{new AuthoringBatchRow("a",5,"a",1),new AuthoringBatchRow("b",1,"b",1)}})
            {var r=AuthoringBatchDeleteCompiler.TryCompile(new(7,"node",rows));Check(!r.IsValid && r.Plan==null && r.LogicalFailure==AuthoringBatchFailure.InvalidSelection,"invalid source mapping accepted");}
            var invalid=AuthoringBatchDeleteCompiler.TryCompile(new(7,"node",new[]{new AuthoringBatchRow("good",0,"a",1),new AuthoringBatchRow("bad\uD800",1,"b",1)}));
            Check(!invalid.IsValid && invalid.Plan==null && invalid.Failure==AuthoringBatchNativeFailure.InvalidUnicode,"serializer changed native delete target");
        });
        Test("delete preview remains immutable and satisfies the saved full native schema", () => {
            var rows=new List<AuthoringBatchRow>{new("opaque-😀",2,"exact\r\ntext",0)};var selection=new AuthoringBatchSelection(7,"node",rows);var plan=AuthoringBatchDeleteCompiler.TryCompile(selection).Plan;Check(plan!=null,"valid plan missing");rows.Clear();
            Check(plan!.RemovedRows.Count==1 && plan.RemovedRows[0].Text=="exact\r\ntext","caller mutation changed removed preview");if(plan.RemovedRows is IList<AuthoringBatchRow> mutable)Reject(()=>mutable.Clear(),"removed preview was writable");
            var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NativeSchemaResponse.json"));Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals("b97d7bb5da9a86738a7d43eec79e5b26547bccb50d68400d92891741258f978d",StringComparison.OrdinalIgnoreCase),"schema changed");
            using var schema=JsonDocument.Parse(bytes);var shape=schema.RootElement.GetProperty("data").GetProperty("$defs").GetProperty("EditOperation").GetProperty("oneOf").EnumerateArray().Single(x=>x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()=="line.delete");
            using var json=JsonDocument.Parse(plan.OperationsJson);var operation=json.RootElement[0];Check(!shape.GetProperty("additionalProperties").GetBoolean() && operation.EnumerateObject().All(p=>shape.GetProperty("properties").TryGetProperty(p.Name,out _)) && operation.GetProperty("line").GetProperty("id").GetString()=="opaque-😀","native deletion schema/identity mismatch");
        });
        Test("delete noncontiguous reverse gesture removes only exact rows with one Check Apply", () => {
            var node=PrepareDeletionRows(6);Fixture.BatchIndices=new[]{4,1};var host=Host();var preview=host.CaptureDelete();var native=AuthoringEditorSession.Current!;
            Check(preview.Plan.RemovedRows.Select(r=>r.StableId).SequenceEqual(new[]{"line-4","line-1"}) && native.Applies==0,"reverse gesture did not become descending stable IDs");
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(node.Scripts.Select(x=>x.LineId).SequenceEqual(new[]{"opaque-anchor","line-2","line-3","line-5"}) && native.Events.SequenceEqual(new[]{"Check","Apply"}) && OperationManager.history.Count==2,"wrong removed set/order or more than one history boundary");
        });
        Test("delete current anchor accepts an observed surviving native anchor", () => {
            PrepareDeletionRows();Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();Check(preview.RemovesCurrentAnchor && !preview.EmptiesNode,"anchor consequence missing from preview");
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(ScriptNodeInspector.instance!.scriptNode.scripts.Single().LineId=="line-1" && AuthoringEditorSession.Current!.Applies==1,"removed anchor was reused");
        });
        Test("delete active tail permits no active row while preserving the surviving cache", () => {
            PrepareDeletionRows();Fixture.BatchIndices=new[]{1};SetDeleteAnchor(1);var host=Host();var preview=host.CaptureDelete();Fixture.PostAnchorIndex=-1;Fixture.KeepDeselectedAnchorReference=true;
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(ScriptNodeInspector.instance!.scriptNode.scripts.Single().LineId=="opaque-anchor" && ScriptNodeInspector.instance.selectedScriptItem?.selected==false,"native deselection was replaced with a fake selection");
        });
        Test("delete last line preserves empty node connections and native no-selection state", () => {
            var node=PrepareDeletionRows(1);Guid id=node.Guid;var project=AuthoringEditorSession.Current!.studio.Project;var entry=project.nodes.OfType<EntryNodeData>().Single();Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();
            Check(preview.EmptiesNode && preview.EmptyNodeNotice.Length>0 && preview.RemovesCurrentAnchor,"empty-node consequence missing");Fixture.KeepDeselectedAnchorReference=true;
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(node.Guid==id && node.Scripts.Count==0 && project.nodes.Count==2 && entry.ConnectionsTo.SequenceEqual(new[]{id}) && ScriptNodeInspector.instance!.scriptNodeListItemsCache.Count==0,"last-line deletion removed the node/edge or invented a placeholder");
            Check(AuthoringEditorSession.Current!.Applies==1 && OperationManager.history.Count==2,"empty-node delete split native history");
        });
        Test("delete all selected lines permits a null anchor without manufacturing a row", () => {
            var node=PrepareDeletionRows(3);Fixture.BatchIndices=new[]{2,0,1};var host=Host();var preview=host.CaptureDelete();var result=host.CommitDelete(preview);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts.Count==0 && ScriptNodeInspector.instance!.selectedScriptItem==null,result.Message);
        });
        Test("delete independently proves required successor starting-position changes", () => {
            var node=PrepareDeletionRows(3);for(int i=0;i<3;i++)node.Scripts[i].characters[2]=new(){name="same",faceId="00",endingPos=new[]{1,5,4}[i],startingPos=new[]{2,1,5}[i]};Fixture.BatchIndices=new[]{1};
            var host=Host();var preview=host.CaptureDelete();Check(preview.DerivedChangesJson.Contains("line-2",StringComparison.Ordinal),"derived surviving-line change absent from preview");
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].LineId=="line-2" && node.Scripts[1].characters[2].startingPos==1 && node.Scripts[1].characters[2].endingPos==4,result.Message);
        });
        Test("delete empty node passes predecessor state through to downstream nodes", () => {
            var node=PrepareDeletionRows(1);node.Scripts[0].characters[2]=new(){name="same",faceId="00",startingPos=2,endingPos=5};var later=new ScriptNodeData{Guid=Guid.NewGuid(),Scripts=new(){new(){LineId="downstream",speakerSlotNum=0,isDialogScript=false}}};later.Scripts[0].characters[2]=new(){name="same",faceId="00",startingPos=5,endingPos=4};node.ConnectionsTo.Add(later.Guid);AuthoringEditorSession.Current!.studio.Project.nodes.Add(later);Fixture.BatchIndices=new[]{0};
            var host=Host();var preview=host.CaptureDelete();var result=host.CommitDelete(preview);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts.Count==0 && node.ConnectionsTo.Single()==later.Guid && later.Scripts[0].characters[2].startingPos==2,result.Message);
        });
        Test("delete first line permits only actual derived project-preview fields", () => {
            var node=PrepareDeletionRows();node.Scripts[0].bgName=11;node.Scripts[1].bgName=22;AuthoringEditorSession.Current!.studio.Project.PreviewBgName=11;Fixture.BatchIndices=new[]{0};
            var host=Host();var preview=host.CaptureDelete();using var derived=JsonDocument.Parse(preview.DerivedChangesJson);Check(derived.RootElement.GetProperty("project_preview").GetProperty("after").GetProperty("background").GetUInt32()==22,"preview omitted exact derived background");
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current.studio.Project.PreviewBgName==22,result.Message);
        });
        Test("delete rejects incorrect normalized positions and arbitrary preview overwrite before live Apply", () => {
            var node=PrepareDeletionRows();node.Scripts[0].characters[2]=new(){name="same",faceId="00",startingPos=2,endingPos=2};Fixture.BatchIndices=new[]{1};var native=AuthoringEditorSession.Current!;native.Refresh(true);string original=AuthoringEditorSession.Fingerprint(native.studio.Project);
            AuthoringGraph.OnNormalize=p=>((ScriptNodeData)p.nodes[0]).Scripts[0].characters[2].startingPos=5;Reject(()=>Host().CaptureDelete(),"arbitrary startingPos was blessed as normalization");
            AuthoringGraph.OnNormalize=p=>p.PreviewHeader="unproven";Reject(()=>Host().CaptureDelete(),"arbitrary preview field was blessed as normalization");
            Check(native.Applies==0 && AuthoringEditorSession.Fingerprint(native.studio.Project)==original && AuthoringEditorSession.Fingerprint(native.Snapshot)==original,"trial changed original project/snapshot");
        });
        Test("delete validates the full native edit graph and never bypasses its errors", () => {
            PrepareDeletionRows();var host=Host();Check(host.CaptureDelete().EmptiesNode,"empty node was categorically refused");AuthoringGraph.OnRequireValid=_=>throw new InvalidOperationException("STAGE_CONFLICT");
            Reject(()=>host.CaptureDelete(),"native edit validation was bypassed");Check(AuthoringEditorSession.Current!.Applies==0 && AuthoringGraph.Validations>0,"invalid graph entered live Apply");
        });
        Test("delete rejects an aliased detached Scripts container before removing rows", () => {
            PrepareDeletionRows();var native=AuthoringEditorSession.Current!;string before=AuthoringEditorSession.Fingerprint(native.studio.Project);ProjectFileStore.CloneOverride=p=>{var clone=p.Clone();((ScriptNodeData)clone.nodes[0]).Scripts=((ScriptNodeData)p.nodes[0]).Scripts;return clone;};
            Reject(()=>Host().CaptureDelete(),"shared Scripts list was edited during preview");Check(AuthoringEditorSession.Fingerprint(native.studio.Project)==before && native.Applies==0,"preview removed live/source rows");
        });
        Test("delete refuses stale foreign selection node and project switch", () => {
            PrepareDeletionRows();var host=Host();var preview=host.CaptureDelete();Fixture.BatchIndices=new[]{1};Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"stale membership applied");Fixture.BatchIndices=new[]{0,1};
            ScriptNodeInspector.instance!.scriptNodeListItemsCache[1].scriptNode=new();Reject(()=>host.CaptureDelete(),"foreign native row accepted");Fixture.Rebind();
            var native=AuthoringEditorSession.Current!;native.studio.Project.nodes.Add(new ScriptNodeData{Guid=Guid.NewGuid(),Scripts=new(){new(){LineId="other-node",speakerSlotNum=0,isDialogScript=false}}});preview=host.CaptureDelete();Fixture.ActiveNodeIndex=2;Fixture.Rebind();Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"node switch applied");
            Fixture.Reset();Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"project switch applied");
        });
        Test("delete rechecks selection and catalog after native Check", () => {
            PrepareDeletionRows();var host=Host();var preview=host.CaptureDelete();var native=AuthoringEditorSession.Current!;native.OnCheck=()=>Fixture.BatchIndices=new[]{1};
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"Check-time selection drift applied");native.OnCheck=null;Fixture.BatchIndices=new[]{0,1};preview=host.CaptureDelete();native.OnCheck=()=>AuthoringResourceCatalog.CatalogVersion="changed";
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Applies==0,"Check-time catalog drift applied");
        });
        Test("delete invalidates card and speaker previews through the same session generation", () => {
            PrepareDeletionRows(3);foreach(var row in SpeakerNode().Scripts){row.isDialogScript=true;row.speakerSlotNum=2;}PrepareSpeakerRows();var cardHost=Host();var workflow=new CardPackV2Workflow(cardHost);var draft=RecoveryDraft();var card=workflow.Preview(draft).Preview!;var speakerHost=Host();var speaker=speakerHost.CaptureSpeaker(3);var textHost=Host();var text=textHost.CaptureSetText("new");Fixture.BatchIndices=new[]{1};var deletion=Host();
            Check(deletion.CommitDelete(deletion.CaptureDelete()).Disposition==CardPackV2CommitDisposition.Applied,"valid delete failed");Check(speakerHost.CommitSpeaker(speaker).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && textHost.CommitSetText(text).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && !workflow.Confirm(card.ConfirmationToken,draft).IsSuccess && AuthoringEditorSession.Current!.Applies==1,"other client old preview survived delete");
        });
        Test("delete F1 early refusal revokes a card token without native entry", () => {
            PrepareDeletionRows();var host=Host();var workflow=new CardPackV2Workflow(host);var draft=RecoveryDraft();var card=workflow.Preview(draft).Preview!;var old=host.CaptureDelete();host.CaptureDelete();
            Check(host.CommitDelete(old).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && !workflow.Confirm(card.ConfirmationToken,draft).IsSuccess && AuthoringEditorSession.Current!.Applies==0,"delete early capture rejection skipped revocation");
        });
        Test("delete unchanged entered failure shares conservative recovery without retry", () => {
            PrepareDeletionRows();var host=Host();var preview=host.CaptureDelete();var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"entered deletion failure was hidden");Reject(()=>Host().CaptureSetText("new"),"SetText bypassed delete fault");Reject(()=>Host().CaptureSpeaker(3),"speaker bypassed delete fault");
            Check(Host().Recover().Recovered && native.Applies==1,"unchanged delete recovery retried or failed");
        });
        Test("delete empty-node refresh failure can recover its verified applied state", () => {
            PrepareDeletionRows(1);Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();invalidate=()=>throw new Exception("cache refresh failure");
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"refresh failure misreported");invalidate=null;
            Check(Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"empty applied deletion recovery required a removed anchor or retried");
        });
        Test("delete missing receipt keeps fault visible to other clients even with empty selection", () => {
            PrepareDeletionRows(1);Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();var native=AuthoringEditorSession.Current!;native.NullReceipt=true;
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"null receipt became success");var workflow=new CardPackV2Workflow(Host());var state=workflow.Preview(RecoveryDraft());
            Check(state.Code=="RECOVERY_REQUIRED" && workflow.RecoveryRequired && !Host().Recover().Recovered && native.Applies==1,"empty post-delete fault was hidden by missing-anchor validation or fake recovery");
        });
        Test("delete throw after history cannot be relabeled recovered success", () => {
            PrepareDeletionRows(1);Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();AuthoringEditorSession.Current!.ThrowAfterHistory=true;
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"history-only delete proof accepted");
        });
        Test("delete partial empty-model mutation without history remains unresolved", () => {
            PrepareDeletionRows(1);Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();var native=AuthoringEditorSession.Current!;native.PartialMutation=true;
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered && native.Applies==1 && OperationManager.history.Count==1,"partial empty result was treated as unchanged or complete");
        });
        Test("delete uses the shared gate for native Apply reentry", () => {
            PrepareDeletionRows();Fixture.BatchIndices=new[]{1};var host=Host();var preview=host.CaptureDelete();var peer=Host();var peerPreview=peer.CaptureDelete();var native=AuthoringEditorSession.Current!;
            native.OnApply=()=>{Check(peer.CommitDelete(peerPreview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"delete reentry acquired a second owner");Check(!peer.Recover().Recovered,"reentrant recovery crossed the native gate");};
            var result=host.CommitDelete(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && native.Applies==1 && native.Checks==1,result.Message);
        });
        Test("delete result proof rejects surviving text mutation and stale selected cache references", () => {
            PrepareDeletionRows();Fixture.BatchIndices=new[]{1};var host=Host();var preview=host.CaptureDelete();AuthoringEditorSession.Current!.OnAfterApply=()=>SpeakerNode().Scripts[0].text="corrupt";
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"unrelated surviving text mutation accepted");
        });
        Test("delete must not treat a still-selected stale row as no active anchor", () => {
            PrepareDeletionRows(1);Fixture.BatchIndices=new[]{0};var host=Host();var preview=host.CaptureDelete();Fixture.KeepDeselectedAnchorReference=true;Fixture.StaleReferenceSelected=true;
            Check(host.CommitDelete(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"selected stale cache pointer accepted under delete exception");
        });
        Test("delete 500 rows is one empty-node transaction and501 never applies a prefix", () => {
            var node=PrepareDeletionRows(500);Fixture.BatchIndices=Enumerable.Range(0,500).ToArray();var host=Host();var preview=host.CaptureDelete();var result=host.CommitDelete(preview);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts.Count==0 && AuthoringEditorSession.Current!.Applies==1 && OperationManager.history.Count==2,result.Message);
            PrepareDeletionRows(501);Fixture.BatchIndices=Enumerable.Range(0,501).ToArray();Reject(()=>host.CaptureDelete(),"501 deletes admitted");Check(AuthoringEditorSession.Current!.Applies==1,"501 executed a prefix or split");
        });
    }
}
