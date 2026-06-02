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
            Assert.Equal([1, 0, 1, 0], probe.CoefficientGroups[0].Blocks.Select(block => block.Eob).ToArray());
            Assert.Equal([16625, 0, 200, 0], probe.CoefficientGroups[0].Blocks.Select(block => block.DequantizedCoefficients[0]).ToArray());
            Assert.Equal([0, 1, 1, 1], probe.CoefficientGroups[0].Blocks.Select(block => block.InitialCoefficientContext).ToArray());
            AssertCoefficientBlock(probe.CoefficientGroups[1].Blocks[0], initialContext: 0, eob: 0, nonZeroCount: 0, dc: 0, firstNonZero: -1, lastNonZero: -1, hash: "ad7facb2586fc6e966c004d7d1d16b024f5805ff7cb47c7a85dabd8b48892ca7", planeType: 1);
            AssertCoefficientBlock(probe.CoefficientGroups[2].Blocks[0], initialContext: 0, eob: 1, nonZeroCount: 1, dc: 25, firstNonZero: 0, lastNonZero: 0, hash: "08facbceb1744917b5a2b39ff4509b223010125c9992bca76b7dec06871a283d", planeType: 1);
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
    public void TryProbeFullFrameSyntax_ForExternalMainFrame_ReturnsConcreteBlock16Unsupported()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var state = CreateState(packet);

        Assert.False(Vp9TileSyntaxScanner.TryProbeFullFrameSyntax(packet, state, out var probes, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, diagnostic.Code);
        Assert.Equal("VP9 key-frame syntax probe supports only 4x4, 4x8, 8x4, 8x8, 32x32, or 64x64 leaf blocks, not Block16X16.", diagnostic.Message);
        Assert.Equal(7, probes.Count);
        Assert.Equal(7, probes.SelectMany(probe => probe.ModeInfos).Count());
        Assert.Equal(21, probes.SelectMany(probe => probe.CoefficientGroups).Count());
    }

    [Fact]
    public void TryProbeFullFrameSyntax_ForExternalAlphaFrame_ReturnsConcreteBlock16Unsupported()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329");
        var state = CreateState(packet);

        Assert.False(Vp9TileSyntaxScanner.TryProbeFullFrameSyntax(packet, state, out var probes, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, diagnostic.Code);
        Assert.Equal("VP9 key-frame syntax probe supports only 4x4, 4x8, 8x4, 8x8, 32x32, or 64x64 leaf blocks, not Block16X16.", diagnostic.Message);
        Assert.Equal(5, probes.Count);
        Assert.Equal(8, probes.SelectMany(probe => probe.ModeInfos).Count());
        Assert.Equal(24, probes.SelectMany(probe => probe.CoefficientGroups).Count());
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
        Assert.Equal(32768, frame.Pixels.Count(value => value != 0));
        foreach (var x in new[] { 0, 320, 640, 960, 1344, 1664, 1984, 2304 })
        {
            AssertYBlock(frame, x, y: 0, size: 64, expected: 255);
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
        Assert.Equal(49_152, frame.Pixels.Count(value => value != 0));
        Assert.Equal("05f0ab1f0ba4dca0c58a6e12d24999eed02bcaca0a1a52af8d66bcfc38cd4f3b", Hash(frame.Pixels));
        Assert.Equal(Hash(frame.Pixels), Hash(secondFrame.Pixels));
        AssertPlaneHash(frame, planeIndex: 0, expectedNonZero: 32_768, expectedHash: "e888c7428609dd5f7974db8379eba2fd70b2ff631e87d367ee05d3747b4dc696");
        AssertPlaneHash(frame, planeIndex: 1, expectedNonZero: 8_192, expectedHash: "584433f86bd2b8b03a00dfa9b0e2af08b2ba01fbceee5c745619efcfc87204f1");
        AssertPlaneHash(frame, planeIndex: 2, expectedNonZero: 8_192, expectedHash: "584433f86bd2b8b03a00dfa9b0e2af08b2ba01fbceee5c745619efcfc87204f1");
        foreach (var x in new[] { 0, 320, 640, 960, 1344, 1664, 1984, 2304 })
        {
            AssertPlaneBlock(frame, planeIndex: 0, x, y: 0, size: 64, expected: 255);
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
        int planeType = 0)
    {
        Assert.Equal(planeType, block.PlaneType);
        Assert.Equal(0, block.ReferenceType);
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
