using Seekr.Elevated;

namespace Seekr.Indexer.Tests;

public sealed class PayloadDeploymentTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-deploy-" + Guid.NewGuid().ToString("N"))).FullName;
    private static readonly string[] Names = ["Seekr.Elevated.exe", "Seekr.Elevated.dll", "Seekr.Elevated.deps.json", "Seekr.Elevated.runtimeconfig.json"];

    private string Payload(string name, string content)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        foreach (var file in Names) File.WriteAllText(Path.Combine(path, file), content);
        return path;
    }

    [Fact]
    public void UninstallRemovesCurrentAndPreviousPayloadButPreservesSiblingData()
    {
        var target = Payload("installed", "current");
        var backup = Payload("installed.previous", "previous");
        var userFile = Path.Combine(_root, "user.txt"); File.WriteAllText(userFile, "keep");
        var stopped = false;
        PayloadDeployment.Uninstall(target, () => { Assert.True(Directory.Exists(target)); Assert.True(Directory.Exists(backup)); stopped = true; });
        Assert.True(stopped); Assert.False(Directory.Exists(target)); Assert.False(Directory.Exists(backup));
        Assert.Equal("keep", File.ReadAllText(userFile));
    }

    [Fact]
    public void UninstallInvalidPathDoesNotStopService()
    {
        var stopped = false;
        Assert.Throws<ArgumentException>(() => PayloadDeployment.Uninstall("invalid\0path", () => stopped = true));
        Assert.False(stopped);
    }

    [Fact]
    public void FailedStartupRestoresCompletePreviousVersion()
    {
        var source = Payload("source", "new");
        var target = Payload("installed", "old");
        var restarted = false;
        Assert.Throws<IOException>(() => PayloadDeployment.Install(source, target, () => { }, () => throw new IOException("start failed"), () => restarted = true));
        Assert.True(restarted);
        Assert.All(Names, name => Assert.Equal("old", File.ReadAllText(Path.Combine(target, name))));
        Assert.Empty(Directory.GetDirectories(_root, "*.staging-*"));
    }

    [Fact]
    public void IncompleteSourceDoesNotStopOrModifyInstalledService()
    {
        var source = Payload("source", "new");
        var target = Payload("installed", "old");
        File.Delete(Path.Combine(source, Names[0]));
        var stopped = false;
        Assert.Throws<IOException>(() => PayloadDeployment.Install(source, target, () => stopped = true, () => { }, () => { }));
        Assert.False(stopped);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, Names[0])));
    }

    [Fact]
    public void InterruptedPublicationWithMissingTargetRecoversPreviousVersion()
    {
        var source = Payload("source", "new");
        var target = Path.Combine(_root, "installed");
        Payload("installed.previous", "old");
        Assert.Throws<IOException>(() => PayloadDeployment.Install(source, target, () => { }, () => throw new IOException("start failed"), () => { }));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, Names[0])));
    }

    [Fact]
    public void SuccessPublishesWholeVersionAndRetainsOneBackup()
    {
        var source = Payload("source", "new");
        var target = Payload("installed", "old");
        PayloadDeployment.Install(source, target, () => { }, () => { }, () => { });
        Assert.All(Names, name => Assert.Equal("new", File.ReadAllText(Path.Combine(target, name))));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target + ".previous", Names[0])));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
