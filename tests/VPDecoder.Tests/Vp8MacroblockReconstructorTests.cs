namespace VPDecoder.Tests;

public sealed class Vp8MacroblockReconstructorTests
{
    private static readonly Vp8DequantFactors DequantFactors = new(
        Y1Dc: 4,
        Y1Ac: 4,
        Y2Dc: 8,
        Y2Ac: 8,
        UvDc: 4,
        UvAc: 4);

    [Fact]
    public void TryReconstruct_ForSkippedDcMacroblock_PredictsNeutralYuv()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.Dc, Vp8MacroblockPredictionMode.Dc, skipCoefficients: true);
        var residual = new Vp8MacroblockResidual(Row: 0, Column: 0, Skipped: true, Blocks: []);

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        Assert.All(buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length).ToArray(), value => Assert.Equal(128, value));
        Assert.All(buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length).ToArray(), value => Assert.Equal(128, value));
        Assert.All(buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length).ToArray(), value => Assert.Equal(128, value));
    }

    [Fact]
    public void TryReconstruct_ForBPredDcOnlyResidual_AddsYDcBlock()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.BPred, Vp8MacroblockPredictionMode.Dc);
        var residual = CreateResidual(
            CreateBlockProbe(blockIndex: 0, coefficientIndex: 0, coefficient: 80));

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        Assert.Equal([168, 168, 168, 168], yPlane.Slice(0, 4).ToArray());
        Assert.Equal([168, 168, 168, 168], yPlane.Slice(3 * buffer.YPlane.Stride, 4).ToArray());
        Assert.Equal(168, yPlane[4]);
        Assert.Equal(168, yPlane[4 * buffer.YPlane.Stride]);
        Assert.Equal(168, yPlane[(15 * buffer.YPlane.Stride) + 15]);
    }

    [Fact]
    public void TryReconstruct_ForUvDcOnlyResidual_AddsChromaDcBlock()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.Dc, Vp8MacroblockPredictionMode.Dc);
        var residual = CreateResidual(
            CreateBlockProbe(blockIndex: 16, coefficientIndex: 0, coefficient: 80));

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        var uPlane = buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length);
        var vPlane = buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length);
        Assert.Equal([168, 168, 168, 168], uPlane.Slice(0, 4).ToArray());
        Assert.Equal(128, uPlane[4]);
        Assert.All(vPlane.ToArray(), value => Assert.Equal(128, value));
    }

    [Fact]
    public void TryReconstruct_ForUvAcResidual_AppliesInverseTransform()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.Dc, Vp8MacroblockPredictionMode.Dc);
        var residual = CreateResidual(
            CreateBlockProbe(blockIndex: 16, coefficientIndex: 0, coefficient: 80),
            CreateBlockProbe(blockIndex: 17, coefficientIndex: 1, coefficient: 80, eob: 2));

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        var uPlane = buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length);
        Assert.Equal([168, 168, 168, 168], uPlane.Slice(0, 4).ToArray());
        Assert.True(uPlane.Slice(4, 4).ToArray().Distinct().Count() > 1);
    }

    [Fact]
    public void TryReconstruct_WhenY2BlockIsNonZero_ReturnsUnsupportedFeature()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.Dc, Vp8MacroblockPredictionMode.Dc);
        var residual = CreateResidual(
            CreateBlockProbe(blockIndex: 24, coefficientIndex: 0, coefficient: 1));

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.False(succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, diagnostic?.Code);
        Assert.Contains("Y2 inverse Walsh", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReconstruct_ForBPredY1AcResidual_AppliesInverseTransform()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.BPred, Vp8MacroblockPredictionMode.Dc);
        var residual = CreateResidual(
            CreateBlockProbe(blockIndex: 0, coefficientIndex: 0, coefficient: 80),
            CreateBlockProbe(blockIndex: 1, coefficientIndex: 1, coefficient: 80, eob: 2));

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        Assert.Equal([168, 168, 168, 168], yPlane.Slice(0, 4).ToArray());
        Assert.True(yPlane.Slice(4, 4).ToArray().Distinct().Count() > 1);
    }

    [Fact]
    public void TryReconstruct_WhenNonBPredY1AcIsNonZero_ReturnsUnsupportedFeature()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(Vp8MacroblockPredictionMode.Dc, Vp8MacroblockPredictionMode.Dc);
        var residual = CreateResidual(
            CreateBlockProbe(blockIndex: 0, coefficientIndex: 1, coefficient: 80, eob: 2));

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.False(succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, diagnostic?.Code);
        Assert.Contains("non-B_PRED Y1 AC", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReconstruct_WhenBlockDirectionalModeHasAboveEdge_ReconstructsBPredBlock()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 32);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        for (var column = 0; column < 16; column++)
        {
            yPlane[(15 * buffer.YPlane.Stride) + column] = (byte)(10 + (column * 10));
        }

        var mode = CreateMode(
            Vp8MacroblockPredictionMode.BPred,
            Vp8MacroblockPredictionMode.Dc,
            row: 1,
            blockModes: Enumerable.Repeat(Vp8BlockPredictionMode.LeftDown, 16).ToArray());
        var residual = new Vp8MacroblockResidual(Row: 1, Column: 0, Skipped: false, Blocks: []);

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        Assert.Equal([20, 30, 40, 50], yPlane.Slice(16 * buffer.YPlane.Stride, 4).ToArray());
    }

    [Fact]
    public void TryReconstruct_WhenDirectionalModeLacksAboveEdge_ReturnsUnsupportedFeature()
    {
        var buffer = Vp8ReconstructionBuffer.Create(16, 16);
        var mode = CreateMode(
            Vp8MacroblockPredictionMode.BPred,
            Vp8MacroblockPredictionMode.Dc,
            blockModes: [Vp8BlockPredictionMode.LeftDown, .. Enumerable.Repeat(Vp8BlockPredictionMode.Dc, 15)]);
        var residual = CreateResidual();

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.False(succeeded);
        Assert.Equal(Vp8DecodeDiagnosticCode.UnsupportedFeature, diagnostic?.Code);
        Assert.Contains("B_LD_PRED", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReconstruct_WhenMacroblockIsClipped_CopiesVisiblePixelsOnly()
    {
        var buffer = Vp8ReconstructionBuffer.Create(17, 16);
        var yPlane = buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length);
        for (var row = 0; row < 16; row++)
        {
            yPlane[(row * buffer.YPlane.Stride) + 15] = 60;
        }

        var uPlane = buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length);
        var vPlane = buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length);
        for (var row = 0; row < 8; row++)
        {
            uPlane[(row * buffer.UPlane.Stride) + 7] = 70;
            vPlane[(row * buffer.VPlane.Stride) + 7] = 90;
        }

        var mode = CreateMode(
            Vp8MacroblockPredictionMode.Dc,
            Vp8MacroblockPredictionMode.Dc,
            row: 0,
            column: 1,
            skipCoefficients: true);
        var residual = new Vp8MacroblockResidual(Row: 0, Column: 1, Skipped: true, Blocks: []);

        var succeeded = Vp8MacroblockReconstructor.TryReconstruct(
            buffer,
            mode,
            residual,
            DequantFactors,
            out var diagnostic);

        Assert.True(succeeded);
        Assert.Null(diagnostic);
        for (var row = 0; row < 16; row++)
        {
            Assert.Equal(60, yPlane[(row * buffer.YPlane.Stride) + 16]);
            Assert.Equal(60, yPlane[(row * buffer.YPlane.Stride) + 15]);
        }

        for (var row = 0; row < 8; row++)
        {
            Assert.Equal(70, uPlane[(row * buffer.UPlane.Stride) + 8]);
            Assert.Equal(90, vPlane[(row * buffer.VPlane.Stride) + 8]);
            Assert.Equal(70, uPlane[(row * buffer.UPlane.Stride) + 7]);
            Assert.Equal(90, vPlane[(row * buffer.VPlane.Stride) + 7]);
        }
    }

    private static Vp8KeyFrameMacroblockMode CreateMode(
        Vp8MacroblockPredictionMode yMode,
        Vp8MacroblockPredictionMode uvMode,
        int row = 0,
        int column = 0,
        bool skipCoefficients = false,
        IReadOnlyList<Vp8BlockPredictionMode>? blockModes = null)
    {
        return new Vp8KeyFrameMacroblockMode(
            row,
            column,
            SegmentId: 0,
            skipCoefficients,
            yMode,
            uvMode,
            blockModes ?? (yMode == Vp8MacroblockPredictionMode.BPred
                ? Enumerable.Repeat(Vp8BlockPredictionMode.Dc, 16).ToArray()
                : []));
    }

    private static Vp8MacroblockResidual CreateResidual(params Vp8ResidualBlockProbe[] blocks)
    {
        return new Vp8MacroblockResidual(Row: 0, Column: 0, Skipped: false, blocks);
    }

    private static Vp8ResidualBlockProbe CreateBlockProbe(
        int blockIndex,
        int coefficientIndex,
        int coefficient,
        int eob = 1)
    {
        var coefficients = new int[16];
        coefficients[coefficientIndex] = coefficient;
        return new Vp8ResidualBlockProbe(
            blockIndex,
            BlockType: blockIndex is >= 16 and < 24 ? 2 : 0,
            StartCoefficient: coefficientIndex == 0 ? 0 : 1,
            InitialContext: 0,
            EffectiveEob: eob,
            new Vp8CoefficientBlock(eob, coefficients));
    }
}
