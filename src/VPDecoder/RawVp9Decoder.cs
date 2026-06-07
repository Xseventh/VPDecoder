namespace VPDecoder;

/// <summary>
/// Decodes raw VP9 frame packets. The caller owns container parsing and frame timing.
/// </summary>
public sealed class RawVp9Decoder
{
    private const int Vp9FrameContextCount = 4;

    private readonly Vp9ReferenceFrameStore _referenceFrames = new();
    private readonly Vp9FrameContext[] _frameContexts = CreateDefaultFrameContexts();
    private readonly int[] _loopFilterRefDeltas = (int[])Vp9FrameHeaderParser.DefaultLoopFilterRefDeltas.Clone();
    private readonly int[] _loopFilterModeDeltas = (int[])Vp9FrameHeaderParser.DefaultLoopFilterModeDeltas.Clone();
    private Vp9PreviousFrameMotionVectors? _previousFrameMotionVectors;
    private RawVp9Decoder? _alphaDecoder;

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

        if (TryParseSuperframeIndex(packet, out var superframeIndex, out diagnostic))
        {
            return DecodeSuperframe(packet, superframeIndex, options);
        }

        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic);
        }

        return DecodeSingleFrame(packet, options);
    }

    private Vp9DecodeResult DecodeSingleFrame(ReadOnlySpan<byte> packet, Vp9DecodeOptions options)
    {
        Vp9DecodeDiagnostic? diagnostic;
        if (!Vp9FrameHeaderParser.TryParse(
                packet,
                _referenceFrames.CreateFrameInfos(),
                _loopFilterRefDeltas,
                _loopFilterModeDeltas,
                out var header,
                out diagnostic))
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

        RefreshLoopFilterDeltaState(header);
        RefreshFrameContext(header, compressedHeader.FrameContext);
        var yuvFrame = reconstructedFrame.Frame;
        _referenceFrames.Refresh(yuvFrame, header.ColorSpace, header.ColorRange, header.RefreshFrameFlags);
        _previousFrameMotionVectors = null;
        if (!header.ShowFrame)
        {
            return Vp9DecodeResult.NoDisplay(header, compressedHeader);
        }

        var outputFrame = options.OutputFormat == Vp9OutputPixelFormat.Yuv420
            ? yuvFrame
            : Vp9ColorConverter.ConvertYuv420ToPacked(
                yuvFrame,
                header.ColorSpace,
                header.ColorRange,
                options.OutputFormat);
        return Vp9DecodeResult.Success(outputFrame, header, compressedHeader);
    }

    private Vp9DecodeResult DecodeSuperframe(
        ReadOnlySpan<byte> packet,
        Vp9SuperframeIndex superframeIndex,
        Vp9DecodeOptions options)
    {
        var offset = 0;
        Vp9DecodeResult? lastResult = null;
        Vp9DecodeResult? lastVisibleResult = null;
        foreach (var frameSize in superframeIndex.FrameSizes)
        {
            var result = DecodeSingleFrame(packet.Slice(offset, frameSize), options);
            if (!result.Succeeded)
            {
                return result;
            }

            lastResult = result;
            if (!result.NoDisplayFrame)
            {
                lastVisibleResult = result;
            }

            offset += frameSize;
        }

        return lastVisibleResult ?? lastResult ?? Vp9DecodeResult.Fail(
            Vp9DecodeDiagnostic.InvalidPacket("VP9 superframe index does not contain any frames."));
    }

    private static bool TryParseSuperframeIndex(
        ReadOnlySpan<byte> packet,
        out Vp9SuperframeIndex superframeIndex,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        superframeIndex = default;
        diagnostic = null;
        var marker = packet[^1];
        if ((marker & 0xe0) != 0xc0)
        {
            return false;
        }

        var frameCount = (marker & 0x07) + 1;
        var magnitude = ((marker >> 3) & 0x03) + 1;
        var indexLength = 2 + (frameCount * magnitude);
        if (indexLength > packet.Length)
        {
            return false;
        }

        var indexOffset = packet.Length - indexLength;
        if (packet[indexOffset] != marker)
        {
            return false;
        }

        var frameSizes = new int[frameCount];
        var sizeOffset = indexOffset + 1;
        var payloadLength = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameSize = 0;
            for (var byteIndex = 0; byteIndex < magnitude; byteIndex++)
            {
                frameSize |= packet[sizeOffset++] << (byteIndex * 8);
            }

            if (frameSize <= 0)
            {
                diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 superframe index contains an empty frame.");
                return false;
            }

            payloadLength = checked(payloadLength + frameSize);
            frameSizes[frame] = frameSize;
        }

        if (payloadLength != indexOffset)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 superframe index sizes do not match the packet payload length.");
            return false;
        }

        superframeIndex = new Vp9SuperframeIndex(frameSizes);
        return true;
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
            ExpectedWidth = colorResult.Frame?.Width ?? colorResult.Header?.Width,
            ExpectedHeight = colorResult.Frame?.Height ?? colorResult.Header?.Height,
            OutputFormat = Vp9OutputPixelFormat.Bgra8888
        };
        var alphaDecoder = _alphaDecoder ??= new RawVp9Decoder();
        var alphaResult = alphaDecoder.DecodeFrame(alphaPacket, alphaOptions);
        if (!alphaResult.Succeeded)
        {
            return alphaResult;
        }

        if (colorResult.NoDisplayFrame || alphaResult.NoDisplayFrame)
        {
            return Vp9DecodeResult.NoDisplay(
                colorResult.Header ?? alphaResult.Header!,
                colorResult.CompressedHeader);
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
        _previousFrameMotionVectors = null;
        ResetFrameContexts();
        ResetLoopFilterDeltaState();
        _alphaDecoder?.Reset();
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

        if (options.OutputFormat != Vp9OutputPixelFormat.Yuv420 &&
            !Vp9ColorConverter.IsSupportedColorSpace(referenceFrame.ColorSpace))
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.UnsupportedFeature(
                    $"VP9 color space {referenceFrame.ColorSpace} is not supported by packed output conversion."),
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
            ColorSpace = referenceFrame.ColorSpace,
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
            Vp9AlphaComposer.MergeBgraWithBgraAlphaInPlace(colorResult.Frame, alphaResult.Frame),
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

        if (!Vp9TileSyntaxScanner.TryReconstructFullInterFrameDirectWithResidualMetadata(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                _referenceFrames,
                out var yuvFrame,
                out var modeBlocks,
                out var diagnostic,
                _previousFrameMotionVectors))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 ordinary inter-frame reconstruction failed without a diagnostic."),
                header,
                compressedHeader);
        }

        if (yuvFrame is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 ordinary inter-frame reconstruction succeeded without returning a frame."),
                header,
                compressedHeader);
        }

        if (!Vp9LoopFilter.TryApplyInterFrame(header, yuvFrame, modeBlocks, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 inter-frame loop filter failed without a diagnostic."),
                header,
                compressedHeader);
        }

        RefreshLoopFilterDeltaState(header);
        RefreshFrameContext(header, compressedHeader.FrameContext);
        _referenceFrames.Refresh(yuvFrame, header.ColorSpace, header.ColorRange, header.RefreshFrameFlags);
        _previousFrameMotionVectors = header.ShowFrame
            ? Vp9PreviousFrameMotionVectors.FromModeBlocks(
                header.Width,
                header.Height,
                header.TileInfo.MiRows,
                header.TileInfo.MiColumns,
                modeBlocks)
            : null;
        if (!header.ShowFrame)
        {
            return Vp9DecodeResult.NoDisplay(header, compressedHeader);
        }

        var outputFrame = options.OutputFormat == Vp9OutputPixelFormat.Yuv420
            ? yuvFrame
            : Vp9ColorConverter.ConvertYuv420ToPacked(
                yuvFrame,
                header.ColorSpace,
                header.ColorRange,
                options.OutputFormat);
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

    private void ResetLoopFilterDeltaState()
    {
        Array.Copy(Vp9FrameHeaderParser.DefaultLoopFilterRefDeltas, _loopFilterRefDeltas, _loopFilterRefDeltas.Length);
        Array.Copy(Vp9FrameHeaderParser.DefaultLoopFilterModeDeltas, _loopFilterModeDeltas, _loopFilterModeDeltas.Length);
    }

    private void RefreshLoopFilterDeltaState(Vp9FrameHeader header)
    {
        var loopFilter = header.LoopFilter;
        if (header.FrameType == Vp9FrameType.KeyFrame || header.IntraOnly || header.ErrorResilientMode)
        {
            ResetLoopFilterDeltaState();
        }

        if (!loopFilter.ModeRefDeltaEnabled)
        {
            return;
        }

        if (loopFilter.RefDeltas.Count == _loopFilterRefDeltas.Length)
        {
            for (var i = 0; i < _loopFilterRefDeltas.Length; i++)
            {
                _loopFilterRefDeltas[i] = loopFilter.RefDeltas[i];
            }
        }

        if (loopFilter.ModeDeltas.Count == _loopFilterModeDeltas.Length)
        {
            for (var i = 0; i < _loopFilterModeDeltas.Length; i++)
            {
                _loopFilterModeDeltas[i] = loopFilter.ModeDeltas[i];
            }
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
        if (header.FrameType == Vp9FrameType.KeyFrame || header.IntraOnly || header.ErrorResilientMode)
        {
            ResetFrameContexts();
            if (header.RefreshFrameContext)
            {
                _frameContexts[0] = frameContext.Clone();
            }

            return;
        }

        if (header.RefreshFrameContext)
        {
            _frameContexts[header.FrameContextIndex] = frameContext.Clone();
        }
    }

    private static Vp9DecodedFrame ConvertReferenceFrame(Vp9ReferenceFrame referenceFrame, Vp9OutputPixelFormat outputFormat)
    {
        return outputFormat == Vp9OutputPixelFormat.Yuv420
            ? Vp9ReferenceFrameStore.CloneFrame(referenceFrame.Frame)
            : Vp9ColorConverter.ConvertYuv420ToPacked(
                referenceFrame.Frame,
                referenceFrame.ColorSpace,
                referenceFrame.ColorRange,
                outputFormat);
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

        if (options.OutputFormat != Vp9OutputPixelFormat.Yuv420 &&
            !Vp9ColorConverter.IsSupportedColorSpace(header.ColorSpace))
        {
            return Vp9DecodeDiagnostic.UnsupportedFeature(
                $"VP9 color space {header.ColorSpace} is not supported by packed output conversion.");
        }

        if (header.ShowExistingFrame)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 show-existing-frame packets require decoder reference state and are not supported yet.");
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

    private readonly record struct Vp9SuperframeIndex(IReadOnlyList<int> FrameSizes);
}
