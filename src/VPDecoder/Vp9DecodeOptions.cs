namespace VPDecoder;

public sealed record Vp9DecodeOptions(
    int? ExpectedWidth = null,
    int? ExpectedHeight = null,
    Vp9OutputPixelFormat OutputFormat = Vp9OutputPixelFormat.Bgra8888)
{
    public static Vp9DecodeOptions Default { get; } = new();
}
