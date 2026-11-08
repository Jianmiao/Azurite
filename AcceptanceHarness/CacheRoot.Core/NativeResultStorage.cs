using System.Runtime.InteropServices;

namespace Azurite.CacheRootLab;

public sealed class NativeResultStorage : IDisposable
{
    private IntPtr allocation;
    private readonly ulong before;
    public ulong Address { get; }
    public ulong AllocationAddress => (ulong)allocation.ToInt64();
    public int AllocationBytes => 0x28 + 32 + 15;
    public NativeResultStorage()
    {
        allocation = Marshal.AllocHGlobal(AllocationBytes);
        before = ((ulong)allocation.ToInt64() + 15) & ~15UL;
        Address = before + 16;
        Marshal.Copy(Enumerable.Repeat((byte)0xa5, 16).ToArray(), 0, (IntPtr)(long)before, 16);
        // Diagnostic storage only: never a fabricated native object or pre-construction of the global.
        Marshal.Copy(Enumerable.Repeat((byte)0xcc, 0x28).ToArray(), 0, (IntPtr)(long)Address, 0x28);
        Marshal.Copy(Enumerable.Repeat((byte)0x5a, 16).ToArray(), 0, (IntPtr)(long)(Address + 0x28), 16);
    }
    public bool CanariesIntact(IBoundedMemory memory)
    {
        if (allocation == IntPtr.Zero) throw new InvalidOperationException("Result storage already released.");
        return memory.Read(before, 16).All(b => b == 0xa5) && memory.Read(Address + 0x28, 16).All(b => b == 0x5a);
    }
    public void Dispose()
    {
        if (allocation == IntPtr.Zero) return;
        // Only harness-owned opaque storage. Never free a decoded Unity data pointer here.
        Marshal.FreeHGlobal(allocation); allocation = IntPtr.Zero;
    }
}
