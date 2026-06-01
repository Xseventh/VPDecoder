namespace VPDecoder;

public sealed class Vp9YuvFrameBuffer
{
    private Vp9YuvFrameBuffer(
        int width,
        int height,
        int yStride,
        int uvStride,
        byte[] pixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane uPlane,
        Vp9DecodedPlane vPlane)
    {
        Width = width;
        Height = height;
        YStride = yStride;
        UvStride = uvStride;
        Pixels = pixels;
        YPlane = yPlane;
        UPlane = uPlane;
        VPlane = vPlane;
    }

    public int Width { get; }

    public int Height { get; }

    public int YStride { get; }

    public int UvStride { get; }

    public byte[] Pixels { get; }

    public Vp9DecodedPlane YPlane { get; }

    public Vp9DecodedPlane UPlane { get; }

    public Vp9DecodedPlane VPlane { get; }

    public static Vp9YuvFrameBuffer Create(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "VP9 YUV frame width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "VP9 YUV frame height must be positive.");
        }

        var yStride = width;
        var yHeight = height;
        var uvWidth = (width + 1) / 2;
        var uvHeight = (height + 1) / 2;
        var uvStride = uvWidth;

        var yLength = checked(yStride * yHeight);
        var uvLength = checked(uvStride * uvHeight);
        var uOffset = yLength;
        var vOffset = checked(uOffset + uvLength);
        var pixels = new byte[checked(vOffset + uvLength)];

        return new Vp9YuvFrameBuffer(
            width,
            height,
            yStride,
            uvStride,
            pixels,
            new Vp9DecodedPlane(Vp9Plane.Y, width, height, yStride, 0, yLength),
            new Vp9DecodedPlane(Vp9Plane.U, uvWidth, uvHeight, uvStride, uOffset, uvLength),
            new Vp9DecodedPlane(Vp9Plane.V, uvWidth, uvHeight, uvStride, vOffset, uvLength));
    }

    public Vp9DecodedFrame ToDecodedFrame()
    {
        return Vp9DecodedFrame.CreateYuv420(Width, Height, Pixels, YPlane, UPlane, VPlane);
    }
}
