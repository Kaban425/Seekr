using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Seekr.Core.Index;
using Seekr.Indexer.Mft;

namespace Seekr.Indexer.Service;

/// <summary>Client side of the Seekr index service pipe.</summary>
public static partial class IndexPipe
{
    /// <summary>Pipe served by the SeekrIndex Windows service.</summary>
    public const string PipeName = "Seekr.Index.v1";

    /// <summary>Windows service name of the index service.</summary>
    public const string ServiceName = "SeekrIndex";

    private const string PipePath = @"\\.\pipe\" + PipeName;
    private const uint GenericRead = 0x8000_0000;
    private const uint FileWriteData = 0x0002;          // write without FILE_APPEND_DATA (= create instance)
    private const uint OpenExisting = 3;
    private const uint SecuritySqosPresent = 0x0010_0000;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPipeBusy = 231;

    /// <summary>
    /// Asks the service for drive <paramref name="driveLetter"/>. Returns null when the service is
    /// not installed or not running, so the caller can fall back to another method.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The pipe is served by some other process.</exception>
    /// <exception cref="TimeoutException">The service did not answer within <paramref name="responseTimeout"/>.</exception>
    public static IReadOnlyList<FileEntry>? TryLoad(char driveLetter, TimeSpan connectTimeout, TimeSpan responseTimeout,
        CancellationToken cancellationToken = default)
    {
        using var pipe = ConnectVerified(connectTimeout, cancellationToken);
        if (pipe is null)
        {
            return null;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(responseTimeout);
        try
        {
            pipe.WriteAsync(new byte[] { (byte)char.ToUpperInvariant(driveLetter) }, deadline.Token).AsTask().GetAwaiter().GetResult();
            var read = Task.Run(() => IndexWire.Read(pipe, deadline.Token), deadline.Token);
            // Disposing the pipe on timeout releases any pending synchronous read.
            _ = read.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return read.WaitAsync(deadline.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The index service response deadline expired."); }
        catch (InvalidDataException ex) { throw new IndexServiceCompatibilityException(ex); }
    }

    /// <summary>Queries a fixed-size USN hint; an older service may reject this command.</summary>
    public static JournalProbe? TryProbe(char driveLetter, JournalCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        using var pipe = ConnectVerified(TimeSpan.FromSeconds(1), cancellationToken);
        if (pipe is null)
        {
            return null;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var request = new byte[1 + JournalWire.CheckpointLength];
        request[0] = (byte)char.ToLowerInvariant(driveLetter);
        JournalWire.EncodeCheckpoint(checkpoint).CopyTo(request, 1);
        pipe.WriteAsync(request, deadline.Token).AsTask().GetAwaiter().GetResult();
        var response = new byte[JournalWire.ResponseLength];
        pipe.ReadExactlyAsync(response, deadline.Token).AsTask().GetAwaiter().GetResult();
        try { return JournalWire.Decode(response); }
        catch (InvalidDataException ex) { throw new IndexServiceCompatibilityException(ex); }
    }

    private static NamedPipeClientStream? ConnectVerified(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var servicePid = ServiceProcess.TryGetProcessId(ServiceName);
        if (servicePid is null)
        {
            return null;
        }

        // A server can only impersonate us after reading from the pipe, so verify it before sending.
        var pipe = Connect(TokenImpersonationLevel.Impersonation, timeout, cancellationToken);
        if (pipe is null)
        {
            return null;
        }

        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != servicePid)
        {
            pipe.Dispose();
            throw new UnauthorizedAccessException($"Pipe {PipeName} is not served by the {ServiceName} service.");
        }
        return pipe;
    }

    /// <summary>
    /// Opens the pipe asking only for read and write-data access, which is all the service grants,
    /// and lets the server impersonate at <paramref name="level"/>. Null if nobody serves the pipe.
    /// </summary>
    public static NamedPipeClientStream? Connect(TokenImpersonationLevel level, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        var qos = SecuritySqosPresent | ((uint)ToSecurityImpersonationLevel(level) << 16);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const uint fileFlagOverlapped = 0x4000_0000;
            var handle = CreateFile(PipePath, GenericRead | FileWriteData, 0, 0, OpenExisting, qos | fileFlagOverlapped, 0);
            if (!handle.IsInvalid)
            {
                return new NamedPipeClientStream(PipeDirection.InOut, isAsync: true, isConnected: true, handle);
            }

            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            var remaining = deadline - DateTime.UtcNow;
            if (error == ErrorFileNotFound || remaining <= TimeSpan.Zero)
            {
                return null;
            }

            if (error != ErrorPipeBusy)
            {
                throw new Win32Exception(error, $"Cannot open {PipePath}.");
            }

            WaitNamedPipe(PipePath, (uint)Math.Min(100, Math.Max(1, remaining.TotalMilliseconds)));
        }
    }

    private static int ToSecurityImpersonationLevel(TokenImpersonationLevel level) => level switch
    {
        TokenImpersonationLevel.Anonymous => 0,
        TokenImpersonationLevel.Identification => 1,
        TokenImpersonationLevel.Impersonation => 2,
        TokenImpersonationLevel.Delegation => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported impersonation level."),
    };

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafePipeHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitNamedPipe(string name, uint timeoutMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
