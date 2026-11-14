using System;
using System.Collections.Generic;
using Azurite.Core;
using Il2CppInterop.Runtime;
using Studio.Scripts;
using UnityEngine;

namespace Azurite;

/// <summary>
/// Unregistered owned NGUI view. The caller retains the Workflow and supplies the
/// real parent, typography, thread and fresh context gate. Default geometry is
/// bounded at 760x940 local UI units; runtime pixels still require validation.
/// </summary>
internal sealed class AuthoringCardPackHostView : IDisposable
{
    private const int Width = 760, Height = 940, PageSize = 3;
    private sealed class ResourcePane
    {
        internal AuthoringCardPackHostResourcePicker Picker = null!;
        internal CardResourceKind Kind;
        internal UIInput Query = null!;
        internal UIButton Search = null!, Next = null!;
        internal UIButton[] Buttons = new UIButton[PageSize];
        internal UILabel[] Labels = new UILabel[PageSize];
        internal string?[] Ids = new string?[PageSize];
        internal bool[] Available = new bool[PageSize];
        internal UILabel Selected = null!;
        internal int Start;
    }

    private readonly int _editorThreadId;
    private readonly Func<bool> _contextAllowed;
    private readonly UILabel _typography;
    private readonly CardPackV2Workflow _workflow;
    private readonly AuthoringCardPackHostDraft _draft;
    private readonly AuthoringCardPackHostResourcePicker _transition, _sound;
    private readonly List<(Action Managed, EventDelegate.Callback Native, Action Remove)> _bindings = new();
    private readonly List<UIButton> _editingButtons = new();
    private readonly List<UIInput> _inputs = new();
    private readonly Color _background, _control;
    private readonly int _depth;
    private GameObject? _root;
    private AuthoringCardPackPanel? _panel;
    private ResourcePane _transitionPane = null!, _soundPane = null!;
    private UIButton _preview = null!, _confirm = null!, _cancel = null!, _recover = null!;
    private UILabel _previewText = null!, _status = null!, _target = null!, _mode = null!;
    private UIScrollView _previewScroll = null!;
    private UIInput _action = null!, _chinese = null!, _english = null!, _extraLocale = null!, _extraText = null!, _locale = null!;
    private PresetCardKind _kind = PresetCardKind.Transition;
    private bool _fiveFamily = true, _dirty = true, _disposed, _disposeRequested, _subscribed, _featureEnabled;
    private int _callbackDepth, _readDepth;
    private string _lastPreview = string.Empty;

    internal AuthoringCardPackHostView(int editorThreadId, Transform parent, UILabel typography, Vector2 origin,
        Func<bool> contextAllowed, CardPackV2Workflow workflow, AuthoringCardPackHostResourcePicker transition,
        AuthoringCardPackHostResourcePicker sound, AuthoringCardPackHostDraft draft)
    {
        if (editorThreadId <= 0 || Environment.CurrentManagedThreadId != editorThreadId)
            throw new InvalidOperationException("View construction requires the qualified editor thread.");
        _editorThreadId = editorThreadId;
        _contextAllowed = contextAllowed ?? throw new ArgumentNullException(nameof(contextAllowed));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _transition = transition ?? throw new ArgumentNullException(nameof(transition));
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        if (parent == null || typography == null || typography.fontSize <= 0 ||
            typography.bitmapFont == null && typography.trueTypeFont == null)
            throw new ArgumentException("A live NGUI parent and actual neighboring font are required.");
        var nativePanel = parent.gameObject.GetComponentInParent<UIPanel>();
        if (nativePanel == null) throw new ArgumentException("The supplied parent is not in a native NGUI panel.");
        _typography = typography; _depth = typography.depth + 2;
        bool lightText = typography.color.r + typography.color.g + typography.color.b > 1.5f;
        _background = lightText ? new Color(.10f, .12f, .15f, .98f) : new Color(.94f, .95f, .97f, .98f);
        _control = lightText ? new Color(.19f, .22f, .27f, 1f) : new Color(.82f, .85f, .89f, 1f);
        try
        {
            _root = NGUITools.AddChild(parent.gameObject);
            _root.name = "AzuriteCardView";
            _root.transform.localPosition = new Vector3(origin.x, origin.y, 0);
            _root.transform.localScale = Vector3.one;
            _root.SetActive(false);
            var background = Background(_root, "Background", 0, 0, Width, Height, _background);
            background.depth = _depth - 1; // Full-surface hit target stays behind every interactive widget.
            NGUITools.AddWidgetCollider(background.gameObject);
            _mode = Label(_root, "Mode", 16, 8, 728, 34, "五类内置示例（所有台词可编辑，待预览验证）");
            string[] names = { "Transition", "Combat", "Action", "Language", "Sound", "Example" };
            string[] captions = { "转场", "战斗", "动作", "语言", "音效", "五类示例" };
            var families = new[] { PresetCardKind.Transition, PresetCardKind.Combat, PresetCardKind.Action, PresetCardKind.Language, PresetCardKind.SoundEffect };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = Button(_root, "Family" + names[i], 16 + i * 122, 50, 112, 30, captions[i]);
                _editingButtons.Add(button);
                Bind(button, () =>
                {
                    _fiveFamily = index == 5;
                    if (!_fiveFamily) _kind = families[index];
                    if (_kind == PresetCardKind.Combat) _action.value = "Stiff";
                    else if (_kind == PresetCardKind.Action) _action.value = "Greeting";
                    _mode.text = _fiveFamily ? "五类内置示例（台词可编辑，待预览验证）" : captions[index] + "卡包草稿（待预览验证）";
                    _panel?.InvalidateDraft();
                });
            }
            _transitionPane = CreatePane("Transition", "转场资源", 16, transition, CardResourceKind.Transition);
            _soundPane = CreatePane("Sound", "音效资源", 388, sound, CardResourceKind.Sound);
            _target = Label(_root, "TargetSlot", 16, 322, 280, 28, "当前目标：读取原生选中槽位");
            Label(_root, "ActionCaption", 302, 322, 74, 28, "动作参数");
            _action = Input(_root, "Action", 382, 320, 362, 32, "Greeting", false);
            Label(_root, "ActionHelp", 16, 356, 728, 36, "动作：Idle / Greeting / FalldownL / FalldownR / Stiff / Shake / Jump / Hophop；战斗仅使用受击反应。", 2);
            Label(_root, "ChineseCaption", 16, 396, 356, 24, "简中台词（内置示例，可编辑）");
            Label(_root, "EnglishCaption", 388, 396, 356, 24, "英文台词（内置示例，可编辑）");
            _chinese = Input(_root, "ChineseText", 16, 424, 356, 58, "老师，我们出发吧。", true);
            _english = Input(_root, "EnglishText", 388, 424, 356, 58, "Sensei, let's go.", true);
            Label(_root, "ExtraLocaleCaption", 16, 492, 132, 24, "额外语言代码");
            Label(_root, "ExtraTextCaption", 162, 492, 582, 24, "额外语言台词（用户提供，不自动翻译）");
            _extraLocale = Input(_root, "ExtraLocale", 16, 520, 132, 32, string.Empty, false);
            _extraText = Input(_root, "ExtraText", 162, 520, 582, 58, string.Empty, true);
            Label(_root, "LocaleCaption", 16, 588, 168, 28, "本次插入语言代码");
            _locale = Input(_root, "SelectedLocale", 190, 586, 132, 32, "zh-CN", false);
            Label(_root, "LocaleHelp", 336, 584, 408, 36, "只选择已提供的台词；不会切换编辑器语言。", 2);
            Label(_root, "PreviewCaption", 16, 628, 728, 24, "插入预览（请核对后确认；长内容可滚动）");
            CreatePreview(nativePanel.depth + 1);
            _preview = Button(_root, "Preview", 16, 842, 174, 34, "预览");
            _confirm = Button(_root, "Confirm", 200, 842, 174, 34, "确认插入");
            _cancel = Button(_root, "Cancel", 384, 842, 174, 34, "取消预览");
            _recover = Button(_root, "Recover", 568, 842, 176, 34, "检查并恢复");
            _status = Label(_root, "Status", 16, 886, 728, 44, string.Empty, 2);
            _panel = new(_workflow, ReadDraft, _preview, _confirm, _cancel, _recover, _previewText, _status);
            foreach (var input in _inputs) Bind(input, () => _panel?.InvalidateDraft());
            _transition.Changed += OnResourcesChanged;
            _sound.Changed += OnResourcesChanged;
            _subscribed = true;
            ApplyEligibility(false);
        }
        catch { DisposeNow(); throw; }
    }

    internal void InvalidatePreview() { RequireThread(); _panel?.InvalidateDraft(); }

    internal void Update(bool enabled)
    {
        RequireThread();
        if (_disposeRequested) { DisposeNow(); return; }
        if (_disposed) return;
        if (_featureEnabled && !enabled) _panel!.InvalidateDraft();
        _featureEnabled = enabled;
        _root!.SetActive(enabled);
        if (!enabled) { ApplyEligibility(false); return; }
        try
        {
            bool allowed = ContextAllowed();
            if (!allowed) _panel!.InvalidateDraft();
            if (allowed)
            {
                // Cheap identity reads may signal an external catalog/session
                // change; they never Search or Resolve resources on this tick.
                _transition.ReadChoice();
                _sound.ReadChoice();
            }
            _panel!.Update();
            if (_dirty && allowed)
            {
                _dirty = false;
                RefreshPane(_transitionPane);
                RefreshPane(_soundPane);
            }
            var inspector = ScriptNodeInspector.instance;
            int slot = inspector?.selectedSlotNum ?? 0;
            _target.text = slot is >= 1 and <= 5 ? "当前原生目标槽位：" + slot + "（只读）" : "当前原生目标槽位：未选择";
            if (_lastPreview != _previewText.text)
            {
                _lastPreview = _previewText.text;
                _previewScroll.ResetPosition();
                _previewScroll.UpdateScrollbars(true);
            }
            ApplyEligibility(allowed);
        }
        catch (Exception error)
        {
            _panel!.InvalidateDraft();
            _status.text = "VIEW_FAILED: " + error.Message;
            ApplyEligibility(false);
        }
    }

    private ResourcePane CreatePane(string prefix, string caption, int x,
        AuthoringCardPackHostResourcePicker picker, CardResourceKind kind)
    {
        var pane = new ResourcePane { Picker = picker, Kind = kind };
        Label(_root!, prefix + "Caption", x, 90, 356, 24, caption + "（须明确选择）");
        pane.Query = Input(_root!, prefix + "Query", x, 118, 210, 28, string.Empty, false);
        pane.Search = Button(_root!, prefix + "Search", x + 218, 118, 62, 28, "搜索");
        pane.Next = Button(_root!, prefix + "Next", x + 286, 118, 70, 28, "下一页");
        _editingButtons.Add(pane.Search);
        Bind(pane.Query, () => { pane.Start = 0; picker.SetQuery(kind, pane.Query.value); });
        Bind(pane.Search, () => { picker.SetQuery(kind, pane.Query.value); pane.Start = 0; picker.SearchFirst(PageSize); });
        Bind(pane.Next, () => { picker.SetQuery(kind, pane.Query.value); pane.Start = picker.Candidates.Count; picker.SearchNext(PageSize); });
        for (int i = 0; i < PageSize; i++)
        {
            int index = i;
            var button = Button(_root!, prefix + "Candidate" + i, x, 154 + i * 38, 356, 34, string.Empty, out var label);
            pane.Buttons[i] = button;
            pane.Labels[i] = label;
            button.isEnabled = false;
            Bind(button, () =>
            {
                string? id = pane.Ids[index];
                if (id == null || !pane.Available[index]) return;
                picker.Select(id);
            });
        }
        pane.Selected = Label(_root!, prefix + "Selected", x, 274, 356, 40, "尚未选择", 2);
        return pane;
    }

    private void RefreshPane(ResourcePane pane)
    {
        var choice = pane.Picker.ReadChoice();
        var candidates = pane.Picker.Candidates;
        if (pane.Start >= candidates.Count) pane.Start = 0;
        for (int i = 0; i < PageSize; i++)
        {
            int at = pane.Start + i;
            var candidate = at < candidates.Count ? candidates[at] : null;
            pane.Ids[i] = candidate?.ResourceId;
            pane.Available[i] = candidate?.Available == true;
            pane.Labels[i].text = candidate == null ? "（无候选）" :
                (candidate.Available ? string.Empty : "不可用：") + candidate.Name + " | " + candidate.ResourceId;
        }
        pane.Selected.text = choice == null ? "尚未选择" : "已选：" + choice.ResourceId;
    }

    private void OnResourcesChanged(AuthoringCardPackPickerChange change)
    {
        // Never reenter the picker or create/destroy/rebind native controls here.
        if (_disposed) return;
        _dirty = true;
        _panel?.InvalidateDraft();
    }

    private CardPackV2Draft ReadDraft()
    {
        _readDepth++;
        try
        {
            if (_disposed || _disposeRequested) throw new InvalidOperationException("View disposal is pending; no new draft may be read.");
            if (!_fiveFamily && _kind != PresetCardKind.Language)
                return _draft.Build(_kind, _kind is PresetCardKind.Action or PresetCardKind.Combat ? _action.value : null);
            var variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["zh-CN"] = _chinese.value, ["en"] = _english.value
            };
            string extra = _extraLocale.value;
            if (!string.IsNullOrEmpty(extra))
            {
                if (!variants.TryAdd(extra, _extraText.value)) throw new InvalidOperationException("额外语言代码不能重复简中或英文代码。");
            }
            else if (!string.IsNullOrEmpty(_extraText.value)) throw new InvalidOperationException("请填写额外台词对应的语言代码。");
            if (_fiveFamily) return _draft.BuildFiveFamilyExample(variants, _locale.value).Draft;
            return _draft.Build(PresetCardKind.Language, variants: variants, locale: _locale.value);
        }
        finally { _readDepth--; }
    }

    private void ApplyEligibility(bool allowed)
    {
        bool editing = allowed && _featureEnabled && !_workflow.RecoveryRequired && !_disposeRequested && !_disposed;
        foreach (var button in _editingButtons) button.isEnabled = editing;
        foreach (var input in _inputs) input.enabled = editing;
        _action.enabled = editing && !_fiveFamily && _kind is PresetCardKind.Action or PresetCardKind.Combat;
        bool language = editing && (_fiveFamily || _kind == PresetCardKind.Language);
        foreach (var input in new[] { _chinese, _english, _extraLocale, _extraText, _locale }) input.enabled = language;
        foreach (var pane in new[] { _transitionPane, _soundPane })
        {
            if (pane == null) continue;
            pane.Next.isEnabled = editing && pane.Picker.NextCursor != null;
            for (int i = 0; i < PageSize; i++) pane.Buttons[i].isEnabled = editing && pane.Ids[i] != null && pane.Available[i];
        }
        if (_preview == null) return;
        _preview.isEnabled = editing && _preview.isEnabled;
        _confirm.isEnabled = editing && _confirm.isEnabled;
        _cancel.isEnabled = _featureEnabled && _cancel.isEnabled && !_disposeRequested;
        _recover.isEnabled = _featureEnabled && _workflow.RecoveryRequired && !_disposeRequested && !_disposed;
    }

    private bool ContextAllowed()
    {
        try { return _contextAllowed(); } catch { return false; }
    }

    private void Callback(Action action)
    {
        if (Environment.CurrentManagedThreadId != _editorThreadId) return;
        if (_disposed || _disposeRequested || !_featureEnabled || !ContextAllowed()) return;
        _callbackDepth++;
        try { action(); }
        catch (Exception error) { _panel?.InvalidateDraft(); if (_status != null) _status.text = "VIEW_INPUT_FAILED: " + error.Message; }
        finally { _callbackDepth--; }
    }
    private void Bind(UIButton button, Action action)
    {
        Action managed = () => Callback(action);
        var native = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(managed) ?? throw new InvalidOperationException("Cannot bind view button.");
        EventDelegate.Add(button.onClick, native);
        _bindings.Add((managed, native, () => EventDelegate.Remove(button.onClick, native)));
    }
    private void Bind(UIInput input, Action action)
    {
        Action managed = () => Callback(action);
        var native = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(managed) ?? throw new InvalidOperationException("Cannot bind view input.");
        EventDelegate.Add(input.onChange, native);
        _bindings.Add((managed, native, () => EventDelegate.Remove(input.onChange, native)));
    }

    private GameObject Node(GameObject parent, string name, float x, float y)
    {
        var node = NGUITools.AddChild(parent);
        node.name = "AzuriteCard" + name;
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
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _editorThreadId) throw new InvalidOperationException("View access requires its qualified editor thread.");
    }
    public void Dispose()
    {
        RequireThread();
        if (_disposed) return;
        if (_callbackDepth != 0 || _readDepth != 0) { _disposeRequested = true; _panel?.InvalidateDraft(); ApplyEligibility(false); return; }
        DisposeNow();
    }
    private void DisposeNow()
    {
        if (_disposed) return;
        _disposed = true;
        if (_subscribed) { _transition.Changed -= OnResourcesChanged; _sound.Changed -= OnResourcesChanged; _subscribed = false; }
        _panel?.Dispose(); _panel = null;
        foreach (var binding in _bindings) { try { binding.Remove(); } catch { } }
        _bindings.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
    }
}

