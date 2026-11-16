using FluentAssertions;
using Seekr.Core.Index;

namespace Seekr.Indexer.Tests;

public sealed class IndexCacheTests : IDisposable
{
    private static readonly FileEntry[] Entries =
    [
        new(0, FileEntry.NoParent, @"C:\", true, 0, DateTime.MinValue, DateTime.MinValue),
        new(1, 0, "Игры", true, 0, DateTime.MinValue, DateTime.MinValue),
        new(2, 1, "dota2.exe", false, 0, DateTime.MinValue, DateTime.MinValue),
    ];

    private static readonly DateTime SavedAt = new(2026, 11, 5, 12, 30, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "seekr-cache-" + Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_dir, "index.bin");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntriesSourceAndTime()
    {
        IndexCache.Save(CachePath, new CachedIndex(Entries, IndexSource.Service, SavedAt));

        var loaded = IndexCache.TryLoad(CachePath);

        loaded.Should().NotBeNull();
        loaded!.Entries.Should().BeEquivalentTo(Entries, o => o.WithStrictOrdering());
        loaded.Source.Should().Be(IndexSource.Service);
        loaded.SavedUtc.Should().Be(SavedAt);
    }

    [Fact]
    public void Save_CreatesFolderAndLeavesNoTempFile()
    {
        IndexCache.Save(CachePath, new CachedIndex(Entries, IndexSource.UserProfile, SavedAt));

        Directory.GetFiles(_dir).Should().Equal(CachePath);
    }

    [Fact]
    public void Save_OverwritesPreviousSnapshot()
    {
        IndexCache.Save(CachePath, new CachedIndex(Entries, IndexSource.UserProfile, SavedAt));
        IndexCache.Save(CachePath, new CachedIndex(Entries[..1], IndexSource.AllDrives, SavedAt.AddHours(1)));

        IndexCache.TryLoad(CachePath)!.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void TryLoad_MissingFileGivesNull()
    {
        IndexCache.TryLoad(CachePath).Should().BeNull();
    }

    [Fact]
    public void TryLoad_CorruptFileGivesNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(CachePath, [1, 2, 3, 4, 5, 6, 7, 8, 9]);

        IndexCache.TryLoad(CachePath).Should().BeNull();
    }

    [Fact]
    public void TryLoad_TruncatedFileGivesNull()
    {
        IndexCache.Save(CachePath, new CachedIndex(Entries, IndexSource.Service, SavedAt));
        var bytes = File.ReadAllBytes(CachePath);
        File.WriteAllBytes(CachePath, bytes[..^4]);

        IndexCache.TryLoad(CachePath).Should().BeNull();
    }
}
