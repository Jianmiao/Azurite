using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace UnityEngine
{
    public class Object
    {
        private static int _nextPointer;
        public IntPtr Pointer { get; } = new(Interlocked.Increment(ref _nextPointer));
    }
    public sealed class GameObject : Object { }
    public sealed class Transform : Object { public Vector3 localPosition; }
    public readonly record struct Vector2(float x, float y)
    {
        public float sqrMagnitude => x * x + y * y;
    }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public float sqrMagnitude => x * x + y * y + z * z;
    }
    public static class Input
    {
        public static bool Throw;
        public static bool MouseButtonDown;
        private static Vector2 _wheel;
        public static Vector2 mouseScrollDelta
        {
            get => Throw ? throw new InvalidOperationException("input boundary unavailable") : _wheel;
            set => _wheel = value;
        }
        public static bool GetMouseButton(int button) => Throw ? throw new InvalidOperationException("input boundary unavailable") : MouseButtonDown;
    }
    public static class ScrollProbeCounters
    {
        public static int ViewSamples;
        public static void Reset() => ViewSamples = 0;
    }
}
namespace UnityEngine.SceneManagement
{
    public readonly record struct Scene(int handle);
    public static class SceneManager
    {
        public static int Handle;
        public static Scene GetActiveScene() => new(Handle);
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(T[] items) => _items = items;
        public int Length => _items.Length;
        public T this[int index] => _items[index];
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public sealed class List<T> : System.Collections.Generic.List<T> { }
}
namespace Il2CppSystem
{
    public abstract class Delegate : UnityEngine.Object
    {
        internal abstract Delegate[] Leaves { get; }
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Delegate> GetInvocationList() => new(Leaves);
        public T Cast<T>() where T : Delegate => (T)this;
        public static Delegate? Combine(Delegate? first, Delegate? second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return new UICamera.FloatDelegate(first.Leaves.Concat(second.Leaves).Cast<UICamera.FloatDelegate>().ToArray());
        }
        public static Delegate? Remove(Delegate? combined, Delegate? own)
        {
            if (combined == null || own == null) return combined;
            var leaves = combined.Leaves.ToList();
            var index = leaves.FindLastIndex(item => item.Pointer == own.Pointer);
            if (index < 0) return combined;
            leaves.RemoveAt(index);
            return leaves.Count switch
            {
                0 => null,
                1 => leaves[0],
                _ => new UICamera.FloatDelegate(leaves.Cast<UICamera.FloatDelegate>().ToArray())
            };
        }
    }
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(System.Delegate action) where T : class
            => (T)(object)new UICamera.FloatDelegate((System.Action<UnityEngine.GameObject, float>)action);
    }
}
public static class UICamera
{
    public static FloatDelegate? onScroll;
    public sealed class FloatDelegate : Il2CppSystem.Delegate
    {
        private readonly System.Action<UnityEngine.GameObject, float>? _callback;
        private readonly FloatDelegate[]? _callbacks;
        public FloatDelegate(System.Action<UnityEngine.GameObject, float> callback) => _callback = callback;
        internal FloatDelegate(FloatDelegate[] callbacks) => _callbacks = callbacks;
        internal override Il2CppSystem.Delegate[] Leaves => _callbacks == null ? new Il2CppSystem.Delegate[] { this } : _callbacks;
        public void Invoke(UnityEngine.GameObject target, float delta)
        {
            if (_callbacks != null) foreach (var callback in _callbacks) callback.Invoke(target, delta);
            else _callback!(target, delta);
        }
    }
}
public sealed class UIPanel : UnityEngine.Object { public UnityEngine.Vector2 clipOffset; }
public class UIScrollView : UnityEngine.Object
{
    private bool _isActiveAndEnabled = true;
    private UIPanel? _panel = new();
    private UnityEngine.Transform? _transform = new();
    private UnityEngine.Vector3 _currentMomentum;
    private float _mScroll;
    private bool _isDragging;
    public bool isActiveAndEnabled { get { UnityEngine.ScrollProbeCounters.ViewSamples++; return _isActiveAndEnabled; } set => _isActiveAndEnabled = value; }
    public UIPanel? panel { get { UnityEngine.ScrollProbeCounters.ViewSamples++; return _panel; } set => _panel = value; }
    public UnityEngine.Transform? transform { get { UnityEngine.ScrollProbeCounters.ViewSamples++; return _transform; } set => _transform = value; }
    public UnityEngine.Vector3 currentMomentum { get { UnityEngine.ScrollProbeCounters.ViewSamples++; return _currentMomentum; } set => _currentMomentum = value; }
    public float mScroll { get { UnityEngine.ScrollProbeCounters.ViewSamples++; return _mScroll; } set => _mScroll = value; }
    public bool isDragging { get { UnityEngine.ScrollProbeCounters.ViewSamples++; return _isDragging; } set => _isDragging = value; }
}
public sealed class SpringPanel { public bool isActiveAndEnabled; }
public sealed class UITable { public UIScrollView? Parent; public T? GetComponentInParent<T>() where T : class => Parent as T; }
public static class Singleton<T> { public static T? Instance; }
public sealed class Catalog
{
    public Il2CppSystem.Collections.Generic.List<UIProfile>? uiProfiles = new();
    public sealed class UIProfile { public UIScrollView? scroll; }
}
namespace Studio.Scripts
{
    public sealed class CenterableUIScrollView : UIScrollView { public SpringPanel? spring; }
    public sealed class ScriptNodeInspector
    {
        public static ScriptNodeInspector? instance;
        public CenterableUIScrollView? scriptListScroll;
        public CenterableUIScrollView? characterTabScroll;
        public CenterableUIScrollView? environmentTabScroll;
    }
}
namespace UI
{
    public sealed class UIPopupModManager { public static UIPopupModManager? instance; public UIScrollView? scroll; }
}
namespace Studio.Scripts.Window.BackgroundExplorer
{
    public sealed class BackgroundExplorer { public UIScrollView? scroll; public BgSortingHierarchy? hierarchy; }
    public sealed class BgSortingHierarchy { public UITable? rootTable; }
}
