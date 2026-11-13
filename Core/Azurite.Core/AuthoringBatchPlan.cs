using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Azurite.Core;

public enum AuthoringBatchAction
{
    ChangeSpeaker,
    SetText,
    Duplicate,
    Delete,
    AddMultiline
}

public enum AuthoringBatchFailure
{
    None,
    NullSelection,
    EmptySelection,
    InvalidSelection,
    StaleSelection,
    InvalidSpeaker,
    EmptyText,
    NoOp,
    TooManyOperations
}

public sealed record AuthoringBatchRow
{
    public AuthoringBatchRow(string stableId, int index, string text, int speakerSlot)
    {
        if (string.IsNullOrWhiteSpace(stableId)) throw new ArgumentException("Stable row ID is required.", nameof(stableId));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (text is null) throw new ArgumentNullException(nameof(text));
        StableId = stableId;
        Index = index;
        Text = text;
        SpeakerSlot = speakerSlot;
    }

    public string StableId { get; }
    public int Index { get; }
    public string Text { get; }
    public int SpeakerSlot { get; }
}

public sealed record AuthoringBatchSelection
{
    public AuthoringBatchSelection(long revision, string nodeId, IReadOnlyList<AuthoringBatchRow> rows)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("Node identity is required.", nameof(nodeId));
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        Revision = revision;
        NodeId = nodeId;
        Rows = new ReadOnlyCollection<AuthoringBatchRow>(rows.ToArray());
    }

    public long Revision { get; }
    public string NodeId { get; }
    public IReadOnlyList<AuthoringBatchRow> Rows { get; }
}

public sealed record AuthoringBatchEdit(
    AuthoringBatchAction Action,
    string StableId,
    int SourceIndex,
    int TargetIndex,
    string? Text,
    int? SpeakerSlot);

public sealed record AuthoringBatchPlan(
    AuthoringBatchAction Action,
    long Revision,
    string NodeId,
    IReadOnlyList<AuthoringBatchEdit> Edits);

public sealed record AuthoringBatchPlanResult(
    bool IsValid,
    AuthoringBatchPlan? Plan,
    AuthoringBatchFailure Failure,
    string Message)
{
    public static AuthoringBatchPlanResult Fail(AuthoringBatchFailure failure, string message) => new(false, null, failure, message);
    public static AuthoringBatchPlanResult Success(AuthoringBatchPlan plan) => new(true, plan, AuthoringBatchFailure.None, string.Empty);
}

/// <summary>
/// Host-independent validation and ordering for native authoring operations.
/// It never mutates a native list; the AA adapter must revalidate the snapshot
/// immediately before entering its history boundary.
/// </summary>
public static class AuthoringBatchPlanner
{
    public const int MaxOperations = 1024;

    public static AuthoringBatchPlanResult TryPlan(
        AuthoringBatchSelection? selection,
        AuthoringBatchAction action,
        string? text = null,
        int speakerSlot = 0)
    {
        if (selection is null) return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.NullSelection, "selection is required");
        if (selection.Rows.Count == 0) return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.EmptySelection, "selection is empty");
        if (!ValidateRows(selection.Rows, out string reason)) return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.InvalidSelection, reason);

        if (action == AuthoringBatchAction.ChangeSpeaker && (speakerSlot < 1 || speakerSlot > 5))
            return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.InvalidSpeaker, "speaker slot must be 1..5");
        if ((action == AuthoringBatchAction.SetText || action == AuthoringBatchAction.AddMultiline) && string.IsNullOrEmpty(text))
            return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.EmptyText, "text is required");

        var edits = new List<AuthoringBatchEdit>();
        switch (action)
        {
            case AuthoringBatchAction.ChangeSpeaker:
                foreach (AuthoringBatchRow row in selection.Rows)
                    if (row.SpeakerSlot != speakerSlot)
                        edits.Add(new(action, row.StableId, row.Index, row.Index, null, speakerSlot));
                break;
            case AuthoringBatchAction.SetText:
                foreach (AuthoringBatchRow row in selection.Rows)
                    if (!string.Equals(row.Text, text, StringComparison.Ordinal))
                        edits.Add(new(action, row.StableId, row.Index, row.Index, text, null));
                break;
            case AuthoringBatchAction.Delete:
                foreach (AuthoringBatchRow row in selection.Rows.OrderByDescending(row => row.Index))
                    edits.Add(new(action, row.StableId, row.Index, row.Index, null, null));
                break;
            case AuthoringBatchAction.Duplicate:
                for (int i = 0; i < selection.Rows.Count; i++)
                {
                    AuthoringBatchRow row = selection.Rows[i];
                    edits.Add(new(action, row.StableId, row.Index, row.Index + i + 1, null, null));
                }
                break;
            case AuthoringBatchAction.AddMultiline:
                string[] lines = SplitLines(text!);
                int added = 0;
                foreach (AuthoringBatchRow row in selection.Rows)
                    foreach (string line in lines)
                        edits.Add(new(action, row.StableId, row.Index, row.Index + 1 + added++, line, null));
                break;
            default:
                return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.InvalidSelection, "unknown action");
        }

        if (edits.Count == 0) return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.NoOp, "batch would not change the selected rows");
        if (edits.Count > MaxOperations) return AuthoringBatchPlanResult.Fail(AuthoringBatchFailure.TooManyOperations, "operation limit exceeded");
        return AuthoringBatchPlanResult.Success(new AuthoringBatchPlan(action, selection.Revision, selection.NodeId, new ReadOnlyCollection<AuthoringBatchEdit>(edits)));
    }

    public static bool Matches(AuthoringBatchSelection expected, AuthoringBatchSelection current)
    {
        if (expected.Revision != current.Revision || !string.Equals(expected.NodeId, current.NodeId, StringComparison.Ordinal) || expected.Rows.Count != current.Rows.Count)
            return false;
        for (int i = 0; i < expected.Rows.Count; i++)
        {
            AuthoringBatchRow a = expected.Rows[i];
            AuthoringBatchRow b = current.Rows[i];
            if (!string.Equals(a.StableId, b.StableId, StringComparison.Ordinal) || a.Index != b.Index || !string.Equals(a.Text, b.Text, StringComparison.Ordinal) || a.SpeakerSlot != b.SpeakerSlot)
                return false;
        }
        return true;
    }

    private static bool ValidateRows(IReadOnlyList<AuthoringBatchRow> rows, out string reason)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int previous = -1;
        foreach (AuthoringBatchRow row in rows)
        {
            if (row is null || !ids.Add(row.StableId) || row.Index <= previous)
            {
                reason = "rows must have unique stable IDs and strictly increasing indices";
                return false;
            }
            previous = row.Index;
        }
        reason = string.Empty;
        return true;
    }

    private static string[] SplitLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
}

public readonly record struct AuthoringHistoryLink(bool LinkedWithPrevious, bool LinkedWithNext)
{
    public static AuthoringHistoryLink ForIndex(int index, int count) => new(index > 0, index + 1 < count);
}
