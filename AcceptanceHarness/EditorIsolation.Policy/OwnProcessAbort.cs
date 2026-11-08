using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Azurite.EditorIsolation;

public static class OwnProcessAbort
{
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DoesNotReturn]
    public static void Terminate(uint code)
    {
        // The pseudo-handle refers only to this process; callers cannot supply another PID/handle.
        TerminateProcess(GetCurrentProcess(), code);
        // Terminating the current process does not return. Preserve fail-closed behavior if the API unexpectedly fails.
        Environment.FailFast("Own-process isolation abort failed: " + Marshal.GetLastWin32Error());
        throw new InvalidOperationException("Unreachable own-process termination return.");
    }
}
