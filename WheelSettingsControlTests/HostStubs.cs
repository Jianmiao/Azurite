using System.Reflection;

// These doubles exercise the production control's object construction, ownership,
// callbacks and geometry. They do not simulate NGUI font shaping or GPU drawing.
namespace UnityEngine
{
    public class Object
    {
        private static long _next;
        public readonly IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _next);
        public static readonly List<Object> Registry = new();
        public bool Destroyed;
        public Object() => Registry.Add(this);
        public static T[] FindObjectsOfType<T>() => Registry.OfType<T>().Where(x => x is not Object obj || !obj.Destroyed).ToArray();
        public static void Destroy(Object? value)
        {
            if (value == null) return;
            value.Destroyed = true;
            if (value is GameObject go)
            {
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.Components) component.Destroyed = true;
                go.transform.SetParent(null);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public string name { get => gameObject.name; set => gameObject.name = value; }
        public T? GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        private Transform? _parent;
        public readonly List<Transform> Children = new();
        public Transform? parent { get => _parent; set => SetParent(value); }
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 position => (_parent?.position ?? Vector3.zero) + localPosition;
        public Vector3 InverseTransformPoint(Vector3 point) => point - position;
        public Vector3 TransformPoint(Vector3 point) => point + position;
        public void SetParent(Transform? next, bool worldPositionStays = false)
        {
            _parent?.Children.Remove(this); _parent = next; next?.Children.Add(this);
        }
    }
    public class GameObject : Object
    {
        public string name;
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public readonly List<Component> Components = new();
        public Transform transform { get; }
        public GameObject(string name = "GameObject")
        { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public GameObject(string name, params Type[] types) : this(name) { foreach (var t in types) AddComponent(t); }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type)
        { var component = (Component)Activator.CreateInstance(type)!; component.gameObject = this; Components.Add(component); return component; }
        public T? GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault(c => c is not Object obj || !obj.Destroyed);
        public T[] GetComponents<T>() => Components.OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false)
        {
            if (!includeInactive && !activeInHierarchy) return Array.Empty<T>();
            return Components.OfType<T>().Concat(transform.Children.SelectMany(x => x.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }
        public void SetActive(bool value) => activeSelf = value;
        internal GameObject Clone(Transform? parent)
        {
            var copy = new GameObject(name) { layer = layer, activeSelf = activeSelf };
            copy.transform.SetParent(parent);
            copy.transform.localPosition = transform.localPosition;
            copy.transform.localScale = transform.localScale;
            foreach (var source in Components.Where(x => x is not Transform))
            {
                var destination = copy.AddComponent(source.GetType());
                for (Type? type = source.GetType(); type != null && type != typeof(Component); type = type.BaseType)
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        var value = field.GetValue(source);
                        if (value is UIRect.AnchorPoint anchor) value = new UIRect.AnchorPoint { target = anchor.target, relative = anchor.relative, absolute = anchor.absolute };
                        if (value is List<EventDelegate.Callback> callbacks) value = new List<EventDelegate.Callback>(callbacks);
                        field.SetValue(destination, value);
                    }
                if (source is UISlider sourceSlider && destination is UISlider copySlider) copySlider.SetWithoutNotify(sourceSlider.value);
            }
            foreach (var child in transform.Children) child.gameObject.Clone(copy.transform);
            return copy;
        }
    }
    public readonly record struct Vector2(float x, float y);
    public record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public readonly record struct Quaternion(float x, float y, float z, float w) { public static Quaternion identity => new(0, 0, 0, 1); }
    public record struct Color(float r, float g, float b, float a = 1)
    { public static Color black => new(0, 0, 0); public static Color white => new(1, 1, 1); }
    public class Font : Object { }
    public class Material : Object { }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public static class Time { public static float unscaledTime; }
    public static class Mathf
    {
        public static int RoundToInt(float x) => (int)MathF.Round(x);
        public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Min(float a, float b) => Math.Min(a,b);
        public static float Max(float a, float b) => Math.Max(a,b);
    }
}
public class UIRect : UnityEngine.MonoBehaviour
{
    public sealed class AnchorPoint { public UnityEngine.Transform? target; public float relative; public int absolute; }
    public AnchorPoint leftAnchor = new(), rightAnchor = new(), topAnchor = new(), bottomAnchor = new();
    public enum AnchorUpdate { OnEnable, OnUpdate, OnStart }
    public AnchorUpdate updateAnchors;
    public void ResetAnchors() { }
}
public class UIWidget : UIRect
{
    public enum Pivot { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }
    public int width = 100, height = 100, depth;
    public Pivot pivot = Pivot.Center;
    public UnityEngine.Color color = UnityEngine.Color.black;
    public UnityEngine.Material? material;
    public UnityEngine.Vector3[] worldCorners
    {
        get
        {
            float x = pivot is Pivot.TopLeft or Pivot.Left or Pivot.BottomLeft ? 0 : pivot is Pivot.TopRight or Pivot.Right or Pivot.BottomRight ? -width : -width * 0.5f;
            float y = pivot is Pivot.BottomLeft or Pivot.Bottom or Pivot.BottomRight ? 0 : pivot is Pivot.TopLeft or Pivot.Top or Pivot.TopRight ? -height : -height * 0.5f;
            return new[] { transform.TransformPoint(new(x, y, 0)), transform.TransformPoint(new(x, y + height, 0)), transform.TransformPoint(new(x + width, y + height, 0)), transform.TransformPoint(new(x + width, y, 0)) };
        }
    }
    public void MarkAsChanged() { }
}
public static class NGUIText { public enum Alignment { Automatic, Left, Center, Right, Justified } }
public class INGUIFont : UnityEngine.Object { }
public class UIFont : INGUIFont { }
public class UILabel : UIWidget
{
    public enum Overflow { ShrinkContent, ClampContent, ResizeFreely, ResizeHeight }
    public enum Effect { None, Shadow, Outline, Outline8 }
    public virtual string text { get; set; } = "";
    public UnityEngine.Font? trueTypeFont;
    public UnityEngine.Font? dynamicFont;
    public INGUIFont? bitmapFont;
    public UnityEngine.Object? ambigiousFont;
    public INGUIFont? font;
    public int fontSize = 16, maxLineCount, spacingX, spacingY;
    public float floatSpacingX, floatSpacingY;
    public bool useFloatSpacing, supportEncoding = true, applyGradient;
    public UnityEngine.Color gradientTop, gradientBottom, effectColor;
    public UnityEngine.Vector2 effectDistance;
    public UnityEngine.FontStyle fontStyle;
    public NGUIText.Alignment alignment;
    public Effect effectStyle;
    public Overflow overflowMethod;
    public bool overflowEllipsis;
    public void ProcessText(bool legacyMode = false, bool full = true) { }
}
namespace UI { public class LocalizedUILabel : UILabel { } }
public class FontAdapter : UnityEngine.MonoBehaviour { }
public class UISlider : UnityEngine.MonoBehaviour
{
    private float _value;
    public List<EventDelegate.Callback> onChange = new();
    public float value { get => _value; set { _value = value; foreach (var callback in onChange.ToArray()) callback(); } }
    public UIWidget? backgroundWidget, foregroundWidget;
    public UnityEngine.Transform? thumb;
    internal void SetWithoutNotify(float value) => _value = value;
}
public static class EventDelegate
{
    public delegate void Callback();
    public static void Add(List<Callback> list, Callback callback) => list.Add(callback);
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    { public static T? ConvertDelegate<T>(Action action) where T : Delegate => (T)(object)new EventDelegate.Callback(action); }
}
public static class NGUITools
{
    public static UnityEngine.GameObject AddChild(UnityEngine.Transform parent, UnityEngine.GameObject prefab) => prefab.Clone(parent);
    public static UnityEngine.GameObject AddChild(UnityEngine.GameObject parent) { var child = new UnityEngine.GameObject(); child.transform.SetParent(parent.transform); return child; }
    public static UnityEngine.GameObject AddChild(UnityEngine.GameObject parent, UnityEngine.GameObject prefab) => prefab.Clone(parent.transform);
    public static T AddWidget<T>(UnityEngine.GameObject parent) where T : UIWidget, new() => AddChild(parent).AddComponent<T>();
}
public class SettingPanel : UnityEngine.MonoBehaviour
{
    public sealed class SettingWidgets { public UILabel fontNameLabel = null!; public UISlider autoDelaySlider = null!; }
    public SettingWidgets widgets = new();
}
