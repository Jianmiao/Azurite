using Azurite.Core;

namespace Azurite;

// Explicit native/Host boundary for real UI + pure compiler execution.
internal class BatchBoundaryHost : IAuthoringBatchHost, IAuthoringHostPresentation
{
    internal int BatchCaptures, BatchCommits, BatchRecovers;
    internal bool Faulted = false, RecoverySucceeds = true, ThrowCommit = false, EmptyNode = false;
    internal CardPackV2CommitDisposition Disposition = CardPackV2CommitDisposition.Applied;
    internal object? Captured, Committed;
    internal AuthoringBatchDraft? LastDraft;
    internal long Epoch;
    internal Action? BeforeCapture = null, AfterCapture = null;
    internal Func<AuthoringBatchSelection> Selection = () => new(7, "node", new[] { new AuthoringBatchRow("row-a", 0, "old", 1), new AuthoringBatchRow("row-b", 2, "second", 2) });
    internal Func<bool> Allowed = () => true;
    internal CardPackContextIdentity Identity => new("project", "session", "revision", "catalog", Selection().NodeId, "row-a", 2);
    T Capture<T>(T value) where T : class
    {
        BeforeCapture?.Invoke();
        if (!Allowed()) throw new InvalidOperationException("ordinary gate closed");
        if (Faulted) throw new CardPackRecoveryRequiredException("fixture unresolved submission");
        BatchCaptures++; Captured = value; AfterCapture?.Invoke(); return value;
    }
    CardPackV2CommitResult Commit(object value)
    {
        if (!ReferenceEquals(value, Captured)) throw new Exception("UI did not commit the exact captured reference");
        var rejected = ValidateCommit(value); if (rejected != null) { Captured = null; return rejected; }
        BatchCommits++; Committed = value; Captured = null; Epoch++;
        if (ThrowCommit) throw new InvalidOperationException("fixture ambiguous native result");
        if (Disposition == CardPackV2CommitDisposition.OutcomeUnknown) Faulted = true;
        return new(Disposition, "fixture verified disposition");
    }
    protected virtual CardPackV2CommitResult? ValidateCommit(object value) => null;
    public AuthoringBatchHostPreview CaptureSetText(string text)
    {
        LastDraft = new(AuthoringBatchAction.SetText, text);
        var p = AuthoringBatchNativeCompiler.TryCompileSetText(Selection(), text); if (!p.IsValid) throw new InvalidOperationException(p.Message);
        return Capture(new AuthoringBatchHostPreview(p.Plan!, Identity, Epoch));
    }
    public AuthoringBatchSpeakerHostPreview CaptureSpeaker(int slot)
    {
        LastDraft = new(AuthoringBatchAction.ChangeSpeaker, SpeakerSlot: slot);
        var p = AuthoringBatchSpeakerCompiler.TryCompile(Selection(), slot); if (!p.IsValid) throw new InvalidOperationException(p.Message);
        return Capture(new AuthoringBatchSpeakerHostPreview(p.Plan!, Identity, Epoch));
    }
    public AuthoringBatchDeleteHostPreview CaptureDelete()
    {
        LastDraft = new(AuthoringBatchAction.Delete);
        var p = AuthoringBatchDeleteCompiler.TryCompile(Selection()); if (!p.IsValid) throw new InvalidOperationException(p.Message);
        return Capture(new AuthoringBatchDeleteHostPreview(p.Plan!, Identity, Epoch, EmptyNode, "[]"));
    }
    public AuthoringBatchMultilineHostPreview CaptureMultiline(string text, AuthoringBatchMultilineOptions? options = null)
    {
        options ??= new(); LastDraft = new(AuthoringBatchAction.AddMultiline, text, Multiline: options);
        var anchors = Selection().Rows.Select(row => new AuthoringBatchMultilineAnchor(row.StableId,
            options.Mode == AuthoringBatchMultilineMode.Dialogue ? "dialogue" : "narration", options.SpeakerSlot, options.OffstageResourceId)).ToArray();
        var p = AuthoringBatchMultilineCompiler.TryCompile(Selection(), text, anchors); if (!p.IsValid) throw new InvalidOperationException(p.Message);
        return Capture(new AuthoringBatchMultilineHostPreview(p.Plan!, Identity, Epoch, options, "[]"));
    }
    public AuthoringBatchDuplicateHostPreview CaptureDuplicate(AuthoringBatchDuplicateOptions? options = null)
    {
        options ??= new(); LastDraft = new(AuthoringBatchAction.Duplicate, CopyVoice: options.CopyVoice);
        var inputs = Selection().Rows.Select(row => new AuthoringBatchDuplicateInput(row.StableId,
            System.Text.Json.JsonSerializer.Serialize(new { inherit = new { from = new { id = row.StableId } }, mode = "narration", speaker = (object?)null,
                text = row.Text, stage = Array.Empty<object>(), highlighted_slots = Array.Empty<int>(), place = "", extra_script = "", scene = new { voice = new { action = "none" } } }))).ToArray();
        var p = AuthoringBatchDuplicateCompiler.TryCompile(Selection(), inputs); if (!p.IsValid) throw new InvalidOperationException(p.Message);
        return Capture(new AuthoringBatchDuplicateHostPreview(p.Plan!, Identity, Epoch, options, "[]"));
    }
    public CardPackV2CommitResult CommitSetText(AuthoringBatchHostPreview p) => Commit(p);
    public CardPackV2CommitResult CommitSpeaker(AuthoringBatchSpeakerHostPreview p) => Commit(p);
    public CardPackV2CommitResult CommitDelete(AuthoringBatchDeleteHostPreview p) => Commit(p);
    public CardPackV2CommitResult CommitMultiline(AuthoringBatchMultilineHostPreview p) => Commit(p);
    public CardPackV2CommitResult CommitDuplicate(AuthoringBatchDuplicateHostPreview p) => Commit(p);
    public virtual CardPackV2RecoveryResult Recover() { BatchRecovers++; Epoch++; if (RecoverySucceeds) Faulted = false; return new(RecoverySucceeds, "fixture recovery"); }
    public AuthoringHostPresentationStatus ReadPresentationStatus(string session) => new(Epoch, Faulted, false);
    public string ReadSpeakerEvidence(AuthoringBatchSpeakerHostPreview p)
    {
        if (!ReferenceEquals(p, Captured)) throw new InvalidOperationException("foreign speaker preview");
        return "[{\"resource_id\":\"verified-stage-character\",\"face_id\":\"00\"}]";
    }
}

