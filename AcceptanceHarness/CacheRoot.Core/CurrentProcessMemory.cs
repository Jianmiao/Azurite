using System.Runtime.InteropServices;

namespace Azurite.CacheRootLab;

public sealed class CurrentProcessMemory : IBoundedMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInformation
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryInformation information, UIntPtr length);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr read);
    public byte[] Read(ulong address, int count)
    {
        Validate(address, count, executable: false);
        var bytes = new byte[count];
        if (count > 0 && (!ReadProcessMemory(GetCurrentProcess(), (IntPtr)(long)address, bytes, (UIntPtr)(uint)count, out var read) || read.ToUInt64() != (ulong)count)) throw new InvalidOperationException("Current-process copy failed or was partial.");
        return bytes;
    }
    public void RequireExecutable(ulong address, int count) => Validate(address, count, executable: true);
    public void RequireModuleImage(ulong address, int count, ulong moduleBase) => Validate(address, count, executable: false, moduleBase);
    private static void Validate(ulong address, int count, bool executable, ulong? moduleBase = null)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || IntPtr.Size != 8 || count < 0 || count > 16384) throw new InvalidOperationException("Unsupported host or oversized memory request.");
        MemoryRange.Check(address, (ulong)count);
        var current = address; var end = address + (ulong)count;
        while (current < end)
        {
            if (VirtualQuery((IntPtr)(long)current, out var info, (UIntPtr)(uint)Marshal.SizeOf<MemoryInformation>()).ToUInt64() != (ulong)Marshal.SizeOf<MemoryInformation>()) throw new InvalidOperationException("Memory mapping is unavailable.");
            var start = (ulong)info.BaseAddress.ToInt64(); var size = info.RegionSize.ToUInt64();
            MemoryRange.Check(start, size);
            if (size == 0 || start > current || start + size <= current || info.State != 0x1000 || (info.Protect & 0x100) != 0) throw new InvalidOperationException("Memory is uncommitted, guarded or invalid.");
            if (moduleBase.HasValue && ((ulong)info.AllocationBase.ToInt64() != moduleBase.Value || info.Type != 0x1000000)) throw new InvalidOperationException("Address is not part of the verified loaded module image.");
            var access = info.Protect & 0xff;
            var readable = access is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
            var code = access is 0x20 or 0x40 or 0x80;
            if (!readable || (executable && !code)) throw new InvalidOperationException("Memory protection does not admit the requested read/code use.");
            current = Math.Min(end, start + size);
        }
    }
}
