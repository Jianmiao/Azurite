using Azurite.Core;
static partial class Program
{
    static void Rejections()
    {
        Test("middle invalid card rejects whole pack without prefix", () => Reject(Compile(Pack(Transition(), Sound() with { ResourceKey = "missing" }, ActionCard())), "RESOURCE_MISSING"));
        Test("501 rejected without splitting", () => Reject(Compile(Pack(Enumerable.Range(0,501).Select(i => Transition() with { CardId = "c" + i }).ToArray())), "OPERATION_LIMIT"));
        Test("empty pack rejected", () => Reject(Compile(Pack()), "EMPTY_PACK"));
        Test("unknown schema rejected", () => Reject(Compile(Pack(Sound()) with { SchemaVersion = "3" }), "SCHEMA_VERSION"));
        Test("duplicate logical card IDs rejected", () => Reject(Compile(Pack(Sound(), Sound())), "CARD_ID"));
        Test("resource kind mismatch rejected", () => Reject(Compile(Pack(Transition() with { ResourceKey = "hit" })), "RESOURCE_KIND"));
        Test("missing host capability rejected", () => Reject(Compile(Pack(Transition()), Context() with { NativeCapabilities = CardNativeCapability.LineInsert }), "HOST_CAPABILITY"));
        Test("missing scene resource capability rejected", () => Reject(Compile(Pack(Transition()), Context() with { Resources = Context().Resources.Select(x => x.ResourceKey == "fade" ? x with { SceneSetSupported = false } : x).ToArray() }), "RESOURCE_CAPABILITY"));
        Test("missing locale rejected", () => Reject(Compile(Pack(Language()), Context() with { SelectedLocale = "ja" }), "LOCALE_MISSING"));
        Test("missing variants rejected", () => Reject(Compile(Pack(Language() with { Variants = null })), "LANGUAGE_VARIANTS"));
        Test("missing explicit target slot rejected", () => Reject(Compile(Pack(ActionCard() with { TargetSlot = null })), "TARGET_SLOT"));
        Test("unoccupied target slot rejected", () => Reject(Compile(Pack(ActionCard() with { TargetSlot = 3 }), Context() with { Identity = Identity with { SelectedTargetSlot = 3 } }, Identity with { SelectedTargetSlot = 3 }), "TARGET_SLOT"));
        Test("different selected target rejected", () => Reject(Compile(Pack(ActionCard() with { TargetSlot = 5 })), "TARGET_SLOT"));
        Test("unknown action rejected", () => Reject(Compile(Pack(ActionCard() with { Action = "raise-hand" })), "ACTION_UNSUPPORTED"));
        Test("Curve action authoring rejected", () => Reject(Compile(Pack(ActionCard() with { Action = "Curve" })), "ACTION_UNSUPPORTED"));
        Test("Curve payload rejected rather than ignored", () => Reject(Compile(Pack(ActionCard() with { Curve = Json("{\"duration\":1}") })), "CURVE_UNSUPPORTED"));
        Test("duration rejected including zero", () => Reject(Compile(Pack(Transition() with { DurationMs = 0 })), "DURATION_UNSUPPORTED"));
        Test("irrelevant fields rejected rather than ignored", () => Reject(Compile(Pack(Sound() with { Action = "Greeting" })), "CARD_FIELDS"));
        Test("target action capability required", () => Reject(Compile(Pack(ActionCard()), Context() with { Resources = Context().Resources.Select(x => x.ResourceKey == "actor" ? x with { SupportedActions = new[] { "Idle" } } : x).ToArray() }), "RESOURCE_CAPABILITY"));
        Test("Shot capability required", () => Reject(Compile(Pack(Combat()), Context() with { Resources = Context().Resources.Select(x => x.ResourceKey == "actor" ? x with { SupportedEffects = new[] { "None" } } : x).ToArray() }), "RESOURCE_CAPABILITY"));
        Test("combat non-reaction rejected", () => Reject(Compile(Pack(Combat() with { Action = "Greeting" })), "COMBAT_REACTION"));
        Test("non-target resources preflighted", () => Reject(Compile(Pack(ActionCard()), Context() with { Resources = Context().Resources.Where(x => x.ResourceKey != "other").ToArray() }), "STAGE_RESOURCE"));
        Test("raw prior-line stage rejected", () => Reject(Compile(Pack(ActionCard()), Context() with { StageIsNativeInherited = false }), "STAGE_NOT_INHERITED"));
        Test("read-only stage fields cannot leak", () => Reject(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\",\"starting_position\":1}]") }), "STAGE_SCHEMA"));
        Test("duplicate stage slots rejected", () => Reject(Compile(Pack(ActionCard()), Context() with { InheritedStage = Json("[{\"slot\":2,\"resource_id\":\"catalog:actor/α\"},{\"slot\":2,\"resource_id\":\"catalog:other/β\"}]") }), "STAGE_SCHEMA"));
        foreach (var stale in new[] { Identity with { ProjectId = "other" }, Identity with { EditSession = "other" }, Identity with { Revision = "other" }, Identity with { CatalogVersion = "other" }, Identity with { NodeId = "other" }, Identity with { AnchorLineId = "other" }, Identity with { SelectedTargetSlot = 5 } })
            Test("stale identity rejected " + stale, () => Reject(Compile(Pack(Sound()), Context() with { Identity = stale }), "STALE_CONTEXT"));
        Test("missing catalog identity rejected", () => { var empty = Identity with { CatalogVersion = "" }; Reject(Compile(Pack(Sound()), Context() with { Identity = empty }, empty), "CONTEXT_IDENTITY"); });
        Test("zero anchor cannot silently lose inherited stage", () => { var zero = Identity with { AnchorLineId = null }; Reject(Compile(Pack(Language()), Context() with { Identity = zero }, zero), "INITIAL_STAGE_REQUIRES_SOURCE"); });
    }
    static PresetCardPack Legacy(PresetCardTemplate card) => new("1", "legacy", "Legacy", Array.Empty<string>(), new[] { card });
    static PresetCardTemplate LegacyCard(PresetCardKind kind, PresetCardStepKind step, string? resource = null, string? action = null, string? locale = null, int? duration = null) => new("old.card", kind, "Old", resource is null ? Array.Empty<PresetCardResource>() : new[] { new PresetCardResource(resource, kind == PresetCardKind.Transition ? "transition" : "sfx") }, new[] { new PresetCardStep(step, resource, action, locale, duration) });
    static void SerializationAndMigration()
    {
        Test("JSON roundtrip preserves five families and exact variant keys", () => { var pack = Pack(Transition(), Sound(), ActionCard(), Combat(), Language()); var json = CardPackV2Json.Serialize(pack); var read = CardPackV2Json.Deserialize(json); Require(CardPackV2Json.Serialize(read) == json, "roundtrip differs"); EqualJson(Success(Compile(pack)).OperationsJson, Success(Compile(read)).OperationsJson); });
        foreach (var field in new[] { "extra_script", "command", "path", "arbitrary" }) Test("unknown field " + field + " rejected", () => {
            var json = "{\"schemaVersion\":\"2\",\"packId\":\"p\",\"displayName\":\"P\",\"cards\":[{\"cardId\":\"s\",\"kind\":\"soundEffect\",\"resourceKey\":\"hit\",\"" + field + "\":\"value\"}]}";
            try { CardPackV2Json.Deserialize(json); throw new Exception("unknown field accepted"); } catch (FormatException) { }
        });
        Test("duplicate JSON fields rejected", () => { try { CardPackV2Json.Deserialize("{\"schemaVersion\":\"2\",\"schemaVersion\":\"1\",\"packId\":\"p\",\"displayName\":\"P\",\"cards\":[]}"); throw new Exception("duplicate field accepted"); } catch (FormatException) { } });
        Test("v1 JSON requires migration", () => { try { CardPackV2Json.Deserialize("{\"schemaVersion\":\"1\",\"packId\":\"p\",\"displayName\":\"P\",\"cards\":[]}"); throw new Exception("legacy accepted"); } catch (FormatException) { } });
        Test("v1 transition migrates with real compiler output", () => { var r = CardPackV2Migration.TryMigrate(Legacy(LegacyCard(PresetCardKind.Transition, PresetCardStepKind.ApplyTransition, "fade"))); Require(r.IsSuccess && r.Pack != null, "migration failed"); EqualJson(Success(Compile(Pack(Transition()))).OperationsJson, Success(Compile(r.Pack!)).OperationsJson); });
        Test("v1 sound migrates with binding retained", () => { var r = CardPackV2Migration.TryMigrate(Legacy(LegacyCard(PresetCardKind.SoundEffect, PresetCardStepKind.PlaySound, "hit"))); Require(r.IsSuccess && r.Pack != null, "migration failed"); EqualJson(Success(Compile(Pack(Sound()))).OperationsJson, Success(Compile(r.Pack!)).OperationsJson); });
        foreach (var legacy in new[] { LegacyCard(PresetCardKind.Language, PresetCardStepKind.SetLanguage, locale:"zh-CN"), LegacyCard(PresetCardKind.Action, PresetCardStepKind.ApplyAction, action:"Greeting"), LegacyCard(PresetCardKind.Combat, PresetCardStepKind.StartCombat, resource:"hit", action:"hit"), LegacyCard(PresetCardKind.Transition, PresetCardStepKind.ApplyTransition, resource:"fade", duration:250) })
            Test("unsupported legacy semantics require migration " + legacy.Kind, () => { var r = CardPackV2Migration.TryMigrate(Legacy(legacy)); Require(!r.IsSuccess && r.Pack is null && r.Errors.Any(x => x.Code == "MIGRATION_NEEDED"), "legacy reinterpreted"); });
        Test("examples compile all five families with supplied bindings", () => { var pack = CardPackV2Examples.Create(2, "fade", "hit"); Require(pack.Cards.Select(x => x.Kind).Distinct().Count() == 5, "family missing"); Require(Success(Compile(pack)).OperationCount == 5, "example invalid"); });
    }
}
