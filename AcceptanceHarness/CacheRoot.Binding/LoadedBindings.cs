using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azurite.CacheRootLab;

internal sealed class LoadedBindings
{
    private static readonly List<IntPtr> RetainedModules = new();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
    internal readonly CurrentProcessMemory Memory = new();
    internal readonly NativeStringReader Strings;
    internal readonly ulong Base;
    internal ulong Root => Add(PinContract.RootRva, NativeStringReader.MeaningfulSize);
    internal ulong ConsumerEntry => Add(0x4c6810, 1);
    internal readonly NativeAbi.SeedRootEntry Seed;
    internal readonly NativeAbi.BuildPathEntry Build;
    internal readonly NativeAbi.DestroyStringEntry Destroy;
    internal LoadedBindings(CacheProbeContext context)
    {
        PinContract.VerifyFilesAndMetadata(context.InstallRoot);
        PinContract.RequireFileHash(Path.Combine(context.InstallRoot, "AzureArchive.exe"), "260fc3ecc5f38dab5230ba624a52e495708d861196dda72486c1250760fe6eff");
        PinContract.RequireFileHash(Path.Combine(context.InstallRoot, "GameAssembly.dll"), "0776516c59811c724c450e205930546c5d102975f279e33c46c96b878fa06c8b");
        using var process = Process.GetCurrentProcess();
        var unity = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase));
        if (!ProbeAdmission.Same(unity.FileName, Path.Combine(context.InstallRoot, "UnityPlayer.dll")) || unity.ModuleMemorySize != PinContract.UnityImageSize) throw new InvalidOperationException("Loaded Unity module path/size mismatch.");
        if (!GetModuleHandleEx(4, unity.BaseAddress, out var retained) || retained != unity.BaseAddress) throw new InvalidOperationException("Cannot retain the verified loaded Unity module.");
        lock (RetainedModules) RetainedModules.Add(retained); // Intentionally retained to child exit; no hot unload/reset.
        Base = (ulong)unity.BaseAddress.ToInt64(); MemoryRange.Check(Base, PinContract.UnityImageSize); Strings = new NativeStringReader(Memory);
        var gameAssembly = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        if (!ProbeAdmission.Same(gameAssembly.FileName, Path.Combine(context.InstallRoot, "GameAssembly.dll"))) throw new InvalidOperationException("Loaded GameAssembly came from another installation.");
        Memory.RequireModuleImage(Base, 64, Base);
        var header = Memory.Read(Base, 64); var peOffset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x3c, 4));
        if (header[0] != 'M' || header[1] != 'Z' || peOffset is < 64 or > 4096) throw new InvalidOperationException("Invalid live PE header.");
        var pe = Memory.Read(Add((uint)peOffset, 84), 84);
        if (BinaryPrimitives.ReadUInt32LittleEndian(pe) != 0x4550 || BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(4)) != 0x8664 || BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(24)) != 0x20b || BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(80)) != PinContract.UnityImageSize) throw new InvalidOperationException("Live PE identity mismatch.");
        foreach (var pin in PinContract.ManagedModules)
        {
            var name = Path.GetFileNameWithoutExtension(pin.FileName);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == name);
            if (!ProbeAdmission.Same(assembly.Location, Path.Combine(context.InstallRoot, "BepInEx", "core", pin.FileName)) || assembly.ManifestModule.ModuleVersionId != pin.Mvid || assembly.GetName().Version?.ToString() != pin.Version) throw new InvalidOperationException("Loaded managed binding identity mismatch: " + name);
        }
        var dobby = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "dobby.dll", StringComparison.OrdinalIgnoreCase));
        if (!ProbeAdmission.Same(dobby.FileName, Path.Combine(context.InstallRoot, "BepInEx", "core", "dobby.dll")) || dobby.ModuleMemorySize != 0x38000) throw new InvalidOperationException("Loaded Dobby identity mismatch.");
        VerifyCode(allWindows: true);
        foreach (var (rva, size) in new[] { (PinContract.RootRva, 34), (PinContract.ManagerRva, 8), (PinContract.CrtRva, 4), (PinContract.AllocatorRva, 8), (0x1913400U, 2) }) Memory.RequireModuleImage(Add(rva, size), size, Base);
        if (!Memory.Read(Add(0x1913400, 2), 2).SequenceEqual(new byte[] { 9, 0 })) throw new InvalidOperationException("Live allocator literal mismatch.");
        Seed = Marshal.GetDelegateForFunctionPointer<NativeAbi.SeedRootEntry>((IntPtr)(long)Add(0x4d45c0, 1));
        Build = Marshal.GetDelegateForFunctionPointer<NativeAbi.BuildPathEntry>((IntPtr)(long)Add(0x4d0170, 1));
        Destroy = Marshal.GetDelegateForFunctionPointer<NativeAbi.DestroyStringEntry>((IntPtr)(long)Add(0xdfcf0, 1));
    }
    internal void VerifyCode(bool allWindows)
    {
        foreach (var window in PinContract.CodeWindows.Where(w => allWindows || w.Name is "root_copy_helper" or "root_converter" or "path_builder" or "string_constructor" or "string_destructor"))
        {
            var address = Add(window.Rva, window.Size); Memory.RequireModuleImage(address, window.Size, Base); Memory.RequireExecutable(address, window.Size); PinContract.CheckWindow(window, Memory.Read(address, window.Size));
        }
    }
    internal PreUseState Observe()
    {
        return new PreUseState(BinaryPrimitives.ReadUInt32LittleEndian(Memory.Read(Add(PinContract.CrtRva, 4), 4)), U64(PinContract.AllocatorRva), U64(PinContract.ManagerRva), Memory.Read(Root, 34).SequenceEqual(PinContract.InitialRoot), ProbeAdmission.GetCurrentThreadId());
    }
    internal ulong Manager => U64(PinContract.ManagerRva);
    private ulong U64(uint rva) => BinaryPrimitives.ReadUInt64LittleEndian(Memory.Read(Add(rva, 8), 8));
    private ulong Add(uint rva, int count)
    {
        if (count < 0 || (ulong)rva + (ulong)count > PinContract.UnityImageSize) throw new InvalidOperationException("RVA escapes pinned Unity image.");
        return checked(Base + rva);
    }
}
