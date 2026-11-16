using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Seekr.Indexer.Mft;

namespace Seekr.Indexer.Tests;

public class UsnRecordParserTests
{
    private const int HeaderSize = 60;

    internal static byte[] BuildRecordV2(ulong frn, ulong parentFrn, string name, FileAttributes attributes)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        var length = (HeaderSize + nameBytes.Length + 7) & ~7; // records are 8-byte aligned
        var buffer = new byte[length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt64LittleEndian(span[8..], frn);
        BinaryPrimitives.WriteUInt64LittleEndian(span[16..], parentFrn);
        BinaryPrimitives.WriteUInt32LittleEndian(span[52..], (uint)attributes);
        BinaryPrimitives.WriteUInt16LittleEndian(span[56..], (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[58..], HeaderSize);
        nameBytes.CopyTo(span[HeaderSize..]);
        return buffer;
    }

    [Fact]
    public void Parse_ReadsSeveralRecords()
    {
        var buffer = BuildRecordV2(10, 5, "Games", FileAttributes.Directory)
            .Concat(BuildRecordV2(11, 10, "dota2.exe", FileAttributes.Archive))
            .ToArray();

        var records = UsnRecordParser.Parse(buffer);

        records.Should().Equal(
            new MftRecord(10, 5, "Games", IsDirectory: true),
            new MftRecord(11, 10, "dota2.exe", IsDirectory: false));
    }

    [Fact]
    public void Parse_KeepsNonAsciiNames()
    {
        var records = UsnRecordParser.Parse(BuildRecordV2(12, 5, "Документы", FileAttributes.Directory));

        records.Single().Name.Should().Be("Документы");
    }

    [Fact]
    public void Parse_SkipsUnsupportedVersions()
    {
        var record = BuildRecordV2(12, 5, "x", FileAttributes.Normal);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), 4);

        UsnRecordParser.Parse(record).Should().BeEmpty();
    }

    [Fact]
    public void Parse_RejectsCorruptLength()
    {
        var record = BuildRecordV2(12, 5, "x", FileAttributes.Normal);
        BinaryPrimitives.WriteUInt32LittleEndian(record, 4);

        var act = () => UsnRecordParser.Parse(record);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_EmptyBufferGivesNoRecords()
    {
        UsnRecordParser.Parse([]).Should().BeEmpty();
    }
}
