#if AZURITE_AUTHORING_SOURCE
using System;
using System.Threading;
using Azurite.Core;
using BepInEx.Configuration;

namespace Azurite;

// This partial is absent from Original builds. Its opt-in does not enable any
// rendering/performance patch, and a known ABI is not runtime acceptance.
public sealed partial class Plugin
{
    private ConfigEntry<bool>? _cardPacksEnabled;
    private AuthoringCardPackHostRoot? _cardPackRoot;
    private int _cardEditorThread, _cardExportHold, _cardStopping, _cardTerminal, _cardFaulted;
    private int _cardRenderingTransition;
    private bool _cardRootSuspended, _cardSafeTick;

    private void BindCardPackAuthoring()
    {
        _cardPacksEnabled = base.Config.Bind("Authoring", "CardPacks", false,
            "Experimental source-built card-pack authoring on the verified AA 1.0.1 host. Requires explicit opt-in; native acceptance remains pending. Does not enable performance experiments.");
    }

    private void BindCardPackDriver(AzuriteDriver driver) => driver.GuiTick = DrawCardPackSelection;

    // Called before any rendering early return, and only by the real Driver Tick.
    // The Load thread is deliberately not used as an editor-thread identity.
    private bool TickCardPackAuthoring()
    {
        int thread = Environment.CurrentManagedThreadId;
        Interlocked.CompareExchange(ref _cardEditorThread, thread, 0);
        if (thread != Volatile.Read(ref _cardEditorThread))
        {
            CardPackFault(new InvalidOperationException("Driver Tick changed editor thread; authoring owner retained."));
            return false;
        }
        if (_cardSafeTick) return false;
        _cardSafeTick = true;
        try
        {
            if (Volatile.Read(ref _cardStopping) != 0)
            {
                if (Volatile.Read(ref _cardTerminal) != 0) return false;
                TryUnloadCardPackAuthoring();
                // Unload has not succeeded yet. Preserve the normal rendering
                // Tick/LateTick pair and its lease restoration until the caller
                // retries Unload; only authoring is stopped during this wait.
                return true;
            }
            if (_cardPackRoot == null && CardPackOwnershipAllowed())
                _cardPackRoot = new AuthoringCardPackHostRoot(thread, CardPackOwnershipAllowed,
                    CardPackContextAllowed, message => base.Log.LogWarning(message));
            if (_cardPackRoot != null)
            {
                SynchronizeCardPackExport();
                _cardPackRoot.Update(CardPackOwnershipAllowed());
            }
        }
        catch (Exception error) { CardPackFault(error); }
        finally { _cardSafeTick = false; }
        return true;
    }

    private bool CardPackLocalAllowed() =>
        _cardEditorThread != 0 && Environment.CurrentManagedThreadId == Volatile.Read(ref _cardEditorThread) &&
        Volatile.Read(ref _cardStopping) == 0 && Volatile.Read(ref _cardFaulted) == 0 &&
        Volatile.Read(ref _cardExportHold) == 0 && Volatile.Read(ref _cardRenderingTransition) == 0 && !_exportSuspended &&
        _cardPacksEnabled?.Value == true;

    private bool IsReleasedCardPackIntervalConflict() => _intervalLease != null &&
        _intervalLease.IsBlocked && !_intervalLease.IsOwned && _intervalLease.Failure == RenderIntervalFailure.OwnershipConflict;

    private bool ReleaseCardPackPerformanceControllers(bool disposeScroll)
    {
        // Capture the real result before dropping the reference. Neither disposed
        // objects nor a later null field are evidence that cleanup succeeded.
        bool fpsRestored = _fps?.TryDispose() ?? true;
        _fps = null;
        bool scrollRestored = disposeScroll ? _scroll?.TryDispose() ?? true : _scroll?.TryRestore() ?? true;
        if (disposeScroll) _scroll = null;
        return fpsRestored && scrollRestored;
    }

    // Rendering-only exit is an explicit call-site decision. Every cleanup runs
    // under a temporary hold; an unclassified failure remains an authoring fault.
    private bool RunCardPackRenderingCleanup(Func<bool> cleanup, string reason, bool renderingOnly)
    {
        Interlocked.Increment(ref _cardRenderingTransition);
        try
        {
            if (!renderingOnly) CardPackFault(new InvalidOperationException("Session safety stop: " + reason));
            bool restored = cleanup();
            if (!restored) CardPackFault(new InvalidOperationException("Rendering cleanup is unverified: " + reason));
            return restored;
        }
        catch (Exception error) { CardPackFault(error); throw; }
        finally { Interlocked.Decrement(ref _cardRenderingTransition); }
    }

    private bool CardPackOwnershipAllowed()
    {
        try
        {
            if (!CardPackLocalAllowed() || _host == null || !_host.Profile.CardPackAuthoring) return false;
            HostCompatibilitySnapshot state = _host.Probe();
            return state.Supported && state.ProbeHealthy && !state.ExportActive &&
                !_host.PublicRenderingBlocked && _host.Profile.CardPackAuthoring && CardPackLocalAllowed();
        }
        catch { return false; }
    }

    private bool CardPackContextAllowed()
    {
        try
        {
            return CardPackOwnershipAllowed() && _activity != null &&
                _progressiveLoading?.BlocksEditorOperations != true &&
                (_progressiveLoading?.ResourceDependentWorkAllowed ?? true) &&
                !(_mutation?.IsBlocking(Now, Math.Clamp(_mutationSettleSeconds.Value, 0.1, 5.0)) ?? false) &&
                _activity.CanUseAuthoringContext() && CardPackLocalAllowed();
        }
        catch { return false; }
    }

    private void DrawCardPackSelection()
    {
        if (_cardPackRoot == null || !CardPackOwnershipAllowed()) return;
        try { _cardPackRoot.DrawSelection(); }
        catch (Exception error) { CardPackFault(error); }
    }

    private void SuspendCardPackAuthoring()
    {
        Interlocked.Exchange(ref _cardExportHold, 1);
        SynchronizeCardPackExport();
    }

    private void ResumeCardPackAuthoring()
    {
        // Only AfterRelease clears this latch; ordinary rendering Tick resets
        // _exportSuspended for older paths and must not reopen authoring.
        Interlocked.Exchange(ref _cardExportHold, 0);
        SynchronizeCardPackExport();
    }

    private void SynchronizeCardPackExport()
    {
        if (_cardPackRoot == null || _cardPackRoot.IsDisposed ||
            Environment.CurrentManagedThreadId != Volatile.Read(ref _cardEditorThread)) return;
        try
        {
            bool held = Volatile.Read(ref _cardExportHold) != 0;
            if (held == _cardRootSuspended) return;
            if (held) _cardPackRoot.SuspendForExport();
            else _cardPackRoot.ResumeAfterExport();
            _cardRootSuspended = held;
        }
        catch (Exception error) { CardPackFault(error); }
    }

    private bool TryUnloadCardPackAuthoring()
    {
        Interlocked.Exchange(ref _cardStopping, 1);
        if (_cardEditorThread != 0 && Environment.CurrentManagedThreadId != Volatile.Read(ref _cardEditorThread))
            return false;
        if (_cardPackRoot == null) return true;
        // A public Unload can be called inside a UI callback. Queue disposal to
        // the next real Tick; keep that callback and the original owner alive.
        if (!_cardSafeTick) return false;
        try
        {
            _cardPackRoot.DisposeAtSafePoint();
            if (!_cardPackRoot.IsDisposed) return false;
            _cardPackRoot = null;
            return true;
        }
        catch (Exception error) { CardPackFault(error); return false; }
    }

    private void ShutdownCardPackAuthoring()
    {
        Interlocked.Exchange(ref _cardTerminal, 1);
        Interlocked.Exchange(ref _cardStopping, 1);
        if (_cardPackRoot == null) return;
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _cardEditorThread))
        {
            CardPackFault(new InvalidOperationException("Terminal callback is not on the editor thread; authoring cleanup incomplete and owner retained."));
            return;
        }
        try
        {
            // Confirmed Unity terminal callback: no further Tick is promised.
            // Root only releases retained owned state, never reacquires Editor.
            _cardPackRoot.AbandonForShutdown();
            if (_cardPackRoot.IsDisposed) _cardPackRoot = null;
        }
        catch (Exception error) { CardPackFault(error); }
    }

    private void CardPackFault(Exception error)
    {
        if (Interlocked.Exchange(ref _cardFaulted, 1) == 0)
            base.Log.LogWarning("Card-pack authoring stopped; retained owner/recovery state is not cleared: " + error);
    }
}
#endif
