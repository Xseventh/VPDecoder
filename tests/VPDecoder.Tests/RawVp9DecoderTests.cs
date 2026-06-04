namespace VPDecoder.Tests;

using System.Diagnostics;
using System.Reflection;

public sealed class RawVp9DecoderTests
{
    private const string MainFrameSamplePath = "/tmp/vp9-main-frame-0.vp9";
    private const string MainFrameSampleSha256 = "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9";
    private const string AlphaFrameSamplePath = "/tmp/vp9-alpha-frame-0.vp9";
    private const string AlphaFrameSampleSha256 = "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329";

    private static readonly byte[] MainFrameHeader =
    [
        0x82, 0x49, 0x83, 0x42, 0x10, 0xa5, 0xf0, 0x54, 0x76,
        0x04, 0x38, 0x24, 0x1c, 0x18, 0x66, 0x1c, 0x02, 0x80
    ];

    private static readonly byte[] ShowExistingFrame0Packet = [0x88];

    [Fact]
    public void DecodeFrame_WhenHeaderIsSupportedButSyntaxIsInvalid_ReturnsConcreteSyntaxDiagnosticWithHeader()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(Vp9TransformMode.Only4X4, result.CompressedHeader.TransformMode);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
        Assert.Contains("marker bit", result.Diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeFrame_WhenTileLayoutIsTruncated_ReturnsTruncatedPacket()
    {
        var packet = CreateHeaderAndCompressedHeaderOnlyPacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthDiffers_ReturnsDimensionMismatch()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(ExpectedWidth: 1, ExpectedHeight: 1352));

        Assert.False(result.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.DimensionMismatch, result.Diagnostic?.Code);
        Assert.NotNull(result.Header);
    }

    [Fact]
    public void DecodeFrame_WhenFrameExceedsPixelLimit_ReturnsAllocationLimitExceeded()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352, MaxPixelCount: 1));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.AllocationLimitExceeded, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenExpectedWidthIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(ExpectedWidth: 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenOutputFormatIsInvalid_ReturnsInvalidDecodeOptions()
    {
        var packet = CreatePaddedMainFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(OutputFormat: (Vp9OutputPixelFormat)99));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidDecodeOptions, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenCompressedHeaderMarkerIsInvalid_ReturnsInvalidPacket()
    {
        var packet = CreatePaddedMainFramePacket();
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);
        packet[frameHeader.HeaderSizeInBytes] = 0xff;
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Null(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenProfileIsUnsupported_ReturnsUnsupportedProfile()
    {
        var packet = CreatePaddedMainFramePacket();
        packet[0] = 0xa2;
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet);

        Assert.False(result.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedProfile, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenOrdinaryInterFrameReferencesAreMissing_ReturnsMissingReferenceFrame()
    {
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(CreatePaddedOrdinaryInterFramePacket(), new Vp9DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9FrameType.InterFrame, result.Header.FrameType);
        Assert.Equal(0x05, result.Header.RefreshFrameFlags);
        Assert.Null(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenOrdinaryInterFrameHasMismatchedReferenceSize_ReturnsReferenceScalingUnsupported()
    {
        var decoder = new RawVp9Decoder();
        var reference = CreatePatternYuvFrame(width: 32, height: 16);
        SeedReferenceFrame(decoder, reference, Vp9ColorRange.Studio, refreshFrameFlags: 0xff);

        var result = decoder.DecodeFrame(CreatePaddedOrdinaryInterFramePacket(), new Vp9DecodeOptions(16, 8));

        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, result.Diagnostic?.Code);
        Assert.Contains("scaling", result.Diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeFrame_WhenInterFrameUsesReferenceSize_ParsesReferenceDimensionsBeforeDecodeDiagnostic()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var keyFrame = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        var result = decoder.DecodeFrame(
            CreatePaddedOrdinaryInterFramePacket(
                sizeFromReference: true,
                tileInfoWidth: 2656),
            new Vp9DecodeOptions(2656, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(0, result.Header.FrameSizeReferenceIndex);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(1352, result.Header.Height);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenRestrictedOrdinaryInterFrameHasReference_DecodesAndRefreshesReference()
    {
        var decoder = new RawVp9Decoder();
        var reference = CreatePatternYuvFrame(width: 16, height: 8);
        SeedReferenceFrame(decoder, reference, Vp9ColorRange.Studio, refreshFrameFlags: 0xff);

        var result = decoder.DecodeFrame(CreatePaddedOrdinaryInterFramePacket(), new Vp9DecodeOptions(16, 8, Vp9OutputPixelFormat.Yuv420));
        var showExisting = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(16, 8, Vp9OutputPixelFormat.Yuv420));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9FrameType.InterFrame, result.Header.FrameType);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Yuv420, result.Frame.PixelFormat);
        Assert.Equal(16, result.Frame.Width);
        Assert.Equal(8, result.Frame.Height);
        Assert.Equal(Hash(reference.Pixels), Hash(result.Frame.Pixels));
        Assert.True(showExisting.Succeeded, showExisting.Diagnostic?.Message);
        Assert.Equal(Hash(reference.Pixels), Hash(showExisting.Frame!.Pixels));
    }

    [Fact]
    public void DecodeFrame_WhenOrdinaryInterFrameNewMvCandidateIsMissing_ReturnsUnsupportedInterFrameFeature()
    {
        var decoder = new RawVp9Decoder();
        var reference = CreatePatternYuvFrame(width: 16, height: 8);
        SeedReferenceFrame(decoder, reference, Vp9ColorRange.Studio, refreshFrameFlags: 0xff);

        var result = decoder.DecodeFrame(
            CreatePaddedOrdinaryInterFramePacket(tilePayload: [0x0c, 0x10, 0x00, 0x00]),
            new Vp9DecodeOptions(16, 8, Vp9OutputPixelFormat.Yuv420));

        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, result.Diagnostic?.Code);
        Assert.Contains("NEWMV", result.Diagnostic?.Message, StringComparison.Ordinal);
        Assert.Contains("candidate", result.Diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeFrame_WhenRestrictedOrdinaryInterFrameHasLoopFilter_AppliesFilterDeterministically()
    {
        var reference = CreateEdgeYuvFrame(width: 16, height: 8);
        var packet = CreatePaddedOrdinaryInterFramePacket(loopFilterLevel: 20);
        var firstDecoder = new RawVp9Decoder();
        SeedReferenceFrame(firstDecoder, reference, Vp9ColorRange.Studio, refreshFrameFlags: 0xff);
        var secondDecoder = new RawVp9Decoder();
        SeedReferenceFrame(secondDecoder, reference, Vp9ColorRange.Studio, refreshFrameFlags: 0xff);

        var first = firstDecoder.DecodeFrame(packet, new Vp9DecodeOptions(16, 8, Vp9OutputPixelFormat.Yuv420));
        var showExisting = firstDecoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(16, 8, Vp9OutputPixelFormat.Yuv420));
        var second = secondDecoder.DecodeFrame(packet, new Vp9DecodeOptions(16, 8, Vp9OutputPixelFormat.Yuv420));

        Assert.True(first.Succeeded, first.Diagnostic?.Message);
        Assert.True(second.Succeeded, second.Diagnostic?.Message);
        Assert.NotNull(first.Frame);
        Assert.NotEqual(Hash(reference.Pixels), Hash(first.Frame.Pixels));
        Assert.Equal(Hash(first.Frame.Pixels), Hash(second.Frame!.Pixels));
        Assert.True(showExisting.Succeeded, showExisting.Diagnostic?.Message);
        Assert.Equal(Hash(first.Frame.Pixels), Hash(showExisting.Frame!.Pixels));
    }

    [Fact]
    public void DecodeFrame_ExternalMainFrameSample_ParsesExpectedHeaderWhenPresent()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.Equal(2656, result.Header.Width);
        Assert.Equal(1352, result.Header.Height);
        Assert.Equal(30398, result.Header.PacketLength);
        Assert.Equal(320, result.Header.FirstPartitionSize);
        Assert.Equal(8, result.Header.TileInfo.TileColumns);
        Assert.NotNull(result.CompressedHeader);
        Assert.Equal(Vp9TransformMode.Select, result.CompressedHeader.TransformMode);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(result.Frame.Pixels));
        Assert.Equal([255], result.Frame.Pixels.Chunk(4).Select(pixel => pixel[3]).Distinct().ToArray());
    }

    [Fact]
    public void DecodeFrame_ExternalAlphaFrameSample_ParsesExpectedHeaderWhenPresent()
    {
        var packet = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9ColorRange.Studio, result.Header.ColorRange);
        Assert.Equal(6233, result.Header.PacketLength);
        Assert.Equal(142, result.Header.FirstPartitionSize);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("de5f6cf32681237d0076b8e106c2d8803a54379f639d9f6e7d10a864ad1ff306", Hash(result.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrame_ReadOnlyMemoryInput_DecodesExternalMainFrame()
    {
        ReadOnlyMemory<byte> packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(result.Frame.Pixels));
    }

    [Theory]
    [InlineData(
        Vp9OutputPixelFormat.Yuv420,
        5_386_368,
        "4f276400ec1d63299b4ec18d83da40482b52d9d09b1e3fd4a100537ed63798ff")]
    [InlineData(
        Vp9OutputPixelFormat.Rgba8888,
        14_363_648,
        "26d00202125b56b944707fdd55051a1365b8795b8512a9b805dea07dc05b41b4")]
    public void DecodeFrame_ExternalMainFrameSample_SupportsRequestedOutputFormats(
        Vp9OutputPixelFormat outputFormat,
        int expectedLength,
        string expectedHash)
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352, outputFormat));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(outputFormat, result.Frame.PixelFormat);
        Assert.Equal(expectedLength, result.Frame.Pixels.Length);
        Assert.Equal(expectedHash, Hash(result.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrame_WhenShowExistingFrameHasNoReference_ReturnsMissingReferenceFrame()
    {
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.True(result.Header.ShowExistingFrame);
        Assert.Equal(0, result.Header.ExistingFrameIndex);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenShowExistingFrameHasReference_ReturnsDeterministicCopy()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var keyFrame = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        var showExisting = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        Assert.True(showExisting.Succeeded, showExisting.Diagnostic?.Message);
        Assert.Null(showExisting.CompressedHeader);
        Assert.NotNull(showExisting.Header);
        Assert.True(showExisting.Header.ShowExistingFrame);
        Assert.NotNull(showExisting.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, showExisting.Frame.PixelFormat);
        Assert.Equal(2656, showExisting.Frame.Width);
        Assert.Equal(1352, showExisting.Frame.Height);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(showExisting.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrame_WhenShowExistingFrameIsReturned_CallerMutationDoesNotChangeReference()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var keyFrame = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        keyFrame.Frame!.Pixels[0] ^= 0xff;
        var firstReference = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(firstReference.Succeeded, firstReference.Diagnostic?.Message);
        firstReference.Frame!.Pixels[0] ^= 0xff;

        var secondReference = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(secondReference.Succeeded, secondReference.Diagnostic?.Message);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(secondReference.Frame!.Pixels));
    }

    [Fact]
    public void Reset_ClearsShowExistingFrameReferences()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var keyFrame = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        decoder.Reset();
        var showExisting = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        Assert.False(showExisting.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, showExisting.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenShowExistingFrameExpectedSizeDiffers_ReturnsDimensionMismatch()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var keyFrame = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));

        var showExisting = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(1, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        Assert.False(showExisting.Succeeded);
        Assert.NotNull(showExisting.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.DimensionMismatch, showExisting.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_WhenShowExistingFramePacketHasTrailingData_ReturnsInvalidPacket()
    {
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrame([0x88, 0x00], new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, result.Diagnostic?.Code);
        Assert.Contains("trailing", result.Diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeFrame_WhenNonDisplayKeyFrameDecodes_RefreshesReferencesWithoutReturningFrame()
    {
        var packet = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        packet[0] = 0x80;
        var decoder = new RawVp9Decoder();

        var nonDisplay = decoder.DecodeFrame(packet, new Vp9DecodeOptions(2656, 1352));
        var showExisting = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(nonDisplay.Succeeded, nonDisplay.Diagnostic?.Message);
        Assert.True(nonDisplay.NoDisplayFrame);
        Assert.Null(nonDisplay.Frame);
        Assert.Null(nonDisplay.Diagnostic);
        Assert.NotNull(nonDisplay.Header);
        Assert.False(nonDisplay.Header.ShowFrame);
        Assert.True(showExisting.Succeeded, showExisting.Diagnostic?.Message);
        Assert.NotNull(showExisting.Frame);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(showExisting.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_ForExternalSamples_MergesAlphaDeterministically()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Header);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("c8095ee5e4b760a8a6f7c18d10b357b9f579c6864bb1cd815061d8d6e930a2ff", Hash(result.Frame.Pixels));
        var alphaValues = result.Frame.Pixels.Chunk(4).Select(pixel => pixel[3]).ToArray();
        Assert.Equal(0, alphaValues.Min());
        Assert.Equal(168, alphaValues.Max());
        Assert.True(alphaValues.Distinct().Count() > 1);
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenColorAndAlphaReferencesExist_DecodesShowExistingSequence()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var keyFrame = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));
        var showExisting = decoder.DecodeFrameWithAlpha(
            ShowExistingFrame0Packet,
            ShowExistingFrame0Packet,
            new Vp9DecodeOptions(2656, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        Assert.True(showExisting.Succeeded, showExisting.Diagnostic?.Message);
        Assert.NotNull(showExisting.Header);
        Assert.True(showExisting.Header.ShowExistingFrame);
        Assert.NotNull(showExisting.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, showExisting.Frame.PixelFormat);
        Assert.Equal("c8095ee5e4b760a8a6f7c18d10b357b9f579c6864bb1cd815061d8d6e930a2ff", Hash(showExisting.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenColorAndAlphaAreNonDisplay_UpdatesReferencesWithoutReturningFrame()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        colorPacket[0] = 0x80;
        alphaPacket[0] = 0x80;
        var decoder = new RawVp9Decoder();

        var nonDisplay = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));
        var showExisting = decoder.DecodeFrameWithAlpha(
            ShowExistingFrame0Packet,
            ShowExistingFrame0Packet,
            new Vp9DecodeOptions(2656, 1352));

        Assert.True(nonDisplay.Succeeded, nonDisplay.Diagnostic?.Message);
        Assert.True(nonDisplay.NoDisplayFrame);
        Assert.Null(nonDisplay.Frame);
        Assert.Null(nonDisplay.Diagnostic);
        Assert.NotNull(nonDisplay.Header);
        Assert.False(nonDisplay.Header.ShowFrame);
        Assert.True(showExisting.Succeeded, showExisting.Diagnostic?.Message);
        Assert.NotNull(showExisting.Frame);
        Assert.Equal("c8095ee5e4b760a8a6f7c18d10b357b9f579c6864bb1cd815061d8d6e930a2ff", Hash(showExisting.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_AfterReset_ClearsAlphaReferenceState()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var keyFrame = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));

        decoder.Reset();
        var result = decoder.DecodeFrameWithAlpha(colorPacket, ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.True(keyFrame.Succeeded, keyFrame.Diagnostic?.Message);
        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.True(result.Header.ShowExistingFrame);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenAlphaFails_DoesNotRollBackColorReferenceState()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var failedAlpha = decoder.DecodeFrameWithAlpha(
            colorPacket,
            ShowExistingFrame0Packet,
            new Vp9DecodeOptions(2656, 1352));
        var colorShowExisting = decoder.DecodeFrame(ShowExistingFrame0Packet, new Vp9DecodeOptions(2656, 1352));

        Assert.False(failedAlpha.Succeeded);
        Assert.Equal(Vp9DecodeDiagnosticCode.MissingReferenceFrame, failedAlpha.Diagnostic?.Code);
        Assert.True(colorShowExisting.Succeeded, colorShowExisting.Diagnostic?.Message);
        Assert.NotNull(colorShowExisting.Frame);
        Assert.Equal("bd018f0c6eac5ae58945a2517c96c29a40f703b6c8c0a07c99debb9a8a864902", Hash(colorShowExisting.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_ReadOnlyMemoryInput_MergesExternalSamples()
    {
        ReadOnlyMemory<byte> colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        ReadOnlyMemory<byte> alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(colorPacket, alphaPacket, new Vp9DecodeOptions(2656, 1352));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, result.Frame.PixelFormat);
        Assert.Equal("c8095ee5e4b760a8a6f7c18d10b357b9f579c6864bb1cd815061d8d6e930a2ff", Hash(result.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenOutputFormatIsRgba_ReturnsRgbaFrame()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(
            colorPacket,
            alphaPacket,
            new Vp9DecodeOptions(2656, 1352, Vp9OutputPixelFormat.Rgba8888));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.NotNull(result.Frame);
        Assert.Equal(Vp9OutputPixelFormat.Rgba8888, result.Frame.PixelFormat);
        Assert.Equal(2656 * 1352 * 4, result.Frame.Pixels.Length);
        Assert.Equal("ac9ec4a5bcd706088dee9596536dec008854e0df4453b149e9ff55a2e2d78703", Hash(result.Frame.Pixels));
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenOutputFormatIsYuv420_ReturnsUnsupportedFeature()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(
            colorPacket,
            alphaPacket,
            new Vp9DecodeOptions(2656, 1352, Vp9OutputPixelFormat.Yuv420));

        Assert.False(result.Succeeded);
        Assert.Null(result.Header);
        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedFeature, result.Diagnostic?.Code);
        Assert.Contains("alpha composition", result.Diagnostic?.Message);
    }

    [Fact]
    public void DecodeFrameWithAlpha_WhenAlphaDimensionsDiffer_ReturnsDimensionMismatch()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var mismatchedAlphaPacket = Vp9TestPackets.CreateHiddenProfile0IntraOnlyFramePacket();
        var decoder = new RawVp9Decoder();

        var result = decoder.DecodeFrameWithAlpha(
            colorPacket,
            mismatchedAlphaPacket,
            new Vp9DecodeOptions(2656, 1352));

        Assert.False(result.Succeeded);
        Assert.Null(result.Frame);
        Assert.NotNull(result.Header);
        Assert.Equal(16, result.Header.Width);
        Assert.Equal(8, result.Header.Height);
        Assert.Equal(Vp9DecodeDiagnosticCode.DimensionMismatch, result.Diagnostic?.Code);
    }

    [Fact]
    public void DecodeFrame_ExternalSamples_CompletesWithinBoundedTime()
    {
        var colorPacket = ReadRequiredSample(MainFrameSamplePath, 30398, MainFrameSampleSha256);
        var alphaPacket = ReadRequiredSample(AlphaFrameSamplePath, 6233, AlphaFrameSampleSha256);
        var decoder = new RawVp9Decoder();
        var stopwatch = Stopwatch.StartNew();

        var colorResult = decoder.DecodeFrame(colorPacket, new Vp9DecodeOptions(2656, 1352));
        var alphaResult = decoder.DecodeFrame(alphaPacket, new Vp9DecodeOptions(2656, 1352));

        stopwatch.Stop();
        Assert.True(colorResult.Succeeded, colorResult.Diagnostic?.Message);
        Assert.True(alphaResult.Succeeded, alphaResult.Diagnostic?.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"VP9 sample parse took {stopwatch.Elapsed}.");
    }

    private static byte[] CreatePaddedMainFramePacket()
    {
        const int firstPartitionSize = 320;
        const int tileCount = 8;
        const int tileSizeFieldBytes = 4 * (tileCount - 1);
        var packet = new byte[MainFrameHeader.Length + firstPartitionSize + tileSizeFieldBytes + tileCount];
        MainFrameHeader.CopyTo(packet, 0);

        var position = MainFrameHeader.Length + firstPartitionSize;
        for (var i = 0; i < tileCount - 1; i++)
        {
            packet[position + 3] = 1;
            position += 4;
            packet[position] = 0x80;
            position++;
        }

        packet[position] = 0x80;
        return packet;
    }

    private static byte[] CreateHeaderAndCompressedHeaderOnlyPacket()
    {
        var packet = new byte[MainFrameHeader.Length + 320];
        MainFrameHeader.CopyTo(packet, 0);
        return packet;
    }

    private static byte[] CreatePaddedOrdinaryInterFramePacket(
        bool sizeFromReference = false,
        int tileInfoWidth = 16,
        int loopFilterLevel = 0,
        byte[]? tilePayload = null)
    {
        const int firstPartitionSize = 64;
        tilePayload ??= [0x01, 0x00, 0x00];
        var headerPacket = Vp9TestPackets.CreateOrdinaryInterFramePacket(
            sizeFromReference: sizeFromReference,
            stopAfterSizeReference: false,
            tileInfoWidth: tileInfoWidth,
            firstPartitionSize: firstPartitionSize,
            loopFilterLevel: loopFilterLevel);
        var packet = new byte[headerPacket.Length + firstPartitionSize + tilePayload.Length];
        headerPacket.CopyTo(packet, 0);
        tilePayload.CopyTo(packet.AsSpan(headerPacket.Length + firstPartitionSize));
        return packet;
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

    private static Vp9DecodedFrame CreateEdgeYuvFrame(int width, int height)
    {
        var buffer = Vp9YuvFrameBuffer.Create(width, height);
        FillVerticalEdge(buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length), buffer.YStride, width, height, edgeX: width / 4);
        FillVerticalEdge(buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length), buffer.UvStride, (width + 1) / 2, (height + 1) / 2, edgeX: width / 8);
        FillVerticalEdge(buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length), buffer.UvStride, (width + 1) / 2, (height + 1) / 2, edgeX: width / 8);
        return buffer.ToDecodedFrame();
    }

    private static void FillVerticalEdge(Span<byte> plane, int stride, int width, int height, int edgeX)
    {
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                plane[(row * stride) + column] = column < edgeX ? (byte)100 : (byte)104;
            }
        }
    }

    private static void SeedReferenceFrame(
        RawVp9Decoder decoder,
        Vp9DecodedFrame frame,
        Vp9ColorRange colorRange,
        int refreshFrameFlags)
    {
        var field = typeof(RawVp9Decoder).GetField("_referenceFrames", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var referenceFrames = Assert.IsType<Vp9ReferenceFrameStore>(field.GetValue(decoder));
        referenceFrames.Refresh(frame, colorRange, refreshFrameFlags);
    }

    private static byte[] ReadRequiredSample(string path, int expectedLength, string expectedSha256)
    {
        Assert.True(File.Exists(path), $"Required VP9 acceptance sample is missing: {path}");
        var packet = File.ReadAllBytes(path);
        Assert.Equal(expectedLength, packet.Length);

        var hash = Hash(packet);
        Assert.Equal(expectedSha256, hash);
        return packet;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
