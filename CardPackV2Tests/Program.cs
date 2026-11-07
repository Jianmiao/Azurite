using Azurite.Core;
using System.Text.Json;

static partial class Program
{
    static int passed, failed;
    static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    static void EqualJson(string expected, string actual) { Require(Canonical(Json(expected)) == Canonical(Json(actual)), "expected " + expected + " but got " + actual); }
    static string Canonical(JsonElement v) => v.ValueKind switch {
        JsonValueKind.Object => "{" + string.Join(",", v.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", v.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(v.GetString()), _ => v.GetRawText()
    };
    const string Stage = "[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"ending_position\":4,\"display_order\":3,\"action\":\"Idle\",\"emoticon\":\"Heart\",\"effect\":\"None\",\"appear\":\"None\",\"shape_overrides\":[\"Closeup\"]},{\"slot\":5,\"resource_id\":\"catalog:other/β\",\"face_id\":\"01\",\"ending_position\":1,\"display_order\":2,\"action\":\"Idle\",\"effect\":\"None\",\"curve\":{\"id\":\"retained\",\"name\":\"Existing curve\",\"duration\":1.5,\"tracks\":[]}}]";
    static readonly CardPackContextIdentity Identity = new("project:{opaque}", "session:[opaque]", "revision:007", "catalog:v3", "node:opaque/节点", "line:opaque/前行", 2);
    static CardResourceBinding Binding(string key, string id, CardResourceKind kind) => new(key, id, kind,
        SceneSetSupported: kind != CardResourceKind.Character, StageSupported: kind == CardResourceKind.Character,
        SupportedActions: new[] { "Idle", "Greeting", "Stiff", "Shake", "FalldownL", "FalldownR" }, SupportedEffects: new[] { "None", "Shot" }, FaceIds: new[] { "00", "01" });
    static CardPackCompilationContext Context() => new(Identity, Json(Stage), true,
        new[] { Binding("fade", "catalog:transition/77", CardResourceKind.Transition), Binding("hit", "catalog:sound/响", CardResourceKind.Sound), Binding("actor", "catalog:actor/α", CardResourceKind.Character), Binding("other", "catalog:other/β", CardResourceKind.Character) }, CardNativeCapability.AllSupported, "zh-CN");
    static CardPackV2 Pack(params CardV2[] cards) => new("2", "test.pack", "Test", cards);
    static CardV2 Transition() => new("fade", PresetCardKind.Transition, ResourceKey: "fade");
    static CardV2 Sound() => new("sound", PresetCardKind.SoundEffect, ResourceKey: "hit");
    static CardV2 ActionCard() => new("greet", PresetCardKind.Action, TargetSlot: 2, Action: "Greeting");
    static CardV2 Combat() => new("hit", PresetCardKind.Combat, ResourceKey: "hit", TargetSlot: 2, Action: "Stiff");
    static CardV2 Language() => new("spoken", PresetCardKind.Language, TargetSlot: 2, Variants: new Dictionary<string, string> { ["zh-CN"] = "老师，我们出发吧。", ["en"] = "Sensei, let's go." });
    static CardPackCompileResult Compile(CardPackV2 pack, CardPackCompilationContext? context = null, CardPackContextIdentity? expected = null) => CardPackV2Compiler.TryCompile(pack, context ?? Context(), expected ?? Identity);
    static CardPackNativePlan Success(CardPackCompileResult r) { Require(r.IsSuccess && r.Plan != null, string.Join(";", r.Errors.Select(x => x.Code + ":" + x.Message))); return r.Plan!; }
    static void Reject(CardPackCompileResult r, string code) { Require(!r.IsSuccess && r.Plan is null, "invalid pack leaked a plan"); Require(r.Errors.Any(x => x.Code == code), "missing error " + code + "; actual: " + string.Join(",", r.Errors.Select(x => x.Code))); }
    static string Operation(CardPackNativePlan plan, int i = 0) => Json(plan.OperationsJson)[i].GetRawText();
    static int Main() { Mapping(); Rejections(); SerializationAndMigration(); Hardening(); Console.WriteLine($"RESULT: {passed} passed; {failed} failed."); return failed == 0 ? 0 : 1; }
    static void Mapping()
    {
        Test("transition literal native JSON preserves opaque IDs", () => EqualJson(
            "[{\"op\":\"line.insert\",\"node\":{\"id\":\"node:opaque/节点\"},\"ref\":\"card_1\",\"after_line\":{\"id\":\"line:opaque/前行\"},\"line\":{\"inherit\":\"previous\",\"mode\":\"control\",\"speaker\":null,\"text\":\"\",\"scene\":{\"transition\":{\"action\":\"set\",\"resource_id\":\"catalog:transition/77\"}}}}]", Success(Compile(Pack(Transition()))).OperationsJson));
        Test("sound literal scene JSON leaves voice and BGM inherited", () => EqualJson(
            "{\"op\":\"line.insert\",\"node\":{\"id\":\"node:opaque/节点\"},\"ref\":\"card_1\",\"after_line\":{\"id\":\"line:opaque/前行\"},\"line\":{\"inherit\":\"previous\",\"mode\":\"control\",\"speaker\":null,\"text\":\"\",\"scene\":{\"sound\":{\"action\":\"set\",\"resource_id\":\"catalog:sound/响\"}}}}", Operation(Success(Compile(Pack(Sound()))))));
        const string actionStage = "[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"ending_position\":4,\"display_order\":3,\"action\":\"Greeting\",\"emoticon\":\"Heart\",\"effect\":\"None\",\"appear\":\"None\",\"shape_overrides\":[\"Closeup\"]},{\"slot\":5,\"resource_id\":\"catalog:other/β\",\"face_id\":\"01\",\"ending_position\":1,\"display_order\":2,\"action\":\"Idle\",\"effect\":\"None\",\"curve\":{\"id\":\"retained\",\"name\":\"Existing curve\",\"duration\":1.5,\"tracks\":[]}}]";
        Test("action literal JSON preserves full stage and retained curve", () => EqualJson(
            "{\"op\":\"line.insert\",\"node\":{\"id\":\"node:opaque/节点\"},\"ref\":\"card_1\",\"after_line\":{\"id\":\"line:opaque/前行\"},\"line\":{\"inherit\":\"previous\",\"mode\":\"control\",\"speaker\":null,\"text\":\"\",\"stage\":" + actionStage + "}}", Operation(Success(Compile(Pack(ActionCard()))))));
        Test("combat literal JSON includes Shot reaction and hit sound", () => EqualJson(
            "{\"inherit\":\"previous\",\"mode\":\"control\",\"speaker\":null,\"text\":\"\",\"stage\":[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"ending_position\":4,\"display_order\":3,\"action\":\"Stiff\",\"emoticon\":\"Heart\",\"effect\":\"Shot\",\"appear\":\"None\",\"shape_overrides\":[\"Closeup\"]},{\"slot\":5,\"resource_id\":\"catalog:other/β\",\"face_id\":\"01\",\"ending_position\":1,\"display_order\":2,\"action\":\"Idle\",\"effect\":\"None\",\"curve\":{\"id\":\"retained\",\"name\":\"Existing curve\",\"duration\":1.5,\"tracks\":[]}}],\"scene\":{\"sound\":{\"action\":\"set\",\"resource_id\":\"catalog:sound/响\"}}}", Json(Operation(Success(Compile(Pack(Combat()))))).GetProperty("line").GetRawText()));
        foreach (var (locale, text) in new[] { ("zh-CN", "老师，我们出发吧。"), ("en", "Sensei, let's go.") })
            Test("language " + locale + " selects supplied text, no UI locale op", () => EqualJson(
                "{\"op\":\"line.insert\",\"node\":{\"id\":\"node:opaque/节点\"},\"ref\":\"card_1\",\"after_line\":{\"id\":\"line:opaque/前行\"},\"line\":{\"inherit\":\"previous\",\"mode\":\"dialogue\",\"speaker\":{\"slot\":2},\"text\":" + JsonSerializer.Serialize(text) + "}}", Operation(Success(Compile(Pack(Language()), Context() with { SelectedLocale = locale })))));
        Test("ordered inserts chain refs and do not replay prior Shot", () => {
            var ops = Json(Success(Compile(Pack(Combat(), ActionCard(), Language()))).OperationsJson);
            EqualJson("{\"ref\":\"card_1\"}", ops[1].GetProperty("after_line").GetRawText()); EqualJson("{\"ref\":\"card_2\"}", ops[2].GetProperty("after_line").GetRawText());
            EqualJson(actionStage, ops[1].GetProperty("line").GetProperty("stage").GetRawText()); Require(ops.GetArrayLength() == 3 && ops[2].GetProperty("ref").GetString() == "card_3", "order wrong");
        });
        Test("null anchor is index zero with no predecessor inheritance", () => {
            var zero = Identity with { AnchorLineId = null }; var ctx = Context() with { Identity = zero, InheritedStage = Json("[]") };
            EqualJson("{\"op\":\"line.insert\",\"node\":{\"id\":\"node:opaque/节点\"},\"ref\":\"card_1\",\"after_line\":null,\"line\":{\"inherit\":\"none\",\"mode\":\"control\",\"speaker\":null,\"text\":\"\",\"scene\":{\"transition\":{\"action\":\"set\",\"resource_id\":\"catalog:transition/77\"}}}}", Operation(Success(Compile(Pack(Transition()), ctx, zero))));
        });
        Test("500 accepted in one pack", () => Require(Json(Success(Compile(Pack(Enumerable.Range(0,500).Select(i => Transition() with { CardId = "c" + i }).ToArray()))).OperationsJson).GetArrayLength() == 500, "count wrong"));
        Test("dialogue text is not trimmed or translated", () => Require(Json(Operation(Success(Compile(Pack(Language() with { Variants = new Dictionary<string,string> { ["zh-CN"] = "  原文\n二行  " } }))))).GetProperty("line").GetProperty("text").GetString() == "  原文\n二行  ", "text modified"));
        foreach (var reaction in new[] { "Shake", "FalldownL", "FalldownR" }) Test("verified combat " + reaction, () => Require(Json(Operation(Success(Compile(Pack(Combat() with { Action = reaction }))))).GetProperty("line").GetProperty("stage")[0].GetProperty("action").GetString() == reaction, "reaction lost"));
        Test("plan is detached from mutable caller collections", () => { var cards = new[] { Transition() }; var resources = Context().Resources.ToArray(); var plan = Success(Compile(Pack(cards), Context() with { Resources = resources })); var original = plan.OperationsJson; cards[0] = Sound(); resources[0] = resources[0] with { ResourceId = "mutated" }; Require(plan.OperationsJson == original && plan.Identity == Identity && plan.OperationCount == 1, "plan mutated"); });
    }
}

