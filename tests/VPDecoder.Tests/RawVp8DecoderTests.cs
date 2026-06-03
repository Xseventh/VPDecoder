namespace VPDecoder.Tests;

public sealed class RawVp8DecoderTests
{
    private static readonly byte[] ValidKeyFramePacket =
    [
        0x30, 0x00, 0x00,
        0x9d, 0x01, 0x2a,
        0x10, 0x00,
        0x08, 0x00,
        0x00
    ];

    [Fact]
    public void DecodeFrame_WhenKeyFrameHeaderIsValid_ReturnsUnsupportedFeatureWithHeader()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(ValidKeyFramePacket, new Vp8DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.Equal(Vp8DecodeResultStatus.Failed, result.Status);
        Assert.False(result.HasDisplayFrame);
        Assert.False(result.NoDisplayFrame);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(16, result.Header.Width);
        Assert.Equal(8, result.Header.Height);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_ReadOnlyMemoryInput_ReturnsStructuredUnsupportedFeatureWithHeader()
    {
        ReadOnlyMemory<byte> packet = ValidKeyFramePacket;
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.Equal(Vp8DecodeResultStatus.Failed, result.Status);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(16, result.Header.Width);
        Assert.Equal(8, result.Header.Height);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenPacketIsEmpty_ReturnsInvalidPacket()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([]);

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([0], new Vp8DecodeOptions(ExpectedWidth: 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedHeightIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([0], new Vp8DecodeOptions(ExpectedHeight: 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenOutputFormatIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([0], new Vp8DecodeOptions(OutputFormat: (Vp9OutputPixelFormat)99));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenInterFrameHeaderIsValid_ReturnsUnsupportedInterFrameFeature()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([0x11, 0x00, 0x00]);

        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp8FrameType.InterFrame, result.Header.FrameType);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedInterFrameFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthDiffers_ReturnsDimensionMismatch()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(ValidKeyFramePacket, new Vp8DecodeOptions(1, 8));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.DimensionMismatch, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenFrameExceedsPixelLimit_ReturnsAllocationLimitExceeded()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(ValidKeyFramePacket, new Vp8DecodeOptions(16, 8, MaxPixelCount: 1));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.AllocationLimitExceeded, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenFirstPartitionExtendsPastPacket_ReturnsTruncatedPacketWithHeader()
    {
        var packet = (byte[])ValidKeyFramePacket.Clone();
        packet[0] = 0x50;
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(2, result.Header.FirstPartitionSize);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
    }
}
