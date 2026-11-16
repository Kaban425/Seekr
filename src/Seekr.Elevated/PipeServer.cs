using System.IO.Pipes;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Seekr.Core.Index;
using Seekr.Indexer.Mft;
using Seekr.Indexer.Service;

namespace Seekr.Elevated;

/// <summary>
/// Serves one request per connection: the client sends a drive letter, the service reads that
/// volume's MFT and returns only the entries the connected user is allowed to see.
/// </summary>
/// <remarks>
/// Hardening: the pipe name is claimed with FILE_FLAG_FIRST_PIPE_INSTANCE and a listening instance
/// always exists, while clients lack the right to create instances, so no other process can serve
/// this name while the service runs. Every request has a deadline, and no client error can stop the loop.
/// </remarks>
public sealed partial class PipeServer(ILogger<PipeServer> logger) : BackgroundService
{
    private const int MaxConcurrentRequests = 2;
    private const int MaxInstances = MaxConcurrentRequests + 2; // handlers + one waiting + one listening
    private static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _slots = new(MaxConcurrentRequests);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var security = CreatePipeSecurity();
        var handlers = new List<Task>();

        // Fails if another process already owns the name; stopping is safer than serving next to it.
        var listening = CreateInstance(security, first: true);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await listening.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                var connected = listening;
                try { listening = await CreateNextInstanceAsync(security, stoppingToken).ConfigureAwait(false); }
                catch { await connected.DisposeAsync().ConfigureAwait(false); throw; }

                handlers.RemoveAll(t => t.IsCompleted);
                handlers.Add(Task.Run(() => ServeAsync(connected, stoppingToken), CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service is stopping.
        }
        finally
        {
            await listening.DisposeAsync().ConfigureAwait(false);
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        _slots.Dispose();
        base.Dispose();
    }

    private static NamedPipeServerStream CreateInstance(PipeSecurity security, bool first) =>
        NamedPipeServerStreamAcl.Create(
            IndexPipe.PipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            inBufferSize: 0, outBufferSize: 0, security);

    /// <summary>Retries with a pause when all instances are busy, instead of spinning.</summary>
    private async Task<NamedPipeServerStream> CreateNextInstanceAsync(PipeSecurity security, CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                return CreateInstance(security, first: false);
            }
            catch (IOException ex)
            {
                LogInstanceBusy(ex);
                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(RequestDeadline);
            try
            {
                await _slots.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    await HandleAsync(pipe, deadline.Token).ConfigureAwait(false);
                }
                finally
                {
                    _slots.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service is stopping.
            }
#pragma warning disable CA1031 // A misbehaving client must never stop the service.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogRequestFailed(ex);
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken deadline)
    {
        var request = new byte[1];
        if (await pipe.ReadAsync(request, deadline).ConfigureAwait(false) != 1)
        {
            return;
        }

        var letter = (char)request[0];
        var isJournalRequest = char.IsAsciiLetterLower(letter);
        if (!char.IsAsciiLetter(letter))
        {
            throw new InvalidDataException($"Invalid drive letter request {request[0]}.");
        }

        using var client = CaptureClientIdentity(pipe);
        if (isJournalRequest)
        {
            letter = char.ToUpperInvariant(letter);
            var checkpointBytes = new byte[JournalWire.CheckpointLength];
            await pipe.ReadExactlyAsync(checkpointBytes, deadline).ConfigureAwait(false);
            if (!WindowsIdentity.RunImpersonated(client.AccessToken, () => DirectoryAccess.CanList($"{letter}:\\")))
            {
                throw new UnauthorizedAccessException("Volume root is not accessible to the caller.");
            }

            var probe = MftVolumeReader.ProbeJournal(letter, JournalWire.DecodeCheckpoint(checkpointBytes), deadline);
            await pipe.WriteAsync(JournalWire.Encode(probe), deadline).ConfigureAwait(false);
            return;
        }

        var entries = MftVolumeReader.ReadVolume(letter, deadline);

        // RunAsClient impersonates one thread only, so impersonate the captured token on each worker.
        var visible = AccessFilter.Filter(
            entries,
            (path, entry) => WindowsIdentity.RunImpersonated(client.AccessToken, () => entry.ParentId == FileEntry.NoParent
                ? DirectoryAccess.CanList(path) : DirectoryAccess.CanListSnapshot(path, entry.FileReferenceNumber)),
            Math.Clamp(Environment.ProcessorCount / MaxConcurrentRequests, 1, 4), deadline);

        await IndexWire.WriteAsync(pipe, visible, deadline).ConfigureAwait(false);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var user = client.Name;
            LogServed(letter, visible.Count, entries.Count, user);
        }
    }

    /// <summary>
    /// Returns the caller's token, refusing anonymous callers and callers that only allow
    /// identification: with those we could not check permissions as the user.
    /// </summary>
    private static WindowsIdentity CaptureClientIdentity(NamedPipeServerStream pipe)
    {
        WindowsIdentity? client = null;
        try
        {
            pipe.RunAsClient(() => client = WindowsIdentity.GetCurrent(ifImpersonating: true));
        }
        catch (SecurityException ex)
        {
            throw new UnauthorizedAccessException("Anonymous clients are not served.", ex);
        }

        if (client is null || client.IsAnonymous || client.IsGuest
            || client.ImpersonationLevel < TokenImpersonationLevel.Impersonation)
        {
            client?.Dispose();
            throw new UnauthorizedAccessException("Client must allow impersonation to be served.");
        }

        return client;
    }

    /// <summary>
    /// Local interactive users may read and write but not create instances; network logons are denied;
    /// SYSTEM owns the pipe.
    /// </summary>
    private static PipeSecurity CreatePipeSecurity()
    {
        const PipeAccessRights clientRights = PipeAccessRights.ReadData | PipeAccessRights.WriteData
            | PipeAccessRights.ReadAttributes | PipeAccessRights.ReadExtendedAttributes
            | PipeAccessRights.ReadPermissions | PipeAccessRights.Synchronize;

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new PipeSecurity();
        using (var self = WindowsIdentity.GetCurrent())
        {
            // Only SYSTEM may name itself owner; when run by hand as an admin the default owner is kept.
            if (self.User == system)
            {
                security.SetOwner(system);
            }
        }

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), clientRights, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Served {Drive}: {Visible} of {Total} entries to {User}")]
    private partial void LogServed(char drive, int visible, int total, string user);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Index request failed")]
    private partial void LogRequestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "All pipe instances are busy; retrying")]
    private partial void LogInstanceBusy(Exception exception);
}
