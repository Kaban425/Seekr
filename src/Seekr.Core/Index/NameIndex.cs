using Seekr.Core.Search;

namespace Seekr.Core.Index;

/// <summary>Immutable in-memory index of file names with ranked substring search.</summary>
public sealed class NameIndex
{
    private const int MaxDepth = 255;

    private readonly FileEntry[] _entries;
    private readonly string[] _roots;
    private readonly string[] _lowerNames;
    private readonly string[] _extensions;
    private readonly byte[] _depths;
    private readonly bool[] _inNoisyFolder;
    private readonly long[] _directorySizes;

    public NameIndex(IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Validate(entries, cancellationToken);

        _entries = [.. entries];
        _roots = _entries.Where(x => x.ParentId == FileEntry.NoParent).Select(x => x.Name).ToArray();
        _lowerNames = new string[_entries.Length];
        _extensions = new string[_entries.Length];
        var extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lookup = extensions.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var i = 0; i < _entries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = _entries[i];
            _lowerNames[i] = entry.Name.ToLowerInvariant();
            var extension = entry.IsDirectory ? ReadOnlySpan<char>.Empty : Path.GetExtension(entry.Name.AsSpan()).TrimStart('.');
            if (extension.IsEmpty) _extensions[i] = string.Empty;
            else if (lookup.TryGetValue(extension, out var known)) _extensions[i] = known;
            else { var value = extension.ToString().ToLowerInvariant(); extensions.Add(value, value); _extensions[i] = value; }
        }
        _depths = new byte[_entries.Length];
        _inNoisyFolder = new bool[_entries.Length];
        _directorySizes = new long[_entries.Length];
        ComputeLocationFacts(cancellationToken);
        ComputeDirectorySizes(cancellationToken);
    }

    public int Count => _entries.Length;
    public IReadOnlyList<string> RootPaths => Array.AsReadOnly(_roots);

    /// <summary>Read-only view of all entries, parent-first, e.g. for saving a snapshot.</summary>
    public IReadOnlyList<FileEntry> Entries => Array.AsReadOnly(_entries);

    /// <summary>
    /// Returns up to <paramref name="limit"/> matches, best first: exact name, then prefix,
    /// word start and substring; within each, programs before folders before other files,
    /// normal locations before caches and system folders, then shallower paths and shorter names.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(SearchQuery query, int limit, string? withinPath = null, Func<string, bool>? includePath = null, SearchOrder order = SearchOrder.Relevance, bool descending = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        if (query.Error is not null || query.IsEmpty || limit == 0)
        {
            return [];
        }

        var prefix = string.IsNullOrEmpty(withinPath) ? null : withinPath.TrimEnd('\\') + "\\";

        // Max-heap on rank: the worst kept match sits on top and is replaced by better ones.
        var comparer = Comparer<int>.Create((a, b) =>
        {
            var comparison = order switch
            {
                SearchOrder.Name => StringComparer.OrdinalIgnoreCase.Compare(_entries[a].Name, _entries[b].Name),
                SearchOrder.Size => GetEffectiveSize(a).CompareTo(GetEffectiveSize(b)),
                SearchOrder.Modified => _entries[a].ModifiedUtc.CompareTo(_entries[b].ModifiedUtc),
                SearchOrder.Created => _entries[a].CreatedUtc.CompareTo(_entries[b].CreatedUtc),
                SearchOrder.Type => StringComparer.OrdinalIgnoreCase.Compare(_extensions[a], _extensions[b]),
                _ => Rank(a, query).CompareTo(Rank(b, query)),
            };
            if (descending) comparison = -Math.Sign(comparison);
            return comparison == 0 ? a.CompareTo(b) : comparison;
        });
        var best = new PriorityQueue<int, int>(limit + 1, Comparer<int>.Create((a, b) => comparer.Compare(b, a)));
        for (var i = 0; i < _entries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(i, query))
            {
                continue;
            }

            if (prefix is not null || includePath is not null)
            {
                var path = GetFullPath(i);
                if (prefix is not null && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (includePath is not null && !includePath(path)) continue;
            }

            if (best.Count < limit)
            {
                best.Enqueue(i, i);
            }
            else if (best.TryPeek(out _, out var worst) && comparer.Compare(i, worst) < 0)
            {
                best.EnqueueDequeue(i, i);
            }
        }

        var ordered = new int[best.Count];
        for (var n = ordered.Length - 1; n >= 0; n--)
        {
            best.TryDequeue(out var id, out _);
            ordered[n] = id;
        }

        return [.. ordered.Select(o => new SearchResult(_entries[o], GetFullPath(o), GetEffectiveSize(o)))];
    }

    public string GetFullPath(int id)
    {
        var parts = new Stack<string>();
        for (var current = id; current != FileEntry.NoParent; current = _entries[current].ParentId)
        {
            parts.Push(_entries[current].Name);
        }

        return Path.Combine([.. parts]);
    }

    /// <summary>Packs the ordering criteria into one number; lower is better, id breaks ties.</summary>
    private long Rank(int index, SearchQuery query)
    {
        var entry = _entries[index];
        long kind = query.Terms.Count > 0 ? (long)MatchRanker.Classify(_lowerNames[index], query.Terms[0]) : 0;
        long noisy = _inNoisyFolder[index] ? 1 : 0;
        long type = MatchRanker.TypeRank(entry.IsDirectory, _extensions[index]);
        long depth = _depths[index];
        long length = Math.Min(entry.Name.Length, 255);

        // 2 bits kind | 1 bit noisy | 2 bits type | 8 bits depth | 8 bits length | 24 bits id
        return (kind << 43) | (noisy << 42) | (type << 40) | (depth << 32) | (length << 24) | (uint)(index & 0xFF_FFFF);
    }

    private bool Matches(int index, SearchQuery query)
    {
        var entry = _entries[index];
        if (query.IsDirectory is { } directory && entry.IsDirectory != directory) return false;

        if (query.Extensions.Count > 0 && !query.Extensions.Contains(_extensions[index]))
        {
            return false;
        }

        {
            var size = GetEffectiveSize(index);
            if (query.SizeMin is { } min && size < min)
            {
                return false;
            }

            if (query.SizeMax is { } max && size > max)
            {
                return false;
            }
        }

        if (query.ModifiedMin is { } modifiedMin && entry.ModifiedUtc < modifiedMin)
        {
            return false;
        }

        if (query.ModifiedMax is { } modifiedMax && entry.ModifiedUtc > modifiedMax)
        {
            return false;
        }

        if (query.CreatedMin is { } createdMin && entry.CreatedUtc < createdMin)
        {
            return false;
        }

        if (query.CreatedMax is { } createdMax && entry.CreatedUtc > createdMax)
        {
            return false;
        }

        var name = _lowerNames[index];
        for (var termIndex = 0; termIndex < query.Terms.Count; termIndex++)
        {
            if (!name.Contains(query.Terms[termIndex], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Parents precede children, so one forward pass can inherit from the parent.</summary>
    private void ComputeLocationFacts(CancellationToken token)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var parent = _entries[i].ParentId;
            if (parent == FileEntry.NoParent)
            {
                continue;
            }

            _depths[i] = (byte)Math.Min(_depths[parent] + 1, MaxDepth);
            var parentName = _lowerNames[parent];
            _inNoisyFolder[i] = !MatchRanker.ClearsNoise(parentName)
                && (_inNoisyFolder[parent]
                    || (_entries[parent].ParentId != FileEntry.NoParent && MatchRanker.IsNoisyFolder(parentName)));
        }
    }

    /// <summary>Computes recursive sizes for directories by summing child file sizes bottom-up.</summary>
    private void ComputeDirectorySizes(CancellationToken token)
    {
        for (var i = _entries.Length - 1; i >= 0; i--)
        {
            token.ThrowIfCancellationRequested();
            var entry = _entries[i];
            if (!entry.IsDirectory)
            {
                _directorySizes[i] = entry.Size;
            }

            if (entry.ParentId != FileEntry.NoParent)
            {
                _directorySizes[entry.ParentId] += _directorySizes[i];
            }
        }
    }

    /// <summary>Returns the file size or, for directories, the recursive content size.</summary>
    public long GetEffectiveSize(int id) => _entries[id].IsDirectory ? _directorySizes[id] : _entries[id].Size;

    private static void Validate(IReadOnlyList<FileEntry> entries, CancellationToken token)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            if (entry.Id != i)
            {
                throw new ArgumentException($"Entry at position {i} has id {entry.Id}.", nameof(entries));
            }

            if (entry.ParentId != FileEntry.NoParent && (entry.ParentId < 0 || entry.ParentId >= i))
            {
                throw new ArgumentException($"Entry {i} has parent {entry.ParentId} that is not an earlier entry.", nameof(entries));
            }
        }
    }
}
