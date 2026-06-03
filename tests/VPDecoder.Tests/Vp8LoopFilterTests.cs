namespace VPDecoder.Tests;

public sealed class Vp8LoopFilterTests
{
    [Fact]
    public void GetThresholds_ForKeyFrameLevelAndSharpness_ComputesVp8Limits()
    {
        var thresholds = Vp8LoopFilter.GetThresholds(filterLevel: 20, sharpnessLevel: 0);

        Assert.Equal(20, thresholds.InteriorLimit);
        Assert.Equal(1, thresholds.HighEdgeVarianceThreshold);
        Assert.Equal(64, thresholds.MacroblockFilterLimit);
        Assert.Equal(60, thresholds.SubblockFilterLimit);
    }

    [Fact]
    public void TryApply_ForSimpleFilter_AdjustsLumaMacroblockEdgeOnly()
    {
        var buffer = CreateTwoMacroblockBuffer(left: 100, right: 110);
        var uBefore = buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length).ToArray();
        var vBefore = buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length).ToArray();

        var succeeded = Vp8LoopFilter.TryApply(
            buffer,
            CreateHeader(Vp8LoopFilterType.Simple, level: 10),
            CreateTwoMacroblocks(filterSubblocks: false),
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        Assert.Equal(100, yPlane[14]);
        Assert.Equal(102, yPlane[15]);
        Assert.Equal(107, yPlane[16]);
        Assert.Equal(110, yPlane[17]);
        Assert.Equal(uBefore, buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length).ToArray());
        Assert.Equal(vBefore, buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length).ToArray());
    }

    [Fact]
    public void TryApply_ForNormalFilter_AdjustsMacroblockEdgeWithVp8NormalKernel()
    {
        var buffer = CreateTwoMacroblockBuffer(left: 100, right: 110);

        var succeeded = Vp8LoopFilter.TryApply(
            buffer,
            CreateHeader(Vp8LoopFilterType.Normal, level: 10),
            CreateTwoMacroblocks(filterSubblocks: false),
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        Assert.Equal(100, yPlane[12]);
        Assert.Equal(101, yPlane[13]);
        Assert.Equal(103, yPlane[14]);
        Assert.Equal(104, yPlane[15]);
        Assert.Equal(106, yPlane[16]);
        Assert.Equal(107, yPlane[17]);
        Assert.Equal(109, yPlane[18]);
        Assert.Equal(110, yPlane[19]);
    }

    [Fact]
    public void TryApply_WhenLoopFilterDeltasAreEnabled_ReturnsUnsupportedFeature()
    {
        var buffer = CreateTwoMacroblockBuffer(left: 100, right: 110);
        var header = CreateHeader(Vp8LoopFilterType.Normal, level: 10) with
        {
            LoopFilter = CreateLoopFilter(Vp8LoopFilterType.Normal, level: 10) with
            {
                DeltaEnabled = true
            }
        };

        var succeeded = Vp8LoopFilter.TryApply(
            buffer,
            header,
            CreateTwoMacroblocks(filterSubblocks: false),
            out var diagnostic);

        Assert.False(succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, diagnostic?.Code);
        Assert.Contains("delta-enabled", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_WhenSegmentationLoopFilterFeatureDataIsEnabled_ReturnsUnsupportedFeature()
    {
        var buffer = CreateTwoMacroblockBuffer(left: 100, right: 110);
        var header = CreateHeader(Vp8LoopFilterType.Normal, level: 10) with
        {
            Segmentation = new Vp8SegmentationHeader(
                Enabled: true,
                UpdateMap: true,
                UpdateFeatureData: true,
                AbsoluteDeltaMode: false,
                QuantizerUpdates: [0, 0, 0, 0],
                LoopFilterUpdates: [0, 1, 0, 0],
                SegmentTreeProbabilities: [null, null, null])
        };

        var succeeded = Vp8LoopFilter.TryApply(
            buffer,
            header,
            CreateTwoMacroblocks(filterSubblocks: false),
            out var diagnostic);

        Assert.False(succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, diagnostic?.Code);
        Assert.Contains("segmentation loop filter", diagnostic?.Message, StringComparison.Ordinal);
    }

    private static Vp8ReconstructionBuffer CreateTwoMacroblockBuffer(byte left, byte right)
    {
        var buffer = Vp8ReconstructionBuffer.Create(width: 32, height: 16);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        for (var row = 0; row < buffer.YPlane.Height; row++)
        {
            for (var column = 0; column < buffer.YPlane.Width; column++)
            {
                yPlane[(row * buffer.YPlane.Stride) + column] = column < 16 ? left : right;
            }
        }

        buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length).Fill(128);
        buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length).Fill(128);
        return buffer;
    }

    private static Vp8KeyFrameSyntaxHeader CreateHeader(Vp8LoopFilterType type, int level)
    {
        return new Vp8KeyFrameSyntaxHeader(
            Vp8KeyFrameColorSpace.Bt601,
            ClampType: false,
            CreateSegmentation(),
            CreateLoopFilter(type, level),
            Log2TokenPartitionCount: 0,
            TokenPartitionCount: 1,
            new Vp8QuantizationHeader(0, 0, 0, 0, 0, 0),
            RefreshEntropyProbabilities: false,
            CoefficientProbabilityUpdates: [],
            MbNoCoeffSkip: false,
            ProbSkipFalse: null);
    }

    private static Vp8SegmentationHeader CreateSegmentation()
    {
        return new Vp8SegmentationHeader(
            Enabled: false,
            UpdateMap: false,
            UpdateFeatureData: false,
            AbsoluteDeltaMode: false,
            QuantizerUpdates: [0, 0, 0, 0],
            LoopFilterUpdates: [0, 0, 0, 0],
            SegmentTreeProbabilities: [null, null, null]);
    }

    private static Vp8LoopFilterHeader CreateLoopFilter(Vp8LoopFilterType type, int level)
    {
        return new Vp8LoopFilterHeader(
            type,
            level,
            SharpnessLevel: 0,
            DeltaEnabled: false,
            DeltaUpdate: false,
            ReferenceFrameDeltas: [0, 0, 0, 0],
            ModeDeltas: [0, 0, 0, 0]);
    }

    private static IReadOnlyList<Vp8LoopFilterMacroblock> CreateTwoMacroblocks(bool filterSubblocks)
    {
        return
        [
            new Vp8LoopFilterMacroblock(Row: 0, Column: 0, filterSubblocks),
            new Vp8LoopFilterMacroblock(Row: 0, Column: 1, filterSubblocks)
        ];
    }
}
