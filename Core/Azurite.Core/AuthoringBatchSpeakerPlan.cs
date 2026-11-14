using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Azurite.Core;

public sealed record AuthoringBatchSpeakerChange(string StableId, int BeforeSlot, int AfterSlot);

/// <summary>Pure slot mapping only. The shared native Host must validate every selected row's mode, stage and effective resources.</summary>
public sealed class AuthoringBatchSpeakerPlan
{
    internal AuthoringBatchSpeakerPlan(AuthoringBatchSelection selection, int targetSlot, string operationsJson, IEnumerable<AuthoringBatchSpeakerChange> changes)
    {
        Selection = selection; TargetSlot = targetSlot; OperationsJson = operationsJson;
        Changes = new ReadOnlyCollection<AuthoringBatchSpeakerChange>(changes.ToArray());
    }
    public AuthoringBatchSelection Selection { get; }
    public int TargetSlot { get; }
    public string OperationsJson { get; }
    public IReadOnlyList<AuthoringBatchSpeakerChange> Changes { get; }
    public int OperationCount => Changes.Count;
}

public sealed record AuthoringBatchSpeakerPlanResult(bool IsValid, AuthoringBatchSpeakerPlan? Plan,
    AuthoringBatchNativeFailure Failure, AuthoringBatchFailure LogicalFailure, string Message);

public static class AuthoringBatchSpeakerCompiler
{
    public static AuthoringBatchSpeakerPlanResult TryCompile(AuthoringBatchSelection? selection, int targetSlot)
    {
        var logical = AuthoringBatchPlanner.TryPlan(selection, AuthoringBatchAction.ChangeSpeaker, speakerSlot: targetSlot);
        if (!logical.IsValid)
            return new(false, null, AuthoringBatchNativeFailure.LogicalPlanRejected, logical.Failure, logical.Message);
        if (logical.Plan!.Edits.Count > AuthoringBatchNativeCompiler.MaxNativeOperations)
            return new(false, null, AuthoringBatchNativeFailure.NativeOperationLimit, AuthoringBatchFailure.None,
                "Native Apply accepts at most 500 changed rows; speaker batches are never split.");
        var before = selection!.Rows.ToDictionary(row => row.StableId, row => row.SpeakerSlot);
        var changes = logical.Plan.Edits.Select(edit => new AuthoringBatchSpeakerChange(edit.StableId, before[edit.StableId], targetSlot)).ToArray();
        string json = JsonSerializer.Serialize(changes.Select(change => new
        {
            op = "line.update", line = new { id = change.StableId }, set = new { speaker = new { slot = targetSlot } }
        }).ToArray());
        // Reject the complete batch if serialization replaced an unpaired surrogate in any opaque ID.
        using var serialized = JsonDocument.Parse(json);
        for (int i = 0; i < changes.Length; i++)
            if (serialized.RootElement[i].GetProperty("line").GetProperty("id").GetString() != changes[i].StableId)
                return new(false, null, AuthoringBatchNativeFailure.InvalidUnicode, AuthoringBatchFailure.None, "A persistent ID cannot be serialized without changing it.");
        return new(true, new AuthoringBatchSpeakerPlan(selection, targetSlot, json, changes),
            AuthoringBatchNativeFailure.None, AuthoringBatchFailure.None, string.Empty);
    }
}
