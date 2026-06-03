namespace VPDecoder.Tests;

public sealed class Vp9ResidualSyntaxTests
{
    [Theory]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx32X32)]
    public void GetUvTransformSizeForYuv420_UsesLibvpxLookup(
        Vp9BlockSize blockSize,
        Vp9TransformSize yTransformSize,
        Vp9TransformSize expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetUvTransformSizeForYuv420(blockSize, yTransformSize));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 14)]
    [InlineData(8, 16)]
    [InlineData(15, 30)]
    public void GetPlaneLeftContextOffset_ForLuma_UsesLuma4x4Rows(int miRow, int expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetPlaneLeftContextOffset(miRow, plane: 0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(8, 0)]
    [InlineData(10, 2)]
    [InlineData(15, 7)]
    [InlineData(16, 0)]
    public void GetPlaneLeftContextOffset_ForYuv420Chroma_MatchesLibvpxPointerOffset(int miRow, int expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetPlaneLeftContextOffset(miRow, plane: 1));
        Assert.Equal(expected, Vp9ResidualSyntax.GetPlaneLeftContextOffset(miRow, plane: 2));
    }

    [Theory]
    [InlineData(false, Vp9ResidualSyntax.IntraBlockReferenceType)]
    [InlineData(true, Vp9ResidualSyntax.InterBlockReferenceType)]
    public void GetReferenceType_SeparatesIntraAndInterCoefficientContexts(bool isInterBlock, int expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetReferenceType(isInterBlock));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GetInterTransformType_ForInterBlock_AlwaysUsesDctDct(int plane)
    {
        var modeInfo = CreateInterModeInfo(isInterBlock: true);

        Assert.Equal(Vp9TransformType.DctDct, Vp9ResidualSyntax.GetInterTransformType(modeInfo, plane));
    }

    [Fact]
    public void GetInterTransformType_WhenInterFrameIntraBlock_ThrowsUnsupported()
    {
        var modeInfo = CreateInterModeInfo(isInterBlock: false);

        Assert.Throws<NotSupportedException>(() => Vp9ResidualSyntax.GetInterTransformType(modeInfo, plane: 0));
    }

    [Fact]
    public void CreateSkippedInterPlaneCoefficientBlocks_ForSkippedLumaBlock_UsesInterReferenceType()
    {
        var header = CreateInterHeader(miColumns: 2, miRows: 2);
        var modeBlock = CreateInterModeBlock(skip: true);

        var group = Vp9ResidualSyntax.CreateSkippedInterPlaneCoefficientBlocks(header, modeBlock, plane: 0);

        Assert.Equal(modeBlock.TileIndex, group.TileIndex);
        Assert.Equal(Vp9BlockSize.Block16X16, group.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx8X8, group.TransformSize);
        Assert.Equal(4, group.Blocks.Count);
        Assert.Equal([0, 0, 2, 2], group.Blocks.Select(block => block.Row4).ToArray());
        Assert.Equal([0, 2, 0, 2], group.Blocks.Select(block => block.Column4).ToArray());
        Assert.All(group.Blocks, block =>
        {
            Assert.Equal(0, block.PlaneType);
            Assert.Equal(Vp9ResidualSyntax.InterBlockReferenceType, block.ReferenceType);
            Assert.Equal(Vp9TransformType.DctDct, block.TransformType);
            Assert.Equal(0, block.InitialCoefficientContext);
            Assert.Equal(0, block.Eob);
            Assert.Equal(0, block.NonZeroCount);
            Assert.Equal(-1, block.FirstNonZeroRasterIndex);
            Assert.Equal(-1, block.LastNonZeroRasterIndex);
            Assert.All(block.DequantizedCoefficients, coefficient => Assert.Equal(0, coefficient));
        });
    }

    [Fact]
    public void CreateSkippedInterPlaneCoefficientBlocks_ForSkippedChromaBlock_UsesUvTransformSize()
    {
        var header = CreateInterHeader(miColumns: 2, miRows: 2);
        var modeBlock = CreateInterModeBlock(skip: true);

        var group = Vp9ResidualSyntax.CreateSkippedInterPlaneCoefficientBlocks(header, modeBlock, plane: 1);

        Assert.Equal(Vp9TransformSize.Tx8X8, group.TransformSize);
        Assert.Single(group.Blocks);
        var block = group.Blocks[0];
        Assert.Equal(1, block.PlaneType);
        Assert.Equal(Vp9ResidualSyntax.InterBlockReferenceType, block.ReferenceType);
        Assert.Equal(Vp9TransformType.DctDct, block.TransformType);
        Assert.Equal(0, block.Eob);
        Assert.Equal(64, block.DequantizedCoefficients.Length);
        Assert.All(block.DequantizedCoefficients, coefficient => Assert.Equal(0, coefficient));
    }

    [Fact]
    public void CreateSkippedInterPlaneCoefficientBlocks_WhenInterBlockIsNotSkipped_ThrowsUnsupported()
    {
        var header = CreateInterHeader(miColumns: 2, miRows: 2);
        var modeBlock = CreateInterModeBlock(skip: false);

        var ex = Assert.Throws<NotSupportedException>(
            () => Vp9ResidualSyntax.CreateSkippedInterPlaneCoefficientBlocks(header, modeBlock, plane: 0));
        Assert.Contains("non-skipped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Vp9InterModeInfoProbe CreateInterModeInfo(bool isInterBlock)
    {
        return new Vp9InterModeInfoProbe(
            Vp9BlockSize.Block16X16,
            Skip: false,
            SkipContext: 0,
            IsInterBlock: isInterBlock,
            IntraInterContext: 0,
            Vp9TransformSize.Tx16X16,
            TransformSizeContext: 1,
            Vp9ReferenceMode.Single,
            Vp9InterReferenceFrame.Last,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            Vp9InterPredictionMode.ZeroMv,
            InterModeContext: 0,
            Vp9InterpolationFilter.EightTap);
    }

    private static Vp9InterBlockModeInfoProbe CreateInterModeBlock(bool skip)
    {
        var modeInfo = new Vp9InterModeInfoProbe(
            Vp9BlockSize.Block16X16,
            skip,
            SkipContext: 0,
            IsInterBlock: true,
            IntraInterContext: 0,
            Vp9TransformSize.Tx8X8,
            TransformSizeContext: 1,
            Vp9ReferenceMode.Single,
            Vp9InterReferenceFrame.Last,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            Vp9InterPredictionMode.ZeroMv,
            InterModeContext: 0,
            Vp9InterpolationFilter.EightTap);

        return new Vp9InterBlockModeInfoProbe(
            TileIndex: 2,
            MiRow: 0,
            MiColumn: 0,
            PartitionPath: [Vp9PartitionType.None],
            modeInfo);
    }

    private static Vp9FrameHeader CreateInterHeader(int miColumns, int miRows)
    {
        var header = Vp9FrameHeaderParser.Parse(Vp9TestPackets.CreateOrdinaryInterFramePacket());
        return header with
        {
            Width = miColumns * 8,
            Height = miRows * 8,
            RenderWidth = miColumns * 8,
            RenderHeight = miRows * 8,
            TileInfo = new Vp9TileInfo(
                MiColumns: miColumns,
                MiRows: miRows,
                SuperblockColumns: 1,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 0,
                Log2TileRows: 0)
        };
    }
}
