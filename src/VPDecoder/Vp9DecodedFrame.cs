namespace VPDecoder;

public sealed record Vp9DecodedFrame(
    int Width,
    int Height,
    Vp9OutputPixelFormat PixelFormat,
    byte[] Pixels);
