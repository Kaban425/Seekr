namespace Seekr.Core.Index;

/// <summary>Removes duplicate paths and descendants of selected directories.</summary>
public static class SelectionSummary
{
    public static IReadOnlyList<SearchResult> Roots(IEnumerable<SearchResult> selected)
    {
        var items = selected.DistinctBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var folders = new HashSet<string>(items.Where(x => x.Entry.IsDirectory)
            .Select(x => x.FullPath.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
        return items.Where(item =>
        {
            var path = item.FullPath.TrimEnd('\\');
            for (var slash = path.LastIndexOf('\\'); slash >= 0; slash = path.LastIndexOf('\\', slash - 1))
            {
                if (folders.Contains(path[..slash])) return false;
                if (slash == 0) break;
            }
            return true;
        }).ToArray();
    }
}
