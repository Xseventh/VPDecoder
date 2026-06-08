namespace VPDecoder;

using System.Runtime.CompilerServices;

internal static class Vp9MotionCompensator
{
    private const int FilterBits = 7;
    private const int SubpelBits = 4;
    private const int SubpelMask = (1 << SubpelBits) - 1;
    private const int SubpelTaps = 8;
    private const int SubpelTapOffset = (SubpelTaps / 2) - 1;
    private const int SubpelRightTapOffset = SubpelTaps - SubpelTapOffset - 1;
    private const int MaxConvolveBlockDimension = 64;
    private const int MaxConvolveBlockPixels = MaxConvolveBlockDimension * MaxConvolveBlockDimension;
    private const int MaxTwoDimensionalTempRows = MaxConvolveBlockDimension + SubpelTaps - 1;
    private const int MaxTwoDimensionalTempPixels = MaxConvolveBlockDimension * MaxTwoDimensionalTempRows;
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

        if (subpelX == 0 &&
            subpelY == 0 &&
            IsInside(sourcePlane, sourceX, sourceY, width, height))
        {
            CopyPlaneRows(
                referenceFrame.Pixels,
                sourcePlane,
                sourceX,
                sourceY,
                destination.Pixels,
                destinationPlane,
                destinationX,
                destinationY,
                width,
                height);
            return true;
        }

        var sourcePixels = referenceFrame.Pixels.AsSpan();
        var destinationPixels = destination.Pixels;
        var kernels = GetFilterKernels(interpolationFilter);

        if (subpelY == 0)
        {
            var xKernel = kernels.Slice(subpelX * SubpelTaps, SubpelTaps);
            if (IsHorizontalFilterInputInside(sourcePlane, sourceX, sourceY, width, height))
            {
                PredictHorizontalRows(
                    sourcePixels,
                    sourcePlane,
                    sourceX,
                    sourceY,
                    destinationPixels,
                    destinationPlane,
                    destinationX,
                    destinationY,
                    width,
                    height,
                    xKernel);
                return true;
            }

            PredictHorizontalRowsClamped(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                destinationPixels,
                destinationPlane,
                destinationX,
                destinationY,
                width,
                height,
                xKernel);
            return true;
        }

        if (subpelX == 0)
        {
            var yKernel = kernels.Slice(subpelY * SubpelTaps, SubpelTaps);
            if (IsVerticalFilterInputInside(sourcePlane, sourceX, sourceY, width, height))
            {
                PredictVerticalRows(
                    sourcePixels,
                    sourcePlane,
                    sourceX,
                    sourceY,
                    destinationPixels,
                    destinationPlane,
                    destinationX,
                    destinationY,
                    width,
                    height,
                    yKernel);
                return true;
            }

            PredictVerticalRowsClamped(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                destinationPixels,
                destinationPlane,
                destinationX,
                destinationY,
                width,
                height,
                yKernel);
            return true;
        }

        var twoDimensionalXKernel = kernels.Slice(subpelX * SubpelTaps, SubpelTaps);
        var twoDimensionalYKernel = kernels.Slice(subpelY * SubpelTaps, SubpelTaps);
        if (IsTwoDimensionalFilterInputInside(sourcePlane, sourceX, sourceY, width, height))
        {
            PredictTwoDimensionalRows(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                destinationPixels,
                destinationPlane,
                destinationX,
                destinationY,
                width,
                height,
                twoDimensionalXKernel,
                twoDimensionalYKernel);
            return true;
        }

        PredictTwoDimensionalRowsClamped(
            sourcePixels,
            sourcePlane,
            sourceX,
            sourceY,
            destinationPixels,
            destinationPlane,
            destinationX,
            destinationY,
            width,
            height,
            twoDimensionalXKernel,
            twoDimensionalYKernel);
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

        if (subpelX0 == 0 &&
            subpelY0 == 0 &&
            subpelX1 == 0 &&
            subpelY1 == 0 &&
            IsInside(sourcePlane0, sourceX0, sourceY0, width, height) &&
            IsInside(sourcePlane1, sourceX1, sourceY1, width, height))
        {
            AveragePlaneRows(
                referenceFrame0.Pixels,
                sourcePlane0,
                sourceX0,
                sourceY0,
                referenceFrame1.Pixels,
                sourcePlane1,
                sourceX1,
                sourceY1,
                destination.Pixels,
                destinationPlane,
                destinationX,
                destinationY,
                width,
                height);
            return true;
        }

        var sourcePixels0 = referenceFrame0.Pixels.AsSpan();
        var sourcePixels1 = referenceFrame1.Pixels.AsSpan();
        var destinationPixels = destination.Pixels;
        var kernels = GetFilterKernels(interpolationFilter);
        var xKernel0 = kernels.Slice(subpelX0 * SubpelTaps, SubpelTaps);
        var yKernel0 = kernels.Slice(subpelY0 * SubpelTaps, SubpelTaps);
        var xKernel1 = kernels.Slice(subpelX1 * SubpelTaps, SubpelTaps);
        var yKernel1 = kernels.Slice(subpelY1 * SubpelTaps, SubpelTaps);
        var isSource0FilterInputInside = IsFilterInputInside(
            sourcePlane0,
            sourceX0,
            sourceY0,
            width,
            height,
            subpelX0,
            subpelY0);
        var isSource1FilterInputInside = IsFilterInputInside(
            sourcePlane1,
            sourceX1,
            sourceY1,
            width,
            height,
            subpelX1,
            subpelY1);
        Span<byte> prediction0 = stackalloc byte[MaxConvolveBlockPixels];
        Span<byte> prediction1 = stackalloc byte[MaxConvolveBlockPixels];
        PredictPlaneBlockToSpan(
            sourcePixels0,
            sourcePlane0,
            sourceX0,
            sourceY0,
            width,
            height,
            subpelX0,
            subpelY0,
            xKernel0,
            yKernel0,
            isSource0FilterInputInside,
            prediction0,
            MaxConvolveBlockDimension);
        PredictPlaneBlockToSpan(
            sourcePixels1,
            sourcePlane1,
            sourceX1,
            sourceY1,
            width,
            height,
            subpelX1,
            subpelY1,
            xKernel1,
            yKernel1,
            isSource1FilterInputInside,
            prediction1,
            MaxConvolveBlockDimension);
        AveragePredictionRows(
            prediction0,
            prediction1,
            destinationPixels,
            destinationPlane,
            destinationX,
            destinationY,
            width,
            height,
            MaxConvolveBlockDimension);

        return true;
    }

    private static void PredictPlaneBlockToSpan(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        int width,
        int height,
        int subpelX,
        int subpelY,
        ReadOnlySpan<short> xKernel,
        ReadOnlySpan<short> yKernel,
        bool isFilterInputInside,
        Span<byte> destination,
        int destinationStride)
    {
        if (subpelX == 0 && subpelY == 0)
        {
            PredictWholePixelToSpan(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                width,
                height,
                isFilterInputInside,
                destination,
                destinationStride);
            return;
        }

        if (subpelY == 0)
        {
            PredictHorizontalToSpan(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                width,
                height,
                xKernel,
                isFilterInputInside,
                destination,
                destinationStride);
            return;
        }

        if (subpelX == 0)
        {
            PredictVerticalToSpan(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                width,
                height,
                yKernel,
                isFilterInputInside,
                destination,
                destinationStride);
            return;
        }

        PredictTwoDimensionalToSpan(
            sourcePixels,
            sourcePlane,
            sourceX,
            sourceY,
            width,
            height,
            xKernel,
            yKernel,
            isFilterInputInside,
            destination,
            destinationStride);
    }

    private static void PredictWholePixelToSpan(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        int width,
        int height,
        bool isFilterInputInside,
        Span<byte> destination,
        int destinationStride)
    {
        if (isFilterInputInside)
        {
            for (var row = 0; row < height; row++)
            {
                var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
                sourcePixels.Slice(sourceOffset, width).CopyTo(destination.Slice(row * destinationStride, width));
            }

            return;
        }

        for (var row = 0; row < height; row++)
        {
            var destinationOffset = row * destinationStride;
            for (var column = 0; column < width; column++)
            {
                destination[destinationOffset + column] = ReadClamped(
                    sourcePixels,
                    sourcePlane,
                    sourceX + column,
                    sourceY + row);
            }
        }
    }

    private static void PredictHorizontalToSpan(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        int width,
        int height,
        ReadOnlySpan<short> kernel,
        bool isFilterInputInside,
        Span<byte> destination,
        int destinationStride)
    {
        for (var row = 0; row < height; row++)
        {
            var destinationOffset = row * destinationStride;
            if (isFilterInputInside)
            {
                var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
                for (var column = 0; column < width; column++)
                {
                    destination[destinationOffset + column] = ApplyHorizontalFilterUnclamped(
                        sourcePixels,
                        sourceOffset + column,
                        kernel);
                }

                continue;
            }

            for (var column = 0; column < width; column++)
            {
                destination[destinationOffset + column] = ApplyHorizontalFilter(
                    sourcePixels,
                    sourcePlane,
                    sourceX + column,
                    sourceY + row,
                    kernel);
            }
        }
    }

    private static void PredictVerticalToSpan(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        int width,
        int height,
        ReadOnlySpan<short> kernel,
        bool isFilterInputInside,
        Span<byte> destination,
        int destinationStride)
    {
        for (var row = 0; row < height; row++)
        {
            var destinationOffset = row * destinationStride;
            if (isFilterInputInside)
            {
                var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
                for (var column = 0; column < width; column++)
                {
                    destination[destinationOffset + column] = ApplyVerticalFilterUnclamped(
                        sourcePixels,
                        sourceOffset + column,
                        sourcePlane.Stride,
                        kernel);
                }

                continue;
            }

            for (var column = 0; column < width; column++)
            {
                destination[destinationOffset + column] = ApplyVerticalFilter(
                    sourcePixels,
                    sourcePlane,
                    sourceX + column,
                    sourceY + row,
                    kernel);
            }
        }
    }

    private static void PredictTwoDimensionalToSpan(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        int width,
        int height,
        ReadOnlySpan<short> xKernel,
        ReadOnlySpan<short> yKernel,
        bool isFilterInputInside,
        Span<byte> destination,
        int destinationStride)
    {
        if (!isFilterInputInside)
        {
            for (var row = 0; row < height; row++)
            {
                var destinationOffset = row * destinationStride;
                for (var column = 0; column < width; column++)
                {
                    var sum = 0;
                    for (var tapY = 0; tapY < SubpelTaps; tapY++)
                    {
                        var intermediate = ApplyHorizontalFilter(
                            sourcePixels,
                            sourcePlane,
                            sourceX + column,
                            sourceY + row + tapY - SubpelTapOffset,
                            xKernel);
                        sum += intermediate * yKernel[tapY];
                    }

                    destination[destinationOffset + column] = ClipPixel(RoundPowerOfTwo(sum, FilterBits));
                }
            }

            return;
        }

        Span<byte> horizontal = stackalloc byte[MaxTwoDimensionalTempPixels];
        var tempStride = MaxConvolveBlockDimension;
        var tempRows = height + SubpelTaps - 1;
        for (var tempRow = 0; tempRow < tempRows; tempRow++)
        {
            var sourceOffset = sourcePlane.Offset +
                ((sourceY + tempRow - SubpelTapOffset) * sourcePlane.Stride) +
                sourceX;
            var tempOffset = tempRow * tempStride;
            for (var column = 0; column < width; column++)
            {
                horizontal[tempOffset + column] = ApplyHorizontalFilterUnclamped(
                    sourcePixels,
                    sourceOffset + column,
                    xKernel);
            }
        }

        for (var row = 0; row < height; row++)
        {
            var destinationOffset = row * destinationStride;
            var tempOffset = row * tempStride;
            for (var column = 0; column < width; column++)
            {
                destination[destinationOffset + column] = ApplyVerticalFilterFromTemp(
                    horizontal,
                    tempOffset + column,
                    tempStride,
                    yKernel);
            }
        }
    }

    private static void AveragePredictionRows(
        ReadOnlySpan<byte> prediction0,
        ReadOnlySpan<byte> prediction1,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        int predictionStride)
    {
        for (var row = 0; row < height; row++)
        {
            var predictionOffset = row * predictionStride;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                destinationPixels[destinationOffset + column] = (byte)(
                    (prediction0[predictionOffset + column] + prediction1[predictionOffset + column] + 1) >> 1);
            }
        }
    }

    private static void CopyPlaneRows(
        byte[] sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height)
    {
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            sourcePixels.AsSpan(sourceOffset, width).CopyTo(destinationPixels.AsSpan(destinationOffset, width));
        }
    }

    private static void AveragePlaneRows(
        byte[] sourcePixels0,
        Vp9DecodedPlane sourcePlane0,
        int sourceX0,
        int sourceY0,
        byte[] sourcePixels1,
        Vp9DecodedPlane sourcePlane1,
        int sourceX1,
        int sourceY1,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height)
    {
        for (var row = 0; row < height; row++)
        {
            var sourceOffset0 = sourcePlane0.Offset + ((sourceY0 + row) * sourcePlane0.Stride) + sourceX0;
            var sourceOffset1 = sourcePlane1.Offset + ((sourceY1 + row) * sourcePlane1.Stride) + sourceX1;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            var sourceRow0 = sourcePixels0.AsSpan(sourceOffset0, width);
            var sourceRow1 = sourcePixels1.AsSpan(sourceOffset1, width);
            var destinationRow = destinationPixels.AsSpan(destinationOffset, width);
            for (var column = 0; column < width; column++)
            {
                destinationRow[column] = (byte)((sourceRow0[column] + sourceRow1[column] + 1) >> 1);
            }
        }
    }

    private static void PredictHorizontalRows(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> kernel)
    {
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                destinationPixels[destinationOffset + column] = ApplyHorizontalFilterUnclamped(
                    sourcePixels,
                    sourceOffset + column,
                    kernel);
            }
        }
    }

    private static void PredictHorizontalRowsClamped(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> kernel)
    {
        for (var row = 0; row < height; row++)
        {
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                destinationPixels[destinationOffset + column] = ApplyHorizontalFilter(
                    sourcePixels,
                    sourcePlane,
                    sourceX + column,
                    sourceY + row,
                    kernel);
            }
        }
    }

    private static void PredictVerticalRows(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> kernel)
    {
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                destinationPixels[destinationOffset + column] = ApplyVerticalFilterUnclamped(
                    sourcePixels,
                    sourceOffset + column,
                    sourcePlane.Stride,
                    kernel);
            }
        }
    }

    private static void PredictVerticalRowsClamped(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> kernel)
    {
        for (var row = 0; row < height; row++)
        {
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                destinationPixels[destinationOffset + column] = ApplyVerticalFilter(
                    sourcePixels,
                    sourcePlane,
                    sourceX + column,
                    sourceY + row,
                    kernel);
            }
        }
    }

    private static void PredictTwoDimensionalRows(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> xKernel,
        ReadOnlySpan<short> yKernel)
    {
        if (width > MaxConvolveBlockDimension || height > MaxConvolveBlockDimension)
        {
            PredictTwoDimensionalRowsDirect(
                sourcePixels,
                sourcePlane,
                sourceX,
                sourceY,
                destinationPixels,
                destinationPlane,
                destinationX,
                destinationY,
                width,
                height,
                xKernel,
                yKernel);
            return;
        }

        Span<byte> horizontal = stackalloc byte[MaxTwoDimensionalTempPixels];
        var tempStride = MaxConvolveBlockDimension;
        var tempRows = height + SubpelTaps - 1;
        for (var tempRow = 0; tempRow < tempRows; tempRow++)
        {
            var sourceOffset = sourcePlane.Offset +
                ((sourceY + tempRow - SubpelTapOffset) * sourcePlane.Stride) +
                sourceX;
            var tempOffset = tempRow * tempStride;
            for (var column = 0; column < width; column++)
            {
                horizontal[tempOffset + column] = ApplyHorizontalFilterUnclamped(
                    sourcePixels,
                    sourceOffset + column,
                    xKernel);
            }
        }

        for (var row = 0; row < height; row++)
        {
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            var tempOffset = row * tempStride;
            for (var column = 0; column < width; column++)
            {
                destinationPixels[destinationOffset + column] = ApplyVerticalFilterFromTemp(
                    horizontal,
                    tempOffset + column,
                    tempStride,
                    yKernel);
            }
        }
    }

    private static void PredictTwoDimensionalRowsDirect(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> xKernel,
        ReadOnlySpan<short> yKernel)
    {
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                var sum = 0;
                var centerOffset = sourceOffset + column;
                for (var tapY = 0; tapY < SubpelTaps; tapY++)
                {
                    var intermediate = ApplyHorizontalFilterUnclamped(
                        sourcePixels,
                        centerOffset + ((tapY - SubpelTapOffset) * sourcePlane.Stride),
                        xKernel);
                    sum += intermediate * yKernel[tapY];
                }

                destinationPixels[destinationOffset + column] = ClipPixel(RoundPowerOfTwo(sum, FilterBits));
            }
        }
    }

    private static void PredictTwoDimensionalRowsClamped(
        ReadOnlySpan<byte> sourcePixels,
        Vp9DecodedPlane sourcePlane,
        int sourceX,
        int sourceY,
        byte[] destinationPixels,
        Vp9DecodedPlane destinationPlane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        ReadOnlySpan<short> xKernel,
        ReadOnlySpan<short> yKernel)
    {
        for (var row = 0; row < height; row++)
        {
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            for (var column = 0; column < width; column++)
            {
                var sum = 0;
                for (var tapY = 0; tapY < SubpelTaps; tapY++)
                {
                    var intermediate = ApplyHorizontalFilter(
                        sourcePixels,
                        sourcePlane,
                        sourceX + column,
                        sourceY + row + tapY - SubpelTapOffset,
                        xKernel);
                    sum += intermediate * yKernel[tapY];
                }

                destinationPixels[destinationOffset + column] = ClipPixel(RoundPowerOfTwo(sum, FilterBits));
            }
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ApplyHorizontalFilterUnclamped(
        ReadOnlySpan<byte> pixels,
        int centerOffset,
        ReadOnlySpan<short> kernel)
    {
        var sum =
            (pixels[centerOffset - 3] * kernel[0]) +
            (pixels[centerOffset - 2] * kernel[1]) +
            (pixels[centerOffset - 1] * kernel[2]) +
            (pixels[centerOffset] * kernel[3]) +
            (pixels[centerOffset + 1] * kernel[4]) +
            (pixels[centerOffset + 2] * kernel[5]) +
            (pixels[centerOffset + 3] * kernel[6]) +
            (pixels[centerOffset + 4] * kernel[7]);

        return ClipPixel(RoundPowerOfTwo(sum, FilterBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ApplyVerticalFilterUnclamped(
        ReadOnlySpan<byte> pixels,
        int centerOffset,
        int stride,
        ReadOnlySpan<short> kernel)
    {
        var sum =
            (pixels[centerOffset - (3 * stride)] * kernel[0]) +
            (pixels[centerOffset - (2 * stride)] * kernel[1]) +
            (pixels[centerOffset - stride] * kernel[2]) +
            (pixels[centerOffset] * kernel[3]) +
            (pixels[centerOffset + stride] * kernel[4]) +
            (pixels[centerOffset + (2 * stride)] * kernel[5]) +
            (pixels[centerOffset + (3 * stride)] * kernel[6]) +
            (pixels[centerOffset + (4 * stride)] * kernel[7]);

        return ClipPixel(RoundPowerOfTwo(sum, FilterBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ApplyVerticalFilterFromTemp(
        ReadOnlySpan<byte> pixels,
        int centerOffset,
        int stride,
        ReadOnlySpan<short> kernel)
    {
        var sum =
            (pixels[centerOffset] * kernel[0]) +
            (pixels[centerOffset + stride] * kernel[1]) +
            (pixels[centerOffset + (2 * stride)] * kernel[2]) +
            (pixels[centerOffset + (3 * stride)] * kernel[3]) +
            (pixels[centerOffset + (4 * stride)] * kernel[4]) +
            (pixels[centerOffset + (5 * stride)] * kernel[5]) +
            (pixels[centerOffset + (6 * stride)] * kernel[6]) +
            (pixels[centerOffset + (7 * stride)] * kernel[7]);

        return ClipPixel(RoundPowerOfTwo(sum, FilterBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ReadClamped(ReadOnlySpan<byte> pixels, Vp9DecodedPlane plane, int x, int y)
    {
        var clampedX = x < 0
            ? 0
            : x >= plane.Width
                ? plane.Width - 1
                : x;
        var clampedY = y < 0
            ? 0
            : y >= plane.Height
                ? plane.Height - 1
                : y;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RoundPowerOfTwo(int value, int bits)
    {
        return (value + (1 << (bits - 1))) >> bits;
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

    private static bool IsHorizontalFilterInputInside(
        Vp9DecodedPlane plane,
        int x,
        int y,
        int width,
        int height)
    {
        return x >= SubpelTapOffset &&
            y >= 0 &&
            width <= plane.Width - x - SubpelRightTapOffset &&
            height <= plane.Height - y;
    }

    private static bool IsVerticalFilterInputInside(
        Vp9DecodedPlane plane,
        int x,
        int y,
        int width,
        int height)
    {
        return x >= 0 &&
            y >= SubpelTapOffset &&
            width <= plane.Width - x &&
            height <= plane.Height - y - SubpelRightTapOffset;
    }

    private static bool IsTwoDimensionalFilterInputInside(
        Vp9DecodedPlane plane,
        int x,
        int y,
        int width,
        int height)
    {
        return x >= SubpelTapOffset &&
            y >= SubpelTapOffset &&
            width <= plane.Width - x - SubpelRightTapOffset &&
            height <= plane.Height - y - SubpelRightTapOffset;
    }

    private static bool IsFilterInputInside(
        Vp9DecodedPlane plane,
        int x,
        int y,
        int width,
        int height,
        int subpelX,
        int subpelY)
    {
        if (subpelX == 0 && subpelY == 0)
        {
            return IsInside(plane, x, y, width, height);
        }

        if (subpelY == 0)
        {
            return IsHorizontalFilterInputInside(plane, x, y, width, height);
        }

        return subpelX == 0
            ? IsVerticalFilterInputInside(plane, x, y, width, height)
            : IsTwoDimensionalFilterInputInside(plane, x, y, width, height);
    }
}
