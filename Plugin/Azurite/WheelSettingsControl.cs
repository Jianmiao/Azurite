using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Azurite;

// A small, self-owned control inserted below AA's font row. It clones only
// visual prefabs, clears the slider's native callbacks, and polls the value so
// no AA FPS/volume callback can be inherited by the new control.
internal sealed class WheelSettingsControl : IDisposable
{
    private const float RowGap = 24f;
    private const int CaptionWidth = 240;
    private const int ValueWidth = 100;
    private sealed class Entry
    {
        internal SettingPanel Panel = null!;
        internal UILabel Anchor = null!;
        internal UISlider Slider = null!;
        internal UILabel Label = null!;
        internal UILabel ValueLabel = null!;
        internal bool Updating;
    }
    private readonly Func<float> _get;
    private readonly Action<float> _set;
    private readonly Dictionary<IntPtr, Entry> _entries = new();
    private readonly List<EventDelegate.Callback> _callbacks = new();
    private readonly List<Action> _managedCallbacks = new();
    private bool _disposed;
    private float _nextDiscovery;
    internal WheelSettingsControl(Func<float> get, Action<float> set) { _get = get; _set = set; }
    internal void Update()
    {
        if (_disposed) return;
        try
        {
            if (Time.unscaledTime >= _nextDiscovery)
            {
                _nextDiscovery = Time.unscaledTime + 0.5f;
                foreach (var panel in UnityEngine.Object.FindObjectsOfType<SettingPanel>())
                {
                    if (panel == null || panel.widgets == null || panel.widgets.fontNameLabel == null) continue;
                    if (!_entries.TryGetValue(panel.Pointer, out var e) || e.Slider == null || e.Label == null || e.ValueLabel == null)
                        Create(panel);
                    else if (e.Anchor != null)
                        Layout(e);
                }
            }
            foreach (var e in _entries.Values)
            {
                if (e.Panel == null || e.Slider == null || e.Label == null || e.ValueLabel == null) continue;
                float scale = Math.Clamp(_get(), 0.5f, 2f);
                float value = Math.Clamp((scale - 0.5f) / 1.5f, 0f, 1f);
                if (!e.Updating && Math.Abs(e.Slider.value - value) > 0.001f)
                { e.Updating = true; e.Slider.value = value; e.Updating = false; }
                if (e.Label.text != "滚动灵敏度") e.Label.text = "滚动灵敏度";
                string text = $"{scale * 100f:0}%";
                if (e.ValueLabel.text != text) e.ValueLabel.text = text;
            }
        }
        catch { /* a closing panel is allowed to disappear between discovery and write */ }
    }
    private Entry? Create(SettingPanel panel)
    {
        var widgets = panel.widgets;
        var anchor = widgets.fontNameLabel;
        var parent = anchor.transform.parent;
        if (parent == null || widgets.autoDelaySlider == null) return null;
        GameObject sliderObject = NGUITools.AddChild(parent, widgets.autoDelaySlider.gameObject);
        var slider = sliderObject.GetComponent<UISlider>();
        if (slider == null) return null;
        sliderObject.name = "AzuriteWheelSensitivitySlider";
        slider.onChange?.Clear();
        // AUTO's prefab carries its own 0s/1s/2s tick labels. They describe a
        // different setting and overlap this control, so keep only the slider
        // widgets. Leave enough horizontal room for the percentage label.
        foreach (var tick in sliderObject.GetComponentsInChildren<UILabel>(true))
            tick.enabled = false;
        // The font-name object may be a LocalizedUILabel with anchors and font
        // adapters. Copy its typography, never its lifecycle or clipping box.
        var label = CreateLabel(parent, anchor, "AzuriteWheelSensitivityLabel", CaptionWidth, NGUIText.Alignment.Left);
        var valueLabel = CreateLabel(parent, anchor, "AzuriteWheelSensitivityValue", ValueWidth, NGUIText.Alignment.Right);
        label.text = "滚动灵敏度";
        valueLabel.text = "100%";
        var entry = new Entry { Panel = panel, Anchor = anchor, Slider = slider, Label = label, ValueLabel = valueLabel };
        Layout(entry);
        Action changed = () =>
        {
            if (_disposed || entry.Updating || entry.Slider == null) return;
            float scale = Math.Clamp(0.5f + entry.Slider.value * 1.5f, 0.5f, 2f);
            _set(scale);
        };
        var callback = DelegateSupport.ConvertDelegate<EventDelegate.Callback>(changed)
            ?? throw new InvalidOperationException("Cannot bind Azurite wheel sensitivity slider.");
        if (slider.onChange == null)
            throw new InvalidOperationException("Azurite wheel sensitivity slider has no change event list.");
        EventDelegate.Add(slider.onChange, callback);
        _managedCallbacks.Add(changed);
        _callbacks.Add(callback);
        _entries[panel.Pointer] = entry;
        return entry;
    }
    private static UILabel CreateLabel(Transform parent, UILabel source, string name, int width, NGUIText.Alignment alignment)
    {
        GameObject obj = NGUITools.AddChild(parent.gameObject);
        obj.name = name;
        var label = obj.AddComponent<UILabel>();
        label.width = width;
        label.pivot = UIWidget.Pivot.Left;
        label.alignment = alignment;
        label.maxLineCount = 1;
        label.overflowMethod = UILabel.Overflow.ShrinkContent;
        ApplyTypography(label, source);
        return label;
    }

    private static void ApplyTypography(UILabel label, UILabel source)
    {
        if (source.bitmapFont != null) label.bitmapFont = source.bitmapFont;
        else label.trueTypeFont = source.trueTypeFont;
        label.fontSize = Math.Max(1, source.fontSize);
        label.fontStyle = source.fontStyle;
        label.height = Math.Max(source.height, label.fontSize + Math.Abs(source.spacingY) + 16);
        label.color = source.color;
        label.depth = source.depth;
    }
    private static void Layout(Entry entry)
    {
        UILabel anchor = entry.Anchor;
        Transform? parent = entry.Slider.transform.parent;
        if (parent == null || entry.Panel.widgets.autoDelaySlider == null) return;
        ApplyTypography(entry.Label, anchor);
        ApplyTypography(entry.ValueLabel, anchor);
        float rowOffset = Math.Max(74f, entry.Label.height + RowGap);
        Vector3 labelPosition = parent.InverseTransformPoint(anchor.transform.position);
        labelPosition.y -= rowOffset;
        // Preserve the native slider column instead of pushing a full-width
        // slider to the right of the font-name value column.
        Vector3 sliderPosition = parent.InverseTransformPoint(entry.Panel.widgets.autoDelaySlider.transform.position);
        sliderPosition.y = labelPosition.y;
        entry.Slider.transform.localPosition = sliderPosition;
        if (!TrySliderBounds(entry.Slider, parent, out float left, out float right)) return;
        labelPosition.x = left - RowGap - entry.Label.width;
        entry.Label.transform.localPosition = labelPosition;
        labelPosition.x = right + RowGap;
        entry.ValueLabel.transform.localPosition = labelPosition;
    }
    private static bool TrySliderBounds(UISlider slider, Transform parent, out float left, out float right)
    {
        left = float.PositiveInfinity;
        right = float.NegativeInfinity;
        foreach (UIWidget widget in slider.gameObject.GetComponentsInChildren<UIWidget>(true))
        {
            if (widget is UILabel) continue;
            var corners = widget.worldCorners;
            if (corners == null || corners.Length != 4) continue;
            for (int i = 0; i < corners.Length; i++)
            {
                float x = parent.InverseTransformPoint(corners[i]).x;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }
        return float.IsFinite(left) && float.IsFinite(right) && right > left;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _callbacks.Clear();
        _managedCallbacks.Clear();
        foreach (var e in _entries.Values)
        {
            try { if (e.Slider != null) UnityEngine.Object.Destroy(e.Slider.gameObject); } catch { }
            try { if (e.Label != null) UnityEngine.Object.Destroy(e.Label.gameObject); } catch { }
            try { if (e.ValueLabel != null) UnityEngine.Object.Destroy(e.ValueLabel.gameObject); } catch { }
        }
        _entries.Clear();
    }
}

