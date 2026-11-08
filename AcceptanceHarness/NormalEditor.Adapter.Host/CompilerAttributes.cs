// Same local compiler metadata shim used by the frozen probe: pinned Unity
// interop exposes a constructor-less NullableAttribute. This invokes no host code.
namespace System.Runtime.CompilerServices;

[CompilerGenerated]
[AttributeUsage(AttributeTargets.All,Inherited=false)]
internal sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;
    public NullableAttribute(byte flag)=>NullableFlags=new[]{flag};
    public NullableAttribute(byte[] flags)=>NullableFlags=flags;
}
