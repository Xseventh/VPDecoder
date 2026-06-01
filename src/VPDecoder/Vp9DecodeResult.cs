namespace VPDecoder;

public sealed record Vp9DecodeResult(
    Vp9DecodedFrame? Frame,
    Vp9FrameHeader? Header,
    Vp9CompressedHeader? CompressedHeader,
    Vp9DecodeDiagnostic? Diagnostic)
{
    public bool Succeeded => Frame is not null && Diagnostic is null;

    public static Vp9DecodeResult Success(Vp9DecodedFrame frame, Vp9FrameHeader header)
    {
        return new Vp9DecodeResult(frame, header, null, null);
    }

    public static Vp9DecodeResult Fail(
        Vp9DecodeDiagnostic diagnostic,
        Vp9FrameHeader? header = null,
        Vp9CompressedHeader? compressedHeader = null)
    {
        return new Vp9DecodeResult(null, header, compressedHeader, diagnostic);
    }
}
