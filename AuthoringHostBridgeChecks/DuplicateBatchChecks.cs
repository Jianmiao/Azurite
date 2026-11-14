using Azurite.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;

static partial class Program
{
    static AuthoringBatchSelection DuplicateSelection(int count=1) => new(7,"node-opaque",Enumerable.Range(0,count).Select(i=>new AuthoringBatchRow("source-"+i,i*3,"text-"+i,0)).ToArray());
    static AuthoringBatchDuplicateInput[] DuplicateInputs(AuthoringBatchSelection selection) => selection.Rows.Select(r=>new AuthoringBatchDuplicateInput(r.StableId,
        JsonSerializer.Serialize(new{inherit=new{from=new{id=r.StableId}},mode="narration",speaker=(object?)null,text=r.Text,stage=Array.Empty<object>(),highlighted_slots=Array.Empty<int>(),place="",extra_script="",scene=new{voice=new{action="none"}}}))).ToArray();
    static void DuplicatePure()
    {
        Test("duplicate compiler inserts beside each noncontiguous original using actual ID references",()=>{
            var selection=DuplicateSelection(3);var result=AuthoringBatchDuplicateCompiler.TryCompile(selection,DuplicateInputs(selection));Check(result.IsValid,result.Message);var plan=result.Plan!;
            Check(plan.Copies.Select(c=>c.TargetIndex).SequenceEqual(new[]{1,5,9}) && plan.Copies.Select(c=>c.SourceId).SequenceEqual(new[]{"source-0","source-1","source-2"}),"copy ordering or target positions changed");
            using var json=JsonDocument.Parse(plan.OperationsJson);for(int i=0;i<3;i++){var op=json.RootElement[i];Check(op.GetProperty("op").GetString()=="line.insert" && op.GetProperty("node").GetProperty("id").GetString()=="node-opaque" && op.GetProperty("after_line").GetProperty("id").GetString()=="source-"+i && op.GetProperty("line").GetProperty("inherit").GetProperty("from").GetProperty("id").GetString()=="source-"+i,"copy did not use its own original ID anchor/from reference");}
            Check(plan.Copies.Select(c=>c.Alias).Distinct().Count()==3,"copy aliases are reused");
        });
        Test("duplicate keeps logical1024 and native500 limits independent",()=>{
            var logical=DuplicateSelection(1024);Check(AuthoringBatchPlanner.TryPlan(logical,AuthoringBatchAction.Duplicate).IsValid,"logical1024 changed");Check(!AuthoringBatchPlanner.TryPlan(DuplicateSelection(1025),AuthoringBatchAction.Duplicate).IsValid,"logical1025 accepted");
            var max=DuplicateSelection(500);Check(AuthoringBatchDuplicateCompiler.TryCompile(max,DuplicateInputs(max)).Plan?.OperationCount==500,"native500 rejected");var over=DuplicateSelection(501);var rejected=AuthoringBatchDuplicateCompiler.TryCompile(over,DuplicateInputs(over));Check(!rejected.IsValid && rejected.Plan==null && rejected.Failure==AuthoringBatchNativeFailure.NativeOperationLimit,"native501 not rejected atomically");
        });
        Test("duplicate target-index overflow rejects before any wrapping edit",()=>{
            var overflow=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("source",int.MaxValue,"text",0)});Check(!AuthoringBatchPlanner.TryPlan(overflow,AuthoringBatchAction.Duplicate).IsValid,"duplicate target index wrapped");
        });
        Test("duplicate planner rejects excess source count without allocating an expanded edit list",()=>{
            var huge=DuplicateSelection(80000);long before=GC.GetAllocatedBytesForCurrentThread();var result=AuthoringBatchPlanner.TryPlan(huge,AuthoringBatchAction.Duplicate);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(!result.IsValid && result.Failure==AuthoringBatchFailure.TooManyOperations && allocated<1000000,"oversized duplicate plan expanded before rejection: "+allocated);
        });
        Test("duplicate compiler preserves complete writable fields and raw curve numeric data",()=>{
            var selection=DuplicateSelection();var inputs=DuplicateInputs(selection);var line=JsonNode.Parse(inputs[0].LineInputJson)!;
            line["stage"]=JsonNode.Parse("[{\"slot\":2,\"resource_id\":\"opaque-actor\",\"face_id\":\"01\",\"ending_position\":3,\"display_order\":4,\"action\":\"Curve\",\"emoticon\":\"Heart\",\"effect\":\"Shot\",\"appear\":\"DL\",\"shape_overrides\":[\"Black\"],\"curve\":{\"duration\":1.2300,\"extension\":{\"value\":2e-3}}}]");line["scene"]=JsonNode.Parse("{\"background_effect\":{\"action\":\"set\",\"resource_id\":\"effect\"},\"transition\":{\"action\":\"set\",\"resource_id\":\"transition\"},\"bgm\":{\"action\":\"set\",\"resource_id\":\"bgm\"},\"sound\":{\"action\":\"set\",\"resource_id\":\"sound\"},\"popup\":{\"action\":\"set\",\"resource_id\":\"popup\"},\"voice\":{\"action\":\"set\",\"resource_id\":\"voice\"}}");line["extra_script"]="#wait;42";line["place"]="place";inputs[0]=new(inputs[0].StableId,line.ToJsonString());
            var result=AuthoringBatchDuplicateCompiler.TryCompile(selection,inputs);Check(result.IsValid,result.Message);Check(result.Plan!.OperationsJson.Contains("1.2300",StringComparison.Ordinal) && result.Plan.OperationsJson.Contains("2e-3",StringComparison.Ordinal),"curve numeric lexemes changed");
            using var nativeSchema=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"NativeSchemaResponse.json")));var defs=nativeSchema.RootElement.GetProperty("data").GetProperty("$defs");using var json=JsonDocument.Parse(result.Plan.OperationsJson);var payload=json.RootElement[0].GetProperty("line");Check(payload.EnumerateObject().All(p=>defs.GetProperty("LineInput").GetProperty("properties").TryGetProperty(p.Name,out _)) && payload.GetProperty("scene").EnumerateObject().Count()==6,"native schema fields/content lost");
        });
        Test("duplicate compiler rejects incomplete foreign ambiguous or unencodable inputs",()=>{
            var selection=DuplicateSelection();var good=DuplicateInputs(selection);Check(!AuthoringBatchDuplicateCompiler.TryCompile(null,good).IsValid && !AuthoringBatchDuplicateCompiler.TryCompile(selection,null).IsValid,"missing copy data accepted");
            foreach(int mutation in Enumerable.Range(0,5)){var line=JsonNode.Parse(good[0].LineInputJson)!;if(mutation==0)line["inherit"]!["from"]!["id"]="foreign";if(mutation==1)line.AsObject().Remove("stage");if(mutation==2)line["line_id"]="fabricated";if(mutation==3)line["text"]="different";if(mutation==4)line["mode"]="invented";Check(!AuthoringBatchDuplicateCompiler.TryCompile(selection,new[]{new AuthoringBatchDuplicateInput("source-0",line.ToJsonString())}).IsValid,"malformed copy input accepted");}
            var bad=new AuthoringBatchSelection(7,"bad\udfff",selection.Rows);Check(!AuthoringBatchDuplicateCompiler.TryCompile(bad,good).IsValid,"invalid node identity rewritten");
            var duplicate=good[0].LineInputJson.Replace("\"mode\":\"narration\"","\"mode\":\"narration\",\"mode\":\"control\"",StringComparison.Ordinal);Check(!AuthoringBatchDuplicateCompiler.TryCompile(selection,new[]{new AuthoringBatchDuplicateInput("source-0",duplicate)}).IsValid,"duplicate JSON property accepted");
        });
        Test("duplicate preview freezes full caller input and source identities",()=>{
            var selection=DuplicateSelection();var inputs=DuplicateInputs(selection);var result=AuthoringBatchDuplicateCompiler.TryCompile(selection,inputs);Check(result.IsValid,result.Message);string json=result.Plan!.OperationsJson;inputs[0]=new("changed","{}");Check(result.Plan.Inputs[0].StableId=="source-0" && result.Plan.OperationsJson==json,"caller mutated immutable copy payload");
        });
    }

    static ScriptNodeData PrepareDuplicateRows(int count=3)
    {
        var node=PrepareMultilineRows(count);
        foreach(var line in node.Scripts){line.voice="";line.bgFriendlyName="source-friendly";for(int slot=0;slot<6;slot++)line.characters[slot]=new(new Script.CharacterRecord());}
        Fixture.Rebind();return node;
    }
    static void DuplicateHost()
    {
        Test("duplicate Host copies each selected original beside its own anchor in one native transaction",()=>{
            var node=PrepareDuplicateRows(5);Fixture.BatchIndices=new[]{3,0};var host=Host();var p=host.CaptureDuplicate();Check(p.Plan.Copies.Select(c=>c.SourceId).SequenceEqual(new[]{"opaque-anchor","line-3"}) && p.CopyNotice.Length>0,"preview order/notice missing");
            var r=host.CommitDuplicate(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied,r.Message);
            Check(node.Scripts.Select(l=>l.text).SequenceEqual(new[]{"text-0","text-0","text-1","text-2","text-3","text-3","text-4"}) && node.Scripts.Select(l=>l.LineId).Distinct().Count()==7,"copy interleaving or identities wrong");
            Check(AuthoringEditorSession.Current!.Events.SequenceEqual(new[]{"Check","Apply"}) && OperationManager.history.Count==2 && AuthoringLines.FromResolutions>0,"copy bypassed native from resolver or split history");
        });
        Test("duplicate preserves complete actor scene curve commands highlights and hidden group fields",()=>{
            var node=PrepareDuplicateRows(1);PrepareSpeakerRows();var source=node.Scripts[0];source.speakerSlotNum=2;source.text="原始\r\n文本😀";source.characters[2].action=8;source.characters[2].effect=1;source.characters[2].emoticon=4;source.characters[2].appear=4;source.characters[2].shapeOverride=5;source.characters[2].endingPos=5;source.characters[2].curve=JsonDocument.Parse("{\"duration\":1.2500,\"tracks\":{\"x\":[{\"time\":0,\"value\":0.25}]},\"extra\":{\"opaque\":[1,2,3]}}").RootElement.Clone();
            source.bgName=22;source.bgFriendlyName="older friendly name";source.bgEffect=33;source.transition=44;source.bgmId=55;source.sound="sound-key";source.popup="popup-key";source.voice="voice-key";source.selectionGroup=812;source.placeText="place";source.additionalPrompt="#wait;42\n#bgshake";source.highlightedSlotNums=new(){5,2};
            foreach(var pair in new[]{("background","22"),("background_effect","33"),("transition","44"),("bgm","55"),("sound","sound-key"),("popup","popup-key"),("voice","voice-key")})AddMultilineScene(pair.Item1,pair.Item2);
            Fixture.Rebind();var host=Host();var p=host.CaptureDuplicate();using(var ops=JsonDocument.Parse(p.Plan.OperationsJson))Check(!ops.RootElement[0].GetProperty("line").GetProperty("scene").TryGetProperty("background",out _),"background reassignment would replace source friendly name");
            var r=host.CommitDuplicate(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied,r.Message);var copy=node.Scripts[1];
            Check(copy.text==source.text && copy.isDialogScript && copy.speakerSlotNum==2 && copy.bgName==22 && copy.bgFriendlyName=="older friendly name" && copy.bgEffect==33 && copy.transition==44 && copy.bgmId==55 && copy.sound=="sound-key" && copy.popup=="popup-key" && copy.voice=="voice-key" && copy.selectionGroup==812 && copy.placeText==source.placeText && copy.additionalPrompt==source.additionalPrompt && copy.highlightedSlotNums.SequenceEqual(new[]{5,2}),"complete copied content was reset");
            Check(copy.characters[2].action==8 && copy.characters[2].appear==4 && copy.characters[2].shapeOverride==5 && copy.characters[2].effect==1 && copy.characters[2].emoticon==4 && JsonSerializer.Serialize(copy.characters[2].curve)==JsonSerializer.Serialize(source.characters[2].curve) && AuthoringLines.CurvesValidated>0,"one-shot/curve data not copied");
        });
        Test("duplicate supports dialogue narration control and preserves selection groups without option operations",()=>{
            var node=PrepareDuplicateRows(3);PrepareSpeakerRows();node.Scripts[0].speakerSlotNum=2;node.Scripts[1].speakerSlotNum=0;node.Scripts[2].speakerSlotNum=0;node.Scripts[2].isDialogScript=false;node.Scripts[2].selectionGroup=777;node.Scripts[2].additionalPrompt="#title;header;title";Fixture.BatchIndices=new[]{0,1,2};Fixture.Rebind();
            var host=Host();var r=host.CommitDuplicate(host.CaptureDuplicate());Check(r.Disposition==CardPackV2CommitDisposition.Applied,r.Message);
            Check(node.Scripts[1].speakerSlotNum==2 && node.Scripts[3].speakerSlotNum==0 && node.Scripts[3].isDialogScript && !node.Scripts[5].isDialogScript && node.Scripts[5].selectionGroup==777 && node.Scripts[5].additionalPrompt=="#title;header;title","mode/group/control content changed");
            using var payload=JsonDocument.Parse(AuthoringEditorSession.Current!.LastOperations!);Check(payload.RootElement.EnumerateArray().All(op=>op.GetProperty("op").GetString()=="line.insert"),"copy invented option/link/copy operation");
        });
        Test("duplicate offstage dialogue and assigned voice preserve real resource references",()=>{
            var node=PrepareDuplicateRows(1);PrepareSpeakerRows();node.Scripts[0].characters[0].name="actor-3";node.Scripts[0].voice="audio-asset";AddMultilineScene("voice","audio-asset");Fixture.Rebind();var host=Host();var r=host.CommitDuplicate(host.CaptureDuplicate());
            Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].speakerSlotNum==0 && node.Scripts[1].characters[0].name=="actor-3" && node.Scripts[1].voice=="audio-asset" && node.Scripts[1].LineId!=node.Scripts[0].LineId,r.Message);
        });
        Test("duplicate explicit no-audio and blank text do not manufacture a voice resource",()=>{
            var node=PrepareDuplicateRows(1);node.Scripts[0].text="";Fixture.Rebind();var host=Host();var r=host.CommitDuplicate(host.CaptureDuplicate());
            Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].text=="" && node.Scripts[1].voice=="" && AuthoringResourceCatalog.Resolved.All(id=>!id.Contains(":voice:",StringComparison.Ordinal)),r.Message);
        });
        Test("duplicate never guesses unknown or unavailable assigned voice as unassigned",()=>{
            var node=PrepareDuplicateRows(1);node.Scripts[0].voice=Guid.NewGuid().ToString("D");Reject(()=>Host().CaptureDuplicate(),"unknown GUID voice silently dropped");
            AddMultilineScene("voice",node.Scripts[0].voice);string id="fixture:voice:"+node.Scripts[0].voice;AuthoringResourceCatalog.SceneResources[id]=AuthoringResourceCatalog.SceneResources[id].Replace("\"available\":true","\"available\":false",StringComparison.Ordinal);Reject(()=>Host().CaptureDuplicate(),"unavailable assigned voice silently dropped");Check(AuthoringEditorSession.Current!.Applies==0,"bad voice entered Apply");
        });
        Test("duplicate explicit without-voice option supports unknown default GUID without inference",()=>{
            var node=PrepareDuplicateRows(1);string sourceVoice=Guid.NewGuid().ToString("D");node.Scripts[0].voice=sourceVoice;var host=Host();Reject(()=>host.CaptureDuplicate(),"default silently dropped unknown voice");
            var preview=host.CaptureDuplicate(new(CopyVoice:false));Check(!preview.Options.CopyVoice && preview.CopyNotice.Contains("不带配音",StringComparison.Ordinal) && preview.CopiedAndDerivedStateJson.Contains("omit_by_explicit_user_choice",StringComparison.Ordinal),"explicit audio omission missing from preview");
            using(var json=JsonDocument.Parse(preview.Plan.OperationsJson))Check(json.RootElement[0].GetProperty("line").GetProperty("scene").GetProperty("voice").GetProperty("action").GetString()=="none","explicit option did not compile native voice:none");
            var r=host.CommitDuplicate(preview);Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[0].voice==sourceVoice && node.Scripts[1].voice=="" && AuthoringEditorSession.Current!.Applies==1 && OperationManager.history.Count==2,r.Message);
        });
        Test("duplicate explicit without-voice applies uniformly to valid audio and keeps other resources strict",()=>{
            var node=PrepareDuplicateRows(1);node.Scripts[0].voice="known-audio";AddMultilineScene("voice","known-audio");var host=Host();var p=host.CaptureDuplicate(new(CopyVoice:false));var r=host.CommitDuplicate(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[0].voice=="known-audio" && node.Scripts[1].voice=="","Explicit without-voice option retained audio: "+r.Message);
            Fixture.Reset();node=PrepareDuplicateRows(1);node.Scripts[0].voice="unknown-audio";node.Scripts[0].sound="missing-sound";Reject(()=>Host().CaptureDuplicate(new(CopyVoice:false)),"without-voice weakened other resource validation");
        });
        Test("duplicate omission option is immutable owner-bound and source/resource changes still revoke it",()=>{
            var node=PrepareDuplicateRows(1);node.Scripts[0].voice="known-audio";AddMultilineScene("voice","known-audio");var host=Host();var p=host.CaptureDuplicate(new(CopyVoice:false));var forged=p with{Options=new(CopyVoice:true)};Check(host.CommitDuplicate(forged).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"caller changed owner-bound voice policy");
            p=host.CaptureDuplicate(new(CopyVoice:false));node.Scripts[0].voice="another-voice";Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"without-voice ignored source voice drift");
        });
        Test("duplicate preflight rejects unrepresentable empty/offstage record residue rather than losing it",()=>{
            var node=PrepareDuplicateRows(1);node.Scripts[0].characters[4].faceId="hidden-residue";Reject(()=>Host().CaptureDuplicate(),"unused record content silently erased");node.Scripts[0].characters[4].faceId="00";PrepareSpeakerRows();node.Scripts[0].characters[0].name="actor-1";node.Scripts[0].characters[0].faceId="01";Reject(()=>Host().CaptureDuplicate(),"offstage record residue silently erased");
        });
        Test("duplicate preserves original background friendly name across catalog display-name changes",()=>{
            var node=PrepareDuplicateRows(1);node.Scripts[0].bgName=22;node.Scripts[0].bgFriendlyName="saved label";AddMultilineScene("background","22");var resource=JsonNode.Parse(AuthoringResourceCatalog.SceneResources["fixture:background:22"])!;resource["name"]="catalog renamed";AuthoringResourceCatalog.SceneResources["fixture:background:22"]=resource.ToJsonString();
            var host=Host();var r=host.CommitDuplicate(host.CaptureDuplicate());Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].bgFriendlyName=="saved label",r.Message);
        });
        Test("duplicate uses native curve validation and rejects native round-trip data loss",()=>{
            var node=PrepareDuplicateRows(1);PrepareSpeakerRows();node.Scripts[0].characters[1].action=8;node.Scripts[0].characters[1].curve=JsonDocument.Parse("{\"duration\":1,\"extension\":42}").RootElement.Clone();AuthoringLines.OnCurveValidate=_=>throw new Exception("native invalid curve");Reject(()=>Host().CaptureDuplicate(),"native curve validation skipped");AuthoringLines.OnCurveValidate=null;AuthoringLines.ExtraCreate=l=>l.characters[1].curve=JsonDocument.Parse("{\"duration\":1}").RootElement.Clone();Reject(()=>Host().CaptureDuplicate(),"native roundtrip dropped curve extension unnoticed");
        });
        Test("duplicate rejects unknown Read fields and non-Curve residual curve state",()=>{
            var node=PrepareDuplicateRows(1);AuthoringLines.ReadOverride=(l,json)=>{var obj=JsonNode.Parse(json)!;obj["unknown_copy_field"]=42;return obj.ToJsonString();};Reject(()=>Host().CaptureDuplicate(),"unknown Read field silently lost");AuthoringLines.ReadOverride=null;PrepareSpeakerRows();node.Scripts[0].characters[1].curve=JsonDocument.Parse("{}").RootElement.Clone();Reject(()=>Host().CaptureDuplicate(),"non-Curve residual copied without evidence");
        });
        Test("duplicate independently proves successor and copied starting-position changes",()=>{
            var node=PrepareDuplicateRows(2);PrepareSpeakerRows();node.Scripts[0].characters[2].endingPos=5;node.Scripts[1].characters[2].startingPos=5;Fixture.Rebind();var host=Host();var p=host.CaptureDuplicate();var r=host.CommitDuplicate(p);
            Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].characters[2].startingPos==5 && node.Scripts[2].characters[2].startingPos==5 && p.CopiedAndDerivedStateJson.Contains("starting_positions",StringComparison.Ordinal),r.Message);
        });
        Test("duplicate fresh and Check-time selection content and catalog drift reject atomically",()=>{
            for(int mode=0;mode<4;mode++){Fixture.Reset();var node=PrepareDuplicateRows(3);var host=Host();var p=host.CaptureDuplicate();var session=AuthoringEditorSession.Current!;if(mode==0)Fixture.BatchIndices=new[]{1};if(mode==1)node.Scripts[0].additionalPrompt="changed";if(mode==2)session.OnCheck=()=>Fixture.BatchIndices=new[]{2};if(mode==3)session.OnCheck=()=>AuthoringResourceCatalog.CatalogVersion="new";
                Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && session.Applies==0,"stale copy accepted, mode="+mode);}
        });
        Test("duplicate rejects source node project and foreign selection-cache ownership changes",()=>{
            for(int mode=0;mode<3;mode++){Fixture.Reset();var node=PrepareDuplicateRows(1);var host=Host();var p=host.CaptureDuplicate();if(mode==0)node.Guid=Guid.NewGuid();if(mode==1)StudioCommon.instance!.Project.ProjectId="foreign-project";if(mode==2)ScriptNodeInspector.instance!.scriptNodeListItemsCache[0].inspector=new ScriptNodeInspector();Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"foreign copy owner accepted");}
        });
        Test("duplicate revalidates inherited and explicit resource binding after Check",()=>{
            PrepareDuplicateRows(1);PrepareSpeakerRows();var host=Host();var p=host.CaptureDuplicate();AuthoringEditorSession.Current!.OnCheck=()=>AuthoringResourceCatalog.CharacterResources["fixture:actor-2"]=AuthoringResourceCatalog.CharacterResources["fixture:actor-2"].Replace("\"available\":true","\"available\":false",StringComparison.Ordinal);
            Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current.Applies==0,"changed resource accepted after Check");
        });
        Test("duplicate rejects aliased clones invalid graph and unrelated Normalize changes",()=>{
            PrepareDuplicateRows(1);ProjectFileStore.CloneOverride=p=>p;Reject(()=>Host().CaptureDuplicate(),"aliased complete copy accepted");ProjectFileStore.CloneOverride=null;AuthoringGraph.OnRequireValid=_=>throw new Exception("invalid graph");Reject(()=>Host().CaptureDuplicate(),"full validation skipped");AuthoringGraph.OnRequireValid=null;AuthoringGraph.OnNormalize=p=>p.ProjectName="unexpected";Reject(()=>Host().CaptureDuplicate(),"Normalize arbitrary change accepted");
        });
        Test("duplicate complete post-proof rejects changed old content new content and identity order",()=>{
            for(int mode=0;mode<4;mode++){Fixture.Reset();PrepareDuplicateRows(3);Fixture.BatchIndices=new[]{0,2};var host=Host();var p=host.CaptureDuplicate();MutateMultilineAfter(n=>{if(mode==0)n.Scripts[0].selectionGroup++;if(mode==1)n.Scripts[1].additionalPrompt="lost";if(mode==2)n.Scripts[1].voice="unexpected-new-audio";if(mode==3){var first=n.Scripts[1];n.Scripts[1]=n.Scripts[4];n.Scripts[4]=first;}});Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"coherent wrong copy accepted, mode="+mode);}
        });
        Test("duplicate receipt verifies every native alias plus actual returned context",()=>{
            for(int mode=0;mode<4;mode++){Fixture.Reset();PrepareDuplicateRows(3);Fixture.BatchIndices=new[]{0,2};var host=Host();var p=host.CaptureDuplicate();AuthoringEditorSession.Current!.ReceiptOverride=json=>{var r=JsonNode.Parse(json)!;if(mode==0)r["context"]!["edit_session"]="foreign";if(mode==1)r["data"]!["created"]!["duplicate_1"]="wrong";if(mode==2)r["data"]!["applied_operations"]=1;if(mode==3)r["data"]!["created"]!.AsObject().Remove("duplicate_2");return r.ToJsonString();};Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"false copy receipt accepted");}
        });
        Test("duplicate fresh line identities cannot hide invalid GUID or source identity reuse",()=>{
            foreach(string invalid in new[]{"not-a-native-guid",Guid.Empty.ToString("D"),"opaque-anchor"}){Fixture.Reset();PrepareDuplicateRows(1);AuthoringLines.ExtraCreate=l=>l.LineId=invalid;Reject(()=>Host().CaptureDuplicate(),"invalid new copy identity was hidden by comparison alignment");Check(AuthoringEditorSession.Current!.Applies==0,"invalid identity entered Apply");}
        });
        Test("duplicate native entry failure uses shared conservative unchanged-state recovery",()=>{
            PrepareDuplicateRows(1);var host=Host();var p=host.CaptureDuplicate();AuthoringEditorSession.Current!.ThrowBeforeApply=true;Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"native failure not uncertain");Reject(()=>Host().CaptureMultiline("x"),"multiline ignored copy fault");Reject(()=>Host().CaptureDelete(),"delete ignored copy fault");Reject(()=>Host().Capture("zh-CN"),"cards ignored copy fault");Check(Host().Recover().Recovered && AuthoringEditorSession.Current.Applies==1,"unchanged copy failed recovery or retried Apply");
        });
        Test("duplicate null partial and throw-after-history never infer completion from history alone",()=>{
            for(int mode=0;mode<3;mode++){Fixture.Reset();PrepareDuplicateRows(1);var host=Host();var p=host.CaptureDuplicate();var s=AuthoringEditorSession.Current!;s.NullReceipt=mode==0;s.PartialMutation=mode==1;s.ThrowAfterHistory=mode==2;Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered && s.Applies==1,"ambiguous copy recovered/retried");}
        });
        Test("duplicate verified receipt and failed refresh can recover the exact committed result",()=>{
            PrepareDuplicateRows(1);var host=Host();var p=host.CaptureDuplicate();invalidate=()=>throw new Exception("refresh failed");Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"refresh failure erased");invalidate=null;Check(Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"confirmed copy recovery failed/retried");
        });
        Test("duplicate shares reentry gate and revokes previews for all prior clients",()=>{
            var node=PrepareDuplicateRows(1);PrepareSpeakerRows();node.Scripts[0].speakerSlotNum=2;AuthoringProjectPreview.Refresh(StudioCommon.instance!.Project);Fixture.Rebind();var cardHost=Host();var cards=Plan(cardHost);var textHost=Host();var text=textHost.CaptureSetText("stale");var speakerHost=Host();var speaker=speakerHost.CaptureSpeaker(3);var deleteHost=Host();var del=deleteHost.CaptureDelete();var multiHost=Host();var multi=multiHost.CaptureMultiline("stale");var host=Host();var p=host.CaptureDuplicate();bool reentry=false;AuthoringEditorSession.Current!.OnApply=()=>{try{Host().CaptureDuplicate();}catch{reentry=true;}};
            Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.Applied && reentry,"copy gate missing");Check(cardHost.Commit(cards,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && textHost.CommitSetText(text).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && speakerHost.CommitSpeaker(speaker).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && deleteHost.CommitDelete(del).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && multiHost.CommitMultiline(multi).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"old previews survived copy");
        });
        Test("duplicate early closed admission still revokes old previews before rejection",()=>{
            PrepareDuplicateRows(1);var other=Host();var old=other.CaptureMultiline("stale");var host=Host();var p=host.CaptureDuplicate();allowed=false;Check(host.CommitDuplicate(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"closed guard admitted copy");allowed=true;Check(other.CommitMultiline(old).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"early F1 revocation missing");
        });
        Test("duplicate native500 remains one transaction and501 never expands native copies",()=>{
            PrepareDuplicateRows(500);Fixture.BatchIndices=Enumerable.Range(0,500).ToArray();var host=Host();var p=host.CaptureDuplicate();var r=host.CommitDuplicate(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current!.Applies==1 && SpeakerNode().Scripts.Count==1000,r.Message);
            Fixture.Reset();PrepareDuplicateRows(501);Fixture.BatchIndices=Enumerable.Range(0,501).ToArray();int before=AuthoringLines.DetachedCreates;Reject(()=>Host().CaptureDuplicate(),"native501 admitted");Check(AuthoringEditorSession.Current!.Applies==0 && AuthoringLines.DetachedCreates==before,"oversized copy expanded/applied prefix");
        });
    }
}
