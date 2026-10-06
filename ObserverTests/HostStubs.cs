using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        public IntPtr Pointer { get; } = new IntPtr(++_nextPointer);
        public string name { get; set; } = "stub";
    }

    public readonly record struct Vector2(float x, float y);
    public readonly record struct Matrix4x4(int Value);
    public sealed class GameObject : Object { public UnityEngine.SceneManagement.Scene scene { get; set; } = new(0, "Editor"); }

    public sealed class Transform : Object
    {
        private Matrix4x4 _matrix;
        public static int MatrixReads;
        public Matrix4x4 localToWorldMatrix { get { MatrixReads++; return _matrix; } set { _matrix = value; } }
    }
}

namespace UnityEngine.SceneManagement
{
    public readonly record struct Scene(int handle, string name = "Editor");
    public static class SceneManager
    {
        public static int Handle;
        public static Scene GetActiveScene() => new Scene(Handle);
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
        public static int InvocationListReads;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Delegate> GetInvocationList() { InvocationListReads++; return new(Leaves); }
        public T Cast<T>() where T : Delegate
        {
            if (this is AggregateDelegate aggregate && typeof(T) == typeof(UIPanel.OnGeometryUpdated))
                return (T)(object)new UIPanel.OnGeometryUpdated(aggregate.Leaves);
            if (this is AggregateDelegate aggregate2 && typeof(T) == typeof(UIPanel.OnClippingMoved))
                return (T)(object)new UIPanel.OnClippingMoved(aggregate2.Leaves);
            return (T)this;
        }

        public static Delegate? Combine(Delegate? first, Delegate? second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return new AggregateDelegate(first.Leaves.Concat(second.Leaves).ToArray());
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
                _ => new AggregateDelegate(leaves.ToArray())
            };
        }

        private sealed class AggregateDelegate : Delegate
        {
            internal readonly Delegate[] LeavesArray;
            public AggregateDelegate(Delegate[] leaves) => LeavesArray = leaves;
            internal override Delegate[] Leaves => LeavesArray;
        }
    }
}

public sealed class UIPanel : UnityEngine.Object
{
    public UnityEngine.GameObject gameObject { get; } = new();
    public delegate void Dummy();

    public sealed class OnGeometryUpdated : Il2CppSystem.Delegate
    {
        private readonly System.Action? _action;
        private readonly Il2CppSystem.Delegate[]? _leaves;
        public OnGeometryUpdated(System.Action action) => _action = action;
        internal OnGeometryUpdated(Il2CppSystem.Delegate[] leaves) => _leaves = leaves;
        internal override Il2CppSystem.Delegate[] Leaves => _leaves ?? new Il2CppSystem.Delegate[] { this };
        public void Invoke()
        {
            if (_leaves != null)
                foreach (var leaf in _leaves) ((OnGeometryUpdated)leaf).Invoke();
            else
                _action?.Invoke();
        }
    }

    public sealed class OnClippingMoved : Il2CppSystem.Delegate
    {
        private readonly System.Action<UIPanel>? _action;
        private readonly Il2CppSystem.Delegate[]? _leaves;
        public OnClippingMoved(System.Action<UIPanel> action) => _action = action;
        internal OnClippingMoved(Il2CppSystem.Delegate[] leaves) => _leaves = leaves;
        internal override Il2CppSystem.Delegate[] Leaves => _leaves ?? new Il2CppSystem.Delegate[] { this };
        public void Invoke(UIPanel panel)
        {
            if (_leaves != null)
                foreach (var leaf in _leaves) ((OnClippingMoved)leaf).Invoke(panel);
            else
                _action?.Invoke(panel);
        }
    }

    public static Il2CppSystem.Collections.Generic.List<UIPanel> list { get; } = new();
    public UnityEngine.Transform transform { get; } = new();
    public bool isActiveAndEnabled { get; set; } = true;
    public UnityEngine.Vector2 clipOffset { get; set; }
    public OnGeometryUpdated? onGeometryUpdated { get; set; }
    public OnClippingMoved? onClipMove { get; set; }
    public Il2CppSystem.Collections.Generic.List<UnityEngine.Object> widgets { get; } = new();
    public Il2CppSystem.Collections.Generic.List<UnityEngine.Object> drawCalls { get; } = new();
}

namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(System.Delegate action) where T : class
        {
            if (typeof(T) == typeof(UIPanel.OnGeometryUpdated))
                return (T)(object)new UIPanel.OnGeometryUpdated((Action)action);
            if (typeof(T) == typeof(UIPanel.OnClippingMoved))
                return (T)(object)new UIPanel.OnClippingMoved((Action<UIPanel>)action);
            throw new InvalidOperationException(typeof(T).FullName);
        }
    }
}
