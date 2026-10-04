// ILSpy omits this compiler metadata helper from the recovered project.
// Unity's interop CoreModule exposes a constructor-less public type with the
// same name, preventing Roslyn from synthesizing its own usable definition.
// Preserve the local helper already present in the original Azurite assembly.
namespace System.Runtime.CompilerServices;

[CompilerGenerated]
[AttributeUsage(AttributeTargets.All, Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;

    public NullableAttribute(byte flag) => NullableFlags = new[] { flag };

    public NullableAttribute(byte[] flags) => NullableFlags = flags;
}
