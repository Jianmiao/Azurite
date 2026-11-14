using Azurite.Core;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static DialogueLine Line(string text, int speaker = 0) => new(text, speaker);

static void AppliesAddDeleteUpdateAsOneTransaction()
{
    var source = new List<DialogueLine> { Line("one", 1), Line("two", 2) };
    var operations = new DialogueBatchOperation[]
    {
        DialogueBatchOperation.Add(1, Line("inserted", 3)),
        DialogueBatchOperation.Update(0, Line("edited", 4)),
        DialogueBatchOperation.Delete(2)
    };

    DialogueBatchApplyResult result = DialogueBatchEditor.TryApply(source, operations);

    Check(result.Applied, "valid batch should apply");
    Check(result.Lines.SequenceEqual(new[] { Line("edited", 4), Line("inserted", 3) }), "batch result ordering/content");
    Check(source.SequenceEqual(new[] { Line("one", 1), Line("two", 2) }), "source must not be mutated by pure apply");
}

static void RejectsInvalidBatchWithoutPartialChanges()
{
    var source = new List<DialogueLine> { Line("one", 1), Line("two", 2) };
    var operations = new DialogueBatchOperation[]
    {
        DialogueBatchOperation.Update(0, Line("edited", 4)),
        DialogueBatchOperation.Delete(9)
    };

    DialogueBatchApplyResult result = DialogueBatchEditor.TryApply(source, operations);

    Check(!result.Applied, "invalid batch should be rejected");
    Check(result.ErrorCode == DialogueBatchError.InvalidIndex, "invalid index error");
    Check(source.SequenceEqual(new[] { Line("one", 1), Line("two", 2) }), "rejected batch must not mutate source");
}

static void EnforcesOperationAndLineBounds()
{
    var source = new List<DialogueLine>();
    var tooMany = Enumerable.Repeat(DialogueBatchOperation.Add(0, Line("x")), DialogueBatchEditor.DefaultMaxOperations + 1).ToArray();
    DialogueBatchApplyResult tooManyResult = DialogueBatchEditor.TryApply(source, tooMany);
    Check(!tooManyResult.Applied && tooManyResult.ErrorCode == DialogueBatchError.TooManyOperations, "operation bound");

    var full = Enumerable.Repeat(Line("x"), DialogueBatchEditor.DefaultMaxLines).ToList();
    DialogueBatchApplyResult tooManyLinesResult = DialogueBatchEditor.TryApply(full, new[] { DialogueBatchOperation.Add(0, Line("overflow")) });
    Check(!tooManyLinesResult.Applied && tooManyLinesResult.ErrorCode == DialogueBatchError.TooManyLines, "line bound");
}

static void ResolvesOnlyAltNumberShortcuts()
{
    Check(SpeakerShortcutRouter.TryResolve(altPressed: true, keyNumber: 1, out var slot1) && slot1 == 1, "Alt+1");
    Check(SpeakerShortcutRouter.TryResolve(altPressed: true, keyNumber: 5, out var slot5) && slot5 == 5, "Alt+5");
    Check(!SpeakerShortcutRouter.TryResolve(altPressed: false, keyNumber: 1, out _), "bare number must pass through");
    Check(!SpeakerShortcutRouter.TryResolve(altPressed: true, keyNumber: 6, out _), "Alt+6 is outside shortcut range");
    Check(!SpeakerShortcutRouter.TryResolve(altPressed: true, keyNumber: 0, out _), "Alt+0 is outside shortcut range");
}

static void GateBlocksTextInputsAndInactiveEditor()
{
    var applied = new List<int>();
    var controller = new SpeakerShortcutController(slot => applied.Add(slot));

    Check(!controller.TryHandle(true, 2, editorActive: true, textInputFocused: true), "text input owns keyboard");
    Check(!controller.TryHandle(true, 2, editorActive: false, textInputFocused: false), "inactive editor must not consume keyboard");
    Check(controller.TryHandle(true, 2, editorActive: true, textInputFocused: false), "active editor accepts shortcut");
    Check(applied.SequenceEqual(new[] { 2 }), "shortcut callback slot");
}

AppliesAddDeleteUpdateAsOneTransaction();
RejectsInvalidBatchWithoutPartialChanges();
EnforcesOperationAndLineBounds();
ResolvesOnlyAltNumberShortcuts();
GateBlocksTextInputsAndInactiveEditor();
Console.WriteLine("BatchEditingTests: 5/5 passed");
int snapshots = SelectionSnapshotTests.Run();
int nativeText = NativeSetTextTests.Run();
return snapshots == 0 && nativeText == 0 ? 0 : 1;
