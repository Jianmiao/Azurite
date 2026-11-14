using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Azurite.Core;

/// <summary>Pure stable-ID deletion plan. The shared Host verifies full graph/normalization and editor state.</summary>
public sealed class AuthoringBatchDeletePlan
{
    internal AuthoringBatchDeletePlan(AuthoringBatchSelection selection, string operationsJson, IEnumerable<AuthoringBatchRow> removedRows)
    {
        Selection = selection; OperationsJson = operationsJson;
        RemovedRows = new ReadOnlyCollection<AuthoringBatchRow>(removedRows.ToArray());
    }
    public AuthoringBatchSelection Selection { get; }
    public string OperationsJson { get; }
    public IReadOnlyList<AuthoringBatchRow> RemovedRows { get; }
    public int OperationCount => RemovedRows.Count;
}

public sealed record AuthoringBatchDeletePlanResult(bool IsValid, AuthoringBatchDeletePlan? Plan,
    AuthoringBatchNativeFailure Failure, AuthoringBatchFailure LogicalFailure, string Message);

public static class AuthoringBatchDeleteCompiler
{
    public static AuthoringBatchDeletePlanResult TryCompile(AuthoringBatchSelection? selection)
    {
        var logical = AuthoringBatchPlanner.TryPlan(selection, AuthoringBatchAction.Delete);
        if (!logical.IsValid)
            return new(false, null, AuthoringBatchNativeFailure.LogicalPlanRejected, logical.Failure, logical.Message);
        if (logical.Plan!.Edits.Count > AuthoringBatchNativeCompiler.MaxNativeOperations)
            return new(false, null, AuthoringBatchNativeFailure.NativeOperationLimit, AuthoringBatchFailure.None,
                "Native Apply accepts at most 500 deletions; this batch is never split.");
        var source = selection!.Rows.ToDictionary(row => row.StableId);
        var removed = logical.Plan.Edits.Select(edit => source[edit.StableId]).ToArray();
        string json = JsonSerializer.Serialize(removed.Select(row => new { op = "line.delete", line = new { id = row.StableId } }).ToArray());
        using var serialized = JsonDocument.Parse(json);
        for (int i = 0; i < removed.Length; i++)
            if (serialized.RootElement[i].GetProperty("line").GetProperty("id").GetString() != removed[i].StableId)
                return new(false, null, AuthoringBatchNativeFailure.InvalidUnicode, AuthoringBatchFailure.None, "A deletion ID cannot be serialized without changing it.");
        return new(true, new AuthoringBatchDeletePlan(selection, json, removed), AuthoringBatchNativeFailure.None, AuthoringBatchFailure.None, string.Empty);
    }
}
