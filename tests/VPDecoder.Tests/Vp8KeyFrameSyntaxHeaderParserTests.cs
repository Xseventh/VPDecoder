namespace VPDecoder.Tests;

public sealed class Vp8KeyFrameSyntaxHeaderParserTests
{
    [Fact]
    public void Parse_AllZeroPartition_ReturnsDefaultKeyFrameSyntaxHeader()
    {
        var syntaxHeader = Vp8KeyFrameSyntaxHeaderParser.Parse(
        [
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ]);

        Assert.Equal(Vp8KeyFrameColorSpace.Bt601, syntaxHeader.ColorSpace);
        Assert.False(syntaxHeader.ClampType);
        Assert.False(syntaxHeader.Segmentation.Enabled);
        Assert.False(syntaxHeader.Segmentation.UpdateMap);
        Assert.False(syntaxHeader.Segmentation.UpdateFeatureData);
        Assert.All(syntaxHeader.Segmentation.QuantizerUpdates, value => Assert.Equal(0, value));
        Assert.All(syntaxHeader.Segmentation.LoopFilterUpdates, value => Assert.Equal(0, value));
        Assert.All(syntaxHeader.Segmentation.SegmentTreeProbabilities, Assert.Null);
        Assert.Equal(Vp8LoopFilterType.Normal, syntaxHeader.LoopFilter.Type);
        Assert.Equal(0, syntaxHeader.LoopFilter.Level);
        Assert.Equal(0, syntaxHeader.LoopFilter.SharpnessLevel);
        Assert.False(syntaxHeader.LoopFilter.DeltaEnabled);
        Assert.False(syntaxHeader.LoopFilter.DeltaUpdate);
        Assert.Equal(0, syntaxHeader.Log2TokenPartitionCount);
        Assert.Equal(1, syntaxHeader.TokenPartitionCount);
        Assert.Equal(0, syntaxHeader.Quantization.YAcQuantizerIndex);
        Assert.Equal(0, syntaxHeader.Quantization.YDcDelta);
        Assert.Equal(0, syntaxHeader.Quantization.Y2DcDelta);
        Assert.Equal(0, syntaxHeader.Quantization.Y2AcDelta);
        Assert.Equal(0, syntaxHeader.Quantization.UvDcDelta);
        Assert.Equal(0, syntaxHeader.Quantization.UvAcDelta);
    }

    [Fact]
    public void TryParse_EmptyPartition_ReturnsTruncatedPacket()
    {
        var parsed = Vp8KeyFrameSyntaxHeaderParser.TryParse([], out var syntaxHeader, out var diagnostic);

        Assert.False(parsed);
        Assert.Null(syntaxHeader);
        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }
}
