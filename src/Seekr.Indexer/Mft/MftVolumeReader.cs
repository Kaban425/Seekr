using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Seekr.Core.Index;

namespace Seekr.Indexer.Mft;

/// <summary>
/// Reads every file name on an NTFS volume straight from the master file table using
/// <c>FSCTL_ENUM_USN_DATA</c>. Seconds instead of minutes, but requires administrator rights.
/// </summary>
public static partial class MftVolumeReader
{
    private const uint GenericRead = 0x8000_0000;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint FsctlEnumUsnData = 0x0009_00B3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorHandleEof = 38;
    private const int BufferSize = 1 << 20;

    /// <summary>Returns parent-first entries for drive <paramref name="driveLetter"/>, e.g. 'C'.</summary>
    /// <exception cref="UnauthorizedAccessException">The process is not elevated.</exception>
    /// <exception cref="NotSupportedException">The drive is not NTFS.</exception>
    public static IReadOnlyList<FileEntry> ReadVolume(char driveLetter, CancellationToken cancellationToken)
    {
        if (!char.IsAsciiLetter(driveLetter))
        {
            throw new ArgumentOutOfRangeException(nameof(driveLetter), driveLetter, "Expected a drive letter.");
        }

        var rootName = $"{char.ToUpperInvariant(driveLetter)}:\\";
        var format = new DriveInfo(rootName).DriveFormat;
        if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"{rootName} is {format}, not NTFS.");
        }

        using var volume = OpenVolume(rootName[0]);
        return MftTreeBuilder.Build(rootName, EnumerateRecords(volume, cancellationToken), cancellationToken: cancellationToken);
    }

    private static SafeFileHandle OpenVolume(char letter)
    {
        var handle = CreateFile($@"\\.\{letter}:", GenericRead, FileShareReadWrite, 0, OpenExisting, 0, 0);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error == ErrorAccessDenied)
        {
            throw new UnauthorizedAccessException("Reading the MFT requires administrator rights.");
        }

        throw new Win32Exception(error, $"Cannot open volume {letter}:.");
    }

    private static List<MftRecord> EnumerateRecords(SafeFileHandle volume, CancellationToken cancellationToken)
    {
        var records = new List<MftRecord>();
        var buffer = new byte[BufferSize];
        var request = new MftEnumDataV0 { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = long.MaxValue };

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadChunk(volume, ref request, buffer, out var returned))
            {
                return records;
            }

            // The first 8 bytes hold the reference number to continue from.
            request.StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0);
            records.AddRange(UsnRecordParser.Parse(buffer.AsSpan(sizeof(ulong), returned - sizeof(ulong))));
        }
    }

    private static unsafe bool TryReadChunk(SafeFileHandle volume, ref MftEnumDataV0 request, byte[] buffer, out int returned)
    {
        fixed (MftEnumDataV0* input = &request)
        fixed (byte* output = buffer)
        {
            if (DeviceIoControl(volume, FsctlEnumUsnData, input, sizeof(MftEnumDataV0), output, buffer.Length, out returned, 0))
            {
                return returned > sizeof(ulong);
            }
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == ErrorHandleEof)
        {
            return false;
        }

        throw new Win32Exception(error, "FSCTL_ENUM_USN_DATA failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftEnumDataV0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, void* inBuffer, int inBufferSize,
        void* outBuffer, int outBufferSize, out int bytesReturned, nint overlapped);
}
