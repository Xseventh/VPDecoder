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
        Assert.Equal(0xff, header.RefreshFrameFlags);
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
    public void Parse_ShowExistingFrame_ReturnsReferenceIndex()
    {
        var header = Vp9FrameHeaderParser.Parse([0x8f]);

        Assert.True(header.ShowExistingFrame);
        Assert.Equal(7, header.ExistingFrameIndex);
        Assert.Equal(Vp9FrameType.InterFrame, header.FrameType);
        Assert.True(header.ShowFrame);
        Assert.Equal(0, header.RefreshFrameFlags);
        Assert.Equal(1, header.HeaderSizeInBytes);
        Assert.Equal(1, header.PacketLength);
    }

    [Fact]
    public void Parse_OrdinaryInterFrameWithExplicitSize_ReturnsReferenceFields()
    {
        var header = Vp9FrameHeaderParser.Parse(Vp9TestPackets.CreateOrdinaryInterFramePacket());

        Assert.Equal(Vp9FrameType.InterFrame, header.FrameType);
        Assert.False(header.IntraOnly);
        Assert.True(header.ShowFrame);
        Assert.False(header.ErrorResilientMode);
        Assert.Equal(0, header.ResetFrameContextMode);
        Assert.Equal(0x05, header.RefreshFrameFlags);
        Assert.Equal([0, 1, 7], header.ReferenceFrameIndices);
        Assert.Equal([false, true, false], header.ReferenceFrameSignBiases);
        Assert.Equal([false, false, false], header.FrameSizeReferenceFlags);
        Assert.Null(header.FrameSizeReferenceIndex);
        Assert.Equal(16, header.Width);
        Assert.Equal(8, header.Height);
        Assert.True(header.RenderSizeDifferent);
        Assert.Equal(10, header.RenderWidth);
        Assert.Equal(6, header.RenderHeight);
        Assert.True(header.AllowHighPrecisionMv);
        Assert.Equal(Vp9InterpolationFilter.EightTapSharp, header.InterpolationFilter);
        Assert.True(header.RefreshFrameContext);
        Assert.True(header.FrameParallelDecodingMode);
        Assert.Equal(2, header.FrameContextIndex);
        Assert.Equal(1, header.FirstPartitionSize);
    }

    [Fact]
    public void TryParse_OrdinaryInterFrameSizeFromReference_ReturnsMissingReferenceFrame()
    {
        Assert.False(
            Vp9FrameHeaderParser.TryParse(
                Vp9TestPackets.CreateOrdinaryInterFramePacket(sizeFromReference: true),
                out var header,
                out var diagnostic));

        Assert.Null(header);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, diagnostic?.Code);
    }

    [Fact]
    public void Parse_OrdinaryInterFrameSizeFromReference_UsesReferenceDimensions()
    {
        Vp9ReferenceFrameInfo?[] references = [new Vp9ReferenceFrameInfo(32, 24), null, null, null, null, null, null, null];
        var packet = Vp9TestPackets.CreateOrdinaryInterFramePacket(
            sizeFromReference: true,
            stopAfterSizeReference: false,
            tileInfoWidth: 32);

        var header = Vp9FrameHeaderParser.Parse(packet, references);

        Assert.Equal([true, false, false], header.FrameSizeReferenceFlags);
        Assert.Equal(0, header.FrameSizeReferenceIndex);
        Assert.Equal(32, header.Width);
        Assert.Equal(24, header.Height);
        Assert.Equal(10, header.RenderWidth);
        Assert.Equal(6, header.RenderHeight);
    }

    [Fact]
    public void Parse_HiddenProfile0IntraOnlyFrame_ReturnsDefaultColorAndRefreshFlags()
    {
        var header = Vp9FrameHeaderParser.Parse(Vp9TestPackets.CreateHiddenProfile0IntraOnlyFramePacket());

        Assert.Equal(Vp9FrameType.InterFrame, header.FrameType);
        Assert.True(header.IntraOnly);
        Assert.False(header.ShowFrame);
        Assert.True(header.SyncCodeValid);
        Assert.Equal(3, header.ResetFrameContextMode);
        Assert.Equal(8, header.BitDepth);
        Assert.Equal(Vp9ColorSpace.Bt601, header.ColorSpace);
        Assert.Equal(Vp9ColorRange.Studio, header.ColorRange);
        Assert.Equal(0x80, header.RefreshFrameFlags);
        Assert.Equal(16, header.Width);
        Assert.Equal(8, header.Height);
        Assert.False(header.RenderSizeDifferent);
        Assert.Equal(1, header.FrameContextIndex);
        Assert.Equal(Vp9InterpolationFilter.None, header.InterpolationFilter);
    }

    [Fact]
    public void Parse_ErrorResilientInterFrame_SkipsRefreshFrameContextBits()
    {
        var header = Vp9FrameHeaderParser.Parse(
            Vp9TestPackets.CreateOrdinaryInterFramePacket(errorResilientMode: true, frameContextIndex: 3));

        Assert.True(header.ErrorResilientMode);
        Assert.False(header.RefreshFrameContext);
        Assert.True(header.FrameParallelDecodingMode);
        Assert.Equal(3, header.FrameContextIndex);
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
