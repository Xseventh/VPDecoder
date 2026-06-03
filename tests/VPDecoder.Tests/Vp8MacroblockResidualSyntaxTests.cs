namespace VPDecoder.Tests;

public sealed class Vp8MacroblockResidualSyntaxTests
{
    [Fact]
    public void ReadMacroblock_ForNonBPredKeyFrameBlock_ReadsY2Y1AndUvBlocks()
    {
        var reader = new Vp8BoolReader(new byte[16]);
        var probabilities = Vp8CoefficientProbabilityContext.CreateDefault();
        var context = Vp8ResidualEntropyContext.Create(macroblockColumns: 1);

        var residual = Vp8MacroblockResidualSyntax.ReadMacroblock(
            ref reader,
            probabilities,
            context,
            CreateMode(Vp8MacroblockPredictionMode.Dc));

        Assert.False(residual.Skipped);
        Assert.Equal(25, residual.Blocks.Count);
        Assert.Equal(24, residual.Blocks[0].BlockIndex);
        Assert.Equal(1, residual.Blocks[0].BlockType);
        Assert.Equal(0, residual.Blocks[0].StartCoefficient);
        Assert.Equal(0, residual.Blocks[0].EffectiveEob);
        Assert.Equal(0, residual.Blocks[1].BlockIndex);
        Assert.Equal(0, residual.Blocks[1].BlockType);
        Assert.Equal(1, residual.Blocks[1].StartCoefficient);
        Assert.Equal(1, residual.Blocks[1].EffectiveEob);
        Assert.Equal(8, residual.Blocks.Count(block => block.BlockType == 2));
        Assert.All(residual.Blocks.Select(block => block.Block), block => Assert.Equal(0, block.Eob));
    }

    [Fact]
    public void ReadMacroblock_ForBPredKeyFrameBlock_ReadsY1AndUvBlocks()
    {
        var reader = new Vp8BoolReader(new byte[16]);
        var probabilities = Vp8CoefficientProbabilityContext.CreateDefault();
        var context = Vp8ResidualEntropyContext.Create(macroblockColumns: 1);

        var residual = Vp8MacroblockResidualSyntax.ReadMacroblock(
            ref reader,
            probabilities,
            context,
            CreateMode(Vp8MacroblockPredictionMode.BPred));

        Assert.False(residual.Skipped);
        Assert.Equal(24, residual.Blocks.Count);
        Assert.Equal(0, residual.Blocks[0].BlockIndex);
        Assert.Equal(3, residual.Blocks[0].BlockType);
        Assert.Equal(0, residual.Blocks[0].StartCoefficient);
        Assert.Equal(0, residual.Blocks[0].EffectiveEob);
        Assert.DoesNotContain(residual.Blocks, block => block.BlockIndex == 24);
        Assert.Equal(8, residual.Blocks.Count(block => block.BlockType == 2));
    }

    [Fact]
    public void ReadMacroblock_WhenSkipCoefficients_ClearsEntropyContexts()
    {
        var reader = new Vp8BoolReader(new byte[1]);
        var probabilities = Vp8CoefficientProbabilityContext.CreateDefault();
        var context = Vp8ResidualEntropyContext.Create(macroblockColumns: 1);
        context.SetAbove(0, 0, hasNonZero: true);
        context.SetLeft(0, hasNonZero: true);

        var residual = Vp8MacroblockResidualSyntax.ReadMacroblock(
            ref reader,
            probabilities,
            context,
            CreateMode(Vp8MacroblockPredictionMode.Dc, skipCoefficients: true));

        Assert.True(residual.Skipped);
        Assert.Empty(residual.Blocks);
        Assert.Equal(0, context.GetAbove(0, 0));
        Assert.Equal(0, context.GetLeft(0));
    }

    private static Vp8KeyFrameMacroblockMode CreateMode(
        Vp8MacroblockPredictionMode yMode,
        bool skipCoefficients = false)
    {
        return new Vp8KeyFrameMacroblockMode(
            Row: 0,
            Column: 0,
            SegmentId: 0,
            skipCoefficients,
            yMode,
            UvMode: Vp8MacroblockPredictionMode.Dc,
            BlockModes: yMode == Vp8MacroblockPredictionMode.BPred
                ? Enumerable.Repeat(Vp8BlockPredictionMode.Dc, 16).ToArray()
                : []);
    }
}
