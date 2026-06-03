namespace VPDecoder.Tests;

public sealed class RawVp8DecoderTests
{
    private const int ValidFirstPartitionSize = 160;

    private static readonly byte[] ValidKeyFramePacket = CreateValidKeyFramePacket(
        ValidFirstPartitionSize,
        width: 16,
        height: 16,
        tokenPartitionBytes: 32);

    [Fact]
    public void DecodeFrame_WhenKeyFrameUsesSupportedDcOnlySubset_ReturnsDecodedFrameWithHeader()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(ValidKeyFramePacket, new Vp8DecodeOptions(16, 16));

        Assert.True(result.Succeeded);
        Assert.Equal(Vp8DecodeResultStatus.DecodedFrame, result.Status);
        Assert.True(result.HasDisplayFrame);
        Assert.False(result.NoDisplayFrame);
        Assert.NotNull(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(16, result.Header.Width);
        Assert.Equal(16, result.Header.Height);
        Assert.Null(result.Diagnostic);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(16 * 16 * 4, result.Frame.Pixels.Length);
        for (var i = 3; i < result.Frame.Pixels.Length; i += 4)
        {
            Assert.Equal(255, result.Frame.Pixels[i]);
        }
    }

    [Fact]
    public void DecodeFrame_ReadOnlyMemoryInput_ReturnsDecodedFrameWithHeader()
    {
        ReadOnlyMemory<byte> packet = ValidKeyFramePacket;
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 16));

        Assert.True(result.Succeeded);
        Assert.Equal(Vp8DecodeResultStatus.DecodedFrame, result.Status);
        Assert.NotNull(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(16, result.Header.Width);
        Assert.Equal(16, result.Header.Height);
        Assert.Null(result.Diagnostic);
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

        var result = decoder.DecodeFrame(ValidKeyFramePacket, new Vp8DecodeOptions(1, 16));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.DimensionMismatch, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenFrameExceedsPixelLimit_ReturnsAllocationLimitExceeded()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(ValidKeyFramePacket, new Vp8DecodeOptions(16, 16, MaxPixelCount: 1));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.AllocationLimitExceeded, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenFirstPartitionExtendsPastPacket_ReturnsTruncatedPacketWithHeader()
    {
        var packet = CreateValidKeyFramePacket(firstPartitionSize: 2_064, emittedFirstPartitionBytes: ValidFirstPartitionSize);
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(2064, result.Header.FirstPartitionSize);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenKeyFrameFirstPartitionIsEmpty_ReturnsTruncatedPacketWithHeader()
    {
        var packet = new byte[]
        {
            0x10, 0x00, 0x00,
            0x9d, 0x01, 0x2a,
            0x10, 0x00,
            0x08, 0x00
        };
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.Equal(Vp8DecodeResultStatus.Failed, result.Status);
        Assert.NotNull(result.Header);
        Assert.Equal(0, result.Header.FirstPartitionSize);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenTokenPartitionIsEmpty_ReturnsTruncatedPacketWithHeader()
    {
        var packet = CreateValidKeyFramePacket(ValidFirstPartitionSize, width: 16, height: 16);
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 16));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
        Assert.Contains("token partition is empty", result.Diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeFrame_WhenKeyFrameMacroblockIsClipped_ReturnsDecodedVisibleFrame()
    {
        var packet = CreateValidKeyFramePacket(
            ValidFirstPartitionSize,
            width: 16,
            height: 8,
            tokenPartitionBytes: 32);
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame(packet, new Vp8DecodeOptions(16, 8));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.Frame);
        Assert.Null(result.Diagnostic);
        Assert.Equal(16, result.Frame.Width);
        Assert.Equal(8, result.Frame.Height);
        Assert.Equal(16 * 8 * 4, result.Frame.Pixels.Length);
    }

    private static byte[] CreateValidKeyFramePacket(
        int firstPartitionSize,
        int width = 16,
        int height = 8,
        int tokenPartitionBytes = 0,
        int? emittedFirstPartitionBytes = null)
    {
        var firstPartitionBytes = emittedFirstPartitionBytes ?? firstPartitionSize;
        var packet = new byte[10 + firstPartitionBytes + tokenPartitionBytes];
        var frameTag = (firstPartitionSize << 5) | 0x10;
        packet[0] = (byte)frameTag;
        packet[1] = (byte)(frameTag >> 8);
        packet[2] = (byte)(frameTag >> 16);
        packet[3] = 0x9d;
        packet[4] = 0x01;
        packet[5] = 0x2a;
        packet[6] = (byte)width;
        packet[7] = (byte)(width >> 8);
        packet[8] = (byte)height;
        packet[9] = (byte)(height >> 8);
        return packet;
    }
}
