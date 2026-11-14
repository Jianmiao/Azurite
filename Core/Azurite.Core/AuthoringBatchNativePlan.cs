using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Azurite.Core;

public enum AuthoringBatchNativeFailure
{
    None,
    LogicalPlanRejected,
    NativeOperationLimit,
    InvalidUnicode
}

public sealed record AuthoringBatchTextChange(string StableId, string Before, string After);

/// <summary>Pure SetText preview. It carries no native session, receipt, or permission to Apply.</summary>
public sealed class AuthoringBatchNativePlan
{
    internal AuthoringBatchNativePlan(AuthoringBatchSelection selection, string operationsJson, IEnumerable<AuthoringBatchTextChange> changes)
    {
        Selection = selection;
        OperationsJson = operationsJson;
        Changes = new ReadOnlyCollection<AuthoringBatchTextChange>(changes.ToArray());
    }

    public AuthoringBatchSelection Selection { get; }
    public string OperationsJson { get; }
    public IReadOnlyList<AuthoringBatchTextChange> Changes { get; }
    public int OperationCount => Changes.Count;
}

public sealed record AuthoringBatchNativePlanResult(
    bool IsValid,
    AuthoringBatchNativePlan? Plan,
    AuthoringBatchNativeFailure Failure,
    AuthoringBatchFailure LogicalFailure,
    string Message);

/// <summary>Compiles full-row SetText only; live capture, transaction ownership and result proof remain host responsibilities.</summary>
public static class AuthoringBatchNativeCompiler
{
    public const int MaxNativeOperations = 500;

    public static AuthoringBatchNativePlanResult TryCompileSetText(AuthoringBatchSelection? selection, string? text)
    {
        var logical = AuthoringBatchPlanner.TryPlan(selection, AuthoringBatchAction.SetText, text);
        if (!logical.IsValid)
            return new(false, null, AuthoringBatchNativeFailure.LogicalPlanRejected, logical.Failure, logical.Message);

        var edits = logical.Plan!.Edits;
        if (edits.Count > MaxNativeOperations)
            return new(false, null, AuthoringBatchNativeFailure.NativeOperationLimit, AuthoringBatchFailure.None,
                "Native Apply accepts at most 500 operations. This batch is never split across Apply calls.");

        // System.Text.Json replaces lone UTF-16 surrogates. Reject instead of changing text or opaque IDs.
        if (!IsWellFormedUnicode(text!) || edits.Any(edit => !IsWellFormedUnicode(edit.StableId)))
            return new(false, null, AuthoringBatchNativeFailure.InvalidUnicode, AuthoringBatchFailure.None,
                "Text and persistent IDs must contain valid Unicode without unpaired surrogates.");

        var before = selection!.Rows.ToDictionary(row => row.StableId, row => row.Text, StringComparer.Ordinal);
        var changes = edits.Select(edit => new AuthoringBatchTextChange(edit.StableId, before[edit.StableId], edit.Text!)).ToArray();
        var operations = changes.Select(change => new
        {
            op = "line.update",
            line = new { id = change.StableId },
            set = new { text = change.After }
        }).ToArray();

        // Omitted LinePatch fields preserve existing host state. Publish only the complete, validated array.
        var plan = new AuthoringBatchNativePlan(selection, JsonSerializer.Serialize(operations), changes);
        return new(true, plan, AuthoringBatchNativeFailure.None, AuthoringBatchFailure.None, string.Empty);
    }

    private static bool IsWellFormedUnicode(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
            i++;
        }
        return true;
    }
}
