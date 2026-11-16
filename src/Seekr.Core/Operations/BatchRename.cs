using System.Globalization;

namespace Seekr.Core.Operations;

public sealed record RenameOptions(string Pattern, int Start = 1, string Find = "", string Replace = "", bool PreserveExtension = true);
public sealed record RenameChange(string Source, string Target);
public sealed record RenameRow(RenameChange Change, bool IsDirectory, long Length, DateTime ModifiedUtc, string? Error)
{
    public string OldName => Path.GetFileName(Change.Source);
    public string NewName => Path.GetFileName(Change.Target);
    public string Status => Error ?? (Change.Source == Change.Target ? "Без изменений" : "Готово");
}
public sealed record RenamePlan(IReadOnlyList<RenameRow> Rows)
{
    public bool IsValid => Rows.Count > 0 && Rows.All(row => row.Error is null);
    public bool HasChanges => Rows.Any(row => row.Change.Source != row.Change.Target);
}
public sealed record RenameResult(bool Success, IReadOnlyList<RenameChange> Changes, string? Error, IReadOnlyList<string> RecoveryPaths);

public static class BatchRename
{
    public static RenamePlan Preview(IReadOnlyList<string> paths, RenameOptions options, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);
        var sources = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sources.Length > 10000) throw new ArgumentException("За один раз можно переименовать до 10 000 элементов.", nameof(paths));
        var changes = sources.Select((source, index) =>
        {
            token.ThrowIfCancellationRequested();
            var directory = Directory.Exists(source);
            var extension = directory ? "" : Path.GetExtension(source);
            var name = directory || !options.PreserveExtension ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
            if (!string.IsNullOrEmpty(options.Find)) name = name.Replace(options.Find, options.Replace, StringComparison.OrdinalIgnoreCase);
            var target = options.Pattern.Replace("{name}", name, StringComparison.Ordinal)
                .Replace("{n}", checked(options.Start + index).ToString("D3", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{ext}", extension.TrimStart('.'), StringComparison.Ordinal);
            if (options.PreserveExtension && !directory && !options.Pattern.Contains("{ext}", StringComparison.Ordinal)) target += extension;
            // Invalid names are retained for preview without escaping the selected parent.
            return new RenameChange(source, FileNameRules.IsValid(target) && options.Start >= 0 && !target.Contains('{') && !target.Contains('}')
                ? Path.Combine(Path.GetDirectoryName(source)!, target) : Path.Combine(Path.GetDirectoryName(source)!, ":invalid-name:"));
        }).ToArray();
        return FromChanges(changes, token);
    }

    public static RenamePlan Reverse(IReadOnlyList<RenameChange> changes, CancellationToken token = default) => FromChanges(changes.Select(x => new RenameChange(x.Target, x.Source)).ToArray(), token);

    public static RenamePlan FromChanges(IReadOnlyList<RenameChange> changes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var sources = changes.Select(x => Path.GetFullPath(x.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parents = changes.Select(x => Path.GetDirectoryName(Path.GetFullPath(x.Source))).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var duplicates = changes.GroupBy(x => Path.GetFullPath(x.Target), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(changes.Select(change =>
        {
            token.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(change.Source); var target = Path.GetFullPath(change.Target);
            var directory = Directory.Exists(source);
            string? error = null;
            long length = 0; DateTime modified = default;
            try
            {
                if (parents != 1 || !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase)) error = "Выберите элементы одной папки.";
                else if (!FileNameRules.IsValid(Path.GetFileName(target))) error = "Недопустимое имя.";
                else if (duplicates.Contains(target)) error = "Имена совпадают.";
                else if ((File.Exists(target) || Directory.Exists(target)) && !sources.Contains(target)) error = "Такое имя уже существует.";
                else if (!directory && !File.Exists(source)) error = "Источник недоступен.";
                FileTransfer.EnsureNoReparse(source);
                modified = directory ? Directory.GetLastWriteTimeUtc(source) : File.GetLastWriteTimeUtc(source);
                length = directory ? 0 : new FileInfo(source).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { error = ex.Message; }
            return new RenameRow(new(source, target), directory, length, modified, error);
        }).ToArray());
    }

    public static RenameResult Execute(RenamePlan plan, CancellationToken token)
    {
        var staged = new List<(RenameRow Row, string Temporary)>();
        var published = new List<(RenameRow Row, string Temporary)>();
        try
        {
            token.ThrowIfCancellationRequested();
            if (!plan.IsValid || !FromChanges(plan.Rows.Select(x => x.Change).ToArray(), token).IsValid) throw new IOException("Предпросмотр устарел или содержит ошибки. Пересчитайте имена.");
            foreach (var row in plan.Rows)
            {
                token.ThrowIfCancellationRequested();
                FileTransfer.EnsureNoReparse(row.Change.Source);
                var modified = row.IsDirectory ? Directory.GetLastWriteTimeUtc(row.Change.Source) : File.GetLastWriteTimeUtc(row.Change.Source);
                if (modified != row.ModifiedUtc || (!row.IsDirectory && new FileInfo(row.Change.Source).Length != row.Length)) throw new IOException("Файл изменился после предпросмотра: " + row.OldName);
            }
            foreach (var row in plan.Rows.Where(x => x.Change.Source != x.Change.Target))
            {
                token.ThrowIfCancellationRequested();
                var temp = Path.Combine(Path.GetDirectoryName(row.Change.Source)!, ".seekr-rename-" + Guid.NewGuid().ToString("N"));
                Move(row.Change.Source, temp, row.IsDirectory); staged.Add((row, temp));
            }
            foreach (var item in staged)
            {
                token.ThrowIfCancellationRequested();
                FileTransfer.EnsureNoReparse(item.Row.Change.Target);
                Move(item.Temporary, item.Row.Change.Target, item.Row.IsDirectory); published.Add(item);
            }
            return new(true, staged.Select(x => x.Row.Change).ToArray(), null, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            var recovery = new List<string>();
            foreach (var item in published.AsEnumerable().Reverse())
            {
                try { Move(item.Row.Change.Target, item.Temporary, item.Row.IsDirectory); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { recovery.Add(item.Row.Change.Target); }
            }
            foreach (var item in staged.AsEnumerable().Reverse())
            {
                if (!File.Exists(item.Temporary) && !Directory.Exists(item.Temporary)) continue;
                try { Move(item.Temporary, item.Row.Change.Source, item.Row.IsDirectory); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { recovery.Add(item.Temporary); }
            }
            return new(false, [], (ex is OperationCanceledException ? "Переименование отменено." : ex.Message)
                + (recovery.Count == 0 ? " Исходные имена сохранены." : " Часть имён требует восстановления; пути указаны ниже."), recovery);
        }
    }

    private static void Move(string source, string target, bool directory)
    { if (directory) Directory.Move(source, target); else File.Move(source, target, overwrite: false); }
}
