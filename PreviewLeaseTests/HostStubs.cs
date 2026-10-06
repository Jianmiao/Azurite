using Il2CppInterop.Runtime.InteropTypes.Arrays;

// These stubs model object lifetime, texture bindings and ownership, not GPU
// rendering or the behaviour of Unity's actual native callbacks.
namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        public IntPtr Pointer { get; } = new IntPtr(++_nextPointer);
        public string name { get; set; } = "stub";
        public bool Destroyed { get; private set; }
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.Destroyed = true; }
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

    public readonly record struct Vector3(float x, float y, float z);
    public readonly record struct Rect(float x, float y, float width, float height);
    public readonly record struct Scene(string name);
    public readonly record struct Matrix4x4(int Value);
    public class Transform : Object { public Matrix4x4 localToWorldMatrix { get; set; } }

    public class GameObject : Object
    {
        public bool activeInHierarchy { get; set; } = true;
        public int layer { get; set; }
        public Scene scene { get; set; } = new Scene("PreviewScene");
    }

    public class Component : Object
    {
        public Transform transform { get; } = new();
        public GameObject gameObject { get; } = new();
        public bool enabled { get; set; } = true;
        public bool isActiveAndEnabled => !Destroyed && enabled && gameObject.activeInHierarchy;
    }

    public class Texture : Object { }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror }
    public struct RenderTextureDescriptor
    {
        public int width, height, volumeDepth, depthBufferBits, msaaSamples, graphicsFormat;
        public bool useDynamicScale, enableRandomWrite;
        public Rendering.TextureDimension dimension;
    }

    public class RenderTexture : Texture
    {
        public static int Allocations, Releases, Blits;
        public static bool FailNextCreate;
        public RenderTextureDescriptor descriptor { get; set; }
        public int width => descriptor.width;
        public int height => descriptor.height;
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapModeU { get; set; }
        public TextureWrapMode wrapModeV { get; set; }
        public int anisoLevel { get; set; }
        public int ReleaseCalls { get; private set; }
        public bool Created { get; private set; }
        public bool IsCreated() => Created;
        public RenderTexture(RenderTextureDescriptor descriptor) { this.descriptor = descriptor; Allocations++; }
        public bool Create()
        {
            if (FailNextCreate) { FailNextCreate = false; return false; }
            return Created = true;
        }
        public void Release() { ReleaseCalls++; Releases++; Created = false; }
    }

    public class Camera : Component
    {
        public RenderTexture? targetTexture { get; set; }
        public Rect rect { get; set; } = new Rect(0, 0, 1, 1);
        public Rendering.Universal.UniversalAdditionalCameraData? Data { get; set; } = new();
        public T? GetComponent<T>() where T : class => Data as T;
        public Vector3 WorldToScreenPoint(Vector3 value) => value;
    }

    public static class Graphics
    {
        public static void Blit(RenderTexture source, RenderTexture target)
        {
            if (source == null || target == null || !target.Created) throw new InvalidOperationException("invalid blit");
            RenderTexture.Blits++;
        }
    }
}

namespace UnityEngine.Rendering
{
    public static class OnDemandRendering { public static bool willCurrentFrameRender = true; }
    public enum TextureDimension { Tex2D, Tex3D, Cube }
}

namespace UnityEngine.Rendering.Universal
{
    public enum CameraRenderType { Base, Overlay }
    public class UniversalAdditionalCameraData
    {
        public CameraRenderType renderType { get; set; } = CameraRenderType.Base;
        public List<UnityEngine.Camera> cameraStack { get; set; } = new();
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T> : Il2CppArrayBase<T> { public Il2CppReferenceArray(T[] items) : base(items) { } }
    public class Il2CppArrayBase<T>
    {
        private readonly T[] _items;
        public Il2CppArrayBase(T[] items) => _items = items;
        public int Length => _items.Length;
        public T this[int index] => _items[index];
    }
    public class Il2CppStructArray<T> : Il2CppArrayBase<T> where T : struct
    {
        public Il2CppStructArray(T[] items) : base(items) { }
    }
}

public class UITexture : UnityEngine.Component
{
    public UnityEngine.Texture? mainTexture { get; set; }
    public UnityEngine.Camera? anchorCamera { get; set; }
    public UnityEngine.Rect uvRect { get; set; } = new UnityEngine.Rect(0, 0, 1, 1);
    public bool isVisible { get; set; } = true;
    public Il2CppStructArray<UnityEngine.Vector3> worldCorners { get; set; } = new(new[]
    {
        new UnityEngine.Vector3(0, 0, 1), new UnityEngine.Vector3(0, 540, 1),
        new UnityEngine.Vector3(960, 540, 1), new UnityEngine.Vector3(960, 0, 1)
    });
}

public class UICamera
{
    public UnityEngine.Camera? cachedCamera;
    public static UICamera? FindCameraForLayer(int layer) => null;
}

public class Test : UnityEngine.Component
{
    public UIPanel? frontPanel { get; set; }
    public bool previewMode { get; set; } = true;
    public UITexture? background { get; set; }
}

namespace Studio.Scripts
{
    public class ScriptNodeInspector : UnityEngine.Component
    {
        public static ScriptNodeInspector? instance;
        public Test? preview;
        public bool loading, unloading;
        public List<UITexture> Textures { get; } = new();
        public int Enumerations;
        public Il2CppArrayBase<T> GetComponentsInChildren<T>(bool includeInactive) where T : class
        { Enumerations++; return new(Textures.OfType<T>().ToArray()); }
    }
}
