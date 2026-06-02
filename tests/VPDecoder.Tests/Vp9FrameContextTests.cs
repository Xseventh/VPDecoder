namespace VPDecoder.Tests;

public sealed class Vp9FrameContextTests
{
    [Fact]
    public void CreateDefault_LoadsLibvpxCoefficientProbabilityTables()
    {
        var context = Vp9FrameContext.CreateDefault();

        Assert.Equal(4 * 2 * 2 * 6 * 6 * 3, context.CoefficientProbabilities.Length);
        Assert.Equal(195, GetCoefficientProbability(context, 0, 0, 0, 0, 0, 0));
        Assert.Equal(29, GetCoefficientProbability(context, 0, 0, 0, 0, 0, 1));
        Assert.Equal(183, GetCoefficientProbability(context, 0, 0, 0, 0, 0, 2));
        Assert.Equal(0, GetCoefficientProbability(context, 0, 0, 0, 0, 3, 0));
        Assert.Equal(125, GetCoefficientProbability(context, 1, 0, 0, 0, 0, 0));
        Assert.Equal(7, GetCoefficientProbability(context, 2, 0, 0, 0, 0, 0));
        Assert.Equal(17, GetCoefficientProbability(context, 3, 0, 0, 0, 0, 0));
        Assert.Equal(1, GetCoefficientProbability(context, 3, 1, 1, 5, 5, 0));
        Assert.Equal(16, GetCoefficientProbability(context, 3, 1, 1, 5, 5, 1));
        Assert.Equal(6, GetCoefficientProbability(context, 3, 1, 1, 5, 5, 2));
    }

    [Fact]
    public void Clone_CopiesMutableProbabilityTables()
    {
        var context = Vp9FrameContext.CreateDefault();

        var clone = context.Clone();
        clone.SkipProbabilities[0] = 1;
        clone.CoefficientProbabilities[0] = 2;
        clone.TxProbabilities.EightByEight[0, 0] = 3;

        Assert.Equal(192, context.SkipProbabilities[0]);
        Assert.Equal(195, context.CoefficientProbabilities[0]);
        Assert.Equal(100, context.TxProbabilities.EightByEight[0, 0]);
    }

    private static byte GetCoefficientProbability(
        Vp9FrameContext context,
        int transformSize,
        int planeType,
        int referenceType,
        int coefficientBand,
        int coefficientContext,
        int node)
    {
        return context.CoefficientProbabilities[context.GetCoefficientProbabilityIndex(
            transformSize,
            planeType,
            referenceType,
            coefficientBand,
            coefficientContext,
            node)];
    }
}
