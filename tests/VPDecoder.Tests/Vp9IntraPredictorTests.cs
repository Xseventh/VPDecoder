namespace VPDecoder.Tests;

public sealed class Vp9IntraPredictorTests
{
    [Fact]
    public void GetDcValue_WhenNoEdgesAreAvailable_Returns128()
    {
        Assert.Equal(128, Vp9IntraPredictor.GetDcValue(32, [], []));
    }

    [Fact]
    public void GetDcValue_WhenOnlyAboveIsAvailable_AveragesAbove()
    {
        byte[] above = [10, 20, 30, 40];

        Assert.Equal(25, Vp9IntraPredictor.GetDcValue(4, above, []));
    }

    [Fact]
    public void GetDcValue_WhenBothEdgesAreAvailable_AveragesBoth()
    {
        byte[] above = [10, 20, 30, 40];
        byte[] left = [50, 60, 70, 80];

        Assert.Equal(45, Vp9IntraPredictor.GetDcValue(4, above, left));
    }

    [Fact]
    public void PredictDc_FillsDestinationBlock()
    {
        var destination = new byte[8 * 8];

        Vp9IntraPredictor.PredictDc(destination, 8, 4, [20, 20, 20, 20], [60, 60, 60, 60]);

        for (var y = 0; y < 4; y++)
        {
            Assert.Equal([40, 40, 40, 40], destination.Skip(y * 8).Take(4).ToArray());
        }
    }
}
