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

    private static Vp9InterModeInfoProbe CreateModeInfo(
        Vp9BlockSize blockSize,
        bool skip,
        Vp9TransformSize transformSize)
    {
        return new Vp9InterModeInfoProbe(
            blockSize,
            skip,
            SkipContext: 0,
            IsInterBlock: true,
            IntraInterContext: 0,
            TransformSize: transformSize,
            TransformSizeContext: 0,
            ReferenceMode: Vp9ReferenceMode.Single,
            ReferenceFrame: Vp9InterReferenceFrame.Last,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            PredictionMode: Vp9InterPredictionMode.ZeroMv,
            InterModeContext: 0,
            InterpolationFilter: Vp9InterpolationFilter.EightTap);
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
