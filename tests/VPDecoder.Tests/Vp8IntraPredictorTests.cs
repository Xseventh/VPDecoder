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

    [Fact]
    public void PredictBlock_WithDirectionalMode_ThrowsExplicitGate()
    {
        var plane = new byte[4 * 4];

        var exception = Assert.Throws<NotSupportedException>(() =>
            Vp8IntraPredictor.PredictBlock(
                plane,
                stride: 4,
                x: 0,
                y: 0,
                Vp8BlockPredictionMode.LeftDown,
                above: [1, 2, 3, 4],
                left: [5, 6, 7, 8],
                topLeft: 9));

        Assert.Contains("VP8 4x4 intra prediction mode LeftDown is not implemented yet.", exception.Message);
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
