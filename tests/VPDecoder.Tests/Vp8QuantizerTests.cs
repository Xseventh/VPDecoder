namespace VPDecoder.Tests;

public sealed class Vp8QuantizerTests
{
    [Fact]
    public void CreateDequantFactors_ForZeroHeader_ReturnsLibvpxMinimums()
    {
        var factors = Vp8Quantizer.CreateDequantFactors(
            new Vp8QuantizationHeader(0, 0, 0, 0, 0, 0),
            CreateSegmentation(enabled: false),
            segmentId: 0);

        Assert.Equal(new Vp8DequantFactors(
            Y1Dc: 4,
            Y1Ac: 4,
            Y2Dc: 8,
            Y2Ac: 8,
            UvDc: 4,
            UvAc: 4), factors);
    }

    [Fact]
    public void CreateDequantFactors_ClampsQIndexAndAppliesUvDcCap()
    {
        var factors = Vp8Quantizer.CreateDequantFactors(
            new Vp8QuantizationHeader(127, 10, 10, 10, 10, 10),
            CreateSegmentation(enabled: false),
            segmentId: 0);

        Assert.Equal(157, factors.Y1Dc);
        Assert.Equal(284, factors.Y1Ac);
        Assert.Equal(314, factors.Y2Dc);
        Assert.Equal(440, factors.Y2Ac);
        Assert.Equal(132, factors.UvDc);
        Assert.Equal(284, factors.UvAc);
    }

    [Fact]
    public void CreateDequantFactors_WhenSegmentUsesDeltaMode_AddsSegmentQuantizer()
    {
        var segmentation = CreateSegmentation(enabled: true, absoluteDeltaMode: false, quantizerUpdates: [5, 0, 0, 0]);

        var factors = Vp8Quantizer.CreateDequantFactors(
            new Vp8QuantizationHeader(20, 0, 0, 0, 0, 0),
            segmentation,
            segmentId: 0);

        Assert.Equal(23, factors.Y1Dc);
        Assert.Equal(29, factors.Y1Ac);
    }

    [Fact]
    public void CreateDequantFactors_WhenSegmentUsesAbsoluteMode_ReplacesBaseQuantizer()
    {
        var segmentation = CreateSegmentation(enabled: true, absoluteDeltaMode: true, quantizerUpdates: [5, 0, 0, 0]);

        var factors = Vp8Quantizer.CreateDequantFactors(
            new Vp8QuantizationHeader(20, 0, 0, 0, 0, 0),
            segmentation,
            segmentId: 0);

        Assert.Equal(9, factors.Y1Dc);
        Assert.Equal(9, factors.Y1Ac);
    }

    private static Vp8SegmentationHeader CreateSegmentation(
        bool enabled,
        bool absoluteDeltaMode = false,
        int[]? quantizerUpdates = null)
    {
        return new Vp8SegmentationHeader(
            enabled,
            UpdateMap: false,
            UpdateFeatureData: enabled,
            absoluteDeltaMode,
            quantizerUpdates ?? [0, 0, 0, 0],
            LoopFilterUpdates: [0, 0, 0, 0],
            SegmentTreeProbabilities: [null, null, null]);
    }
}
