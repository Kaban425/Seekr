using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Seekr.Indexer.Mft;

/// <summary>
/// Parses <c>USN_RECORD_V2</c> structures returned by <c>FSCTL_ENUM_USN_DATA</c>.
/// Layout: https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2
/// </summary>
public static class UsnRecordParser
{
    private const int MinRecordLength = 60;
    private const ushort SupportedMajorVersion = 2;

    public static IReadOnlyList<MftRecord> Parse(ReadOnlySpan<byte> buffer)
    {
        var records = new List<MftRecord>();
        var offset = 0;

        while (buffer.Length - offset >= sizeof(uint))
        {
            var span = buffer[offset..];
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
            if (length == 0)
            {
                break;
            }

            if (length < MinRecordLength || length > span.Length)
            {
                throw new InvalidDataException($"Corrupt USN record at offset {offset}: length {length}.");
            }

            if (BinaryPrimitives.ReadUInt16LittleEndian(span[4..]) == SupportedMajorVersion)
            {
                records.Add(ReadV2(span[..length], offset));
            }

            offset += length;
        }

        return records;
    }

    private static MftRecord ReadV2(ReadOnlySpan<byte> record, int offset)
    {
        var frn = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
        var parentFrn = BinaryPrimitives.ReadUInt64LittleEndian(record[16..]);
        var attributes = (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(record[52..]);
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[56..]);
        int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[58..]);

        if (nameOffset + nameLength > record.Length || nameLength % sizeof(char) != 0)
        {
            throw new InvalidDataException($"Corrupt file name in USN record at offset {offset}.");
        }

        var name = new string(MemoryMarshal.Cast<byte, char>(record.Slice(nameOffset, nameLength)));
        return new MftRecord(frn, parentFrn, name, attributes.HasFlag(FileAttributes.Directory));
    }
}
