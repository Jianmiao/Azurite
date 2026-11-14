using Azurite;
using Azurite.Core;
using AzureArchive.Automation;

static partial class Program
{
    static void UiStatus()
    {
        Test("readonly UI status does not enter Capture Check Apply Resolve or Clone", () =>
        {
            var host=Host();var session=AuthoringEditorSession.Current!;var snapshot=session.Snapshot;
            ProjectFileStore.CloneOverride=_=>throw new Exception("readonly status cloned project");
            AuthoringResourceCatalog.AfterResolve=()=>throw new Exception("readonly status resolved resource");
            allowed=false;
            for(int i=0;i<20;i++)
            {
                var status=host.ReadPresentationStatus(session.EditSession);
                Check(!status.RecoveryRequired&&!status.Busy,"idle status wrong");
            }
            Check(session.Checks==0&&session.Applies==0&&ReferenceEquals(snapshot,session.Snapshot),"presentation status entered native boundary");
        });
        Test("speaker evidence is captured exact-reference resource data and revokes", () =>
        {
            PrepareSpeakerRows();var host=Host();var preview=host.CaptureSpeaker(3);int resolves=AuthoringResourceCatalog.Resolved.Count;
            var json=host.ReadSpeakerEvidence(preview);
            Check(json.Contains("fixture:actor-3")&&AuthoringResourceCatalog.Resolved.Count==resolves,"speaker evidence missing or re-resolved");
            Reject(()=>host.ReadSpeakerEvidence(preview with {}),"copied foreign preview accepted");
            Reject(()=>Host().ReadSpeakerEvidence(preview),"different owner preview accepted");
            host.CaptureSetText("new");Reject(()=>host.ReadSpeakerEvidence(preview),"replaced capture retained speaker evidence");
        });
        Test("readonly session status reflects shared epoch fault and actual recovery", () =>
        {
            var host=Host();var plan=Plan(host);var session=AuthoringEditorSession.Current!;var before=host.ReadPresentationStatus(session.EditSession);
            session.ThrowBeforeApply=true;Check(host.Commit(plan,7).Disposition==CardPackV2CommitDisposition.OutcomeUnknown,"fixture did not create uncertainty");
            var other=Host();var fault=other.ReadPresentationStatus(session.EditSession);
            Check(fault.RecoveryRequired&&fault.SubmissionVersion>before.SubmissionVersion&&!fault.Busy,"shared status did not reflect actual Host fault");
            Check(other.Recover().Recovered&&!host.ReadPresentationStatus(session.EditSession).RecoveryRequired,"verified recovery did not clear Host status");
        });
        Test("readonly UI seams require the actual owner thread", () =>
        {
            var host=Host();var key=AuthoringEditorSession.Current!.EditSession;
            System.Threading.Tasks.Task.Run(()=>Reject(()=>host.ReadPresentationStatus(key),"wrong thread queried static host state")).GetAwaiter().GetResult();
        });
    }
}
