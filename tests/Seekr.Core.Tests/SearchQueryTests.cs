using FluentAssertions;
using Seekr.Core.Search;

namespace Seekr.Core.Tests;

public class SearchQueryTests
{
    [Fact]
    public void Parse_SplitsTextIntoLowercaseTerms()
    {
        var query = SearchQuery.Parse("  Dota   Steam ");

        query.Terms.Should().Equal("dota", "steam");
        query.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReadsExtensionFilterWithSeveralValues()
    {
        var query = SearchQuery.Parse("notes ext:MD;.txt");

        query.Terms.Should().Equal("notes");
        query.Extensions.Should().BeEquivalentTo("md", "txt");
    }

    [Fact]
    public void Parse_EmptyInputGivesEmptyQuery()
    {
        var query = SearchQuery.Parse("   ");

        query.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_EmptyExtensionFilterIsTreatedAsTerm()
    {
        var query = SearchQuery.Parse("ext:");

        query.Terms.Should().Equal("ext:");
        query.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReadsCreatedDateFilter()
    {
        var query = SearchQuery.Parse("dc:today");

        query.Terms.Should().BeEmpty();
        query.CreatedMin.Should().NotBeNull();
        query.CreatedMax.Should().NotBeNull();
    }

    [Fact]
    public void Parse_CreatedDateRangeUsesIsoDates()
    {
        var query = SearchQuery.Parse("dc:2026-01-01..2026-01-31");

        query.CreatedMin.Should().Be(new DateTime(2026, 1, 1).ToUniversalTime());
        query.CreatedMax.Should().Be(new DateTime(2026, 2, 1).ToUniversalTime().AddTicks(-1));
    }
}
