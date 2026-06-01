namespace VPDecoder.Tests;

public sealed class Vp9BoolReaderTests
{
    [Fact]
    public void Constructor_WhenMarkerBitIsOne_ThrowsInvalidPacketDiagnostic()
    {
        var ex = Assert.Throws<Vp9BoolReaderException>(() => _ = new Vp9BoolReader([0xff]));

        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, ex.Diagnostic.Code);
    }

    [Fact]
    public void ReadBit_AllZeroInput_EventuallyReportsEndOfStream()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);

        for (var i = 0; i < 32; i++)
        {
            Assert.False(reader.ReadBit());
        }

        Assert.True(reader.HasError);
    }

    [Fact]
    public void Constructor_ExternalMainFrameCompressedHeader_ConsumesMarkerWhenPresent()
    {
        var path = "/tmp/vp9-main-frame-0.vp9";
        if (!File.Exists(path))
        {
            return;
        }

        var packet = File.ReadAllBytes(path);
        var header = Vp9FrameHeaderParser.Parse(packet);
        var compressedHeader = packet.AsSpan(header.HeaderSizeInBytes, header.FirstPartitionSize);

        var reader = new Vp9BoolReader(compressedHeader);
        var txMode = reader.ReadLiteral(2);

        Assert.InRange(txMode, 0, 4);
        Assert.False(reader.HasError);
    }
}
