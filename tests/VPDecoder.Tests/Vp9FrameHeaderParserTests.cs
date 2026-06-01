namespace VPDecoder.Tests;

public sealed class Vp9FrameHeaderParserTests
{
    private static readonly byte[] MainFrameHeader =
    [
        0x82, 0x49, 0x83, 0x42, 0x10, 0xa5, 0xf0, 0x54, 0x76,
        0x04, 0x38, 0x24, 0x1c, 0x18, 0x66, 0x1c, 0x02, 0x80
    ];

    private static readonly byte[] AlphaFrameHeader =
    [
        0x82, 0x49, 0x83, 0x42, 0x00, 0xa5, 0xf0, 0x54, 0x76,
        0x12, 0x38, 0x24, 0x1c, 0x18, 0x74, 0x1c, 0x01, 0x1c
    ];

    [Fact]
    public void Parse_MainFrameHeader_ReturnsExpectedFields()
    {
        var header = Vp9FrameHeaderParser.Parse(MainFrameHeader);

        Assert.Equal(0, header.Profile);
        Assert.Equal(Vp9FrameType.KeyFrame, header.FrameType);
        Assert.True(header.ShowFrame);
        Assert.False(header.ErrorResilientMode);
        Assert.Equal(8, header.BitDepth);
        Assert.Equal(Vp9ColorSpace.Unknown, header.ColorSpace);
        Assert.Equal(Vp9ColorRange.Full, header.ColorRange);
        Assert.Equal(1, header.SubsamplingX);
        Assert.Equal(1, header.SubsamplingY);
        Assert.Equal(2656, header.Width);
        Assert.Equal(1352, header.Height);
        Assert.Equal(2656, header.RenderWidth);
        Assert.Equal(1352, header.RenderHeight);
        Assert.True(header.RefreshFrameContext);
        Assert.True(header.FrameParallelDecodingMode);
        Assert.Equal(2, header.LoopFilter.FilterLevel);
        Assert.Equal([1, 0, -1, -1], header.LoopFilter.RefDeltas);
        Assert.Equal(51, header.Quantization.BaseQIndex);
        Assert.False(header.Segmentation.Enabled);
        Assert.Equal(332, header.TileInfo.MiColumns);
        Assert.Equal(169, header.TileInfo.MiRows);
        Assert.Equal(8, header.TileInfo.TileColumns);
        Assert.Equal(1, header.TileInfo.TileRows);
        Assert.Equal(320, header.FirstPartitionSize);
        Assert.Equal(18, header.HeaderSizeInBytes);
    }

    [Fact]
    public void Parse_AlphaFrameHeader_ReturnsExpectedFields()
    {
        var header = Vp9FrameHeaderParser.Parse(AlphaFrameHeader);

        Assert.Equal(Vp9ColorRange.Studio, header.ColorRange);
        Assert.Equal(2656, header.Width);
        Assert.Equal(1352, header.Height);
        Assert.Equal(9, header.LoopFilter.FilterLevel);
        Assert.Equal(58, header.Quantization.BaseQIndex);
        Assert.Equal(142, header.FirstPartitionSize);
        Assert.Equal(8, header.TileInfo.TileColumns);
    }

    [Fact]
    public void TryParse_TruncatedHeader_ReturnsTruncatedPacketDiagnostic()
    {
        Assert.False(Vp9FrameHeaderParser.TryParse(MainFrameHeader.AsSpan(0, 4), out _, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, diagnostic.Code);
    }

    [Fact]
    public void TryParse_InvalidMarker_ReturnsInvalidPacketDiagnostic()
    {
        var packet = (byte[])MainFrameHeader.Clone();
        packet[0] = 0x00;

        Assert.False(Vp9FrameHeaderParser.TryParse(packet, out _, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, diagnostic.Code);
    }
}
