using Seekr.Core.Index;
using Seekr.Indexer.Service;

namespace Seekr.Indexer;

/// <summary>A saved index: its entries, where they came from and when.</summary>
public sealed record CachedIndex(IReadOnlyList<FileEntry> Entries, IndexSource Source, DateTime SavedUtc);

/// <summary>
/// Saves the index to disk so the next start can show results immediately while a fresh index
/// is built in the background. The file lives in the user's local profile and only holds names
/// that user could already see.
/// </summary>
public static class IndexCache
{
    private static readonly object PublicationLock = new();
    private static readonly byte[] Magic = "SKC1"u8.ToArray();

    /// <summary>%LocalAppData%\Seekr\index.bin</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "index.bin");

    /// <summary>Writes atomically: a crash mid-save leaves the previous snapshot intact.</summary>
    public static void Save(string path, CachedIndex index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(index);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(Magic);
                file.WriteByte((byte)index.Source);
                file.Write(BitConverter.GetBytes(index.SavedUtc.ToUniversalTime().Ticks));
                IndexWire.Write(file, index.Entries, cancellationToken);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (PublicationLock) { cancellationToken.ThrowIfCancellationRequested(); File.Move(temp, path, overwrite: true); }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Returns the snapshot, or null if there is none or it is unreadable.</summary>
    public static CachedIndex? TryLoad(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            {
                return null;
            }

            var source = (IndexSource)reader.ReadByte();
            if (!Enum.IsDefined(source))
            {
                return null;
            }

            var saved = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            return new CachedIndex(IndexWire.Read(file), source, saved);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or ArgumentOutOfRangeException)
        {
            // Missing, truncated or corrupt: behave as if there were no cache.
            return null;
        }
    }
}
