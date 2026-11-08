using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;

namespace Azurite.CacheRootLab;

public static class NativeAbi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SeedRootEntry(IntPtr utf16Input, IntPtr existingRoot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr BuildPathEntry(IntPtr resultStorage, IntPtr suffixSpan, byte transform);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr ConstructStringEntry(IntPtr storage, ushort label);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DestroyStringEntry(IntPtr stringObject);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate byte CacheInitializeEntry(IntPtr nativeCacheObject, byte readIndex);

    // Compile-time qualification of the installed public interface only. This component installs no hook.
    public static Type QualifiedDetourInterface => typeof(INativeDetour);
    public static string GuardCoverage => "Every native Cache pointer at entry +0x4c6810, both readIndex=0 and readIndex=1; never an IL2CPP MethodInfo/Cache handle signature.";
}
