namespace VPDecoder;

public sealed record Vp9DecodeDiagnostic(
    Vp9DecodeDiagnosticCode Code,
    string Message)
{
    public static Vp9DecodeDiagnostic InvalidPacket(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.InvalidPacket, message);
    }

    public static Vp9DecodeDiagnostic UnsupportedProfile(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.UnsupportedProfile, message);
    }

    public static Vp9DecodeDiagnostic UnsupportedBitDepth(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.UnsupportedBitDepth, message);
    }

    public static Vp9DecodeDiagnostic UnsupportedChromaSubsampling(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.UnsupportedChromaSubsampling, message);
    }

    public static Vp9DecodeDiagnostic UnsupportedInterFrameFeature(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, message);
    }

    public static Vp9DecodeDiagnostic UnsupportedFeature(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.UnsupportedFeature, message);
    }

    public static Vp9DecodeDiagnostic TruncatedPacket(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.TruncatedPacket, message);
    }

    public static Vp9DecodeDiagnostic DimensionMismatch(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.DimensionMismatch, message);
    }

    public static Vp9DecodeDiagnostic InternalDecodeFailure(string message)
    {
        return new Vp9DecodeDiagnostic(Vp9DecodeDiagnosticCode.InternalDecodeFailure, message);
    }
}
