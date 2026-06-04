namespace VPDecoder;

internal static class Vp9MotionCompensator
{
    private const int FilterBits = 7;
    private const int SubpelBits = 4;
    private const int SubpelMask = (1 << SubpelBits) - 1;
    private const int SubpelTaps = 8;
    private const int SubpelTapOffset = (SubpelTaps / 2) - 1;
    private const int MotionVectorQ4LowerBound = -(1 << 15);
    private const int MotionVectorQ4UpperBound = (1 << 15) - 1;

    private static ReadOnlySpan<short> BilinearFilters =>
    [
        0, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 120, 8, 0, 0, 0,
        0, 0, 0, 112, 16, 0, 0, 0, 0, 0, 0, 104, 24, 0, 0, 0,
        0, 0, 0, 96, 32, 0, 0, 0, 0, 0, 0, 88, 40, 0, 0, 0,
        0, 0, 0, 80, 48, 0, 0, 0, 0, 0, 0, 72, 56, 0, 0, 0,
        0, 0, 0, 64, 64, 0, 0, 0, 0, 0, 0, 56, 72, 0, 0, 0,
        0, 0, 0, 48, 80, 0, 0, 0, 0, 0, 0, 40, 88, 0, 0, 0,
        0, 0, 0, 32, 96, 0, 0, 0, 0, 0, 0, 24, 104, 0, 0, 0,
        0, 0, 0, 16, 112, 0, 0, 0, 0, 0, 0, 8, 120, 0, 0, 0
    ];

    private static ReadOnlySpan<short> EightTapFilters =>
    [
        0, 0, 0, 128, 0, 0, 0, 0, 0, 1, -5, 126, 8, -3, 1, 0,
        -1, 3, -10, 122, 18, -6, 2, 0, -1, 4, -13, 118, 27, -9, 3, -1,
        -1, 4, -16, 112, 37, -11, 4, -1, -1, 5, -18, 105, 48, -14, 4, -1,
        -1, 5, -19, 97, 58, -16, 5, -1, -1, 6, -19, 88, 68, -18, 5, -1,
        -1, 6, -19, 78, 78, -19, 6, -1, -1, 5, -18, 68, 88, -19, 6, -1,
        -1, 5, -16, 58, 97, -19, 5, -1, -1, 4, -14, 48, 105, -18, 5, -1,
        -1, 4, -11, 37, 112, -16, 4, -1, -1, 3, -9, 27, 118, -13, 4, -1,
        0, 2, -6, 18, 122, -10, 3, -1, 0, 1, -3, 8, 126, -5, 1, 0
    ];

    private static ReadOnlySpan<short> EightTapSharpFilters =>
    [
        0, 0, 0, 128, 0, 0, 0, 0, -1, 3, -7, 127, 8, -3, 1, 0,
        -2, 5, -13, 125, 17, -6, 3, -1, -3, 7, -17, 121, 27, -10, 5, -2,
        -4, 9, -20, 115, 37, -13, 6, -2, -4, 10, -23, 108, 48, -16, 8, -3,
        -4, 10, -24, 100, 59, -19, 9, -3, -4, 11, -24, 90, 70, -21, 10, -4,
        -4, 11, -23, 80, 80, -23, 11, -4, -4, 10, -21, 70, 90, -24, 11, -4,
        -3, 9, -19, 59, 100, -24, 10, -4, -3, 8, -16, 48, 108, -23, 10, -4,
        -2, 6, -13, 37, 115, -20, 9, -4, -2, 5, -10, 27, 121, -17, 7, -3,
        -1, 3, -6, 17, 125, -13, 5, -2, 0, 1, -3, 8, 127, -7, 3, -1
    ];

    private static ReadOnlySpan<short> EightTapSmoothFilters =>
    [
        0, 0, 0, 128, 0, 0, 0, 0, -3, -1, 32, 64, 38, 1, -3, 0,
        -2, -2, 29, 63, 41, 2, -3, 0, -2, -2, 26, 63, 43, 4, -4, 0,
        -2, -3, 24, 62, 46, 5, -4, 0, -2, -3, 21, 60, 49, 7, -4, 0,
        -1, -4, 18, 59, 51, 9, -4, 0, -1, -4, 16, 57, 53, 12, -4, -1,
        -1, -4, 14, 55, 55, 14, -4, -1, -1, -4, 12, 53, 57, 16, -4, -1,
        0, -4, 9, 51, 59, 18, -4, -1, 0, -4, 7, 49, 60, 21, -3, -2,
        0, -4, 5, 46, 62, 24, -3, -2, 0, -4, 4, 43, 63, 26, -2, -2,
        0, -3, 2, 41, 63, 29, -2, -2, 0, -3, 1, 38, 64, 32, -1, -3
    ];

    public static bool TryCopyWholePixelPlaneBlock(
        Vp9DecodedFrame referenceFrame,
        Vp9YuvFrameBuffer destination,
        Vp9Plane plane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        Vp9MotionVector planeMotionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (!Vp9InterPredictor.IsValidMotionVector(planeMotionVector))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion vector is outside the valid VP9 range.");
            return false;
        }

        if (!Vp9InterPredictor.IsWholePixelMotionVector(planeMotionVector))
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 whole-pixel motion compensation helper requires whole-pixel motion vectors.");
            return false;
        }

        return TryCopyPlaneBlock(
            referenceFrame,
            destination,
            plane,
            destinationX,
            destinationY,
            width,
            height,
            new Vp9MotionVector(checked(planeMotionVector.Row * 2), checked(planeMotionVector.Column * 2)),
            Vp9InterpolationFilter.EightTap,
            out diagnostic);
    }

    public static bool TryCopyPlaneBlock(
        Vp9DecodedFrame referenceFrame,
        Vp9YuvFrameBuffer destination,
        Vp9Plane plane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        Vp9MotionVector planeMotionVectorQ4,
        Vp9InterpolationFilter interpolationFilter,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (referenceFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 motion compensation currently supports only YUV420 reference frames.");
            return false;
        }

        if (referenceFrame.Width != destination.Width || referenceFrame.Height != destination.Height)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 reference frame scaling is not supported by motion compensation yet.");
            return false;
        }

        if (!IsValidQ4MotionVector(planeMotionVectorQ4))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion vector is outside the valid VP9 range.");
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 motion compensation block dimensions must be positive.");
            return false;
        }

        if (!IsConcreteInterpolationFilter(interpolationFilter))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion compensation requires a concrete interpolation filter.");
            return false;
        }

        var sourcePlane = GetPlane(referenceFrame, plane);
        var destinationPlane = GetPlane(destination, plane);
        var sourceX = destinationX + (planeMotionVectorQ4.Column >> SubpelBits);
        var sourceY = destinationY + (planeMotionVectorQ4.Row >> SubpelBits);
        var subpelX = planeMotionVectorQ4.Column & SubpelMask;
        var subpelY = planeMotionVectorQ4.Row & SubpelMask;
        if (!IsInside(destinationPlane, destinationX, destinationY, width, height))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion compensation block extends outside the destination plane.");
            return false;
        }

        for (var row = 0; row < height; row++)
        {
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                destination.Pixels[destinationOffset + column] = PredictPixel(
                    referenceFrame.Pixels,
                    sourcePlane,
                    sourceX + column,
                    sourceY + row,
                    subpelX,
                    subpelY,
                    interpolationFilter);
            }
        }

        return true;
    }

    public static bool TryAveragePlaneBlock(
        Vp9DecodedFrame referenceFrame0,
        Vp9DecodedFrame referenceFrame1,
        Vp9YuvFrameBuffer destination,
        Vp9Plane plane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        Vp9MotionVector planeMotionVector0Q4,
        Vp9MotionVector planeMotionVector1Q4,
        Vp9InterpolationFilter interpolationFilter,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (!ValidateReferenceFrame(referenceFrame0, destination, out diagnostic) ||
            !ValidateReferenceFrame(referenceFrame1, destination, out diagnostic))
        {
            return false;
        }

        if (!IsValidQ4MotionVector(planeMotionVector0Q4) ||
            !IsValidQ4MotionVector(planeMotionVector1Q4))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion vector is outside the valid VP9 range.");
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 motion compensation block dimensions must be positive.");
            return false;
        }

        if (!IsConcreteInterpolationFilter(interpolationFilter))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion compensation requires a concrete interpolation filter.");
            return false;
        }

        var sourcePlane0 = GetPlane(referenceFrame0, plane);
        var sourcePlane1 = GetPlane(referenceFrame1, plane);
        var destinationPlane = GetPlane(destination, plane);
        var sourceX0 = destinationX + (planeMotionVector0Q4.Column >> SubpelBits);
        var sourceY0 = destinationY + (planeMotionVector0Q4.Row >> SubpelBits);
        var sourceX1 = destinationX + (planeMotionVector1Q4.Column >> SubpelBits);
        var sourceY1 = destinationY + (planeMotionVector1Q4.Row >> SubpelBits);
        var subpelX0 = planeMotionVector0Q4.Column & SubpelMask;
        var subpelY0 = planeMotionVector0Q4.Row & SubpelMask;
        var subpelX1 = planeMotionVector1Q4.Column & SubpelMask;
        var subpelY1 = planeMotionVector1Q4.Row & SubpelMask;
        if (!IsInside(destinationPlane, destinationX, destinationY, width, height))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion compensation block extends outside the destination plane.");
            return false;
        }

        for (var row = 0; row < height; row++)
        {
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                var pixel0 = PredictPixel(
                    referenceFrame0.Pixels,
                    sourcePlane0,
                    sourceX0 + column,
                    sourceY0 + row,
                    subpelX0,
                    subpelY0,
                    interpolationFilter);
                var pixel1 = PredictPixel(
                    referenceFrame1.Pixels,
                    sourcePlane1,
                    sourceX1 + column,
                    sourceY1 + row,
                    subpelX1,
                    subpelY1,
                    interpolationFilter);
                destination.Pixels[destinationOffset + column] = (byte)((pixel0 + pixel1 + 1) >> 1);
            }
        }

        return true;
    }

    private static byte PredictPixel(
        ReadOnlyMemory<byte> referencePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        int subpelX,
        int subpelY,
        Vp9InterpolationFilter interpolationFilter)
    {
        var pixels = referencePixels.Span;
        if (subpelX == 0 && subpelY == 0)
        {
            return ReadClamped(pixels, sourcePlane, sourceX, sourceY);
        }

        var kernels = GetFilterKernels(interpolationFilter);
        if (subpelY == 0)
        {
            return ApplyHorizontalFilter(pixels, sourcePlane, sourceX, sourceY, kernels.Slice(subpelX * SubpelTaps, SubpelTaps));
        }

        if (subpelX == 0)
        {
            return ApplyVerticalFilter(pixels, sourcePlane, sourceX, sourceY, kernels.Slice(subpelY * SubpelTaps, SubpelTaps));
        }

        var xKernel = kernels.Slice(subpelX * SubpelTaps, SubpelTaps);
        var yKernel = kernels.Slice(subpelY * SubpelTaps, SubpelTaps);
        var sum = 0;
        for (var tapY = 0; tapY < SubpelTaps; tapY++)
        {
            var intermediate = ApplyHorizontalFilter(
                pixels,
                sourcePlane,
                sourceX,
                sourceY + tapY - SubpelTapOffset,
                xKernel);
            sum += intermediate * yKernel[tapY];
        }

        return ClipPixel(RoundPowerOfTwo(sum, FilterBits));
    }

    private static bool ValidateReferenceFrame(
        Vp9DecodedFrame referenceFrame,
        Vp9YuvFrameBuffer destination,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (referenceFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 motion compensation currently supports only YUV420 reference frames.");
            return false;
        }

        if (referenceFrame.Width != destination.Width || referenceFrame.Height != destination.Height)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 reference frame scaling is not supported by motion compensation yet.");
            return false;
        }

        return true;
    }

    private static byte ApplyHorizontalFilter(
        ReadOnlySpan<byte> pixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        ReadOnlySpan<short> kernel)
    {
        var sum = 0;
        for (var tap = 0; tap < SubpelTaps; tap++)
        {
            sum += ReadClamped(pixels, sourcePlane, sourceX + tap - SubpelTapOffset, sourceY) * kernel[tap];
        }

        return ClipPixel(RoundPowerOfTwo(sum, FilterBits));
    }

    private static byte ApplyVerticalFilter(
        ReadOnlySpan<byte> pixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        ReadOnlySpan<short> kernel)
    {
        var sum = 0;
        for (var tap = 0; tap < SubpelTaps; tap++)
        {
            sum += ReadClamped(pixels, sourcePlane, sourceX, sourceY + tap - SubpelTapOffset) * kernel[tap];
        }

        return ClipPixel(RoundPowerOfTwo(sum, FilterBits));
    }

    private static byte ReadClamped(ReadOnlySpan<byte> pixels, Vp9DecodedPlane plane, int x, int y)
    {
        var clampedX = Math.Clamp(x, 0, plane.Width - 1);
        var clampedY = Math.Clamp(y, 0, plane.Height - 1);
        return pixels[plane.Offset + (clampedY * plane.Stride) + clampedX];
    }

    private static ReadOnlySpan<short> GetFilterKernels(Vp9InterpolationFilter interpolationFilter)
    {
        return interpolationFilter switch
        {
            Vp9InterpolationFilter.EightTap => EightTapFilters,
            Vp9InterpolationFilter.EightTapSmooth => EightTapSmoothFilters,
            Vp9InterpolationFilter.EightTapSharp => EightTapSharpFilters,
            Vp9InterpolationFilter.Bilinear => BilinearFilters,
            _ => throw new ArgumentOutOfRangeException(
                nameof(interpolationFilter),
                interpolationFilter,
                "VP9 motion compensation requires a concrete interpolation filter.")
        };
    }

    private static int RoundPowerOfTwo(int value, int bits)
    {
        return (value + (1 << (bits - 1))) >> bits;
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
    }

    private static bool IsValidQ4MotionVector(Vp9MotionVector motionVector)
    {
        return motionVector.Row > MotionVectorQ4LowerBound &&
            motionVector.Row < MotionVectorQ4UpperBound &&
            motionVector.Column > MotionVectorQ4LowerBound &&
            motionVector.Column < MotionVectorQ4UpperBound;
    }

    private static bool IsConcreteInterpolationFilter(Vp9InterpolationFilter interpolationFilter)
    {
        return interpolationFilter is
            Vp9InterpolationFilter.EightTap or
            Vp9InterpolationFilter.EightTapSmooth or
            Vp9InterpolationFilter.EightTapSharp or
            Vp9InterpolationFilter.Bilinear;
    }

    private static Vp9DecodedPlane GetPlane(Vp9DecodedFrame frame, Vp9Plane plane)
    {
        var index = GetPlaneIndex(plane);
        return frame.Planes[index];
    }

    private static Vp9DecodedPlane GetPlane(Vp9YuvFrameBuffer frame, Vp9Plane plane)
    {
        return plane switch
        {
            Vp9Plane.Y => frame.YPlane,
            Vp9Plane.U => frame.UPlane,
            Vp9Plane.V => frame.VPlane,
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, "Unsupported VP9 plane.")
        };
    }

    private static int GetPlaneIndex(Vp9Plane plane)
    {
        return plane switch
        {
            Vp9Plane.Y => 0,
            Vp9Plane.U => 1,
            Vp9Plane.V => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, "Unsupported VP9 plane.")
        };
    }

    private static bool IsInside(Vp9DecodedPlane plane, int x, int y, int width, int height)
    {
        return x >= 0 &&
            y >= 0 &&
            width <= plane.Width - x &&
            height <= plane.Height - y;
    }
}
