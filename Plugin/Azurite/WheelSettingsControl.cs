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
    private const float MinimumLabelWidth = 240f;
    private const float MaximumLabelWidth = 320f;
    private sealed class Entry
    {
        internal SettingPanel Panel = null!;
        internal UILabel Anchor = null!;
        internal UISlider Slider = null!;
        internal UILabel Label = null!;
        internal bool Updating;
        internal Vector3 LastAnchorPosition;
        internal float LastSliderLeftOffset;
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
                    if (!_entries.TryGetValue(panel.Pointer, out var e) || e.Slider == null || e.Label == null)
                        Create(panel);
                    else if (e.Anchor != null && (e.Anchor.transform.localPosition != e.LastAnchorPosition || Math.Abs(SliderLeftOffset(e.Slider) - e.LastSliderLeftOffset) > 0.5f))
                        Layout(e);
                }
            }
            foreach (var e in _entries.Values)
            {
                if (e.Panel == null || e.Slider == null || e.Label == null) continue;
                float scale = Math.Clamp(_get(), 0.5f, 2f);
                float value = Math.Clamp((scale - 0.5f) / 1.5f, 0f, 1f);
                if (!e.Updating && Math.Abs(e.Slider.value - value) > 0.001f)
                { e.Updating = true; e.Slider.value = value; e.Updating = false; }
                string text = $"滚轮灵敏度  {scale * 100f:0}%";
                if (e.Label.text != text) e.Label.text = text;
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
        GameObject labelObject = NGUITools.AddChild(parent, anchor.gameObject);
        var label = labelObject.GetComponent<UILabel>();
        if (label == null) return null;
        labelObject.name = "AzuriteWheelSensitivityLabel";
        // A copied font label can be wider than the fixed 185-unit offset used
        // by the first implementation. At 100% UI scale the value text then
        // reaches into the slider thumb. Keep a dedicated, bounded caption
        // column and compute the slider's left edge from the actual pivots.
        label.width = (int)Math.Clamp(Math.Max(anchor.width, MinimumLabelWidth), MinimumLabelWidth, MaximumLabelWidth);
        label.pivot = UIWidget.Pivot.Left;
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.text = "滚轮灵敏度  100%";
        var entry = new Entry { Panel = panel, Anchor = anchor, Slider = slider, Label = label };
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
    private static float LeftEdge(UIWidget widget)
    {
        float x = widget.transform.localPosition.x;
        return widget.pivot switch
        {
            UIWidget.Pivot.TopLeft or UIWidget.Pivot.Left or UIWidget.Pivot.BottomLeft => x,
            UIWidget.Pivot.Top or UIWidget.Pivot.Center or UIWidget.Pivot.Bottom => x - widget.width * 0.5f,
            _ => x - widget.width
        };
    }
    private static void Layout(Entry entry)
    {
        UILabel anchor = entry.Anchor;
        float anchorLeft = LeftEdge(anchor);
        float rowOffset = Math.Max(74f, anchor.height + 24f);
        Vector3 labelPosition = anchor.transform.localPosition;
        labelPosition.x = anchorLeft;
        labelPosition.y -= rowOffset;
        entry.Label.transform.localPosition = labelPosition;

        // Keep a real gap between the caption's right edge and the slider's
        // left edge even when the host uses a centered slider pivot.
        float labelRight = labelPosition.x + entry.Label.width;
        float sliderPositionX = labelRight + RowGap - SliderLeftOffset(entry.Slider);
        Vector3 sliderPosition = entry.Slider.transform.localPosition;
        sliderPosition.x = sliderPositionX;
        sliderPosition.y = labelPosition.y;
        entry.Slider.transform.localPosition = sliderPosition;
        entry.LastAnchorPosition = anchor.transform.localPosition;
        entry.LastSliderLeftOffset = SliderLeftOffset(entry.Slider);
    }
    private static float SliderLeftOffset(UISlider slider)
    {
        float min = 0f;
        bool found = false;
        foreach (UIWidget widget in slider.gameObject.GetComponentsInChildren<UIWidget>(true))
        {
            // The AUTO template contains UILabel tick marks. They are hidden
            // above and must not expand the slider's geometry for placement.
            if (widget is UILabel) continue;
            min = found ? Math.Min(min, LeftEdge(widget)) : LeftEdge(widget);
            found = true;
        }
        return found ? min : 0f;
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
        }
        _entries.Clear();
    }
}

