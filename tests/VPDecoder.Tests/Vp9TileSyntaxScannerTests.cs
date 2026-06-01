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
