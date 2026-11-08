using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace Azurite.EditorIsolation;

internal sealed record NativeBinding(NativeMethodContract Contract, MethodInfo Method, IntPtr MethodInfo, IntPtr Entry);

internal static class NativeBindings
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal delegate bool MoveNextEntry(IntPtr instance, IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr InstanceStringEntry(IntPtr instance, IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr StaticStringEntry(IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StaticVoidEntry(IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StaticTwoStringVoidEntry(IntPtr name, IntPtr value, IntPtr methodInfo);

    internal static Dictionary<string, NativeBinding> Bind(RunPaths paths)
    {
        HostContract.VerifyFiles(paths.Install);
        using var process = Process.GetCurrentProcess();
        if (!RunPaths.Same(process.MainModule!.FileName, Path.Combine(paths.Install, "AzureArchive.exe"))) throw new InvalidOperationException("Executable is not the explicit lab installation.");
        var module = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        if (!RunPaths.Same(module.FileName, Path.Combine(paths.Install, "GameAssembly.dll"))) throw new InvalidOperationException("Loaded native module is outside the lab installation.");
        var result = new Dictionary<string, NativeBinding>();
        foreach (var spec in HostContract.Methods)
        {
            var assembly = spec.Assembly == "Assembly-CSharp" ? typeof(UserSettings).Assembly : typeof(UnityEngine.AddressableAssets.Initialization.AddressablesRuntimeProperties).Assembly;
            if (!RunPaths.Same(assembly.Location, Path.Combine(paths.Install, "BepInEx", "interop", spec.Assembly + ".dll"))) throw new InvalidOperationException("Interop was loaded from an unpinned path.");
            var type = assembly.GetType(spec.Type, throwOnError: true)!;
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            var method = type.GetMethod(spec.Method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ?? throw new MissingMethodException(spec.Type, spec.Method);
            var pointer = (IntPtr)(type.GetField(spec.PointerField, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? throw new MissingFieldException(spec.Type, spec.PointerField));
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Missing native MethodInfo: " + spec.Id);
            var entry = Marshal.ReadIntPtr(pointer);
            if (entry.ToInt64() - module.BaseAddress.ToInt64() != spec.Rva) throw new InvalidOperationException("Native RVA drift: " + spec.Id);
            result.Add(spec.Id, new(spec, method, pointer, entry));
        }
        CheckField(typeof(UserSettings), "NativeFieldInfoPtr_data", 0x28);
        CheckField(typeof(UserSettings), "NativeFieldInfoPtr_settingFilePath", 0x38);
        CheckField(typeof(UserSettings), "NativeFieldInfoPtr__cachedWorkspacePath", 0x68);
        CheckField(typeof(UserSettings._ReadUserSettings_d__43), "NativeFieldInfoPtr___1__state", 0x10);
        CheckField(typeof(UserSettings._ReadUserSettings_d__43), "NativeFieldInfoPtr___4__this", 0x20);
        return result;
    }
    private static void CheckField(Type type, string field, uint expectedOffset)
    {
        var pointer = (IntPtr)(type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? throw new MissingFieldException(type.FullName, field));
        if (pointer == IntPtr.Zero || IL2CPP.il2cpp_field_get_offset(pointer) != expectedOffset) throw new InvalidOperationException("Native field offset drift: " + field);
    }
    internal static bool InvokeMoveNext(NativeBinding method, IntPtr instance) => Marshal.GetDelegateForFunctionPointer<MoveNextEntry>(method.Entry)(instance, method.MethodInfo);
}
