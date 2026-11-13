using Azurite.Core;

static partial class Program
{
    // The host is the only external boundary. Compilation and workflow are the production code.
    sealed class CardHost : ICardPackV2Host
    {
        internal CardPackV2Snapshot Snapshot = new(Context(), 7);
        internal int Captures, Commits, Recoveries;
        internal CardPackNativePlan? LastPlan;
        internal long LastSelectionRevision;
        internal Exception? CaptureError, CommitError;
        internal CardPackV2CommitDisposition Disposition = CardPackV2CommitDisposition.Applied;
        internal bool CanRecover;
        internal Action? OnCommit;
        public CardPackV2Snapshot Capture(string? locale)
        {
            Captures++;
            if (CaptureError != null) throw CaptureError;
            return Snapshot with { Context = Snapshot.Context with { SelectedLocale = locale } };
        }
        public CardPackV2CommitResult Commit(CardPackNativePlan plan, long selectionRevision)
        {
            Commits++; LastPlan = plan; LastSelectionRevision = selectionRevision; OnCommit?.Invoke();
            if (CommitError != null) throw CommitError;
            return new(Disposition, "host receipt");
        }
        public CardPackV2RecoveryResult Recover() { Recoveries++; return new(CanRecover, "host recovery receipt"); }
    }
    static CardPackV2Draft Draft(params CardV2[] cards) => new(CardPackV2Json.Serialize(Pack(cards)), "zh-CN");
    static CardPackV2Preview Preview(CardPackV2Workflow flow, CardPackV2Draft draft)
    {
        var result = flow.Preview(draft);
        Require(result.IsSuccess && result.Preview != null, "preview failed: " + result.Code);
        return result.Preview!;
    }
    static void Confirmation()
    {
        Test("confirmation consumes five-family preview in one exact host commit", () => {
            var host = new CardHost(); var flow = new CardPackV2Workflow(host);
            var draft = Draft(Transition(), Combat(), ActionCard(), Language(), Sound());
            var preview = Preview(flow, draft);
            Require(host.Commits == 0 && preview.OperationCount == 5, "preview mutated host or lost cards");
            Require(preview.Text.Contains("老师，我们出发吧。") && preview.Text.Contains("catalog:transition/77") && preview.Text.Contains("Stiff"), "preview hides exact text/resource/action");
            var applied = flow.Confirm(preview.ConfirmationToken, draft);
            Require(applied.IsSuccess && applied.Code == "APPLIED", "native applied receipt not reported");
            Require(host.Captures == 2 && host.Commits == 1 && host.LastSelectionRevision == 7, "fresh capture/one-commit boundary violated");
            Require(host.LastPlan!.Identity == Identity && Json(host.LastPlan.OperationsJson).GetArrayLength() == 5, "saved native identity or operations lost");
            Require(!flow.Confirm(preview.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "confirmation replayed");
        });
        Test("draft edits including unused language text invalidate confirmation", () => {
            foreach (var changed in new[] { Draft(Language() with { Variants = new Dictionary<string,string> { ["zh-CN"] = "老师，我们出发吧。", ["en"] = "Changed" } }), Draft(Language()) with { SelectedLocale = "en" } }) {
                var host = new CardHost(); var flow = new CardPackV2Workflow(host); var original = Draft(Language()); var preview = Preview(flow, original);
                Require(flow.Confirm(preview.ConfirmationToken, changed).Code == "DRAFT_CHANGED" && host.Commits == 0, "changed draft committed");
                Require(!flow.Confirm(preview.ConfirmationToken, original).IsSuccess, "rejected token reused");
            }
        });
        Test("selection and every native identity change block commit", () => {
            var identities = new[] { Identity with { ProjectId = "other" }, Identity with { EditSession = "other" }, Identity with { Revision = "other" }, Identity with { CatalogVersion = "other" }, Identity with { NodeId = "other" }, Identity with { AnchorLineId = "other" }, Identity with { SelectedTargetSlot = 3 } };
            foreach (var identity in identities) {
                var host = new CardHost(); var flow = new CardPackV2Workflow(host); var draft = Draft(Transition()); var preview = Preview(flow, draft);
                host.Snapshot = host.Snapshot with { Context = host.Snapshot.Context with { Identity = identity } };
                Require(!flow.Confirm(preview.ConfirmationToken, draft).IsSuccess && host.Commits == 0, "changed native identity committed");
            }
            var selectedHost = new CardHost(); var selectedFlow = new CardPackV2Workflow(selectedHost); var sameDraft = Draft(Transition()); var selectedPreview = Preview(selectedFlow, sameDraft);
            selectedHost.Snapshot = selectedHost.Snapshot with { SelectionRevision = 8 };
            Require(selectedFlow.Confirm(selectedPreview.ConfirmationToken, sameDraft).Code == "STALE_SELECTION" && selectedHost.Commits == 0, "selection change committed");
        });
        Test("resource disappearance or rebinding is checked again before commit", () => {
            foreach (bool remove in new[] { true, false }) {
                var host = new CardHost(); var flow = new CardPackV2Workflow(host); var draft = Draft(Transition()); var preview = Preview(flow, draft);
                var resources = host.Snapshot.Context.Resources.Where(x => !remove || x.ResourceKey != "fade").Select(x => !remove && x.ResourceKey == "fade" ? x with { ResourceId = "changed:id" } : x).ToArray();
                host.Snapshot = host.Snapshot with { Context = host.Snapshot.Context with { Resources = resources } };
                Require(!flow.Confirm(preview.ConfirmationToken, draft).IsSuccess && host.Commits == 0, "stale resource committed");
            }
        });
        Test("busy capture and native rejection consume token without poisoning future previews", () => {
            var host = new CardHost(); var flow = new CardPackV2Workflow(host); var draft = Draft(Transition()); var preview = Preview(flow, draft);
            host.CaptureError = new InvalidOperationException("EDITOR_BUSY");
            Require(flow.Confirm(preview.ConfirmationToken, draft).Code == "CAPTURE_FAILED" && host.Commits == 0 && !flow.RecoveryRequired, "busy preflight mutated or poisoned workflow");
            host.CaptureError = null; host.Disposition = CardPackV2CommitDisposition.RejectedBeforeMutation;
            var next = Preview(flow, draft);
            Require(flow.Confirm(next.ConfirmationToken, draft).Code == "HOST_REJECTED" && host.Commits == 1 && !flow.RecoveryRequired, "rejected native check became success/uncertainty");
            Require(!flow.Confirm(next.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "rejected native check retried");
            Preview(flow, draft);
        });
        Test("unknown or thrown Apply requires explicit successful recovery and a new preview", () => {
            foreach (bool throws in new[] { true, false }) {
                var host = new CardHost { Disposition = CardPackV2CommitDisposition.OutcomeUnknown, CommitError = throws ? new Exception("restore failed") : null };
                var flow = new CardPackV2Workflow(host); var draft = Draft(Transition()); var preview = Preview(flow, draft);
                Require(flow.Confirm(preview.ConfirmationToken, draft).Code == "OUTCOME_UNKNOWN" && flow.RecoveryRequired && host.Commits == 1, "uncertainty not latched");
                Require(!flow.Preview(draft).IsSuccess && !flow.Confirm(preview.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "uncertain write retried");
                Require(!flow.Recover().IsSuccess && flow.RecoveryRequired, "failed recovery unlocked writes");
                host.CanRecover = true;
                Require(flow.Recover().IsSuccess && !flow.RecoveryRequired && host.Recoveries == 2, "verified recovery did not unlock a fresh workflow");
                Require(!flow.Confirm(preview.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "recovery replayed old token");
                host.CommitError = null; host.Disposition = CardPackV2CommitDisposition.Applied;
                Require(flow.Confirm(Preview(flow, draft).ConfirmationToken, draft).IsSuccess && host.Commits == 2, "new explicitly confirmed edit blocked after recovery");
            }
        });
        Test("cancel and replacement preview invalidate only their tokens", () => {
            var host = new CardHost(); var flow = new CardPackV2Workflow(host); var draft = Draft(Transition()); var old = Preview(flow, draft); var next = Preview(flow, draft);
            Require(old.ConfirmationToken != next.ConfirmationToken && !flow.Confirm(old.ConfirmationToken, draft).IsSuccess, "old preview remained confirmable");
            Require(flow.Confirm(next.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "old-token rejection consumed current preview");
            var canceled = Preview(flow, draft); flow.Cancel();
            Require(!flow.Confirm(canceled.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "cancel did not revoke token");
            var invalidated = Preview(flow, draft); Require(!flow.Preview(new("{}", "zh-CN")).IsSuccess, "invalid pack accepted");
            Require(!flow.Confirm(invalidated.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "failed preview retained previous token");
        });
        Test("cancel after uncertain Apply preserves uncertainty instead of claiming no submission", () => {
            var host = new CardHost { Disposition = CardPackV2CommitDisposition.OutcomeUnknown }; var flow = new CardPackV2Workflow(host); var draft = Draft(Transition());
            flow.Confirm(Preview(flow, draft).ConfirmationToken, draft);
            var cancelled = flow.Cancel();
            Require(!cancelled.IsSuccess && cancelled.Code == "RECOVERY_REQUIRED" && flow.RecoveryRequired && host.Commits == 1,
                "cancel reclassified an uncertain native submission as an ordinary unsubmitted preview");
        });
        Test("commit callback reentry cannot preview confirm cancel or recover", () => {
            var host = new CardHost(); var flow = new CardPackV2Workflow(host); var draft = Draft(Transition()); var preview = Preview(flow, draft);
            host.OnCommit = () => { Require(flow.Preview(draft).Code == "WORKFLOW_BUSY", "reentrant preview admitted"); Require(flow.Confirm(preview.ConfirmationToken, draft).Code == "WORKFLOW_BUSY", "reentrant confirm admitted"); Require(flow.Cancel().Code == "WORKFLOW_BUSY" && flow.Recover().Code == "WORKFLOW_BUSY", "reentrant state change admitted"); };
            Require(flow.Confirm(preview.ConfirmationToken, draft).IsSuccess && host.Commits == 1, "reentry caused extra/missing commit");
        });
        Test("native whole-pack limit is enforced before host commit", () => {
            var host = new CardHost(); var flow = new CardPackV2Workflow(host);
            var fiveHundred = Draft(Enumerable.Range(0,500).Select(i => Transition() with { CardId = "card" + i }).ToArray());
            Require(flow.Confirm(Preview(flow, fiveHundred).ConfirmationToken, fiveHundred).IsSuccess && host.LastPlan!.OperationCount == 500 && host.Commits == 1, "500-operation single commit lost");
            var tooMany = fiveHundred.Json.Replace("\"cards\":[", "\"cards\":[{\"cardId\":\"overflow\",\"kind\":\"transition\",\"resourceKey\":\"fade\"},", StringComparison.Ordinal);
            Require(!flow.Preview(new(tooMany, "zh-CN")).IsSuccess && host.Commits == 1, "501-card pack admitted or split");
        });
    }
}
