using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Azurite.Core;

public sealed record CardPackV2Draft(string Json, string? SelectedLocale);
public sealed record CardPackV2Snapshot(CardPackCompilationContext Context, long SelectionRevision);
public enum CardPackV2CommitDisposition { RejectedBeforeMutation, Applied, OutcomeUnknown }
public sealed record CardPackV2CommitResult(CardPackV2CommitDisposition Disposition, string Message);
public sealed record CardPackV2RecoveryResult(bool Recovered, string Message);
public sealed record CardPackV2Preview(Guid ConfirmationToken, string DisplayName, int OperationCount, string Text);
public sealed record CardPackV2WorkflowResult(bool IsSuccess, string Code, string Message, CardPackV2Preview? Preview = null);

/// <summary>Structured host signal for a persistent unresolved native submission; never an automatic recovery instruction.</summary>
public sealed class CardPackRecoveryRequiredException : InvalidOperationException
{
    public CardPackRecoveryRequiredException(string message) : base(message) { }
}

/// <summary>
/// Synchronous owner-thread boundary. Capture verifies the active Studio/Current ownership,
/// input/save/export guards, persistent selection and fresh catalog/inherited stage.
/// Commit must recheck those guards and the supplied selection revision, call native
/// Check(session, revision, true, true), then at most one Apply with the saved catalog.
/// No await, manufactured session, sequential fallback or extra history write is allowed.
/// After Apply is entered, exceptions are OutcomeUnknown. Refresh native pointers and
/// selection after either outcome; never report RejectedBeforeMutation after entering Apply.
/// Recover is an explicit inspection/recovery of Current, model and history consistency,
/// not a retry or a readiness flag. Return true only when a new edit is safe.
/// The host also owns a session-wide uncertainty latch: recreating a view/workflow
/// must not bypass an unresolved Apply. Keep this workflow alive across panel closes;
/// Capture/Commit must reject an uncertain session until explicit recovery succeeds.
/// </summary>
public interface ICardPackV2Host
{
    CardPackV2Snapshot Capture(string? selectedLocale);
    CardPackV2CommitResult Commit(CardPackNativePlan plan, long selectionRevision);
    CardPackV2RecoveryResult Recover();
}

public sealed class CardPackV2Workflow
{
    private sealed record Pending(CardPackV2Draft Draft, CardPackV2Snapshot Snapshot,
        CardPackNativePlan Plan, CardPackV2Preview Preview);
    private readonly ICardPackV2Host _host;
    private readonly object _gate = new();
    private Pending? _pending;
    private bool _busy;
    private bool _recoveryRequired;

    public CardPackV2Workflow(ICardPackV2Host host) => _host = host ?? throw new ArgumentNullException(nameof(host));
    public bool RecoveryRequired { get { lock (_gate) return _recoveryRequired; } }

    /// <summary>Read-only preview. JSON and locale are immutable copies; even unused variant edits require a new preview.</summary>
    public CardPackV2WorkflowResult Preview(CardPackV2Draft draft) => Run(() =>
    {
        _pending = null;
        if (_recoveryRequired) return Failure("RECOVERY_REQUIRED", "上次提交结果未确认；请先检查并恢复编辑状态。");
        CardPackV2 pack;
        try { pack = CardPackV2Json.Deserialize(draft?.Json!); }
        catch (FormatException error) { return Failure("PACK_INVALID", error.Message); }
        var capture = Capture(draft!, out var snapshot);
        if (capture != null) return capture;
        var compiled = CardPackV2Compiler.TryCompile(pack, snapshot!.Context, snapshot.Context.Identity);
        if (!compiled.IsSuccess) return CompilationFailure(compiled);
        var plan = compiled.Plan!;
        var preview = new CardPackV2Preview(Guid.NewGuid(), pack.DisplayName, plan.OperationCount, Describe(pack, plan, draft!.SelectedLocale));
        _pending = new(draft with { }, snapshot, plan, preview);
        return new(true, "PREVIEW_READY", "预览已生成，尚未写入。请核对后确认。", preview);
    });

    /// <summary>
    /// Consumes a matching token before any revalidation. A failed attempt requires a new
    /// preview. A stale token never consumes a newer preview. No automatic retry exists.
    /// </summary>
    public CardPackV2WorkflowResult Confirm(Guid token, CardPackV2Draft draft) => Run(() =>
    {
        if (_recoveryRequired) return Failure("RECOVERY_REQUIRED", "上次提交结果未确认；请先检查并恢复编辑状态。");
        var pending = _pending;
        if (pending == null || token != pending.Preview.ConfirmationToken)
            return Failure("PREVIEW_REQUIRED", "确认已失效，请重新预览。");
        _pending = null;
        if (draft != pending.Draft) return Failure("DRAFT_CHANGED", "卡包内容或语言已改变，请重新预览。");
        var capture = Capture(draft, out var fresh);
        if (capture != null) return capture;
        if (fresh!.SelectionRevision != pending.Snapshot.SelectionRevision)
            return Failure("STALE_SELECTION", "选择已改变，请重新预览。");
        // Recompile against fresh resolved resources and inherited stage, never reuse a
        // mutable preview input. The compiler also compares every native identity field.
        var compiled = CardPackV2Compiler.TryCompile(CardPackV2Json.Deserialize(draft.Json), fresh.Context, pending.Plan.Identity);
        if (!compiled.IsSuccess) return CompilationFailure(compiled);
        var plan = compiled.Plan!;
        if (plan.OperationCount != pending.Plan.OperationCount ||
            !string.Equals(plan.OperationsJson, pending.Plan.OperationsJson, StringComparison.Ordinal))
            return Failure("PLAN_CHANGED", "资源或插入内容已改变，请重新预览。");

        // Host must perform its final ownership/guard/Check immediately before Apply.
        // The call is deliberately synchronous and is made once. No local history write.
        CardPackV2CommitResult? result;
        try { result = _host.Commit(plan, fresh.SelectionRevision); }
        catch (Exception error) { return Unknown(error.GetType().Name + ": " + error.Message); }
        if (result?.Disposition == CardPackV2CommitDisposition.Applied)
            return new(true, "APPLIED", result.Message);
        if (result?.Disposition == CardPackV2CommitDisposition.RejectedBeforeMutation)
            return Failure("HOST_REJECTED", result.Message);
        return Unknown(result?.Message ?? "宿主未返回有效提交结果。");
    });

    public CardPackV2WorkflowResult Cancel() => Run(() =>
    {
        _pending = null;
        if (_recoveryRequired) return Failure("RECOVERY_REQUIRED", "仅取消预览；之前提交的结果仍待确认，请检查并恢复编辑状态。");
        return new(true, "CANCELLED", "已取消预览，未提交。");
    });

    /// <summary>Explicit host recovery only. Does not recreate a preview or replay any operation.</summary>
    public CardPackV2WorkflowResult Recover() => Run(() =>
    {
        if (!_recoveryRequired) return new(true, "NO_RECOVERY_NEEDED", "无需恢复。");
        CardPackV2RecoveryResult? result;
        try { result = _host.Recover(); }
        catch (Exception error) { return Failure("RECOVERY_FAILED", error.GetType().Name + ": " + error.Message); }
        if (result?.Recovered != true) return Failure("RECOVERY_FAILED", result?.Message ?? "宿主未确认恢复结果。");
        _recoveryRequired = false;
        _pending = null;
        return new(true, "RECOVERED", result.Message + " 后续新操作须重新预览并确认。");
    });

    private CardPackV2WorkflowResult? Capture(CardPackV2Draft draft, out CardPackV2Snapshot? snapshot)
    {
        snapshot = null;
        try
        {
            var captured = _host.Capture(draft.SelectedLocale);
            if (captured?.Context?.Identity == null || captured.SelectionRevision < 0 ||
                !string.Equals(captured.Context.SelectedLocale, draft.SelectedLocale, StringComparison.Ordinal))
                return Failure("CAPTURE_FAILED", "宿主未提供有效的选择、原生上下文或指定语言。");
            snapshot = captured;
            return null;
        }
        catch (CardPackRecoveryRequiredException error)
        {
            _pending = null;
            _recoveryRequired = true;
            return Failure("RECOVERY_REQUIRED", error.Message);
        }
        catch (Exception error) { return Failure("CAPTURE_FAILED", error.GetType().Name + ": " + error.Message); }
    }

    private CardPackV2WorkflowResult Unknown(string message)
    {
        _recoveryRequired = true;
        return Failure("OUTCOME_UNKNOWN", "提交结果未确认；不会自动重试或宣称已回滚。" + message);
    }

    private CardPackV2WorkflowResult Run(Func<CardPackV2WorkflowResult> action)
    {
        lock (_gate)
        {
            if (_busy) return Failure("WORKFLOW_BUSY", "当前操作尚未结束。");
            _busy = true;
            try { return action(); }
            finally { _busy = false; }
        }
    }

    private static CardPackV2WorkflowResult Failure(string code, string message) => new(false, code, message);
    private static CardPackV2WorkflowResult CompilationFailure(CardPackCompileResult result) =>
        Failure(result.Errors.FirstOrDefault()?.Code ?? "COMPILE_FAILED", string.Join("\n", result.Errors.Select(error => error.Code + ": " + error.Message)));

    private static string Describe(CardPackV2 pack, CardPackNativePlan plan, string? locale)
    {
        using var operations = JsonDocument.Parse(plan.OperationsJson);
        var text = new StringBuilder().Append(pack.DisplayName).Append(" — ").Append(plan.OperationCount).AppendLine(" 行，待确认")
            .Append("节点：").AppendLine(plan.Identity.NodeId).Append("插入位置：").AppendLine(plan.Identity.AnchorLineId ?? "节点开头");
        for (int i = 0; i < pack.Cards.Count; i++)
        {
            var card = pack.Cards[i];
            var line = operations.RootElement[i].GetProperty("line");
            text.Append(i + 1).Append(". ").Append(card.CardId).Append(" | ");
            switch (card.Kind)
            {
                case PresetCardKind.Transition:
                    text.Append("转场：").Append(line.GetProperty("scene").GetProperty("transition").GetProperty("resource_id").GetString());
                    break;
                case PresetCardKind.SoundEffect:
                    text.Append("音效：").Append(line.GetProperty("scene").GetProperty("sound").GetProperty("resource_id").GetString());
                    break;
                case PresetCardKind.Action:
                    text.Append("动作 | 槽位 ").Append(card.TargetSlot).Append(" | ").Append(card.Action);
                    break;
                case PresetCardKind.Combat:
                    text.Append("受击 | 槽位 ").Append(card.TargetSlot).Append(" | Shot + ").Append(card.Action).Append(" | 音效：")
                        .Append(line.GetProperty("scene").GetProperty("sound").GetProperty("resource_id").GetString());
                    break;
                case PresetCardKind.Language:
                    text.Append("台词 | 槽位 ").Append(card.TargetSlot).Append(" | ").Append(locale).AppendLine()
                        .Append(line.GetProperty("text").GetString());
                    break;
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
