namespace VPDecoder.Tests;

public sealed class Vp8IntraPredictorTests
{
    [Fact]
    public void GetDcValue_WhenNoEdgesAreAvailable_Returns128()
    {
        Assert.Equal(128, Vp8IntraPredictor.GetDcValue(16, [], [], hasAbove: false, hasLeft: false));
    }

    [Fact]
    public void GetDcValue_WhenOnlyAboveIsAvailable_AveragesAbove()
    {
        byte[] above = [10, 20, 30, 40];

        Assert.Equal(25, Vp8IntraPredictor.GetDcValue(4, above, [], hasAbove: true, hasLeft: false));
    }

    [Fact]
    public void GetDcValue_WhenOnlyLeftIsAvailable_AveragesLeft()
    {
        byte[] left = [50, 60, 70, 80];

        Assert.Equal(65, Vp8IntraPredictor.GetDcValue(4, [], left, hasAbove: false, hasLeft: true));
    }

    [Fact]
    public void GetDcValue_WhenBothEdgesAreAvailable_AveragesBoth()
    {
        byte[] above = [10, 20, 30, 40];
        byte[] left = [50, 60, 70, 80];

        Assert.Equal(45, Vp8IntraPredictor.GetDcValue(4, above, left, hasAbove: true, hasLeft: true));
    }

    [Fact]
    public void PredictMacroblock_Dc_FillsRequestedRegion()
    {
        var plane = new byte[8 * 8];

        Vp8IntraPredictor.PredictMacroblock(
            plane,
            stride: 8,
            x: 2,
            y: 1,
            size: 4,
            Vp8MacroblockPredictionMode.Dc,
            above: [20, 20, 20, 20],
            left: [60, 60, 60, 60],
            topLeft: 0,
            hasAbove: true,
            hasLeft: true);

        Assert.Equal([40, 40, 40, 40], plane.Skip((1 * 8) + 2).Take(4).ToArray());
        Assert.Equal([40, 40, 40, 40], plane.Skip((2 * 8) + 2).Take(4).ToArray());
        Assert.Equal([40, 40, 40, 40], plane.Skip((3 * 8) + 2).Take(4).ToArray());
        Assert.Equal([40, 40, 40, 40], plane.Skip((4 * 8) + 2).Take(4).ToArray());
    }

    [Fact]
    public void PredictMacroblock_Vertical_CopiesAboveRowIntoEveryRow()
    {
        var plane = new byte[4 * 4];

        Vp8IntraPredictor.PredictMacroblock(
            plane,
            stride: 4,
            x: 0,
            y: 0,
            size: 4,
            Vp8MacroblockPredictionMode.Vertical,
            above: [10, 20, 30, 40],
            left: [],
            topLeft: 0,
            hasAbove: true,
            hasLeft: false);

        for (var row = 0; row < 4; row++)
        {
            Assert.Equal([10, 20, 30, 40], plane.Skip(row * 4).Take(4).ToArray());
        }
    }

    [Fact]
    public void PredictMacroblock_Horizontal_FillsRowsFromLeftEdge()
    {
        var plane = new byte[4 * 4];

        Vp8IntraPredictor.PredictMacroblock(
            plane,
            stride: 4,
            x: 0,
            y: 0,
            size: 4,
            Vp8MacroblockPredictionMode.Horizontal,
            above: [],
            left: [5, 15, 25, 35],
            topLeft: 0,
            hasAbove: false,
            hasLeft: true);

        Assert.Equal([5, 5, 5, 5], plane.Take(4).ToArray());
        Assert.Equal([15, 15, 15, 15], plane.Skip(4).Take(4).ToArray());
        Assert.Equal([25, 25, 25, 25], plane.Skip(8).Take(4).ToArray());
        Assert.Equal([35, 35, 35, 35], plane.Skip(12).Take(4).ToArray());
    }

    [Fact]
    public void PredictMacroblock_TrueMotion_UsesEdgesAndClipsPixels()
    {
        var plane = new byte[4 * 4];

        Vp8IntraPredictor.PredictMacroblock(
            plane,
            stride: 4,
            x: 0,
            y: 0,
            size: 4,
            Vp8MacroblockPredictionMode.TrueMotion,
            above: [100, 140, 230, 250],
            left: [90, 120, 200, 250],
            topLeft: 80,
            hasAbove: true,
            hasLeft: true);

        Assert.Equal([110, 150, 240, 255], plane.Take(4).ToArray());
        Assert.Equal([140, 180, 255, 255], plane.Skip(4).Take(4).ToArray());
        Assert.Equal([220, 255, 255, 255], plane.Skip(8).Take(4).ToArray());
        Assert.Equal([255, 255, 255, 255], plane.Skip(12).Take(4).ToArray());
    }

    [Fact]
    public void PredictBlock_TrueMotion_FillsFourByFourBlock()
    {
        var plane = new byte[8 * 8];

        Vp8IntraPredictor.PredictBlock(
            plane,
            stride: 8,
            x: 2,
            y: 3,
            Vp8BlockPredictionMode.TrueMotion,
            above: [30, 40, 50, 60],
            left: [20, 30, 40, 50],
            topLeft: 10);

        Assert.Equal([40, 50, 60, 70], plane.Skip((3 * 8) + 2).Take(4).ToArray());
        Assert.Equal([50, 60, 70, 80], plane.Skip((4 * 8) + 2).Take(4).ToArray());
        Assert.Equal([60, 70, 80, 90], plane.Skip((5 * 8) + 2).Take(4).ToArray());
        Assert.Equal([70, 80, 90, 100], plane.Skip((6 * 8) + 2).Take(4).ToArray());
    }

    [Fact]
    public void PredictMacroblock_WithBPred_ThrowsExplicitGate()
    {
        var plane = new byte[16 * 16];

        var exception = Assert.Throws<NotSupportedException>(() =>
            Vp8IntraPredictor.PredictMacroblock(
                plane,
                stride: 16,
                x: 0,
                y: 0,
                size: 16,
                Vp8MacroblockPredictionMode.BPred,
                above: [],
                left: [],
                topLeft: 0,
                hasAbove: false,
                hasLeft: false));

        Assert.Contains("VP8 B_PRED macroblock prediction", exception.Message);
    }

    [Theory]
    [InlineData((int)Vp8BlockPredictionMode.Vertical, new byte[] { 11, 20, 30, 40, 11, 20, 30, 40, 11, 20, 30, 40, 11, 20, 30, 40 })]
    [InlineData((int)Vp8BlockPredictionMode.Horizontal, new byte[] { 71, 71, 71, 71, 100, 100, 100, 100, 110, 110, 110, 110, 118, 118, 118, 118 })]
    [InlineData((int)Vp8BlockPredictionMode.LeftDown, new byte[] { 20, 30, 40, 50, 30, 40, 50, 60, 40, 50, 60, 70, 50, 60, 70, 78 })]
    [InlineData((int)Vp8BlockPredictionMode.RightDown, new byte[] { 11, 20, 30, 38, 28, 11, 20, 30, 71, 28, 11, 20, 100, 71, 28, 11 })]
    [InlineData((int)Vp8BlockPredictionMode.VerticalRight, new byte[] { 8, 15, 25, 35, 11, 20, 30, 38, 28, 8, 15, 25, 71, 11, 20, 30 })]
    [InlineData((int)Vp8BlockPredictionMode.VerticalLeft, new byte[] { 15, 25, 35, 45, 20, 30, 40, 50, 25, 35, 45, 60, 30, 40, 60, 70 })]
    [InlineData((int)Vp8BlockPredictionMode.HorizontalDown, new byte[] { 48, 28, 11, 20, 95, 71, 48, 28, 105, 100, 95, 71, 115, 110, 105, 100 })]
    [InlineData((int)Vp8BlockPredictionMode.HorizontalUp, new byte[] { 95, 100, 105, 110, 105, 110, 115, 118, 115, 118, 120, 120, 120, 120, 120, 120 })]
    public void PredictBlock_ForVp8FourByFourModes_UsesSmoothedDirectionalPredictors(
        int mode,
        byte[] expected)
    {
        var plane = new byte[4 * 4];

        Vp8IntraPredictor.PredictBlock(
            plane,
            stride: 4,
            x: 0,
            y: 0,
            (Vp8BlockPredictionMode)mode,
            above: [10, 20, 30, 40, 50, 60, 70, 80],
            left: [90, 100, 110, 120],
            topLeft: 5);

        Assert.Equal(expected, plane);
    }

    [Fact]
    public void PredictMacroblock_VerticalWithoutAbove_ThrowsExplicitGate()
    {
        var plane = new byte[4 * 4];

        var exception = Assert.Throws<NotSupportedException>(() =>
            Vp8IntraPredictor.PredictMacroblock(
                plane,
                stride: 4,
                x: 0,
                y: 0,
                size: 4,
                Vp8MacroblockPredictionMode.Vertical,
                above: [],
                left: [],
                topLeft: 0,
                hasAbove: false,
                hasLeft: false));

        Assert.Contains("VP8 vertical intra prediction requires an above edge sample row.", exception.Message);
    }
}
