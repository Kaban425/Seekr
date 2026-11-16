using Seekr.Setup;

namespace Seekr.Setup.Tests;

public sealed class SetupTests
{
    [Fact]
    public void InstallOptionsRejectRelativeRootAndPropertyInjection()
    {
        Assert.Throws<ArgumentException>(() => SetupOptions.ValidatePath("relative"));
        Assert.Throws<ArgumentException>(() => SetupOptions.ValidatePath(@"C:\"));
        Assert.Throws<ArgumentException>(() => SetupOptions.ValidatePath("C:\\x\" REMOVE=ALL"));
        Assert.Throws<ArgumentException>(() => SetupOptions.ValidatePath("C:\\x\nREINSTALL=ALL"));
        Assert.Contains("SEEKR_DESKTOP_CHOICE=0", new SetupOptions(@"C:\Тест Seekr", false).Properties, StringComparison.Ordinal);
        Assert.Contains("SEEKR_INSTALL_DIRECTORY=\"C:\\Тест Seekr\"", new SetupOptions(@"C:\Тест Seekr", false).Properties, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressAccountsForResetIncrementAndAddedTicks()
    {
        var progress = new MsiProgress();
        progress.Update(0, 100, 0);
        progress.Update(2, 25, 0);
        Assert.Equal(25, progress.Percent);
        progress.Update(3, 100, 0);
        Assert.Equal(12.5, progress.Percent);
        progress.Update(0, 100, 1);
        progress.Update(2, 25, 0);
        Assert.Equal(75, progress.Percent);
        progress.Update(2, 999, 0);
        Assert.Equal(0, progress.Percent);
    }
}
