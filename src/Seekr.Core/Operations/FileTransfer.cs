using System.Buffers;

namespace Seekr.Core.Operations;

public enum ConflictChoice { Skip, KeepBoth, Replace, Merge, Cancel }
public sealed record TransferConflict(string Source, string Destination, bool IsDirectory);
public sealed record TransferProgress(string Path, long CompletedBytes, long TotalBytes, int CompletedFiles, int TotalFiles);
public sealed record TransferFailure(string Path, string Error);
public sealed record TransferReport(IReadOnlyList<string> Completed, IReadOnlyList<TransferFailure> Failed, int Skipped)
{
    public IReadOnlyList<MoveUndo> UndoMoves { get; init; } = [];
    public string? UndoUnavailable { get; init; }
}

public static class FileTransfer
{
    private sealed record Node(string Source, string Destination, bool IsDirectory, long Length);

    public static async Task<TransferReport> ExecuteAsync(IReadOnlyList<string> sources, string destination, bool move,
        Func<TransferConflict, Task<ConflictChoice>> resolve, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(resolve);
        destination = Path.GetFullPath(destination);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException(destination);
        EnsureNoReparse(destination);
        var normalized = sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var source in normalized)
        {
            if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), Path.GetPathRoot(source)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Операции с корнем диска не поддерживаются.");
            if (Directory.Exists(source) && IsInside(destination, source)) throw new IOException("Нельзя поместить папку внутрь самой себя.");
        }
        var roots = normalized.Where(s => !normalized.Any(other => !s.Equals(other, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(other) && IsInside(s, other))).ToArray();
        var completed = new List<string>();
        var failed = new List<TransferFailure>();
        var skipped = 0;
        var undo = new List<MoveUndo>();
        string? undoUnavailable = null;
        foreach (var source in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureNoReparse(source);
                var target = Path.Combine(destination, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && move) { skipped++; continue; }
                var directory = Directory.Exists(source);
                if (!directory && !File.Exists(source)) throw new FileNotFoundException("Исходный файл недоступен", source);
                var replaceRoot = false;
                var hadConflict = File.Exists(target) || Directory.Exists(target);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    var choice = await resolve(new(source, target, directory)).WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (choice == ConflictChoice.Cancel) throw new OperationCanceledException(cancellationToken);
                    if (choice == ConflictChoice.Skip) { skipped++; continue; }
                    if (choice == ConflictChoice.KeepBoth) target = UniqueName(target);
                    else if (choice == ConflictChoice.Merge && directory && Directory.Exists(target)) { }
                    else if (directory || Directory.Exists(target)) throw new IOException("Замена папок не поддерживается. Выберите «Сохранить оба».");
                    else if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Нельзя заменить файл им самим.");
                    else replaceRoot = true;
                }

                var plan = BuildPlan(source, target, cancellationToken);
                long doneBytes = 0;
                var totalBytes = plan.Sum(x => x.Length);
                var totalFiles = plan.Count(x => !x.IsDirectory);
                var doneFiles = 0;
                var anySkipped = false;
                foreach (var node in plan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoReparse(Path.GetDirectoryName(node.Destination)!);
                    if (node.IsDirectory) { Directory.CreateDirectory(node.Destination); EnsureNoReparse(node.Destination); continue; }
                    var fileTarget = node.Destination;
                    var overwrite = replaceRoot && node.Source == source;
                    if (!overwrite && (File.Exists(fileTarget) || Directory.Exists(fileTarget)))
                    {
                        var choice = await resolve(new(node.Source, fileTarget, false)).WaitAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (choice == ConflictChoice.Cancel) throw new OperationCanceledException(cancellationToken);
                        if (choice == ConflictChoice.Skip) { skipped++; anySkipped = true; continue; }
                        if (choice == ConflictChoice.KeepBoth) fileTarget = UniqueName(fileTarget);
                        else { if (Directory.Exists(fileTarget)) throw new IOException("Файл нельзя записать поверх папки."); overwrite = true; }
                    }
                    EnsureNoReparse(fileTarget);
                    if (move)
                    {
                        // The OS moves same-volume files atomically, and handles cross-volume files.
                        // Cancellation is honored between moved files; already moved files stay at destination.
                        if (string.Equals(Path.GetPathRoot(node.Source), Path.GetPathRoot(fileTarget), StringComparison.OrdinalIgnoreCase))
                            File.Move(node.Source, fileTarget, overwrite);
                        else
                        {
                            await CopyAtomicAsync(node.Source, fileTarget, overwrite, bytes =>
                                progress?.Report(new(node.Source, doneBytes + bytes, totalBytes, doneFiles, totalFiles)), cancellationToken);
                            // The complete destination is published before deleting the source.
                            cancellationToken.ThrowIfCancellationRequested();
                            File.Delete(node.Source);
                        }
                        doneBytes += node.Length;
                    }
                    else
                    {
                        await CopyAtomicAsync(node.Source, fileTarget, overwrite, bytes =>
                            progress?.Report(new(node.Source, doneBytes + bytes, totalBytes, doneFiles, totalFiles)), cancellationToken);
                        doneBytes += node.Length;
                    }
                    doneFiles++;
                    progress?.Report(new(node.Source, doneBytes, totalBytes, doneFiles, totalFiles));
                }
                if (move)
                {
                    foreach (var folder in plan.Where(x => x.IsDirectory).Reverse())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!Directory.EnumerateFileSystemEntries(folder.Source).Any()) Directory.Delete(folder.Source, recursive: false);
                    }
                }
                if (!anySkipped)
                {
                    completed.Add(source);
                    if (move && !hadConflict && string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
                    {
                        try { undo.Add(MoveUndo.Capture(source, target, cancellationToken)); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { undoUnavailable = "Операция завершена, но снимок для отмены недоступен: " + ex.Message; }
                    }
                    else if (move) undoUnavailable = "Перемещение на другой диск, с заменой или объединением возвращается вручную.";
                }
                else if (move) undoUnavailable = "Частичное перемещение возвращается вручную через очередь операций.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { failed.Add(new(source, ex.Message)); }
        }
        return new(completed, failed, skipped) { UndoMoves = undo, UndoUnavailable = undoUnavailable };
    }

    private static List<Node> BuildPlan(string source, string destination, CancellationToken token)
    {
        var nodes = new List<Node>();
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((source, destination));
        while (pending.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            EnsureNoReparse(entry.Source);
            var directory = Directory.Exists(entry.Source);
            nodes.Add(new(entry.Source, entry.Destination, directory, directory ? 0 : new FileInfo(entry.Source).Length));
            if (!directory) continue;
            foreach (var child in Directory.EnumerateFileSystemEntries(entry.Source))
                pending.Push((child, Path.Combine(entry.Destination, Path.GetFileName(child))));
        }
        return nodes;
    }

    private static async Task CopyAtomicAsync(string source, string destination, bool overwrite, Action<long> progress, CancellationToken token)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".seekr-" + Guid.NewGuid().ToString("N") + ".partial");
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                long copied = 0;
                var lastReport = Environment.TickCount64;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), token);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    copied += read;
                    if (Environment.TickCount64 - lastReport >= 100) { progress(copied); lastReport = Environment.TickCount64; }
                }
                await output.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            EnsureNoReparse(destination);
            File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Rename(string source, string name)
    {
        if (!FileNameRules.IsValid(name))
            throw new ArgumentException("Недопустимое имя", nameof(name));
        source = Path.GetFullPath(source);
        EnsureNoReparse(source);
        var parent = Path.GetDirectoryName(source) ?? throw new IOException("Нельзя переименовать корень диска.");
        var target = Path.Combine(parent, name);
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target, overwrite: false);
    }

    private static bool IsInside(string path, string folder) => path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string UniqueName(string path)
    {
        var parent = Path.GetDirectoryName(path)!;
        var folder = Directory.Exists(path);
        var name = folder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var extension = folder ? string.Empty : Path.GetExtension(path);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(parent, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Не удалось подобрать свободное имя.");
    }

    public static void EnsureNoReparse(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try { if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)
                    && (Directory.Exists(current) ? new DirectoryInfo(current).LinkTarget : new FileInfo(current).LinkTarget) is not null) throw new IOException("Операция через ссылку или точку соединения запрещена: " + current); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
