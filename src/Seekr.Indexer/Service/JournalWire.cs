using System.Buffers.Binary;
using Seekr.Indexer.Mft;

namespace Seekr.Indexer.Service;

/// <summary>Fixed-size USN change hints. Raw journal records never cross the pipe.</summary>
public static class JournalWire
{
    public const int CheckpointLength = 16;
    public const int ResponseLength = 21;

    public static byte[] EncodeCheckpoint(JournalCheckpoint checkpoint)
    {
        var bytes = new byte[CheckpointLength];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, checkpoint.JournalId);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), checkpoint.NextUsn);
        return bytes;
    }

    public static JournalCheckpoint DecodeCheckpoint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != CheckpointLength || BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]) < 0)
        {
            throw new InvalidDataException("Invalid journal checkpoint.");
        }

        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]));
    }

    public static byte[] Encode(JournalProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var bytes = new byte[ResponseLength];
        "SKJ1"u8.CopyTo(bytes);
        bytes[4] = (byte)probe.Kind;
        EncodeCheckpoint(probe.Checkpoint).CopyTo(bytes, 5);
        return bytes;
    }

    public static JournalProbe Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ResponseLength || !bytes[..4].SequenceEqual("SKJ1"u8)
            || bytes[4] > (byte)JournalChangeKind.Unavailable)
        {
            throw new InvalidDataException("Invalid journal response.");
        }

        return new((JournalChangeKind)bytes[4], DecodeCheckpoint(bytes[5..]));
    }
}
