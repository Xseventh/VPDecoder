namespace VPDecoder.Tests;

public sealed class Vp9LoopFilterTests
{
    [Theory]
    [InlineData(
        "/tmp/vp9-main-frame-0.vp9",
        30398,
        "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9",
        3,
        3,
        13,
        0)]
    [InlineData(
        "/tmp/vp9-alpha-frame-0.vp9",
        6233,
        "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329",
        10,
        10,
        34,
        0)]
    public void GetThresholds_ForExternalSamples_IncludesKeyFrameIntraDelta(
        string path,
        int expectedLength,
        string expectedSha256,
        int expectedLevel,
        byte expectedLimit,
        byte expectedMacroblockLimit,
        byte expectedHev)
    {
        var packet = ReadRequiredSample(path, expectedLength, expectedSha256);
        var header = Vp9FrameHeaderParser.Parse(packet);

        var level = Vp9LoopFilter.GetKeyFrameFilterLevel(header.LoopFilter);
        var thresholds = Vp9LoopFilter.GetThresholds(level, header.LoopFilter.SharpnessLevel);

        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedLimit, thresholds.Limit);
        Assert.Equal(expectedMacroblockLimit, thresholds.MacroblockLimit);
        Assert.Equal(expectedHev, thresholds.HighEdgeVarianceThreshold);
    }

    [Fact]
    public void GetKeyFrameFilterLevel_WhenBaseLevelIsZero_KeepsLoopFilterDisabled()
    {
        var header = new Vp9LoopFilterHeader(
            FilterLevel: 0,
            SharpnessLevel: 0,
            ModeRefDeltaEnabled: true,
            ModeRefDeltaUpdate: false,
            RefDeltas: [1, 0, -1, -1],
            ModeDeltas: [0, 0]);

        Assert.Equal(0, Vp9LoopFilter.GetKeyFrameFilterLevel(header));
    }

    [Fact]
    public void ApplyVertical4_ForSmoothEdge_AppliesFourTapFilter()
    {
        var thresholds = new Vp9LoopFilterThresholds(Limit: 3, MacroblockLimit: 13, HighEdgeVarianceThreshold: 0);
        var plane = CreateVerticalEdgePlane(width: 16, height: 8, edgeX: 8, left: 100, right: 104);

        Vp9LoopFilter.ApplyVertical4(plane, stride: 16, startIndex: 8, thresholds);

        AssertRows(plane, stride: 16, rows: 8, columns: 4, expected: [100, 100, 101, 101, 102, 103, 104, 104]);
    }

    [Fact]
    public void ApplyHorizontal4_ForSmoothEdge_AppliesFourTapFilter()
    {
        var thresholds = new Vp9LoopFilterThresholds(Limit: 3, MacroblockLimit: 13, HighEdgeVarianceThreshold: 0);
        var plane = CreateHorizontalEdgePlane(width: 8, height: 16, edgeY: 8, top: 100, bottom: 104);

        Vp9LoopFilter.ApplyHorizontal4(plane, stride: 8, startIndex: 8 * 8, thresholds);

        AssertColumns(plane, stride: 8, columns: 8, row: 4, expected: [100, 100, 101, 101, 102, 103, 104, 104]);
    }

    [Fact]
    public void ApplyVertical8_ForFlatSmoothEdge_AppliesSevenTapFilter()
    {
        var thresholds = new Vp9LoopFilterThresholds(Limit: 3, MacroblockLimit: 13, HighEdgeVarianceThreshold: 0);
        var plane = CreateVerticalEdgePlane(width: 16, height: 8, edgeX: 8, left: 100, right: 104);

        Vp9LoopFilter.ApplyVertical8(plane, stride: 16, startIndex: 8, thresholds);

        AssertRows(plane, stride: 16, rows: 8, columns: 4, expected: [100, 101, 101, 102, 103, 103, 104, 104]);
    }

    [Fact]
    public void ApplyHorizontal8_ForFlatSmoothEdge_AppliesSevenTapFilter()
    {
        var thresholds = new Vp9LoopFilterThresholds(Limit: 3, MacroblockLimit: 13, HighEdgeVarianceThreshold: 0);
        var plane = CreateHorizontalEdgePlane(width: 8, height: 16, edgeY: 8, top: 100, bottom: 104);

        Vp9LoopFilter.ApplyHorizontal8(plane, stride: 8, startIndex: 8 * 8, thresholds);

        AssertColumns(plane, stride: 8, columns: 8, row: 4, expected: [100, 101, 101, 102, 103, 103, 104, 104]);
    }

    [Fact]
    public void ApplyVertical16_ForFlatSmoothEdge_AppliesFifteenTapFilter()
    {
        var thresholds = new Vp9LoopFilterThresholds(Limit: 3, MacroblockLimit: 13, HighEdgeVarianceThreshold: 0);
        var plane = CreateVerticalEdgePlane(width: 16, height: 8, edgeX: 8, left: 100, right: 104);

        Vp9LoopFilter.ApplyVertical16(plane, stride: 16, startIndex: 8, thresholds);

        AssertRows(plane, stride: 16, rows: 8, columns: 0, expected: [100, 100, 101, 101, 101, 101, 102, 102, 102, 103, 103, 103, 103, 104, 104, 104]);
    }

    [Fact]
    public void ApplyHorizontal16_ForFlatSmoothEdge_AppliesFifteenTapFilter()
    {
        var thresholds = new Vp9LoopFilterThresholds(Limit: 3, MacroblockLimit: 13, HighEdgeVarianceThreshold: 0);
        var plane = CreateHorizontalEdgePlane(width: 8, height: 16, edgeY: 8, top: 100, bottom: 104);

        Vp9LoopFilter.ApplyHorizontal16(plane, stride: 8, startIndex: 8 * 8, thresholds);

        AssertColumns(plane, stride: 8, columns: 8, row: 0, expected: [100, 100, 101, 101, 101, 101, 102, 102, 102, 103, 103, 103, 103, 104, 104, 104]);
    }

    private static byte[] CreateVerticalEdgePlane(int width, int height, int edgeX, byte left, byte right)
    {
        var plane = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                plane[(y * width) + x] = x < edgeX ? left : right;
            }
        }

        return plane;
    }

    private static byte[] CreateHorizontalEdgePlane(int width, int height, int edgeY, byte top, byte bottom)
    {
        var plane = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                plane[(y * width) + x] = y < edgeY ? top : bottom;
            }
        }

        return plane;
    }

    private static void AssertRows(byte[] plane, int stride, int rows, int columns, byte[] expected)
    {
        for (var row = 0; row < rows; row++)
        {
            Assert.Equal(expected, plane.AsSpan((row * stride) + columns, expected.Length).ToArray());
        }
    }

    private static void AssertColumns(byte[] plane, int stride, int columns, int row, byte[] expected)
    {
        for (var column = 0; column < columns; column++)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], plane[((row + i) * stride) + column]);
            }
        }
    }

    private static byte[] ReadRequiredSample(string path, int expectedLength, string expectedSha256)
    {
        Assert.True(File.Exists(path), $"Required VP9 acceptance sample is missing: {path}");
        var packet = File.ReadAllBytes(path);
        Assert.Equal(expectedLength, packet.Length);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet)).ToLowerInvariant();
        Assert.Equal(expectedSha256, hash);
        return packet;
    }
}
