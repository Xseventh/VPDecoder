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
            Assert.Equal(16625, probe.DequantizedValue);
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
            Assert.Equal(0, probe.InitialCoefficientContext);
            Assert.Equal(1, probe.Eob);
            Assert.Equal(1, probe.NonZeroCount);
            Assert.Equal(1024, probe.DequantizedCoefficients.Length);
            Assert.Equal(16625, probe.DequantizedCoefficients[0]);
            Assert.Equal(0, probe.FirstNonZeroRasterIndex);
            Assert.Equal(0, probe.LastNonZeroRasterIndex);
            Assert.Equal("3878815e7c5359a006b9706f73c0c80f445f8dc8e063739dd09adf63bb4df1fd", probe.CoefficientsSha256);
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
            AssertCoefficientBlock(group.Blocks[0], initialContext: 0, eob: 1, nonZeroCount: 1, dc: 16625, firstNonZero: 0, lastNonZero: 0, hash: "3878815e7c5359a006b9706f73c0c80f445f8dc8e063739dd09adf63bb4df1fd");
            AssertCoefficientBlock(group.Blocks[1], initialContext: 1, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7");
            AssertCoefficientBlock(group.Blocks[2], initialContext: 1, eob: 1, nonZeroCount: 1, dc: 200, firstNonZero: 0, lastNonZero: 0, hash: "3dc873eebf42181783041b82c11f23cd10404d6ae0a36e5b48463a6deee1e21e");
            AssertCoefficientBlock(group.Blocks[3], initialContext: 1, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7");
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
            AssertCoefficientBlock(group.Blocks[0], initialContext: 0, eob: 1, nonZeroCount: 1, dc: -16380, firstNonZero: 0, lastNonZero: 0, hash: "11c1b2812be2ade82d7d2f30c5e2fd5f312cff471fd9294ed134e59f004335fc");
        });
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
        Assert.Equal(8192, frame.Pixels.Count(value => value != 0));
        Assert.All(frame.Pixels.Take(32), value => Assert.Equal(255, value));
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

    private static Vp9KeyFrameDecodeState CreateState(byte[] packet)
    {
        var header = Vp9FrameHeaderParser.Parse(packet);
        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, header, out var compressedHeader, out var compressedDiagnostic), compressedDiagnostic?.Message);
        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var layoutDiagnostic), layoutDiagnostic?.Message);
        Assert.True(Vp9KeyFrameDecodeState.TryCreate(header, compressedHeader!, tileBuffers, out var state, out var stateDiagnostic), stateDiagnostic?.Message);
        return state!;
    }

    private static void AssertCoefficientBlock(
        Vp9CoefficientBlockProbe block,
        int initialContext,
        int eob,
        int nonZeroCount,
        int dc,
        int firstNonZero,
        int lastNonZero,
        string hash)
    {
        Assert.Equal(Vp9TransformSize.Tx32X32, block.TransformSize);
        Assert.Equal(0, block.PlaneType);
        Assert.Equal(0, block.ReferenceType);
        Assert.Equal(initialContext, block.InitialCoefficientContext);
        Assert.Equal(eob, block.Eob);
        Assert.Equal(nonZeroCount, block.NonZeroCount);
        Assert.Equal(1024, block.DequantizedCoefficients.Length);
        Assert.Equal(dc, block.DequantizedCoefficients[0]);
        Assert.Equal(firstNonZero, block.FirstNonZeroRasterIndex);
        Assert.Equal(lastNonZero, block.LastNonZeroRasterIndex);
        Assert.Equal(hash, block.CoefficientsSha256);
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
