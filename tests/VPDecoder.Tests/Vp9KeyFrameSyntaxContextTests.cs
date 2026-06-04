namespace VPDecoder.Tests;

public sealed class Vp9KeyFrameSyntaxContextTests
{
    [Fact]
    public void GetPartitionContext_WhenInitialFrameContextsAreZero_ReturnsLibvpxInitialContexts()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(12, context.GetPartitionContext(0, 0, Vp9BlockSize.Block64X64));
        Assert.Equal(8, context.GetPartitionContext(0, 0, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void UpdatePartitionContext_UsesLibvpxPartitionContextLookup()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        context.UpdatePartitionContext(0, 0, Vp9BlockSize.Block32X32, Vp9BlockSize.Block16X16);

        Assert.Equal(11, context.GetPartitionContext(0, 0, Vp9BlockSize.Block32X32));
        Assert.Equal(10, context.GetPartitionContext(0, 4, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void UpdatePartitionContext_WhenClippedAtRightEdge_StillUpdatesFullLeftContextWidth()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 3, miRows: 8));

        context.UpdatePartitionContext(0, 2, Vp9BlockSize.Block32X32, Vp9BlockSize.Block16X16);

        Assert.Equal(10, context.GetPartitionContext(2, 0, Vp9BlockSize.Block32X32));
        Assert.Equal(10, context.GetPartitionContext(3, 0, Vp9BlockSize.Block32X32));
    }

    [Fact]
    public void UpdatePartitionContext_WhenPartitionBlockIsRectangular_UpdatesLeftByHeight()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        context.UpdatePartitionContext(0, 0, Vp9BlockSize.Block64X32, Vp9BlockSize.Block64X32);

        Assert.Equal(14, context.GetPartitionContext(0, 0, Vp9BlockSize.Block64X64));
        Assert.Equal(14, context.GetPartitionContext(3, 0, Vp9BlockSize.Block64X64));
        Assert.Equal(12, context.GetPartitionContext(4, 0, Vp9BlockSize.Block64X64));
    }

    [Fact]
    public void GetSkipAndYModeContexts_UseAboveLeftModesButRespectTileLeftBoundary()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));
        context.SetModeInfo(
            miRow: 0,
            miColumn: 0,
            Vp9BlockSize.Block8X8,
            skip: true,
            Vp9TransformSize.Tx4X4,
            Vp9PredictionMode.Vertical);

        Assert.Equal(1, context.GetSkipContext(0, 1, tileMiColumnStart: 0));
        var modeContext = context.GetYModeContext(0, 1, tileMiColumnStart: 0);
        Assert.Equal(Vp9PredictionMode.Dc, modeContext.Above);
        Assert.Equal(Vp9PredictionMode.Vertical, modeContext.Left);

        Assert.Equal(0, context.GetSkipContext(0, 1, tileMiColumnStart: 1));
        modeContext = context.GetYModeContext(0, 1, tileMiColumnStart: 1);
        Assert.Equal(Vp9PredictionMode.Dc, modeContext.Above);
        Assert.Equal(Vp9PredictionMode.Dc, modeContext.Left);
    }

    [Fact]
    public void GetYModeContext_WhenNeighborsAreSub8_UsesLibvpxAboveAndLeftBlockModes()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));
        context.SetModeInfo(
            miRow: 0,
            miColumn: 1,
            Vp9BlockSize.Block4X4,
            skip: false,
            Vp9TransformSize.Tx4X4,
            Vp9PredictionMode.D63,
            [Vp9PredictionMode.Dc, Vp9PredictionMode.D45, Vp9PredictionMode.D117, Vp9PredictionMode.D63]);
        context.SetModeInfo(
            miRow: 1,
            miColumn: 0,
            Vp9BlockSize.Block4X4,
            skip: false,
            Vp9TransformSize.Tx4X4,
            Vp9PredictionMode.TrueMotion,
            [Vp9PredictionMode.Horizontal, Vp9PredictionMode.D153, Vp9PredictionMode.D207, Vp9PredictionMode.TrueMotion]);

        var modeContext = context.GetYModeContext(1, 1, tileMiColumnStart: 0);

        Assert.Equal(Vp9PredictionMode.D117, modeContext.Above);
        Assert.Equal(Vp9PredictionMode.D153, modeContext.Left);
    }

    [Fact]
    public void GetTransformSizeContext_UsesMaxTransformWhenNeighborsAreMissing()
    {
        var context = Vp9KeyFrameSyntaxContext.Create(CreateHeader(miColumns: 16, miRows: 16));

        Assert.Equal(1, context.GetTransformSizeContext(0, 0, tileMiColumnStart: 0, Vp9BlockSize.Block32X32));

        context.SetModeInfo(
            miRow: 0,
            miColumn: 1,
            Vp9BlockSize.Block8X8,
            skip: false,
            Vp9TransformSize.Tx4X4,
            Vp9PredictionMode.Dc);
        context.SetModeInfo(
            miRow: 1,
            miColumn: 0,
            Vp9BlockSize.Block8X8,
            skip: false,
            Vp9TransformSize.Tx4X4,
            Vp9PredictionMode.Dc);

        Assert.Equal(0, context.GetTransformSizeContext(1, 1, tileMiColumnStart: 0, Vp9BlockSize.Block32X32));
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
            FrameType: Vp9FrameType.KeyFrame,
            ShowFrame: true,
            ErrorResilientMode: false,
            SyncCodeValid: true,
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
            RefreshFrameFlags: 0xff,
            FrameParallelDecodingMode: false,
            FrameContextIndex: 0,
            LoopFilter: new Vp9LoopFilterHeader(0, 0, false, false, [], []),
            Quantization: new Vp9QuantizationHeader(0, 0, 0, 0),
            Segmentation: new Vp9SegmentationHeader(false, false, false, false, false, [], []),
            TileInfo: new Vp9TileInfo(miColumns, miRows, 2, 0, 0, 0, 0),
            FirstPartitionSize: 0,
            IntraOnly: false,
            ResetFrameContextMode: 0,
            ReferenceFrameIndices: [],
            ReferenceFrameSignBiases: [],
            FrameSizeReferenceFlags: [],
            FrameSizeReferenceIndex: null,
            RenderSizeDifferent: false,
            AllowHighPrecisionMv: false,
            InterpolationFilter: Vp9InterpolationFilter.None);
    }
}
