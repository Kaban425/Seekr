using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public class IndexWireTests
{
    private static readonly FileEntry[] Sample =
    [
        new(0, FileEntry.NoParent, @"C:\", true, 0, DateTime.MinValue, DateTime.MinValue),
        new(1, 0, "Документы", true, 0, DateTime.MinValue, DateTime.MinValue),
        new(2, 1, "dota2.exe", false, 1024, DateTime.UtcNow, DateTime.UtcNow),
    ];

    [Fact]
    public void WriteThenRead_RoundTripsEntries()
    {
        using var stream = new MemoryStream();
        IndexWire.Write(stream, Sample);
        stream.Position = 0;

        IndexWire.Read(stream).Should().BeEquivalentTo(Sample, o => o.WithStrictOrdering());
    }

    [Fact]
    public void LegacyHeaderRequestsServiceUpdateAsRecoverableIoFailure()
    {
        using var stream = new MemoryStream("SKR1"u8.ToArray());
        var act = () => IndexWire.Read(stream);
        act.Should().Throw<IOException>().WithMessage("*Обновите службу*");
    }

    [Fact]
    public void Read_RejectsWrongHeader()
    {
        using var stream = new MemoryStream("NOPE"u8.ToArray());

        var act = () => IndexWire.Read(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Read_RejectsTruncatedStream()
    {
        using var full = new MemoryStream();
        IndexWire.Write(full, Sample);
        using var truncated = new MemoryStream(full.ToArray()[..^3]);

        var act = () => IndexWire.Read(truncated);

        act.Should().Throw<Exception>().Where(e => e is EndOfStreamException || e is InvalidDataException);
    }

    [Fact]
    public void Read_RejectsParentThatPointsForward()
    {
        FileEntry[] bad =
        [
            new(0, FileEntry.NoParent, @"C:\", true, 0, DateTime.MinValue, DateTime.MinValue),
            new(1, 0, "a", true, 0, DateTime.MinValue, DateTime.MinValue),
        ];
        using var stream = new MemoryStream();
        IndexWire.Write(stream, bad);
        var bytes = stream.ToArray();
        // The second entry's parent id starts right after the first entry.
        BitConverter.GetBytes(5).CopyTo(bytes, 9);

        var act = () => IndexWire.Read(new MemoryStream(bytes));

        act.Should().Throw<InvalidDataException>();
    }

    private static MemoryStream Header(int count)
    {
        var stream = new MemoryStream();
        stream.Write("SKR2"u8);
        stream.Write(BitConverter.GetBytes(count));
        return stream;
    }

    [Fact]
    public void Read_RejectsHugeNameLengthBeforeAllocating()
    {
        using var stream = Header(1);
        stream.Write(BitConverter.GetBytes(FileEntry.NoParent));
        stream.WriteByte(1);
        stream.Write([0xFF, 0xFF, 0xFF, 0xFF, 0x07]); // 7-bit encoded int.MaxValue
        stream.Position = 0;

        var act = () => IndexWire.Read(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Read_RejectsTooManyEntries()
    {
        using var stream = Header(IndexWire.MaxEntries + 1);
        stream.Position = 0;

        var act = () => IndexWire.Read(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void WriteThenRead_AcceptsLongestNtfsName()
    {
        var name = new string('я', 255);
        FileEntry[] entries =
        [
            new(0, FileEntry.NoParent, @"C:\", true, 0, DateTime.MinValue, DateTime.MinValue),
            new(1, 0, name, false, 0, DateTime.MinValue, DateTime.MinValue),
        ];
        using var stream = new MemoryStream();
        IndexWire.Write(stream, entries);
        stream.Position = 0;

        IndexWire.Read(stream)[1].Name.Should().Be(name);
    }
}
