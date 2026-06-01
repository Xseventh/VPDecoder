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
