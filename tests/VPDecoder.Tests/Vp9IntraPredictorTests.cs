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

    [Fact]
    public void PredictVertical_CopiesAboveRowIntoEveryRow()
    {
        var destination = new byte[4 * 4];

        Vp9IntraPredictor.PredictVertical(destination, 4, 4, [10, 20, 30, 40]);

        for (var y = 0; y < 4; y++)
        {
            Assert.Equal([10, 20, 30, 40], destination.Skip(y * 4).Take(4).ToArray());
        }
    }

    [Fact]
    public void PredictHorizontal_FillsEachRowFromLeftEdge()
    {
        var destination = new byte[4 * 4];

        Vp9IntraPredictor.PredictHorizontal(destination, 4, 4, [5, 15, 25, 35]);

        Assert.Equal([5, 5, 5, 5], destination.Take(4).ToArray());
        Assert.Equal([15, 15, 15, 15], destination.Skip(4).Take(4).ToArray());
        Assert.Equal([25, 25, 25, 25], destination.Skip(8).Take(4).ToArray());
        Assert.Equal([35, 35, 35, 35], destination.Skip(12).Take(4).ToArray());
    }

    [Fact]
    public void PredictTrueMotion_UsesAboveLeftAndClipsPixels()
    {
        var destination = new byte[4 * 4];

        Vp9IntraPredictor.PredictTrueMotion(
            destination,
            stride: 4,
            size: 4,
            above: [100, 140, 230, 250],
            left: [90, 120, 200, 250],
            aboveLeft: 80);

        Assert.Equal([110, 150, 240, 255], destination.Take(4).ToArray());
        Assert.Equal([140, 180, 255, 255], destination.Skip(4).Take(4).ToArray());
        Assert.Equal([220, 255, 255, 255], destination.Skip(8).Take(4).ToArray());
        Assert.Equal([255, 255, 255, 255], destination.Skip(12).Take(4).ToArray());
    }

    [Theory]
    [InlineData(Vp9PredictionMode.D45, new byte[] { 20, 30, 40, 40, 30, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40 })]
    [InlineData(Vp9PredictionMode.D63, new byte[] { 15, 25, 35, 45, 20, 30, 40, 50, 25, 35, 40, 40, 30, 40, 40, 40 })]
    [InlineData(Vp9PredictionMode.D117, new byte[] { 8, 15, 25, 35, 23, 11, 20, 30, 56, 8, 15, 25, 80, 23, 11, 20 })]
    [InlineData(Vp9PredictionMode.D135, new byte[] { 23, 11, 20, 30, 56, 23, 11, 20, 80, 56, 23, 11, 90, 80, 56, 23 })]
    [InlineData(Vp9PredictionMode.D153, new byte[] { 38, 23, 11, 20, 75, 56, 38, 23, 85, 80, 75, 56, 95, 90, 85, 80 })]
    [InlineData(Vp9PredictionMode.D207, new byte[] { 75, 80, 85, 90, 85, 90, 95, 98, 95, 98, 100, 100, 100, 100, 100, 100 })]
    public void Predict_ForDirectionalModes_UsesLibvpxAverages(Vp9PredictionMode mode, byte[] expected)
    {
        var destination = new byte[4 * 4];

        Vp9IntraPredictor.Predict(
            mode,
            destination,
            stride: 4,
            size: 4,
            above: [10, 20, 30, 40, 50, 60],
            left: [70, 80, 90, 100],
            aboveLeft: 5);

        Assert.Equal(expected, destination);
    }

    [Fact]
    public void PredictTrueMotion_WithoutAboveLeft_ThrowsConcreteDiagnostic()
    {
        var destination = new byte[4 * 4];

        var exception = Assert.Throws<NotSupportedException>(() =>
            Vp9IntraPredictor.PredictTrueMotion(
                destination,
                stride: 4,
                size: 4,
                above: [1, 2, 3, 4],
                left: [5, 6, 7, 8],
                aboveLeft: null));

        Assert.Contains("VP9 TrueMotion intra predictor requires an above-left sample.", exception.Message);
    }
}
