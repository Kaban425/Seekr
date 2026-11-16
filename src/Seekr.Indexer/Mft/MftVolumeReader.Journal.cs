using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Seekr.Indexer.Mft;

public static partial class MftVolumeReader
{
    private const uint FsctlQueryUsnJournal = 0x0009_00F4;
    private const uint FsctlReadUsnJournal = 0x0009_00BB;
    // Names, metadata, content sizes, links and access rights affect search and folder totals.
    private const uint SearchChangeReasons = 0x1 | 0x2 | 0x4 | 0x10 | 0x20 | 0x40 | 0x100 | 0x200 | 0x800 | 0x1000 | 0x2000 | 0x8000 | 0x10000 | 0x100000;

    /// <summary>
    /// A bounded, non-waiting journal read. Returns only a change hint, never names or file IDs.
    /// The checkpoint is captured BEFORE reading, so changes racing a refresh are replayed.
    /// Missing/reset/wrapped journals require a full refresh; this never creates a journal.
    /// </summary>
    public static unsafe JournalProbe ProbeJournal(char driveLetter, JournalCheckpoint previous,
        CancellationToken cancellationToken)
    {
        if (!char.IsAsciiLetterUpper(driveLetter) || previous.NextUsn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(driveLetter));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var volume = OpenJournalVolume(driveLetter);
        JournalData journal = default;
        int returned;
        try
        {
            returned = JournalIoControl(volume, FsctlQueryUsnJournal, null, 0, &journal, sizeof(JournalData), cancellationToken);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 1178 or 1179 or 1 or 50)
        {
            return new(JournalChangeKind.Unavailable, default);
        }

        if (returned < sizeof(JournalData) || journal.NextUsn < 0)
        {
            throw new InvalidDataException("Truncated USN journal information.");
        }

        var checkpoint = new JournalCheckpoint(journal.Id, journal.NextUsn);
        if (previous == default)
        {
            return new(JournalChangeKind.Baseline, checkpoint);
        }

        if (!previous.CanResume(journal.Id, Math.Max(journal.FirstUsn, journal.LowestValidUsn), journal.NextUsn)
            || !previous.IsWithinReadBudget(journal.NextUsn))
        {
            return new(JournalChangeKind.Changed, checkpoint);
        }

        if (previous.NextUsn == journal.NextUsn)
        {
            return new(JournalChangeKind.Unchanged, checkpoint);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var request = new JournalReadData
        {
            StartUsn = previous.NextUsn, ReasonMask = SearchChangeReasons, JournalId = journal.Id,
            // ReturnOnlyOnClose, Timeout and BytesToWaitFor stay zero: do not wait for writers.
        };
        var buffer = new byte[BufferSize];
        fixed (byte* output = buffer)
        {
            try
            {
                returned = JournalIoControl(volume, FsctlReadUsnJournal, &request, sizeof(JournalReadData),
                    output, buffer.Length, cancellationToken);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 1178 or 1179 or 1181)
            {
                return new(JournalChangeKind.Changed, default);
            }
        }

        if (returned < sizeof(long))
        {
            throw new InvalidDataException("Truncated USN change buffer.");
        }

        // Any matching record suffices; no need to parse or expose its file name.
        // If Windows stops before our captured horizon, conservatively refresh as well.
        var changed = returned > sizeof(long) || BitConverter.ToInt64(buffer) < journal.NextUsn;
        return new(changed ? JournalChangeKind.Changed : JournalChangeKind.Unchanged, checkpoint);
    }

    private static SafeFileHandle OpenJournalVolume(char letter)
    {
        const uint overlapped = 0x4000_0000;
        var handle = CreateFile($@"\\.\{letter}:", GenericRead, FileShareReadWrite, 0, OpenExisting, overlapped, 0);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error == ErrorAccessDenied) throw new UnauthorizedAccessException("Journal access requires elevation.");
        throw new Win32Exception(error, "Cannot open journal volume.");
    }

    /// <summary>
    /// Uses cancellable native I/O. Buffers and OVERLAPPED remain alive until Windows confirms
    /// completion, including on cancellation; disposing them early could corrupt process memory.
    /// </summary>
    private static unsafe int JournalIoControl(SafeFileHandle handle, uint code, void* input, int inputSize,
        void* output, int outputSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var overlapped = new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() };
        if (DeviceIoControl(handle, code, input, inputSize, output, outputSize, out var returned, (nint)(&overlapped)))
        {
            token.ThrowIfCancellationRequested();
            return returned;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != 997) throw new Win32Exception(error, "USN request failed."); // ERROR_IO_PENDING

        using var registration = token.UnsafeRegister(static state =>
        {
            _ = CancelIoEx((SafeFileHandle)state!, 0);
        }, handle);
        var succeeded = GetOverlappedResult(handle, &overlapped, out returned, true);
        error = Marshal.GetLastPInvokeError();
        token.ThrowIfCancellationRequested();
        if (!succeeded) throw new Win32Exception(error, "USN request failed.");
        return returned;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelIoEx(SafeFileHandle handle, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetOverlappedResult(SafeFileHandle handle, NativeOverlapped* overlapped,
        out int bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [StructLayout(LayoutKind.Sequential)]
    private struct JournalData
    {
        public ulong Id;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JournalReadData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong JournalId;
    }
}
