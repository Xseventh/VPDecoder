namespace VPDecoder.Tests;

public sealed class Vp9BlockReconstructorTests
{
    [Fact]
    public void ReconstructDcOnlyGroup_ForTx32AcBlock_AppliesInverseTransformAfterDcPrediction()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(32, 32);
        var geometry = new Vp9TileGeometry(
            TileRow: 0,
            TileColumn: 0,
            MiRowStart: 0,
            MiRowEnd: 4,
            MiColumnStart: 0,
            MiColumnEnd: 4,
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: 0));
        var modeInfo = new Vp9ModeInfoProbe(
            TileIndex: 0,
            MiRow: 0,
            MiColumn: 0,
            Vp9BlockSize.Block32X32,
            PartitionPath: [Vp9PartitionType.None],
            Skip: false,
            SkipContext: 0,
            Vp9TransformSize.Tx32X32,
            TransformSizeContext: 0,
            Vp9PredictionMode.Dc,
            Vp9PredictionMode.Dc,
            YSubModes: []);
        var coefficients = new int[1024];
        coefficients[0] = 16625;
        coefficients[1] = 420;
        coefficients[32] = -315;
        coefficients[33] = 210;
        var group = new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            Vp9BlockSize.Block32X32,
            Vp9TransformSize.Tx32X32,
            [
                new Vp9CoefficientBlockProbe(
                    TileIndex: 0,
                    Vp9TransformSize.Tx32X32,
                    Vp9TransformType.DctDct,
                    Row4: 0,
                    Column4: 0,
                    PlaneType: 0,
                    ReferenceType: 0,
                    InitialCoefficientContext: 0,
                    Eob: 4,
                    NonZeroCount: 4,
                    FirstNonZeroRasterIndex: 0,
                    LastNonZeroRasterIndex: 33,
                    coefficients,
                    CoefficientsSha256: "synthetic")
            ]);

        Vp9BlockReconstructor.ReconstructDcOnlyGroup(frameBuffer, geometry, modeInfo, group, plane: 0);

        var yPlane = frameBuffer.Pixels.AsSpan(frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length).ToArray();
        Assert.Equal("e88985af3f6e6f538a58eca3ee27c51f903a5c6dcbf96540d4a372f0509062ef", Hash(yPlane));
        Assert.True(yPlane.Distinct().Count() > 1);
    }

    [Fact]
    public void ReconstructDcOnlyGroup_ForTransformGrid_UsesBlockOffsetsInsteadOfListOrder()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(16, 16);
        var geometry = new Vp9TileGeometry(
            TileRow: 0,
            TileColumn: 0,
            MiRowStart: 0,
            MiRowEnd: 2,
            MiColumnStart: 0,
            MiColumnEnd: 2,
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: 0));
        var modeInfo = new Vp9ModeInfoProbe(
            TileIndex: 0,
            MiRow: 0,
            MiColumn: 0,
            Vp9BlockSize.Block16X16,
            PartitionPath: [Vp9PartitionType.None],
            Skip: false,
            SkipContext: 0,
            Vp9TransformSize.Tx4X4,
            TransformSizeContext: 0,
            Vp9PredictionMode.Dc,
            Vp9PredictionMode.Dc,
            YSubModes: []);

        var blocks = new List<Vp9CoefficientBlockProbe>();
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (row == 0 && column == 3)
                {
                    continue;
                }

                blocks.Add(CreateTx4Block(row, column, dc: 0));
            }
        }

        blocks.Add(CreateTx4Block(row4: 0, column4: 3, dc: 16625));
        var group = new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            Vp9BlockSize.Block16X16,
            Vp9TransformSize.Tx4X4,
            blocks);

        Vp9BlockReconstructor.ReconstructDcOnlyGroup(frameBuffer, geometry, modeInfo, group, plane: 0);

        var yPlane = frameBuffer.Pixels.AsSpan(frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length).ToArray();
        Assert.True(MaxBlockValue(yPlane, stride: 16, x: 12, y: 0, size: 4) >
            MaxBlockValue(yPlane, stride: 16, x: 12, y: 12, size: 4));
    }

    [Fact]
    public void ReconstructDcOnlyGroup_ForVerticalMode_UsesAbovePredictor()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(16, 16);
        var yPlane = frameBuffer.Pixels.AsSpan(frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length);
        byte[] above = [10, 20, 30, 40, 50, 60, 70, 80];
        above.CopyTo(yPlane.Slice(7 * frameBuffer.YStride, above.Length));
        var geometry = new Vp9TileGeometry(
            TileRow: 0,
            TileColumn: 0,
            MiRowStart: 0,
            MiRowEnd: 2,
            MiColumnStart: 0,
            MiColumnEnd: 2,
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: 0));
        var modeInfo = new Vp9ModeInfoProbe(
            TileIndex: 0,
            MiRow: 1,
            MiColumn: 0,
            Vp9BlockSize.Block8X8,
            PartitionPath: [Vp9PartitionType.None],
            Skip: true,
            SkipContext: 0,
            Vp9TransformSize.Tx8X8,
            TransformSizeContext: 0,
            Vp9PredictionMode.Vertical,
            Vp9PredictionMode.Dc,
            YSubModes: []);
        var group = new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            Vp9BlockSize.Block8X8,
            Vp9TransformSize.Tx8X8,
            [
                new Vp9CoefficientBlockProbe(
                    TileIndex: 0,
                    Vp9TransformSize.Tx8X8,
                    Vp9TransformType.AdstDct,
                    Row4: 0,
                    Column4: 0,
                    PlaneType: 0,
                    ReferenceType: 0,
                    InitialCoefficientContext: 0,
                    Eob: 0,
                    NonZeroCount: 0,
                    FirstNonZeroRasterIndex: -1,
                    LastNonZeroRasterIndex: -1,
                    new int[64],
                    CoefficientsSha256: "synthetic")
            ]);

        Vp9BlockReconstructor.ReconstructDcOnlyGroup(frameBuffer, geometry, modeInfo, group, plane: 0);

        for (var row = 8; row < 16; row++)
        {
            Assert.Equal(above, yPlane.Slice(row * frameBuffer.YStride, above.Length).ToArray());
        }
    }

    [Fact]
    public void AddInterResidualGroup_WhenBlocksAreEmpty_LeavesPredictionPixelsUnchanged()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(16, 16);
        Array.Fill(frameBuffer.Pixels, (byte)100, frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length);
        var beforeHash = Hash(frameBuffer.Pixels);
        var modeBlock = CreateInterModeBlock();
        var group = CreateInterTx8Group(dc: 0);

        Vp9BlockReconstructor.AddInterResidualGroup(frameBuffer, modeBlock, group, plane: 0);

        Assert.Equal(beforeHash, Hash(frameBuffer.Pixels));
    }

    [Fact]
    public void AddInterResidualGroup_WhenBlockHasDc_AddsResidualToPredictionPixels()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(16, 16);
        Array.Fill(frameBuffer.Pixels, (byte)100, frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length);
        var modeBlock = CreateInterModeBlock();
        var group = CreateInterTx8Group(dc: 512);

        Vp9BlockReconstructor.AddInterResidualGroup(frameBuffer, modeBlock, group, plane: 0);

        var yPlane = frameBuffer.Pixels.AsSpan(frameBuffer.YPlane.Offset, frameBuffer.YPlane.Length);
        Assert.Equal(108, yPlane[0]);
        Assert.Equal(108, yPlane[7]);
        Assert.Equal(108, yPlane[(7 * frameBuffer.YStride) + 7]);
        Assert.Equal(100, yPlane[8]);
        Assert.Equal(100, yPlane[8 * frameBuffer.YStride]);
        Assert.Equal(100, yPlane[(12 * frameBuffer.YStride) + 12]);
    }

    [Fact]
    public void AddInterResidualGroup_WhenGroupGeometryDoesNotMatchMode_ThrowsArgumentDiagnostic()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(16, 16);
        var modeBlock = CreateInterModeBlock();
        var group = CreateInterTx8Group(dc: 0) with
        {
            BlockSize = Vp9BlockSize.Block8X8
        };

        Assert.Throws<ArgumentException>(
            () => Vp9BlockReconstructor.AddInterResidualGroup(frameBuffer, modeBlock, group, plane: 0));
    }

    [Fact]
    public void AddInterResidualGroup_WhenCoefficientReferenceTypeIsIntra_ThrowsArgumentDiagnostic()
    {
        var frameBuffer = Vp9YuvFrameBuffer.Create(16, 16);
        var modeBlock = CreateInterModeBlock();
        var group = CreateInterTx8Group(dc: 0);
        var firstBlock = group.Blocks[0] with
        {
            ReferenceType = Vp9ResidualSyntax.IntraBlockReferenceType
        };
        group = group with
        {
            Blocks = [firstBlock, group.Blocks[1], group.Blocks[2], group.Blocks[3]]
        };

        Assert.Throws<ArgumentException>(
            () => Vp9BlockReconstructor.AddInterResidualGroup(frameBuffer, modeBlock, group, plane: 0));
    }

    private static Vp9CoefficientBlockProbe CreateTx4Block(int row4, int column4, int dc)
    {
        var coefficients = new int[16];
        coefficients[0] = dc;
        var hasDc = dc != 0;
        return new Vp9CoefficientBlockProbe(
            TileIndex: 0,
            Vp9TransformSize.Tx4X4,
            Vp9TransformType.DctDct,
            row4,
            column4,
            PlaneType: 0,
            ReferenceType: 0,
            InitialCoefficientContext: 0,
            Eob: hasDc ? 1 : 0,
            NonZeroCount: hasDc ? 1 : 0,
            FirstNonZeroRasterIndex: hasDc ? 0 : -1,
            LastNonZeroRasterIndex: hasDc ? 0 : -1,
            coefficients,
            CoefficientsSha256: "synthetic");
    }

    private static Vp9InterBlockModeInfoProbe CreateInterModeBlock()
    {
        var modeInfo = new Vp9InterModeInfoProbe(
            Vp9BlockSize.Block16X16,
            Skip: false,
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
            TileIndex: 0,
            MiRow: 0,
            MiColumn: 0,
            PartitionPath: [Vp9PartitionType.None],
            modeInfo);
    }

    private static Vp9CoefficientBlockGroupProbe CreateInterTx8Group(int dc)
    {
        return new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            Vp9BlockSize.Block16X16,
            Vp9TransformSize.Tx8X8,
            [
                CreateTx8Block(row4: 0, column4: 0, dc),
                CreateTx8Block(row4: 0, column4: 2, dc: 0),
                CreateTx8Block(row4: 2, column4: 0, dc: 0),
                CreateTx8Block(row4: 2, column4: 2, dc: 0)
            ]);
    }

    private static Vp9CoefficientBlockProbe CreateTx8Block(int row4, int column4, int dc)
    {
        var coefficients = new int[64];
        coefficients[0] = dc;
        var hasDc = dc != 0;
        return new Vp9CoefficientBlockProbe(
            TileIndex: 0,
            Vp9TransformSize.Tx8X8,
            Vp9TransformType.DctDct,
            row4,
            column4,
            PlaneType: 0,
            ReferenceType: Vp9ResidualSyntax.InterBlockReferenceType,
            InitialCoefficientContext: 0,
            Eob: hasDc ? 1 : 0,
            NonZeroCount: hasDc ? 1 : 0,
            FirstNonZeroRasterIndex: hasDc ? 0 : -1,
            LastNonZeroRasterIndex: hasDc ? 0 : -1,
            coefficients,
            CoefficientsSha256: "synthetic");
    }

    private static int MaxBlockValue(byte[] plane, int stride, int x, int y, int size)
    {
        var max = 0;
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                max = Math.Max(max, plane[((y + row) * stride) + x + column]);
            }
        }

        return max;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
