namespace VPDecoder;

/// <summary>
/// Reserved raw VP8 packet decoder surface. VP8 bitstream support is intentionally gated until VP9 frame decoding is complete.
/// </summary>
public sealed class RawVp8Decoder
{
    public Vp8DecodeResult DecodeFrame(ReadOnlySpan<byte> packet, Vp8DecodeOptions? options = null)
    {
        options ??= Vp8DecodeOptions.Default;
        if (options.ExpectedWidth is <= 0)
        {
            return Vp8DecodeResult.Fail(Vp8DecodeDiagnostic.InvalidDecodeOptions("Expected VP8 width must be positive when provided."));
        }

        if (options.ExpectedHeight is <= 0)
        {
            return Vp8DecodeResult.Fail(Vp8DecodeDiagnostic.InvalidDecodeOptions("Expected VP8 height must be positive when provided."));
        }

        if (!Enum.IsDefined(typeof(Vp9OutputPixelFormat), options.OutputFormat))
        {
            return Vp8DecodeResult.Fail(Vp8DecodeDiagnostic.InvalidDecodeOptions($"Unsupported VP8 output pixel format value {options.OutputFormat}."));
        }

        if (packet.IsEmpty)
        {
            return Vp8DecodeResult.Fail(Vp8DecodeDiagnostic.InvalidPacket("VP8 packet is empty."));
        }

        return Vp8DecodeResult.Fail(Vp8DecodeDiagnostic.UnsupportedFeature("VP8 raw frame decoding is not implemented yet."));
    }

    public void Reset()
    {
    }
}

public sealed record Vp8DecodeOptions(
    int? ExpectedWidth = null,
    int? ExpectedHeight = null,
    Vp9OutputPixelFormat OutputFormat = Vp9OutputPixelFormat.Bgra8888)
{
    public static Vp8DecodeOptions Default { get; } = new();
}

public sealed record Vp8DecodeResult(
    Vp9DecodedFrame? Frame,
    Vp8DecodeDiagnostic? Diagnostic)
{
    public bool Succeeded => Frame is not null && Diagnostic is null;

    public static Vp8DecodeResult Fail(Vp8DecodeDiagnostic diagnostic)
    {
        return new Vp8DecodeResult(null, diagnostic);
    }
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
}

public enum Vp8DecodeDiagnosticCode
{
    InvalidDecodeOptions,
    InvalidPacket,
    UnsupportedFeature
}
