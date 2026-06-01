namespace VPDecoder;

/// <summary>
/// Decodes raw VP9 frame packets. The caller owns container parsing and frame timing.
/// </summary>
public sealed class RawVp9Decoder
{
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

        if (!Vp9FrameHeaderParser.TryParse(packet, out var header, out diagnostic))
        {
            return Vp9DecodeResult.Fail(
                diagnostic ?? Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 header parser failed without a diagnostic."));
        }

        if (header is null)
        {
            return Vp9DecodeResult.Fail(
                Vp9DecodeDiagnostic.InternalDecodeFailure("VP9 header parser succeeded without returning a header."));
        }

        diagnostic = ValidateHeader(header, options);
        if (diagnostic is not null)
        {
            return Vp9DecodeResult.Fail(diagnostic, header);
        }

        if (!Vp9CompressedHeaderParser.TryParse(packet, header, out var compressedHeader, out diagnostic))
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

        return Vp9DecodeResult.Fail(
            Vp9DecodeDiagnostic.UnsupportedFeature(
                "VP9 pixel reconstruction is not implemented yet. Header parsing and feature gating succeeded."),
            header,
            compressedHeader);
    }

    public Vp9DecodeResult DecodeFrameWithAlpha(
        ReadOnlySpan<byte> colorPacket,
        ReadOnlySpan<byte> alphaPacket,
        Vp9DecodeOptions? options = null)
    {
        options ??= Vp9DecodeOptions.Default;

        var colorResult = DecodeFrame(colorPacket, options);
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

        return MergeAlpha(colorResult, alphaResult);
    }

    public void Reset()
    {
        // Future inter-frame support will clear reference frames and frame contexts here.
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

        var merged = (byte[])colorResult.Frame.Pixels.Clone();
        var colorStride = colorResult.Frame.Stride;
        var alphaStride = alphaResult.Frame.Stride;
        for (var y = 0; y < colorResult.Frame.Height; y++)
        {
            var colorRow = y * colorStride;
            var alphaRow = y * alphaStride;
            for (var x = 0; x < colorResult.Frame.Width; x++)
            {
                merged[colorRow + (x * 4) + 3] = alphaResult.Frame.Pixels[alphaRow + (x * 4) + 2];
            }
        }

        return Vp9DecodeResult.Success(
            Vp9DecodedFrame.CreatePacked(
                colorResult.Frame.Width,
                colorResult.Frame.Height,
                Vp9OutputPixelFormat.Bgra8888,
                merged,
                colorStride),
            colorResult.Header!,
            colorResult.CompressedHeader);
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

        if (header.FrameType != Vp9FrameType.KeyFrame)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 inter frames require decoder reference state and are not supported yet.");
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
