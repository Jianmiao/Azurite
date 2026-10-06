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
    public OnGeometryUpdated? onGeometryUpdated { get; set; }
    public OnClippingMoved? onClipMove { get; set; }

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
