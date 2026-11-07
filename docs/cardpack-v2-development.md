# Card pack v2: offline model and compiler boundary

Date: 2026-11-08. This is development syntax/mapping work, not a public card package or a native-readiness claim. No AA session, preview, save, Undo, audio or rendering was invoked. The public RenderControlV1/v1 protocol is unchanged.

## Entry points and result

- `CardPackV2Json.Deserialize(json)` reads strict v2 data. Unknown or duplicate object properties are errors; optional fields irrelevant to a family are errors. It does not translate text, resolve files or run scripts.
- `CardPackV2Migration.TryMigrate(legacyPack)` explicitly converts supported typed v1 transition/sound data. `MIGRATION_NEEDED` returns no pack for code-only language, targetless action, unspecified combat, duration, unsupported/unused resource requirements or unmapped host capabilities. A valid migration is not evidence of available resources. The old v1 parser/mapper/catalog remain legacy compatibility evidence; they are not a new native integration path.
- `CardPackV2Compiler.TryCompile(pack, freshContext, expectedPreviewIdentity)` returns `CardPackCompileResult`. Success contains one detached `CardPackNativePlan`: plain `OperationsJson`, immutable `Identity`, and `OperationCount`. Failure has diagnostics and **no plan**, including an invalid middle card. It never calls the host or mutates its inputs.
- `CardPackV2Examples.Create(targetSlot, transitionKey, soundKey)` produces one data example for each of the five families. Callers must supply logical bindings resolved from the verified current catalog. It does not synthesize resource IDs.

## Version 2 data shape and five examples

One card produces one `line.insert` operation. `resourceKey` is a local logical binding key, not `resource_id`, native_key, filename or persistent line identity. Example slot 2 is a parameter to replace with the validated selected existing slot. The supplied zh-CN/en variants are exact content, not translation requests.

```json
{
  "schemaVersion": "2",
  "packId": "authoring.examples",
  "displayName": "Five-family data examples",
  "cards": [
    {"cardId":"transition","kind":"transition","resourceKey":"transition.selected"},
    {"cardId":"combat","kind":"combat","resourceKey":"sound.hit","targetSlot":2,"action":"Stiff"},
    {"cardId":"action","kind":"action","targetSlot":2,"action":"Greeting"},
    {"cardId":"language","kind":"language","targetSlot":2,"variants":{"zh-CN":"老师，我们出发吧。","en":"Sensei, let's go."}},
    {"cardId":"sound","kind":"soundEffect","resourceKey":"sound.hit"}
  ]
}
```

The example is compilable only if the current character verifies Greeting, Stiff and Shot, the selected slot exists, both logical resources resolve with the proper kinds/capabilities, the locale has supplied text, and the host capabilities are verified. Test catalog IDs are deliberately synthetic fixtures; they are never packaged as usable AA resource IDs.

Mappings:

| Family | Emitted native content |
|---|---|
| Transition | control line with `scene.transition:{action:"set",resource_id:resolvedId}` |
| Sound | control line with `scene.sound:{action:"set",resource_id:resolvedId}` |
| Action | control line with the **entire** inherited stage, replacing only target `action` |
| Combat | control line with the **entire** inherited stage, target `effect:"Shot"` plus verified `Stiff`, `Shake`, `FalldownL` or `FalldownR`, and resolved hit sound |
| Language | dialogue line with `{speaker:{slot:targetSlot},text:selectedVariant}`; no locale operation, editor UI change or translation |

Combat is a minimal combined hit/reaction/audio cue, not a combat system, timed choreography engine, damage/AI/turn model or proof of a visible result. Card actions are Idle, Greeting, FalldownL, FalldownR, Stiff, Shake, Jump and Hophop, additionally restricted by verified target resource capabilities. Duration overrides, Curve authoring and arbitrary action names are rejected rather than dropped.

## Context the future native adapter must provide

`CardPackContextIdentity` copies the native project ID, EditSession, Revision, CatalogVersion, selected node GUID, optional anchor LineId, and validated selected target slot. All identity strings are opaque: no derivation from names, UI row pointers or numeric indices. Equality against the captured preview identity rejects any changed field. This is data equality, not proof of native ownership or freshness.

`CardPackCompilationContext` additionally contains:

- `InheritedStage`: full native **already-inherited** writable stage array at the insertion point. Include all occupants and retained writable properties, including face, ending position, display order, emoticon, effect, appear, shape overrides and existing curve data. Exclude read-only fields such as starting_position, node_id and line_id.
- `StageIsNativeInherited`: the adapter's affirmative evidence that this is native inheritance output. Do not set this merely because the preceding line was read. One-shot actions are not inherited by the native engine. Replaying raw previous-line stage data can replay transient events. The adapter must verify the baseline remains valid throughout the insertion chain; this compiler does not implement the host's inheritance engine.
- `Resources`: logical-to-actual bindings, including every retained character as well as selected scene resources. Actual IDs come from one current `Search/Resolve` catalog version. Kind, stage support, scene-set support, supported action/effect names and available face IDs must be verified. Omitted face defaults to 00 and is checked. These properties and `CardNativeCapability` are normalized **internal inputs**, not asserted native JSON capability field names. No blanket `AllSupported` may be assumed in the adapter.
- `SelectedLocale`: exact key into supplied variants. Missing keys fail; no fallback or text transformation occurs.

Stage fields are projected and cloned, never replaced with a one-character array. Only target action/effect changes; all other writable data survives. Retained curve objects are copied intact but **not validated as curves by this compiler**: the adapter must supply a native-validated existing curve. The reconstructed external schema deliberately omits `appear` and `curve`; it cannot validate payloads retaining them. This intentional compatibility gap must be covered by native validation/acceptance, not by removing existing user data or claiming the subset is a complete schema.

Every operation uses `node:{id:copiedNodeId}` and a request-local `ref` (`card_1`, etc.). The first `after_line` is `{id:copiedAnchorId}`. Later operations chain `{ref:previousAlias}` in order. These aliases are not persistent IDs. Native code allocates line identities. `after_line:null` means index zero; the first line then uses `inherit:"none"`, later lines use previous. A nonempty inherited stage with a null anchor returns `INITIAL_STAGE_REQUIRES_SOURCE`: this minimal API cannot verify connected-node/start-of-dialogue inheritance without an explicit source. The adapter must not reinterpret this as append or guess a predecessor.

## Confirmation and native runtime gate

The effective whole-pack limit is **500**, below the approved logical ceiling of 1024. A 501-card pack returns no plan. Do not split Apply calls: that would change the whole-pack atomic/Undo contract.

At confirmation the future adapter must revalidate active Studio/session ownership, live project/node/anchor/selection, busy/input/save/export guards, catalog version and all resources. Recheck the unchanged previewed card content, selected locale and resulting operation string; pack/locale UI changes require a fresh preview even when native revision is unchanged. Call native `Check(savedEditSession, savedRevision, true, true)` and one `Current.Apply(parsedOperations, savedCatalogVersion)` at the proven native main-thread ownership boundary. The compiler's data validation does not replace Check. Do not manually Begin a session, replay sequential mutations, or add another history write.

Native Apply may rebuild objects. Refresh native/UI pointers and selection using returned persistent IDs after success or failure. An exception is not permission to automatically retry or claim rollback. Restore failure, snapshot-history behavior, one Undo/Redo, save/reopen, preview/effect/audio accuracy, stage inheritance between lines and language persistence remain unproved. Full supplied multilingual variants remain in the external card data; emitted native lines contain only the selected text. No plugin-side multilingual persistence was implemented.

## Evidence and tests

Native mapping is based on the local offline investigation at `E:\aamod\diagnostics\azurite-plan-20261108\native-contract\CONTRACT.md` and `operations.reconstructed-subset.schema.json`. That evidence is a static reconstruction, not a runtime schema export.

`CardPackV2Tests` links the actual production source files and tests literal JSON/behavior. `BoxSelectionTests`, `BatchEditingTests`, `PresetCardPackTests` and `CardPackIntegrationTests` preserve standalone pure model coverage from the reference development tree. `PresetCardPackTests` was changed to source links to avoid rebuilding or replacing a deployable Core artifact. The tests do not start AA. Legacy mapper tests describe the historical DTO boundary only; do not use them as native operation acceptance.

Run `dotnet run --project CardPackV2Tests/CardPackV2Tests.csproj -c Release`. Detailed red/green evidence, file ownership and remaining gates are in `.superpowers/sdd/azurite-authoring-20261108/models-report.md`.

## UI selection token after the round-1 review

The imported BoxSelection model has a focused, authorized correction: selection membership or anchor changes advance `SelectionRevision` once; true no-ops do not. Unchanged drag previews and End do not produce duplicate increments. Cancel/replacement Begin restore pointer-down state; Reconcile cancels then removes invalid rows. `Refresh` remains explicit list-generation invalidation, including on an empty selection, while ordinary `Clear` is change-detected.

This UI token is distinct from native project/session revision and does not version every row geometry/order change. The reference consumer gates Refresh by inspector/cache/count changes and Reconcile by identity invalidation, so stable frames need not invalidate a stable selection. Some existing consumer host hooks use Clear rather than Refresh, and its gesture flag is not reset by every model reconciliation; these remain adapter runtime/ownership checks, not reasons to expand this offline model patch into Plugin changes. See the round-1 report for hash provenance and the 16 revision regressions.
