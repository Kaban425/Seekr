using System.Globalization;
using System.Text.RegularExpressions;

namespace Seekr.Core.Search;

/// <summary>
/// Parsed search input: every term must appear in the file name, and optional filters
/// limit results by extension, size, modification date and creation date.
/// </summary>
public sealed class SearchQuery
{
    private const string ExtensionPrefix = "ext:";
    private const string SizePrefix = "size:";
    private const string DateModifiedPrefix = "dm:";
    private const string DateCreatedPrefix = "dc:";

    private SearchQuery(
        IReadOnlyList<string> terms,
        IReadOnlySet<string> extensions,
        long? sizeMin,
        long? sizeMax,
        DateTime? modifiedMin,
        DateTime? modifiedMax,
        DateTime? createdMin,
        DateTime? createdMax, bool? isDirectory)
    {
        Terms = terms;
        Extensions = extensions;
        SizeMin = sizeMin;
        SizeMax = sizeMax;
        ModifiedMin = modifiedMin;
        ModifiedMax = modifiedMax;
        CreatedMin = createdMin;
        CreatedMax = createdMax;
        IsDirectory = isDirectory;
    }

    /// <summary>Lowercase substrings that must all occur in the name.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>Lowercase extensions without the dot; empty means any.</summary>
    public IReadOnlySet<string> Extensions { get; }

    public long? SizeMin { get; }

    public long? SizeMax { get; }

    public DateTime? ModifiedMin { get; }

    public DateTime? ModifiedMax { get; }

    public DateTime? CreatedMin { get; }

    public DateTime? CreatedMax { get; }

    public string? Error { get; private init; }
    public bool? IsDirectory { get; }

    public bool IsEmpty => Terms.Count == 0
        && Extensions.Count == 0
        && SizeMin is null
        && SizeMax is null
        && ModifiedMin is null
        && ModifiedMax is null
        && CreatedMin is null
        && CreatedMax is null && IsDirectory is null;

    public static SearchQuery Parse(string? input)
    {
        string? error = null;
        var terms = new List<string>();
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        long? sizeMin = null;
        long? sizeMax = null;
        DateTime? modifiedMin = null;
        DateTime? modifiedMax = null;
        DateTime? createdMin = null;
        DateTime? createdMax = null;
        bool? isDirectory = null;

        foreach (var token in (input ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = token.ToLowerInvariant();
            if (lower.StartsWith("kind:", StringComparison.Ordinal))
            {
                if (lower is "kind:file" or "kind:files") isDirectory = false;
                else if (lower is "kind:folder" or "kind:folders") isDirectory = true;
                else error = "Тип объекта: kind:file — файлы, kind:folder — папки.";
                continue;
            }
            if (lower.StartsWith(ExtensionPrefix, StringComparison.Ordinal) && lower.Length > ExtensionPrefix.Length)
            {
                AddExtensions(lower[ExtensionPrefix.Length..], extensions);
                continue;
            }

            if (lower.StartsWith(SizePrefix, StringComparison.Ordinal) && lower.Length > SizePrefix.Length)
            {
                (sizeMin, sizeMax) = ParseSize(lower[SizePrefix.Length..]);
                if (sizeMin is null && sizeMax is null || sizeMin > sizeMax) error = "Проверьте размер: например size:>10MB";
                continue;
            }

            if (lower.StartsWith(DateModifiedPrefix, StringComparison.Ordinal) && lower.Length > DateModifiedPrefix.Length)
            {
                (modifiedMin, modifiedMax) = ParseDate(lower[DateModifiedPrefix.Length..]);
                if (modifiedMin is null && modifiedMax is null || modifiedMin > modifiedMax) error = "Проверьте дату: например dm:today или dm:2026-01-01..2026-01-31";
                continue;
            }

            if (lower.StartsWith(DateCreatedPrefix, StringComparison.Ordinal) && lower.Length > DateCreatedPrefix.Length)
            {
                (createdMin, createdMax) = ParseDate(lower[DateCreatedPrefix.Length..]);
                if (createdMin is null && createdMax is null || createdMin > createdMax) error = "Проверьте дату: например dm:today или dm:2026-01-01..2026-01-31";
                continue;
            }

            terms.Add(lower);
        }

        return new SearchQuery(terms, extensions, sizeMin, sizeMax, modifiedMin, modifiedMax, createdMin, createdMax, isDirectory) { Error = error };
    }

    private static void AddExtensions(string list, HashSet<string> target)
    {
        foreach (var raw in list.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = raw.TrimStart('.');
            if (ext.Length > 0)
            {
                target.Add(ext);
            }
        }
    }

    private static (long? Min, long? Max) ParseSize(string value)
    {
        try { return ParseSizeCore(value); }
        catch (Exception ex) when (ex is OverflowException or FormatException) { return (null, null); }
    }

    private static (long? Min, long? Max) ParseSizeCore(string value)
    {
        var rangeMatch = Regex.Match(value, @"^(?<a>[0-9.,]+)\s*(?<au>[kmgt]?b?)\.\.(?<b>[0-9.,]+)\s*(?<bu>[kmgt]?b?)$", RegexOptions.IgnoreCase);
        if (rangeMatch.Success)
        {
            var min = ParseBytes(rangeMatch.Groups["a"].Value, rangeMatch.Groups["au"].Value);
            var max = ParseBytes(rangeMatch.Groups["b"].Value, rangeMatch.Groups["bu"].Value);
            return (min, max);
        }

        var singleMatch = Regex.Match(value, @"^(?<op>[<>]?)(?<n>[0-9.,]+)\s*(?<u>[kmgt]?b?)$", RegexOptions.IgnoreCase);
        if (!singleMatch.Success)
        {
            return (null, null);
        }

        var bytes = ParseBytes(singleMatch.Groups["n"].Value, singleMatch.Groups["u"].Value);
        var op = singleMatch.Groups["op"].Value;
        return op switch
        {
            ">" => (checked(bytes + 1), null),
            "<" => (null, bytes - 1),
            _ => (bytes, bytes),
        };
    }

    private static long ParseBytes(string number, string unit)
    {
        if (!decimal.TryParse(number.Replace(",", "."), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new FormatException("Invalid size.");
        }

        var multiplier = unit.ToLowerInvariant() switch
        {
            "kb" or "k" => 1024L,
            "mb" or "m" => 1024L * 1024,
            "gb" or "g" => 1024L * 1024 * 1024,
            "tb" or "t" => 1024L * 1024 * 1024 * 1024,
            _ => 1L,
        };

        return checked((long)(value * multiplier));
    }

    private static (DateTime? Min, DateTime? Max) ParseDate(string value)
    {
        try { return ParseDateCore(value); }
        catch (ArgumentOutOfRangeException) { return (null, null); }
    }

    private static (DateTime? Min, DateTime? Max) ParseDateCore(string value)
    {
        var now = DateTime.Today;
        var start = now;
        var end = now.AddDays(1);
        switch (value)
        {
            case "today": break;
            case "yesterday": start = now.AddDays(-1); end = now; break;
            case "thisweek": start = now.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7)); end = start.AddDays(7); break;
            case "lastweek": start = now.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7) - 7); end = start.AddDays(7); break;
            case "thismonth": start = new DateTime(now.Year, now.Month, 1); end = start.AddMonths(1); break;
            case "lastmonth": start = new DateTime(now.Year, now.Month, 1).AddMonths(-1); end = start.AddMonths(1); break;
            case "thisyear": start = new DateTime(now.Year, 1, 1); end = start.AddYears(1); break;
            case "lastyear": start = new DateTime(now.Year - 1, 1, 1); end = start.AddYears(1); break;
            default:
                if (value.StartsWith('<') || value.StartsWith('>'))
                {
                    var date = ParseIsoDate(value[1..]);
                    if (date is null) return (null, null);
                    return value[0] == '<'
                        ? (null, date.Value.ToUniversalTime().AddTicks(-1))
                        : (date.Value.AddDays(1).ToUniversalTime(), null);
                }
                var parts = value.Split("..", StringSplitOptions.None);
                var first = ParseIsoDate(parts[0]);
                var last = parts.Length == 2 ? ParseIsoDate(parts[1]) : first;
                if (parts.Length > 2 || first is null || last is null || last.Value == DateTime.MaxValue.Date) return (null, null);
                start = first.Value;
                end = last.Value.AddDays(1);
                break;
        }
        return (start.ToUniversalTime(), end.ToUniversalTime().AddTicks(-1));
    }

    private static DateTime? ParseIsoDate(string value)
    {
        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return DateTime.SpecifyKind(date.Date, DateTimeKind.Local);
        }

        return null;
    }
}
