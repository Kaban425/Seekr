using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public sealed partial class DirectoryIdentityTests
{
    [Fact]
    public void ReplacingReadableDirectoryAtSamePathDoesNotAuthorizeOldSnapshot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-identity-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        try
        {
            ulong reference;
            using (var handle = CreateFile(path, 1, 7, 0, 3, 0x02000000, 0))
            {
                Assert.False(handle.IsInvalid);
                Assert.True(GetFileInformationByHandle(handle, out var information));
                reference = ((ulong)information.IndexHigh << 32) | information.IndexLow;
            }
            Assert.True(DirectoryAccess.CanListSnapshot(path, reference));
            var renamed = Path.Combine(root, "renamed");
            Directory.Move(path, renamed);
            Directory.CreateDirectory(path);
            Assert.True(DirectoryAccess.CanList(path));
            Assert.False(DirectoryAccess.CanListSnapshot(path, reference));
            Assert.True(DirectoryAccess.CanListSnapshot(renamed, reference));
        }
        finally { Directory.Delete(root, true); }
    }

    [StructLayout(LayoutKind.Explicit, Size = 52)]
    private struct NativeInformation
    {
        [FieldOffset(44)] public uint IndexHigh;
        [FieldOffset(48)] public uint IndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out NativeInformation information);
}
