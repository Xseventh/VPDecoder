namespace VPDecoder;

/// <summary>
/// Decodes raw VP9 frame packets. The caller owns container parsing and frame timing.
/// </summary>
public sealed class RawVp9Decoder
{
    private const int Vp9FrameContextCount = 4;

    private readonly Vp9ReferenceFrameStore _referenceFrames = new();
    private readonly Vp9FrameContext[] _frameContexts = CreateDefaultFrameContexts();

    public Vp9DecodeResult DecodeFrame(ReadOnlyMemory<byte> packet, Vp9DecodeOptions? options = null)
    {
        return DecodeFrame(packet.Span, options);
    }

    public Vp9DecodeResult DecodeFrame(ReadOnlySpan<byte> packet, Vp9DecodeOptions? options = null)
    {
        options ??= Vp9DecodeOptions.Default;

        var diagnostic = ValidateOptions(options);
        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic);
        }

        if (packet.IsEmpty)
        {
            return Vp9DecodeResult.Fail(Vp9DecodeDiagnostic.InvalidPacket("VP9 packet is empty."));
        }

        if (!Vp9FrameHeaderParser.TryParse(packet, _referenceFrames.CreateFrameInfos(), out var header, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 header parser failed without a diagnostic."));
        }

        if (header is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 header parser succeeded without returning a header."));
        }

        if (header.ShowExistingFrame)
        {
            return DecodeShowExistingFrame(header, options, packet.Length);
        }

        diagnostic = ValidateInterFrameReferences(header);
        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic, header);
        }

        diagnostic = ValidateHeader(header, options);
        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic, header);
        }

        var baseFrameContext = GetBaseFrameContext(header);
        if (!Vp9CompressedHeaderParser.TryParse(packet, header, baseFrameContext, out var compressedHeader, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 compressed header parser failed without a diagnostic."),
                header);
        }

        if (compressedHeader is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 compressed header parser succeeded without returning a header."),
                header);
        }

        if (!Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 tile layout parser failed without a diagnostic."),
                header,
                compressedHeader);
        }

        if (tileBuffers.Count != header.TileInfo.TileColumns * header.TileInfo.TileRows)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 tile layout parser returned an unexpected tile count."),
                header,
                compressedHeader);
        }

        if (header.FrameType != Vp9FrameType.KeyFrame)
        {
            return DecodeOrdinaryInterFrame(packet, header, compressedHeader, tileBuffers, options);
        }

        if (!Vp9KeyFrameDecodeState.TryCreate(header, compressedHeader, tileBuffers, out var state, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 key-frame decode state creation failed without a diagnostic."),
                header,
                compressedHeader);
        }

        if (state is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 key-frame decode state creation succeeded without returning a state."),
                header,
                compressedHeader);
        }

        if (!Vp9TileSyntaxScanner.TryReconstructFullFrameWithSyntax(packet.ToArray(), state, out var reconstructedFrame, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 full-frame reconstruction failed without a diagnostic."),
                header,
                compressedHeader);
        }

        if (reconstructedFrame is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 full-frame reconstruction succeeded without returning a frame."),
                header,
                compressedHeader);
        }

        if (!Vp9LoopFilter.TryApply(header, reconstructedFrame, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 loop filter failed without a diagnostic."),
                header,
                compressedHeader);
        }

        RefreshFrameContext(header, compressedHeader.FrameContext);
        var yuvFrame = reconstructedFrame.Frame;
        _referenceFrames.Refresh(yuvFrame, header.ColorRange, header.RefreshFrameFlags);

        var outputFrame = options.OutputFormat == Vp9OutputPixelFormat.Yuv420
            ? yuvFrame
            : Vp9ColorConverter.ConvertYuv420ToPacked(yuvFrame, header.ColorRange, options.OutputFormat);
        return Vp9DecodeResult.Success(outputFrame, header, compressedHeader);
    }

    public Vp9DecodeResult DecodeFrameWithAlpha(
        ReadOnlyMemory<byte> colorPacket,
        ReadOnlyMemory<byte> alphaPacket,
        Vp9DecodeOptions? options = null)
    {
        return DecodeFrameWithAlpha(colorPacket.Span, alphaPacket.Span, options);
    }

    public Vp9DecodeResult DecodeFrameWithAlpha(
        ReadOnlySpan<byte> colorPacket,
        ReadOnlySpan<byte> alphaPacket,
        Vp9DecodeOptions? options = null)
    {
        options ??= Vp9DecodeOptions.Default;

        var diagnostic = ValidateOptions(options);
        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic);
        }

        if (options.OutputFormat == Vp9OutputPixelFormat.Yuv420)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.UnsupportedFeature(
                    "VP9 alpha composition requires BGRA8888 or RGBA8888 packed output."));
        }

        var packedOptions = options with { OutputFormat = Vp9OutputPixelFormat.Bgra8888 };
        var colorResult = DecodeFrame(colorPacket, packedOptions);
        if (!colorResult.Succeeded)
        {
            return colorResult;
        }

        var alphaOptions = options with
        {
            ExpectedWidth = colorResult.Frame!.Width,
            ExpectedHeight = colorResult.Frame.Height,
            OutputFormat = Vp9OutputPixelFormat.Bgra8888
        };
        var alphaDecoder = new RawVp9Decoder();
        var alphaResult = alphaDecoder.DecodeFrame(alphaPacket, alphaOptions);
        if (!alphaResult.Succeeded)
        {
            return alphaResult;
        }

        var merged = MergeAlpha(colorResult, alphaResult);
        if (!merged.Succeeded || options.OutputFormat == Vp9OutputPixelFormat.Bgra8888)
        {
            return merged;
        }

        return Vp9DecodeResult.Success(
            Vp9AlphaComposer.ConvertBgraToRgba(merged.Frame!),
            merged.Header!,
            merged.CompressedHeader);
    }

    public void Reset()
    {
        _referenceFrames.Reset();
        ResetFrameContexts();
    }

    private Vp9DecodeResult DecodeShowExistingFrame(Vp9FrameHeader header, Vp9DecodeOptions options, int packetLength)
    {
        if (header.Profile != 0)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.UnsupportedProfile(
                    $"VP9 profile {header.Profile} is not supported by this decoder slice."),
                header);
        }

        if (header.HeaderSizeInBytes != packetLength)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InvalidPacket("VP9 show-existing-frame packet contains trailing data."),
                header);
        }

        if (header.ExistingFrameIndex is not { } index || index < 0 || index >= _referenceFrames.Count)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InvalidPacket("VP9 show-existing-frame packet references an invalid slot."),
                header);
        }

        if (!_referenceFrames.TryGet(index, out var referenceFrame) || referenceFrame?.Frame is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.MissingReferenceFrame($"VP9 reference frame slot {index} is empty."),
                header);
        }

        var diagnostic = ValidateReferenceFrame(referenceFrame.Frame, options);
        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic, header);
        }

        var resultHeader = header with
        {
            Width = referenceFrame.Frame.Width,
            Height = referenceFrame.Frame.Height,
            RenderWidth = referenceFrame.Frame.Width,
            RenderHeight = referenceFrame.Frame.Height,
            ColorRange = referenceFrame.ColorRange
        };

        return Vp9DecodeResult.Success(ConvertReferenceFrame(referenceFrame, options.OutputFormat), resultHeader);
    }

    private static Vp9DecodeResult MergeAlpha(Vp9DecodeResult colorResult, Vp9DecodeResult alphaResult)
    {
        if (colorResult.Frame is null || alphaResult.Frame is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 alpha merge requires decoded color and alpha frames."),
                colorResult.Header,
                colorResult.CompressedHeader);
        }

        if (colorResult.Frame.Width != alphaResult.Frame.Width || colorResult.Frame.Height != alphaResult.Frame.Height)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.DimensionMismatch("VP9 alpha frame dimensions do not match the color frame."),
                colorResult.Header,
                colorResult.CompressedHeader);
        }

        if (colorResult.Frame.PixelFormat != Vp9OutputPixelFormat.Bgra8888 ||
            alphaResult.Frame.PixelFormat != Vp9OutputPixelFormat.Bgra8888)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.UnsupportedFeature("VP9 alpha merge currently requires BGRA8888 color and alpha frames."),
                colorResult.Header,
                colorResult.CompressedHeader);
        }

        return Vp9DecodeResult.Success(
            Vp9AlphaComposer.MergeBgraWithBgraAlpha(colorResult.Frame, alphaResult.Frame),
            colorResult.Header!,
            colorResult.CompressedHeader);
    }

    private Vp9DecodeResult DecodeOrdinaryInterFrame(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9DecodeOptions options)
    {
        if (header.IntraOnly)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 intra-only inter-frame pixel decode is not supported yet."),
                header,
                compressedHeader);
        }

        if (!Vp9TileSyntaxScanner.TryReconstructFullInterFrameZeroMvWithResidualMetadata(
                packet.ToArray(),
                header,
                compressedHeader,
                tileBuffers,
                _referenceFrames,
                out var reconstructedFrame,
                out _,
                out var diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 ordinary inter-frame reconstruction failed without a diagnostic."),
                header,
                compressedHeader);
        }

        if (reconstructedFrame is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 ordinary inter-frame reconstruction succeeded without returning metadata."),
                header,
                compressedHeader);
        }

        if (!Vp9LoopFilter.TryApply(header, reconstructedFrame, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 inter-frame loop filter failed without a diagnostic."),
                header,
                compressedHeader);
        }

        RefreshFrameContext(header, compressedHeader.FrameContext);
        var yuvFrame = reconstructedFrame.Frame;
        _referenceFrames.Refresh(yuvFrame, header.ColorRange, header.RefreshFrameFlags);

        var outputFrame = options.OutputFormat == Vp9OutputPixelFormat.Yuv420
            ? yuvFrame
            : Vp9ColorConverter.ConvertYuv420ToPacked(yuvFrame, header.ColorRange, options.OutputFormat);
        return Vp9DecodeResult.Success(outputFrame, header, compressedHeader);
    }

    private static Vp9FrameContext[] CreateDefaultFrameContexts()
    {
        var frameContexts = new Vp9FrameContext[Vp9FrameContextCount];
        for (var i = 0; i < frameContexts.Length; i++)
        {
            frameContexts[i] = Vp9FrameContext.CreateDefault();
        }

        return frameContexts;
    }

    private void ResetFrameContexts()
    {
        for (var i = 0; i < _frameContexts.Length; i++)
        {
            _frameContexts[i] = Vp9FrameContext.CreateDefault();
        }
    }

    private Vp9FrameContext GetBaseFrameContext(Vp9FrameHeader header)
    {
        if (header.FrameType == Vp9FrameType.KeyFrame || header.IntraOnly || header.ErrorResilientMode)
        {
            return Vp9FrameContext.CreateDefault();
        }

        return _frameContexts[header.FrameContextIndex];
    }

    private void RefreshFrameContext(Vp9FrameHeader header, Vp9FrameContext frameContext)
    {
        if (header.RefreshFrameContext)
        {
            _frameContexts[header.FrameContextIndex] = frameContext.Clone();
        }
    }

    private static Vp9DecodedFrame ConvertReferenceFrame(Vp9ReferenceFrame referenceFrame, Vp9OutputPixelFormat outputFormat)
    {
        return outputFormat == Vp9OutputPixelFormat.Yuv420
            ? Vp9ReferenceFrameStore.CloneFrame(referenceFrame.Frame)
            : Vp9ColorConverter.ConvertYuv420ToPacked(referenceFrame.Frame, referenceFrame.ColorRange, outputFormat);
    }

    private static Vp9DecodeDiagnostic? ValidateOptions(Vp9DecodeOptions options)
    {
        if (options.ExpectedWidth is <= 0)
        {
            return Vp9DecodeDiagnostic.InvalidDecodeOptions("Expected VP9 width must be positive when provided.");
        }

        if (options.ExpectedHeight is <= 0)
        {
            return Vp9DecodeDiagnostic.InvalidDecodeOptions("Expected VP9 height must be positive when provided.");
        }

        if (!Enum.IsDefined(typeof(Vp9OutputPixelFormat), options.OutputFormat))
        {
            return Vp9DecodeDiagnostic.InvalidDecodeOptions($"Unsupported VP9 output pixel format value {options.OutputFormat}.");
        }

        if (options.MaxWidth <= 0)
        {
            return Vp9DecodeDiagnostic.InvalidDecodeOptions("Maximum VP9 width must be positive.");
        }

        if (options.MaxHeight <= 0)
        {
            return Vp9DecodeDiagnostic.InvalidDecodeOptions("Maximum VP9 height must be positive.");
        }

        if (options.MaxPixelCount <= 0)
        {
            return Vp9DecodeDiagnostic.InvalidDecodeOptions("Maximum VP9 pixel count must be positive.");
        }

        return null;
    }

    private static Vp9DecodeDiagnostic? ValidateReferenceFrame(Vp9DecodedFrame frame, Vp9DecodeOptions options)
    {
        if (options.ExpectedWidth is { } expectedWidth && frame.Width != expectedWidth)
        {
            return Vp9DecodeDiagnostic.DimensionMismatch(
                $"Decoded VP9 reference width {frame.Width} does not match expected width {expectedWidth}.");
        }

        if (options.ExpectedHeight is { } expectedHeight && frame.Height != expectedHeight)
        {
            return Vp9DecodeDiagnostic.DimensionMismatch(
                $"Decoded VP9 reference height {frame.Height} does not match expected height {expectedHeight}.");
        }

        if (frame.Width > options.MaxWidth)
        {
            return Vp9DecodeDiagnostic.AllocationLimitExceeded(
                $"VP9 reference width {frame.Width} exceeds configured maximum width {options.MaxWidth}.");
        }

        if (frame.Height > options.MaxHeight)
        {
            return Vp9DecodeDiagnostic.AllocationLimitExceeded(
                $"VP9 reference height {frame.Height} exceeds configured maximum height {options.MaxHeight}.");
        }

        var pixelCount = checked((long)frame.Width * frame.Height);
        if (pixelCount > options.MaxPixelCount)
        {
            return Vp9DecodeDiagnostic.AllocationLimitExceeded(
                $"VP9 reference frame has {pixelCount} pixels, exceeding configured maximum {options.MaxPixelCount}.");
        }

        return null;
    }

    private Vp9DecodeDiagnostic? ValidateInterFrameReferences(Vp9FrameHeader header)
    {
        return _referenceFrames.ValidateInterFrameReferences(header);
    }

    private static Vp9DecodeDiagnostic? ValidateHeader(Vp9FrameHeader header, Vp9DecodeOptions options)
    {
        if (options.ExpectedWidth is { } expectedWidth && header.Width != expectedWidth)
        {
            return Vp9DecodeDiagnostic.DimensionMismatch(
                $"Decoded VP9 width {header.Width} does not match expected width {expectedWidth}.");
        }

        if (options.ExpectedHeight is { } expectedHeight && header.Height != expectedHeight)
        {
            return Vp9DecodeDiagnostic.DimensionMismatch(
                $"Decoded VP9 height {header.Height} does not match expected height {expectedHeight}.");
        }

        if (header.Profile != 0)
        {
            return Vp9DecodeDiagnostic.UnsupportedProfile(
                $"VP9 profile {header.Profile} is not supported by this decoder slice.");
        }

        if (header.BitDepth != 8)
        {
            return Vp9DecodeDiagnostic.UnsupportedBitDepth(
                $"VP9 bit depth {header.BitDepth} is not supported by this decoder slice.");
        }

        if (header.SubsamplingX != 1 || header.SubsamplingY != 1)
        {
            return Vp9DecodeDiagnostic.UnsupportedChromaSubsampling(
                $"VP9 chroma subsampling {header.SubsamplingX}:{header.SubsamplingY} is not supported by this decoder slice.");
        }

        if (header.ShowExistingFrame)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 show-existing-frame packets require decoder reference state and are not supported yet.");
        }

        if (!header.ShowFrame)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 non-display frames require decoder reference state and are not supported yet.");
        }

        if (header.Segmentation.Enabled)
        {
            return Vp9DecodeDiagnostic.UnsupportedFeature(
                "VP9 segmentation is parsed but not supported by the pixel decoder yet.");
        }

        if (header.Quantization.Lossless)
        {
            return Vp9DecodeDiagnostic.UnsupportedFeature(
                "VP9 lossless transform mode is not supported by this decoder slice.");
        }

        if (header.TileInfo.TileRows != 1)
        {
            return Vp9DecodeDiagnostic.UnsupportedFeature(
                $"VP9 tile rows ({header.TileInfo.TileRows}) are not supported by this decoder slice.");
        }

        if (header.Width > options.MaxWidth)
        {
            return Vp9DecodeDiagnostic.AllocationLimitExceeded(
                $"VP9 width {header.Width} exceeds configured maximum width {options.MaxWidth}.");
        }

        if (header.Height > options.MaxHeight)
        {
            return Vp9DecodeDiagnostic.AllocationLimitExceeded(
                $"VP9 height {header.Height} exceeds configured maximum height {options.MaxHeight}.");
        }

        var pixelCount = checked((long)header.Width * header.Height);
        if (pixelCount > options.MaxPixelCount)
        {
            return Vp9DecodeDiagnostic.AllocationLimitExceeded(
                $"VP9 frame has {pixelCount} pixels, exceeding configured maximum {options.MaxPixelCount}.");
        }

        if (header.FirstPartitionSize <= 0)
        {
            return Vp9DecodeDiagnostic.InvalidPacket("VP9 first partition size must be positive.");
        }

        if (header.HeaderSizeInBytes + header.FirstPartitionSize > header.PacketLength)
        {
            return Vp9DecodeDiagnostic.TruncatedPacket(
                "VP9 compressed header extends past the packet boundary.");
        }

        return null;
    }
}
