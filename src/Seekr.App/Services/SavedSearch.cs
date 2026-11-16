namespace Seekr.App.Services;

public sealed record SavedSearch(string Name, string Query, string? Path,
    bool SearchContents = false, string SortColumn = "", bool SortAscending = true);
