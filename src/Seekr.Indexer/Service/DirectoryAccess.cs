using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Seekr.Indexer.Service;

/// <summary>Checks directory permissions for the current (possibly impersonated) thread token.</summary>
public static partial class DirectoryAccess
{
    private const uint FileListDirectory = 0x0001;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x0200_0000; // required to open a directory handle
    private const uint FileFlagOpenReparsePoint = 0x0020_0000; // check the junction itself, never its target
    private const string ExtendedPrefix = @"\\?\";

    /// <summary>True when the caller may list the directory's contents.</summary>
    public static bool CanList(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var handle = CreateFile(
            ToExtendedPath(path), FileListDirectory, FileShareAll, 0, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, 0);
        return !handle.IsInvalid;
    }

    /// <summary>Fails closed when the path now names a different directory from the privileged snapshot.</summary>
    public static bool CanListSnapshot(string path, ulong expectedReference)
    {
        if (expectedReference == 0) return false;
        using var handle = CreateFile(ToExtendedPath(path), FileListDirectory, FileShareAll, 0, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, 0);
        return !handle.IsInvalid && GetFileInformationByHandleEx(handle, 18, out var identity, (uint)Marshal.SizeOf<FileIdInfo>())
            && identity.Low == expectedReference && identity.High == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo { public ulong VolumeSerial; public ulong Low; public ulong High; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileIdInfo information, uint size);

    /// <summary>
    /// Adds the <c>\\?\</c> prefix so Windows uses the exact MFT name. Without it, Win32 trims
    /// trailing dots and spaces, so "secret." would be checked as the different folder "secret".
    /// </summary>
    public static string ToExtendedPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? path : ExtendedPrefix + path;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);
}
