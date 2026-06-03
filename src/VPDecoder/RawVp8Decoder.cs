namespace VPDecoder;

/// <summary>
/// Reserved raw VP8 packet decoder surface. VP8 bitstream support is intentionally gated until VP9 frame decoding is complete.
/// </summary>
public sealed class RawVp8Decoder
{
    public Vp8DecodeResult DecodeFrame(ReadOnlyMemory<byte> packet, Vp8DecodeOptions? options = null)
    {
        return DecodeFrame(packet.Span, options);
    }

    public Vp8DecodeResult DecodeFrame(ReadOnlySpan<byte> packet, Vp8DecodeOptions? options = null)
    {
        options ??= Vp8DecodeOptions.Default;
        var diagnostic = ValidateOptions(options);
        if (diagnostic is not null)
        {
            return Vp8DecodeResult.Fail(diagnostic);
        }

        if (!Vp8FrameHeaderParser.TryParse(packet, out var header, out diagnostic))
        {
            return Vp8DecodeResult.Fail(
                diagnostic ?? Vp8DecodeDiagnostic.InternalDecodeFailure("VP8 header parser failed without a diagnostic."));
        }

        if (header is null)
        {
            return Vp8DecodeResult.Fail(Vp8DecodeDiagnostic.InternalDecodeFailure("VP8 header parser succeeded without returning a header."));
        }

        diagnostic = ValidateHeader(header, options);
        if (diagnostic is not null)
        {
            return Vp8DecodeResult.Fail(diagnostic, header);
        }

        if (header.FrameType == Vp8FrameType.InterFrame)
        {
            return Vp8DecodeResult.Fail(
                Vp8DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP8 inter frames require decoder reference state and are not supported yet."),
                header);
        }

        diagnostic = ValidateKeyFrameSyntax(packet, header);
        if (diagnostic is not null)
        {
            return Vp8DecodeResult.Fail(diagnostic, header);
        }

        return Vp8DecodeResult.Fail(
            Vp8DecodeDiagnostic.UnsupportedFeature("VP8 key-frame pixel reconstruction is not implemented yet."),
            header);
    }

    public void Reset()
    {
    }

    private static Vp8DecodeDiagnostic? ValidateOptions(Vp8DecodeOptions options)
    {
        if (options.ExpectedWidth is <= 0)
        {
            return Vp8DecodeDiagnostic.InvalidDecodeOptions("Expected VP8 width must be positive when provided.");
        }

        if (options.ExpectedHeight is <= 0)
        {
            return Vp8DecodeDiagnostic.InvalidDecodeOptions("Expected VP8 height must be positive when provided.");
        }

        if (!Enum.IsDefined(typeof(Vp9OutputPixelFormat), options.OutputFormat))
        {
            return Vp8DecodeDiagnostic.InvalidDecodeOptions($"Unsupported VP8 output pixel format value {options.OutputFormat}.");
        }

        if (options.MaxWidth <= 0)
        {
            return Vp8DecodeDiagnostic.InvalidDecodeOptions("Maximum VP8 width must be positive.");
        }

        if (options.MaxHeight <= 0)
        {
            return Vp8DecodeDiagnostic.InvalidDecodeOptions("Maximum VP8 height must be positive.");
        }

        if (options.MaxPixelCount <= 0)
        {
            return Vp8DecodeDiagnostic.InvalidDecodeOptions("Maximum VP8 pixel count must be positive.");
        }

        return null;
    }

    private static Vp8DecodeDiagnostic? ValidateHeader(Vp8FrameHeader header, Vp8DecodeOptions options)
    {
        if (header.HeaderSizeInBytes + header.FirstPartitionSize > header.PacketLength)
        {
            return Vp8DecodeDiagnostic.TruncatedPacket("VP8 first partition extends past the packet boundary.");
        }

        if (header.FrameType != Vp8FrameType.KeyFrame)
        {
            return null;
        }

        if (options.ExpectedWidth is { } expectedWidth && header.Width != expectedWidth)
        {
            return Vp8DecodeDiagnostic.DimensionMismatch(
                $"Decoded VP8 width {header.Width} does not match expected width {expectedWidth}.");
        }

        if (options.ExpectedHeight is { } expectedHeight && header.Height != expectedHeight)
        {
            return Vp8DecodeDiagnostic.DimensionMismatch(
                $"Decoded VP8 height {header.Height} does not match expected height {expectedHeight}.");
        }

        if (header.Width > options.MaxWidth)
        {
            return Vp8DecodeDiagnostic.AllocationLimitExceeded(
                $"VP8 width {header.Width} exceeds configured maximum width {options.MaxWidth}.");
        }

        if (header.Height > options.MaxHeight)
        {
            return Vp8DecodeDiagnostic.AllocationLimitExceeded(
                $"VP8 height {header.Height} exceeds configured maximum height {options.MaxHeight}.");
        }

        var pixelCount = checked((long)header.Width * header.Height);
        if (pixelCount > options.MaxPixelCount)
        {
            return Vp8DecodeDiagnostic.AllocationLimitExceeded(
                $"VP8 frame has {pixelCount} pixels, exceeding configured maximum {options.MaxPixelCount}.");
        }

        return null;
    }

    private static Vp8DecodeDiagnostic? ValidateKeyFrameSyntax(ReadOnlySpan<byte> packet, Vp8FrameHeader header)
    {
        return Vp8KeyFrameSyntaxHeaderParser.TryParseFrameSyntax(
            packet.Slice(header.HeaderSizeInBytes, header.FirstPartitionSize),
            header.Width,
            header.Height,
            out _,
            out var diagnostic)
            ? null
            : diagnostic ?? Vp8DecodeDiagnostic.InternalDecodeFailure("VP8 key-frame syntax parser failed without a diagnostic.");
    }
}

public sealed record Vp8DecodeOptions(
    int? ExpectedWidth = null,
    int? ExpectedHeight = null,
    Vp9OutputPixelFormat OutputFormat = Vp9OutputPixelFormat.Bgra8888,
    int MaxWidth = 16_384,
    int MaxHeight = 16_384,
    long MaxPixelCount = 268_435_456)
{
    public static Vp8DecodeOptions Default { get; } = new();
}

public sealed record Vp8DecodeResult(
    Vp9DecodedFrame? Frame,
    Vp8FrameHeader? Header,
    Vp8DecodeDiagnostic? Diagnostic)
{
    public Vp8DecodeResultStatus Status => Diagnostic is not null
        ? Vp8DecodeResultStatus.Failed
        : Frame is null
            ? Vp8DecodeResultStatus.NoDisplayFrame
            : Vp8DecodeResultStatus.DecodedFrame;

    public bool Succeeded => Status != Vp8DecodeResultStatus.Failed;

    public bool HasDisplayFrame => Status == Vp8DecodeResultStatus.DecodedFrame;

    public bool NoDisplayFrame => Status == Vp8DecodeResultStatus.NoDisplayFrame;

    public static Vp8DecodeResult Success(Vp9DecodedFrame frame, Vp8FrameHeader header)
    {
        return new Vp8DecodeResult(frame, header, null);
    }

    public static Vp8DecodeResult NoDisplay(Vp8FrameHeader header)
    {
        return new Vp8DecodeResult(null, header, null);
    }

    public static Vp8DecodeResult Fail(Vp8DecodeDiagnostic diagnostic, Vp8FrameHeader? header = null)
    {
        return new Vp8DecodeResult(null, header, diagnostic);
    }
}

public enum Vp8DecodeResultStatus
{
    Failed,
    DecodedFrame,
    NoDisplayFrame
}

public sealed record Vp8DecodeDiagnostic(
    Vp8DecodeDiagnosticCode Code,
    string Message)
{
    public static Vp8DecodeDiagnostic InvalidDecodeOptions(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.InvalidDecodeOptions, message);
    }

    public static Vp8DecodeDiagnostic InvalidPacket(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.InvalidPacket, message);
    }

    public static Vp8DecodeDiagnostic UnsupportedFeature(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.UnsupportedFeature, message);
    }

    public static Vp8DecodeDiagnostic UnsupportedInterFrameFeature(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.UnsupportedInterFrameFeature, message);
    }

    public static Vp8DecodeDiagnostic TruncatedPacket(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.TruncatedPacket, message);
    }

    public static Vp8DecodeDiagnostic DimensionMismatch(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.DimensionMismatch, message);
    }

    public static Vp8DecodeDiagnostic AllocationLimitExceeded(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.AllocationLimitExceeded, message);
    }

    public static Vp8DecodeDiagnostic InternalDecodeFailure(string message)
    {
        return new Vp8DecodeDiagnostic(Vp8DecodeDiagnosticCode.InternalDecodeFailure, message);
    }
}

public enum Vp8DecodeDiagnosticCode
{
    InvalidDecodeOptions,
    InvalidPacket,
    UnsupportedFeature,
    UnsupportedInterFrameFeature,
    TruncatedPacket,
    DimensionMismatch,
    AllocationLimitExceeded,
    InternalDecodeFailure
}
