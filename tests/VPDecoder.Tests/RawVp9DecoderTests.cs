namespace VPDecoder.Tests;

public sealed class RawVp9DecoderTests
{
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
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
        Assert.Contains("pixel reconstruction", result.Diagnostic?.Message);
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
        var path = "/tmp/vp9-main-frame-0.vp9";
        if (!File.Exists(path))
        {
            return;
        }

        var packet = File.ReadAllBytes(path);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(1352, result.Header.Height);
        Assert.Equal(30398, result.Header.PacketLength);
        Assert.Equal(640, result.Header.FirstPartitionSize);
        Assert.Equal(8, result.Header.TileInfo.TileColumns);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_ExternalAlphaFrameSample_ParsesExpectedHeaderWhenPresent()
    {
        var path = "/tmp/vp9-alpha-frame-0.vp9";
        if (!File.Exists(path))
        {
            return;
        }

        var packet = File.ReadAllBytes(path);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9ColorRange.Studio, result.Header.ColorRange);
        Assert.Equal(6233, result.Header.PacketLength);
        Assert.Equal(284, result.Header.FirstPartitionSize);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    private static byte[] CreatePaddedMainFramePacket()
    {
        var packet = new byte[MainFrameHeader.Length + 640];
        MainFrameHeader.CopyTo(packet, 0);
        return packet;
    }
}
