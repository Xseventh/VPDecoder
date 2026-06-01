namespace VPDecoder.Tests;

using System.Diagnostics;

public sealed class RawVp9DecoderTests
{
    private const string MainFrameSamplePath = "/tmp/vp9-main-frame-0.vp9";
    private const string MainFrameSampleSha256 = "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9";
    private const string AlphaFrameSamplePath = "/tmp/vp9-alpha-frame-0.vp9";
    private const string AlphaFrameSampleSha256 = "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329";

    private static readonly byte[] MainFrameHeader =
    [
        0x82, 0x49, 0x83, 0x42, 0x10, 0xa5, 0xf0, 0x54, 0x76,
        0x04, 0x38, 0x24, 0x1c, 0x18, 0x66, 0x1c, 0x02, 0x80
    ];

    [Fact]
    public void DecodeFrame_WhenHeaderIsSupported_ReturnsPixelDecoderUnsupportedDiagnosticWithHeader()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(Vp9TransformMode.Only4X4, result.CompressedHeader.TransformMode);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
        Assert.Contains("pixel reconstruction", result.Diagnostic?.Message);
    }

    [Fact]
    public void DecodeFrame_WhenTileLayoutIsTruncated_ReturnsTruncatedPacket()
    {
        var packet = CreateHeaderAndCompressedHeaderOnlyPacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthDiffers_ReturnsDimensionMismatch()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(ExpectedWidth: 1, ExpectedHeight: 1352));

        Assert.False(result.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.DimensionMismatch, result.Diagnostic?.Code);
        Assert.NotNull(result.Header);
    }

    [Fact]
    public void DecodeFrame_WhenFrameExceedsPixelLimit_ReturnsAllocationLimitExceeded()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352, MaxPixelCount: 1));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.AllocationLimitExceeded, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(ExpectedWidth: 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenOutputFormatIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(OutputFormat: (Vp9OutputPixelFormat)99));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenCompressedHeaderMarkerIsInvalid_ReturnsInvalidPacket()
    {
        var packet = CreatePaddedMainFramePacket();
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);
        packet[frameHeader.HeaderSizeInBytes] = 0xff;
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Null(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenProfileIsUnsupported_ReturnsUnsupportedProfile()
    {
        var packet = CreatePaddedMainFramePacket();
        packet[0] = 0xa2;
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet);

        Assert.False(result.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedProfile, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_ExternalMainFrameSample_ParsesExpectedHeaderWhenPresent()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(1352, result.Header.Height);
        Assert.Equal(30398, result.Header.PacketLength);
        Assert.Equal(320, result.Header.FirstPartitionSize);
        Assert.Equal(8, result.Header.TileInfo.TileColumns);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9TransformMode.Select, result.CompressedHeader.TransformMode);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_ExternalAlphaFrameSample_ParsesExpectedHeaderWhenPresent()
    {
        var packet = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9ColorRange.Studio, result.Header.ColorRange);
        Assert.Equal(6233, result.Header.PacketLength);
        Assert.Equal(142, result.Header.FirstPartitionSize);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenColorDecodeIsUnsupported_PropagatesColorDiagnostic()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
        Assert.Contains("pixel reconstruction", result.Diagnostic?.Message);
    }

    [Fact]
    public void DecodeFrame_ExternalSamples_CompletesWithinBoundedTime()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var stopwatch = Stopwatch.StartNew();

        var colorResult = decoder.DecodeFrame(colorPacket, new Vp9DecodeOptions(2656, 1352));
        var alphaResult = decoder.DecodeFrame(alphaPacket, new Vp9DecodeOptions(2656, 1352));

        stopwatch.Stop();
        Assert.False(colorResult.Succeeded);
        Assert.False(alphaResult.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, colorResult.Diagnostic?.Code);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, alphaResult.Diagnostic?.Code);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"VP9 sample parse took {stopwatch.Elapsed}.");
    }

    private static byte[] CreatePaddedMainFramePacket()
    {
        const int firstPartitionSize = 320;
        const int tileCount = 8;
        const int tileSizeFieldBytes = 4 * (tileCount - 1);
        var packet = new byte[MainFrameHeader.Length + firstPartitionSize + tileSizeFieldBytes + tileCount];
        MainFrameHeader.CopyTo(packet, 0);

        var position = MainFrameHeader.Length + firstPartitionSize;
        for (var i = 0; i < tileCount - 1; i++)
        {
            packet[position + 3] = 1;
            position += 4;
            packet[position] = 0x80;
            position++;
        }

        packet[position] = 0x80;
        return packet;
    }

    private static byte[] CreateHeaderAndCompressedHeaderOnlyPacket()
    {
        var packet = new byte[MainFrameHeader.Length + 320];
        MainFrameHeader.CopyTo(packet, 0);
        return packet;
    }

    private static byte[] ReadRequiredSample(string path, int expectedLength, string expectedSha256)
    {
        Assert.True(File.Exists(path), $"Required VP9 acceptance sample is missing: {path}");
        var packet = File.ReadAllBytes(path);
        Assert.Equal(expectedLength, packet.Length);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet)).ToLowerInvariant();
        Assert.Equal(expectedSha256, hash);
        return packet;
    }
}
