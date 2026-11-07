using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Azurite.Core;

/// <summary>One data-only insertion. ResourceKey is a logical catalog binding, never a host ID or path.</summary>
public sealed record CardV2(string CardId, PresetCardKind Kind, string? ResourceKey = null,
    int? TargetSlot = null, string? Action = null, IReadOnlyDictionary<string, string>? Variants = null,
    int? DurationMs = null, JsonElement? Curve = null);
public sealed record CardPackV2(string SchemaVersion, string PackId, string DisplayName, IReadOnlyList<CardV2> Cards);
public enum CardResourceKind { Transition, Sound, Character }
/// <summary>Internal normalized evidence flags, NOT native catalog/schema property names.</summary>
[Flags]
public enum CardNativeCapability { None = 0, LineInsert = 1, SceneTransition = 2, SceneSound = 4, StageAction = 8, StageEffect = 16, DialogueText = 32, AllSupported = 63 }
/// <summary>The adapter resolves these values from one verified catalog version; the compiler never discovers resources.</summary>
public sealed record CardResourceBinding(string ResourceKey, string ResourceId, CardResourceKind Kind,
    bool SceneSetSupported, bool StageSupported, IReadOnlyList<string> SupportedActions,
    IReadOnlyList<string> SupportedEffects, IReadOnlyList<string> FaceIds);
/// <summary>Opaque identity copied from native state plus revalidated selected slot. No pointers, names or indices.</summary>
public sealed record CardPackContextIdentity(string ProjectId, string EditSession, string Revision,
    string CatalogVersion, string NodeId, string? AnchorLineId, int? SelectedTargetSlot);
/// <summary>
/// Trusted adapter input. InheritedStage must be native inheritance output projected to writable fields,
/// including every occupant and any existing native-validated curve, not a raw prior-line snapshot.
/// All cards here change only transient action/effect/text/scene cues; they never move/replace occupants.
/// The adapter must verify that this inherited baseline is valid throughout the planned insertion chain.
/// </summary>
public sealed record CardPackCompilationContext(CardPackContextIdentity Identity, JsonElement InheritedStage,
    bool StageIsNativeInherited, IReadOnlyList<CardResourceBinding> Resources,
    CardNativeCapability NativeCapabilities, string? SelectedLocale);
public sealed record CardPackDiagnostic(string Code, string Message, string? CardId = null);
public sealed record CardPackCompileResult(bool IsSuccess, CardPackNativePlan? Plan, IReadOnlyList<CardPackDiagnostic> Errors);
/// <summary>Detached immutable output for a later guarded native Check/Apply. Success means offline compilation only.</summary>
public sealed record CardPackNativePlan(string OperationsJson, CardPackContextIdentity Identity, int OperationCount);
public sealed record CardPackMigrationResult(bool IsSuccess, CardPackV2? Pack, IReadOnlyList<CardPackDiagnostic> Errors);
