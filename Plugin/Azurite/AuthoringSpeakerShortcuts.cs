using System;
using Studio.Scripts;
using Studio.Scripts.Nodes;
using Studio.Scripts.OperationManagement;
using UnityEngine;

namespace Azurite;

/// <summary>Alt+1..5 changes one selected script row through AA's undoable operation.</summary>
internal sealed class AuthoringSpeakerShortcuts : IDisposable
{
    private static readonly KeyCode[] SlotKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5
    };

    private readonly Action<string>? _log;
    private readonly Func<bool> _authoringAllowed;
    private Func<int>? _selectedCount;
    private Func<int, bool>? _batchApplySpeaker;
    private bool _disposed;
    private bool _blockedAfterMutationFailure;

    private sealed class PartialMutationException : Exception
    {
        internal PartialMutationException(string message, Exception inner) : base(message, inner) { }
    }

    internal AuthoringSpeakerShortcuts(Action<string>? log, Func<bool> authoringAllowed)
    {
        _log = log;
        _authoringAllowed = authoringAllowed ?? throw new ArgumentNullException(nameof(authoringAllowed));
    }

    internal static bool CanUpdate(HostProfile? profile, bool pluginEnabled, bool optionEnabled,
        bool hostSupported, bool probeHealthy, bool exportActive, bool exportSuspended,
        bool progressiveBusy, bool mutationProtected, bool focused) =>
        pluginEnabled && optionEnabled && profile?.SingleSpeakerAuthoring == true && hostSupported &&
        probeHealthy && !exportActive && !exportSuspended && !progressiveBusy &&
        !mutationProtected && focused;

    // A future certified selection adapter may supply a batch handler. Until
    // then, a count above one with no handler rejects the shortcut.
    internal void ConfigureBatch(Func<int> selectedCount, Func<int, bool>? applySpeaker)
    {
        _selectedCount = selectedCount ?? throw new ArgumentNullException(nameof(selectedCount));
        _batchApplySpeaker = applySpeaker;
    }

    internal void Update(bool editorAllowed)
    {
        if (_disposed || _blockedAfterMutationFailure || !editorAllowed || UICamera.isDragging || UIInput.current != null || UIInput.selection != null)
            return;
        if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;

        for (int index = 0; index < SlotKeys.Length; index++)
        {
            if (!Input.GetKeyDown(SlotKeys[index])) continue;
            int slot = index + 1;
            try
            {
                if (!_authoringAllowed()) return;
                if (!ApplySpeaker(slot))
                    _log?.Invoke($"speaker shortcut Alt+{slot} ignored: no eligible selected script row.");
            }
            catch (PartialMutationException error)
            {
                _log?.Invoke($"speaker shortcut Alt+{slot} {error.Message}: {error.InnerException?.GetType().Name}: {error.InnerException?.Message}");
            }
            catch (Exception error)
            {
                _log?.Invoke($"speaker shortcut Alt+{slot} failed closed: {error.GetType().Name}: {error.Message}");
            }
            return;
        }
    }

    private bool ApplySpeaker(int slot)
    {
        if (_blockedAfterMutationFailure || slot < 1 || slot > 5) return false;
        ScriptNodeInspector? inspector = ScriptNodeInspector.instance;
        if (inspector == null || !inspector.isActiveAndEnabled || inspector.loading || inspector.unloading)
            return false;
        int selectedCount = _selectedCount?.Invoke() ?? 1;
        if (selectedCount != 1)
            return selectedCount > 1 && (_batchApplySpeaker?.Invoke(slot) ?? false);

        ScriptListItem? selected = inspector.selectedScriptItem;
        ScriptNode? node = inspector.scriptNode;
        if (selected == null || node?.scripts == null || !selected.selected || !selected.isActiveAndEnabled ||
            !selected.gameObject.activeInHierarchy || selected.inspector?.Pointer != inspector.Pointer ||
            selected.scriptNode?.Pointer != node.Pointer)
            return false;

        int index = selected.index;
        var cache = inspector.scriptNodeListItemsCache;
        if (index < 0 || index >= node.scripts.Count || cache == null || index >= cache.Count ||
            cache[index]?.Pointer != selected.Pointer)
            return false;

        Script? script = node.scripts[index];
        if (script == null || script.Pointer == IntPtr.Zero || script.speakerSlotNum == slot) return false;

        ScriptData before = new ScriptData(script);
        ScriptData after = new ScriptData(script) { speakerSlotNum = slot };
        var operation = new ScenarioScriptInspectorModifyOperation(node, script, before, after);
        try { operation.Redo(); }
        catch (Exception error)
        {
            _blockedAfterMutationFailure = true;
            bool changed = index < node.scripts.Count && node.scripts[index]?.speakerSlotNum == slot;
            if (changed)
                throw new PartialMutationException("changed the model before native Redo failed; undo history is absent and shortcuts are disabled for this session", error);
            throw new InvalidOperationException("native Redo failed; model state is uncertain and shortcuts are disabled for this session", error);
        }
        if (index >= node.scripts.Count || node.scripts[index]?.speakerSlotNum != slot)
        {
            _blockedAfterMutationFailure = true;
            throw new InvalidOperationException("Native speaker operation did not update the selected row.");
        }
        try { OperationManager.WriteHistory(operation, linkedWithPrev: false, linkedWithNext: false); }
        catch (Exception error)
        {
            _blockedAfterMutationFailure = true;
            throw new PartialMutationException("changed the model but history registration failed; undo state is unknown and shortcuts are disabled for this session", error);
        }
        inspector.SyncScriptName();
        inspector.SyncSlots();
        return true;
    }

    public void Dispose() => _disposed = true;
}
