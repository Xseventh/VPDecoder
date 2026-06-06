namespace VPDecoder;

using System.Runtime.CompilerServices;

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

        var yPlane = yuvFrame.Planes[0];
        var uPlane = yuvFrame.Planes[1];
        var vPlane = yuvFrame.Planes[2];
        var width = yuvFrame.Width;
        var height = yuvFrame.Height;
        var sourcePixels = yuvFrame.Pixels;
        var stride = checked(width * 4);
        var packed = new byte[checked(stride * height)];
        var matrix = GetMatrix(colorSpace);
        var isStudioRange = colorRange == Vp9ColorRange.Studio;
        var isBgra = outputFormat == Vp9OutputPixelFormat.Bgra8888;

        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var uvRow = y / 2;
            var uRow = uPlane.Offset + (uvRow * uPlane.Stride);
            var vRow = vPlane.Offset + (uvRow * vPlane.Stride);
            var outRow = y * stride;
            for (var x = 0; x < width; x += 2)
            {
                var uvColumn = x >> 1;
                var uSample = sourcePixels[uRow + uvColumn];
                var vSample = sourcePixels[vRow + uvColumn];
                WritePackedSample(
                    sourcePixels[yRow + x],
                    uSample,
                    vSample,
                    matrix,
                    isStudioRange,
                    packed,
                    outRow + (x * 4),
                    isBgra);

                if (x + 1 < width)
                {
                    WritePackedSample(
                        sourcePixels[yRow + x + 1],
                        uSample,
                        vSample,
                        matrix,
                        isStudioRange,
                        packed,
                        outRow + ((x + 1) * 4),
                        isBgra);
                }
            }
        }

        return Vp9DecodedFrame.CreatePacked(
            width,
            height,
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePackedSample(
        int y,
        int u,
        int v,
        Vp9YuvToRgbMatrix matrix,
        bool isStudioRange,
        byte[] packed,
        int offset,
        bool isBgra)
    {
        var c = isStudioRange
            ? (y <= 16 ? 0 : (y - 16) * 298)
            : y * 256;
        var d = u - 128;
        var e = v - 128;

        var r = isStudioRange
            ? (c + (matrix.LimitedCrToR * e) + 128) >> 8
            : y + ((matrix.FullCrToR * e) >> 8);
        var g = isStudioRange
            ? (c - (matrix.LimitedCbToG * d) - (matrix.LimitedCrToG * e) + 128) >> 8
            : y - ((matrix.FullCbToG * d + matrix.FullCrToG * e) >> 8);
        var b = isStudioRange
            ? (c + (matrix.LimitedCbToB * d) + 128) >> 8
            : y + ((matrix.FullCbToB * d) >> 8);

        if (isBgra)
        {
            packed[offset] = ClipPixel(b);
            packed[offset + 1] = ClipPixel(g);
            packed[offset + 2] = ClipPixel(r);
        }
        else
        {
            packed[offset] = ClipPixel(r);
            packed[offset + 1] = ClipPixel(g);
            packed[offset + 2] = ClipPixel(b);
        }

        packed[offset + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClipPixel(int value)
    {
        return value <= 0
            ? (byte)0
            : value >= 255
                ? (byte)255
                : (byte)value;
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
