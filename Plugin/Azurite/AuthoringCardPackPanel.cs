using System;
using System.Collections.Generic;
using Azurite.Core;
using Il2CppInterop.Runtime;

namespace Azurite;

/// <summary>Owns callbacks on dedicated card-pack preview/confirm/cancel/recover controls.</summary>
internal sealed class AuthoringCardPackPanel : IDisposable
{
    private readonly CardPackV2Workflow _workflow;
    private readonly Func<CardPackV2Draft> _readDraft;
    private readonly UIButton _preview, _confirm, _cancel, _recover;
    private readonly UILabel _previewText, _status;
    // Retain both delegate layers for IL2CPP. Remove only our callbacks on dispose.
    private readonly List<(UIButton Button, EventDelegate.Callback Callback, Action Managed)> _bindings = new();
    private CardPackV2Draft? _previewDraft;
    private Guid? _token;
    private bool _disposed;

    // The composition root supplies four dedicated buttons, an existing scrollable
    // multiline preview label, a status label, and its selected pack/parameters reader.
    // Never clone a native action button with inherited callbacks into this boundary.
    internal AuthoringCardPackPanel(CardPackV2Workflow workflow, Func<CardPackV2Draft> readDraft,
        UIButton preview, UIButton confirm, UIButton cancel, UIButton recover, UILabel previewText, UILabel status)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _readDraft = readDraft ?? throw new ArgumentNullException(nameof(readDraft));
        var identities = new HashSet<IntPtr>();
        foreach (var button in new[] { preview, confirm, cancel, recover })
            if (button == null || button.Pointer == IntPtr.Zero || !identities.Add(button.Pointer) || button.onClick == null || button.onClick.Count != 0)
                throw new ArgumentException("Card-pack panel requires distinct dedicated buttons with no inherited callbacks.");
        if (previewText == null || status == null) throw new ArgumentException("Preview and status labels are required.");
        _preview = preview; _confirm = confirm; _cancel = cancel; _recover = recover;
        _previewText = previewText; _status = status;
        try
        {
            _previewText.supportEncoding = false;
            _status.supportEncoding = false;
            Bind(_preview, Preview);
            Bind(_confirm, Confirm);
            Bind(_cancel, Cancel);
            Bind(_recover, Recover);
            _status.text = "请选择卡包、目标和资源，然后预览。";
            RefreshButtons();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Called by the owning panel tick. Draft edits revoke visible confirmation immediately.</summary>
    internal void Update()
    {
        if (_disposed) return;
        Invoke(() =>
        {
            if (_token.HasValue && _previewDraft != _readDraft())
            {
                ClearPreview();
                _workflow.Cancel();
                _status.text = "DRAFT_CHANGED: 卡包内容或语言已改变，请重新预览。";
            }
            RefreshButtons();
        });
    }

    private void Preview()
    {
        ClearPreview();
        var draft = _readDraft();
        var result = _workflow.Preview(draft);
        if (result.Preview != null)
        {
            _token = result.Preview.ConfirmationToken;
            _previewDraft = draft;
            _previewText.text = result.Preview.Text;
        }
        Present(result);
    }

    private void Confirm()
    {
        if (!_token.HasValue) return;
        var token = _token.Value;
        // Re-read current controls even when Update has not yet observed an edit.
        var draft = _readDraft();
        _token = null;
        _previewDraft = null;
        Present(_workflow.Confirm(token, draft));
    }

    private void Cancel()
    {
        ClearPreview();
        Present(_workflow.Cancel());
    }

    private void Recover()
    {
        ClearPreview();
        Present(_workflow.Recover());
    }

    private void ClearPreview()
    {
        _token = null;
        _previewDraft = null;
        if (_previewText != null) _previewText.text = string.Empty;
    }

    private void Present(CardPackV2WorkflowResult result)
    {
        _status.text = result.Code + ": " + result.Message;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool recovery = _workflow.RecoveryRequired;
        _preview.isEnabled = !recovery;
        _confirm.isEnabled = !recovery && _token.HasValue;
        _cancel.isEnabled = _token.HasValue;
        _recover.isEnabled = recovery;
    }

    private void Bind(UIButton button, Action action)
    {
        Action managed = () => Invoke(action);
        var callback = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(managed)
            ?? throw new InvalidOperationException("Cannot bind card-pack control.");
        _bindings.Add((button, callback, managed));
        EventDelegate.Add(button.onClick, callback);
    }

    private void Invoke(Action action)
    {
        if (_disposed) return;
        try { action(); }
        catch (Exception error)
        {
            // UI/read failures never trigger a second host call. The workflow itself
            // retains uncertain-commit recovery state, independently of this view.
            ClearPreview();
            _workflow.Cancel();
            if (_status != null) _status.text = "PANEL_FAILED: " + error.GetType().Name + ": " + error.Message;
            RefreshButtons();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _token = null;
        _previewDraft = null;
        _workflow.Cancel();
        foreach (var binding in _bindings)
        {
            try
            {
                if (binding.Button != null)
                {
                    EventDelegate.Remove(binding.Button.onClick, binding.Callback);
                    binding.Button.isEnabled = false;
                }
            }
            catch { /* destroyed native controls have no live UI callback owner */ }
        }
        _bindings.Clear();
    }
}
