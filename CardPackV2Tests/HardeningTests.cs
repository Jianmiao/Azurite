using Azurite.Core;
static partial class Program
{
    static void Hardening()
    {
        foreach (var field in new[] { "slot", "ending_position", "display_order" })
        foreach (var bad in new[] { "\"two\"", "null", "true", "{}" })
            Test("malformed stage numeric " + field + " " + bad, () => {
                var stage = field == "slot" ? "[{\"slot\":" + bad + ",\"resource_id\":\"catalog:actor/α\"}]" : "[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"" + field + "\":" + bad + "}]";
                Reject(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json(stage) }), "STAGE_SCHEMA");
            });
        Test("inherited omitted face needs catalog default 00", () => Reject(Compile(Pack(ActionCard()), Context() with {
            InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\"}]"),
            Resources = Context().Resources.Select(x => x.ResourceKey == "actor" ? x with { FaceIds = new[] { "01" } } : x).ToArray()
        }), "STAGE_RESOURCE"));
        Test("inherited null curve survives on non-target occupant", () => {
            var stage = "[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"action\":\"Idle\"},{\"slot\":5,\"resource_id\":\"catalog:other/β\",\"face_id\":\"01\",\"action\":\"Idle\",\"curve\":null}]";
            var actual = Json(Operation(Success(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json(stage) })))).GetProperty("line").GetProperty("stage");
            EqualJson("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"action\":\"Greeting\"},{\"slot\":5,\"resource_id\":\"catalog:other/β\",\"face_id\":\"01\",\"action\":\"Idle\",\"curve\":null}]", actual.GetRawText());
        });
        Test("inherited null curve survives on target occupant", () => {
            var stage = "[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"action\":\"Idle\",\"curve\":null}]";
            var actual = Json(Operation(Success(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json(stage) })))).GetProperty("line").GetProperty("stage");
            EqualJson("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"action\":\"Greeting\",\"curve\":null}]", actual.GetRawText());
        });
        foreach (var bad in new[] { "true", "[]", "\"curve\"" })
            Test("inherited non-object non-null curve rejected " + bad, () => Reject(Compile(Pack(ActionCard()), Context() with {
                InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"00\",\"curve\":" + bad + "}]")
            }), "STAGE_SCHEMA"));
        Test("malformed catalog cannot authorize empty explicit face", () => Reject(Compile(Pack(ActionCard()), Context() with {
            InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"\"}]"),
            Resources = Context().Resources.Select(x => x.ResourceKey == "actor" ? x with { FaceIds = new[] { "" } } : x).ToArray()
        }), "STAGE_RESOURCE"));
        Test("valid explicit face and omitted default still compile", () => {
            Success(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"face_id\":\"01\"}]") }));
            Success(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\"}]") }));
        });
        Test("legacy extra capability cannot silently disappear", () => {
            var old = Legacy(LegacyCard(PresetCardKind.Transition, PresetCardStepKind.ApplyTransition, "fade")) with { RequiredHostCapabilities = new[] { "audio.play" } };
            var result = CardPackV2Migration.TryMigrate(old);
            Require(!result.IsSuccess && result.Pack == null && result.Errors.Any(x => x.Code == "MIGRATION_NEEDED"), "unused legacy requirement dropped");
        });
        Test("JSON null text is rejected as format error", () => { try { CardPackV2Json.Deserialize(null!); throw new Exception("null accepted"); } catch (FormatException) {} });
        Test("explicit null Curve field is not ignored", () => { try { CardPackV2Json.Deserialize("{\"schemaVersion\":\"2\",\"packId\":\"p\",\"displayName\":\"P\",\"cards\":[{\"cardId\":\"s\",\"kind\":\"soundEffect\",\"resourceKey\":\"hit\",\"curve\":null}]}"); throw new Exception("Curve ignored"); } catch (FormatException) {} });
    }
}
