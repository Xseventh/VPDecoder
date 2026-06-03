namespace VPDecoder.Tests;

public sealed class Vp8KeyFrameSyntaxHeaderParserTests
{
    private const int MinimalAllZeroPartitionLength = 160;

    [Fact]
    public void Parse_AllZeroPartition_ReturnsDefaultKeyFrameSyntaxHeader()
    {
        var syntaxHeader = Vp8KeyFrameSyntaxHeaderParser.Parse(new byte[MinimalAllZeroPartitionLength]);

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
        Assert.False(syntaxHeader.RefreshEntropyProbabilities);
        Assert.Empty(syntaxHeader.CoefficientProbabilityUpdates);
        Assert.False(syntaxHeader.MbNoCoeffSkip);
        Assert.Null(syntaxHeader.ProbSkipFalse);
    }

    [Fact]
    public void CoefficientUpdateProbabilities_UseLibvpxFlattenedOrder()
    {
        Assert.Equal(4 * 8 * 3 * 11, Vp8CoefficientUpdateProbabilities.Count);
        Assert.Equal(255, Vp8CoefficientUpdateProbabilities.GetProbability(0, 0, 0, 0));
        Assert.Equal(176, Vp8CoefficientUpdateProbabilities.GetProbability(0, 1, 0, 0));
        Assert.Equal(217, Vp8CoefficientUpdateProbabilities.GetProbability(1, 0, 0, 0));
        Assert.Equal(186, Vp8CoefficientUpdateProbabilities.GetProbability(2, 0, 0, 0));
        Assert.Equal(248, Vp8CoefficientUpdateProbabilities.GetProbability(3, 0, 0, 0));
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
