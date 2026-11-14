namespace Azurite.Core;

/// <summary>A host-bound preview; the native owner consumes it once and never retries Apply.</summary>
public sealed record AuthoringBatchHostPreview(AuthoringBatchNativePlan Plan, CardPackContextIdentity Identity, long SubmissionVersion);

/// <summary>Implemented by the same owner as ICardPackV2Host, sharing its boundary and unresolved-session recovery.</summary>
public interface IAuthoringBatchHost
{
    AuthoringBatchHostPreview CaptureSetText(string text);
    CardPackV2CommitResult CommitSetText(AuthoringBatchHostPreview preview);
    CardPackV2RecoveryResult Recover();
}
