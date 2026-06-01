namespace VPDecoder.Tests;

public sealed class RawVp8DecoderTests
{
    [Fact]
    public void DecodeFrame_WhenPacketIsNonEmpty_ReturnsUnsupportedFeature()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([0x9d, 0x01, 0x2a], new Vp8DecodeOptions(1, 1));

        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenPacketIsEmpty_ReturnsInvalidPacket()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([]);

        Assert.False(result.Succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var decoder = new RawVp8Decoder();

        var result = decoder.DecodeFrame([0], new Vp8DecodeOptions(ExpectedWidth: 0));

        Assert.False(result.Succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }
}
