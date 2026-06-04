namespace VPDecoder.Tests;

public sealed class Vp9ModeInfoSyntaxTests
{
    [Theory]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx32X32)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx32X32)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx32X32)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx32X32)]
    public void GetMaximumTransformSize_ReturnsLibvpxLookup(Vp9BlockSize blockSize, Vp9TransformSize expected)
    {
        Assert.Equal(expected, Vp9ModeInfoSyntax.GetMaximumTransformSize(blockSize));
    }

    [Fact]
    public void ReadTransformSize_WhenSelectIsDisallowed_ReturnsMaximumBlockTransform()
    {
        var reader = new Vp9BoolReader([0x00]);
        var compressedHeader = new Vp9CompressedHeader(
            Vp9TransformMode.Select,
            Vp9FrameContext.CreateDefault(),
            TxProbabilityUpdateCount: 0,
            CoefficientProbabilityUpdateCount: 0,
            SkipProbabilityUpdateCount: 0);

        var transformSize = Vp9ModeInfoSyntax.ReadTransformSize(
            ref reader,
            compressedHeader,
            Vp9BlockSize.Block32X32,
            allowSelect: false,
            out var transformSizeContext);

        Assert.Equal(Vp9TransformSize.Tx32X32, transformSize);
        Assert.Equal(0, transformSizeContext);
        Assert.False(reader.HasError);
    }

    [Fact]
    public void ReadTransformSize_WhenSelectIsDisallowed_DoesNotReadSelectedTransformContext()
    {
        var reader = new Vp9BoolReader([0x00]);
        var compressedHeader = new Vp9CompressedHeader(
            Vp9TransformMode.Select,
            Vp9FrameContext.CreateDefault(),
            TxProbabilityUpdateCount: 0,
            CoefficientProbabilityUpdateCount: 0,
            SkipProbabilityUpdateCount: 0);

        var transformSize = Vp9ModeInfoSyntax.ReadTransformSize(
            ref reader,
            compressedHeader,
            Vp9BlockSize.Block16X16,
            transformSizeContext: 99,
            allowSelect: false);

        Assert.Equal(Vp9TransformSize.Tx16X16, transformSize);
        Assert.False(reader.HasError);
    }

    [Fact]
    public void ReadInterPredictionMode_AllZeroBoolReader_ReturnsZeroMv()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);

        var mode = Vp9InterModeInfoSyntax.ReadInterPredictionMode(
            ref reader,
            Vp9FrameContext.CreateDefault(),
            interModeContext: 0);

        Assert.Equal(Vp9InterPredictionMode.ZeroMv, mode);
        Assert.False(reader.HasError);
    }

    [Fact]
    public void ReadSingleReferenceFrame_AllZeroBoolReader_ReturnsLastFrame()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);

        var (referenceFrame, context1) = Vp9InterModeInfoSyntax.ReadSingleReferenceFrame(
            ref reader,
            Vp9FrameContext.CreateDefault(),
            context0: 0,
            context1: 1);

        Assert.Equal(Vp9InterReferenceFrame.Last, referenceFrame);
        Assert.Null(context1);
        Assert.False(reader.HasError);
    }

    [Fact]
    public void ReadSwitchableInterpolationFilter_AllZeroBoolReader_ReturnsEightTap()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);

        var interpolationFilter = Vp9InterModeInfoSyntax.ReadSwitchableInterpolationFilter(
            ref reader,
            Vp9FrameContext.CreateDefault(),
            switchableInterpolationContext: 0);

        Assert.Equal(Vp9InterpolationFilter.EightTap, interpolationFilter);
        Assert.False(reader.HasError);
    }

    [Fact]
    public void TryReadSupportedInterBlock_WhenReferenceModeIsNotSingle_ReturnsUnsupportedDiagnostic()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);
        var frameHeader = CreateOrdinaryInterFrameHeader();
        var compressedHeader = CreateCompressedHeader(Vp9ReferenceMode.Compound);

        Assert.False(Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
            ref reader,
            frameHeader,
            compressedHeader,
            Vp9BlockSize.Block16X16,
            CreateDefaultInterContexts(),
            out var probe,
            out var diagnostic));

        Assert.Null(probe);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("compound", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadSupportedInterBlock_WhenSegmentationIsEnabled_ReturnsUnsupportedDiagnostic()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);
        var header = CreateOrdinaryInterFrameHeader();
        var frameHeader = header with
        {
            Segmentation = header.Segmentation with { Enabled = true }
        };
        var compressedHeader = CreateCompressedHeader();

        Assert.False(Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
            ref reader,
            frameHeader,
            compressedHeader,
            Vp9BlockSize.Block16X16,
            CreateDefaultInterContexts(),
            out var probe,
            out var diagnostic));

        Assert.Null(probe);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("segmentation", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadSupportedInterBlock_WhenSub8X8Block_ReturnsUnsupportedDiagnostic()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);
        var frameHeader = CreateOrdinaryInterFrameHeader();
        var compressedHeader = CreateCompressedHeader();

        Assert.False(Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
            ref reader,
            frameHeader,
            compressedHeader,
            Vp9BlockSize.Block4X4,
            CreateDefaultInterContexts(),
            out var probe,
            out var diagnostic));

        Assert.Null(probe);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("sub-8x8", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadSupportedInterBlock_WhenInterFrameIntraBlock_ReturnsUnsupportedDiagnostic()
    {
        var reader = new Vp9BoolReader([0x00, 0x00]);
        var frameHeader = CreateOrdinaryInterFrameHeader();
        var compressedHeader = CreateCompressedHeader();

        Assert.False(Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
            ref reader,
            frameHeader,
            compressedHeader,
            Vp9BlockSize.Block16X16,
            CreateDefaultInterContexts(),
            out var probe,
            out var diagnostic));

        Assert.Null(probe);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("intra blocks", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Vp9FrameHeader CreateOrdinaryInterFrameHeader()
    {
        return Vp9FrameHeaderParser.Parse(Vp9TestPackets.CreateOrdinaryInterFramePacket());
    }

    private static Vp9CompressedHeader CreateCompressedHeader(Vp9ReferenceMode referenceMode = Vp9ReferenceMode.Single)
    {
        return new Vp9CompressedHeader(
            Vp9TransformMode.Only4X4,
            Vp9FrameContext.CreateDefault(),
            TxProbabilityUpdateCount: 0,
            CoefficientProbabilityUpdateCount: 0,
            SkipProbabilityUpdateCount: 0,
            ReferenceMode: referenceMode);
    }

    private static Vp9InterModeInfoContexts CreateDefaultInterContexts()
    {
        return new Vp9InterModeInfoContexts(
            Skip: 0,
            IntraInter: 0,
            TransformSize: 0,
            SingleReference0: 0,
            SingleReference1: 1,
            InterMode: 0,
            SwitchableInterpolation: 0);
    }
}
