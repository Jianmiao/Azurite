using System.Reflection;
using Azurite;
using Azurite.Core;
using AzureArchive.Automation;

static partial class Program
{
    sealed class InvalidationHost : ICardPackV2Host
    {
        readonly ICardPackV2Host inner = Host();
        internal int Captures, Commits;
        internal Action? DuringCapture;
        public CardPackV2Snapshot Capture(string? locale) { Captures++; DuringCapture?.Invoke(); return inner.Capture(locale); }
        public CardPackV2CommitResult Commit(CardPackNativePlan plan, long revision) { Commits++; return inner.Commit(plan, revision); }
        public CardPackV2RecoveryResult Recover() => inner.Recover();
    }
    sealed class InvalidationView : IDisposable
    {
        readonly MethodInfo invalidate;
        internal readonly UIButton Preview = new(), Confirm = new(), Cancel = new(), Recover = new();
        internal readonly UILabel Text = new(), Status = new();
        internal readonly AuthoringCardPackPanel Panel;
        internal InvalidationView(CardPackV2Workflow workflow, Func<CardPackV2Draft> read)
        {
            invalidate = typeof(AuthoringCardPackPanel).GetMethod("InvalidateDraft", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(invalidate != null, "production Panel has no explicit invalidation boundary");
            Panel = new(workflow, read, Preview, Confirm, Cancel, Recover, Text, Status);
        }
        internal void Invalidate() => invalidate.Invoke(Panel, null);
        public void Dispose() => Panel.Dispose();
    }
    static void PanelInvalidation()
    {
        Test("resource invalidation clears a preview while retaining dedicated callbacks", () => {
            var host = new InvalidationHost(); var workflow = new CardPackV2Workflow(host);
            using var view = new InvalidationView(workflow, RecoveryDraft);
            view.Preview.Click(); Check(view.Confirm.isEnabled && view.Text.text.Length > 0, "preview missing");
            view.Invalidate(); view.Confirm.Click();
            Check(!view.Confirm.isEnabled && !view.Cancel.isEnabled && view.Text.text == "" && view.Preview.onClick.Count == 1 && view.Confirm.onClick.Count == 1 && host.Commits == 0, "invalidation retained a token or rebuilt callbacks");
            view.Preview.Click(); view.Confirm.Click();
            Check(host.Commits == 1 && AuthoringEditorSession.Current!.Applies == 1, "fresh explicit preview and confirm no longer work");
        });
        Test("invalidation inside readDraft prevents native preview capture", () => {
            var host = new InvalidationHost(); InvalidationView? view = null;
            using (view = new(new CardPackV2Workflow(host), () => { view!.Invalidate(); return RecoveryDraft(); })) {
                view.Preview.Click(); Check(host.Captures == 0 && !view.Confirm.isEnabled && view.Text.text == "", "stale readDraft continued to capture");
            }
        });
        Test("invalidation inside preview capture cannot publish the completed stale token", () => {
            var host = new InvalidationHost(); using var view = new InvalidationView(new CardPackV2Workflow(host), RecoveryDraft);
            host.DuringCapture = view.Invalidate; view.Preview.Click();
            Check(host.Captures == 1 && !view.Confirm.isEnabled && view.Text.text == "" && view.Status.text.StartsWith("DRAFT_CHANGED:"), "late preview republished an invalidated token");
            host.DuringCapture = null; view.Preview.Click(); view.Confirm.Click();
            Check(host.Commits == 1, "invalidation stranded future confirmation");
        });
        Test("invalidation while confirming draft prevents entering Workflow commit", () => {
            var host = new InvalidationHost(); bool revoke = false; InvalidationView? view = null;
            using (view = new(new CardPackV2Workflow(host), () => { if (revoke) view!.Invalidate(); return RecoveryDraft(); })) {
                view.Preview.Click(); revoke = true; view.Confirm.Click();
                Check(host.Captures == 1 && host.Commits == 0 && !view.Confirm.isEnabled, "invalidated confirm entered the host");
            }
        });
        Test("disposal during draft read prevents a hidden preview after view destruction", () => {
            var host = new InvalidationHost(); InvalidationView? view = null;
            using (view = new(new CardPackV2Workflow(host), () => { view!.Dispose(); return RecoveryDraft(); })) {
                view.Preview.Click(); Check(host.Captures == 0 && view.Preview.onClick.Count == 0 && !view.Confirm.isEnabled, "disposed view captured a hidden preview");
            }
        });
        Test("draft invalidation keeps persistent recovery available without automatic host recovery", () => {
            var first = Host(); var plan = Plan(first); var native = AuthoringEditorSession.Current!;
            native.ThrowBeforeApply = true; first.Commit(plan, 7);
            var host = new InvalidationHost(); var workflow = new CardPackV2Workflow(host);
            using var view = new InvalidationView(workflow, RecoveryDraft);
            view.Preview.Click(); view.Invalidate();
            Check(workflow.RecoveryRequired && view.Recover.isEnabled && !view.Preview.isEnabled && !view.Confirm.isEnabled && native.Checks == 1 && native.Applies == 1, "invalidation erased or automatically recovered an uncertain operation");
        });
    }
}
