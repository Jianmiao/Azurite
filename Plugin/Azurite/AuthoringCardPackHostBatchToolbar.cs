using System;
using System.Collections.Generic;
using Azurite.Core;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Azurite;

/// <summary>Owned native batch client. All writes go through the Root's existing Host.</summary>
internal sealed class AuthoringCardPackHostBatchToolbar : IDisposable
{
    private readonly int _editorThreadId, _depth;
    private readonly Func<bool> _contextAllowed;
    private readonly AuthoringBatchWorkflow _workflow;
    private readonly AuthoringCardPackHostResourcePicker _offstage;
    private readonly UILabel _typography;
    private readonly Color _background, _control;
    private readonly List<(Action Managed, EventDelegate.Callback Native, Action Remove)> _bindings = new();
    private readonly List<UIInput> _inputs = new();
    private readonly List<UIButton> _editing = new(), _slots = new(), _modes = new(), _voices = new();
    private GameObject? _root;
    private UIButton _preview = null!, _confirm = null!, _cancel = null!, _recover = null!, _search = null!, _next = null!;
    private UILabel _previewText = null!, _status = null!, _parameters = null!, _selected = null!, _confirmLabel = null!;
    private UIInput _text = null!, _query = null!;
    private UIScrollView _previewScroll = null!;
    private readonly UIButton[] _candidates = new UIButton[3];
    private readonly UILabel[] _candidateLabels = new UILabel[3];
    private readonly string?[] _ids = new string?[3];
    private readonly bool[] _available = new bool[3];
    private int _start, _slot = 1, _mode, _callbackDepth;
    private long _generation;
    private bool _copyVoice = true, _enabled, _disposed, _disposeRequested, _dirty = true, _subscribed;
    private AuthoringBatchAction _action = AuthoringBatchAction.ChangeSpeaker;
    private Guid? _token;
    private AuthoringBatchDraft? _previewDraft;

    internal AuthoringCardPackHostBatchToolbar(int editorThreadId, Transform parent, UILabel typography, Vector2 origin,
        Func<bool> contextAllowed, AuthoringBatchWorkflow workflow, AuthoringCardPackHostResourcePicker offstage)
    {
        if (editorThreadId <= 0 || Environment.CurrentManagedThreadId != editorThreadId) throw new InvalidOperationException("Batch toolbar requires the qualified editor thread.");
        _editorThreadId = editorThreadId; _contextAllowed = contextAllowed; _workflow = workflow; _offstage = offstage;
        if (parent == null || typography == null || typography.fontSize <= 0 || typography.bitmapFont == null && typography.trueTypeFont == null)
            throw new ArgumentException("A real UI parent and neighboring font are required.");
        var panel = parent.gameObject.GetComponentInParent<UIPanel>() ?? throw new ArgumentException("Native NGUI parent panel is required.");
        _typography = typography; _depth = typography.depth + 2;
        bool light = typography.color.r + typography.color.g + typography.color.b > 1.5f;
        _background = light ? new(.10f, .12f, .15f, .98f) : new(.94f, .95f, .97f, .98f);
        _control = light ? new(.19f, .22f, .27f, 1f) : new(.82f, .85f, .89f, 1f);
        try
        {
            _root = NGUITools.AddChild(parent.gameObject); _root.name = "AzuriteBatchView";
            _root.transform.localPosition = new(origin.x, origin.y, 0); _root.transform.localScale = Vector3.one; _root.SetActive(false);
            var background = Background(_root, "Background", 0, 0, 760, 940, _background);
            background.depth = _depth - 1; // Full-surface hit target stays behind every interactive widget.
            NGUITools.AddWidgetCollider(background.gameObject);
            Label(_root, "Title", 16, 8, 728, 32, "批量编辑 · 编辑器多选行 · 预览后确认");
            string[] names = { "Speaker", "Text", "Duplicate", "Delete", "Multiline" };
            string[] labels = { "发言槽位", "替换全文", "复制", "删除", "多行插入" };
            var actions = new[] { AuthoringBatchAction.ChangeSpeaker, AuthoringBatchAction.SetText, AuthoringBatchAction.Duplicate, AuthoringBatchAction.Delete, AuthoringBatchAction.AddMultiline };
            for (int i = 0; i < 5; i++)
            {
                int at = i; var button = Button(_root, "Action" + names[i], 16 + 148 * i, 48, 136, 32, labels[i]);
                _editing.Add(button); Bind(button, () => { _action = actions[at]; InvalidatePreview(); RefreshControls(); });
            }
            _parameters = Label(_root, "Parameters", 16, 88, 728, 40, "", 2);
            for (int i = 1; i <= 5; i++)
            {
                int slot = i; var button = Button(_root, "Slot" + i, 16 + (i - 1) * 148, 132, 136, 30, "槽位 " + i);
                _slots.Add(button); Bind(button, () => { _slot = slot; InvalidatePreview(); RefreshControls(); });
            }
            var keep = Button(_root, "KeepVoice", 16, 174, 356, 30, "复制时保留配音（默认）");
            var omit = Button(_root, "OmitVoice", 388, 174, 356, 30, "复制时不带配音");
            _voices.Add(keep); _voices.Add(omit);
            Bind(keep, () => { _copyVoice = true; InvalidatePreview(); RefreshControls(); });
            Bind(omit, () => { _copyVoice = false; InvalidatePreview(); RefreshControls(); });
            string[] modes = { "逐锚点可读模式", "显式旁白", "台上槽位对白", "画外音对白" };
            for (int i = 0; i < 4; i++)
            {
                int mode = i; var button = Button(_root, "Mode" + i, 16 + i * 184, 216, 176, 32, modes[i]);
                _modes.Add(button); Bind(button, () => { _mode = mode; InvalidatePreview(); RefreshControls(); });
            }
            Label(_root, "TextCaption", 16, 260, 728, 32, "替换全文 / 插入文本（保留空白、空行；多行插入保留末尾空行）");
            _text = Input(_root, "Text", 16, 298, 728, 86, "", true);
            Bind(_text, InvalidatePreview);
            Label(_root, "OffstageCaption", 16, 396, 728, 28, "画外音角色：搜索并明确选择实际资源（仅画外音模式使用）");
            _query = Input(_root, "OffstageQuery", 16, 428, 450, 30, "", false);
            _search = Button(_root, "OffstageSearch", 478, 428, 126, 30, "搜索");
            _next = Button(_root, "OffstageNext", 616, 428, 128, 30, "下一页");
            Bind(_query, () => { _start = 0; _offstage.SetQuery(CardResourceKind.Character, _query.value); });
            Bind(_search, () => { _offstage.SetQuery(CardResourceKind.Character, _query.value); _start = 0; _offstage.SearchFirst(3); });
            Bind(_next, () => { _offstage.SetQuery(CardResourceKind.Character, _query.value); _start = _offstage.Candidates.Count; _offstage.SearchNext(3); });
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _candidates[i] = Button(_root, "OffstageCandidate" + i, 16, 468 + i * 36, 728, 30, "（无候选）", out _candidateLabels[i]);
                Bind(_candidates[i], () => { if (_ids[index] != null && _available[index]) _offstage.Select(_ids[index]!); });
            }
            _selected = Label(_root, "OffstageSelected", 16, 580, 728, 40, "尚未选择", 2);
            Label(_root, "PreviewCaption", 16, 628, 728, 24, "精确变化预览（可滚动；仅确认后写入，单次最多 500 项）");
            CreatePreview(panel.depth + 1);
            _preview = Button(_root, "Preview", 16, 842, 174, 34, "捕获并预览");
            _confirm = Button(_root, "Confirm", 200, 842, 174, 34, "确认操作", out _confirmLabel);
            _cancel = Button(_root, "Cancel", 384, 842, 174, 34, "取消预览");
            _recover = Button(_root, "Recover", 568, 842, 176, 34, "检查并恢复");
            _status = Label(_root, "Status", 16, 886, 728, 44, "选择动作与参数，再捕获所选行。", 2);
            Bind(_preview, Preview); Bind(_confirm, Confirm); Bind(_cancel, () => { ClearPreview(); Present(_workflow.Cancel()); }, false);
            Bind(_recover, () => { ClearPreview(); Present(_workflow.Recover()); }, false);
            _offstage.Changed += ResourcesChanged; _subscribed = true;
            RefreshControls();
        }
        catch { DisposeNow(); throw; }
    }
    internal void Update(bool enabled)
    {
        RequireThread();
        if (_disposeRequested) { DisposeNow(); return; }
        if (_disposed) return;
        if (_enabled && !enabled) InvalidatePreview();
        _enabled = enabled; _root!.SetActive(enabled);
        try
        {
            if (enabled && Allowed())
            {
                if (_action == AuthoringBatchAction.AddMultiline && _mode == 3) _offstage.ReadChoice();
                if (_dirty) { RefreshResources(); _dirty = false; }
                if (_token.HasValue && _previewDraft != ReadDraft()) InvalidatePreview();
            }
            else if (enabled) InvalidatePreview();
            RefreshControls();
        }
        catch (Exception error) { InvalidatePreview(); _status.text = "VIEW_FAILED: " + error.Message; RefreshControls(); }
    }
    internal void InvalidatePreview()
    {
        RequireThread(); if (_disposed) return;
        unchecked { _generation++; }
        ClearPreview();
        Present(_workflow.Cancel());
    }
    private AuthoringBatchDraft ReadDraft()
    {
        AuthoringBatchMultilineOptions? options = null;
        if (_action == AuthoringBatchAction.AddMultiline)
            options = _mode switch
            {
                0 => new(), 1 => new(AuthoringBatchMultilineMode.Narration),
                2 => new(AuthoringBatchMultilineMode.Dialogue, SpeakerSlot: _slot),
                _ => new(AuthoringBatchMultilineMode.Dialogue, OffstageResourceId:
                    _offstage.ReadChoice()?.ResourceId ?? throw new InvalidOperationException("请明确选择支持画外音的实际角色资源。"))
            };
        return new(_action, _text.value, _slot, options, _copyVoice);
    }
    private void Preview()
    {
        long generation = _generation; ClearPreview(); var draft = ReadDraft();
        if (generation != _generation || _disposeRequested) return;
        var result = _workflow.Preview(draft);
        if (generation != _generation || _disposeRequested) { InvalidatePreview(); return; }
        if (result.Preview != null)
        {
            _token = result.Preview.ConfirmationToken; _previewDraft = draft; _previewText.text = result.Preview.Text;
            _previewScroll.ResetPosition(); _previewScroll.UpdateScrollbars(true);
        }
        Present(result);
    }
    private void Confirm()
    {
        if (!_token.HasValue) return;
        long generation = _generation; var token = _token.Value; _token = null; _previewDraft = null;
        RefreshControls(); var draft = ReadDraft();
        if (generation != _generation || _disposeRequested) { InvalidatePreview(); return; }
        Present(_workflow.Confirm(token, draft));
    }
    private void ResourcesChanged(AuthoringCardPackPickerChange change) { if (!_disposed) { _dirty = true; InvalidatePreview(); } }
    private void RefreshResources()
    {
        var values = _offstage.Candidates;
        if (_start >= values.Count) _start = 0;
        for (int i = 0; i < 3; i++)
        {
            var item = _start + i < values.Count ? values[_start + i] : null;
            _ids[i] = item?.ResourceId; _available[i] = item?.Available == true;
            _candidateLabels[i].text = item == null ? "（无候选）" : (item.Available ? "" : "不可用：") + item.Name + " | " + item.ResourceId;
        }
        if (_action == AuthoringBatchAction.AddMultiline && _mode == 3)
            _selected.text = _offstage.ReadChoice() is { } choice ? "已选：" + choice.ResourceId : "尚未选择";
    }
    private void ClearPreview() { _token = null; _previewDraft = null; if (_previewText != null) _previewText.text = ""; }
    private void Present(CardPackV2WorkflowResult result) { if (_disposed) return; if (_status != null) _status.text = result.Code + ": " + result.Message; RefreshControls(); }
    private void RefreshControls()
    {
        bool editing = _enabled && !_disposed && !_disposeRequested && !_workflow.RecoveryRequired && Allowed();
        bool multi = _action == AuthoringBatchAction.AddMultiline;
        foreach (var button in _editing) button.isEnabled = editing;
        foreach (var button in _slots) button.isEnabled = editing && (_action == AuthoringBatchAction.ChangeSpeaker || multi && _mode == 2);
        foreach (var button in _modes) button.isEnabled = editing && multi;
        foreach (var button in _voices) button.isEnabled = editing && _action == AuthoringBatchAction.Duplicate;
        if (_preview == null) return;
        bool offstage = editing && multi && _mode == 3;
        _text.enabled = editing && (multi || _action == AuthoringBatchAction.SetText); _query.enabled = offstage;
        _search.isEnabled = offstage; _next.isEnabled = offstage && _offstage.NextCursor != null;
        for (int i = 0; i < 3; i++) _candidates[i].isEnabled = offstage && _ids[i] != null && _available[i];
        _preview.isEnabled = editing; _confirm.isEnabled = editing && _token.HasValue; _cancel.isEnabled = _enabled && _token.HasValue;
        _recover.isEnabled = _enabled && !_disposeRequested && _workflow.RecoveryRequired;
        _parameters.text = "当前：" + AuthoringBatchWorkflow.Name(_action) + " | 槽位 " + _slot + " | " + (_copyVoice ? "复制保留配音" : "复制时不带配音") + "\n多行模式：" + new[] { "逐锚点可读（控制锚点须明确选择模式）", "旁白", "台上槽位对白", "画外音对白" }[_mode];
        _confirmLabel.text = _action switch { AuthoringBatchAction.Delete => "确认删除所选行", AuthoringBatchAction.Duplicate => _copyVoice ? "确认复制（带配音）" : "确认复制（无配音）", AuthoringBatchAction.AddMultiline => "确认插入全部行", AuthoringBatchAction.SetText => "确认替换全文", _ => "确认更换槽位" };
    }
    private bool Allowed() { try { return _contextAllowed(); } catch { return false; } }
    private void Callback(Action action, bool requireContext)
    {
        if (Environment.CurrentManagedThreadId != _editorThreadId || _disposed || _disposeRequested || !_enabled || _callbackDepth != 0 || requireContext && (!Allowed() || _workflow.RecoveryRequired)) return;
        _callbackDepth++;
        try { action(); }
        catch (Exception error) { InvalidatePreview(); _status.text = "VIEW_INPUT_FAILED: " + error.Message; }
        finally { _callbackDepth--; }
    }
    private void Bind(UIButton button, Action action, bool requireContext = true)
    {
        Action managed = () => { if (button.isEnabled) Callback(action, requireContext); };
        var native = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(managed) ?? throw new InvalidOperationException("Cannot bind batch button.");
        EventDelegate.Add(button.onClick, native); _bindings.Add((managed, native, () => EventDelegate.Remove(button.onClick, native)));
    }
    private void Bind(UIInput input, Action action)
    {
        Action managed = () => { if (input.enabled) Callback(action, true); };
        var native = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(managed) ?? throw new InvalidOperationException("Cannot bind batch input.");
        EventDelegate.Add(input.onChange, native); _bindings.Add((managed, native, () => EventDelegate.Remove(input.onChange, native)));
    }
    private GameObject Node(GameObject parent, string name, float x, float y)
    {
        var node = NGUITools.AddChild(parent);
        node.name = "AzuriteBatch" + name;
        node.transform.localPosition = new Vector3(x, -y, 0);
        node.transform.localScale = Vector3.one;
        return node;
    }
    private UITexture Background(GameObject parent, string name, int x, int y, int width, int height, Color color)
    {
        var node = Node(parent, name, x, y);
        var background = node.AddComponent<UITexture>();
        background.mainTexture = Texture2D.whiteTexture; background.pivot = UIWidget.Pivot.TopLeft;
        background.width = width; background.height = height; background.color = color; background.depth = _depth;
        return background;
    }
    private UILabel Label(GameObject parent, string name, int x, int y, int width, int height, string text, int lines = 1)
    {
        var node = Node(parent, name, x, y);
        var label = node.AddComponent<UILabel>();
        if (_typography.bitmapFont != null) label.bitmapFont = _typography.bitmapFont; else label.trueTypeFont = _typography.trueTypeFont;
        label.fontSize = _typography.fontSize; label.fontStyle = _typography.fontStyle; label.spacingY = _typography.spacingY;
        label.color = _typography.color; label.depth = _depth + 2; label.width = width; label.height = height;
        label.pivot = UIWidget.Pivot.TopLeft; label.alignment = NGUIText.Alignment.Left;
        label.maxLineCount = lines; label.overflowMethod = UILabel.Overflow.ClampContent; label.supportEncoding = false; label.text = text;
        return label;
    }
    private UIButton Button(GameObject parent, string name, int x, int y, int width, int height, string caption)
        => Button(parent, name, x, y, width, height, caption, out _);
    private UIButton Button(GameObject parent, string name, int x, int y, int width, int height, string caption, out UILabel label)
    {
        var background = Background(parent, name, x, y, width, height, _control);
        var button = background.gameObject.AddComponent<UIButton>();
        button.tweenTarget = background.gameObject; button.defaultColor = _control;
        label = Label(background.gameObject, name + "Label", 6, 4, width - 12, height - 8, caption);
        NGUITools.AddWidgetCollider(background.gameObject);
        return button;
    }
    private UIInput Input(GameObject parent, string name, int x, int y, int width, int height, string value, bool multiline)
    {
        var background = Background(parent, name, x, y, width, height, _control);
        var label = Label(background.gameObject, name + "Label", 6, 4, width - 12, height - 8, value, multiline ? 2 : 1);
        var input = background.gameObject.AddComponent<UIInput>();
        input.label = label; input.defaultColor = _typography.color; input.validation = UIInput.Validation.None;
        input.onReturnKey = multiline ? UIInput.OnReturnKey.NewLine : UIInput.OnReturnKey.Submit;
        input.characterLimit = 0; input.value = value;
        NGUITools.AddWidgetCollider(background.gameObject);
        _inputs.Add(input);
        return input;
    }
    private void CreatePreview(int panelDepth)
    {
        // The fixed hit surface is a sibling of the movable scroll content, so
        // long text cannot scroll its only drag/wheel target out of the viewport.
        var background = Background(_root!, "PreviewBackground", 16, 654, 728, 174, _background);
        var viewport = Node(_root!, "PreviewViewport", 16, 654);
        var panel = viewport.AddComponent<UIPanel>(); panel.depth = panelDepth;
        panel.clipping = UIDrawCall.Clipping.SoftClip; panel.baseClipRegion = new Vector4(364, -87, 728, 174);
        _previewScroll = viewport.AddComponent<UIScrollView>(); _previewScroll.movement = UIScrollView.Movement.Vertical;
        NGUITools.AddWidgetCollider(background.gameObject);
        var drag = background.gameObject.AddComponent<UIDragScrollView>(); drag.scrollView = _previewScroll;
        _previewText = Label(viewport, "PreviewText", 6, 4, 710, 166, string.Empty, 0);
        _previewText.overflowMethod = UILabel.Overflow.ResizeHeight;
    }
    private void RequireThread() { if (Environment.CurrentManagedThreadId != _editorThreadId) throw new InvalidOperationException("Batch view requires its owner thread."); }
    public void Dispose()
    {
        RequireThread(); if (_disposed) return;
        if (_callbackDepth != 0) { _disposeRequested = true; InvalidatePreview(); return; }
        DisposeNow();
    }
    private void DisposeNow()
    {
        if (_disposed) return; _disposed = true; _workflow.Cancel();
        if (_subscribed) _offstage.Changed -= ResourcesChanged;
        foreach (var binding in _bindings) { try { binding.Remove(); } catch { } } _bindings.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root); _root = null;
    }
}

