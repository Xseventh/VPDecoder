namespace VPDecoder.Tests;

public sealed class Vp9InterFrameSyntaxContextTests
{
    [Fact]
    public void GetPartitionContext_WhenInitialFrameContextsAreZero_ReturnsLibvpxInitialContexts()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(12, context.GetPartitionContext(0, 0, Vp9BlockSize.Block64X64));
        Assert.Equal(8, context.GetPartitionContext(0, 0, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void UpdatePartitionContext_UsesLibvpxPartitionContextLookup()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        context.UpdatePartitionContext(0, 0, Vp9BlockSize.Block32X32, Vp9BlockSize.Block16X16);

        Assert.Equal(11, context.GetPartitionContext(0, 0, Vp9BlockSize.Block32X32));
        Assert.Equal(10, context.GetPartitionContext(0, 4, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void GetSkipContext_UsesAboveAndLeftButRespectsTileLeftBoundary()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));
        context.SetModeInfo(0, 0, CreateModeInfo(Vp9BlockSize.Block8X8, skip: true, Vp9TransformSize.Tx4X4));

        Assert.Equal(1, context.GetSkipContext(0, 1, tileMiColumnStart: 0));
        Assert.Equal(0, context.GetSkipContext(0, 1, tileMiColumnStart: 1));

        context.SetModeInfo(0, 1, CreateModeInfo(Vp9BlockSize.Block8X8, skip: true, Vp9TransformSize.Tx4X4));
        context.SetModeInfo(1, 0, CreateModeInfo(Vp9BlockSize.Block8X8, skip: true, Vp9TransformSize.Tx4X4));

        Assert.Equal(2, context.GetSkipContext(1, 1, tileMiColumnStart: 0));
    }

    [Fact]
    public void GetTransformSizeContext_UsesMaxTransformWhenNeighborsAreMissingOrSkipped()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(1, context.GetTransformSizeContext(0, 0, tileMiColumnStart: 0, Vp9BlockSize.Block32X32));

        context.SetModeInfo(0, 1, CreateModeInfo(Vp9BlockSize.Block8X8, skip: true, Vp9TransformSize.Tx4X4));
        context.SetModeInfo(1, 0, CreateModeInfo(Vp9BlockSize.Block8X8, skip: true, Vp9TransformSize.Tx4X4));

        Assert.Equal(1, context.GetTransformSizeContext(1, 1, tileMiColumnStart: 0, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void GetTransformSizeContext_UsesUnskippedAboveLeftTransformSizes()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));
        context.SetModeInfo(0, 1, CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4));
        context.SetModeInfo(1, 0, CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4));

        Assert.Equal(0, context.GetTransformSizeContext(1, 1, tileMiColumnStart: 0, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void GetIntraInterContext_UsesAboveLeftInterFlags()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(0, context.GetIntraInterContext(0, 0, tileMiColumnStart: 0));

        context.SetModeInfo(0, 1, CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4, isInterBlock: false));

        Assert.Equal(2, context.GetIntraInterContext(1, 1, tileMiColumnStart: 0));

        context.SetModeInfo(1, 0, CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4));

        Assert.Equal(1, context.GetIntraInterContext(1, 1, tileMiColumnStart: 0));
        Assert.Equal(2, context.GetIntraInterContext(1, 1, tileMiColumnStart: 1));
    }

    [Theory]
    [InlineData((int)Vp9InterReferenceFrame.Last, 4, 2)]
    [InlineData((int)Vp9InterReferenceFrame.Golden, 0, 4)]
    [InlineData((int)Vp9InterReferenceFrame.AltRef, 0, 0)]
    public void GetSingleReferenceContexts_WhenOneEdgeAvailable_MatchesLibvpxLookup(
        int referenceFrame,
        int expectedContext0,
        int expectedContext1)
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));
        context.SetModeInfo(
            1,
            0,
            CreateModeInfo(
                Vp9BlockSize.Block8X8,
                skip: false,
                Vp9TransformSize.Tx4X4,
                referenceFrame: (Vp9InterReferenceFrame)referenceFrame));

        var referenceContexts = context.GetSingleReferenceContexts(1, 1, tileMiColumnStart: 0);

        Assert.Equal(expectedContext0, referenceContexts.Context0);
        Assert.Equal(expectedContext1, referenceContexts.Context1);
    }

    [Fact]
    public void GetSingleReferenceContexts_WhenBothEdgesAvailable_MatchesLibvpxSingleReferenceLookup()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        var initial = context.GetSingleReferenceContexts(0, 0, tileMiColumnStart: 0);
        Assert.Equal(2, initial.Context0);
        Assert.Equal(2, initial.Context1);

        context.SetModeInfo(
            0,
            1,
            CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4, referenceFrame: Vp9InterReferenceFrame.Last));
        context.SetModeInfo(
            1,
            0,
            CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4, referenceFrame: Vp9InterReferenceFrame.Golden));

        var referenceContexts = context.GetSingleReferenceContexts(1, 1, tileMiColumnStart: 0);

        Assert.Equal(2, referenceContexts.Context0);
        Assert.Equal(4, referenceContexts.Context1);
    }

    [Fact]
    public void GetInterModeContext_UsesFirstTwoLibvpxMotionReferencePositions()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(2, context.GetInterModeContext(1, 1, Vp9BlockSize.Block16X16, tileMiColumnStart: 0, tileMiColumnEnd: 16));

        context.SetModeInfo(
            0,
            1,
            CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4, predictionMode: Vp9InterPredictionMode.ZeroMv));
        context.SetModeInfo(
            1,
            0,
            CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4, predictionMode: Vp9InterPredictionMode.NearMv));

        Assert.Equal(1, context.GetInterModeContext(1, 1, Vp9BlockSize.Block16X16, tileMiColumnStart: 0, tileMiColumnEnd: 16));
        Assert.Equal(1, context.GetInterModeContext(1, 1, Vp9BlockSize.Block16X16, tileMiColumnStart: 1, tileMiColumnEnd: 16));

        context.SetModeInfo(
            2,
            0,
            CreateModeInfo(Vp9BlockSize.Block8X8, skip: false, Vp9TransformSize.Tx4X4, predictionMode: Vp9InterPredictionMode.ZeroMv));

        Assert.Equal(2, context.GetInterModeContext(2, 1, Vp9BlockSize.Block16X16, tileMiColumnStart: 1, tileMiColumnEnd: 16));
    }

    [Fact]
    public void GetSwitchableInterpolationContext_UsesLibvpxAboveLeftRules()
    {
        var context = Vp9InterFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(3, context.GetSwitchableInterpolationContext(0, 0, tileMiColumnStart: 0));

        context.SetModeInfo(
            0,
            1,
            CreateModeInfo(
                Vp9BlockSize.Block8X8,
                skip: false,
                Vp9TransformSize.Tx4X4,
                interpolationFilter: Vp9InterpolationFilter.EightTapSmooth));
        context.SetModeInfo(
            1,
            0,
            CreateModeInfo(
                Vp9BlockSize.Block8X8,
                skip: false,
                Vp9TransformSize.Tx4X4,
                interpolationFilter: Vp9InterpolationFilter.EightTapSmooth));

        Assert.Equal(1, context.GetSwitchableInterpolationContext(1, 1, tileMiColumnStart: 0));
        Assert.Equal(1, context.GetSwitchableInterpolationContext(1, 1, tileMiColumnStart: 1));

        context.SetModeInfo(
            1,
            0,
            CreateModeInfo(
                Vp9BlockSize.Block8X8,
                skip: false,
                Vp9TransformSize.Tx4X4,
                interpolationFilter: Vp9InterpolationFilter.EightTapSharp));

        Assert.Equal(3, context.GetSwitchableInterpolationContext(1, 1, tileMiColumnStart: 0));
        Assert.Equal(1, context.GetSwitchableInterpolationContext(1, 1, tileMiColumnStart: 1));
    }

    private static Vp9InterModeInfoProbe CreateModeInfo(
        Vp9BlockSize blockSize,
        bool skip,
        Vp9TransformSize transformSize,
        bool isInterBlock = true,
        Vp9InterReferenceFrame referenceFrame = Vp9InterReferenceFrame.Last,
        Vp9InterPredictionMode predictionMode = Vp9InterPredictionMode.ZeroMv,
        Vp9InterpolationFilter interpolationFilter = Vp9InterpolationFilter.EightTap)
    {
        return new Vp9InterModeInfoProbe(
            blockSize,
            skip,
            SkipContext: 0,
            IsInterBlock: isInterBlock,
            IntraInterContext: 0,
            TransformSize: transformSize,
            TransformSizeContext: 0,
            ReferenceMode: Vp9ReferenceMode.Single,
            ReferenceFrame: referenceFrame,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            PredictionMode: predictionMode,
            InterModeContext: 0,
            InterpolationFilter: interpolationFilter);
    }

    private static Vp9FrameHeader CreateHeader(int miColumns, int miRows)
    {
        return new Vp9FrameHeader(
            PacketLength: 0,
            HeaderSizeInBytes: 0,
            FrameMarker: 2,
            Profile: 0,
            ShowExistingFrame: false,
            ExistingFrameIndex: null,
            FrameType: Vp9FrameType.InterFrame,
            ShowFrame: true,
            ErrorResilientMode: false,
            SyncCodeValid: false,
            BitDepth: 8,
            ColorSpace: Vp9ColorSpace.Bt709,
            ColorRange: Vp9ColorRange.Studio,
            SubsamplingX: 1,
            SubsamplingY: 1,
            Width: miColumns * 8,
            Height: miRows * 8,
            RenderWidth: miColumns * 8,
            RenderHeight: miRows * 8,
            RefreshFrameContext: true,
            RefreshFrameFlags: 0,
            FrameParallelDecodingMode: false,
            FrameContextIndex: 0,
            LoopFilter: new Vp9LoopFilterHeader(0, 0, false, false, [], []),
            Quantization: new Vp9QuantizationHeader(0, 0, 0, 0),
            Segmentation: new Vp9SegmentationHeader(false, false, false, false, false, [], []),
            TileInfo: new Vp9TileInfo(miColumns, miRows, 2, 0, 0, 0, 0),
            FirstPartitionSize: 0,
            IntraOnly: false,
            ResetFrameContextMode: 0,
            ReferenceFrameIndices: [0, 1, 2],
            ReferenceFrameSignBiases: [false, false, false, false],
            FrameSizeReferenceFlags: [false, false, false],
            FrameSizeReferenceIndex: null,
            RenderSizeDifferent: false,
            AllowHighPrecisionMv: false,
            InterpolationFilter: Vp9InterpolationFilter.EightTap);
    }
}
