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

    [Theory]
    [InlineData(
        "/tmp/vp9-main-frame-0.vp9",
        30398,
        "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9",
        3)]
    [InlineData(
        "/tmp/vp9-alpha-frame-0.vp9",
        6233,
        "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329",
        10)]
    public void TryBuildKeyFrameMasks_ForExternalSamples_BuildsAdjustedSuperblockMasks(
        string path,
        int expectedLength,
        string expectedSha256,
        byte expectedFilterLevel)
    {
        var packet = ReadRequiredSample(path, expectedLength, expectedSha256);
        var header = Vp9FrameHeaderParser.Parse(packet);
        var state = CreateState(packet, header);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFullFrameWithSyntax(packet, state, out var reconstructed, out var reconstructionDiagnostic), reconstructionDiagnostic?.Message);
        Assert.NotNull(reconstructed);
        Assert.True(Vp9LoopFilterMaskBuilder.TryBuildKeyFrameMasks(header, reconstructed, out var masks, out var diagnostic), diagnostic?.Message);

        Assert.Null(diagnostic);
        Assert.Equal(924, masks.Count);
        Assert.All(masks, mask => Assert.Equal(expectedFilterLevel, mask.FilterLevel));
        Assert.Equal(header.TileInfo.MiRows * header.TileInfo.MiColumns, masks.Sum(mask => mask.ActiveLevelCount));
        Assert.Contains(masks, mask => mask.HasAnyFilter);

        var first = masks[0];
        Assert.Equal(0, first.MiRow);
        Assert.Equal(0, first.MiColumn);
        for (var tx = 0; tx < (int)Vp9TransformSize.Tx32X32; tx++)
        {
            Assert.Equal(0UL, first.LeftY[tx] & 0x0101010101010101UL);
            Assert.Equal(0, first.LeftUv[tx] & 0x1111);
        }

        var last = masks[^1];
        Assert.Equal(168, last.MiRow);
        Assert.Equal(328, last.MiColumn);
        Assert.Equal(4, last.ActiveLevelCount);
        Assert.True(last.HasAnyFilter);
    }

    [Theory]
    [InlineData(
        "/tmp/vp9-main-frame-0.vp9",
        30398,
        "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9",
        3_130_773,
        "4f276400ec1d63299b4ec18d83da40482b52d9d09b1e3fd4a100537ed63798ff",
        1_335_318,
        "19c729c84c0cd8d44d4e4eeb8301b3f5d8d5a88b9d3c23309d642b406d8a4c8c",
        897_727,
        "a25285cf859d1f515202c4b60bacd7b652182f12492239793c3d51a31be7fb72",
        897_728,
        "ccbcde0669f2b04c523ed61e011826b89a30bef4673794ed8d685d7fc7db140d")]
    [InlineData(
        "/tmp/vp9-alpha-frame-0.vp9",
        6233,
        "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329",
        2_757_454,
        "eed6a4429dd161e0f6248a91e3cdb9ac0810df6b417e82017d6e86acf7413fc9",
        961_998,
        "f1931ea77d8936a5ad44572159028191522a0133aefdda14479a44e59304eb64",
        897_728,
        "00e7569e2bc6b0d8d8bc2464a82456bf8a6e12ab19b041330d8ec119adfd3476",
        897_728,
        "00e7569e2bc6b0d8d8bc2464a82456bf8a6e12ab19b041330d8ec119adfd3476")]
    public void TryApply_ForExternalSamples_WritesDeterministicFilteredYuv(
        string path,
        int expectedLength,
        string expectedSha256,
        int expectedNonZero,
        string expectedHash,
        int expectedYNonZero,
        string expectedYHash,
        int expectedUNonZero,
        string expectedUHash,
        int expectedVNonZero,
        string expectedVHash)
    {
        var packet = ReadRequiredSample(path, expectedLength, expectedSha256);
        var header = Vp9FrameHeaderParser.Parse(packet);
        var state = CreateState(packet, header);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFullFrameWithSyntax(packet, state, out var reconstructed, out var reconstructionDiagnostic), reconstructionDiagnostic?.Message);
        Assert.NotNull(reconstructed);
        Assert.True(Vp9LoopFilter.TryApply(header, reconstructed, out var filterDiagnostic), filterDiagnostic?.Message);

        var frame = reconstructed.Frame;
        Assert.Equal(2656, frame.Width);
        Assert.Equal(1352, frame.Height);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(expectedNonZero, frame.Pixels.Count(value => value != 0));
        Assert.Equal(expectedHash, Hash(frame.Pixels));
        AssertPlaneHash(frame, planeIndex: 0, expectedYNonZero, expectedYHash);
        AssertPlaneHash(frame, planeIndex: 1, expectedUNonZero, expectedUHash);
        AssertPlaneHash(frame, planeIndex: 2, expectedVNonZero, expectedVHash);
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

    private static Vp9KeyFrameDecodeState CreateState(byte[] packet, Vp9FrameHeader header)
    {
        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, header, out var compressedHeader, out var compressedDiagnostic), compressedDiagnostic?.Message);
        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var layoutDiagnostic), layoutDiagnostic?.Message);
        Assert.True(Vp9KeyFrameDecodeState.TryCreate(header, compressedHeader!, tileBuffers, out var state, out var stateDiagnostic), stateDiagnostic?.Message);
        return state!;
    }

    private static void AssertPlaneHash(
        Vp9DecodedFrame frame,
        int planeIndex,
        int expectedNonZero,
        string expectedHash)
    {
        var plane = frame.Planes[planeIndex];
        var bytes = frame.Pixels.AsSpan(plane.Offset, plane.Length).ToArray();
        Assert.Equal(expectedNonZero, bytes.Count(value => value != 0));
        Assert.Equal(expectedHash, Hash(bytes));
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
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
