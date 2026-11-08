// The pinned Unity interop exports a constructor-less NullableAttribute.
// Supply local compiler metadata, as the existing shipping project does.
namespace System.Runtime.CompilerServices;

[CompilerGenerated]
[AttributeUsage(AttributeTargets.All, Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;
    public NullableAttribute(byte flag) => NullableFlags = new[] { flag };
    public NullableAttribute(byte[] flags) => NullableFlags = flags;
}
