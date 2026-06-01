namespace VPDecoder;

public sealed record Vp9DecodedFrame
{
    public Vp9DecodedFrame(
        int width,
        int height,
        Vp9OutputPixelFormat pixelFormat,
        byte[] pixels)
        : this(
            width,
            height,
            pixelFormat,
            pixels,
            GetDefaultPackedStride(width, pixelFormat),
            [])
    {
    }

    public Vp9DecodedFrame(
        int width,
        int height,
        Vp9OutputPixelFormat pixelFormat,
        byte[] pixels,
        int stride,
        IReadOnlyList<Vp9DecodedPlane> planes)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "VP9 decoded frame width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "VP9 decoded frame height must be positive.");
        }

        if (stride < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "VP9 decoded frame stride cannot be negative.");
        }

        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        Stride = stride;
        Planes = planes ?? throw new ArgumentNullException(nameof(planes));
        ValidatePixelPayload();
    }

    public int Width { get; }

    public int Height { get; }

    public Vp9OutputPixelFormat PixelFormat { get; }

    public byte[] Pixels { get; }

    public int Stride { get; }

    public IReadOnlyList<Vp9DecodedPlane> Planes { get; }

    public static Vp9DecodedFrame CreatePacked(
        int width,
        int height,
        Vp9OutputPixelFormat pixelFormat,
        byte[] pixels,
        int stride)
    {
        if (pixelFormat is not (Vp9OutputPixelFormat.Bgra8888 or Vp9OutputPixelFormat.Rgba8888))
        {
            throw new ArgumentException("Packed VP9 output must be BGRA8888 or RGBA8888.", nameof(pixelFormat));
        }

        return new Vp9DecodedFrame(width, height, pixelFormat, pixels, stride, []);
    }

    public static Vp9DecodedFrame CreateYuv420(
        int width,
        int height,
        byte[] pixels,
        Vp9DecodedPlane y,
        Vp9DecodedPlane u,
        Vp9DecodedPlane v)
    {
        return new Vp9DecodedFrame(
            width,
            height,
            Vp9OutputPixelFormat.Yuv420,
            pixels,
            0,
            [y, u, v]);
    }

    private void ValidatePixelPayload()
    {
        switch (PixelFormat)
        {
            case Vp9OutputPixelFormat.Bgra8888:
            case Vp9OutputPixelFormat.Rgba8888:
                var minimumPackedStride = checked(Width * 4);
                if (Stride < minimumPackedStride)
                {
                    throw new ArgumentException("Packed VP9 stride is smaller than the visible row size.");
                }

                var minimumPackedLength = checked(((long)Stride * (Height - 1)) + minimumPackedStride);
                if (Pixels.LongLength < minimumPackedLength)
                {
                    throw new ArgumentException("Packed VP9 pixel buffer is smaller than the frame dimensions require.");
                }

                if (Planes.Count != 0)
                {
                    throw new ArgumentException("Packed VP9 output cannot carry YUV plane metadata.");
                }

                break;

            case Vp9OutputPixelFormat.Yuv420:
                if (Stride != 0)
                {
                    throw new ArgumentException("YUV420 VP9 output uses per-plane strides instead of the packed stride.");
                }

                ValidateYuv420Planes();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(PixelFormat), PixelFormat, "Unsupported VP9 output pixel format.");
        }
    }

    private void ValidateYuv420Planes()
    {
        if (Planes.Count != 3)
        {
            throw new ArgumentException("YUV420 VP9 output must carry Y, U, and V planes.");
        }

        ValidatePlane(Planes[0], Vp9Plane.Y, Width, Height);
        ValidatePlane(Planes[1], Vp9Plane.U, (Width + 1) / 2, (Height + 1) / 2);
        ValidatePlane(Planes[2], Vp9Plane.V, (Width + 1) / 2, (Height + 1) / 2);
    }

    private void ValidatePlane(Vp9DecodedPlane plane, Vp9Plane expectedPlane, int expectedWidth, int expectedHeight)
    {
        if (plane.Plane != expectedPlane)
        {
            throw new ArgumentException($"Expected {expectedPlane} plane metadata but found {plane.Plane}.");
        }

        if (plane.Width != expectedWidth || plane.Height != expectedHeight)
        {
            throw new ArgumentException($"{expectedPlane} plane dimensions do not match the frame format.");
        }

        if (plane.Stride < plane.Width)
        {
            throw new ArgumentException($"{expectedPlane} plane stride is smaller than its visible row size.");
        }

        if (plane.Offset < 0 || plane.Length < 0)
        {
            throw new ArgumentException($"{expectedPlane} plane offset and length must be non-negative.");
        }

        var minimumPlaneLength = checked(((long)plane.Stride * (plane.Height - 1)) + plane.Width);
        if (plane.Length < minimumPlaneLength)
        {
            throw new ArgumentException($"{expectedPlane} plane length is smaller than its dimensions require.");
        }

        var end = checked((long)plane.Offset + plane.Length);
        if (end > Pixels.LongLength)
        {
            throw new ArgumentException($"{expectedPlane} plane extends past the VP9 pixel buffer.");
        }
    }

    private static int GetDefaultPackedStride(int width, Vp9OutputPixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            Vp9OutputPixelFormat.Bgra8888 or Vp9OutputPixelFormat.Rgba8888 => checked(width * 4),
            Vp9OutputPixelFormat.Yuv420 => 0,
            _ => 0
        };
    }
}

public sealed record Vp9DecodedPlane(
    Vp9Plane Plane,
    int Width,
    int Height,
    int Stride,
    int Offset,
    int Length);

public enum Vp9Plane
{
    Y,
    U,
    V
}
