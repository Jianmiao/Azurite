using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Azurite.EditorIsolation;

public static class WindowsFileIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    public static void RequireSingleLink(string path)
    {
        RunPaths.RequireSafeExisting(path);
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var info) || info.LinkCount != 1) throw new InvalidOperationException("File identity is unavailable or has multiple hard links.");
    }
}
