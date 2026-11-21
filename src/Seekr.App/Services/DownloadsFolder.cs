using System.Runtime.InteropServices;

namespace Seekr.App.Services;

internal static class DownloadsFolder
{
    private static readonly Guid FolderId = new("374DE290-123F-4565-9164-39C4925E467B");
    private const uint DontVerify = 0x4000;

    public static string? GetPath()
    {
        var result = SHGetKnownFolderPath(in FolderId, DontVerify, IntPtr.Zero, out var path);
        try { return result == 0 ? Marshal.PtrToStringUni(path) : null; }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid folder, uint flags, IntPtr token, out IntPtr path);
}
