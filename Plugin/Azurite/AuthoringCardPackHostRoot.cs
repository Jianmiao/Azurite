using System;
using System.Collections.Generic;
using Azurite.Core;
using AzureArchive.Automation;
using Il2CppInterop.Runtime;
using Studio.Scripts;
using UnityEngine;

namespace Azurite;

/// <summary>
/// Unregistered AuthoringSource owner. The caller records the first real Driver
/// Tick thread and supplies fresh host/profile/export and editor-context gates.
/// Opening a view never begins a native session or infers host qualification.
/// </summary>
internal sealed class AuthoringCardPackHostRoot
{
    private const float ViewWidth = 760, ViewHeight = 940, MinimumScale = .65f;
    private sealed record SessionKey(string ProjectId, string EditSession);
    private sealed record Scope(SessionKey Key, IntPtr SessionPointer, ScriptNodeInspector Inspector);
    private sealed record Surface(IntPtr Inspector, UIPanel Panel, UILabel Font, float Left, float Right, float Top, float Bottom, float Scale)
    {
        internal bool SameOwner(Surface other) => Inspector == other.Inspector && Panel.Pointer == other.Panel.Pointer && Font.Pointer == other.Font.Pointer;
    }
    private sealed record PresentationStamp(string Revision, string Catalog, string Node, IntPtr Anchor, int Slot, long Selection, long Epoch, bool Context);
    private sealed class Bundle
    {
        internal SessionKey Key = null!;
        internal AuthoringCardPackHostResourcePicker Transition = null!, Sound = null!;
        internal AuthoringCardPackHostDraft Draft = null!;
        internal ICardPackV2Host Host = null!;
        internal CardPackV2Workflow Workflow = null!;
        internal AuthoringBatchWorkflow Batch = null!;
        internal AuthoringClientSession Clients = null!;
        internal IAuthoringHostPresentation Presentation = null!;
        internal AuthoringCardPackHostResourcePicker Offstage = null!;
        internal PresentationStamp? Stamp;
    }

    private readonly int _editorThreadId;
    private readonly Func<bool> _hostOwnershipAllowed, _freshContextAllowed;
    private readonly Action<string> _log;
    private readonly Dictionary<SessionKey, Bundle> _sessions = new();
    private Bundle? _active;
    private IntPtr _activeSessionPointer;
    private Surface? _surface;
    private AuthoringCardPackHostSelection? _selection;
    private AuthoringCardPackHostView? _view;
    private AuthoringCardPackHostBatchToolbar? _batchView;
    private GameObject? _batchLauncher;
    private UIButton? _batchToggle;
    private UILabel? _batchToggleLabel;
    private Action? _batchToggleManaged;
    private EventDelegate.Callback? _batchToggleNative;
    private bool _requestedBatchOpen;
    private GameObject? _launcher, _placement;
    private UIButton? _toggle;
    private UILabel? _toggleLabel;
    private Action? _toggleManaged;
    private EventDelegate.Callback? _toggleNative;
    private bool _enabled, _exportSuspended, _requestedOpen, _pendingInvalidation, _selectionFaulted, _updating, _disposed, _surfaceReady;
    private int _callbackDepth;
    private Exception? _cleanupFailure;
    private string? _lastDiagnostic;

    internal AuthoringCardPackHostRoot(int editorThreadId, Func<bool> hostOwnershipAllowed,
        Func<bool> freshContextAllowed, Action<string> log)
    {
        if (editorThreadId <= 0 || Environment.CurrentManagedThreadId != editorThreadId)
            throw new InvalidOperationException("Authoring root must be created on the caller's verified Driver Tick thread.");
        _editorThreadId = editorThreadId;
        _hostOwnershipAllowed = hostOwnershipAllowed ?? throw new ArgumentNullException(nameof(hostOwnershipAllowed));
        _freshContextAllowed = freshContextAllowed ?? throw new ArgumentNullException(nameof(freshContextAllowed));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal bool IsDisposed => _disposed;

    internal void Update(bool enabled)
    {
        RequireThread();
        if (_disposed) return;
        if (_cleanupFailure != null) throw new InvalidOperationException("Authoring owner cleanup remains incomplete.", _cleanupFailure);
        if (_updating || _callbackDepth != 0) throw new InvalidOperationException("Authoring root Update requires a non-reentrant safe Tick.");
        _updating = true;
        try
        {
            _enabled = enabled;
            if (_pendingInvalidation)
            {
                _pendingInvalidation = false;
                HidePresentation();
            }
            if (!CanOwnHost()) { HidePresentation(); return; }

            var scope = ReadScope();
            if (scope == null)
            {
                CancelActive();
                _selection?.Update(false);
                RetireSurface(reportFormLoss: true);
                _surface = null;
                Diagnostic("卡包：当前原生编辑器/会话所有者不可用；未创建或替代会话。");
                return;
            }
            bool sessionChanged = _active?.Key != scope.Key;
            if (sessionChanged)
            {
                CancelActive();
                _selection?.Update(false);
                RetireSurface(reportFormLoss: true);
                _surface = null;
                _active = GetBundle(scope.Key);
                _requestedOpen = false; _requestedBatchOpen = false;
            }
            if (_activeSessionPointer != scope.SessionPointer)
            {
                CancelActive();
                _view?.Update(false); _batchView?.Update(false);
                _activeSessionPointer = scope.SessionPointer;
            }

            var surface = ReadSurface(scope.Inspector);
            if (surface == null)
            {
                HidePresentation();
                // Retain the last owner identity while hidden. A later valid
                // viewport may belong to a different parent and must be compared.
                _surfaceReady = false;
                Diagnostic("卡包：没有可验证的未裁剪UI父层级、字体或足够的可读视口。");
                return;
            }
            if (_surface != null && !_surface.SameOwner(surface))
            {
                CancelActive();
                RetireSurface(reportFormLoss: true);
            }
            _surface = surface;
            EnsureLauncher(surface);
            Place(surface);
            _surfaceReady = true;
            bool context = CanSharedContext();
            if (context) EnsureSelection();
            _selection?.Update(context && _selection.IsInstalled);

            ObservePresentation(context);

            bool canPresent = _selection?.IsInstalled == true && CanOwnHost();
            _launcher!.SetActive(true);
            _toggle!.isEnabled = canPresent;
            _batchLauncher!.SetActive(true); _batchToggle!.isEnabled = canPresent;
            _batchToggleLabel!.text = _active!.Batch.RecoveryRequired ? "批量·待恢复" : _requestedBatchOpen ? "关闭批量" : "批量编辑";
            _toggleLabel!.text = _active!.Workflow.RecoveryRequired ? "卡包·待恢复" : _requestedOpen ? "关闭卡包" : "卡包";
            if (_requestedOpen && canPresent && _view == null)
            {
                try
                {
                    EnsurePlacement(surface);
                    Place(surface);
                    var owner = _active!;
                    _view = new(_editorThreadId, _placement!.transform, surface.Font, new Vector2(0, 0),
                        () => CanContextFor(owner.Key), owner.Workflow, owner.Transition, owner.Sound, owner.Draft);
                }
                catch
                {
                    RetireSurface(reportFormLoss: true);
                    _requestedOpen = false; _requestedBatchOpen = false;
                    throw;
                }
            }
            if (_requestedBatchOpen && canPresent && _batchView == null)
            {
                EnsurePlacement(surface);
                var owner = _active!;
                _batchView = new(_editorThreadId, _placement!.transform, surface.Font, new Vector2(0, 0),
                    () => CanContextFor(owner.Key), owner.Batch, owner.Offstage);
            }
            _view?.Update(_requestedOpen && canPresent);
            _batchView?.Update(_requestedBatchOpen && canPresent);
            if (context && !_selectionFaulted) _lastDiagnostic = null;
        }
        catch (Exception error)
        {
            CancelActive();
            try { HidePresentation(); }
            catch (Exception cleanupError)
            {
                _enabled = false;
                _cleanupFailure = new AggregateException(error, cleanupError);
                throw new InvalidOperationException("Authoring UI failure could not be cleaned up safely.", _cleanupFailure);
            }
            Diagnostic("卡包更新拒绝：" + error.GetType().Name + ": " + error.Message);
        }
        finally { _updating = false; }
    }

    internal void DrawSelection()
    {
        RequireThread();
        if (!_disposed && CanSharedContext()) _selection?.OnGUI();
    }

    internal void RequestOpen(bool open)
    {
        RequireThread();
        if (_disposed || _cleanupFailure != null) return;
        if (open && !CanOwnHost()) return;
        if (open) { _requestedBatchOpen = false; CancelActive(); }
        _requestedOpen = open;
        if (!open)
        {
            CancelActive();
            _pendingInvalidation = true;
        }
    }

    internal void RequestBatchOpen(bool open)
    {
        RequireThread();
        if (_disposed || _cleanupFailure != null || open && !CanOwnHost()) return;
        if (open) _requestedOpen = false;
        _requestedBatchOpen = open;
        CancelActive();
        if (!open) _pendingInvalidation = true;
    }

    private void CancelActive()
    {
        _active?.Workflow.Cancel(); _active?.Batch.Cancel();
        _view?.InvalidatePreview(); _batchView?.InvalidatePreview();
    }
    private void EnsurePlacement(Surface surface)
    {
        if (_placement != null) return;
        _placement = NGUITools.AddChild(surface.Panel.gameObject);
        _placement.name = "AzuriteCardPackPlacement";
        Place(surface);
    }
    private PresentationStamp ReadStamp(Bundle owner, bool context)
    {
        var session = AuthoringEditorSession.Current ?? throw new InvalidOperationException("Current session is unavailable.");
        var inspector = ScriptNodeInspector.instance ?? throw new InvalidOperationException("Inspector is unavailable.");
        var status = owner.Presentation.ReadPresentationStatus(owner.Key.EditSession);
        if (status.RecoveryRequired) owner.Clients.ObserveRecoveryRequired();
        return new(session.Revision, AuthoringResourceCatalog.CatalogVersion, inspector.scriptNode.guid.ToString(),
            inspector.selectedScriptItem?.Pointer ?? IntPtr.Zero, inspector.selectedSlotNum,
            _selection?.PresentationRevision ?? -1, status.SubmissionVersion, context && !status.Busy);
    }
    private void ObservePresentation(bool context)
    {
        var owner = _active!;
        var fresh = ReadStamp(owner, context);
        if (owner.Stamp != null && owner.Stamp != fresh) CancelActive();
        owner.Stamp = fresh;
    }

    internal void SuspendForExport()
    {
        RequireThread();
        if (_disposed) return;
        _exportSuspended = true;
        _pendingInvalidation = true;
        CancelActive();
    }

    internal void ResumeAfterExport()
    {
        RequireThread();
        if (!_disposed) _exportSuspended = false;
        // UI, selection and all pending validation reopen only on a fresh Tick.
    }

    private Bundle GetBundle(SessionKey key)
    {
        if (_sessions.TryGetValue(key, out var existing)) return existing;
        var bundle = new Bundle { Key = key };
        Func<bool> gate = () => CanContextFor(key);
        bundle.Transition = new(_editorThreadId, gate, "transition", CardResourceKind.Transition);
        bundle.Sound = new(_editorThreadId, gate, "sound", CardResourceKind.Sound);
        IReadOnlyList<AuthoringCardPackResourceChoice> Choices()
        {
            if (!gate()) throw new InvalidOperationException("The resource bundle is not the current qualified session.");
            var values = new List<AuthoringCardPackResourceChoice>();
            var transition = bundle.Transition.ReadChoice(); var sound = bundle.Sound.ReadChoice();
            if (transition != null) values.Add(transition);
            if (sound != null) values.Add(sound);
            return values.AsReadOnly();
        }
        bundle.Host = AuthoringCardPackHostComposition.Create(_editorThreadId, gate,
            () => _selection?.SelectionRevision ?? -1, Choices,
            (node, lines) =>
            {
                // Post-Apply may close write eligibility. Do not wrap this readonly
                // native-result check in gate(); retain the real installed owner.
                var current = _selection ?? throw new InvalidOperationException("Selection owner is unavailable for native-result verification.");
                current.InvalidateNativeResult(node, lines);
            },
            () =>
            {
                if (!gate()) throw new InvalidOperationException("The batch selection is not in the current qualified session.");
                var current = _selection ?? throw new InvalidOperationException("Selection owner is unavailable.");
                if (!current.TryGetSelectedRows(out var inspector, out var rows) || inspector?.Pointer != ScriptNodeInspector.instance?.Pointer)
                    throw new InvalidOperationException("Native selected rows are not synchronized with the current Inspector.");
                return rows;
            });
        bundle.Presentation = bundle.Host as IAuthoringHostPresentation ?? throw new InvalidOperationException("Shared Host lacks presentation evidence.");
        var batchHost = bundle.Host as IAuthoringBatchHost ?? throw new InvalidOperationException("Shared Host lacks batch actions.");
        bundle.Clients = new();
        bundle.Workflow = new(bundle.Host, bundle.Clients);
        bundle.Batch = new(batchHost, bundle.Clients);
        bundle.Offstage = new(_editorThreadId, gate, "offstage", CardResourceKind.Character);
        bundle.Clients.Starting += client =>
        {
            if (!ReferenceEquals(_active, bundle)) return;
            if (ReferenceEquals(client, bundle.Workflow)) _batchView?.InvalidatePreview();
            else _view?.InvalidatePreview();
        };
        bundle.Clients.RecoveryChanged += () => { if (ReferenceEquals(_active, bundle)) CancelActive(); };
        bundle.Clients.Completed += (_, result) =>
        {
            if (!ReferenceEquals(_active, bundle) || _disposed) return;
            // A successful capture may refresh the native revision itself. Record
            // that observed stamp without issuing a second Capture on the next tick.
            try
            {
                bundle.Stamp = ReadStamp(bundle, CanContextFor(bundle.Key));
                if (!bundle.Stamp.Context) CancelActive();
            }
            catch { bundle.Stamp = null; CancelActive(); }
        };
        bundle.Draft = new(_editorThreadId, gate, Choices, "transition", "sound");
        _sessions.Add(key, bundle);
        return bundle;
    }

    private bool CanOwnHost()
    {
        if (Environment.CurrentManagedThreadId != _editorThreadId || _disposed || !_enabled || _exportSuspended || _cleanupFailure != null) return false;
        try { return _hostOwnershipAllowed() && !_exportSuspended && !_disposed && _enabled; }
        catch { return false; }
    }

    private bool CanSharedContext()
    {
        if (!CanOwnHost() || _active == null || _surface == null || !_surfaceReady) return false;
        try
        {
            if (!_freshContextAllowed()) return false;
            var current = ReadScope();
            return current != null && current.Key == _active.Key && current.SessionPointer == _activeSessionPointer &&
                current.Inspector.Pointer == _surface.Inspector && !_exportSuspended && _enabled;
        }
        catch { return false; }
    }
    private bool CanContextFor(SessionKey key) => _active?.Key == key && _selection?.IsInstalled == true && CanSharedContext();

    private Scope? ReadScope()
    {
        var session = AuthoringEditorSession.Current;
        var studio = StudioCommon.instance;
        var inspector = ScriptNodeInspector.instance;
        if (session == null || session.disposed || studio == null || !studio.isActiveAndEnabled || !studio.gameObject.activeInHierarchy ||
            session.studio?.Pointer != studio.Pointer || inspector == null || !inspector.isActiveAndEnabled || !inspector.gameObject.activeInHierarchy ||
            inspector.loading || inspector.unloading || inspector.rearrangeScheduled || inspector.scriptNode == null ||
            string.IsNullOrWhiteSpace(session.ProjectId) || string.IsNullOrWhiteSpace(session.EditSession)) return null;
        return new(new(session.ProjectId, session.EditSession), session.Pointer, inspector);
    }

    private static Surface? ReadSurface(ScriptNodeInspector inspector)
    {
        UILabel? font = inspector.contentInput?.label;
        if (!UsableFont(font)) font = inspector.nicknameLabel;
        if (!UsableFont(font)) font = inspector.nodeNameInput?.label;
        if (!UsableFont(font)) return null;
        UIPanel? parent = null;
        for (Transform? node = inspector.transform; node != null; node = node.parent)
        {
            var candidate = node.gameObject.GetComponent<UIPanel>();
            if (candidate != null && candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy && candidate.clipping == UIDrawCall.Clipping.None)
                parent = candidate;
        }
        if (parent == null) return null;
        var corners = parent.worldCorners;
        if (corners == null || corners.Length != 4) return null;
        float left = float.PositiveInfinity, right = float.NegativeInfinity, top = float.NegativeInfinity, bottom = float.PositiveInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector3 point = parent.transform.InverseTransformPoint(corners[i]);
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) return null;
            left = Math.Min(left, point.x); right = Math.Max(right, point.x);
            top = Math.Max(top, point.y); bottom = Math.Min(bottom, point.y);
        }
        float scale = Math.Min(1f, Math.Min((right - left - 24f) / ViewWidth, (top - bottom - 60f) / ViewHeight));
        if (!float.IsFinite(scale) || scale < MinimumScale) return null;
        return new(inspector.Pointer, parent, font!, left, right, top, bottom, scale);
    }
    private static bool UsableFont(UILabel? font) => font != null && font.fontSize > 0 && (font.bitmapFont != null || font.trueTypeFont != null);

    private void EnsureSelection()
    {
        if (_selection?.IsInstalled == true || _selectionFaulted) return;
        var selection = new AuthoringCardPackHostSelection(_editorThreadId, CanSharedContext, Diagnostic);
        selection.SetOwnedInputGuard(OwnsAuthoringInput);
        if (!selection.Install())
        {
            selection.Dispose();
            _selectionFaulted = true;
            Diagnostic("卡包选择挂钩不可用；已停止本owner的自动安装尝试，不使用固定revision替代。");
            return;
        }
        _selection = selection;
    }

    private bool OwnsAuthoringInput(GameObject? hovered)
    {
        if (hovered == null || !hovered.activeInHierarchy) return false;
        // Exact retained objects, never names or a blanket UIButton exclusion:
        // dialogue rows can themselves be buttons. Hidden/retired views do not
        // own a pointer, and native row/drag/scroll siblings remain available.
        for (Transform? node = hovered.transform; node != null; node = node.parent)
        {
            IntPtr pointer = node.gameObject.Pointer;
            if (_placement != null && pointer == _placement.Pointer ||
                _launcher != null && pointer == _launcher.Pointer ||
                _batchLauncher != null && pointer == _batchLauncher.Pointer) return true;
        }
        return false;
    }

    private void EnsureLauncher(Surface surface)
    {
        if (_launcher != null && _batchLauncher != null) return;
        try { CreateLauncher(surface); CreateBatchLauncher(surface); }
        catch { RetireLauncher(); throw; }
    }

    private void CreateLauncher(Surface surface)
    {
        _launcher = NGUITools.AddChild(surface.Panel.gameObject);
        _launcher.name = "AzuriteCardPackLauncher";
        _launcher.transform.localScale = Vector3.one;
        var background = _launcher.AddComponent<UITexture>();
        background.mainTexture = Texture2D.whiteTexture; background.pivot = UIWidget.Pivot.TopLeft;
        background.width = 112; background.height = 32; background.depth = surface.Font.depth + 5;
        bool lightText = surface.Font.color.r + surface.Font.color.g + surface.Font.color.b > 1.5f;
        background.color = lightText ? new Color(.18f, .21f, .26f, 1) : new Color(.82f, .85f, .90f, 1);
        _toggle = _launcher.AddComponent<UIButton>(); _toggle.tweenTarget = _launcher; _toggle.defaultColor = background.color;
        var labelObject = NGUITools.AddChild(_launcher); labelObject.name = "AzuriteCardPackLauncherLabel";
        labelObject.transform.localPosition = new Vector3(6, -4, 0); labelObject.transform.localScale = Vector3.one;
        _toggleLabel = labelObject.AddComponent<UILabel>();
        if (surface.Font.bitmapFont != null) _toggleLabel.bitmapFont = surface.Font.bitmapFont; else _toggleLabel.trueTypeFont = surface.Font.trueTypeFont;
        _toggleLabel.fontSize = surface.Font.fontSize; _toggleLabel.fontStyle = surface.Font.fontStyle; _toggleLabel.spacingY = surface.Font.spacingY;
        _toggleLabel.color = surface.Font.color; _toggleLabel.depth = surface.Font.depth + 6; _toggleLabel.pivot = UIWidget.Pivot.TopLeft;
        _toggleLabel.alignment = NGUIText.Alignment.Left; _toggleLabel.width = 100; _toggleLabel.height = 24;
        _toggleLabel.maxLineCount = 1; _toggleLabel.overflowMethod = UILabel.Overflow.ClampContent; _toggleLabel.supportEncoding = false; _toggleLabel.text = "卡包";
        if (_toggle.onClick == null || _toggle.onClick.Count != 0) throw new InvalidOperationException("Launcher must be a dedicated empty native button.");
        _toggleManaged = () =>
        {
            RequireThread();
            _callbackDepth++;
            try { RequestOpen(!_requestedOpen); } finally { _callbackDepth--; }
        };
        _toggleNative = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(_toggleManaged)
            ?? throw new InvalidOperationException("Cannot bind the owned card-pack launcher.");
        EventDelegate.Add(_toggle.onClick, _toggleNative);
        NGUITools.AddWidgetCollider(_launcher);
    }

    private void CreateBatchLauncher(Surface surface)
    {
        _batchLauncher = NGUITools.AddChild(surface.Panel.gameObject);
        _batchLauncher.name = "AzuriteBatchLauncher";
        _batchLauncher.transform.localScale = Vector3.one;
        var background = _batchLauncher.AddComponent<UITexture>();
        background.mainTexture = Texture2D.whiteTexture; background.pivot = UIWidget.Pivot.TopLeft;
        background.width = 112; background.height = 32; background.depth = surface.Font.depth + 5;
        bool lightText = surface.Font.color.r + surface.Font.color.g + surface.Font.color.b > 1.5f;
        background.color = lightText ? new Color(.18f, .21f, .26f, 1) : new Color(.82f, .85f, .90f, 1);
        _batchToggle = _batchLauncher.AddComponent<UIButton>(); _batchToggle.tweenTarget = _batchLauncher; _batchToggle.defaultColor = background.color;
        var labelObject = NGUITools.AddChild(_batchLauncher); labelObject.name = "AzuriteBatchLauncherLabel";
        labelObject.transform.localPosition = new Vector3(6, -4, 0); labelObject.transform.localScale = Vector3.one;
        _batchToggleLabel = labelObject.AddComponent<UILabel>();
        if (surface.Font.bitmapFont != null) _batchToggleLabel.bitmapFont = surface.Font.bitmapFont; else _batchToggleLabel.trueTypeFont = surface.Font.trueTypeFont;
        _batchToggleLabel.fontSize = surface.Font.fontSize; _batchToggleLabel.fontStyle = surface.Font.fontStyle; _batchToggleLabel.spacingY = surface.Font.spacingY;
        _batchToggleLabel.color = surface.Font.color; _batchToggleLabel.depth = surface.Font.depth + 6; _batchToggleLabel.pivot = UIWidget.Pivot.TopLeft;
        _batchToggleLabel.alignment = NGUIText.Alignment.Left; _batchToggleLabel.width = 100; _batchToggleLabel.height = 24;
        _batchToggleLabel.maxLineCount = 1; _batchToggleLabel.overflowMethod = UILabel.Overflow.ClampContent; _batchToggleLabel.supportEncoding = false; _batchToggleLabel.text = "批量编辑";
        if (_batchToggle.onClick == null || _batchToggle.onClick.Count != 0) throw new InvalidOperationException("Launcher must be a dedicated empty native button.");
        _batchToggleManaged = () =>
        {
            RequireThread();
            _callbackDepth++;
            try { RequestBatchOpen(!_requestedBatchOpen); } finally { _callbackDepth--; }
        };
        _batchToggleNative = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(_batchToggleManaged)
            ?? throw new InvalidOperationException("Cannot bind the owned card-pack launcher.");
        EventDelegate.Add(_batchToggle.onClick, _batchToggleNative);
        NGUITools.AddWidgetCollider(_batchLauncher);
    }

    private void Place(Surface surface)
    {
        if (_launcher != null) _launcher.transform.localPosition = new Vector3(surface.Right - 124, surface.Top - 8, 0);
        if (_batchLauncher != null) _batchLauncher.transform.localPosition = new Vector3(surface.Right - 244, surface.Top - 8, 0);
        if (_placement != null)
        {
            _placement.transform.localPosition = new Vector3(surface.Left + (surface.Right - surface.Left - ViewWidth * surface.Scale) * .5f, surface.Top - 48, 0);
            _placement.transform.localScale = new Vector3(surface.Scale, surface.Scale, 1);
        }
    }

    private void HidePresentation()
    {
        CancelActive();
        _selection?.Update(false);
        try { _view?.Update(false); _batchView?.Update(false); }
        catch { RetireSurface(reportFormLoss: true); }
        if (_launcher != null) _launcher.SetActive(false);
        if (_batchLauncher != null) _batchLauncher.SetActive(false);
    }

    private void RetireSurface(bool reportFormLoss)
    {
        bool hadForm = _view != null || _batchView != null;
        _surfaceReady = false;
        if (_view != null) { _view.Dispose(); _view = null; }
        if (_batchView != null) { _batchView.Dispose(); _batchView = null; }
        if (_placement != null) UnityEngine.Object.Destroy(_placement);
        _placement = null;
        RetireLauncher();
        if (reportFormLoss && hadForm) Diagnostic("卡包父层级或会话已更换：旧表单已关闭，未提交输入未自动迁移；原会话恢复状态仍保留。");
    }

    private void RetireLauncher()
    {
        if (_batchToggle != null && _batchToggleNative != null) EventDelegate.Remove(_batchToggle.onClick, _batchToggleNative);
        _batchToggleManaged = null; _batchToggleNative = null; _batchToggle = null; _batchToggleLabel = null;
        if (_batchLauncher != null) UnityEngine.Object.Destroy(_batchLauncher);
        _batchLauncher = null;
        if (_toggle != null && _toggleNative != null) EventDelegate.Remove(_toggle.onClick, _toggleNative);
        _toggleManaged = null; _toggleNative = null; _toggle = null; _toggleLabel = null;
        if (_launcher != null) UnityEngine.Object.Destroy(_launcher);
        _launcher = null;
    }

    internal void DisposeAtSafePoint() => CompleteDisposal();
    internal void AbandonForShutdown() => CompleteDisposal();

    private void CompleteDisposal()
    {
        RequireThread();
        if (_disposed) return;
        if (_updating || _callbackDepth != 0) throw new InvalidOperationException("Root disposal requires a safe owner-thread lifecycle point after UI callbacks.");
        if (_cleanupFailure != null) throw new InvalidOperationException("Previous owned cleanup failed; release is not verified.", _cleanupFailure);
        _enabled = false; _exportSuspended = true; _requestedOpen = false; _requestedBatchOpen = false;
        foreach (var bundle in _sessions.Values) { bundle.Workflow.Cancel(); bundle.Batch.Cancel(); }
        // Terminal cleanup deliberately does not call gates, ReadScope, Update,
        // Current, resources or a host recovery method: the Editor may be gone.
        var errors = new List<Exception>();
        try { RetireSurface(reportFormLoss: false); } catch (Exception error) { errors.Add(error); }
        try { _selection?.Dispose(); _selection = null; } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0)
        {
            _cleanupFailure = new AggregateException(errors);
            throw new InvalidOperationException("Owned authoring cleanup did not complete; the root is not reported disposed.", _cleanupFailure);
        }
        _surface = null;
        _disposed = true;
        // Keep managed recovery bundles intact. No uncertainty latch is cleared.
    }

    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _editorThreadId) throw new InvalidOperationException("Authoring root requires its verified editor thread.");
    }
    private void Diagnostic(string message)
    {
        if (_lastDiagnostic == message) return;
        _lastDiagnostic = message;
        try { _log(message); } catch { /* diagnostics cannot authorize or bypass owner cleanup */ }
    }
}

