using Azurite.Core;
using System.Text.Json;
using AzureArchive.Automation;
using Studio.Scripts;
using Studio.Scripts.OperationManagement;

static partial class Program
{
    static AuthoringBatchSelection MultilineSelection(int count=1) => new(7,"node-opaque",Enumerable.Range(0,count).Select(i=>new AuthoringBatchRow("anchor-"+i,i*2,"old-"+i,2)).ToArray());
    static AuthoringBatchMultilineAnchor[] NarrationAnchors(AuthoringBatchSelection selection) => selection.Rows.Select(r=>new AuthoringBatchMultilineAnchor(r.StableId,"narration")).ToArray();
    static void MultilinePure()
    {
        Test("multiline splits all CRLF CR LF lines after each original noncontiguous anchor",()=>{
            var selection=MultilineSelection(2);var result=AuthoringBatchMultilineCompiler.TryCompile(selection,"\r\nA\rB\n",NarrationAnchors(selection));
            Check(result.IsValid,result.Message);var plan=result.Plan!;
            Check(plan.OperationCount==8 && plan.Lines.Select(l=>l.Text).SequenceEqual(new[]{"","A","B","","","A","B",""}),"split or final empty line lost");
            Check(plan.Lines.Select(l=>l.TargetIndex).SequenceEqual(new[]{1,2,3,4,7,8,9,10}),"multi-anchor target order changed");
            using var json=JsonDocument.Parse(plan.OperationsJson);var ops=json.RootElement;
            Check(ops[0].GetProperty("after_line").GetProperty("id").GetString()=="anchor-0" && ops[4].GetProperty("after_line").GetProperty("id").GetString()=="anchor-1","later anchor incorrectly chained after first anchor");
            for(int i=0;i<8;i++) {var op=ops[i];Check(op.GetProperty("op").GetString()=="line.insert" && op.GetProperty("node").GetProperty("id").GetString()=="node-opaque" && op.GetProperty("line").GetProperty("inherit").GetString()=="previous","native insert shape changed");if(i%4!=0)Check(op.GetProperty("after_line").GetProperty("ref").GetString()==ops[i-1].GetProperty("ref").GetString(),"new row ref chain broken");}
            Check(plan.Lines.Select(l=>l.Alias).Distinct().Count()==8,"aliases reused across independent chains");
        });
        Test("multiline keeps logical1024 and native500 independent without prefix or allocation fanout",()=>{
            var one=MultilineSelection();var anchors=NarrationAnchors(one);string Lines(int count)=>string.Join('\n',Enumerable.Repeat("x",count));
            Check(AuthoringBatchPlanner.TryPlan(one,AuthoringBatchAction.AddMultiline,Lines(1024)).IsValid,"logical1024 reduced to native limit");
            Check(!AuthoringBatchPlanner.TryPlan(one,AuthoringBatchAction.AddMultiline,Lines(1025)).IsValid,"logical1025 accepted");
            Check(AuthoringBatchMultilineCompiler.TryCompile(one,Lines(500),anchors).Plan?.OperationCount==500,"native500 rejected");
            var over=AuthoringBatchMultilineCompiler.TryCompile(one,Lines(501),anchors);Check(!over.IsValid && over.Plan==null && over.Failure==AuthoringBatchNativeFailure.NativeOperationLimit,"native501 did not reject whole batch");
            var many=MultilineSelection(251);Check(!AuthoringBatchMultilineCompiler.TryCompile(many,"a\nb",NarrationAnchors(many)).IsValid,"fanout502 accepted");
        });
        Test("multiline logical planner rejects huge fanout before splitting and expanded allocations",()=>{
            var selection=MultilineSelection(4);string huge=new('\n',100000);long before=GC.GetAllocatedBytesForCurrentThread();
            var result=AuthoringBatchPlanner.TryPlan(selection,AuthoringBatchAction.AddMultiline,huge);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(!result.IsValid && result.Failure==AuthoringBatchFailure.TooManyOperations,"oversized logical fanout accepted");
            Check(allocated<1000000,"rejected fanout allocated expanded arrays/edits: "+allocated);
        });
        Test("multiline logical target-index overflow rejects before any wrapping edit",()=>{
            var selection=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("anchor",int.MaxValue,"old",0)});
            var result=AuthoringBatchPlanner.TryPlan(selection,AuthoringBatchAction.AddMultiline,"x");Check(!result.IsValid && result.Plan==null,"target index wrapped");
        });
        Test("multiline explicit modes and speakers preserve identities and actual native schema fields",()=>{
            var selection=MultilineSelection(3);var anchors=new[]{new AuthoringBatchMultilineAnchor("anchor-0","narration"),new AuthoringBatchMultilineAnchor("anchor-1","dialogue",2),new AuthoringBatchMultilineAnchor("anchor-2","dialogue",OffstageResourceId:"opaque:角色😀")};
            var result=AuthoringBatchMultilineCompiler.TryCompile(selection,"空白😀",anchors);Check(result.IsValid,result.Message);
            using var json=JsonDocument.Parse(result.Plan!.OperationsJson);var ops=json.RootElement;
            Check(ops[0].GetProperty("line").GetProperty("speaker").ValueKind==JsonValueKind.Null && ops[1].GetProperty("line").GetProperty("speaker").GetProperty("slot").GetInt32()==2 && ops[2].GetProperty("line").GetProperty("speaker").GetProperty("offstage_resource_id").GetString()=="opaque:角色😀","speaker choice changed");
            using var schema=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"NativeSchemaResponse.json")));var defs=schema.RootElement.GetProperty("data").GetProperty("$defs");var shape=defs.GetProperty("EditOperation").GetProperty("oneOf").EnumerateArray().Single(x=>x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()=="line.insert");
            foreach(var op in ops.EnumerateArray()){Check(op.EnumerateObject().All(p=>shape.GetProperty("properties").TryGetProperty(p.Name,out _)),"unknown insert field");var line=op.GetProperty("line");Check(line.EnumerateObject().All(p=>defs.GetProperty("LineInput").GetProperty("properties").TryGetProperty(p.Name,out _)) && defs.GetProperty("LineInput").GetProperty("required").EnumerateArray().All(r=>line.TryGetProperty(r.GetString()!,out _)),"LineInput schema mismatch");}
        });
        Test("multiline compiler rejects ambiguous modes speakers stale anchors and invalid Unicode",()=>{
            var selection=MultilineSelection();var good=NarrationAnchors(selection);
            Check(!AuthoringBatchMultilineCompiler.TryCompile(null,"x",good).IsValid && !AuthoringBatchMultilineCompiler.TryCompile(selection,"",good).IsValid && !AuthoringBatchMultilineCompiler.TryCompile(selection,"x",null).IsValid,"missing input accepted");
            foreach(var a in new[]{new AuthoringBatchMultilineAnchor("anchor-0","control"),new AuthoringBatchMultilineAnchor("anchor-0","dialogue"),new AuthoringBatchMultilineAnchor("anchor-0","dialogue",0),new AuthoringBatchMultilineAnchor("anchor-0","dialogue",2,"both"),new AuthoringBatchMultilineAnchor("anchor-0","narration",2),new AuthoringBatchMultilineAnchor("foreign","narration")})Check(!AuthoringBatchMultilineCompiler.TryCompile(selection,"x",new[]{a}).IsValid,"invalid mode/speaker/anchor accepted");
            Check(!AuthoringBatchMultilineCompiler.TryCompile(selection,"bad\ud800",good).IsValid,"invalid text UTF16 changed silently");
            var malformed=new AuthoringBatchSelection(7,"bad\udfff",selection.Rows);Check(!AuthoringBatchMultilineCompiler.TryCompile(malformed,"x",good).IsValid,"invalid nodeID UTF16 changed silently");
            var badRow=new AuthoringBatchSelection(7,"node",new[]{new AuthoringBatchRow("id\udfff",0,"old",2)});Check(!AuthoringBatchMultilineCompiler.TryCompile(badRow,"x",NarrationAnchors(badRow)).IsValid,"invalid LineId changed silently");
        });
        Test("multiline immutable preview keeps blank-only content and freezes caller anchor list",()=>{
            var selection=MultilineSelection();var anchors=NarrationAnchors(selection);var result=AuthoringBatchMultilineCompiler.TryCompile(selection,"\n",anchors);Check(result.IsValid,result.Message);anchors[0]=new("foreign","control");
            Check(result.Plan!.Lines.Count==2 && result.Plan.Lines.All(l=>l.Text=="") && result.Plan.Anchors[0].StableId=="anchor-0","preview was changed through caller list or empty lines lost");
        });
    }

    static ScriptNodeData PrepareMultilineRows(int count=3)
    {
        var node=PrepareDeletionRows(count);
        foreach(var line in node.Scripts){line.isDialogScript=true;line.speakerSlotNum=0;line.bgName=0;line.additionalPrompt="source-command";line.voice="source-audio";line.StageJson="";}
        Fixture.BatchIndices=new[]{0};Fixture.Rebind();return node;
    }
    static void AddMultilineScene(string kind,string key)
        => AuthoringResourceCatalog.SceneResources["fixture:"+kind+":"+key]=JsonSerializer.Serialize(new{resource_id="fixture:"+kind+":"+key,kind,catalog_version="fixture-catalog",available=true,native_key=key});
    static void MutateMultilineAfter(Action<ScriptNodeData> mutate)
        => AuthoringEditorSession.Current!.OnAfterApply=()=>{mutate(SpeakerNode());((AuthoringEditorSession.ProjectSnapshotOperation)OperationManager.history.Peek()).after=StudioCommon.instance!.Project.Clone();};
    static void MultilineHost()
    {
        Test("multiline Host inserts full independent chains at noncontiguous original anchors once",()=>{
            var node=PrepareMultilineRows(5);Fixture.BatchIndices=new[]{3,0};var host=Host();var preview=host.CaptureMultiline("A\r\nB\r");
            Check(preview.Plan.OperationCount==6 && preview.InheritanceNotice.Length>0,"preview lacks exact fanout or inheritance notice");
            var result=host.CommitMultiline(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied,result.Message);
            Check(node.Scripts.Select(l=>l.text).SequenceEqual(new[]{"text-0","A","B","","text-1","text-2","text-3","A","B","","text-4"}),"anchor interleaving or trailing empty line wrong");
            Check(AuthoringEditorSession.Current!.Events.SequenceEqual(new[]{"Check","Apply"}) && OperationManager.history.Count==2,"multiple native boundaries/history entries");
        });
        Test("multiline default preserves each dialogue and narration new-line mode",()=>{
            var node=PrepareMultilineRows(2);PrepareSpeakerRows();node.Scripts[0].speakerSlotNum=2;node.Scripts[1].speakerSlotNum=0;Fixture.BatchIndices=new[]{0,1};Fixture.Rebind();
            var host=Host();var preview=host.CaptureMultiline("new");Check(preview.Plan.Lines[0].Mode=="dialogue" && preview.Plan.Lines[1].Mode=="narration","mixed readable modes collapsed");
            var result=host.CommitMultiline(preview);Check(result.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].speakerSlotNum==2 && node.Scripts[3].speakerSlotNum==0,result.Message);
        });
        Test("multiline control anchors require explicit narration and keep old control untouched",()=>{
            var node=PrepareMultilineRows(1);node.Scripts[0].isDialogScript=false;var host=Host();Reject(()=>host.CaptureMultiline("visible"),"control silently selected a new mode");
            var p=host.CaptureMultiline("visible",new(AuthoringBatchMultilineMode.Narration));var result=host.CommitMultiline(p);
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && !node.Scripts[0].isDialogScript && node.Scripts[1].isDialogScript && node.Scripts[1].speakerSlotNum==0,result.Message);
        });
        Test("multiline control anchor supports explicit dialogue with retained real slot",()=>{
            var node=PrepareMultilineRows(1);PrepareSpeakerRows();node.Scripts[0].isDialogScript=false;
            var host=Host();var p=host.CaptureMultiline("dialogue",new(AuthoringBatchMultilineMode.Dialogue,3));var r=host.CommitMultiline(p);
            Check(r.Disposition==CardPackV2CommitDisposition.Applied && !node.Scripts[0].isDialogScript && node.Scripts[1].speakerSlotNum==3,r.Message);
        });
        Test("multiline supports existing and explicitly chosen offstage resource identities",()=>{
            var node=PrepareMultilineRows(1);PrepareSpeakerRows();node.Scripts[0].speakerSlotNum=0;node.Scripts[0].characters[0].name="actor-1";
            var host=Host();var p=host.CaptureMultiline("offstage");Check(p.Plan.Lines[0].OffstageResourceId=="fixture:actor-1","offstage resource identity guessed or lost");
            var r=host.CommitMultiline(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].characters[0].name=="actor-1",r.Message);
            Fixture.BatchIndices=new[]{0};var q=host.CaptureMultiline("chosen",new(AuthoringBatchMultilineMode.Dialogue,OffstageResourceId:"fixture:actor-4"));var s=host.CommitMultiline(q);Check(s.Disposition==CardPackV2CommitDisposition.Applied && node.Scripts[1].characters[0].name=="actor-4",s.Message);
        });
        Test("multiline inherits scene state while resetting one-shot data and creating fresh voice identities",()=>{
            var node=PrepareMultilineRows(1);PrepareSpeakerRows();var source=node.Scripts[0];source.speakerSlotNum=2;source.characters[1].appear=6;source.characters[2].endingPos=4;source.characters[2].action=1;source.characters[2].effect=1;source.characters[2].curve="source-curve";source.sound="old-sound";source.transition=9;source.popup="panel";source.bgName=22;source.bgEffect=33;source.bgmId=44;source.selectionGroup=91;
            AddMultilineScene("popup","panel");AddMultilineScene("background","22");AddMultilineScene("background_effect","33");AddMultilineScene("bgm","44");Fixture.Rebind();
            var host=Host();var p=host.CaptureMultiline("one\ntwo");Check(p.InheritedAndDerivedStateJson.Contains("selection_group",StringComparison.Ordinal),"inherited non-Read state omitted from preview");
            var r=host.CommitMultiline(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied,r.Message);
            foreach(var l in node.Scripts.Skip(1)){Check(l.bgName==22 && l.bgEffect==33 && l.popup=="panel" && l.bgmId==44 && l.selectionGroup==91,"persistent inherited scene/group lost");Check(l.sound=="" && l.transition==0 && l.additionalPrompt=="" && l.characters[2].action==0 && l.characters[2].curve==null && string.IsNullOrEmpty(l.characters[1].name),"one-shot state or departed actor replayed");Check(l.voice!=source.voice && l.LineId!=source.LineId && l.characters[2].startingPos==4,"identity or derived position reused");}
            Check(node.Scripts[1].voice!=node.Scripts[2].voice,"new voice identities reused");
        });
        Test("multiline dropped dialogue actor refuses default but explicit narration remains usable",()=>{
            var node=PrepareMultilineRows(1);PrepareSpeakerRows();node.Scripts[0].speakerSlotNum=2;node.Scripts[0].characters[2].appear=6;Fixture.Rebind();var host=Host();Reject(()=>host.CaptureMultiline("x"),"departed speaker accepted");
            var r=host.CommitMultiline(host.CaptureMultiline("x",new(AuthoringBatchMultilineMode.Narration)));Check(r.Disposition==CardPackV2CommitDisposition.Applied,r.Message);
        });
        Test("multiline rejects missing and stale inherited resources before native entry",()=>{
            PrepareMultilineRows(1);PrepareSpeakerRows();AuthoringResourceCatalog.Missing=true;Reject(()=>Host().CaptureMultiline("x"),"unavailable retained resource accepted");Check(AuthoringEditorSession.Current!.Applies==0,"resource rejection entered Apply");
        });
        Test("multiline rejects ambiguous explicit mode parameters and absent dialogue resources",()=>{
            PrepareMultilineRows(1);var host=Host();
            foreach(var options in new[]{new AuthoringBatchMultilineOptions((AuthoringBatchMultilineMode)99),new AuthoringBatchMultilineOptions(AuthoringBatchMultilineMode.Narration,2),new AuthoringBatchMultilineOptions(AuthoringBatchMultilineMode.PerAnchorReadable,OffstageResourceId:"opaque"),new AuthoringBatchMultilineOptions(AuthoringBatchMultilineMode.Dialogue),new AuthoringBatchMultilineOptions(AuthoringBatchMultilineMode.Dialogue,2,"both")})Reject(()=>host.CaptureMultiline("x",options),"ambiguous explicit mode accepted");
            Reject(()=>host.CaptureMultiline("x",new(AuthoringBatchMultilineMode.Dialogue,2)),"empty stage slot accepted");Reject(()=>host.CaptureMultiline("x",new(AuthoringBatchMultilineMode.Dialogue,OffstageResourceId:"missing")),"invented offstage resource accepted");
            Check(AuthoringEditorSession.Current!.Applies==0,"invalid new-line parameters entered native Apply");
        });
        Test("multiline revalidates resource binding inside native Check",()=>{
            PrepareMultilineRows(1);PrepareSpeakerRows();var host=Host();var p=host.CaptureMultiline("x");AuthoringEditorSession.Current!.OnCheck=()=>AuthoringResourceCatalog.CharacterResources["fixture:actor-2"]=AuthoringResourceCatalog.CharacterResources["fixture:actor-2"].Replace("\"available\":true","\"available\":false",StringComparison.Ordinal);
            var r=host.CommitMultiline(p);Check(r.Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current.Applies==0,r.Message);
        });
        Test("multiline preflight rejects arbitrary native Create and Normalize changes",()=>{
            PrepareMultilineRows(1);AuthoringLines.ExtraCreate=l=>l.selectionGroup=987654;Reject(()=>Host().CaptureMultiline("x"),"arbitrary Create field blessed by expected proof");AuthoringLines.ExtraCreate=null;AuthoringGraph.OnNormalize=p=>p.ProjectName="wrong";Reject(()=>Host().CaptureMultiline("x"),"Normalize mutation blessed");Check(AuthoringEditorSession.Current!.Applies==0,"preflight rejection wrote live");
        });
        Test("multiline preflight uses full native edit validation and detached clones",()=>{
            PrepareMultilineRows(1);AuthoringGraph.OnRequireValid=_=>throw new InvalidOperationException("GRAPH_INVALID");Reject(()=>Host().CaptureMultiline("x"),"edit validation skipped");AuthoringGraph.OnRequireValid=null;
            ProjectFileStore.CloneOverride=p=>p;Reject(()=>Host().CaptureMultiline("x"),"aliased complete project accepted");Check(AuthoringEditorSession.Current!.Applies==0,"alias rejection wrote live");
        });
        Test("multiline fresh selection and Check-time selection changes reject atomically",()=>{
            PrepareMultilineRows(3);var host=Host();var p=host.CaptureMultiline("x");Fixture.BatchIndices=new[]{1};Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"stale preview accepted");
            p=host.CaptureMultiline("x");AuthoringEditorSession.Current!.OnCheck=()=>Fixture.BatchIndices=new[]{2};Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current.Applies==0,"Check-time selection changed");
        });
        Test("multiline rejects Check-time catalog and project drift",()=>{
            PrepareMultilineRows(1);var host=Host();var p=host.CaptureMultiline("x");AuthoringEditorSession.Current!.OnCheck=()=>AuthoringResourceCatalog.CatalogVersion="changed";Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"catalog drift accepted");
            Check(AuthoringEditorSession.Current.Applies==0,"drift entered Apply");
        });
        Test("multiline source node project and foreign cache changes invalidate pending previews",()=>{
            for(int mutation=0;mutation<3;mutation++){Fixture.Reset();var node=PrepareMultilineRows(2);var host=Host();var p=host.CaptureMultiline("x");
                if(mutation==0)node.Guid=Guid.NewGuid();if(mutation==1)StudioCommon.instance!.Project.ProjectId="different-project";if(mutation==2)ScriptNodeInspector.instance!.scriptNodeListItemsCache[0].inspector=new ScriptNodeInspector();
                Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"changed native owner/cache accepted");}
        });
        Test("multiline complete proof rejects coherent wrong text order old state and reused new identities",()=>{
            foreach(int mutation in Enumerable.Range(0,4)){Fixture.Reset();var node=PrepareMultilineRows(2);var host=Host();var p=host.CaptureMultiline("one\ntwo");MutateMultilineAfter(n=>{if(mutation==0)n.Scripts[1].text="wrong";if(mutation==1){var first=n.Scripts[1];n.Scripts[1]=n.Scripts[2];n.Scripts[2]=first;}if(mutation==2)n.Scripts[0].voice="changed-old";if(mutation==3)n.Scripts[1].voice=n.Scripts[0].voice;});
                Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"coherent incorrect result was accepted, mutation="+mutation);Check(!Host().Recover().Recovered,"incorrect result recovered");}
        });
        Test("multiline receipt must bind each independent alias to its actual ordered native ID",()=>{
            for(int mutation=0;mutation<6;mutation++){Fixture.Reset();PrepareMultilineRows(3);Fixture.BatchIndices=new[]{0,2};var host=Host();var p=host.CaptureMultiline("new");
                AuthoringEditorSession.Current!.ReceiptOverride=json=>{var root=System.Text.Json.Nodes.JsonNode.Parse(json)!;var data=root["data"]!;var ids=data["created"]!;
                    if(mutation==0)ids["multiline_1"]="wrong";if(mutation==1){string a=ids["multiline_1"]!.GetValue<string>();ids["multiline_1"]=ids["multiline_2"]!.GetValue<string>();ids["multiline_2"]=a;}if(mutation==2)data["applied_operations"]=1;if(mutation==3)ids.AsObject().Remove("multiline_2");if(mutation==4)data["changed_node_ids"]=new System.Text.Json.Nodes.JsonArray("foreign");if(mutation==5)root["context"]!["edit_session"]="foreign-session";return root.ToJsonString();};
                Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown && !Host().Recover().Recovered,"invalid native identity receipt accepted, mutation="+mutation);}
        });
        Test("multiline fresh identities must match the pinned native Guid generation contract",()=>{
            for(int mutation=0;mutation<3;mutation++){Fixture.Reset();PrepareMultilineRows(1);AuthoringLines.ExtraCreate=l=>{if(mutation==0)l.LineId="not-a-native-guid";if(mutation==1)l.voice="not-a-native-guid";if(mutation==2)l.voice=Guid.Empty.ToString("D");};Reject(()=>Host().CaptureMultiline("x"),"non-native identity was hidden by detached comparison alignment");}
        });
        Test("multiline refresh failure recovers only confirmed receipt and full after-state",()=>{
            PrepareMultilineRows(1);var host=Host();var p=host.CaptureMultiline("x");invalidate=()=>throw new Exception("refresh failed");Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"refresh failure not retained");invalidate=null;
            Check(Host().Recover().Recovered && AuthoringEditorSession.Current!.Applies==1,"verified insertion failed recovery or retried Apply");
        });
        Test("multiline partial null-receipt and throw-after-history remain unresolved",()=>{
            for(int mode=0;mode<3;mode++){Fixture.Reset();PrepareMultilineRows(1);var host=Host();var p=host.CaptureMultiline("x");var s=AuthoringEditorSession.Current!;s.PartialMutation=mode==0;s.NullReceipt=mode==1;s.ThrowAfterHistory=mode==2;Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"failure not uncertain");Check(!Host().Recover().Recovered && s.Applies==1,"unconfirmed failure was accepted/retried");}
        });
        Test("multiline unchanged native entry failure locks all clients until shared recovery",()=>{
            PrepareMultilineRows(1);var host=Host();var p=host.CaptureMultiline("x");AuthoringEditorSession.Current!.ThrowBeforeApply=true;Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"native entry failure not uncertain");
            Reject(()=>Host().CaptureDelete(),"delete ignored fault");Reject(()=>Host().CaptureSetText("changed"),"text ignored fault");Reject(()=>Host().Capture("zh-CN"),"cards ignored fault");Check(Host().Recover().Recovered,"unchanged source could not recover");
        });
        Test("multiline shares reentry gate and consumes all client previews",()=>{
            PrepareMultilineRows(1);var cards=Host();var cardPlan=Plan(cards);var textHost=Host();var text=textHost.CaptureSetText("stale");var deleteHost=Host();var del=deleteHost.CaptureDelete();var host=Host();var p=host.CaptureMultiline("x");bool rejected=false;AuthoringEditorSession.Current!.OnApply=()=>{try{Host().CaptureMultiline("nested");}catch{rejected=true;}};
            var r=host.CommitMultiline(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied && rejected,r.Message);Check(cards.Commit(cardPlan,7).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && textHost.CommitSetText(text).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && deleteHost.CommitDelete(del).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"old client preview survived insertion");
        });
        Test("multiline failed early attempt still revokes other client preview",()=>{
            PrepareMultilineRows(1);var textHost=Host();var text=textHost.CaptureSetText("stale");var host=Host();var p=host.CaptureMultiline("x");allowed=false;Check(host.CommitMultiline(p).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation,"closed guard admitted");allowed=true;Check(textHost.CommitSetText(text).Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && AuthoringEditorSession.Current!.Applies==0,"F1 revocation missing");
        });
        Test("multiline native500 succeeds once and501 rejects before detached fanout",()=>{
            PrepareMultilineRows(1);var host=Host();string text=string.Join('\n',Enumerable.Repeat("x",500));var p=host.CaptureMultiline(text);var r=host.CommitMultiline(p);Check(r.Disposition==CardPackV2CommitDisposition.Applied && AuthoringEditorSession.Current!.Applies==1,r.Message);
            Fixture.Reset();PrepareMultilineRows(1);int before=AuthoringLines.DetachedCreates;Reject(()=>Host().CaptureMultiline(text+"\nx"),"native501 accepted");Check(AuthoringLines.DetachedCreates==before && AuthoringEditorSession.Current!.Applies==0,"oversized batch expanded detached rows or applied prefix");
        });
    }
}
