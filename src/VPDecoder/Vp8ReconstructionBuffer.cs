namespace VPDecoder;

internal sealed class Vp8ReconstructionBuffer
{
    private readonly Vp9YuvFrameBuffer _buffer;

    private Vp8ReconstructionBuffer(Vp9YuvFrameBuffer buffer)
    {
        _buffer = buffer;
    }

    public int Width => _buffer.Width;

    public int Height => _buffer.Height;

    public Vp9DecodedPlane YPlane => _buffer.YPlane;

    public Vp9DecodedPlane UPlane => _buffer.UPlane;

    public Vp9DecodedPlane VPlane => _buffer.VPlane;

    public byte[] Pixels => _buffer.Pixels;

    public static Vp8ReconstructionBuffer Create(int width, int height)
    {
        return new Vp8ReconstructionBuffer(Vp9YuvFrameBuffer.Create(width, height));
    }

    public void FillNeutralChroma()
    {
        Pixels.AsSpan(UPlane.Offset, UPlane.Length).Fill(128);
        Pixels.AsSpan(VPlane.Offset, VPlane.Length).Fill(128);
    }

    public Vp9DecodedFrame ToDecodedFrame(Vp8DecodeOptions options)
    {
        var yuvFrame = _buffer.ToDecodedFrame();
        return options.OutputFormat == Vp9OutputPixelFormat.Yuv420
            ? yuvFrame
            : Vp9ColorConverter.ConvertYuv420ToPacked(
                yuvFrame,
                Vp9ColorRange.Studio,
                options.OutputFormat);
    }
}
