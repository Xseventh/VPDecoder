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
        if (isStudioRange)
        {
            if (outputFormat == Vp9OutputPixelFormat.Bgra8888)
            {
                ConvertStudioRangeBgra(sourcePixels, yPlane, uPlane, vPlane, width, height, stride, matrix, packed);
            }
            else
            {
                ConvertStudioRangeRgba(sourcePixels, yPlane, uPlane, vPlane, width, height, stride, matrix, packed);
            }
        }
        else if (outputFormat == Vp9OutputPixelFormat.Bgra8888)
        {
            ConvertFullRangeBgra(sourcePixels, yPlane, uPlane, vPlane, width, height, stride, matrix, packed);
        }
        else
        {
            ConvertFullRangeRgba(sourcePixels, yPlane, uPlane, vPlane, width, height, stride, matrix, packed);
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

    internal static Vp9DecodedFrame MergeYuv420RedChannelAsBgraAlphaInPlace(
        Vp9DecodedFrame colorFrame,
        Vp9DecodedFrame alphaFrame,
        Vp9ColorSpace alphaColorSpace,
        Vp9ColorRange alphaColorRange)
    {
        if (colorFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888 ||
            alphaFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            throw new ArgumentException("VP9 alpha composition requires a BGRA8888 color frame and a YUV420 alpha frame.");
        }

        if (colorFrame.Width != alphaFrame.Width || colorFrame.Height != alphaFrame.Height)
        {
            throw new ArgumentException("VP9 alpha frame dimensions must match the color frame.");
        }

        var yPlane = alphaFrame.Planes[0];
        var vPlane = alphaFrame.Planes[2];
        var alphaPixels = alphaFrame.Pixels;
        var colorPixels = colorFrame.Pixels;
        var matrix = GetMatrix(alphaColorSpace);
        if (alphaColorRange == Vp9ColorRange.Studio)
        {
            MergeStudioRangeRedChannelAsBgraAlpha(
                alphaPixels,
                yPlane,
                vPlane,
                colorFrame.Width,
                colorFrame.Height,
                colorFrame.Stride,
                matrix,
                colorPixels);
        }
        else
        {
            MergeFullRangeRedChannelAsBgraAlpha(
                alphaPixels,
                yPlane,
                vPlane,
                colorFrame.Width,
                colorFrame.Height,
                colorFrame.Stride,
                matrix,
                colorPixels);
        }

        return colorFrame;
    }

    private static void ConvertStudioRangeBgra(
        byte[] sourcePixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane uPlane,
        Vp9DecodedPlane vPlane,
        int width,
        int height,
        int stride,
        Vp9YuvToRgbMatrix matrix,
        byte[] packed)
    {
        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var uvRow = y >> 1;
            var uRow = uPlane.Offset + (uvRow * uPlane.Stride);
            var vRow = vPlane.Offset + (uvRow * vPlane.Stride);
            var outRow = y * stride;
            var evenWidth = width & ~1;
            for (var x = 0; x < evenWidth; x += 2)
            {
                var uvColumn = x >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = matrix.LimitedCrToR * e;
                var chromaG = (matrix.LimitedCbToG * d) + (matrix.LimitedCrToG * e);
                var chromaB = matrix.LimitedCbToB * d;
                var outputOffset = outRow + (x * 4);
                WriteStudioBgraSample(sourcePixels[yRow + x], chromaR, chromaG, chromaB, packed, outputOffset);
                WriteStudioBgraSample(sourcePixels[yRow + x + 1], chromaR, chromaG, chromaB, packed, outputOffset + 4);
            }

            if (evenWidth < width)
            {
                var uvColumn = evenWidth >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = matrix.LimitedCrToR * e;
                var chromaG = (matrix.LimitedCbToG * d) + (matrix.LimitedCrToG * e);
                var chromaB = matrix.LimitedCbToB * d;
                WriteStudioBgraSample(sourcePixels[yRow + evenWidth], chromaR, chromaG, chromaB, packed, outRow + (evenWidth * 4));
            }
        }
    }

    private static void ConvertStudioRangeRgba(
        byte[] sourcePixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane uPlane,
        Vp9DecodedPlane vPlane,
        int width,
        int height,
        int stride,
        Vp9YuvToRgbMatrix matrix,
        byte[] packed)
    {
        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var uvRow = y >> 1;
            var uRow = uPlane.Offset + (uvRow * uPlane.Stride);
            var vRow = vPlane.Offset + (uvRow * vPlane.Stride);
            var outRow = y * stride;
            var evenWidth = width & ~1;
            for (var x = 0; x < evenWidth; x += 2)
            {
                var uvColumn = x >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = matrix.LimitedCrToR * e;
                var chromaG = (matrix.LimitedCbToG * d) + (matrix.LimitedCrToG * e);
                var chromaB = matrix.LimitedCbToB * d;
                var outputOffset = outRow + (x * 4);
                WriteStudioRgbaSample(sourcePixels[yRow + x], chromaR, chromaG, chromaB, packed, outputOffset);
                WriteStudioRgbaSample(sourcePixels[yRow + x + 1], chromaR, chromaG, chromaB, packed, outputOffset + 4);
            }

            if (evenWidth < width)
            {
                var uvColumn = evenWidth >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = matrix.LimitedCrToR * e;
                var chromaG = (matrix.LimitedCbToG * d) + (matrix.LimitedCrToG * e);
                var chromaB = matrix.LimitedCbToB * d;
                WriteStudioRgbaSample(sourcePixels[yRow + evenWidth], chromaR, chromaG, chromaB, packed, outRow + (evenWidth * 4));
            }
        }
    }

    private static void ConvertFullRangeBgra(
        byte[] sourcePixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane uPlane,
        Vp9DecodedPlane vPlane,
        int width,
        int height,
        int stride,
        Vp9YuvToRgbMatrix matrix,
        byte[] packed)
    {
        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var uvRow = y >> 1;
            var uRow = uPlane.Offset + (uvRow * uPlane.Stride);
            var vRow = vPlane.Offset + (uvRow * vPlane.Stride);
            var outRow = y * stride;
            var evenWidth = width & ~1;
            for (var x = 0; x < evenWidth; x += 2)
            {
                var uvColumn = x >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = (matrix.FullCrToR * e) >> 8;
                var chromaG = (matrix.FullCbToG * d + matrix.FullCrToG * e) >> 8;
                var chromaB = (matrix.FullCbToB * d) >> 8;
                var outputOffset = outRow + (x * 4);
                WriteFullBgraSample(sourcePixels[yRow + x], chromaR, chromaG, chromaB, packed, outputOffset);
                WriteFullBgraSample(sourcePixels[yRow + x + 1], chromaR, chromaG, chromaB, packed, outputOffset + 4);
            }

            if (evenWidth < width)
            {
                var uvColumn = evenWidth >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = (matrix.FullCrToR * e) >> 8;
                var chromaG = (matrix.FullCbToG * d + matrix.FullCrToG * e) >> 8;
                var chromaB = (matrix.FullCbToB * d) >> 8;
                WriteFullBgraSample(sourcePixels[yRow + evenWidth], chromaR, chromaG, chromaB, packed, outRow + (evenWidth * 4));
            }
        }
    }

    private static void ConvertFullRangeRgba(
        byte[] sourcePixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane uPlane,
        Vp9DecodedPlane vPlane,
        int width,
        int height,
        int stride,
        Vp9YuvToRgbMatrix matrix,
        byte[] packed)
    {
        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var uvRow = y >> 1;
            var uRow = uPlane.Offset + (uvRow * uPlane.Stride);
            var vRow = vPlane.Offset + (uvRow * vPlane.Stride);
            var outRow = y * stride;
            var evenWidth = width & ~1;
            for (var x = 0; x < evenWidth; x += 2)
            {
                var uvColumn = x >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = (matrix.FullCrToR * e) >> 8;
                var chromaG = (matrix.FullCbToG * d + matrix.FullCrToG * e) >> 8;
                var chromaB = (matrix.FullCbToB * d) >> 8;
                var outputOffset = outRow + (x * 4);
                WriteFullRgbaSample(sourcePixels[yRow + x], chromaR, chromaG, chromaB, packed, outputOffset);
                WriteFullRgbaSample(sourcePixels[yRow + x + 1], chromaR, chromaG, chromaB, packed, outputOffset + 4);
            }

            if (evenWidth < width)
            {
                var uvColumn = evenWidth >> 1;
                var d = sourcePixels[uRow + uvColumn] - 128;
                var e = sourcePixels[vRow + uvColumn] - 128;
                var chromaR = (matrix.FullCrToR * e) >> 8;
                var chromaG = (matrix.FullCbToG * d + matrix.FullCrToG * e) >> 8;
                var chromaB = (matrix.FullCbToB * d) >> 8;
                WriteFullRgbaSample(sourcePixels[yRow + evenWidth], chromaR, chromaG, chromaB, packed, outRow + (evenWidth * 4));
            }
        }
    }

    private static void MergeStudioRangeRedChannelAsBgraAlpha(
        byte[] alphaPixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane vPlane,
        int width,
        int height,
        int stride,
        Vp9YuvToRgbMatrix matrix,
        byte[] colorPixels)
    {
        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var vRow = vPlane.Offset + ((y >> 1) * vPlane.Stride);
            var outRow = y * stride;
            var evenWidth = width & ~1;
            for (var x = 0; x < evenWidth; x += 2)
            {
                var chromaR = matrix.LimitedCrToR * (alphaPixels[vRow + (x >> 1)] - 128);
                var outputOffset = outRow + (x * 4) + 3;
                colorPixels[outputOffset] = ConvertStudioRedSample(alphaPixels[yRow + x], chromaR);
                colorPixels[outputOffset + 4] = ConvertStudioRedSample(alphaPixels[yRow + x + 1], chromaR);
            }

            if (evenWidth < width)
            {
                var chromaR = matrix.LimitedCrToR * (alphaPixels[vRow + (evenWidth >> 1)] - 128);
                colorPixels[outRow + (evenWidth * 4) + 3] = ConvertStudioRedSample(alphaPixels[yRow + evenWidth], chromaR);
            }
        }
    }

    private static void MergeFullRangeRedChannelAsBgraAlpha(
        byte[] alphaPixels,
        Vp9DecodedPlane yPlane,
        Vp9DecodedPlane vPlane,
        int width,
        int height,
        int stride,
        Vp9YuvToRgbMatrix matrix,
        byte[] colorPixels)
    {
        for (var y = 0; y < height; y++)
        {
            var yRow = yPlane.Offset + (y * yPlane.Stride);
            var vRow = vPlane.Offset + ((y >> 1) * vPlane.Stride);
            var outRow = y * stride;
            var evenWidth = width & ~1;
            for (var x = 0; x < evenWidth; x += 2)
            {
                var chromaR = (matrix.FullCrToR * (alphaPixels[vRow + (x >> 1)] - 128)) >> 8;
                var outputOffset = outRow + (x * 4) + 3;
                colorPixels[outputOffset] = ConvertFullRedSample(alphaPixels[yRow + x], chromaR);
                colorPixels[outputOffset + 4] = ConvertFullRedSample(alphaPixels[yRow + x + 1], chromaR);
            }

            if (evenWidth < width)
            {
                var chromaR = (matrix.FullCrToR * (alphaPixels[vRow + (evenWidth >> 1)] - 128)) >> 8;
                colorPixels[outRow + (evenWidth * 4) + 3] = ConvertFullRedSample(alphaPixels[yRow + evenWidth], chromaR);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStudioBgraSample(
        int y,
        int chromaR,
        int chromaG,
        int chromaB,
        byte[] packed,
        int offset)
    {
        var c = y <= 16 ? 0 : (y - 16) * 298;
        packed[offset] = ClipPixel((c + chromaB + 128) >> 8);
        packed[offset + 1] = ClipPixel((c - chromaG + 128) >> 8);
        packed[offset + 2] = ClipPixel((c + chromaR + 128) >> 8);
        packed[offset + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ConvertStudioRedSample(int y, int chromaR)
    {
        var c = y <= 16 ? 0 : (y - 16) * 298;
        return ClipPixel((c + chromaR + 128) >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ConvertFullRedSample(int y, int chromaR)
    {
        return ClipPixel(y + chromaR);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStudioRgbaSample(
        int y,
        int chromaR,
        int chromaG,
        int chromaB,
        byte[] packed,
        int offset)
    {
        var c = y <= 16 ? 0 : (y - 16) * 298;
        packed[offset] = ClipPixel((c + chromaR + 128) >> 8);
        packed[offset + 1] = ClipPixel((c - chromaG + 128) >> 8);
        packed[offset + 2] = ClipPixel((c + chromaB + 128) >> 8);
        packed[offset + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFullBgraSample(
        int y,
        int chromaR,
        int chromaG,
        int chromaB,
        byte[] packed,
        int offset)
    {
        packed[offset] = ClipPixel(y + chromaB);
        packed[offset + 1] = ClipPixel(y - chromaG);
        packed[offset + 2] = ClipPixel(y + chromaR);
        packed[offset + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFullRgbaSample(
        int y,
        int chromaR,
        int chromaG,
        int chromaB,
        byte[] packed,
        int offset)
    {
        packed[offset] = ClipPixel(y + chromaR);
        packed[offset + 1] = ClipPixel(y - chromaG);
        packed[offset + 2] = ClipPixel(y + chromaB);
        packed[offset + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClipPixel(int value)
    {
        if ((uint)value <= byte.MaxValue)
        {
            return (byte)value;
        }

        return value < byte.MinValue ? byte.MinValue : byte.MaxValue;
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
