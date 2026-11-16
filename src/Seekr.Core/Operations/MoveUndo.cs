using System.Security.Cryptography;
using System.Text;

namespace Seekr.Core.Operations;

public sealed record MoveUndo(string Original, string Current, string Fingerprint)
{
    public static MoveUndo Capture(string original, string current, CancellationToken token = default)
        => new(original, current, Stamp(current, token));

    public void Restore(CancellationToken token = default)
    {
        FileTransfer.EnsureNoReparse(Original);
        FileTransfer.EnsureNoReparse(Current);
        if (File.Exists(Original) || Directory.Exists(Original)) throw new IOException("Исходное имя уже занято. Отмена ничего не заменяет.");
        if (Stamp(Current, token) != Fingerprint) throw new IOException("Содержимое изменилось после операции. Автоматическая отмена остановлена.");
        if (!Directory.Exists(Path.GetDirectoryName(Original))) throw new IOException("Исходная папка больше не существует.");
        // Cross-volume moves require the normal transfer queue, with its progress and cancellation.
        if (!string.Equals(Path.GetPathRoot(Original), Path.GetPathRoot(Current), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Для возврата на другой диск используйте обычное перемещение.");
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(Current)) Directory.Move(Current, Original);
        else File.Move(Current, Original, overwrite: false);
    }

    private static string Stamp(string path, CancellationToken token)
    {
        FileTransfer.EnsureNoReparse(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pending = new Stack<string>(); pending.Push(path);
        var count = 0; long bytes = 0;
        while (pending.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            if (++count > FolderAnalysis.EntryLimit) throw new IOException("Слишком много файлов для безопасной автоматической отмены.");
            FileTransfer.EnsureNoReparse(entry);
            var directory = Directory.Exists(entry);
            hash.AppendData(Encoding.UTF8.GetBytes((directory ? "D:" : "F:") + Path.GetRelativePath(path, entry) + "\0"));
            if (directory)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(entry).Take(FolderAnalysis.EntryLimit + 1).OrderDescending(StringComparer.Ordinal)) pending.Push(child);
            }
            else
            {
                bytes = checked(bytes + new FileInfo(entry).Length);
                if (bytes > 64L * 1024 * 1024) throw new IOException("Автоотмена ограничена 64 МБ, чтобы не перечитывать большие данные. Верните файл обычным перемещением.");
                using var stream = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                hash.AppendData(Encoding.UTF8.GetBytes(FolderAnalysis.Fingerprint(stream, token)));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
