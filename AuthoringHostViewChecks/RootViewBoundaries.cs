using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;

namespace Azurite;

// The actual Root, both native views and both workflows are source-linked.
// The actual selection adapter is also linked. Only native API and Host
// construction are boundary doubles; the existing Bridge suite tests real Host.
internal static class AuthoringCardPackHostComposition
{
    internal static readonly List<RootViewHost> Hosts = new();
    internal static ICardPackV2Host Create(int thread, Func<bool> gate, Func<long> revision,
        Func<IReadOnlyList<AuthoringCardPackResourceChoice>> choices, Action<string,IReadOnlyList<string>> invalidate,
        Func<IReadOnlyList<ScriptListItem>>? readBatchRows = null)
    {
        var host = new RootViewHost(thread,gate,revision,choices,readBatchRows!); Hosts.Add(host); return host;
    }
}
internal sealed class RootViewHost : BatchBoundaryHost, ICardPackV2Host
{
    private readonly int _thread;
    private readonly Func<long> _revision;
    private readonly Func<IReadOnlyList<AuthoringCardPackResourceChoice>> _choices;
    internal readonly Func<IReadOnlyList<ScriptListItem>> Rows;
    internal int CardCaptures, CardCommits;
    internal RootViewHost(int thread, Func<bool> gate, Func<long> revision, Func<IReadOnlyList<AuthoringCardPackResourceChoice>> choices, Func<IReadOnlyList<ScriptListItem>> rows)
    {
        _thread=thread;Allowed=gate;_revision=revision;_choices=choices;Rows=rows;
        Selection=()=>new(_revision(),ScriptNodeInspector.instance!.scriptNode.guid.ToString(), Rows().Select(row=>new AuthoringBatchRow(
            row.scriptNode.scripts[row.index].LineId,row.index,"original "+row.index,2)).ToArray());
    }
    public CardPackV2Snapshot Capture(string? locale)
    {
        if(!Allowed())throw new InvalidOperationException("actual Root gate closed");
        if(Faulted)throw new CardPackRecoveryRequiredException("shared fault");
        CardCaptures++;var s=AuthoringEditorSession.Current!;var i=ScriptNodeInspector.instance!;
        var identity=new CardPackContextIdentity(s.ProjectId,s.EditSession,s.Revision,AuthoringResourceCatalog.CatalogVersion,i.scriptNode.guid.ToString(),"anchor",i.selectedSlotNum);
        return new(new AuthoringCardPackHostContent(_thread,_choices).Capture(identity,locale),_revision(),Epoch);
    }
    public CardPackV2CommitResult Commit(CardPackNativePlan plan,long revision)
    {
        if(!Allowed())throw new InvalidOperationException("actual Root gate closed");CardCommits++;Epoch++;
        return new(CardPackV2CommitDisposition.Applied,"fixture card applied");
    }
    public override CardPackV2RecoveryResult Recover()
    {
        if(!Allowed())return new(false,"actual Root gate closed");return base.Recover();
    }
    protected override CardPackV2CommitResult? ValidateCommit(object value)
    {
        // Exercise the actual strong selection delegate supplied by Root, as the
        // real Host does before mutation. PresentationRevision is never used here.
        try
        {
            var expected=value switch
            {
                AuthoringBatchHostPreview p=>p.Plan.Selection,AuthoringBatchSpeakerHostPreview p=>p.Plan.Selection,
                AuthoringBatchDeleteHostPreview p=>p.Plan.Selection,AuthoringBatchMultilineHostPreview p=>p.Plan.Selection,
                AuthoringBatchDuplicateHostPreview p=>p.Plan.Selection,_=>throw new InvalidOperationException("unknown preview")
            };
            if(!AuthoringBatchPlanner.Matches(expected,Selection()))return new(CardPackV2CommitDisposition.RejectedBeforeMutation,"Strong selection changed before commit.");
            return null;
        }
        catch(Exception e){return new(CardPackV2CommitDisposition.RejectedBeforeMutation,"Strong selection invalid: "+e.Message);}
    }
}
