namespace VPDecoder;

public static class Vp9ColorConverter
{
    public static Vp9DecodedFrame ConvertYuv420ToPacked(
        Vp9DecodedFrame yuvFrame,
        Vp9ColorRange colorRange,
        Vp9OutputPixelFormat outputFormat)
    {
        return ConvertYuv420ToPacked(yuvFrame, Vp9ColorSpace.Bt601, colorRange, outputFormat);
    }

    public static Vp9DecodedFrame ConvertYuv420ToPacked(
        Vp9DecodedFrame yuvFrame,
        Vp9ColorSpace colorSpace,
        Vp9ColorRange colorRange,
        Vp9OutputPixelFormat outputFormat)
    {
        if (yuvFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            throw new ArgumentException("VP9 color conversion requires a YUV420 source frame.", nameof(yuvFrame));
        }

        if (outputFormat is not (Vp9OutputPixelFormat.Bgra8888 or Vp9OutputPixelFormat.Rgba8888))
        {
            throw new ArgumentException("VP9 color conversion output must be BGRA8888 or RGBA8888.", nameof(outputFormat));
        }

        var yPlane = yuvFrame.Planes.Single(plane => plane.Plane == Vp9Plane.Y);
        var uPlane = yuvFrame.Planes.Single(plane => plane.Plane == Vp9Plane.U);
        var vPlane = yuvFrame.Planes.Single(plane => plane.Plane == Vp9Plane.V);
        var stride = checked(yuvFrame.Width * 4);
        var packed = new byte[checked(stride * yuvFrame.Height)];

        for (var y = 0; y < yuvFrame.Height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var uvRow = y / 2;
            var uRow = uPlane.Offset + (uvRow * uPlane.Stride);
            var vRow = vPlane.Offset + (uvRow * vPlane.Stride);
            var outRow = y * stride;
            for (var x = 0; x < yuvFrame.Width; x++)
            {
                var ySample = yuvFrame.Pixels[yRow + x];
                var uSample = yuvFrame.Pixels[uRow + (x / 2)];
                var vSample = yuvFrame.Pixels[vRow + (x / 2)];
                var (r, g, b) = ConvertSample(ySample, uSample, vSample, colorSpace, colorRange);
                var offset = outRow + (x * 4);
                if (outputFormat == Vp9OutputPixelFormat.Bgra8888)
                {
                    packed[offset] = b;
                    packed[offset + 1] = g;
                    packed[offset + 2] = r;
                }
                else
                {
                    packed[offset] = r;
                    packed[offset + 1] = g;
                    packed[offset + 2] = b;
                }

                packed[offset + 3] = 255;
            }
        }

        return Vp9DecodedFrame.CreatePacked(
            yuvFrame.Width,
            yuvFrame.Height,
            outputFormat,
            packed,
            stride);
    }

    public static bool IsSupportedColorSpace(Vp9ColorSpace colorSpace)
    {
        return colorSpace is
            Vp9ColorSpace.Unknown or
            Vp9ColorSpace.Bt601 or
            Vp9ColorSpace.Bt709 or
            Vp9ColorSpace.Smpte170;
    }

    private static (byte R, byte G, byte B) ConvertSample(
        int y,
        int u,
        int v,
        Vp9ColorSpace colorSpace,
        Vp9ColorRange colorRange)
    {
        var matrix = GetMatrix(colorSpace);
        var c = colorRange == Vp9ColorRange.Studio
            ? Math.Max(0, y - 16) * 298
            : y * 256;
        var d = u - 128;
        var e = v - 128;

        var r = colorRange == Vp9ColorRange.Studio
            ? (c + (matrix.LimitedCrToR * e) + 128) >> 8
            : y + ((matrix.FullCrToR * e) >> 8);
        var g = colorRange == Vp9ColorRange.Studio
            ? (c - (matrix.LimitedCbToG * d) - (matrix.LimitedCrToG * e) + 128) >> 8
            : y - ((matrix.FullCbToG * d + matrix.FullCrToG * e) >> 8);
        var b = colorRange == Vp9ColorRange.Studio
            ? (c + (matrix.LimitedCbToB * d) + 128) >> 8
            : y + ((matrix.FullCbToB * d) >> 8);

        return ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }

    private static Vp9YuvToRgbMatrix GetMatrix(Vp9ColorSpace colorSpace)
    {
        return colorSpace switch
        {
            Vp9ColorSpace.Bt709 => new Vp9YuvToRgbMatrix(
                LimitedCrToR: 459,
                LimitedCbToG: 55,
                LimitedCrToG: 136,
                LimitedCbToB: 541,
                FullCrToR: 403,
                FullCbToG: 48,
                FullCrToG: 120,
                FullCbToB: 475),
            Vp9ColorSpace.Unknown or
                Vp9ColorSpace.Bt601 or
                Vp9ColorSpace.Smpte170 => new Vp9YuvToRgbMatrix(
                    LimitedCrToR: 409,
                    LimitedCbToG: 100,
                    LimitedCrToG: 208,
                    LimitedCbToB: 516,
                    FullCrToR: 359,
                    FullCbToG: 88,
                    FullCrToG: 183,
                    FullCbToB: 454),
            _ => throw new ArgumentOutOfRangeException(
                nameof(colorSpace),
                colorSpace,
                "VP9 color conversion does not support this color space.")
        };
    }

    private readonly record struct Vp9YuvToRgbMatrix(
        int LimitedCrToR,
        int LimitedCbToG,
        int LimitedCrToG,
        int LimitedCbToB,
        int FullCrToR,
        int FullCbToG,
        int FullCrToG,
        int FullCbToB);
}
