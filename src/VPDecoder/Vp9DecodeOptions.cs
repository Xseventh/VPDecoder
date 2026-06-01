namespace VPDecoder;

public sealed record Vp9DecodeOptions(
    int? ExpectedWidth = null,
    int? ExpectedHeight = null,
    Vp9OutputPixelFormat OutputFormat = Vp9OutputPixelFormat.Bgra8888,
    int MaxWidth = 16_384,
    int MaxHeight = 16_384,
    long MaxPixelCount = 268_435_456)
{
    public static Vp9DecodeOptions Default { get; } = new();
}
