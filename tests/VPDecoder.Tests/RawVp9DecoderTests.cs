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
    public void DecodeFrame_WhenHeaderIsSupportedButSyntaxIsInvalid_ReturnsConcreteSyntaxDiagnosticWithHeader()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(Vp9TransformMode.Only4X4, result.CompressedHeader.TransformMode);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
        Assert.Contains("marker bit", result.Diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
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

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(1352, result.Header.Height);
        Assert.Equal(30398, result.Header.PacketLength);
        Assert.Equal(320, result.Header.FirstPartitionSize);
        Assert.Equal(8, result.Header.TileInfo.TileColumns);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9TransformMode.Select, result.CompressedHeader.TransformMode);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(result.Frame.Pixels));
        Assert.Equal([255], result.Frame.Pixels.Chunk(4).Select(pixel => pixel[3]).Distinct().ToArray());
    }

    [Fact]
    public void DecodeFrame_ExternalAlphaFrameSample_ParsesExpectedHeaderWhenPresent()
    {
        var packet = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9ColorRange.Studio, result.Header.ColorRange);
        Assert.Equal(6233, result.Header.PacketLength);
        Assert.Equal(142, result.Header.FirstPartitionSize);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("de5f6cf32681237d0076b8e106c2d8803a54379f639d9f6e7d10a864ad1ff306", Hash(result.Frame.Pixels));
    }

    [Theory]
    [InlineData(
        Vp9OutputPixelFormat.Yuv420,
        5_386_368,
        "4f276400ec1d63299b4ec18d83da40482b52d9d09b1e3fd4a100537ed63798ff")]
    [InlineData(
        Vp9OutputPixelFormat.Rgba8888,
        14_363_648,
        "26d00202125b56b944707fdd55051a1365b8795b8512a9b805dea07dc05b41b4")]
    public void DecodeFrame_ExternalMainFrameSample_SupportsRequestedOutputFormats(
        Vp9OutputPixelFormat outputFormat,
        int expectedLength,
        string expectedHash)
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352, outputFormat));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(outputFormat, result.Frame.PixelFormat);
        Assert.Equal(expectedLength, result.Frame.Pixels.Length);
        Assert.Equal(expectedHash, Hash(result.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_ForExternalSamples_MergesAlphaDeterministically()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("c8095ee5e4b760a8a6f7c18d10b357b9f579c6864bb1cd815061d8d6e930a2ff", Hash(result.Frame.Pixels));
        var alphaValues = result.Frame.Pixels.Chunk(4).Select(pixel => pixel[3]).ToArray();
        Assert.Equal(0, alphaValues.Min());
        Assert.Equal(168, alphaValues.Max());
        Assert.True(alphaValues.Distinct().Count() > 1);
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenOutputFormatIsRgba_ReturnsRgbaFrame()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(
            colorPacket,
            alphaPacket,
            new Vp9DecodeOptions(2656, 1352, Vp9OutputPixelFormat.Rgba8888));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Rgba8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("ac9ec4a5bcd706088dee9596536dec008854e0df4453b149e9ff55a2e2d78703", Hash(result.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenOutputFormatIsYuv420_ReturnsUnsupportedFeature()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(
            colorPacket,
            alphaPacket,
            new Vp9DecodeOptions(2656, 1352, Vp9OutputPixelFormat.Yuv420));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
        Assert.Contains("alpha composition", result.Diagnostic?.Message);
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
        Assert.True(colorResult.Succeeded, colorResult.Diagnostic?.Message);
        Assert.True(alphaResult.Succeeded, alphaResult.Diagnostic?.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"VP9 sample parse took {stopwatch.Elapsed}.");
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

        var hash = Hash(packet);
        Assert.Equal(expectedSha256, hash);
        return packet;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
