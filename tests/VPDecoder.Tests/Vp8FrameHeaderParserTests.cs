namespace VPDecoder.Tests;

public sealed class Vp8FrameHeaderParserTests
{
    [Fact]
    public void Parse_KeyFrameHeader_ReturnsExpectedFields()
    {
        var header = Vp8FrameHeaderParser.Parse(CreateKeyFramePacket());

        Assert.Equal(Vp8FrameType.KeyFrame, header.FrameType);
        Assert.Equal(0, header.Version);
        Assert.True(header.ShowFrame);
        Assert.Equal(1, header.FirstPartitionSize);
        Assert.True(header.SyncCodeValid);
        Assert.Equal(16, header.Width);
        Assert.Equal(8, header.Height);
        Assert.Equal(0, header.HorizontalScale);
        Assert.Equal(0, header.VerticalScale);
        Assert.Equal(10, header.HeaderSizeInBytes);
        Assert.Equal(11, header.PacketLength);
    }

    [Fact]
    public void Parse_KeyFrameHeader_ExtractsScaleBits()
    {
        var packet = CreateKeyFramePacket(rawWidth: (2 << 14) | 16, rawHeight: (1 << 14) | 8);

        var header = Vp8FrameHeaderParser.Parse(packet);

        Assert.Equal(16, header.Width);
        Assert.Equal(8, header.Height);
        Assert.Equal(2, header.HorizontalScale);
        Assert.Equal(1, header.VerticalScale);
    }

    [Fact]
    public void Parse_InterFrameHeader_ReturnsFrameTagFieldsOnly()
    {
        var header = Vp8FrameHeaderParser.Parse([0x11, 0x00, 0x00]);

        Assert.Equal(Vp8FrameType.InterFrame, header.FrameType);
        Assert.Equal(0, header.Version);
        Assert.True(header.ShowFrame);
        Assert.Equal(0, header.FirstPartitionSize);
        Assert.False(header.SyncCodeValid);
        Assert.Equal(0, header.Width);
        Assert.Equal(0, header.Height);
        Assert.Equal(3, header.HeaderSizeInBytes);
    }

    [Fact]
    public void TryParse_TruncatedFrameTag_ReturnsTruncatedPacket()
    {
        Assert.False(Vp8FrameHeaderParser.TryParse([0x30, 0x00], out var header, out var diagnostic));

        Assert.Null(header);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    [Fact]
    public void TryParse_TruncatedKeyFrameHeader_ReturnsTruncatedPacket()
    {
        Assert.False(Vp8FrameHeaderParser.TryParse([0x30, 0x00, 0x00], out var header, out var diagnostic));

        Assert.Null(header);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    [Fact]
    public void TryParse_InvalidKeyFrameSync_ReturnsInvalidPacket()
    {
        var packet = CreateKeyFramePacket();
        packet[3] = 0x00;

        Assert.False(Vp8FrameHeaderParser.TryParse(packet, out var header, out var diagnostic));

        Assert.Null(header);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidPacket, diagnostic?.Code);
    }

    private static byte[] CreateKeyFramePacket(int rawWidth = 16, int rawHeight = 8)
    {
        return
        [
            0x30, 0x00, 0x00,
            0x9d, 0x01, 0x2a,
            (byte)(rawWidth & 0xff), (byte)(rawWidth >> 8),
            (byte)(rawHeight & 0xff), (byte)(rawHeight >> 8),
            0x00
        ];
    }
}
