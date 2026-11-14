using System.Linq;

namespace Azurite.Core;

/// <summary>A host-bound preview; the native owner consumes it once and never retries Apply.</summary>
public sealed record AuthoringBatchHostPreview(AuthoringBatchNativePlan Plan, CardPackContextIdentity Identity, long SubmissionVersion);

public sealed record AuthoringBatchSpeakerHostPreview(AuthoringBatchSpeakerPlan Plan, CardPackContextIdentity Identity, long SubmissionVersion)
{
    public string OffstageResetNotice => "每个实际修改行的画外音角色记录（characters[0]）都会重置为宿主默认值；仅切换已有对白的发言槽位。";
}

public sealed record AuthoringBatchDeleteHostPreview(AuthoringBatchDeletePlan Plan, CardPackContextIdentity Identity, long SubmissionVersion, bool EmptiesNode, string DerivedChangesJson)
{
    public bool RemovesCurrentAnchor => Plan.RemovedRows.Any(row => row.StableId == Identity.AnchorLineId);
    public string EmptyNodeNotice => EmptiesNode ? "删除后保留节点和连接，行列表为空；不会自动添加占位行或删除节点。" : string.Empty;
}

public sealed record AuthoringBatchMultilineHostPreview(AuthoringBatchMultilinePlan Plan, CardPackContextIdentity Identity,
    long SubmissionVersion, AuthoringBatchMultilineOptions Options, string InheritedAndDerivedStateJson)
{
    public string InheritanceNotice => "每个原锚点后独立插入全部输入行；按原生 previous 规则继承并清理退场角色、一次性动作等状态。新行身份及语音身份由实际提交生成，不复制原语音；预览中的别名不是持久行 ID。";
}

/// <summary>Implemented by the same owner as ICardPackV2Host, sharing its boundary and unresolved-session recovery.</summary>
public interface IAuthoringBatchHost
{
    AuthoringBatchHostPreview CaptureSetText(string text);
    CardPackV2CommitResult CommitSetText(AuthoringBatchHostPreview preview);
    AuthoringBatchSpeakerHostPreview CaptureSpeaker(int targetSlot);
    CardPackV2CommitResult CommitSpeaker(AuthoringBatchSpeakerHostPreview preview);
    AuthoringBatchDeleteHostPreview CaptureDelete();
    CardPackV2CommitResult CommitDelete(AuthoringBatchDeleteHostPreview preview);
    AuthoringBatchMultilineHostPreview CaptureMultiline(string text, AuthoringBatchMultilineOptions? options = null);
    CardPackV2CommitResult CommitMultiline(AuthoringBatchMultilineHostPreview preview);
    CardPackV2RecoveryResult Recover();
}
