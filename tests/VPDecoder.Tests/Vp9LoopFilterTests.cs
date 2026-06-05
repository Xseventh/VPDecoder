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

    [Theory]
    [InlineData((int)Vp9InterReferenceFrame.Last, (int)Vp9InterPredictionMode.ZeroMv, 17)]
    [InlineData((int)Vp9InterReferenceFrame.Last, (int)Vp9InterPredictionMode.NearestMv, 28)]
    [InlineData((int)Vp9InterReferenceFrame.Last, (int)Vp9InterPredictionMode.NearMv, 28)]
    [InlineData((int)Vp9InterReferenceFrame.Golden, (int)Vp9InterPredictionMode.ZeroMv, 12)]
    [InlineData((int)Vp9InterReferenceFrame.AltRef, (int)Vp9InterPredictionMode.NewMv, 30)]
    public void GetInterFrameFilterLevel_WhenModeRefDeltaIsEnabled_AppliesReferenceAndModeDeltas(
        int referenceFrame,
        int predictionMode,
        int expectedLevel)
    {
        var header = new Vp9LoopFilterHeader(
            FilterLevel: 20,
            SharpnessLevel: 0,
            ModeRefDeltaEnabled: true,
            ModeRefDeltaUpdate: false,
            RefDeltas: [1, 2, -3, 4],
            ModeDeltas: [-5, 6]);

        Assert.Equal(
            expectedLevel,
            Vp9LoopFilter.GetInterFrameFilterLevel(
                header,
                (Vp9InterReferenceFrame)referenceFrame,
                (Vp9InterPredictionMode)predictionMode));
    }

    [Fact]
    public void GetInterFrameFilterLevel_WhenBaseLevelIsHigh_ScalesDeltasAndClamps()
    {
        var header = new Vp9LoopFilterHeader(
            FilterLevel: 40,
            SharpnessLevel: 0,
            ModeRefDeltaEnabled: true,
            ModeRefDeltaUpdate: false,
            RefDeltas: [0, 20, -20, 0],
            ModeDeltas: [20, -20]);

        Assert.Equal(63, Vp9LoopFilter.GetInterFrameFilterLevel(header, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.ZeroMv));
        Assert.Equal(0, Vp9LoopFilter.GetInterFrameFilterLevel(header, Vp9InterReferenceFrame.Golden, Vp9InterPredictionMode.NewMv));
    }

    [Fact]
    public void GetInterFrameFilterLevel_WhenModeRefDeltaIsDisabled_UsesBaseLevel()
    {
        var header = new Vp9LoopFilterHeader(
            FilterLevel: 20,
            SharpnessLevel: 0,
            ModeRefDeltaEnabled: false,
            ModeRefDeltaUpdate: false,
            RefDeltas: [],
            ModeDeltas: []);

        Assert.Equal(20, Vp9LoopFilter.GetInterFrameFilterLevel(header, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.ZeroMv));
    }

    [Fact]
    public void GetInterFrameFilterLevel_WhenModeInfoIsIntraBlock_UsesIntraReferenceDelta()
    {
        var header = new Vp9LoopFilterHeader(
            FilterLevel: 20,
            SharpnessLevel: 0,
            ModeRefDeltaEnabled: true,
            ModeRefDeltaUpdate: false,
            RefDeltas: [1, 2, -3, 4],
            ModeDeltas: [-5, 6]);
        var modeInfo = new Vp9InterModeInfoProbe(
            Vp9BlockSize.Block8X8,
            Skip: true,
            SkipContext: 0,
            IsInterBlock: false,
            IntraInterContext: 0,
            Vp9TransformSize.Tx4X4,
            TransformSizeContext: 0,
            Vp9ReferenceMode.Single,
            Vp9InterReferenceFrame.Last,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            Vp9InterPredictionMode.ZeroMv,
            InterModeContext: 0,
            Vp9InterpolationFilter.Switchable);

        Assert.Equal(21, Vp9LoopFilter.GetInterFrameFilterLevel(header, modeInfo));
    }

    [Fact]
    public void GetInterFrameFilterLevel_WhenDeltaTablesAreIncomplete_ThrowsArgumentDiagnostic()
    {
        var missingRefDeltas = new Vp9LoopFilterHeader(
            FilterLevel: 20,
            SharpnessLevel: 0,
            ModeRefDeltaEnabled: true,
            ModeRefDeltaUpdate: false,
            RefDeltas: [0],
            ModeDeltas: [0, 0]);
        var missingModeDeltas = missingRefDeltas with
        {
            RefDeltas = [0, 0, 0, 0],
            ModeDeltas = [0]
        };

        Assert.Throws<ArgumentException>(
            () => Vp9LoopFilter.GetInterFrameFilterLevel(missingRefDeltas, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.ZeroMv));
        Assert.Throws<ArgumentException>(
            () => Vp9LoopFilter.GetInterFrameFilterLevel(missingModeDeltas, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.NewMv));
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

    [Fact]
    public void TryBuildInterFrameMasks_ForUniformZeroMvBlocks_BuildsAdjustedMask()
    {
        var header = CreateSyntheticInterHeader(width: 16, height: 8) with
        {
            LoopFilter = new Vp9LoopFilterHeader(
                FilterLevel: 20,
                SharpnessLevel: 0,
                ModeRefDeltaEnabled: true,
                ModeRefDeltaUpdate: false,
                RefDeltas: [1, 2, -3, 4],
                ModeDeltas: [-5, 6])
        };
        var reconstructed = CreateSyntheticInterReconstructedFrame(
            width: 16,
            height: 8,
            CreateInterModeBlock(0, 0, Vp9BlockSize.Block64X64, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.ZeroMv));

        Assert.True(Vp9LoopFilterMaskBuilder.TryBuildInterFrameMasks(header, reconstructed, out var masks, out var diagnostic), diagnostic?.Message);

        Assert.Null(diagnostic);
        var mask = Assert.Single(masks);
        Assert.Equal(20, mask.FilterLevel);
        Assert.Equal(17, mask.LevelsY[0]);
        Assert.Equal(17, mask.GetLumaThresholds(0).Limit);
        Assert.Equal(2, mask.ActiveLevelCount);
        Assert.True(mask.HasAnyFilter);
    }

    [Fact]
    public void TryBuildInterFrameMasks_WhenPerBlockLevelsDiffer_StoresPerBlockLevels()
    {
        var header = CreateSyntheticInterHeader(width: 16, height: 8) with
        {
            LoopFilter = new Vp9LoopFilterHeader(
                FilterLevel: 20,
                SharpnessLevel: 0,
                ModeRefDeltaEnabled: true,
                ModeRefDeltaUpdate: false,
                RefDeltas: [1, 2, -3, 4],
                ModeDeltas: [-5, 6])
        };
        var reconstructed = CreateSyntheticInterReconstructedFrame(
            width: 16,
            height: 8,
            CreateInterModeBlock(0, 0, Vp9BlockSize.Block8X8, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.ZeroMv),
            CreateInterModeBlock(0, 1, Vp9BlockSize.Block8X8, Vp9InterReferenceFrame.Last, Vp9InterPredictionMode.NewMv));

        Assert.True(Vp9LoopFilterMaskBuilder.TryBuildInterFrameMasks(header, reconstructed, out var masks, out var diagnostic), diagnostic?.Message);

        var mask = Assert.Single(masks);
        Assert.Null(diagnostic);
        Assert.Equal(17, mask.LevelsY[0]);
        Assert.Equal(28, mask.LevelsY[1]);
        Assert.Equal(2, mask.ActiveLevelCount);
        Assert.Equal(17, mask.GetLumaThresholds(0).Limit);
        Assert.Equal(28, mask.GetLumaThresholds(1).Limit);
    }

    [Theory]
    [InlineData(
        "/tmp/vp9-main-frame-0.vp9",
        30398,
        "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9",
        2_645_013,
        "30cc7bab018118e09d0655fcb3e10423031a7c81f00f0123022084773c8b5d92",
        849_557,
        "9670050122e730c9b88347b6d16ee2ca6011c58118187ae35c996860c89d21e4",
        897_728,
        "ba963c0d8a457a4872399017f18c889b793e01eefd030f0733bfe1323470dc70",
        897_728,
        "c3616a18439cfde2a1a3dbaec8acb05384a07711e9bc4dffcb7a9886af46c1d2")]
    [InlineData(
        "/tmp/vp9-alpha-frame-0.vp9",
        6233,
        "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329",
        2_633_334,
        "ea15041995aa626f6a7fad52bf46271ec1a6231e9e4fdf110c31a01f1a0459f6",
        837_878,
        "ffb3fc3cc4c8619d7833230bdc8c9f59f6a9008fbaa93ff6465750cca92c8e8a",
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

    private static Vp9FrameHeader CreateSyntheticInterHeader(int width, int height)
    {
        var packet = Vp9TestPackets.CreateOrdinaryInterFramePacket(
            sizeFromReference: false,
            stopAfterSizeReference: false,
            tileInfoWidth: width,
            firstPartitionSize: 64);
        return Vp9FrameHeaderParser.Parse(packet) with
        {
            Width = width,
            Height = height,
            RenderWidth = width,
            RenderHeight = height,
            TileInfo = new Vp9TileInfo(
                MiColumns: (width + 7) / 8,
                MiRows: (height + 7) / 8,
                SuperblockColumns: (((width + 7) / 8) + 7) / 8,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 0,
                Log2TileRows: 0)
        };
    }

    private static Vp9ReconstructedFrame CreateSyntheticInterReconstructedFrame(
        int width,
        int height,
        params Vp9InterBlockModeInfoProbe[] modeBlocks)
    {
        var frame = Vp9YuvFrameBuffer.Create(width, height).ToDecodedFrame();
        var superblock = new Vp9InterSuperblockSyntaxProbe(
            TileIndex: 0,
            Partitions: [],
            ModeInfos: modeBlocks,
            CoefficientGroups: modeBlocks.SelectMany(modeBlock => CreateEmptyCoefficientGroups(modeBlock.ModeInfo.BlockSize)).ToArray());
        return Vp9ReconstructedFrame.FromInter(
            frame,
            [superblock],
            miRows: (height + 7) / 8,
            miColumns: (width + 7) / 8);
    }

    private static Vp9InterBlockModeInfoProbe CreateInterModeBlock(
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        Vp9InterReferenceFrame referenceFrame,
        Vp9InterPredictionMode predictionMode)
    {
        var modeInfo = new Vp9InterModeInfoProbe(
            blockSize,
            Skip: true,
            SkipContext: 0,
            IsInterBlock: true,
            IntraInterContext: 0,
            Vp9TransformSize.Tx4X4,
            TransformSizeContext: 0,
            Vp9ReferenceMode.Single,
            referenceFrame,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            predictionMode,
            InterModeContext: 0,
            Vp9InterpolationFilter.EightTap);

        return new Vp9InterBlockModeInfoProbe(
            TileIndex: 0,
            miRow,
            miColumn,
            PartitionPath: [Vp9PartitionType.None],
            modeInfo);
    }

    private static IEnumerable<Vp9CoefficientBlockGroupProbe> CreateEmptyCoefficientGroups(Vp9BlockSize blockSize)
    {
        yield return new Vp9CoefficientBlockGroupProbe(0, blockSize, Vp9TransformSize.Tx4X4, Blocks: []);
        yield return new Vp9CoefficientBlockGroupProbe(0, blockSize, Vp9TransformSize.Tx4X4, Blocks: []);
        yield return new Vp9CoefficientBlockGroupProbe(0, blockSize, Vp9TransformSize.Tx4X4, Blocks: []);
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
