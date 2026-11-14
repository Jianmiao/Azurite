using System;
using System.Text;
using System.Text.Json;

namespace Azurite.Core;

public sealed record AuthoringBatchDraft(AuthoringBatchAction Action, string Text = "", int SpeakerSlot = 1,
    AuthoringBatchMultilineOptions? Multiline = null, bool CopyVoice = true);

/// <summary>One-shot UI token over an exact Host-owned preview. Confirm never captures again.</summary>
public sealed class AuthoringBatchWorkflow
{
    private sealed record Pending(AuthoringBatchDraft Draft, object Captured, CardPackV2Preview Preview);
    private readonly IAuthoringBatchHost _host;
    private readonly AuthoringClientSession _session;
    private Pending? _pending;
    private bool _busy;
    public bool RecoveryRequired => _session.RecoveryRequired;
    public AuthoringBatchWorkflow(IAuthoringBatchHost host, AuthoringClientSession? session = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _session = session ?? new();
        _session.Starting += sender => { if (!ReferenceEquals(sender, this)) _pending = null; };
    }
    public CardPackV2WorkflowResult Preview(AuthoringBatchDraft draft) => Run(() =>
    {
        _pending = null;
        if (RecoveryRequired) return Failure("RECOVERY_REQUIRED", "上次提交结果未确认；请先检查并恢复编辑状态。");
        try
        {
            object captured = draft.Action switch
            {
                AuthoringBatchAction.ChangeSpeaker => _host.CaptureSpeaker(draft.SpeakerSlot),
                AuthoringBatchAction.SetText => _host.CaptureSetText(draft.Text),
                AuthoringBatchAction.Duplicate => _host.CaptureDuplicate(new(draft.CopyVoice)),
                AuthoringBatchAction.Delete => _host.CaptureDelete(),
                AuthoringBatchAction.AddMultiline => _host.CaptureMultiline(draft.Text, draft.Multiline),
                _ => throw new ArgumentException("Unsupported batch action.")
            };
            var preview = Describe(captured);
            _pending = new(draft with { }, captured, preview);
            return new(true, "PREVIEW_READY", "尚未写入。请核对所选行、完整变化及资源后确认。", preview);
        }
        catch (CardPackRecoveryRequiredException error) { _session.ObserveRecoveryRequired(); return Failure("RECOVERY_REQUIRED", error.Message); }
        catch (Exception error) { return Failure("CAPTURE_FAILED", error.GetType().Name + ": " + error.Message); }
    });
    public CardPackV2WorkflowResult Confirm(Guid token, AuthoringBatchDraft draft) => Run(() =>
    {
        if (RecoveryRequired) return Failure("RECOVERY_REQUIRED", "上次提交结果未确认；请先检查并恢复编辑状态。");
        var pending = _pending;
        if (pending == null || pending.Preview.ConfirmationToken != token) return Failure("PREVIEW_REQUIRED", "确认已失效，请重新预览。");
        _pending = null;
        if (pending.Draft != draft) return Failure("DRAFT_CHANGED", "参数已改变，请重新预览。");
        CardPackV2CommitResult? result;
        try
        {
            // The original reference is the Host's ownership capability; no copy or recapture.
            result = pending.Captured switch
            {
                AuthoringBatchHostPreview p => _host.CommitSetText(p),
                AuthoringBatchSpeakerHostPreview p => _host.CommitSpeaker(p),
                AuthoringBatchDeleteHostPreview p => _host.CommitDelete(p),
                AuthoringBatchMultilineHostPreview p => _host.CommitMultiline(p),
                AuthoringBatchDuplicateHostPreview p => _host.CommitDuplicate(p),
                _ => throw new InvalidOperationException("Unknown preview type.")
            };
        }
        catch (Exception error) { return Unknown(error.GetType().Name + ": " + error.Message); }
        if (result?.Disposition == CardPackV2CommitDisposition.Applied) return new(true, "APPLIED", result.Message);
        if (result?.Disposition == CardPackV2CommitDisposition.RejectedBeforeMutation) return Failure("HOST_REJECTED", result.Message);
        return Unknown(result?.Message ?? "宿主未返回有效提交结果。");
    });
    public CardPackV2WorkflowResult Cancel() => Run(() =>
    {
        _pending = null;
        return RecoveryRequired ? Failure("RECOVERY_REQUIRED", "仅取消预览；此前提交仍待检查并恢复。") : new(true, "CANCELLED", "已取消预览，未提交。");
    }, false);
    public CardPackV2WorkflowResult Recover() => Run(() =>
    {
        _pending = null;
        if (!RecoveryRequired) return new(true, "NO_RECOVERY_NEEDED", "无需恢复。");
        CardPackV2RecoveryResult? result;
        try { result = _host.Recover(); }
        catch (Exception error) { return Failure("RECOVERY_FAILED", error.GetType().Name + ": " + error.Message); }
        if (result?.Recovered != true) return Failure("RECOVERY_FAILED", result?.Message ?? "宿主未确认恢复结果。");
        _session.ConfirmRecovery();
        return new(true, "RECOVERED", result.Message + " 后续操作须重新预览。");
    });
    private CardPackV2WorkflowResult Unknown(string message)
    {
        _session.ObserveRecoveryRequired();
        return Failure("OUTCOME_UNKNOWN", "提交结果未确认；不会自动重试或宣称已回滚。" + message);
    }
    private CardPackV2WorkflowResult Run(Func<CardPackV2WorkflowResult> action, bool interaction = true)
    {
        if (_busy) return Failure("WORKFLOW_BUSY", "当前操作尚未结束。");
        _busy = true;
        bool began = false;
        var result = Failure("WORKFLOW_FAILED", "操作未完成，请重新预览。");
        try
        {
            if (interaction && !(began = _session.Begin(this))) return Failure("WORKFLOW_BUSY", "另一个客户端仍在操作。");
            return result = action();
        }
        finally { _busy = false; if (began) _session.End(this, result); }
    }
    private static CardPackV2WorkflowResult Failure(string code, string message) => new(false, code, message);
    public static string Name(AuthoringBatchAction action) => action switch
    {
        AuthoringBatchAction.ChangeSpeaker => "批量更换发言槽位", AuthoringBatchAction.SetText => "批量替换全文",
        AuthoringBatchAction.Duplicate => "批量复制", AuthoringBatchAction.Delete => "批量删除",
        AuthoringBatchAction.AddMultiline => "多行插入", _ => "未知动作"
    };
    private CardPackV2Preview Describe(object captured)
    {
        AuthoringBatchSelection selection; CardPackContextIdentity identity; int count; string operations; string name;
        var details = new StringBuilder();
        switch (captured)
        {
            case AuthoringBatchHostPreview p:
                selection = p.Plan.Selection; identity = p.Identity; count = p.Plan.OperationCount; operations = p.Plan.OperationsJson; name = Name(AuthoringBatchAction.SetText);
                foreach (var row in p.Plan.Changes) details.AppendLine(row.StableId + "\n原文：" + Literal(row.Before) + "\n替换：" + Literal(row.After));
                break;
            case AuthoringBatchSpeakerHostPreview p:
                selection = p.Plan.Selection; identity = p.Identity; count = p.Plan.OperationCount; operations = p.Plan.OperationsJson; name = Name(AuthoringBatchAction.ChangeSpeaker);
                details.AppendLine(p.OffstageResetNotice);
                foreach (var row in p.Plan.Changes) details.AppendLine(row.StableId + " | 槽位 " + row.BeforeSlot + " → " + row.AfterSlot);
                if (_host is not IAuthoringHostPresentation status) throw new InvalidOperationException("Host does not expose verified speaker resources.");
                details.AppendLine("已验证目标资源：").AppendLine(Pretty(status.ReadSpeakerEvidence(p)));
                break;
            case AuthoringBatchDeleteHostPreview p:
                selection = p.Plan.Selection; identity = p.Identity; count = p.Plan.OperationCount; operations = p.Plan.OperationsJson; name = Name(AuthoringBatchAction.Delete);
                details.AppendLine("将删除全部列出的所选行。" + p.EmptyNodeNotice)
                    .AppendLine(p.RemovesCurrentAnchor ? "当前锚点也会被删除；提交后重新取得原生选择。" : "当前锚点未列入删除。")
                    .AppendLine("派生变化：").AppendLine(Pretty(p.DerivedChangesJson));
                break;
            case AuthoringBatchMultilineHostPreview p:
                selection = p.Plan.Selection; identity = p.Identity; count = p.Plan.OperationCount; operations = p.Plan.OperationsJson; name = Name(AuthoringBatchAction.AddMultiline);
                details.AppendLine(p.InheritanceNotice);
                foreach (var row in p.Plan.Lines) details.AppendLine(row.AnchorId + " 后第 " + (row.LineOffset + 1) + " 行 | " + row.Mode + " | " + (row.OffstageResourceId ?? row.SpeakerSlot?.ToString() ?? "旁白") + "\n" + Literal(row.Text));
                details.AppendLine("完整继承与派生状态：").AppendLine(Pretty(p.InheritedAndDerivedStateJson));
                break;
            case AuthoringBatchDuplicateHostPreview p:
                selection = p.Plan.Selection; identity = p.Identity; count = p.Plan.OperationCount; operations = p.Plan.OperationsJson; name = Name(AuthoringBatchAction.Duplicate) + (p.Options.CopyVoice ? "（保留配音）" : "（复制时不带配音）");
                details.AppendLine(p.CopyNotice).AppendLine("完整复制与派生状态：").AppendLine(Pretty(p.CopiedAndDerivedStateJson));
                break;
            default: throw new InvalidOperationException("Host returned an invalid preview.");
        }
        var text = new StringBuilder().AppendLine(name + " — 待确认，尚未写入")
            .AppendLine("节点：" + identity.NodeId + " | 当前锚点：" + (identity.AnchorLineId ?? "无"))
            .AppendLine("目录版本：" + identity.CatalogVersion)
            .AppendLine("所选 " + selection.Rows.Count + " 行；实际变化 " + count + " 项；单次原生上限 " + AuthoringBatchNativeCompiler.MaxNativeOperations + " 项（不会拆分提交）。")
            .AppendLine("所选原行（原顺序；文本用 JSON 转义保留换行及空白）：");
        foreach (var row in selection.Rows) text.AppendLine((row.Index + 1) + ". " + row.StableId + " | 槽位 " + row.SpeakerSlot + " | " + Literal(row.Text));
        text.Append(details).AppendLine("精确原生操作：").AppendLine(Pretty(operations));
        return new(Guid.NewGuid(), name, count, text.ToString());
    }
    private static string Literal(string value) => value.Length == 0 ? "（空行） \"\"" : JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private static string Pretty(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
