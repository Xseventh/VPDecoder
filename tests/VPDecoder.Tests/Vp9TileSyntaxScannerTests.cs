namespace VPDecoder.Tests;

public sealed class Vp9TileSyntaxScannerTests
{
    [Fact]
    public void TryProbeFirstSuperblockPartitions_ForExternalMainFrame_ReadsExpectedFirstPartitions()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstSuperblockPartitions(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.Equal(Enumerable.Range(0, 8), probes.Select(probe => probe.TileIndex));
        Assert.All(probes, probe =>
        {
            Assert.Equal(0, probe.MiRow);
            Assert.Equal(12, probe.PartitionContext);
            Assert.Equal(Vp9PartitionType.None, probe.PartitionType);
        });
        Assert.Equal([0, 40, 80, 120, 168, 208, 248, 288], probes.Select(probe => probe.MiColumn).ToArray());
    }

    [Fact]
    public void TryProbeFirstSuperblockPartitions_ForExternalAlphaFrame_ReadsExpectedFirstPartitions()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstSuperblockPartitions(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Equal(0, probe.MiRow);
            Assert.Equal(12, probe.PartitionContext);
            Assert.Equal(Vp9PartitionType.Split, probe.PartitionType);
        });
        Assert.Equal([0, 40, 80, 120, 168, 208, 248, 288], probes.Select(probe => probe.MiColumn).ToArray());
    }

    [Fact]
    public void TryProbeFirstLeafModeInfo_ForExternalMainFrame_ReadsExpectedFirstLeafModes()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafModeInfo(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Equal([Vp9PartitionType.None], probe.PartitionPath);
            Assert.Equal(Vp9BlockSize.Block64X64, probe.BlockSize);
            Assert.False(probe.Skip);
            Assert.Equal(0, probe.SkipContext);
            Assert.Equal(Vp9TransformSize.Tx32X32, probe.TransformSize);
            Assert.Equal(1, probe.TransformSizeContext);
            Assert.Equal(Vp9PredictionMode.Dc, probe.YMode);
            Assert.Equal(Vp9PredictionMode.Dc, probe.UvMode);
        });
    }

    [Fact]
    public void TryProbeFirstLeafModeInfo_ForExternalAlphaFrame_ReadsExpectedFirstLeafModes()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafModeInfo(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Equal([Vp9PartitionType.Split, Vp9PartitionType.None], probe.PartitionPath);
            Assert.Equal(Vp9BlockSize.Block32X32, probe.BlockSize);
            Assert.False(probe.Skip);
            Assert.Equal(0, probe.SkipContext);
            Assert.Equal(Vp9TransformSize.Tx32X32, probe.TransformSize);
            Assert.Equal(1, probe.TransformSizeContext);
            Assert.Equal(Vp9PredictionMode.Dc, probe.YMode);
            Assert.Equal(Vp9PredictionMode.Dc, probe.UvMode);
        });
    }

    [Fact]
    public void ReadPartition_WithInterFrameContext_UsesFrameContextProbabilities()
    {
        var interReader = new Vp9BoolReader([0x57, 0x00]);
        var keyReader = new Vp9BoolReader([0x57, 0x00]);
        var frameContext = Vp9FrameContext.CreateDefault();

        var interPartition = Vp9PartitionSyntax.ReadPartition(
            ref interReader,
            frameContext,
            partitionContext: 12,
            hasRows: true,
            hasColumns: true);
        var keyPartition = Vp9PartitionSyntax.ReadPartition(
            ref keyReader,
            partitionContext: 12,
            hasRows: true,
            hasColumns: true);

        Assert.Equal(Vp9PartitionType.None, interPartition);
        Assert.Equal(Vp9PartitionType.Horizontal, keyPartition);
        Assert.False(interReader.HasError);
        Assert.False(keyReader.HasError);
    }

    [Fact]
    public void TryProbeFirstInterSuperblockModeInfo_ForSyntheticOrdinaryInterFrame_ReadsFirstInterBlock()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstInterSuperblockModeInfo(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var diagnostic),
            diagnostic?.Message);

        Assert.Single(probes);
        var probe = probes[0];
        Assert.Equal(0, probe.TileIndex);
        Assert.Single(probe.Partitions);
        Assert.Single(probe.ModeInfos);
        Assert.Equal(Vp9PartitionType.None, probe.Partitions[0].PartitionType);
        Assert.Equal(12, probe.Partitions[0].PartitionContext);

        var modeBlock = probe.ModeInfos[0];
        Assert.Equal(0, modeBlock.TileIndex);
        Assert.Equal(0, modeBlock.MiRow);
        Assert.Equal(0, modeBlock.MiColumn);
        Assert.Equal([Vp9PartitionType.None], modeBlock.PartitionPath);
        Assert.Equal(Vp9BlockSize.Block64X64, modeBlock.ModeInfo.BlockSize);
        Assert.False(modeBlock.ModeInfo.Skip);
        Assert.True(modeBlock.ModeInfo.IsInterBlock);
        Assert.Equal(Vp9TransformSize.Tx4X4, modeBlock.ModeInfo.TransformSize);
        Assert.Equal(Vp9InterReferenceFrame.Last, modeBlock.ModeInfo.ReferenceFrame);
        Assert.Equal(Vp9InterPredictionMode.ZeroMv, modeBlock.ModeInfo.PredictionMode);
        Assert.Equal(Vp9InterpolationFilter.EightTapSharp, modeBlock.ModeInfo.InterpolationFilter);
    }

    [Fact]
    public void TryProbeFirstInterSuperblockModeInfo_WhenSwitchableInterpolation_ReturnsUnsupportedDiagnostic()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length) with
        {
            InterpolationFilter = Vp9InterpolationFilter.Switchable
        };
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.False(
            Vp9TileSyntaxScanner.TryProbeFirstInterSuperblockModeInfo(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var diagnostic));

        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("switchable", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(probes);
    }

    [Fact]
    public void TryPredictFirstInterSuperblockZeroMv_ForSyntheticOrdinaryInterFrame_CopiesReferenceBlock()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.True(
            Vp9TileSyntaxScanner.TryPredictFirstInterSuperblockZeroMv(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var diagnostic),
            diagnostic?.Message);
        Assert.True(
            Vp9TileSyntaxScanner.TryPredictFirstInterSuperblockZeroMv(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var secondFrame,
                out _,
                out var secondDiagnostic),
            secondDiagnostic?.Message);

        Assert.NotNull(frame);
        Assert.NotNull(secondFrame);
        Assert.Single(probes);
        Assert.Single(probes[0].ModeInfos);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(64, frame.Width);
        Assert.Equal(64, frame.Height);
        Assert.Equal(Hash(referenceFrame.Pixels), Hash(frame.Pixels));
        Assert.Equal(Hash(frame.Pixels), Hash(secondFrame.Pixels));
    }

    [Fact]
    public void TryPredictFirstInterSuperblockZeroMv_WhenReferenceIsMissing_ReturnsMissingReferenceFrame()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrames = new Vp9ReferenceFrameStore();

        Assert.False(
            Vp9TileSyntaxScanner.TryPredictFirstInterSuperblockZeroMv(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Single(probes);
        Assert.Single(probes[0].ModeInfos);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, diagnostic?.Code);
    }

    [Fact]
    public void TryPredictFirstInterSuperblockZeroMv_WhenChromaIsUnsupported_ReturnsSpecificDiagnostic()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length) with
        {
            SubsamplingX = 0,
            SubsamplingY = 1
        };
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.False(
            Vp9TileSyntaxScanner.TryPredictFirstInterSuperblockZeroMv(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                new Vp9ReferenceFrameStore(),
                out var frame,
                out var probes,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Empty(probes);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedChromaSubsampling, diagnostic?.Code);
    }

    [Fact]
    public void TryReconstructFirstSkippedInterSuperblockZeroMv_ForSyntheticOrdinaryInterFrame_CopiesReferenceAndCreatesZeroResiduals()
    {
        byte[] tilePayload = [0x54, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.True(
            Vp9TileSyntaxScanner.TryReconstructFirstSkippedInterSuperblockZeroMv(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var residualGroups,
                out var diagnostic),
            diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Single(probes);
        Assert.Single(probes[0].ModeInfos);
        Assert.True(probes[0].ModeInfos[0].ModeInfo.Skip);
        Assert.Equal(Vp9InterPredictionMode.ZeroMv, probes[0].ModeInfos[0].ModeInfo.PredictionMode);
        Assert.Equal(Hash(referenceFrame.Pixels), Hash(frame.Pixels));
        Assert.Equal([256, 64, 64], residualGroups.Select(group => group.Blocks.Count).ToArray());
        Assert.All(residualGroups, group =>
        {
            Assert.Equal(Vp9TransformSize.Tx4X4, group.TransformSize);
            Assert.All(group.Blocks, block =>
            {
                Assert.Equal(Vp9ResidualSyntax.InterBlockReferenceType, block.ReferenceType);
                Assert.Equal(Vp9TransformType.DctDct, block.TransformType);
                Assert.Equal(0, block.Eob);
                Assert.Equal(0, block.NonZeroCount);
                Assert.All(block.DequantizedCoefficients, coefficient => Assert.Equal(0, coefficient));
            });
        });
    }

    [Fact]
    public void TryReconstructFirstSkippedInterSuperblockZeroMv_WhenInterBlockIsNotSkipped_ReturnsUnsupportedDiagnostic()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.False(
            Vp9TileSyntaxScanner.TryReconstructFirstSkippedInterSuperblockZeroMv(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var residualGroups,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Single(probes);
        Assert.False(probes[0].ModeInfos[0].ModeInfo.Skip);
        Assert.Empty(residualGroups);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("non-skipped", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryProbeFirstInterSuperblockResidualSyntax_ForSyntheticNonSkippedInterFrame_ReadsInterResidualGroups()
    {
        var tilePayload = new byte[64];
        tilePayload[0] = 0x03;

        AssertSyntheticInterResidualGroups(tilePayload, expectedSkip: false);
    }

    [Fact]
    public void TryProbeFirstInterSuperblockResidualSyntax_ForSyntheticSkippedInterFrame_ReadsInterResidualGroups()
    {
        byte[] tilePayload = [0x54, 0x00, 0x00];

        AssertSyntheticInterResidualGroups(tilePayload, expectedSkip: true);
    }

    [Fact]
    public void TryProbeFirstInterSuperblockResidualSyntax_ForSplitSkippedInterFrame_ReadsAllInterResidualGroups()
    {
        byte[] tilePayload = [0x79, 0xdb, 0x98, 0xba, 0xe0, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstInterSuperblockResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var residualGroups,
                out var diagnostic),
            diagnostic?.Message);

        Assert.Single(probes);
        Assert.Equal(5, probes[0].Partitions.Count);
        Assert.Equal(
            [Vp9PartitionType.Split, Vp9PartitionType.None, Vp9PartitionType.None, Vp9PartitionType.None, Vp9PartitionType.None],
            probes[0].Partitions.Select(partition => partition.PartitionType).ToArray());
        Assert.Equal(4, probes[0].ModeInfos.Count);
        Assert.Equal(12, residualGroups.Count);
        Assert.Equal([0, 1, 1, 2], probes[0].ModeInfos.Select(mode => mode.ModeInfo.SkipContext).ToArray());
        Assert.Equal([2, 1, 1, 0], probes[0].ModeInfos.Select(mode => mode.ModeInfo.InterModeContext).ToArray());
        Assert.All(probes[0].ModeInfos, mode =>
        {
            Assert.True(mode.ModeInfo.Skip);
            Assert.Equal(Vp9BlockSize.Block32X32, mode.ModeInfo.BlockSize);
            Assert.Equal(Vp9InterReferenceFrame.Last, mode.ModeInfo.ReferenceFrame);
            Assert.Equal(Vp9InterPredictionMode.ZeroMv, mode.ModeInfo.PredictionMode);
        });
        Assert.All(residualGroups, group =>
        {
            Assert.Equal(Vp9TransformSize.Tx4X4, group.TransformSize);
            Assert.All(group.Blocks, block => Assert.Equal(0, block.Eob));
        });
    }

    [Fact]
    public void TryProbeFirstInterSuperblockResidualSyntax_WhenNonSkippedResidualIsTruncated_ReturnsTruncatedPacket()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.False(
            Vp9TileSyntaxScanner.TryProbeFirstInterSuperblockResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var residualGroups,
                out var diagnostic));

        Assert.Empty(probes);
        Assert.Empty(residualGroups);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    [Fact]
    public void TryReconstructFirstInterSuperblockZeroMvWithResidual_ForSyntheticNonSkippedInterFrame_CopiesReferenceAndAppliesZeroResiduals()
    {
        var tilePayload = new byte[64];
        tilePayload[0] = 0x03;
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.True(
            Vp9TileSyntaxScanner.TryReconstructFirstInterSuperblockZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var residualGroups,
                out var diagnostic),
            diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Single(probes);
        Assert.False(probes[0].ModeInfos[0].ModeInfo.Skip);
        Assert.Equal([256, 64, 64], residualGroups.Select(group => group.Blocks.Count).ToArray());
        Assert.Equal(Hash(referenceFrame.Pixels), Hash(frame.Pixels));
    }

    [Fact]
    public void TryReconstructFirstInterSuperblockZeroMvWithResidual_ForSplitSkippedInterFrame_CopiesReferenceAndAppliesZeroResiduals()
    {
        byte[] tilePayload = [0x79, 0xdb, 0x98, 0xba, 0xe0, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.True(
            Vp9TileSyntaxScanner.TryReconstructFirstInterSuperblockZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var residualGroups,
                out var diagnostic),
            diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Single(probes);
        Assert.Equal(4, probes[0].ModeInfos.Count);
        Assert.Equal(12, residualGroups.Count);
        Assert.Equal(Hash(referenceFrame.Pixels), Hash(frame.Pixels));
    }

    [Fact]
    public void TryReconstructFirstInterSuperblockZeroMvWithResidual_WhenReferenceIsMissing_ReturnsMissingReferenceFrame()
    {
        var tilePayload = new byte[64];
        tilePayload[0] = 0x03;
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.False(
            Vp9TileSyntaxScanner.TryReconstructFirstInterSuperblockZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                new Vp9ReferenceFrameStore(),
                out var frame,
                out var probes,
                out var residualGroups,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Single(probes);
        Assert.NotEmpty(residualGroups);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, diagnostic?.Code);
    }

    [Fact]
    public void TryReconstructFirstInterSuperblockZeroMvWithResidual_WhenResidualIsTruncated_ReturnsTruncatedPacket()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.False(
            Vp9TileSyntaxScanner.TryReconstructFirstInterSuperblockZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var residualGroups,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Empty(probes);
        Assert.Empty(residualGroups);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    [Fact]
    public void TryProbeFullInterFrameResidualSyntax_ForTwoSyntheticTiles_ReadsEachTileSuperblock()
    {
        byte[] tilePayload = [0x54, 0x00, 0x00];
        var packet = tilePayload.Concat(tilePayload).ToArray();
        var header = CreateSyntheticTwoTileOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length),
            new Vp9TileBuffer(Index: 1, SizeFieldOffset: null, DataOffset: tilePayload.Length, Size: tilePayload.Length)
        ];

        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFullInterFrameResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var diagnostic),
            diagnostic?.Message);

        Assert.Equal(2, probes.Count);
        Assert.Equal([0, 1], probes.Select(probe => probe.TileIndex).ToArray());
        Assert.Equal([0, 8], probes.Select(probe => probe.ModeInfos[0].MiColumn).ToArray());
        Assert.All(probes, probe =>
        {
            Assert.Single(probe.Partitions);
            Assert.Single(probe.ModeInfos);
            Assert.Equal(Vp9PartitionType.None, probe.Partitions[0].PartitionType);
            Assert.True(probe.ModeInfos[0].ModeInfo.Skip);
            Assert.Equal(Vp9InterPredictionMode.ZeroMv, probe.ModeInfos[0].ModeInfo.PredictionMode);
            Assert.Equal([256, 64, 64], probe.CoefficientGroups.Select(group => group.Blocks.Count).ToArray());
            Assert.All(probe.CoefficientGroups, group =>
            {
                Assert.Equal(Vp9TransformSize.Tx4X4, group.TransformSize);
                Assert.All(group.Blocks, block =>
                {
                    Assert.Equal(Vp9ResidualSyntax.InterBlockReferenceType, block.ReferenceType);
                    Assert.Equal(0, block.Eob);
                });
            });
        });
    }

    [Fact]
    public void TryProbeFullInterFrameResidualSyntax_WhenFirstTileResidualIsTruncated_ReturnsTruncatedPacket()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.False(
            Vp9TileSyntaxScanner.TryProbeFullInterFrameResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var diagnostic));

        Assert.Empty(probes);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
        Assert.Contains("full inter residual", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReconstructFullInterFrameZeroMvWithResidual_ForTwoSyntheticTiles_CopiesReferenceAndAppliesZeroResiduals()
    {
        byte[] tilePayload = [0x54, 0x00, 0x00];
        var packet = tilePayload.Concat(tilePayload).ToArray();
        var header = CreateSyntheticTwoTileOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length),
            new Vp9TileBuffer(Index: 1, SizeFieldOffset: null, DataOffset: tilePayload.Length, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 128, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.True(
            Vp9TileSyntaxScanner.TryReconstructFullInterFrameZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var diagnostic),
            diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Equal(2, probes.Count);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(128, frame.Width);
        Assert.Equal(64, frame.Height);
        Assert.Equal(Hash(referenceFrame.Pixels), Hash(frame.Pixels));
        Assert.All(
            probes.SelectMany(probe => probe.ModeInfos),
            modeInfo => Assert.Equal(new Vp9MotionVector(0, 0), modeInfo.MotionVector));
    }

    [Fact]
    public void TryReconstructFullInterFrameZeroMvWithResidual_WhenReferenceIsMissing_ReturnsMissingReferenceFrame()
    {
        byte[] tilePayload = [0x54, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.False(
            Vp9TileSyntaxScanner.TryReconstructFullInterFrameZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                new Vp9ReferenceFrameStore(),
                out var frame,
                out var probes,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Single(probes);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, diagnostic?.Code);
    }

    [Fact]
    public void TryReconstructFullInterFrameZeroMvWithResidual_WhenResidualIsTruncated_ReturnsTruncatedPacket()
    {
        byte[] tilePayload = [0x03, 0x00, 0x00];
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];
        var referenceFrame = CreatePatternYuvFrame(width: 64, height: 64);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);

        Assert.False(
            Vp9TileSyntaxScanner.TryReconstructFullInterFrameZeroMvWithResidual(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var frame,
                out var probes,
                out var diagnostic));

        Assert.Null(frame);
        Assert.Empty(probes);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, diagnostic?.Code);
    }

    [Fact]
    public void TryReconstructInterFrameFromProbes_ForNearestMvWithSpatialCandidate_CopiesReference()
    {
        var header = CreateSyntheticOrdinaryInterHeader(packetLength: 0) with
        {
            Width = 16,
            Height = 8,
            RenderWidth = 16,
            RenderHeight = 8,
            TileInfo = new Vp9TileInfo(
                MiColumns: 2,
                MiRows: 1,
                SuperblockColumns: 1,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 0,
                Log2TileRows: 0)
        };
        var referenceFrame = CreatePatternYuvFrame(width: 16, height: 8);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);
        var first = CreateInterModeBlock(0, 0, Vp9InterPredictionMode.ZeroMv);
        var second = CreateInterModeBlock(0, 1, Vp9InterPredictionMode.NearestMv);
        IReadOnlyList<Vp9InterSuperblockSyntaxProbe> probes =
        [
            new Vp9InterSuperblockSyntaxProbe(
                TileIndex: 0,
                Partitions: [],
                ModeInfos: [first, second],
                CoefficientGroups:
                [
                    .. CreateEmptyInterTx4Groups(first.ModeInfo.BlockSize),
                    .. CreateEmptyInterTx4Groups(second.ModeInfo.BlockSize)
                ])
        ];

        Assert.True(
            Vp9TileSyntaxScanner.TryReconstructInterFrameFromProbes(
                header,
                probes,
                referenceFrames,
                out var reconstructedFrame,
                out var predictedProbes,
                out var diagnostic),
            diagnostic?.Message);

        Assert.NotNull(reconstructedFrame);
        Assert.Equal(Hash(referenceFrame.Pixels), Hash(reconstructedFrame.Frame.Pixels));
        var predictedModes = Assert.Single(predictedProbes).ModeInfos;
        Assert.Equal(new Vp9MotionVector(0, 0), predictedModes[0].MotionVector);
        Assert.Equal(new Vp9MotionVector(0, 0), predictedModes[1].MotionVector);
    }

    [Fact]
    public void TryReconstructInterFrameFromProbes_WhenNearestCandidateIsInOtherTile_ReturnsUnsupportedDiagnostic()
    {
        var header = CreateSyntheticOrdinaryInterHeader(packetLength: 0) with
        {
            Width = 16,
            Height = 8,
            RenderWidth = 16,
            RenderHeight = 8,
            TileInfo = new Vp9TileInfo(
                MiColumns: 2,
                MiRows: 1,
                SuperblockColumns: 1,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 0,
                Log2TileRows: 0)
        };
        var referenceFrame = CreatePatternYuvFrame(width: 16, height: 8);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);
        var otherTileCandidate = CreateInterModeBlock(0, 0, Vp9InterPredictionMode.ZeroMv, tileIndex: 1);
        var nearest = CreateInterModeBlock(0, 1, Vp9InterPredictionMode.NearestMv, tileIndex: 0);
        IReadOnlyList<Vp9InterSuperblockSyntaxProbe> probes =
        [
            new Vp9InterSuperblockSyntaxProbe(
                TileIndex: 1,
                Partitions: [],
                ModeInfos: [otherTileCandidate],
                CoefficientGroups: [.. CreateEmptyInterTx4Groups(otherTileCandidate.ModeInfo.BlockSize)]),
            new Vp9InterSuperblockSyntaxProbe(
                TileIndex: 0,
                Partitions: [],
                ModeInfos: [nearest],
                CoefficientGroups: [.. CreateEmptyInterTx4Groups(nearest.ModeInfo.BlockSize)])
        ];

        Assert.False(Vp9TileSyntaxScanner.TryReconstructInterFrameFromProbes(
            header,
            probes,
            referenceFrames,
            out var reconstructedFrame,
            out var predictedProbes,
            out var diagnostic));

        Assert.Null(reconstructedFrame);
        Assert.Empty(predictedProbes);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("NEARESTMV", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReconstructInterFrameFromProbes_WhenNearMvHasOnlyOneCandidate_ReturnsUnsupportedDiagnostic()
    {
        var header = CreateSyntheticOrdinaryInterHeader(packetLength: 0) with
        {
            Width = 16,
            Height = 8,
            RenderWidth = 16,
            RenderHeight = 8,
            TileInfo = new Vp9TileInfo(
                MiColumns: 2,
                MiRows: 1,
                SuperblockColumns: 1,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 0,
                Log2TileRows: 0)
        };
        var referenceFrame = CreatePatternYuvFrame(width: 16, height: 8);
        var referenceFrames = new Vp9ReferenceFrameStore();
        referenceFrames.Refresh(referenceFrame, Vp9ColorRange.Studio, refreshFrameFlags: 0x01);
        var first = CreateInterModeBlock(0, 0, Vp9InterPredictionMode.ZeroMv);
        var second = CreateInterModeBlock(0, 1, Vp9InterPredictionMode.NearMv);
        IReadOnlyList<Vp9InterSuperblockSyntaxProbe> probes =
        [
            new Vp9InterSuperblockSyntaxProbe(
                TileIndex: 0,
                Partitions: [],
                ModeInfos: [first, second],
                CoefficientGroups:
                [
                    .. CreateEmptyInterTx4Groups(first.ModeInfo.BlockSize),
                    .. CreateEmptyInterTx4Groups(second.ModeInfo.BlockSize)
                ])
        ];

        Assert.False(Vp9TileSyntaxScanner.TryReconstructInterFrameFromProbes(
            header,
            probes,
            referenceFrames,
            out var reconstructedFrame,
            out var predictedProbes,
            out var diagnostic));

        Assert.Null(reconstructedFrame);
        Assert.Empty(predictedProbes);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("NEARMV", diagnostic?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryProbeFirstLeafCoefficientToken_ForExternalMainFrame_ReadsExpectedFirstToken()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafCoefficientToken(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Equal(Vp9TransformSize.Tx32X32, probe.TransformSize);
            Assert.Equal(0, probe.PlaneType);
            Assert.Equal(0, probe.ReferenceType);
            Assert.Equal(0, probe.CoefficientBand);
            Assert.Equal(0, probe.CoefficientContext);
            Assert.Equal(Vp9CoefficientToken.Category6, probe.Token);
            Assert.Equal(-16375, probe.DequantizedValue);
        });
    }

    [Fact]
    public void TryProbeFirstLeafCoefficientToken_ForExternalAlphaFrame_ReadsExpectedFirstToken()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafCoefficientToken(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Equal(Vp9TransformSize.Tx32X32, probe.TransformSize);
            Assert.Equal(0, probe.PlaneType);
            Assert.Equal(0, probe.ReferenceType);
            Assert.Equal(0, probe.CoefficientBand);
            Assert.Equal(0, probe.CoefficientContext);
            Assert.Equal(Vp9CoefficientToken.Category6, probe.Token);
            Assert.Equal(-16380, probe.DequantizedValue);
        });
    }

    [Fact]
    public void TryProbeFirstLeafCoefficientBlock_ForExternalMainFrame_DecodesDeterministicTx32Blocks()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafCoefficientBlock(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafCoefficientBlock(packet, secondState, out var secondProbes, out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.Equal(probes.Select(probe => probe.CoefficientsSha256), secondProbes.Select(probe => probe.CoefficientsSha256));
        Assert.All(probes, probe =>
        {
            Assert.Equal(Vp9TransformSize.Tx32X32, probe.TransformSize);
            Assert.Equal(0, probe.PlaneType);
            Assert.Equal(0, probe.ReferenceType);
            Assert.Equal(Vp9TransformType.DctDct, probe.TransformType);
            Assert.Equal(0, probe.Row4);
            Assert.Equal(0, probe.Column4);
            Assert.Equal(0, probe.InitialCoefficientContext);
            Assert.Equal(1, probe.Eob);
            Assert.Equal(1, probe.NonZeroCount);
            Assert.Equal(1024, probe.DequantizedCoefficients.Length);
            Assert.Equal(-16375, probe.DequantizedCoefficients[0]);
            Assert.Equal(0, probe.FirstNonZeroRasterIndex);
            Assert.Equal(0, probe.LastNonZeroRasterIndex);
            Assert.Equal("aef78701b7e01c244066b053118a778f174f3268be36c4f49d40cfd4c03779a0", probe.CoefficientsSha256);
        });
    }

    [Fact]
    public void TryProbeFirstLeafCoefficientBlock_ForExternalAlphaFrame_DecodesDeterministicTx32Blocks()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafCoefficientBlock(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafCoefficientBlock(packet, secondState, out var secondProbes, out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.Equal(probes.Select(probe => probe.CoefficientsSha256), secondProbes.Select(probe => probe.CoefficientsSha256));
        Assert.All(probes, probe =>
        {
            Assert.Equal(Vp9TransformSize.Tx32X32, probe.TransformSize);
            Assert.Equal(0, probe.PlaneType);
            Assert.Equal(0, probe.ReferenceType);
            Assert.Equal(Vp9TransformType.DctDct, probe.TransformType);
            Assert.Equal(0, probe.Row4);
            Assert.Equal(0, probe.Column4);
            Assert.Equal(0, probe.InitialCoefficientContext);
            Assert.Equal(1, probe.Eob);
            Assert.Equal(1, probe.NonZeroCount);
            Assert.Equal(1024, probe.DequantizedCoefficients.Length);
            Assert.Equal(-16380, probe.DequantizedCoefficients[0]);
            Assert.Equal(0, probe.FirstNonZeroRasterIndex);
            Assert.Equal(0, probe.LastNonZeroRasterIndex);
            Assert.Equal("11c1b2812be2ade82d7d2f30c5e2fd5f312cff471fd9294ed134e59f004335fc", probe.CoefficientsSha256);
        });
    }

    [Fact]
    public void TryProbeFirstLeafYCoefficientBlocks_ForExternalMainFrame_DecodesAllFirstLeafTx32Blocks()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafYCoefficientBlocks(packet, state, out var groups, out var diagnostic), diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafYCoefficientBlocks(packet, secondState, out var secondGroups, out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.Equal(8, groups.Count);
        Assert.Equal(
            groups.SelectMany(group => group.Blocks).Select(block => block.CoefficientsSha256),
            secondGroups.SelectMany(group => group.Blocks).Select(block => block.CoefficientsSha256));
        Assert.All(groups, group =>
        {
            Assert.Equal(Vp9BlockSize.Block64X64, group.BlockSize);
            Assert.Equal(Vp9TransformSize.Tx32X32, group.TransformSize);
            Assert.Equal(4, group.Blocks.Count);
            Assert.Equal([0, 0, 8, 8], group.Blocks.Select(block => block.Row4).ToArray());
            Assert.Equal([0, 8, 0, 8], group.Blocks.Select(block => block.Column4).ToArray());
            Assert.All(group.Blocks, block => Assert.Equal(Vp9TransformType.DctDct, block.TransformType));
            AssertCoefficientBlock(group.Blocks[0], initialContext: 0, eob: 1, nonZeroCount: 1, dc: -16375, firstNonZero: 0, lastNonZero: 0, hash: "aef78701b7e01c244066b053118a778f174f3268be36c4f49d40cfd4c03779a0", row4: 0, column4: 0);
            AssertCoefficientBlock(group.Blocks[1], initialContext: 1, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7", row4: 0, column4: 8);
            AssertCoefficientBlock(group.Blocks[2], initialContext: 1, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7", row4: 8, column4: 0);
            AssertCoefficientBlock(group.Blocks[3], initialContext: 0, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7", row4: 8, column4: 8);
        });
    }

    [Fact]
    public void TryProbeFirstLeafYCoefficientBlocks_ForExternalAlphaFrame_DecodesAllFirstLeafTx32Blocks()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafYCoefficientBlocks(packet, state, out var groups, out var diagnostic), diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstLeafYCoefficientBlocks(packet, secondState, out var secondGroups, out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.Equal(8, groups.Count);
        Assert.Equal(
            groups.SelectMany(group => group.Blocks).Select(block => block.CoefficientsSha256),
            secondGroups.SelectMany(group => group.Blocks).Select(block => block.CoefficientsSha256));
        Assert.All(groups, group =>
        {
            Assert.Equal(Vp9BlockSize.Block32X32, group.BlockSize);
            Assert.Equal(Vp9TransformSize.Tx32X32, group.TransformSize);
            Assert.Single(group.Blocks);
            AssertCoefficientBlock(group.Blocks[0], initialContext: 0, eob: 1, nonZeroCount: 1, dc: -16380, firstNonZero: 0, lastNonZero: 0, hash: "11c1b2812be2ade82d7d2f30c5e2fd5f312cff471fd9294ed134e59f004335fc", row4: 0, column4: 0);
        });
    }

    [Fact]
    public void TryProbeFirstSuperblockSyntax_ForExternalMainFrame_DrainsYuvCoefficientGroups()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstSuperblockSyntax(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Single(probe.ModeInfos);
            Assert.Equal(3, probe.CoefficientGroups.Count);
            var mode = probe.ModeInfos[0];
            Assert.Equal([Vp9PartitionType.None], mode.PartitionPath);
            Assert.Equal(Vp9BlockSize.Block64X64, mode.BlockSize);
            Assert.False(mode.Skip);
            Assert.Equal(0, mode.SkipContext);
            Assert.Equal(Vp9TransformSize.Tx32X32, mode.TransformSize);
            Assert.Equal(Vp9PredictionMode.Dc, mode.YMode);
            Assert.Equal(Vp9PredictionMode.Dc, mode.UvMode);

            Assert.Equal(Vp9TransformSize.Tx32X32, probe.CoefficientGroups[0].TransformSize);
            Assert.Equal([1, 0, 0, 0], probe.CoefficientGroups[0].Blocks.Select(block => block.Eob).ToArray());
            Assert.Equal([-16375, 0, 0, 0], probe.CoefficientGroups[0].Blocks.Select(block => block.DequantizedCoefficients[0]).ToArray());
            Assert.Equal([0, 1, 1, 0], probe.CoefficientGroups[0].Blocks.Select(block => block.InitialCoefficientContext).ToArray());
            AssertCoefficientBlock(probe.CoefficientGroups[1].Blocks[0], initialContext: 0, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7", planeType: 1);
            AssertCoefficientBlock(probe.CoefficientGroups[2].Blocks[0], initialContext: 0, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7", planeType: 1);
        });
    }

    [Fact]
    public void TryProbeFirstSuperblockSyntax_ForExternalAlphaFrame_DrainsSplitYuvCoefficientGroups()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFirstSuperblockSyntax(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Equal(8, probes.Count);
        Assert.All(probes, probe =>
        {
            Assert.Equal(4, probe.ModeInfos.Count);
            Assert.Equal(12, probe.CoefficientGroups.Count);
            Assert.Equal([0, 0, 0, 2], probe.ModeInfos.Select(mode => mode.SkipContext).ToArray());
            Assert.Equal([false, true, true, true], probe.ModeInfos.Select(mode => mode.Skip).ToArray());
            Assert.All(probe.ModeInfos, mode =>
            {
                Assert.Equal([Vp9PartitionType.Split, Vp9PartitionType.None], mode.PartitionPath);
                Assert.Equal(Vp9BlockSize.Block32X32, mode.BlockSize);
                Assert.Equal(Vp9TransformSize.Tx32X32, mode.TransformSize);
                Assert.Equal(1, mode.TransformSizeContext);
                Assert.Equal(Vp9PredictionMode.Dc, mode.YMode);
                Assert.Equal(Vp9PredictionMode.Dc, mode.UvMode);
            });

            Assert.Equal([0, 0, 4, 4], probe.ModeInfos.Select(mode => mode.MiRow).ToArray());
            Assert.Equal([0, 4, 0, 4], probe.ModeInfos.Select(mode => mode.MiColumn - probe.ModeInfos[0].MiColumn).ToArray());
            AssertCoefficientBlock(probe.CoefficientGroups[0].Blocks[0], initialContext: 0, eob: 1, nonZeroCount: 1, dc: -16380, firstNonZero: 0, lastNonZero: 0, hash: "11c1b2812be2ade82d7d2f30c5e2fd5f312cff471fd9294ed134e59f004335fc");
            for (var i = 1; i < probe.CoefficientGroups.Count; i++)
            {
                var expectedLength = probe.CoefficientGroups[i].TransformSize == Vp9TransformSize.Tx16X16 ? 256 : 1024;
                var expectedPlaneType = i % 3 == 0 ? 0 : 1;
                var expectedHash = expectedLength == 256
                    ? "5f70bf18a086007016e948b04aed3b82103a36bea41755b6cddfaf10ace3c6ef"
                    : "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7";
                AssertCoefficientBlock(probe.CoefficientGroups[i].Blocks[0], initialContext: 0, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: expectedHash, coefficientLength: expectedLength, planeType: expectedPlaneType);
            }
        });
    }

    [Fact]
    public void TryProbeFullFrameSyntax_ForExternalMainFrame_ReadsCompleteSyntax()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFullFrameSyntax(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Null(diagnostic);
        Assert.Equal(924, probes.Count);
        Assert.Equal(5320, probes.SelectMany(probe => probe.ModeInfos).Count());
        Assert.Equal(15960, probes.SelectMany(probe => probe.CoefficientGroups).Count());
        Assert.Equal(25326, probes.SelectMany(probe => probe.CoefficientGroups).SelectMany(group => group.Blocks).Count());
        var lastProbe = probes[^1];
        Assert.Equal(7, lastProbe.TileIndex);
        Assert.Single(lastProbe.ModeInfos);
        Assert.Equal(3, lastProbe.CoefficientGroups.Count);
        var lastMode = lastProbe.ModeInfos[0];
        Assert.Equal(168, lastMode.MiRow);
        Assert.Equal(328, lastMode.MiColumn);
        Assert.Equal(Vp9BlockSize.Block32X16, lastMode.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx16X16, lastMode.TransformSize);
        Assert.Equal(Vp9PredictionMode.Dc, lastMode.YMode);
        Assert.Equal(Vp9PredictionMode.Dc, lastMode.UvMode);
        Assert.True(lastMode.Skip);
        var lastGroup = lastProbe.CoefficientGroups[^1];
        Assert.Equal(Vp9BlockSize.Block32X16, lastGroup.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx8X8, lastGroup.TransformSize);
        Assert.Equal(2, lastGroup.Blocks.Count);
    }

    [Fact]
    public void TryProbeFullFrameSyntax_ForExternalAlphaFrame_ReadsCompleteSyntax()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryProbeFullFrameSyntax(packet, state, out var probes, out var diagnostic), diagnostic?.Message);

        Assert.Null(diagnostic);
        Assert.Equal(924, probes.Count);
        Assert.Equal(2218, probes.SelectMany(probe => probe.ModeInfos).Count());
        Assert.Equal(6654, probes.SelectMany(probe => probe.CoefficientGroups).Count());
        Assert.Equal(10488, probes.SelectMany(probe => probe.CoefficientGroups).SelectMany(group => group.Blocks).Count());
        var lastProbe = probes[^1];
        Assert.Equal(7, lastProbe.TileIndex);
        Assert.Equal(2, lastProbe.ModeInfos.Count);
        Assert.Equal(6, lastProbe.CoefficientGroups.Count);
        var lastMode = lastProbe.ModeInfos[^1];
        Assert.Equal(168, lastMode.MiRow);
        Assert.Equal(330, lastMode.MiColumn);
        Assert.Equal(Vp9BlockSize.Block16X8, lastMode.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx8X8, lastMode.TransformSize);
        Assert.Equal(Vp9PredictionMode.Dc, lastMode.YMode);
        Assert.Equal(Vp9PredictionMode.Dc, lastMode.UvMode);
        Assert.True(lastMode.Skip);
        var lastGroup = lastProbe.CoefficientGroups[^1];
        Assert.Equal(Vp9BlockSize.Block16X8, lastGroup.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx4X4, lastGroup.TransformSize);
        Assert.Equal(2, lastGroup.Blocks.Count);
    }

    [Fact]
    public void TryProbeFirstBlock16X16LumaTx4Group_ForExternalMainFrame_ReadsFirstGatedGroup()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstBlock16X16LumaTx4Group(
                packet,
                state,
                out var modeInfo,
                out var group,
                out var diagnostic),
            diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstBlock16X16LumaTx4Group(
                packet,
                secondState,
                out _,
                out var secondGroup,
                out var secondDiagnostic),
            secondDiagnostic?.Message);

        Assert.NotNull(modeInfo);
        Assert.NotNull(group);
        Assert.NotNull(secondGroup);
        Assert.Equal(130, modeInfo.MiRow);
        Assert.Equal(38, modeInfo.MiColumn);
        Assert.Equal(Vp9BlockSize.Block16X16, modeInfo.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx4X4, modeInfo.TransformSize);
        Assert.Equal(Vp9PredictionMode.Vertical, modeInfo.YMode);
        Assert.Equal(Vp9TransformSize.Tx4X4, group.TransformSize);
        Assert.Equal(16, group.Blocks.Count);
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3], group.Blocks.Select(block => block.Row4).ToArray());
        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3], group.Blocks.Select(block => block.Column4).ToArray());
        Assert.All(group.Blocks, block =>
        {
            Assert.Equal(Vp9TransformType.AdstDct, block.TransformType);
            Assert.Equal(16, block.DequantizedCoefficients.Length);
        });
        Assert.Equal([1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0], group.Blocks.Select(block => block.InitialCoefficientContext).ToArray());
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0, 4, 0, 0, 0], group.Blocks.Select(block => block.Eob).ToArray());
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 2, 0, 0, 0], group.Blocks.Select(block => block.NonZeroCount).ToArray());
        Assert.Equal(
            [
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "208d57a2642c3854e8e8b459190e2f88e4aeed9fc35c2cb87bae4b1065fa8473",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "c98dcbd41ec1e4a6b196644ffd1917d5e53882ecbda34b38f89cb78c6743530b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b"
            ],
            group.Blocks.Select(block => block.CoefficientsSha256).ToArray());
        Assert.Equal(
            group.Blocks.Select(block => block.CoefficientsSha256),
            secondGroup.Blocks.Select(block => block.CoefficientsSha256));
        Assert.Equal(
            group.Blocks.Select(block => block.InitialCoefficientContext),
            secondGroup.Blocks.Select(block => block.InitialCoefficientContext));
        Assert.Equal(
            group.Blocks.Select(block => block.Eob),
            secondGroup.Blocks.Select(block => block.Eob));
    }

    [Fact]
    public void TryProbeFirstBlock16X16LumaTx4Group_ForExternalAlphaFrame_ReadsFirstGatedGroup()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstBlock16X16LumaTx4Group(
                packet,
                state,
                out var modeInfo,
                out var group,
                out var diagnostic),
            diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstBlock16X16LumaTx4Group(
                packet,
                secondState,
                out _,
                out var secondGroup,
                out var secondDiagnostic),
            secondDiagnostic?.Message);

        Assert.NotNull(modeInfo);
        Assert.NotNull(group);
        Assert.NotNull(secondGroup);
        Assert.Equal(128, modeInfo.MiRow);
        Assert.Equal(70, modeInfo.MiColumn);
        Assert.Equal(Vp9BlockSize.Block16X16, modeInfo.BlockSize);
        Assert.Equal(Vp9TransformSize.Tx4X4, modeInfo.TransformSize);
        Assert.Equal(Vp9PredictionMode.Dc, modeInfo.YMode);
        Assert.Equal(Vp9TransformSize.Tx4X4, group.TransformSize);
        Assert.Equal(16, group.Blocks.Count);
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3], group.Blocks.Select(block => block.Row4).ToArray());
        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3], group.Blocks.Select(block => block.Column4).ToArray());
        Assert.All(group.Blocks, block =>
        {
            Assert.Equal(Vp9TransformType.DctDct, block.TransformType);
            Assert.Equal(16, block.DequantizedCoefficients.Length);
        });
        Assert.Equal([1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], group.Blocks.Select(block => block.InitialCoefficientContext).ToArray());
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 15], group.Blocks.Select(block => block.Eob).ToArray());
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 12], group.Blocks.Select(block => block.NonZeroCount).ToArray());
        Assert.Equal(
            [
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b",
                "c0fbd41c36754369f391b1954f17603e0c30da4c00f4a84a55b8b0e3810a0b33"
            ],
            group.Blocks.Select(block => block.CoefficientsSha256).ToArray());
        Assert.Equal(
            group.Blocks.Select(block => block.CoefficientsSha256),
            secondGroup.Blocks.Select(block => block.CoefficientsSha256));
        Assert.Equal(
            group.Blocks.Select(block => block.InitialCoefficientContext),
            secondGroup.Blocks.Select(block => block.InitialCoefficientContext));
        Assert.Equal(
            group.Blocks.Select(block => block.Eob),
            secondGroup.Blocks.Select(block => block.Eob));
    }

    [Theory]
    [InlineData(
        "/tmp/vp9-main-frame-0.vp9",
        30398,
        "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9",
        3_128_047,
        "c8134844215286d7716b26e190df1add2ba69336bca055859b1d44e055db13b0",
        1_332_592,
        "2ee2e98171d4c15074812c3afe61eddd602368352e1848d5ec545c7e1740da06",
        897_727,
        "f668c4f40fa59027caa47a6503849d7043f475961dade9eba635a134940564e6",
        897_728,
        "d561a7b36054a0fee55ef6e0272f11189f61d58800a8204db25d8af352b2827e")]
    [InlineData(
        "/tmp/vp9-alpha-frame-0.vp9",
        6233,
        "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329",
        2_752_618,
        "93e06f2267476282f4c1a2565ed67255ca14e1784350e4ceb13ba5f9292be0ff",
        957_162,
        "5d50988525d0d0b0b864c65ee1272492d9b91547c9fd75fbd68154d8b959638d",
        897_728,
        "00e7569e2bc6b0d8d8bc2464a82456bf8a6e12ab19b041330d8ec119adfd3476",
        897_728,
        "00e7569e2bc6b0d8d8bc2464a82456bf8a6e12ab19b041330d8ec119adfd3476")]
    public void TryReconstructFullFrame_ForExternalSamples_WritesDeterministicUnfilteredYuv(
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
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFullFrame(packet, state, out var frame, out var diagnostic), diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Null(diagnostic);
        Assert.Equal(2656, frame.Width);
        Assert.Equal(1352, frame.Height);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(5_386_368, frame.Pixels.Length);
        Assert.Equal(expectedNonZero, frame.Pixels.Count(value => value != 0));
        Assert.Equal(expectedHash, Hash(frame.Pixels));
        AssertPlaneHash(frame, planeIndex: 0, expectedYNonZero, expectedYHash);
        AssertPlaneHash(frame, planeIndex: 1, expectedUNonZero, expectedUHash);
        AssertPlaneHash(frame, planeIndex: 2, expectedVNonZero, expectedVHash);
    }

    [Theory]
    [InlineData(
        "/tmp/vp9-main-frame-0.vp9",
        30398,
        "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9",
        5320,
        15960,
        25326,
        168,
        328,
        Vp9BlockSize.Block32X16,
        Vp9TransformSize.Tx16X16,
        Vp9TransformSize.Tx8X8)]
    [InlineData(
        "/tmp/vp9-alpha-frame-0.vp9",
        6233,
        "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329",
        2218,
        6654,
        10488,
        168,
        330,
        Vp9BlockSize.Block16X8,
        Vp9TransformSize.Tx8X8,
        Vp9TransformSize.Tx4X4)]
    public void TryReconstructFullFrameWithSyntax_ForExternalSamples_RetainsLoopFilterMetadata(
        string path,
        int expectedLength,
        string expectedSha256,
        int expectedModeCount,
        int expectedGroupCount,
        int expectedCoefficientBlockCount,
        int expectedLastMiRow,
        int expectedLastMiColumn,
        Vp9BlockSize expectedLastBlockSize,
        Vp9TransformSize expectedLastYTransformSize,
        Vp9TransformSize expectedLastUvTransformSize)
    {
        var packet = ReadRequiredSample(path, expectedLength, expectedSha256);
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFullFrameWithSyntax(packet, state, out var reconstructed, out var diagnostic), diagnostic?.Message);

        Assert.NotNull(reconstructed);
        Assert.Null(diagnostic);
        Assert.Equal(2656, reconstructed.Frame.Width);
        Assert.Equal(1352, reconstructed.Frame.Height);
        Assert.Equal(924, reconstructed.Superblocks.Count);
        Assert.Equal(expectedModeCount, reconstructed.ModeBlocks.Count);
        Assert.Equal(expectedGroupCount, reconstructed.CoefficientGroupCount);
        Assert.Equal(expectedCoefficientBlockCount, reconstructed.CoefficientBlockCount);
        Assert.Equal(reconstructed.MiRows * reconstructed.MiColumns, reconstructed.CoveredMiUnitCount);
        Assert.Equal(169, reconstructed.MiRows);
        Assert.Equal(332, reconstructed.MiColumns);
        Assert.Contains(reconstructed.ModeBlocks, modeBlock => modeBlock.NonZeroCoefficientBlockCount > 0);

        Assert.True(reconstructed.TryGetModeBlockAtMi(168, 331, out var lastVisibleBlock));
        Assert.NotNull(lastVisibleBlock);
        Assert.Equal(expectedLastMiRow, lastVisibleBlock.ModeInfo.MiRow);
        Assert.Equal(expectedLastMiColumn, lastVisibleBlock.ModeInfo.MiColumn);
        Assert.Equal(expectedLastBlockSize, lastVisibleBlock.ModeInfo.BlockSize);
        Assert.Equal(expectedLastYTransformSize, lastVisibleBlock.CoefficientGroups[0].TransformSize);
        Assert.Equal(expectedLastUvTransformSize, lastVisibleBlock.CoefficientGroups[1].TransformSize);
        Assert.Equal(expectedLastUvTransformSize, lastVisibleBlock.CoefficientGroups[2].TransformSize);

        Assert.False(reconstructed.TryGetModeBlockAtMi(169, 0, out _));
        Assert.False(reconstructed.TryGetModeBlockAtMi(0, 332, out _));
    }

    [Fact]
    public void TryReconstructFirstLeafYDc_ForExternalMainFrame_WritesDeterministicYBlocks()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFirstLeafYDc(packet, state, out var frame, out var diagnostic), diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(5_386_368, frame.Pixels.Length);
        Assert.Equal(0, frame.Pixels.Count(value => value != 0));
        foreach (var x in new[] { 0, 320, 640, 960, 1344, 1664, 1984, 2304 })
        {
            AssertYBlock(frame, x, y: 0, size: 64, expected: 0);
        }
    }

    [Fact]
    public void TryReconstructFirstLeafYDc_ForExternalAlphaFrame_ClipsNegativeDcToZero()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFirstLeafYDc(packet, state, out var frame, out var diagnostic), diagnostic?.Message);

        Assert.NotNull(frame);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(5_386_368, frame.Pixels.Length);
        Assert.All(frame.Pixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void TryReconstructFirstSuperblockDcOnly_ForExternalMainFrame_WritesDeterministicYuvBlocks()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFirstSuperblockDcOnly(packet, state, out var frame, out var diagnostic), diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(Vp9TileSyntaxScanner.TryReconstructFirstSuperblockDcOnly(packet, secondState, out var secondFrame, out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.NotNull(frame);
        Assert.NotNull(secondFrame);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(5_386_368, frame.Pixels.Length);
        Assert.Equal(16_384, frame.Pixels.Count(value => value != 0));
        Assert.Equal("5580ae7be668d41b95e4e82b0578998e2bd67f2ae62f1b771d64091a8fe2dbee", Hash(frame.Pixels));
        Assert.Equal(Hash(frame.Pixels), Hash(secondFrame.Pixels));
        AssertPlaneHash(frame, planeIndex: 0, expectedNonZero: 0, expectedHash: "c67cc10105aa9f7de24a1a7ef211f10d682dd62291f292f4a95b09f54f234e34");
        AssertPlaneHash(frame, planeIndex: 1, expectedNonZero: 8_192, expectedHash: "584433f86bd2b8b03a00dfa9b0e2af08b2ba01fbceee5c745619efcfc87204f1");
        AssertPlaneHash(frame, planeIndex: 2, expectedNonZero: 8_192, expectedHash: "584433f86bd2b8b03a00dfa9b0e2af08b2ba01fbceee5c745619efcfc87204f1");
        foreach (var x in new[] { 0, 320, 640, 960, 1344, 1664, 1984, 2304 })
        {
            AssertPlaneBlock(frame, planeIndex: 0, x, y: 0, size: 64, expected: 0);
            AssertPlaneBlock(frame, planeIndex: 1, x / 2, y: 0, size: 32, expected: 128);
            AssertPlaneBlock(frame, planeIndex: 2, x / 2, y: 0, size: 32, expected: 128);
        }
    }

    [Fact]
    public void TryReconstructFirstSuperblockDcOnly_ForExternalAlphaFrame_ReconstructsSkippedChromaPredictors()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.True(Vp9TileSyntaxScanner.TryReconstructFirstSuperblockDcOnly(packet, state, out var frame, out var diagnostic), diagnostic?.Message);

        var secondState = CreateState(packet);
        Assert.True(Vp9TileSyntaxScanner.TryReconstructFirstSuperblockDcOnly(packet, secondState, out var secondFrame, out var secondDiagnostic), secondDiagnostic?.Message);

        Assert.NotNull(frame);
        Assert.NotNull(secondFrame);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(5_386_368, frame.Pixels.Length);
        Assert.Equal(16_384, frame.Pixels.Count(value => value != 0));
        Assert.Equal("5580ae7be668d41b95e4e82b0578998e2bd67f2ae62f1b771d64091a8fe2dbee", Hash(frame.Pixels));
        Assert.Equal(Hash(frame.Pixels), Hash(secondFrame.Pixels));
        AssertPlaneHash(frame, planeIndex: 0, expectedNonZero: 0, expectedHash: "c67cc10105aa9f7de24a1a7ef211f10d682dd62291f292f4a95b09f54f234e34");
        AssertPlaneHash(frame, planeIndex: 1, expectedNonZero: 8_192, expectedHash: "584433f86bd2b8b03a00dfa9b0e2af08b2ba01fbceee5c745619efcfc87204f1");
        AssertPlaneHash(frame, planeIndex: 2, expectedNonZero: 8_192, expectedHash: "584433f86bd2b8b03a00dfa9b0e2af08b2ba01fbceee5c745619efcfc87204f1");
        foreach (var x in new[] { 0, 320, 640, 960, 1344, 1664, 1984, 2304 })
        {
            AssertPlaneBlock(frame, planeIndex: 0, x, y: 0, size: 64, expected: 0);
            AssertPlaneBlock(frame, planeIndex: 1, x / 2, y: 0, size: 32, expected: 128);
            AssertPlaneBlock(frame, planeIndex: 2, x / 2, y: 0, size: 32, expected: 128);
        }
    }

    private static Vp9KeyFrameDecodeState CreateState(byte[] packet)
    {
        var header = Vp9FrameHeaderParser.Parse(packet);
        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, header, out var compressedHeader, out var compressedDiagnostic), compressedDiagnostic?.Message);
        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var layoutDiagnostic), layoutDiagnostic?.Message);
        Assert.True(Vp9KeyFrameDecodeState.TryCreate(header, compressedHeader!, tileBuffers, out var state, out var stateDiagnostic), stateDiagnostic?.Message);
        return state!;
    }

    private static Vp9FrameHeader CreateSyntheticOrdinaryInterHeader(int packetLength)
    {
        var header = Vp9FrameHeaderParser.Parse(Vp9TestPackets.CreateOrdinaryInterFramePacket());
        return header with
        {
            PacketLength = packetLength,
            HeaderSizeInBytes = 0,
            Width = 64,
            Height = 64,
            RenderWidth = 64,
            RenderHeight = 64,
            TileInfo = new Vp9TileInfo(
                MiColumns: 8,
                MiRows: 8,
                SuperblockColumns: 1,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 0,
                Log2TileRows: 0)
        };
    }

    private static Vp9FrameHeader CreateSyntheticTwoTileOrdinaryInterHeader(int packetLength)
    {
        var header = CreateSyntheticOrdinaryInterHeader(packetLength);
        return header with
        {
            Width = 128,
            RenderWidth = 128,
            TileInfo = new Vp9TileInfo(
                MiColumns: 16,
                MiRows: 8,
                SuperblockColumns: 2,
                MinLog2TileColumns: 0,
                MaxLog2TileColumns: 0,
                Log2TileColumns: 1,
                Log2TileRows: 0)
        };
    }

    private static Vp9CompressedHeader CreateSyntheticInterCompressedHeader()
    {
        return new Vp9CompressedHeader(
            Vp9TransformMode.Only4X4,
            Vp9FrameContext.CreateDefault(),
            TxProbabilityUpdateCount: 0,
            CoefficientProbabilityUpdateCount: 0,
            SkipProbabilityUpdateCount: 0,
            ReferenceMode: Vp9ReferenceMode.Single);
    }

    private static Vp9InterBlockModeInfoProbe CreateInterModeBlock(
        int miRow,
        int miColumn,
        Vp9InterPredictionMode predictionMode,
        int tileIndex = 0)
    {
        var modeInfo = new Vp9InterModeInfoProbe(
            Vp9BlockSize.Block8X8,
            Skip: true,
            SkipContext: 0,
            IsInterBlock: true,
            IntraInterContext: 0,
            Vp9TransformSize.Tx4X4,
            TransformSizeContext: 0,
            Vp9ReferenceMode.Single,
            Vp9InterReferenceFrame.Last,
            SingleReferenceContext0: 0,
            SingleReferenceContext1: null,
            predictionMode,
            InterModeContext: 0,
            Vp9InterpolationFilter.EightTap);

        return new Vp9InterBlockModeInfoProbe(
            TileIndex: tileIndex,
            miRow,
            miColumn,
            PartitionPath: [Vp9PartitionType.None],
            modeInfo);
    }

    private static IEnumerable<Vp9CoefficientBlockGroupProbe> CreateEmptyInterTx4Groups(Vp9BlockSize blockSize)
    {
        yield return new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            blockSize,
            Vp9TransformSize.Tx4X4,
            CreateEmptyInterTx4Blocks(width4: 2, height4: 2, planeType: 0));
        yield return new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            blockSize,
            Vp9TransformSize.Tx4X4,
            CreateEmptyInterTx4Blocks(width4: 1, height4: 1, planeType: 1));
        yield return new Vp9CoefficientBlockGroupProbe(
            TileIndex: 0,
            blockSize,
            Vp9TransformSize.Tx4X4,
            CreateEmptyInterTx4Blocks(width4: 1, height4: 1, planeType: 1));
    }

    private static IReadOnlyList<Vp9CoefficientBlockProbe> CreateEmptyInterTx4Blocks(
        int width4,
        int height4,
        int planeType)
    {
        var blocks = new List<Vp9CoefficientBlockProbe>();
        for (var row4 = 0; row4 < height4; row4++)
        {
            for (var column4 = 0; column4 < width4; column4++)
            {
                blocks.Add(new Vp9CoefficientBlockProbe(
                    TileIndex: 0,
                    Vp9TransformSize.Tx4X4,
                    Vp9TransformType.DctDct,
                    row4,
                    column4,
                    planeType,
                    Vp9ResidualSyntax.InterBlockReferenceType,
                    InitialCoefficientContext: 0,
                    Eob: 0,
                    NonZeroCount: 0,
                    FirstNonZeroRasterIndex: -1,
                    LastNonZeroRasterIndex: -1,
                    DequantizedCoefficients: new int[16],
                    CoefficientsSha256: "synthetic"));
            }
        }

        return blocks;
    }

    private static void AssertSyntheticInterResidualGroups(byte[] tilePayload, bool expectedSkip)
    {
        var packet = tilePayload;
        var header = CreateSyntheticOrdinaryInterHeader(packet.Length);
        var compressedHeader = CreateSyntheticInterCompressedHeader();
        IReadOnlyList<Vp9TileBuffer> tileBuffers =
        [
            new Vp9TileBuffer(Index: 0, SizeFieldOffset: null, DataOffset: 0, Size: tilePayload.Length)
        ];

        Assert.True(
            Vp9TileSyntaxScanner.TryProbeFirstInterSuperblockResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out var probes,
                out var residualGroups,
                out var diagnostic),
            diagnostic?.Message);

        Assert.Single(probes);
        Assert.Single(probes[0].ModeInfos);
        Assert.Equal(expectedSkip, probes[0].ModeInfos[0].ModeInfo.Skip);
        Assert.Equal([256, 64, 64], residualGroups.Select(group => group.Blocks.Count).ToArray());
        Assert.All(residualGroups, group =>
        {
            Assert.Equal(Vp9TransformSize.Tx4X4, group.TransformSize);
            Assert.All(group.Blocks, block =>
            {
                Assert.Equal(Vp9ResidualSyntax.InterBlockReferenceType, block.ReferenceType);
                Assert.Equal(Vp9TransformType.DctDct, block.TransformType);
                Assert.Equal(0, block.NonZeroCount);
            });
        });
    }

    private static Vp9DecodedFrame CreatePatternYuvFrame(int width, int height)
    {
        var buffer = Vp9YuvFrameBuffer.Create(width, height);
        for (var i = 0; i < buffer.YPlane.Length; i++)
        {
            buffer.Pixels[buffer.YPlane.Offset + i] = (byte)i;
        }

        for (var i = 0; i < buffer.UPlane.Length; i++)
        {
            buffer.Pixels[buffer.UPlane.Offset + i] = (byte)(100 + i);
        }

        for (var i = 0; i < buffer.VPlane.Length; i++)
        {
            buffer.Pixels[buffer.VPlane.Offset + i] = (byte)(200 + i);
        }

        return buffer.ToDecodedFrame();
    }

    private static void AssertCoefficientBlock(
        Vp9CoefficientBlockProbe block,
        int initialContext,
        int eob,
        int nonZeroCount,
        int dc,
        int firstNonZero,
        int lastNonZero,
        string hash,
        int coefficientLength = 1024,
        int planeType = 0,
        Vp9TransformType? transformType = Vp9TransformType.DctDct,
        int? row4 = null,
        int? column4 = null)
    {
        Assert.Equal(planeType, block.PlaneType);
        Assert.Equal(0, block.ReferenceType);
        if (transformType is { } expectedTransformType)
        {
            Assert.Equal(expectedTransformType, block.TransformType);
        }

        if (row4 is { } expectedRow4)
        {
            Assert.Equal(expectedRow4, block.Row4);
        }

        if (column4 is { } expectedColumn4)
        {
            Assert.Equal(expectedColumn4, block.Column4);
        }

        Assert.Equal(initialContext, block.InitialCoefficientContext);
        Assert.Equal(eob, block.Eob);
        Assert.Equal(nonZeroCount, block.NonZeroCount);
        Assert.Equal(coefficientLength, block.DequantizedCoefficients.Length);
        Assert.Equal(dc, block.DequantizedCoefficients[0]);
        Assert.Equal(firstNonZero, block.FirstNonZeroRasterIndex);
        Assert.Equal(lastNonZero, block.LastNonZeroRasterIndex);
        Assert.Equal(hash, block.CoefficientsSha256);
    }

    private static void AssertYBlock(Vp9DecodedFrame frame, int x, int y, int size, byte expected)
    {
        AssertPlaneBlock(frame, planeIndex: 0, x, y, size, expected);
    }

    private static void AssertPlaneBlock(Vp9DecodedFrame frame, int planeIndex, int x, int y, int size, byte expected)
    {
        var yPlane = frame.Planes[planeIndex];
        for (var row = 0; row < size; row++)
        {
            var offset = yPlane.Offset + ((y + row) * yPlane.Stride) + x;
            Assert.All(frame.Pixels.Skip(offset).Take(size), value => Assert.Equal(expected, value));
        }
    }

    private static void AssertPlaneHash(Vp9DecodedFrame frame, int planeIndex, int expectedNonZero, string expectedHash)
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
