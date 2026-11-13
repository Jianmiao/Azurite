using Azurite;
using System.Text.Json;
using System.Text.Json.Nodes;

static class Program
{
    const string NodeId = "719bc0eb-b7c8-49fa-a958-c00f15611471";
    const string LineId = "fixture-line-opaque";
    static int passed, failed;
    static string fixture = "";
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    static JsonElement Parse(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    static JsonElement Changed(Action<JsonNode> change)
    {
        var node = JsonNode.Parse(fixture)!; change(node); return Parse(node.ToJsonString());
    }
    static AuthoringCardPackCopyProjectionResult Project(JsonElement input)
    {
        Check(AuthoringCardPackHostCopyProjection.TryProject(input, NodeId, LineId, out var result, out var error),
            "projection rejected: " + error?.Code + " " + error?.Path);
        Check(result != null && error == null, "success returned missing result or a failure diagnostic");
        return result!;
    }
    static void Reject(JsonElement input, string code, string path, string node = NodeId, string line = LineId)
    {
        Check(!AuthoringCardPackHostCopyProjection.TryProject(input, node, line, out var result, out var error), "invalid content was projected");
        Check(result == null, "failure leaked a partial candidate");
        Check(error?.Code == code && error.Path == path, $"wrong failure: expected {code} {path}, got {error?.Code} {error?.Path}");
    }
    static JsonNode Stage(JsonNode root, int index = 0) => root["stage"]![index]!;

    static int Main()
    {
        fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ReadShapeFixture.json"));
        Test("copy preserves every writable field and removes only identities and derived stage position", () => {
            var result = Project(Parse(fixture)); var line = result.LineInput;
            Check(line.EnumerateObject().Select(x => x.Name).OrderBy(x=>x).SequenceEqual(new[]{"inherit","mode","speaker","text","stage","highlighted_slots","scene","place","extra_script"}.OrderBy(x=>x)), "wrong writable field set");
            Check(line.GetProperty("inherit").GetProperty("from").GetProperty("id").GetString() == LineId, "inherit did not target the persistent source ID");
            Check(line.GetProperty("text").GetString() == "原文 [FF0000]色[-]\r\n第二行\n", "source text normalized or lost");
            Check(line.GetProperty("mode").GetString() == "dialogue" && line.GetProperty("speaker").GetProperty("slot").GetInt32() == 2, "speaker/mode changed");
            Check(line.GetProperty("place").GetString() == "原地点" && line.GetProperty("extra_script").GetString() == "# 原生场景脚本\nopaque content; never executed here", "place/extra_script changed");
            Check(line.GetProperty("highlighted_slots").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{5,2}), "highlighted order or membership changed");
            var stage = line.GetProperty("stage");
            Check(stage.GetArrayLength()==2 && stage[0].GetProperty("slot").GetInt32()==2 && stage[1].GetProperty("slot").GetInt32()==5, "stage order changed");
            Check(!stage[0].TryGetProperty("starting_position",out _) && !stage[1].TryGetProperty("starting_position",out _), "read-only derived positions leaked into LineInput");
            Check(stage[0].GetProperty("ending_position").GetInt32()==3 && stage[0].GetProperty("display_order").GetInt32()==5, "writable stage positions lost");
            Check(result.SourceRead.GetProperty("stage")[0].GetProperty("starting_position").GetInt32()==4, "source proof snapshot lost read-only values");
        });
        Test("copy replays one-shot stage values rather than using inherited defaults", () => {
            var actor=Project(Parse(fixture)).LineInput.GetProperty("stage")[0];
            Check(actor.GetProperty("action").GetString()=="Greeting" && actor.GetProperty("effect").GetString()=="Shot" && actor.GetProperty("emoticon").GetString()=="Heart" && actor.GetProperty("appear").GetString()=="AR", "one-shot content dropped");
            Check(actor.GetProperty("shape_overrides").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(new[]{"Sig","Black","Closeup"}), "shape flags lost");
            Check(actor.GetProperty("resource_id").GetString()=="fixture:actor-A" && actor.GetProperty("face_id").GetString()=="face-A", "opaque character/face identity replaced");
        });
        Test("all seven scene families and voice IDs survive explicit set projection", () => {
            var result=Project(Parse(fixture)); var scene=result.LineInput.GetProperty("scene");
            foreach(var item in new[]{("background","fixture:background"),("background_effect","fixture:background-effect"),("transition","fixture:transition"),("bgm","fixture:bgm"),("sound","fixture:sound"),("popup","fixture:popup"),("voice","fixture:voice")})
                Check(scene.GetProperty(item.Item1).GetProperty("action").GetString()=="set" && scene.GetProperty(item.Item1).GetProperty("resource_id").GetString()==item.Item2, item.Item1+" lost");
            Check(result.RequiredResources.Count==9, "a catalog obligation was omitted");
            Check(result.RequiredResources.Any(x=>x.Kind=="voice" && x.ResourceId=="fixture:voice" && x.Path=="$.scene.voice"), "voice was not scheduled for catalog revalidation");
            Check(result.RequiredResources.Any(x=>x.Kind=="character" && x.ResourceId=="fixture:actor-B" && x.FaceId=="face-B" && x.Path=="$.stage[1]"), "character face revalidation missing");
        });
        Test("explicit empty scene actions survive without fabricated resource references", () => {
            var input=Changed(root=>{foreach(var key in root["scene"]!.AsObject().Select(x=>x.Key).ToArray())root["scene"]![key]=new JsonObject{["action"]=key is "background" or "bgm" ? "inherit":"none"};});
            var result=Project(input);
            Check(result.RequiredResources.Count==2,"empty scene action invented a resource");
            foreach(var p in result.LineInput.GetProperty("scene").EnumerateObject()) Check(p.Value.GetProperty("action").GetString()==(p.Name is "background" or "bgm" ? "inherit":"none"), "empty action changed");
        });
        Test("curve extension fields and numeric lexemes survive as a complete JSON subtree", () => {
            var input=Parse(fixture); var result=Project(input);
            Check(result.LineInput.GetProperty("stage")[1].GetProperty("curve").GetRawText()==input.GetProperty("stage")[1].GetProperty("curve").GetRawText(), "curve was reconstructed, filtered or numerically rounded");
            Check(result.OutstandingProofs.HasFlag(AuthoringCopyOutstandingProof.NativeRoundTrip),"curve claimed native validation");
        });
        Test("offstage speaker gets an explicit character catalog obligation", () => {
            var result=Project(Changed(root=>root["speaker"]=new JsonObject{["offstage_resource_id"]="fixture:offstage"}));
            Check(result.LineInput.GetProperty("speaker").GetProperty("offstage_resource_id").GetString()=="fixture:offstage", "offstage identity lost");
            Check(result.RequiredResources.Count==10 && result.RequiredResources.Any(x=>x.Kind=="character" && x.ResourceId=="fixture:offstage" && x.FaceId==null && x.Path=="$.speaker"), "offstage revalidation missing");
        });
        Test("narration and control retain null speaker and may have empty stage", () => {
            foreach(var mode in new[]{"narration","control"}) {
                var result=Project(Changed(root=>{root["mode"]=mode;root["speaker"]=null;root["stage"]=new JsonArray();root["highlighted_slots"]=new JsonArray();}));
                Check(result.LineInput.GetProperty("mode").GetString()==mode && result.LineInput.GetProperty("speaker").ValueKind==JsonValueKind.Null && result.LineInput.GetProperty("stage").GetArrayLength()==0,"empty-stage non-dialogue changed");
            }
        });
        Test("projection remains an unqualified candidate with all unresolved proof obligations", () => {
            var result=Project(Parse(fixture));
            Check(result.OutstandingProofs== (AuthoringCopyOutstandingProof.SourceStateCoverage | AuthoringCopyOutstandingProof.EffectiveCatalog | AuthoringCopyOutstandingProof.NativeRoundTrip | AuthoringCopyOutstandingProof.TransactionAndEditor), "pure projection silently claimed source/native/editor acceptance");
        });
        Test("result owns its JSON after caller document disposal and source remains unchanged", () => {
            AuthoringCardPackCopyProjectionResult result;
            using(var doc=JsonDocument.Parse(fixture)) {var before=doc.RootElement.GetRawText();result=Project(doc.RootElement);Check(before==doc.RootElement.GetRawText(),"input mutated");}
            Check(result.SourceRead.GetProperty("line_id").GetString()==LineId && result.LineInput.GetProperty("text").GetString()!.EndsWith("\n"),"result retained disposed JSON");
        });
        Test("node and line identity mismatches reject without normalization or fabrication", () => {
            Reject(Parse(fixture),"IdentityMismatch","$.node_id",node:"different-node");
            Reject(Parse(fixture),"IdentityMismatch","$.line_id",line:"different-line");
            Reject(Parse(fixture),"InvalidIdentity","$.node_id",node:"");
            Reject(Parse(fixture),"InvalidIdentity","$.line_id",line:" ");
        });
        Test("unknown fields reject at every closed contract boundary", () => {
            foreach(var item in new[]{("$",(Action<JsonNode>)(r=>r["unmapped_state"]=true)),("$.stage[0]",r=>Stage(r)["unmapped_state"]=true),("$.speaker",r=>r["speaker"]!["unmapped_state"]=true),("$.scene",r=>r["scene"]!["unmapped_state"]=true),("$.scene.voice",r=>r["scene"]!["voice"]!["unmapped_state"]=true)})
                Reject(Changed(item.Item2),"UnknownField",item.Item1+".unmapped_state");
        });
        Test("missing fields reject instead of inheriting guessed defaults", () => {
            foreach(var field in new[]{"line_id","node_id","mode","speaker","text","stage","highlighted_slots","scene","place","extra_script"})
                Reject(Changed(r=>r.AsObject().Remove(field)),"MissingField","$."+field);
            foreach(var field in new[]{"slot","resource_id","face_id","starting_position","ending_position","display_order","action","emoticon","effect","appear","shape_overrides","curve"})
                Reject(Changed(r=>Stage(r).AsObject().Remove(field)),"MissingField","$.stage[0]."+field);
            foreach(var field in new[]{"background","background_effect","transition","bgm","sound","popup","voice"})
                Reject(Changed(r=>r["scene"]!.AsObject().Remove(field)),"MissingField","$.scene."+field);
        });
        Test("duplicate JSON names reject including unknown extension content", () => {
            Reject(Parse(fixture.Replace("\"line_id\":", "\"line_id\":\"duplicate\",\"line_id\":")),"DuplicateField","$.line_id");
            Reject(Parse(fixture.Replace("\"nested\":", "\"nested\":null,\"nested\":")),"DuplicateField","$.stage[1].curve.curve_extension.nested");
        });
        Test("stage slots and bounded positions reject duplicates and invalid values", () => {
            Reject(Changed(r=>Stage(r,1)["slot"]=2),"DuplicateSlot","$.stage[1].slot");
            foreach(var field in new[]{"slot","ending_position","display_order"})foreach(var bad in new[]{0,6})
                Reject(Changed(r=>Stage(r)[field]=bad),"InvalidInteger","$.stage[0]."+field);
            Reject(Changed(r=>Stage(r)["starting_position"]="4"),"InvalidInteger","$.stage[0].starting_position");
            Reject(Changed(r=>{var a=r["stage"]!.AsArray();for(int i=0;i<4;i++)a.Add(JsonNode.Parse(a[0]!.ToJsonString()));}),"InvalidArray","$.stage");
        });
        Test("highlight and shape arrays reject duplicates invalid members and overflow", () => {
            Reject(Changed(r=>r["highlighted_slots"]=new JsonArray(2,2)),"DuplicateValue","$.highlighted_slots[1]");
            Reject(Changed(r=>r["highlighted_slots"]=new JsonArray(6)),"InvalidInteger","$.highlighted_slots[0]");
            Reject(Changed(r=>r["highlighted_slots"]=new JsonArray(1,2,3,4,5,1)),"InvalidArray","$.highlighted_slots");
            Reject(Changed(r=>Stage(r)["shape_overrides"]=new JsonArray("Sig","Sig")),"DuplicateValue","$.stage[0].shape_overrides[1]");
            Reject(Changed(r=>Stage(r)["shape_overrides"]=new JsonArray("Unknown")),"UnsupportedValue","$.stage[0].shape_overrides[0]");
        });
        Test("all unrecognized one-shot enums reject rather than downgrade to Idle or None", () => {
            foreach(var field in new[]{"action","emoticon","effect","appear"})Reject(Changed(r=>Stage(r)[field]="FutureValue"),"UnsupportedValue","$.stage[0]."+field);
        });
        Test("curve action contract rejects missing object and noncurve residual curve", () => {
            Reject(Changed(r=>Stage(r,1)["curve"]=null),"CurveActionMismatch","$.stage[1].curve");
            Reject(Changed(r=>Stage(r,1)["curve"]="serialized text"),"CurveActionMismatch","$.stage[1].curve");
            Reject(Changed(r=>Stage(r)["curve"]=new JsonObject()),"CurveActionMismatch","$.stage[0].curve");
        });
        Test("scene action families reject swapped empty actions and unused resource IDs", () => {
            Reject(Changed(r=>r["scene"]!["bgm"]=new JsonObject{["action"]="none"}),"UnsupportedValue","$.scene.bgm.action");
            Reject(Changed(r=>r["scene"]!["voice"]=new JsonObject{["action"]="inherit"}),"UnsupportedValue","$.scene.voice.action");
            Reject(Changed(r=>r["scene"]!["voice"]!["action"]="none"),"UnexpectedResource","$.scene.voice.resource_id");
            Reject(Changed(r=>r["scene"]!["sound"]!["action"]="stop"),"UnsupportedValue","$.scene.sound.action");
        });
        Test("missing null empty and whitespace resource IDs never become native keys", () => {
            foreach(var value in new string?[]{null,""," "}) {
                Reject(Changed(r=>Stage(r)["resource_id"]=value),"InvalidString","$.stage[0].resource_id");
                Reject(Changed(r=>Stage(r)["face_id"]=value),"InvalidString","$.stage[0].face_id");
                Reject(Changed(r=>r["scene"]!["voice"]!["resource_id"]=value),"InvalidString","$.scene.voice.resource_id");
            }
            Reject(Changed(r=>r["scene"]!["voice"]!.AsObject().Remove("resource_id")),"MissingField","$.scene.voice.resource_id");
        });
        Test("invalid speaker combinations and mode semantics cannot silently clear data", () => {
            Reject(Changed(r=>r["speaker"]=null),"SpeakerModeMismatch","$.speaker");
            Reject(Changed(r=>r["mode"]="control"),"SpeakerModeMismatch","$.speaker");
            Reject(Changed(r=>r["mode"]="future"),"UnsupportedValue","$.mode");
            Reject(Changed(r=>r["speaker"]=new JsonObject{["slot"]=1}),"UnoccupiedSpeaker","$.speaker.slot");
            Reject(Changed(r=>r["speaker"]!["offstage_resource_id"]="fixture:actor"),"InvalidSpeaker","$.speaker");
        });
        Test("invalid JSON kinds and non-string text reject without coercion", () => {
            Reject(default,"InvalidObject","$");Reject(Parse("[]"),"InvalidObject","$");
            foreach(var field in new[]{"text","place","extra_script"})Reject(Changed(r=>r[field]=null),"InvalidString","$."+field);
            Reject(Changed(r=>r["stage"]=new JsonObject()),"InvalidArray","$.stage");
        });
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed. Pure/static-shape only; no native execution.");
        return failed==0?0:1;
    }
}
