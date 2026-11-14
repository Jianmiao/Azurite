using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class Program
{
    static ScriptNodeData SpeakerNode() => (ScriptNodeData)AuthoringEditorSession.Current!.studio.Project.nodes[0];
    static void PrepareSpeakerRows()
    {
        foreach(var line in SpeakerNode().Scripts)
            for(int slot=1;slot<=5;slot++) line.characters[slot]=new(){name="actor-"+slot,faceId="00",startingPos=slot,endingPos=slot,displayOrder=slot};
        for(int slot=1;slot<=5;slot++) AuthoringResourceCatalog.CharacterResources["fixture:actor-"+slot]=JsonSerializer.Serialize(new{resource_id="fixture:actor-"+slot,kind="character",catalog_version="fixture-catalog",available=true,native_key="actor-"+slot,name="Fixture actor",source="project",aliases=Array.Empty<string>(),metadata=new{},capabilities=new{stage=true,offstage=true,faces_loaded=true,faces=new[]{"00","01"}}});
        Fixture.Rebind();
    }
    static void SpeakerBatch()
    {
        Test("speaker compiler emits persistent-ID speaker-only patches and filters no-ops", () => {
            var selection=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("native-😀",2,"one",1),new AuthoringBatchRow("noop",8,"two",3),new AuthoringBatchRow("offstage",13,"three",0)});
            var result=AuthoringBatchSpeakerCompiler.TryCompile(selection,3);Check(result.IsValid && result.Plan!=null,result.Message);
            var plan=result.Plan!;using var json=JsonDocument.Parse(plan.OperationsJson);Check(json.RootElement.GetArrayLength()==2 && plan.OperationCount==2,"no-op filtering/count wrong");
            Check(json.RootElement[0].GetProperty("line").GetProperty("id").GetString()=="native-😀" && json.RootElement[1].GetProperty("line").GetProperty("id").GetString()=="offstage","persistent IDs or order changed");
            foreach(var op in json.RootElement.EnumerateArray())
            {Check(op.GetProperty("op").GetString()=="line.update" && op.EnumerateObject().Count()==3,"wrong operation");var set=op.GetProperty("set");Check(set.EnumerateObject().Count()==1 && set.GetProperty("speaker").EnumerateObject().Count()==1 && set.GetProperty("speaker").GetProperty("slot").GetInt32()==3,"mode/text/stage leaked into speaker patch");}
            Check(plan.Changes[1]==new AuthoringBatchSpeakerChange("offstage",0,3),"before/after speaker preview lost");
            if(plan.Changes is IList<AuthoringBatchSpeakerChange> list)Reject(()=>list[0]=new("forged",0,1),"published changes were mutable");
        });
        Test("speaker compiler keeps logical1024 and native500 independent", () => {
            AuthoringBatchSelection Many(int count,int changed)=>new(7,"node",Enumerable.Range(0,count).Select(i=>new AuthoringBatchRow("row-"+i,i,"text",i<changed?1:3)).ToArray());
            Check(AuthoringBatchSpeakerCompiler.TryCompile(Many(500,500),3).Plan?.OperationCount==500,"500 speaker operations rejected");
            Check(AuthoringBatchSpeakerCompiler.TryCompile(Many(501,501),3).Failure==AuthoringBatchNativeFailure.NativeOperationLimit,"501 native updates admitted");
            Check(AuthoringBatchPlanner.TryPlan(Many(1024,1024),AuthoringBatchAction.ChangeSpeaker,speakerSlot:3).IsValid,"logical1024 changed");
            Check(AuthoringBatchSpeakerCompiler.TryCompile(Many(1025,1025),3).LogicalFailure==AuthoringBatchFailure.TooManyOperations,"logical1025 not rejected");
            Check(AuthoringBatchSpeakerCompiler.TryCompile(Many(1024,500),3).Plan?.OperationCount==500,"counted selected instead of changed rows");
        });
        Test("speaker compiler rejects invalid slots and unencodable IDs without prefixes", () => {
            var selection=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("id",0,"text",1)});
            foreach(int slot in new[]{-1,0,6}){var r=AuthoringBatchSpeakerCompiler.TryCompile(selection,slot);Check(!r.IsValid && r.Plan==null && r.LogicalFailure==AuthoringBatchFailure.InvalidSpeaker,"invalid target slot admitted");}
            Check(AuthoringBatchSpeakerCompiler.TryCompile(selection,1).LogicalFailure==AuthoringBatchFailure.NoOp,"all-no-op not retained");
            var bad=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("good",0,"text",1),new AuthoringBatchRow("bad\uD800",1,"text",1)});
            var invalid=AuthoringBatchSpeakerCompiler.TryCompile(bad,3);Check(!invalid.IsValid && invalid.Plan==null && invalid.Failure==AuthoringBatchNativeFailure.InvalidUnicode,"opaque ID was rewritten by serializer");
        });
        Test("speaker output uses the complete pinned host Speaker schema", () => {
            var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NativeSchemaResponse.json"));Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals("b97d7bb5da9a86738a7d43eec79e5b26547bccb50d68400d92891741258f978d",StringComparison.OrdinalIgnoreCase),"schema drift");
            using var schema=JsonDocument.Parse(bytes);var slot=schema.RootElement.GetProperty("data").GetProperty("$defs").GetProperty("Speaker").GetProperty("oneOf")[0].GetProperty("oneOf")[0];
            var plan=AuthoringBatchSpeakerCompiler.TryCompile(new(7,"node",new[]{new AuthoringBatchRow("id",0,"text",0)}),5).Plan;Check(plan!=null,"valid schema-bound output missing");using var json=JsonDocument.Parse(plan!.OperationsJson);var speaker=json.RootElement[0].GetProperty("set").GetProperty("speaker");
            Check(!slot.GetProperty("additionalProperties").GetBoolean() && speaker.EnumerateObject().All(p=>slot.GetProperty("properties").TryGetProperty(p.Name,out _)) && speaker.GetProperty("slot").GetInt32()>=slot.GetProperty("properties").GetProperty("slot").GetProperty("minimum").GetInt32() && speaker.GetProperty("slot").GetInt32()<=slot.GetProperty("properties").GetProperty("slot").GetProperty("maximum").GetInt32(),"native Speaker shape/bounds mismatch");
        });
        Test("speaker Host previews detached changes and submits once through shared Check Apply", () => {
            PrepareSpeakerRows();var host=Host();var native=AuthoringEditorSession.Current!;var original=AuthoringEditorSession.Fingerprint(native.studio.Project);
            var preview=host.CaptureSpeaker(3);Check(preview.Plan.OperationCount==2 && preview.OffstageResetNotice.Contains("characters[0]"),"preview hid offstage reset or lost changed rows");
            Check(native.Checks==0 && native.Applies==0 && AuthoringEditorSession.Fingerprint(native.studio.Project)==original && AuthoringLines.DetachedPatches>0 && AuthoringGraph.Normalizes==1,"preview changed live state or skipped the one native detached Normalize after Patch");
            var result=host.CommitSpeaker(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(native.Events.SequenceEqual(new[]{"Check","Apply"}) && native.CheckedInput && native.CheckedResources && native.Applies==1 && OperationManager.history.Count==2,"speaker used a separate or repeated transaction");
            Check(SpeakerNode().Scripts.All(x=>x.speakerSlotNum==3 && x.isDialogScript) && SpeakerNode().Scripts[0].text=="before","speaker changed text/mode");
        });
        Test("existing offstage dialogue can choose an occupied onstage speaker and reset offstage data", () => {
            PrepareSpeakerRows();var row=SpeakerNode().Scripts[0];row.speakerSlotNum=0;row.characters[0]=new(){name="old-offstage",faceId="old-face",curve="old-curve",startingPos=4,endingPos=5,displayOrder=3,action=7,effect=2,appear=3,shapeOverride=4};Fixture.Rebind();
            var host=Host();var preview=host.CaptureSpeaker(3);Check(preview.Plan.Changes[0].BeforeSlot==0 && preview.OffstageResetNotice.Length>0,"offstage switch was hidden");
            var result=host.CommitSpeaker(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            var reset=row.characters[0];
            Check(row.speakerSlotNum==3 && row.isDialogScript && reset.name=="" && reset.faceId=="00" && reset.curve==null &&
                reset.startingPos==0 && reset.endingPos==0 && reset.displayOrder==1 && reset.emoticon==-1 && reset.action==0 && reset.effect==0 && reset.appear==0 && reset.shapeOverride==0 && row.characters[3].name=="actor-3",
                "offstage reset differs from pinned native constructor or target actor was replaced");
        });
        Test("speaker rejects narration control and invalid no-op rows without Apply", () => {
            PrepareSpeakerRows();var row=SpeakerNode().Scripts[0];row.isDialogScript=false;row.speakerSlotNum=3;Fixture.Rebind();
            Reject(()=>Host().CaptureSpeaker(3),"control no-op row was skipped as valid");row.isDialogScript=true;row.speakerSlotNum=0;row.characters[0].name=null;Fixture.Rebind();
            Reject(()=>Host().CaptureSpeaker(3),"narration silently converted to dialogue");Check(AuthoringEditorSession.Current!.Applies==0,"invalid mode applied");
        });
        Test("one missing target stage actor rejects the whole speaker batch", () => {
            PrepareSpeakerRows();SpeakerNode().Scripts[1].characters[3].name=null;
            Reject(()=>Host().CaptureSpeaker(3),"missing actor was fabricated or skipped");Check(AuthoringEditorSession.Current!.Applies==0,"partial speaker batch applied");
        });
        Test("speaker target requires real effective resource identity availability and faces", () => {
            PrepareSpeakerRows();string baseline=AuthoringResourceCatalog.CharacterResources["fixture:actor-3"];
            Action<JsonNode>[] mutate={j=>j["available"]=false,j=>j["resource_id"]="other",j=>j["kind"]="sound",j=>j["catalog_version"]="other",j=>j["native_key"]="other",j=>j["capabilities"]!["stage"]=false,j=>j["capabilities"]!["faces_loaded"]=false,j=>j["capabilities"]!["faces"]=new JsonArray("01"),j=>j["capabilities"]!["load_error"]="missing"};
            foreach(var change in mutate){var resource=JsonNode.Parse(baseline)!;change(resource);AuthoringResourceCatalog.CharacterResources["fixture:actor-3"]=resource.ToJsonString();Reject(()=>Host().CaptureSpeaker(3),"unverified target resource accepted");}
            Check(AuthoringEditorSession.Current!.Checks==0 && AuthoringEditorSession.Current.Applies==0,"resource rejection reached mutation");
        });
        Test("speaker recaptures resource validity before commit and after native Check", () => {
            PrepareSpeakerRows();var host=Host();var preview=host.CaptureSpeaker(3);AuthoringResourceCatalog.CharacterResources.Remove("fixture:actor-3");
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"vanished target applied");
            PrepareSpeakerRows();preview=host.CaptureSpeaker(3);AuthoringEditorSession.Current!.OnCheck=()=>AuthoringResourceCatalog.CharacterResources.Remove("fixture:actor-3");
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current.Applies==0,"Check-time resource change applied");
        });
        Test("speaker detached Patch must match an independent complete-project change proof", () => {
            PrepareSpeakerRows();var native=AuthoringEditorSession.Current!;string original=AuthoringEditorSession.Fingerprint(native.studio.Project);AuthoringLines.ExtraPatch=line=>line.voice="unexpected";
            Reject(()=>Host().CaptureSpeaker(3),"native detached Patch extra field change was self-certified");
            Check(native.Applies==0 && AuthoringEditorSession.Fingerprint(native.studio.Project)==original,"detached validation mutated the live project");
        });
        Test("speaker clone checks nested character-list detachment before reset", () => {
            PrepareSpeakerRows();var native=AuthoringEditorSession.Current!;string original=AuthoringEditorSession.Fingerprint(native.studio.Project);
            ProjectFileStore.CloneOverride=source=>{var clone=source.Clone();((ScriptNodeData)clone.nodes[0]).Scripts[0].characters=((ScriptNodeData)source.nodes[0]).Scripts[0].characters;return clone;};
            Reject(()=>Host().CaptureSpeaker(3),"aliased character list reached native Patch");Check(native.Applies==0 && AuthoringEditorSession.Fingerprint(native.studio.Project)==original,"nested alias mutated source");
        });
        Test("speaker rejects Normalize starting-position changes before live Apply", () => {
            PrepareSpeakerRows();var native=AuthoringEditorSession.Current!;native.Refresh(true);string original=AuthoringEditorSession.Fingerprint(native.studio.Project);string snapshot=AuthoringEditorSession.Fingerprint(native.Snapshot);
            AuthoringGraph.OnNormalize=project=>((ScriptNodeData)project.nodes[0]).Scripts[0].characters[3].startingPos=4;
            Reject(()=>Host().CaptureSpeaker(3),"Normalize-derived starting position was not checked before submission");
            Check(AuthoringGraph.Normalizes==1 && native.Checks==0 && native.Applies==0 && AuthoringEditorSession.Fingerprint(native.studio.Project)==original && AuthoringEditorSession.Fingerprint(native.Snapshot)==snapshot,"Normalize changed the original model/snapshot or entered live mutation");
        });
        Test("speaker rejects Normalize preview-field changes before live Apply", () => {
            PrepareSpeakerRows();var native=AuthoringEditorSession.Current!;native.Refresh(true);string original=AuthoringEditorSession.Fingerprint(native.studio.Project);string snapshot=AuthoringEditorSession.Fingerprint(native.Snapshot);
            AuthoringGraph.OnNormalize=project=>project.PreviewHeader="derived-header-change";
            Reject(()=>Host().CaptureSpeaker(3),"Normalize preview side effect was silently accepted");
            Check(AuthoringGraph.Normalizes==1 && native.Checks==0 && native.Applies==0 && AuthoringEditorSession.Fingerprint(native.studio.Project)==original && AuthoringEditorSession.Fingerprint(native.Snapshot)==snapshot,"Normalize preview changed a live baseline");
        });
        Test("speaker rejects changed mode or selection before Apply", () => {
            PrepareSpeakerRows();var host=Host();var preview=host.CaptureSpeaker(3);SpeakerNode().Scripts[1].isDialogScript=false;Fixture.Rebind();
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"mode drift applied");SpeakerNode().Scripts[1].isDialogScript=true;Fixture.Rebind();preview=host.CaptureSpeaker(3);Fixture.BatchIndices=new[]{1};
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"selection drift applied");
        });
        Test("speaker post-proof rejects unrelated stage or project changes", () => {
            PrepareSpeakerRows();var host=Host();var preview=host.CaptureSpeaker(3);var native=AuthoringEditorSession.Current!;native.OnAfterApply=()=>SpeakerNode().Scripts[0].characters[4].name="wrong-stage";
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered && native.Applies==1,"speaker accepted unrelated full-state changes");
        });
        Test("speaker post-proof rejects missing required offstage reset", () => {
            PrepareSpeakerRows();SpeakerNode().Scripts[0].characters[0].name="legacy-offstage";var old=SpeakerNode().Scripts[0].characters[0].Clone();var host=Host();var preview=host.CaptureSpeaker(3);
            AuthoringEditorSession.Current!.OnAfterApply=()=>SpeakerNode().Scripts[0].characters[0]=old;
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"speaker-only numeric check accepted missing reset");
        });
        Test("speaker refresh failure recovers through the same verified uncertainty owner", () => {
            PrepareSpeakerRows();var host=Host();var preview=host.CaptureSpeaker(3);invalidate=()=>throw new Exception("UI refresh failed");
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"refresh failure hidden");invalidate=null;
            Check(Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"speaker used insertion/text recovery or retried");
        });
        Test("speaker fault blocks cards and SetText until unchanged-state recovery", () => {
            PrepareSpeakerRows();var host=Host();var preview=host.CaptureSpeaker(3);var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"entered speaker failure misreported");Reject(()=>Host().Capture("zh-CN"),"card bypassed speaker fault");Reject(()=>Host().CaptureSetText("new"),"SetText bypassed speaker fault");
            Check(Host().Recover().Recovered && native.Applies==1,"speaker fault recovery retried or failed");
        });
        Test("SetText fault blocks speaker and speaker cannot infer success from thrown history", () => {
            PrepareSpeakerRows();var host=Host();var text=host.CaptureSetText("new");var native=AuthoringEditorSession.Current!;native.ThrowBeforeApply=true;host.CommitSetText(text);
            Reject(()=>Host().CaptureSpeaker(3),"speaker bypassed SetText uncertainty");Check(host.Recover().Recovered,"unchanged recovery failed");native.ThrowBeforeApply=false;var speaker=host.CaptureSpeaker(3);native.ThrowAfterHistory=true;
            Check(host.CommitSpeaker(speaker).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"history-only speaker result recovered as success");
        });
        Test("speaker shares reentry gate and F1 early-rejection token revocation", () => {
            PrepareSpeakerRows();var host=Host();var workflow=new CardPackV2Workflow(host);var draft=RecoveryDraft();var card=workflow.Preview(draft).Preview!;var old=host.CaptureSpeaker(3);host.CaptureSpeaker(4);
            Check(host.CommitSpeaker(old).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && !workflow.Confirm(card.ConfirmationToken,draft).IsSuccess && AuthoringEditorSession.Current!.Applies==0,"speaker early refusal bypassed F1");
            var peer=Host();var text=peer.CaptureSetText("text");var preview=host.CaptureSpeaker(3);AuthoringEditorSession.Current!.OnApply=()=>Check(peer.CommitSetText(text).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"SetText reentered speaker Apply");
            Check(host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current.Applies==1,"speaker outer shared transaction failed");
        });
        Test("speaker batch500 and 1024-selection no-ops stay atomic while501 rejects", () => {
            var node=SpeakerNode();node.Scripts=Enumerable.Range(0,1024).Select(i=>new ScriptData{LineId=i==0?"opaque-anchor":"row-"+i,text="keep",speakerSlotNum=i<500?2:3}).ToList();PrepareSpeakerRows();Fixture.BatchIndices=Enumerable.Range(0,1024).ToArray();var host=Host();var preview=host.CaptureSpeaker(3);
            Check(preview.Plan.OperationCount==500 && host.CommitSpeaker(preview).Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current!.Applies==1,"changed-row count or atomic boundary wrong");
            foreach(var row in node.Scripts.Take(501))row.speakerSlotNum=2;Fixture.Rebind();Reject(()=>host.CaptureSpeaker(3),"501 speaker operations admitted");Check(AuthoringEditorSession.Current!.Applies==1,"501 rejection wrote a prefix");
        });
    }
}
