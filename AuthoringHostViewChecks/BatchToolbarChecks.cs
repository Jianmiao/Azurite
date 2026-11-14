using Azurite.Core;
using Azurite;
using UnityEngine;
using AzureArchive.Automation;
using System.Text.Json;

static partial class Program
{
    static T BatchFind<T>(string name) where T:class => GameObject.All.Single(x => x.name == "AzuriteBatch" + name && !x.IsDestroyed).GetComponent<T>()!;
    static void BatchClick(string name) => BatchFind<UIButton>(name).Click();
    sealed class BatchFixture : IDisposable
    {
        internal readonly Fixture Native = new();
        internal readonly BatchBoundaryHost Host = new();
        internal readonly AuthoringBatchWorkflow Workflow;
        internal readonly AuthoringCardPackHostResourcePicker Picker;
        internal readonly AuthoringCardPackHostBatchToolbar View;
        internal BatchFixture()
        {
            Host.Allowed = () => Native.Allowed;
            Workflow = new(Host, Native.Clients);
            Picker = new(Environment.CurrentManagedThreadId, () => Native.Allowed, "offstage", CardResourceKind.Character);
            View = new(Environment.CurrentManagedThreadId, Native.Parent.transform, Native.Typography, new(0,0), () => Native.Allowed, Workflow, Picker);
            Native.Clients.Starting += client => { if (ReferenceEquals(client, Workflow)) Native.View?.InvalidatePreview(); else View.InvalidatePreview(); };
            Native.Clients.RecoveryChanged += () => { Native.View?.InvalidatePreview(); View.InvalidatePreview(); };
            View.Update(true);
        }
        public void Dispose() { View.Dispose(); Native.Dispose(); }
    }
    static void WithBatch(Action<BatchFixture> action) { using var f = new BatchFixture(); action(f); }
    static readonly (string Name, Action Test)[] BatchCases =
    {
        ("batch workflow and native toolbar are compiled production types", () =>
        {
            Check(typeof(CardPackV2Workflow).Assembly.GetType("Azurite.Core.AuthoringBatchWorkflow") != null, "batch workflow is absent");
            Check(typeof(Program).Assembly.GetType("Azurite.AuthoringCardPackHostBatchToolbar") != null, "native batch toolbar is absent");
        }),
        ("native five-action callbacks capture then commit the exact preview once", () =>
        {
            foreach (var item in new[] { ("Speaker", AuthoringBatchAction.ChangeSpeaker), ("Text", AuthoringBatchAction.SetText), ("Duplicate", AuthoringBatchAction.Duplicate), ("Delete", AuthoringBatchAction.Delete), ("Multiline", AuthoringBatchAction.AddMultiline) })
                WithBatch(f =>
                {
                    BatchClick("Action" + item.Item1); BatchFind<UIInput>("Text").value = "new\r\n\rtrail\n";
                    BatchClick("Preview");
                    Check(BatchFind<UIButton>("Confirm").isEnabled, "preview failed for " + item.Item1 + ": " + BatchFind<UILabel>("Status").text);
                    var captured = f.Host.Captured; Check(f.Host.LastDraft!.Action == item.Item2, "wrong Host route");
                    BatchClick("Confirm"); BatchClick("Confirm");
                    Check(f.Host.BatchCaptures == 1 && f.Host.BatchCommits == 1 && ReferenceEquals(captured, f.Host.Committed), "recaptured/copied/repeated " + item.Item1);
                });
        }),
        ("speaker buttons route all five explicit slots and resource evidence", () =>
        {
            for (int slot = 1; slot <= 5; slot++) WithBatch(f =>
            {
                BatchClick("Slot" + slot); BatchClick("Preview");
                Check(f.Host.LastDraft?.SpeakerSlot == slot && BatchFind<UILabel>("PreviewText").text.Contains("verified-stage-character"), "speaker slot or verified resource missing");
                Check(BatchFind<UILabel>("PreviewText").text.Contains("characters[0]"), "offstage reset omitted");
            });
        }),
        ("duplicate keeps voice by default and omits only on explicit native click", () => WithBatch(f =>
        {
            BatchClick("ActionDuplicate"); BatchClick("Preview"); Check(f.Host.LastDraft!.CopyVoice, "default voice was omitted");
            BatchClick("OmitVoice"); Check(!BatchFind<UIButton>("Confirm").isEnabled, "voice choice did not revoke token");
            BatchClick("Preview"); Check(!f.Host.LastDraft!.CopyVoice && BatchFind<UILabel>("PreviewText").text.Contains("复制时不带配音") && BatchFind<UILabel>("ConfirmLabel").text.Contains("无配音"), "omission was not explicit");
            BatchClick("Confirm"); Check(f.Host.BatchCommits == 1, "omitted copy did not commit");
        })),
        ("preserve-voice capture failure never retries without voice", () => WithBatch(f =>
        {
            f.Host.BeforeCapture = () => throw new InvalidOperationException("voice resource unavailable");
            BatchClick("ActionDuplicate"); BatchClick("Preview");
            Check(f.Host.LastDraft!.CopyVoice && f.Host.BatchCaptures == 0 && !BatchFind<UIButton>("Confirm").isEnabled && BatchFind<UILabel>("Status").text.Contains("unavailable"), "automatic omission fallback occurred");
        })),
        ("multiline preserves CR LF and trailing empty lines per anchor", () => WithBatch(f =>
        {
            BatchClick("ActionMultiline"); BatchFind<UIInput>("Text").value = "a\r\nb\rc\n"; BatchClick("Preview");
            var captured = (AuthoringBatchMultilineHostPreview)f.Host.Captured!;
            Check(captured.Options.Mode == AuthoringBatchMultilineMode.PerAnchorReadable && captured.Plan.Lines.Count == 8 && captured.Plan.Lines[3].Text == "" && captured.Plan.Lines[7].Text == "", "empty lines or per-anchor default lost");
            Check(BatchFind<UILabel>("PreviewText").text.Contains("（空行）"), "trailing empty line not readable");
        })),
        ("multiline explicit narration and stage dialogue stay user selected", () => WithBatch(f =>
        {
            BatchClick("ActionMultiline"); BatchFind<UIInput>("Text").value = "readable"; BatchClick("Mode1"); BatchClick("Preview");
            Check(f.Host.LastDraft!.Multiline!.Mode == AuthoringBatchMultilineMode.Narration, "explicit narration ignored");
            BatchClick("Mode2"); BatchClick("Slot5"); BatchClick("Preview");
            Check(f.Host.LastDraft!.Multiline == new AuthoringBatchMultilineOptions(AuthoringBatchMultilineMode.Dialogue,5), "explicit dialogue slot ignored");
        })),
        ("offstage dialogue uses actual queried and resolved resource", () => WithBatch(f =>
        {
            BatchClick("ActionMultiline"); BatchClick("Mode3"); BatchFind<UIInput>("Text").value = "offstage";
            BatchClick("Preview"); Check(!BatchFind<UIButton>("Confirm").isEnabled, "offstage silently selected");
            AuthoringResourceCatalog.Resources["offstage-id"] = "{\"resource_id\":\"offstage-id\",\"kind\":\"character\",\"catalog_version\":\"fixture-catalog\",\"available\":true,\"capabilities\":{\"offstage\":true}}";
            AuthoringResourceCatalog.SearchResults.Enqueue(Page("character",new[]{"offstage-id"})); BatchClick("OffstageSearch"); f.View.Update(true);
            Check(f.Picker.ReadChoice() == null, "search auto selected"); BatchClick("OffstageCandidate0"); f.View.Update(true); BatchClick("Preview");
            Check(f.Host.LastDraft!.Multiline!.OffstageResourceId == "offstage-id" && AuthoringResourceCatalog.Calls.Single().Kind == "character", "offstage native choice not forwarded");
        })),
        ("character lacking offstage capability cannot be selected", () => WithBatch(f =>
        {
            BatchClick("ActionMultiline"); BatchClick("Mode3"); AuthoringResourceCatalog.SearchResults.Enqueue(Page("character", new[]{"fixture-character"}));
            BatchClick("OffstageSearch"); f.View.Update(true); BatchClick("OffstageCandidate0");
            Check(f.Picker.ReadChoice() == null && !BatchFind<UIButton>("Confirm").isEnabled, "unsupported character selected");
        })),
        ("delete warns about empty retained node and removed anchor", () => WithBatch(f =>
        {
            f.Host.EmptyNode = true; BatchClick("ActionDelete"); BatchClick("Preview"); string text = BatchFind<UILabel>("PreviewText").text;
            Check(text.Contains("保留节点和连接") && text.Contains("当前锚点也会被删除") && text.Contains("row-b") && text.Contains("500"), "deletion consequences incomplete");
            BatchClick("Cancel"); BatchClick("Confirm"); Check(f.Host.BatchCommits == 0, "cancelled deletion committed");
        })),
        ("batch parameter edits revoke confirmation synchronously", () => WithBatch(f =>
        {
            BatchClick("ActionText"); BatchFind<UIInput>("Text").value = "first"; BatchClick("Preview"); BatchFind<UIInput>("Text").value = "second";
            Check(!BatchFind<UIButton>("Confirm").isEnabled && BatchFind<UILabel>("PreviewText").text == "", "edited draft retained visible token"); BatchClick("Confirm"); Check(f.Host.BatchCommits == 0, "stale edit committed");
        })),
        ("draft change during capture retires returned preview", () => WithBatch(f =>
        {
            f.Host.AfterCapture = f.View.InvalidatePreview; BatchClick("Preview");
            Check(!BatchFind<UIButton>("Confirm").isEnabled && f.Host.BatchCommits == 0, "reentrant invalidation was lost");
        })),
        ("actual card and batch controls immediately revoke each other", () => WithBatch(f =>
        {
            f.Native.Create(); Click("FamilyLanguage"); Click("Preview"); Check(Find<UIButton>("Confirm").isEnabled, "card not ready");
            BatchClick("Preview"); Check(!Find<UIButton>("Confirm").isEnabled && BatchFind<UIButton>("Confirm").isEnabled, "batch did not revoke card visible token");
            Click("Preview"); Check(!BatchFind<UIButton>("Confirm").isEnabled && Find<UIButton>("Confirm").isEnabled, "card did not revoke batch visible token");
        })),
        ("shared uncertainty blocks both controls until verified host recovery", () => WithBatch(f =>
        {
            f.Native.Create(); f.Host.Disposition = CardPackV2CommitDisposition.OutcomeUnknown; BatchClick("Preview"); BatchClick("Confirm"); f.Native.View!.Update(true);
            Check(f.Workflow.RecoveryRequired && f.Native.Workflow.RecoveryRequired && BatchFind<UIButton>("Recover").isEnabled && Find<UIButton>("Recover").isEnabled, "shared uncertainty not visible");
            f.Host.RecoverySucceeds = false; BatchClick("Recover"); Check(f.Workflow.RecoveryRequired, "failed recovery cleared fault");
            f.Native.Allowed = false; f.View.Update(true); f.Host.RecoverySucceeds = true; BatchClick("Recover");
            Check(f.Host.BatchRecovers == 2 && !f.Workflow.RecoveryRequired && !f.Native.Workflow.RecoveryRequired && f.Host.BatchCommits == 1, "recovery needed draft or retried write");
        })),
        ("rejected versus exceptional outcome uses only Host disposition", () =>
        {
            WithBatch(f => { f.Host.Disposition = CardPackV2CommitDisposition.RejectedBeforeMutation; BatchClick("Preview"); BatchClick("Confirm"); Check(!f.Workflow.RecoveryRequired && BatchFind<UILabel>("Status").text.StartsWith("HOST_REJECTED"), "pre-mutation rejection mislabeled"); });
            WithBatch(f => { f.Host.ThrowCommit = true; BatchClick("Preview"); BatchClick("Confirm"); Check(f.Workflow.RecoveryRequired && BatchFind<UILabel>("Status").text.StartsWith("OUTCOME_UNKNOWN"), "exception claimed rollback"); });
        }),
        ("idle batch ticks do not capture resolve or search", () => WithBatch(f =>
        {
            for(int i=0;i<10;i++) f.View.Update(true);
            Check(f.Host.BatchCaptures == 0 && AuthoringResourceCatalog.Searches.Count == 0 && AuthoringResourceCatalog.Calls.Count == 0, "idle did native heavy work");
        })),
        ("all batch text is literal and preview remains scrollable", () => WithBatch(f =>
        {
            BatchClick("ActionText"); BatchFind<UIInput>("Text").value = "[FF0000] exact \n"; BatchClick("Preview");
            Check(!BatchFind<UILabel>("PreviewText").supportEncoding && BatchFind<UILabel>("PreviewText").text.Contains("[FF0000]") && BatchFind<UILabel>("PreviewText").maxLineCount == 0, "plain text or scroll lost");
            Check(BatchFind<UITexture>("PreviewBackground").transform.parent != BatchFind<UIScrollView>("PreviewViewport").transform, "pointer scroll surface moves");
        })),
        ("batch close and disposal retain fault and remove callbacks", () =>
        {
            using var f = new BatchFixture(); f.Host.Disposition = CardPackV2CommitDisposition.OutcomeUnknown; BatchClick("Preview"); BatchClick("Confirm");
            var button = BatchFind<UIButton>("Preview"); f.View.Update(false); f.View.Update(true); Check(f.Workflow.RecoveryRequired, "close cleared fault");
            f.View.Dispose(); Check(button.onClick.Count == 0 && !f.Native.Parent.IsDestroyed, "batch dispose leaked callbacks or destroyed parent");
        }),
        ("workflow wrong token preserves current preview and changed draft consumes it", () =>
        {
            var host = new BatchBoundaryHost(); var workflow = new AuthoringBatchWorkflow(host); var draft = new AuthoringBatchDraft(AuthoringBatchAction.SetText,"new");
            var p = workflow.Preview(draft).Preview!; Check(workflow.Confirm(Guid.NewGuid(),draft).Code == "PREVIEW_REQUIRED", "foreign token accepted");
            Check(workflow.Confirm(p.ConfirmationToken,draft with { Text="changed" }).Code == "DRAFT_CHANGED", "draft change accepted");
            Check(workflow.Confirm(p.ConfirmationToken,draft).Code == "PREVIEW_REQUIRED" && host.BatchCommits == 0, "changed draft token reusable");
        }),
        ("batch operation limit rejection does not split or auto retry", () => WithBatch(f =>
        {
            BatchClick("ActionMultiline"); BatchFind<UIInput>("Text").value = string.Join("\n", Enumerable.Repeat("line",251)); BatchClick("Preview");
            Check(f.Host.BatchCaptures == 0 && !BatchFind<UIButton>("Confirm").isEnabled && BatchFind<UILabel>("Status").text.Contains("500"), "limit split or silently truncated");
        }))
    };
}
