namespace Seekr.Core.Search;

/// <summary>How well a name matches a term; lower is better.</summary>
public enum MatchKind
{
    /// <summary>The name, or the name without its extension, equals the term.</summary>
    Exact = 0,

    /// <summary>The name starts with the term.</summary>
    Prefix = 1,

    /// <summary>The term starts a word inside the name, e.g. "dota" in "my-dota.txt".</summary>
    WordStart = 2,

    /// <summary>The term appears somewhere else in the name.</summary>
    Substring = 3,
}

/// <summary>Pure scoring helpers used to order search results.</summary>
public static class MatchRanker
{
    private static readonly HashSet<string> ProgramExtensions =
        new(["exe", "lnk", "url", "msi", "bat", "cmd", "appref-ms"], StringComparer.Ordinal);

    /// <summary>
    /// Folder names whose contents are rarely what a person is looking for: caches,
    /// system stores, dependency folders. Everything below them ranks lower.
    /// </summary>
    private static readonly HashSet<string> NoisyFolderNames = new(
        ["appdata", "winsxs", "prefetch", "$recycle.bin", "system volume information", "node_modules",
         ".git", "servicing", "softwaredistribution", "installer", "assembly", "temp", "cache", "caches"],
        StringComparer.Ordinal);

    /// <param name="lowerName">The file name in lowercase.</param>
    /// <param name="term">A lowercase term known to occur in <paramref name="lowerName"/>.</param>
    public static MatchKind Classify(string lowerName, string term)
    {
        ArgumentNullException.ThrowIfNull(lowerName);
        ArgumentNullException.ThrowIfNull(term);

        if (lowerName.Length == term.Length || IsNameWithoutExtension(lowerName, term))
        {
            return lowerName.StartsWith(term, StringComparison.Ordinal) ? MatchKind.Exact : MatchKind.Substring;
        }

        if (lowerName.StartsWith(term, StringComparison.Ordinal))
        {
            return MatchKind.Prefix;
        }

        for (var at = lowerName.IndexOf(term, StringComparison.Ordinal); at > 0;
             at = lowerName.IndexOf(term, at + 1, StringComparison.Ordinal))
        {
            if (IsSeparator(lowerName[at - 1]))
            {
                return MatchKind.WordStart;
            }
        }

        return MatchKind.Substring;
    }

    /// <summary>0 for programs and shortcuts, 1 for folders, 2 for other files.</summary>
    public static int TypeRank(bool isDirectory, string extension)
    {
        if (isDirectory)
        {
            return 1;
        }

        return ProgramExtensions.Contains(extension) ? 0 : 2;
    }

    public static bool IsNoisyFolder(string lowerName) => NoisyFolderNames.Contains(lowerName);

    /// <summary>
    /// Folders whose contents are useful even inside a noisy folder: the Start menu lives in
    /// AppData but holds the shortcuts people search for most.
    /// </summary>
    public static bool ClearsNoise(string lowerName) => lowerName is "start menu";

    private static bool IsNameWithoutExtension(string lowerName, string term)
    {
        var dot = lowerName.LastIndexOf('.');
        return dot == term.Length && lowerName.StartsWith(term, StringComparison.Ordinal);
    }

    private static bool IsSeparator(char c) => c is ' ' or '_' or '-' or '.' or '(' or '[' or '+';
}
