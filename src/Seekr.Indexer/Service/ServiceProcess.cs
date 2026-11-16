using System.Runtime.InteropServices;

namespace Seekr.Indexer.Service;

/// <summary>Looks up the process of a running Windows service via the Service Control Manager.</summary>
public static partial class ServiceProcess
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const int StatusProcessSize = 36;    // SERVICE_STATUS_PROCESS
    private const int ProcessIdOffset = 28;      // dwProcessId
    private const int CurrentStateOffset = 4;    // dwCurrentState
    private const int ServiceRunning = 4;

    /// <summary>Process id of service <paramref name="serviceName"/>, or null if it is missing or not running.</summary>
    public static uint? TryGetProcessId(string serviceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);

        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == 0)
        {
            return null;
        }

        try
        {
            var service = OpenService(manager, serviceName, ServiceQueryStatus);
            if (service == 0)
            {
                return null;
            }

            try
            {
                var buffer = new byte[StatusProcessSize];
                if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, buffer.Length, out _)
                    || BitConverter.ToInt32(buffer, CurrentStateOffset) != ServiceRunning)
                {
                    return null;
                }

                var pid = BitConverter.ToUInt32(buffer, ProcessIdOffset);
                return pid == 0 ? null : pid;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(nint service, int infoLevel, byte[] buffer, int bufferSize, out int bytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}
