using System;

namespace Azurite.Core;

/// <summary>UI-only coordination. Never grants native eligibility or performs recovery.</summary>
public sealed class AuthoringClientSession
{
    private object? _active;
    public bool RecoveryRequired { get; private set; }
    public event Action<object>? Starting;
    public event Action<object, CardPackV2WorkflowResult>? Completed;
    public event Action? RecoveryChanged;
    public void ObserveRecoveryRequired()
    {
        if (RecoveryRequired) return;
        RecoveryRequired = true;
        RecoveryChanged?.Invoke();
    }
    internal bool Begin(object client)
    {
        if (_active != null) return false;
        _active = client;
        try { Starting?.Invoke(client); return true; }
        catch { _active = null; throw; }
    }
    internal void End(object client, CardPackV2WorkflowResult result)
    {
        _active = null;
        Completed?.Invoke(client, result);
    }
    // Called only after the existing Host returns Recovered=true.
    internal void ConfirmRecovery()
    {
        RecoveryRequired = false;
        RecoveryChanged?.Invoke();
    }
}

