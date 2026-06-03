namespace VPDecoder.Tests;

public sealed class Vp8ResidualSyntaxTests
{
    [Fact]
    public void ReadBlock_WhenTokenStreamStartsWithEob_ReturnsEmptyBlock()
    {
        var reader = new Vp8BoolReader(new byte[8]);
        var probabilities = Vp8CoefficientProbabilityContext.CreateDefault();

        var block = Vp8ResidualSyntax.ReadBlock(
            ref reader,
            probabilities,
            blockType: 0,
            initialCoefficientContext: 0,
            startCoefficient: 0);

        Assert.Equal(0, block.Eob);
        Assert.All(block.Coefficients, coefficient => Assert.Equal(0, coefficient));
    }

    [Fact]
    public void ReadBlock_WhenStartCoefficientSkipsDcAndSeesEob_ReturnsEmptyBlock()
    {
        var reader = new Vp8BoolReader(new byte[8]);
        var probabilities = Vp8CoefficientProbabilityContext.CreateDefault();

        var block = Vp8ResidualSyntax.ReadBlock(
            ref reader,
            probabilities,
            blockType: 0,
            initialCoefficientContext: 0,
            startCoefficient: 1);

        Assert.Equal(0, block.Eob);
        Assert.All(block.Coefficients, coefficient => Assert.Equal(0, coefficient));
    }

    [Fact]
    public void ReadBlock_WhenCoefficientContextIsInvalid_ThrowsArgumentOutOfRange()
    {
        var reader = new Vp8BoolReader(new byte[8]);
        var probabilities = Vp8CoefficientProbabilityContext.CreateDefault();

        try
        {
            Vp8ResidualSyntax.ReadBlock(
                ref reader,
                probabilities,
                blockType: 0,
                initialCoefficientContext: 3,
                startCoefficient: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Assert.Equal("initialCoefficientContext", ex.ParamName);
            return;
        }

        Assert.Fail("Expected invalid VP8 coefficient context to throw.");
    }
}
