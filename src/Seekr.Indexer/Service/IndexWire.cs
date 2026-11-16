using System.Text;
using Seekr.Core.Index;

namespace Seekr.Indexer.Service;

/// <summary>
/// Binary format for sending an index over the service pipe:
/// "SKR2", int32 count, then per entry: int32 parent id, byte is-directory,
/// length-prefixed UTF-8 name, int64 size, int64 modified ticks, int64 created ticks.
/// Readers treat the data as untrusted and validate every field.
/// </summary>
public static class IndexWire
{
    /// <summary>Upper bound on entries in one response (a full NTFS volume rarely has 10M).</summary>
    public const int MaxEntries = 20_000_000;

    // NTFS names are at most 255 UTF-16 units, i.e. at most 765 UTF-8 bytes.
    private const int MaxNameBytes = 1024;
    private const int BufferSize = 1 << 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Magic = "SKR2"u8.ToArray();

    public static void Write(Stream stream, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);

        cancellationToken.ThrowIfCancellationRequested();
        // Pipes are unbuffered: without this every field would be its own system call.
        // The buffer is flushed below and deliberately not disposed, which would close the stream.
        var buffered = new BufferedStream(stream, BufferSize);
        using var writer = new BinaryWriter(buffered, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(writer, entry);
        }

        writer.Flush();
    }

    private static void WriteEntry(BinaryWriter writer, FileEntry entry)
    {
        writer.Write(entry.ParentId);
        writer.Write(entry.IsDirectory);
        writer.Write(entry.Name);
        writer.Write(entry.Size);
        writer.Write(entry.ModifiedUtc.ToUniversalTime().Ticks);
        writer.Write(entry.CreatedUtc.ToUniversalTime().Ticks);
    }

    /// <summary>Streams bounded chunks and applies the request deadline to every pipe write.</summary>
    public static async Task WriteAsync(Stream stream, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream(BufferSize + MaxNameBytes);
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(writer, entry);
            if (buffer.Length < BufferSize) continue;
            await stream.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken).ConfigureAwait(false);
            buffer.SetLength(0);
        }
        if (buffer.Length > 0) await stream.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<FileEntry> Read(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        cancellationToken.ThrowIfCancellationRequested();
        var buffered = new BufferedStream(stream, BufferSize);
        using var reader = new BinaryReader(buffered, Encoding.UTF8, leaveOpen: true);
        var header = reader.ReadBytes(Magic.Length);
        if (header.AsSpan().SequenceEqual("SKR1"u8)) throw new IndexServiceCompatibilityException();
        if (!header.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a Seekr index stream.");
        }

        var count = reader.ReadInt32();
        if (count is < 0 or > MaxEntries)
        {
            throw new InvalidDataException($"Implausible entry count {count}.");
        }

        var entries = new List<FileEntry>(Math.Min(count, 1_000_000));
        for (var id = 0; id < count; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = reader.ReadInt32();
            var directoryFlag = reader.ReadByte();
            if (directoryFlag > 1) throw new InvalidDataException("Invalid directory flag.");
            var isDirectory = directoryFlag == 1;
            var name = ReadName(reader, id);
            var size = reader.ReadInt64();
            var modifiedTicks = reader.ReadInt64();
            var createdTicks = reader.ReadInt64();
            if (size < 0 || modifiedTicks < 0 || modifiedTicks > DateTime.MaxValue.Ticks || createdTicks < 0 || createdTicks > DateTime.MaxValue.Ticks)
                throw new InvalidDataException("Invalid size or timestamp.");
            var modified = new DateTime(modifiedTicks, DateTimeKind.Utc);
            var created = new DateTime(createdTicks, DateTimeKind.Utc);

            if (parent != FileEntry.NoParent && (parent < 0 || parent >= id))
            {
                throw new InvalidDataException($"Entry {id} has invalid parent {parent}.");
            }

            if (name.Contains('\0') || (parent == FileEntry.NoParent
                ? !isDirectory || !Path.IsPathFullyQualified(name)
                : !entries[parent].IsDirectory || name is "." or ".." || name.IndexOfAny(['\\', '/', ':']) >= 0))
                throw new InvalidDataException("Invalid entry name or parent type.");
            entries.Add(new FileEntry(id, parent, name, isDirectory, size, modified, created));
        }

        return entries;
    }

    /// <summary>Reads a length-prefixed UTF-8 name, checking the length before allocating.</summary>
    private static string ReadName(BinaryReader reader, int id)
    {
        int length;
        try
        {
            length = reader.Read7BitEncodedInt();
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"Entry {id} has a corrupt name length.", ex);
        }

        if (length is <= 0 or > MaxNameBytes)
        {
            throw new InvalidDataException($"Entry {id} has an invalid name length {length}.");
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }

        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid UTF-8 name.", ex); }
    }
}
