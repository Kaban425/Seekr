using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.Core.Tests;

public class RankingTests
{
    private static readonly DateTime Time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Builder
    {
        private readonly List<FileEntry> _entries = [new(0, FileEntry.NoParent, @"C:\", true, 0, Time, Time)];

        public int Dir(int parent, string name) => Add(parent, name, isDirectory: true);

        public int File(int parent, string name) => Add(parent, name, isDirectory: false);

        public NameIndex Build() => new(_entries);

        private int Add(int parent, string name, bool isDirectory)
        {
            var id = _entries.Count;
            _entries.Add(new FileEntry(id, parent, name, isDirectory, 0, Time, Time));
            return id;
        }
    }

    private static List<string> Names(NameIndex index, string query, int limit = 10) =>
        [.. index.Search(SearchQuery.Parse(query), limit).Select(r => r.Entry.Name)];

    [Fact]
    public void ExactNameBeatsPrefixBeatsWordStartBeatsSubstring()
    {
        var b = new Builder();
        b.File(0, "mydota.txt");
        b.File(0, "my-dota.txt");
        b.File(0, "dotabuff.txt");
        b.File(0, "dota");

        Names(b.Build(), "dota").Should().Equal("dota", "dotabuff.txt", "my-dota.txt", "mydota.txt");
    }

    [Fact]
    public void NameWithoutExtensionCountsAsExact()
    {
        var b = new Builder();
        b.File(0, "notepad-plus.exe");
        b.File(0, "notepad.exe");

        Names(b.Build(), "notepad").First().Should().Be("notepad.exe");
    }

    [Fact]
    public void SystemAndCacheFoldersRankBelowUserFiles()
    {
        var b = new Builder();
        var windows = b.Dir(0, "Windows");
        var prefetch = b.Dir(windows, "Prefetch");
        b.File(prefetch, "DOTA2.EXE-2D4D4E8A.pf");
        var games = b.Dir(0, "Games");
        b.File(games, "dota2.exe");

        Names(b.Build(), "dota2").First().Should().Be("dota2.exe");
    }

    [Fact]
    public void EverythingInsideNoisyFolderIsAlsoNoisy()
    {
        var b = new Builder();
        var appData = b.Dir(0, "AppData");
        var deep = b.Dir(b.Dir(appData, "Local"), "Steam");
        b.File(deep, "steam.exe");
        b.File(b.Dir(0, "Program Files"), "steam.exe");

        var top = b.Build().Search(SearchQuery.Parse("steam.exe"), 1).Single();

        top.FullPath.Should().Be(@"C:\Program Files\steam.exe");
    }

    [Fact]
    public void StartMenuShortcutsAreNotTreatedAsNoise()
    {
        var b = new Builder();
        var startMenu = b.Dir(b.Dir(b.Dir(b.Dir(0, "AppData"), "Roaming"), "Microsoft"), "Start Menu");
        b.File(startMenu, "Steam.lnk");
        b.File(b.Dir(0, "Downloads"), "steam-setup-notes.lnk");

        Names(b.Build(), "steam").First().Should().Be("Steam.lnk");
    }

    [Fact]
    public void ProgramsBeatOtherFilesWithSameMatch()
    {
        var b = new Builder();
        b.Dir(0, "dota2");
        b.File(0, "dota2.txt");
        b.File(0, "dota2.exe");

        Names(b.Build(), "dota2").First().Should().Be("dota2.exe");
    }

    [Fact]
    public void ShallowerPathWinsTies()
    {
        var b = new Builder();
        b.File(b.Dir(b.Dir(0, "a"), "b"), "report.pdf");
        b.File(0, "report.pdf");

        b.Build().Search(SearchQuery.Parse("report"), 1).Single().FullPath.Should().Be(@"C:\report.pdf");
    }

    [Fact]
    public void LimitKeepsTheBestMatches()
    {
        var b = new Builder();
        for (var i = 0; i < 50; i++)
        {
            b.File(0, $"zz-log-{i}.txt");
        }

        b.File(0, "log.txt");

        Names(b.Build(), "log", limit: 1).Should().Equal("log.txt");
    }
}
