using System.Diagnostics;
using FluentAssertions;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public class ServiceProcessTests
{
    [Fact]
    public void TryGetProcessId_ReturnsNullForUnknownService()
    {
        ServiceProcess.TryGetProcessId("seekr-no-such-service-" + Guid.NewGuid().ToString("N")).Should().BeNull();
    }

    [Fact]
    public void TryGetProcessId_FindsRunningSystemService()
    {
        // The Windows Event Log service runs on every supported Windows version.
        var pid = ServiceProcess.TryGetProcessId("EventLog");

        pid.Should().NotBeNull();
        using var process = Process.GetProcessById((int)pid!.Value);
        process.Id.Should().Be((int)pid.Value);
    }
}
