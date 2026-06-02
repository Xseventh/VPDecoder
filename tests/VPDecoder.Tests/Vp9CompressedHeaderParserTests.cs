namespace VPDecoder.Tests;

public sealed class Vp9CompressedHeaderParserTests
{
    private const string MainFrameSamplePath = "/tmp/vp9-main-frame-0.vp9";
    private const string MainFrameSampleSha256 = "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9";
    private const string AlphaFrameSamplePath = "/tmp/vp9-alpha-frame-0.vp9";
    private const string AlphaFrameSampleSha256 = "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329";

    [Fact]
    public void TryParse_ExternalMainFrameSample_ReturnsProbabilityUpdateSummary()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);

        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, frameHeader, out var compressedHeader, out var diagnostic), diagnostic?.Message);
        Assert.NotNull(compressedHeader);
        Assert.Equal(Vp9TransformMode.Select, compressedHeader.TransformMode);
        Assert.Equal(9, compressedHeader.TxProbabilityUpdateCount);
        Assert.Equal(200, compressedHeader.CoefficientProbabilityUpdateCount);
        Assert.Equal(1, compressedHeader.SkipProbabilityUpdateCount);
        Assert.Equal([192, 128, 42], compressedHeader.FrameContext.SkipProbabilities);
    }

    [Fact]
    public void TryParse_ExternalAlphaFrameSample_ReturnsProbabilityUpdateSummary()
    {
        var packet = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);

        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, frameHeader, out var compressedHeader, out var diagnostic), diagnostic?.Message);
        Assert.NotNull(compressedHeader);
        Assert.Equal(Vp9TransformMode.Select, compressedHeader.TransformMode);
        Assert.Equal(9, compressedHeader.TxProbabilityUpdateCount);
        Assert.Equal(84, compressedHeader.CoefficientProbabilityUpdateCount);
        Assert.Equal(2, compressedHeader.SkipProbabilityUpdateCount);
        Assert.Equal([192, 111, 21], compressedHeader.FrameContext.SkipProbabilities);
    }

    [Fact]
    public void TryParse_WhenCompressedHeaderHasNoUpdates_ReturnsDefaultFrameContext()
    {
        var packet = CreatePaddedMainFramePacket();
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);

        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, frameHeader, out var compressedHeader, out var diagnostic), diagnostic?.Message);
        Assert.NotNull(compressedHeader);
        Assert.Equal(Vp9TransformMode.Only4X4, compressedHeader.TransformMode);
        Assert.Equal(0, compressedHeader.TxProbabilityUpdateCount);
        Assert.Equal(0, compressedHeader.CoefficientProbabilityUpdateCount);
        Assert.Equal(0, compressedHeader.SkipProbabilityUpdateCount);
        Assert.Equal([192, 128, 64], compressedHeader.FrameContext.SkipProbabilities);
    }

    [Fact]
    public void TryParse_WithBaseFrameContext_ClonesBeforeApplyingUpdates()
    {
        var packet = CreatePaddedMainFramePacket();
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);
        var baseFrameContext = Vp9FrameContext.CreateDefault();
        baseFrameContext.SkipProbabilities[0] = 7;
        baseFrameContext.SkipProbabilities[1] = 8;
        baseFrameContext.SkipProbabilities[2] = 9;

        Assert.True(
            Vp9CompressedHeaderParser.TryParse(
                packet,
                frameHeader,
                baseFrameContext,
                out var compressedHeader,
                out var diagnostic),
            diagnostic?.Message);

        Assert.NotNull(compressedHeader);
        Assert.Equal([7, 8, 9], compressedHeader.FrameContext.SkipProbabilities);
        compressedHeader.FrameContext.SkipProbabilities[0] = 99;
        Assert.Equal([7, 8, 9], baseFrameContext.SkipProbabilities);
    }

    [Fact]
    public void TryParse_WhenBoolReaderMarkerIsInvalid_ReturnsInvalidPacketDiagnostic()
    {
        var packet = CreatePaddedMainFramePacket();
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);
        packet[frameHeader.HeaderSizeInBytes] = 0xff;

        Assert.False(Vp9CompressedHeaderParser.TryParse(packet, frameHeader, out _, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, diagnostic.Code);
    }

    private static byte[] CreatePaddedMainFramePacket()
    {
        byte[] header =
        [
            0x82, 0x49, 0x83, 0x42, 0x10, 0xa5, 0xf0, 0x54, 0x76,
            0x04, 0x38, 0x24, 0x1c, 0x18, 0x66, 0x1c, 0x02, 0x80
        ];
        var packet = new byte[header.Length + 320];
        header.CopyTo(packet, 0);
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
