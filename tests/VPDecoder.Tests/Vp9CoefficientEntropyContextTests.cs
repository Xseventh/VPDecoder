namespace VPDecoder.Tests;

public sealed class Vp9CoefficientEntropyContextTests
{
    [Fact]
    public void SetTransformContext_WhenEobCrossesVisibleEdge_ClearsInvisibleTailContexts()
    {
        var context = Vp9CoefficientEntropyContext.Create(CreateHeader(miColumns: 8, miRows: 8));

        context.SetTransformContext(
            plane: 0,
            x4: 0,
            y4: 0,
            Vp9TransformSize.Tx8X8,
            hasEob: true,
            visibleWidth4: 1,
            visibleHeight4: 1);

        Assert.Equal(2, context.GetInitialContext(plane: 0, x4: 0, y4: 0, Vp9TransformSize.Tx4X4));
        Assert.Equal(0, context.GetInitialContext(plane: 0, x4: 1, y4: 1, Vp9TransformSize.Tx4X4));
    }

    [Fact]
    public void SetTransformContext_WhenNoEob_ClearsFullTransformFootprint()
    {
        var context = Vp9CoefficientEntropyContext.Create(CreateHeader(miColumns: 8, miRows: 8));
        context.SetTransformContext(
            plane: 0,
            x4: 0,
            y4: 0,
            Vp9TransformSize.Tx8X8,
            hasEob: true,
            visibleWidth4: 2,
            visibleHeight4: 2);

        context.SetTransformContext(
            plane: 0,
            x4: 0,
            y4: 0,
            Vp9TransformSize.Tx8X8,
            hasEob: false,
            visibleWidth4: 1,
            visibleHeight4: 1);

        Assert.Equal(0, context.GetInitialContext(plane: 0, x4: 0, y4: 0, Vp9TransformSize.Tx8X8));
    }

    [Fact]
    public void SetTransformContext_WhenLeftContextWraps_ClearsWrappedInvisibleTail()
    {
        var context = Vp9CoefficientEntropyContext.Create(CreateHeader(miColumns: 8, miRows: 8));

        context.SetTransformContext(
            plane: 0,
            x4: 0,
            y4: 15,
            Vp9TransformSize.Tx8X8,
            hasEob: true,
            visibleWidth4: 2,
            visibleHeight4: 1);

        Assert.Equal(2, context.GetInitialContext(plane: 0, x4: 0, y4: 15, Vp9TransformSize.Tx4X4));
        Assert.Equal(1, context.GetInitialContext(plane: 0, x4: 0, y4: 0, Vp9TransformSize.Tx4X4));
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
