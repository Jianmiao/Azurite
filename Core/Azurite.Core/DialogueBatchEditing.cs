using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Azurite.Core;

/// <summary>
/// The small, host-independent part of dialogue editing.  It deliberately
/// contains no Unity, IL2CPP, or AA object references: a native adapter can
/// validate and apply its result through the existing synchronous inspector
/// methods and history boundary.
/// </summary>
public sealed record DialogueLine
{
    public DialogueLine(string text, int speakerSlot)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (speakerSlot < 0) throw new ArgumentOutOfRangeException(nameof(speakerSlot));
        Text = text;
        SpeakerSlot = speakerSlot;
    }

    public string Text { get; }

    /// <summary>AA's zero value means no speaker; positive values are slots.</summary>
    public int SpeakerSlot { get; }
}

public enum DialogueBatchOperationKind
{
    Add,
    Delete,
    Update
}

public sealed record DialogueBatchOperation
{
    private DialogueBatchOperation(DialogueBatchOperationKind kind, int index, DialogueLine? line)
    {
        Kind = kind;
        Index = index;
        Line = line;
    }

    public DialogueBatchOperationKind Kind { get; }

    /// <summary>Zero-based model index. Add permits an index equal to Count.</summary>
    public int Index { get; }

    public DialogueLine? Line { get; }

    public static DialogueBatchOperation Add(int index, DialogueLine line) =>
        new(DialogueBatchOperationKind.Add, index, line ?? throw new ArgumentNullException(nameof(line)));

    public static DialogueBatchOperation Delete(int index) =>
        new(DialogueBatchOperationKind.Delete, index, null);

    public static DialogueBatchOperation Update(int index, DialogueLine line) =>
        new(DialogueBatchOperationKind.Update, index, line ?? throw new ArgumentNullException(nameof(line)));
}

public enum DialogueBatchError
{
    None,
    NullInput,
    TooManyOperations,
    TooManyLines,
    InvalidOperation,
    InvalidIndex,
    InvalidLine
}

public sealed record DialogueBatchApplyResult
{
    private DialogueBatchApplyResult(
        bool applied,
        IReadOnlyList<DialogueLine> lines,
        DialogueBatchError errorCode,
        int failedOperation,
        string? message)
    {
        Applied = applied;
        Lines = lines;
        ErrorCode = errorCode;
        FailedOperation = failedOperation;
        Message = message;
    }

    public bool Applied { get; }

    /// <summary>
    /// A detached snapshot.  Callers may replace their native model only after
    /// <see cref="Applied"/> is true.
    /// </summary>
    public IReadOnlyList<DialogueLine> Lines { get; }

    public DialogueBatchError ErrorCode { get; }

    public int FailedOperation { get; }

    public string? Message { get; }

    internal static DialogueBatchApplyResult Success(List<DialogueLine> lines) =>
        new(true, new ReadOnlyCollection<DialogueLine>(lines), DialogueBatchError.None, -1, null);

    internal static DialogueBatchApplyResult Failure(DialogueBatchError code, int failedOperation, string message) =>
        new(false, Array.Empty<DialogueLine>(), code, failedOperation, message);
}

public static class DialogueBatchEditor
{
    public const int DefaultMaxOperations = 1024;
    public const int DefaultMaxLines = 4096;

    /// <summary>
    /// Validates and applies a batch against a detached working copy.  Any
    /// rejected operation leaves the caller's source untouched; this is the
    /// contract needed before entering AA's native mutation/history boundary.
    /// </summary>
    public static DialogueBatchApplyResult TryApply(
        IReadOnlyList<DialogueLine>? source,
        IReadOnlyList<DialogueBatchOperation>? operations,
        int maxOperations = DefaultMaxOperations,
        int maxLines = DefaultMaxLines)
    {
        if (source is null || operations is null)
            return DialogueBatchApplyResult.Failure(DialogueBatchError.NullInput, -1, "source and operations are required");
        if (maxOperations < 0 || maxLines < 0)
            throw new ArgumentOutOfRangeException(maxOperations < 0 ? nameof(maxOperations) : nameof(maxLines));
        if (operations.Count > maxOperations)
            return DialogueBatchApplyResult.Failure(DialogueBatchError.TooManyOperations, -1, "operation limit exceeded");
        if (source.Count > maxLines)
            return DialogueBatchApplyResult.Failure(DialogueBatchError.TooManyLines, -1, "line limit exceeded");

        var working = new List<DialogueLine>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            DialogueLine? line = source[i];
            if (line is null)
                return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidLine, i, "source contains a null line");
            working.Add(line);
        }

        for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            DialogueBatchOperation? operation = operations[operationIndex];
            if (operation is null)
                return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidOperation, operationIndex, "operation is null");

            switch (operation.Kind)
            {
                case DialogueBatchOperationKind.Add:
                    if (operation.Line is null)
                        return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidLine, operationIndex, "add requires a line");
                    if (operation.Index < 0 || operation.Index > working.Count)
                        return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidIndex, operationIndex, "add index is outside the working list");
                    if (working.Count == maxLines)
                        return DialogueBatchApplyResult.Failure(DialogueBatchError.TooManyLines, operationIndex, "line limit exceeded");
                    working.Insert(operation.Index, operation.Line);
                    break;

                case DialogueBatchOperationKind.Delete:
                    if (operation.Index < 0 || operation.Index >= working.Count)
                        return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidIndex, operationIndex, "delete index is outside the working list");
                    working.RemoveAt(operation.Index);
                    break;

                case DialogueBatchOperationKind.Update:
                    if (operation.Line is null)
                        return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidLine, operationIndex, "update requires a line");
                    if (operation.Index < 0 || operation.Index >= working.Count)
                        return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidIndex, operationIndex, "update index is outside the working list");
                    working[operation.Index] = operation.Line;
                    break;

                default:
                    return DialogueBatchApplyResult.Failure(DialogueBatchError.InvalidOperation, operationIndex, "unknown batch operation");
            }
        }

        return DialogueBatchApplyResult.Success(working);
    }
}

public static class SpeakerShortcutRouter
{
    public const int FirstSlot = 1;
    public const int LastSlot = 5;

    public static bool TryResolve(bool altPressed, int keyNumber, out int speakerSlot)
    {
        speakerSlot = 0;
        if (!altPressed || keyNumber < FirstSlot || keyNumber > LastSlot)
            return false;
        speakerSlot = keyNumber;
        return true;
    }
}

/// <summary>
/// Keyboard ownership gate used by the Unity adapter.  The adapter should pass
/// GetKeyDown state, so a held Alt key does not repeatedly mutate a line.
/// </summary>
public sealed class SpeakerShortcutController
{
    private readonly Action<int> _apply;

    public SpeakerShortcutController(Action<int> apply)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public bool TryHandle(bool altPressed, int keyNumber, bool editorActive, bool textInputFocused)
    {
        if (!editorActive || textInputFocused ||
            !SpeakerShortcutRouter.TryResolve(altPressed, keyNumber, out int slot))
            return false;
        _apply(slot);
        return true;
    }
}
