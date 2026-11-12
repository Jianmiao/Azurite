namespace Azurite.NormalEditor.Entry.Evidence;

// Values must be collected by the eventual admitted integration; this assembly
// neither discovers a process nor attests to the physical origin of these inputs.
public sealed record CatalogRunIdentity(CatalogBinding Binding, DateTimeOffset ProcessStartUtc, DateTimeOffset ObserverStartedUtc);
public sealed record CatalogEvidenceOrigin(string RunOutputDirectory, string OriginalEvidencePath);
public sealed record CatalogEvidenceLimits(int MaxLineBytes = 8192, int MaxFileBytes = 262144, int MaxRecords = 64);
public sealed record CatalogJournalSeal(long FileBytes, string Sha256, int RecordCount);
public sealed record ExternalCatalogCloseProof(
    CatalogRunIdentity Identity, CatalogEvidenceOrigin Origin, long CatalogOwner,
    int MonitorProcessId, DateTimeOffset MonitorProcessStartUtc,
    DateTimeOffset CloseRequestedUtc, DateTimeOffset ExitObservedUtc, DateTimeOffset EvidenceObservedUtc,
    int CloseTimeoutMilliseconds, int ExitCode,
    bool NormalCloseRequestSucceeded, bool TimedOut, bool ForceTerminated, bool ObserverFailureObserved,
    long EvidenceBytes, string EvidenceSha256);
public sealed record CatalogEvidenceAssessment(bool EligibleForCatalogOnlyEvidence, bool ReadyRecordSeen, string Reason);
