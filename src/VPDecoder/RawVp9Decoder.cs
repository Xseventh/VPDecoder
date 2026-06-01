namespace VPDecoder;

/// <summary>
/// Decodes raw VP9 frame packets. The caller owns container parsing and frame timing.
/// </summary>
public sealed class RawVp9Decoder
{
    public Vp9DecodeResult DecodeFrame(ReadOnlySpan<byte> packet, Vp9DecodeOptions? options = null)
    {
        options ??= Vp9DecodeOptions.Default;

        if (packet.IsEmpty)
        {
            return Vp9DecodeResult.Fail(Vp9DecodeDiagnostic.InvalidPacket("VP9 packet is empty."));
        }

        if (!Vp9FrameHeaderParser.TryParse(packet, out var header, out var diagnostic))
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

        return Vp9DecodeResult.Fail(
            Vp9DecodeDiagnostic.UnsupportedFeature(
                "VP9 pixel reconstruction is not implemented yet. Header parsing and feature gating succeeded."),
            header);
    }

    public void Reset()
    {
        // Future inter-frame support will clear reference frames and frame contexts here.
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
