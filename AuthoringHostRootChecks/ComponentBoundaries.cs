// Root orchestration checks: the production Root and Core Workflow run unchanged.
// Previously verified child components are explicit lifecycle boundary doubles;
// this suite does not claim another real View/Selection/native integration run.
using System.Text.Json;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;
using UnityEngine;
namespace Azurite;

internal sealed record AuthoringCardPackResourceChoice(string ResourceKey,string ResourceId,CardResourceKind Kind);
internal sealed class AuthoringCardPackHostResourcePicker
{
    readonly Func<bool> allowed;
    internal AuthoringCardPackHostResourcePicker(int thread,Func<bool> gate,string key,CardResourceKind kind){allowed=gate;}
    internal AuthoringCardPackResourceChoice? ReadChoice(){if(!allowed())throw new InvalidOperationException("picker gate closed");return null;}
}
internal sealed class AuthoringCardPackHostDraft
{
    internal AuthoringCardPackHostDraft(int thread,Func<bool> gate,Func<IReadOnlyList<AuthoringCardPackResourceChoice>> choices,string transition,string sound){}
}
internal sealed class AuthoringCardPackHostSelection:IDisposable
{
    internal static List<AuthoringCardPackHostSelection> Instances=new();internal static bool FailInstall;
    readonly Func<bool> allowed;internal int Installs,Disposes,Draws,Updates;internal bool Enabled;
    internal bool IsInstalled{get;private set;}internal long SelectionRevision=>IsInstalled?11:-1;
    internal long PresentationRevision=>SelectionRevision;
    internal void SetOwnedInputGuard(Func<GameObject?,bool> guard){}
    internal AuthoringCardPackHostSelection(int thread,Func<bool> gate,Action<string>? log=null){allowed=gate;Instances.Add(this);}
    internal bool Install(){Installs++;IsInstalled=!FailInstall&&allowed();return IsInstalled;}
    internal void Update(bool enabled){Updates++;Enabled=enabled&&allowed();}
    internal void OnGUI(){if(Enabled&&allowed())Draws++;}
    internal void InvalidateNativeResult(string id,IReadOnlyList<string> lines){if(!IsInstalled)throw new InvalidOperationException("selection not installed");}
    internal bool TryGetSelectedRows(out ScriptNodeInspector inspector,out IReadOnlyList<ScriptListItem> rows){inspector=ScriptNodeInspector.instance!;rows=inspector.scriptNodeListItemsCache;return IsInstalled;}
    public void Dispose(){Disposes++;IsInstalled=false;Enabled=false;}
}
internal sealed class AuthoringCardPackHostView:IDisposable
{
    internal static List<AuthoringCardPackHostView> Instances=new();internal static bool FailConstruction;
    internal readonly CardPackV2Workflow Workflow;internal readonly Transform Parent;internal readonly UILabel Typography;internal readonly Vector2 Origin;internal readonly Func<bool> Context;
    internal bool Enabled,Disposed,FailUpdate,FailDispose;internal int Disposes;internal string FormText="example";
    internal AuthoringCardPackHostView(int thread,Transform parent,UILabel typography,Vector2 origin,Func<bool> context,
        CardPackV2Workflow workflow,AuthoringCardPackHostResourcePicker transition,AuthoringCardPackHostResourcePicker sound,AuthoringCardPackHostDraft draft)
    {if(FailConstruction)throw new InvalidOperationException("fixture view construction failure");Workflow=workflow;Parent=parent;Typography=typography;Origin=origin;Context=context;Instances.Add(this);}
    internal void InvalidatePreview(){Workflow.Cancel();}
    internal void Update(bool enabled){if(Disposed||FailUpdate)throw new InvalidOperationException("view parent is gone");Enabled=enabled;if(!enabled)Workflow.Cancel();}
    public void Dispose(){if(FailDispose)throw new InvalidOperationException("fixture owned cleanup failure");if(Disposed)return;Disposes++;Disposed=true;Enabled=false;Workflow.Cancel();}
}
internal static class AuthoringCardPackHostComposition
{
    internal static ICardPackV2Host Create(int thread,Func<bool> gate,Func<long> selection,
        Func<IReadOnlyList<AuthoringCardPackResourceChoice>> choices,Action<string,IReadOnlyList<string>> invalidate,
        Func<IReadOnlyList<ScriptListItem>>? readBatchRows=null)
        =>new RootBoundaryHost(gate,selection){ReadBatchRows=readBatchRows};
}
internal sealed class RootBoundaryHost:BatchBoundaryHost,ICardPackV2Host
{
    internal static List<RootBoundaryHost> Instances=new();internal readonly Func<bool> Gate;readonly Func<long> selection;
    internal Func<IReadOnlyList<ScriptListItem>>? ReadBatchRows;
    internal int Captures,Commits,Recovers;
    internal RootBoundaryHost(Func<bool> gate,Func<long> selection){Gate=gate;this.selection=selection;Allowed=gate;Instances.Add(this);}
    public CardPackV2Snapshot Capture(string? locale)
    {
        if(!Gate())throw new InvalidOperationException("fresh root gate closed");Captures++;
        if(Faulted)throw new CardPackRecoveryRequiredException("fixture session uncertainty");
        var session=AuthoringEditorSession.Current!;var node=ScriptNodeInspector.instance!.scriptNode;
        var identity=new CardPackContextIdentity(session.ProjectId,session.EditSession,session.Revision,"catalog",node.guid.ToString(),"anchor",2);
        var binding=new CardResourceBinding("transition","fixture-transition",CardResourceKind.Transition,true,false,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>());
        return new(new(identity,JsonDocument.Parse("[]").RootElement.Clone(),true,new[]{binding},CardNativeCapability.LineInsert|CardNativeCapability.SceneTransition,locale),selection());
    }
    public CardPackV2CommitResult Commit(CardPackNativePlan plan,long revision){if(!Gate())throw new InvalidOperationException("gate closed");Commits++;return new(CardPackV2CommitDisposition.Applied,"fixture");}
    public override CardPackV2RecoveryResult Recover(){if(!Gate())throw new InvalidOperationException("wrong or blocked session");Recovers++;Faulted=false;return new(true,"fixture recovered");}
}

internal sealed class AuthoringCardPackHostBatchToolbar:IDisposable
{
    internal static List<AuthoringCardPackHostBatchToolbar> Instances=new();
    internal readonly AuthoringBatchWorkflow Workflow;internal bool Enabled,Disposed;
    internal AuthoringCardPackHostBatchToolbar(int thread,Transform parent,UILabel font,Vector2 origin,Func<bool> gate,AuthoringBatchWorkflow workflow,AuthoringCardPackHostResourcePicker picker){Workflow=workflow;Instances.Add(this);}
    internal void Update(bool enabled){Enabled=enabled;if(!enabled)Workflow.Cancel();}
    internal void InvalidatePreview(){Workflow.Cancel();}
    public void Dispose(){Disposed=true;Enabled=false;Workflow.Cancel();}
}
