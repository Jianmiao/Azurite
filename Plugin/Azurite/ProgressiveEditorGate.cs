using System;

namespace Azurite;

// The native project preload and the editor operation gate are separate
// lifetimes.  Once StudioCommon has created the editor shell, structure and
// dialogue data are safe to edit while assets continue to arrive.  Preview,
// character and background work remains resource-gated until the preload has
// settled.  Export owns the rendering boundary and temporarily blocks edits
// without cancelling the preload coroutine.
internal enum ProgressiveEditorPhase
{
	Idle,
	Loading,
	EditorReady,
	Ready,
	ExportBlocked,
	Failed,
	Cancelled,
	Closed
}

internal sealed class ProgressiveEditorGate
{
	private bool _resourcesCompleted;
	private bool _editorShown;
	public ProgressiveEditorPhase Phase { get; private set; }
	public bool IsEditorVisible => _editorShown && (Phase is ProgressiveEditorPhase.EditorReady or ProgressiveEditorPhase.Ready or ProgressiveEditorPhase.ExportBlocked or ProgressiveEditorPhase.Failed);
	public bool AllowsStructureEditing => _editorShown && (Phase is ProgressiveEditorPhase.EditorReady or ProgressiveEditorPhase.Ready or ProgressiveEditorPhase.Failed);
	public bool AllowsResourceDependentWork => Phase == ProgressiveEditorPhase.Ready;
	public bool BlocksEditorOperations => !AllowsStructureEditing;
	public bool ContinuePreload => Phase is ProgressiveEditorPhase.Loading or ProgressiveEditorPhase.EditorReady or ProgressiveEditorPhase.ExportBlocked;
	public bool ExportOwned => Phase == ProgressiveEditorPhase.ExportBlocked;
	public bool ResourcesCompleted => _resourcesCompleted || Phase == ProgressiveEditorPhase.Ready;

	public ProgressiveEditorGate() => Phase = ProgressiveEditorPhase.Idle;

	public bool Begin()
	{
		if (Phase != ProgressiveEditorPhase.Idle) return false;
		_resourcesCompleted = false;
		_editorShown = false;
		Phase = ProgressiveEditorPhase.Loading;
		return true;
	}

	public bool EditorShown()
	{
		if (Phase != ProgressiveEditorPhase.Loading) return false;
		_editorShown = true;
		Phase = ProgressiveEditorPhase.EditorReady;
		return true;
	}

	public bool MarkResourcesReady()
	{
		if (Phase == ProgressiveEditorPhase.EditorReady)
		{
			_resourcesCompleted = true;
			Phase = ProgressiveEditorPhase.Ready;
			return true;
		}
		if (Phase == ProgressiveEditorPhase.ExportBlocked)
		{
			_resourcesCompleted = true;
			return true;
		}
		return ResourcesCompleted;
	}

	public bool AcquireExport()
	{
		if (Phase is not (ProgressiveEditorPhase.Loading or ProgressiveEditorPhase.EditorReady or ProgressiveEditorPhase.Ready)) return false;
		Phase = ProgressiveEditorPhase.ExportBlocked;
		return true;
	}

	public bool ReleaseExport(bool resourcesReady)
	{
		if (Phase != ProgressiveEditorPhase.ExportBlocked) return false;
		Phase = resourcesReady || _resourcesCompleted ? ProgressiveEditorPhase.Ready : ProgressiveEditorPhase.EditorReady;
		return true;
	}

	public bool Fail()
	{
		if (Phase is ProgressiveEditorPhase.Closed or ProgressiveEditorPhase.Cancelled or ProgressiveEditorPhase.Failed) return false;
		Phase = ProgressiveEditorPhase.Failed;
		return true;
	}

	public bool Cancel()
	{
		if (Phase is ProgressiveEditorPhase.Closed or ProgressiveEditorPhase.Cancelled) return false;
		Phase = ProgressiveEditorPhase.Cancelled;
		return true;
	}

	public bool Close()
	{
		if (Phase == ProgressiveEditorPhase.Closed) return false;
		Phase = ProgressiveEditorPhase.Closed;
		return true;
	}
}
