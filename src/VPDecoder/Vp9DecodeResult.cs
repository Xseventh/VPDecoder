namespace VPDecoder;

public sealed record Vp9DecodeResult(
    Vp9DecodedFrame? Frame,
    Vp9FrameHeader? Header,
    Vp9CompressedHeader? CompressedHeader,
    Vp9DecodeDiagnostic? Diagnostic)
{
    public Vp9DecodeResultStatus Status => Diagnostic is not null
        ? Vp9DecodeResultStatus.Failed
        : Frame is null
            ? Vp9DecodeResultStatus.NoDisplayFrame
            : Vp9DecodeResultStatus.DecodedFrame;

    public bool Succeeded => Status != Vp9DecodeResultStatus.Failed;

    public bool HasDisplayFrame => Status == Vp9DecodeResultStatus.DecodedFrame;

    public bool NoDisplayFrame => Status == Vp9DecodeResultStatus.NoDisplayFrame;

    public static Vp9DecodeResult Success(
        Vp9DecodedFrame frame,
        Vp9FrameHeader header,
        Vp9CompressedHeader? compressedHeader = null)
    {
        return new Vp9DecodeResult(frame, header, compressedHeader, null);
    }

    public static Vp9DecodeResult NoDisplay(
        Vp9FrameHeader header,
        Vp9CompressedHeader? compressedHeader = null)
    {
        return new Vp9DecodeResult(null, header, compressedHeader, null);
    }

    public static Vp9DecodeResult Fail(
        Vp9DecodeDiagnostic diagnostic,
        Vp9FrameHeader? header = null,
        Vp9CompressedHeader? compressedHeader = null)
    {
        return new Vp9DecodeResult(null, header, compressedHeader, diagnostic);
    }
}

public enum Vp9DecodeResultStatus
{
    Failed,
    DecodedFrame,
    NoDisplayFrame
}
