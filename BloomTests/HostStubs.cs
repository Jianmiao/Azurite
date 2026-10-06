// Test seams for native lifetime, setting writes and renderer discovery.
// These stubs do not model native URP execution or establish GPU savings.
namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        public IntPtr Pointer { get; set; } = new(++_nextPointer);
        public bool Destroyed { get; set; }
        public T? TryCast<T>() where T : Object => this as T;
        public static bool operator ==(Object? left, Object? right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : left.Pointer == right.Pointer;
        }
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? other) => other is Object value && this == value;
        public override int GetHashCode() => Pointer.GetHashCode();
    }
}

namespace UnityEngine.Rendering
{
    public class RenderPipelineAsset : UnityEngine.Object { }
    public static class GraphicsSettings
    {
        public static RenderPipelineAsset? currentRenderPipeline { get; set; }
    }
}

namespace UnityEngine.Rendering.Universal
{
    public class ScriptableRendererFeature : UnityEngine.Object
    {
        public bool isActive { get; set; } = true;
    }
    public class FeatureList : List<ScriptableRendererFeature?>
    {
        public int Reads;
        public new ScriptableRendererFeature? this[int index]
        {
            get { Reads++; return base[index]; }
            set => base[index] = value;
        }
    }
    public class ScriptableRendererData : UnityEngine.Object
    {
        public FeatureList? rendererFeatures { get; set; } = new();
    }
    public class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        public ScriptableRendererData? scriptableRendererData { get; set; } = new();
        public float renderScale { get; set; } = 1;
    }
}

namespace UnityEngine.SceneManagement
{
    public readonly record struct Scene(int handle);
    public static class SceneManager
    {
        public static int ActiveHandle = 1;
        public static Scene GetActiveScene() => new(ActiveHandle);
    }
}

namespace Studio.Scripts
{
    public class ScriptNodeInspector : UnityEngine.Object
    {
        public static ScriptNodeInspector? instance;
        public bool isActiveAndEnabled = true;
        public bool loading;
        public bool unloading;
    }
}

namespace Rendering
{
    public class MXBloomSettings : UnityEngine.Object
    {
        public bool Value = true;
        public int Writes;
        public bool IgnoreNextWrite;
        public bool ThrowBeforeWrite;
        public bool ThrowAfterWrite;
        public bool ThrowOnRead;
        public float Intensity = 1;
        public float Threshold = 2;
        public int Diffusion = 7;
        public bool Enable
        {
            get
            {
                if (ThrowOnRead) throw new InvalidOperationException("native get failed");
                return Value;
            }
            set
            {
                Writes++;
                if (ThrowBeforeWrite) { ThrowBeforeWrite = false; throw new InvalidOperationException("native set failed before write"); }
                if (IgnoreNextWrite) { IgnoreNextWrite = false; return; }
                Value = value;
                if (ThrowAfterWrite) { ThrowAfterWrite = false; throw new InvalidOperationException("native set failed after write"); }
            }
        }
    }
    public class UIRenderFeature : UnityEngine.Rendering.Universal.ScriptableRendererFeature
    {
        public UIRenderPipelineSettings? settings { get; set; } = new();
        public sealed class UIRenderPipelineSettings
        {
            public MXBloomSettings? BloomSettings { get; set; } = new();
            public string PassTag = "UI pass";
            public object PassSettings = new();
        }
    }
}
