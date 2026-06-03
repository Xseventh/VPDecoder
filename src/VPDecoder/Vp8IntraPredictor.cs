namespace VPDecoder;

internal static class Vp8IntraPredictor
{
    public static void PredictMacroblock(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        int size,
        Vp8MacroblockPredictionMode mode,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft)
    {
        ValidateSize(size);
        var destination = GetBlockDestination(plane, stride, x, y, size);

        switch (mode)
        {
            case Vp8MacroblockPredictionMode.Dc:
                PredictDc(destination, stride, size, above, left, hasAbove, hasLeft);
                break;
            case Vp8MacroblockPredictionMode.Vertical:
                PredictVertical(destination, stride, size, above, hasAbove);
                break;
            case Vp8MacroblockPredictionMode.Horizontal:
                PredictHorizontal(destination, stride, size, left, hasLeft);
                break;
            case Vp8MacroblockPredictionMode.TrueMotion:
                PredictTrueMotion(destination, stride, size, above, left, topLeft, hasAbove, hasLeft);
                break;
            case Vp8MacroblockPredictionMode.BPred:
                throw new NotSupportedException("VP8 B_PRED macroblock prediction must be expanded through 4x4 block modes.");
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown VP8 macroblock prediction mode.");
        }
    }

    public static void PredictBlock(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        Vp8BlockPredictionMode mode,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft)
    {
        PredictBlock(
            plane,
            stride,
            x,
            y,
            mode,
            above,
            left,
            topLeft,
            hasAbove: true,
            hasLeft: true);
    }

    public static void PredictBlock(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        Vp8BlockPredictionMode mode,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft)
    {
        var destination = GetBlockDestination(plane, stride, x, y, size: 4);
        switch (mode)
        {
            case Vp8BlockPredictionMode.Dc:
                PredictDc(destination, stride, size: 4, above, left, hasAbove, hasLeft);
                break;
            case Vp8BlockPredictionMode.Vertical:
                PredictVerticalEdge4X4(destination, stride, above, topLeft, hasAbove);
                break;
            case Vp8BlockPredictionMode.Horizontal:
                PredictHorizontalEdge4X4(destination, stride, left, topLeft, hasLeft);
                break;
            case Vp8BlockPredictionMode.TrueMotion:
                PredictTrueMotion(destination, stride, size: 4, above, left, topLeft, hasAbove, hasLeft);
                break;
            case Vp8BlockPredictionMode.LeftDown:
                PredictLeftDown4X4(destination, stride, above, hasAbove);
                break;
            case Vp8BlockPredictionMode.RightDown:
                PredictRightDown4X4(destination, stride, above, left, topLeft, hasAbove, hasLeft);
                break;
            case Vp8BlockPredictionMode.VerticalRight:
                PredictVerticalRight4X4(destination, stride, above, left, topLeft, hasAbove, hasLeft);
                break;
            case Vp8BlockPredictionMode.VerticalLeft:
                PredictVerticalLeft4X4(destination, stride, above, hasAbove);
                break;
            case Vp8BlockPredictionMode.HorizontalDown:
                PredictHorizontalDown4X4(destination, stride, above, left, topLeft, hasAbove, hasLeft);
                break;
            case Vp8BlockPredictionMode.HorizontalUp:
                PredictHorizontalUp4X4(destination, stride, left, hasLeft);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown VP8 4x4 intra prediction mode.");
        }
    }

    public static byte GetDcValue(
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        bool hasAbove,
        bool hasLeft)
    {
        ValidateSize(size);
        if (hasAbove)
        {
            ValidateRequiredEdge(above, size, nameof(above), Vp8MacroblockPredictionMode.Dc);
        }

        if (hasLeft)
        {
            ValidateRequiredEdge(left, size, nameof(left), Vp8MacroblockPredictionMode.Dc);
        }

        if (!hasAbove && !hasLeft)
        {
            return 128;
        }

        var sum = 0;
        if (hasAbove)
        {
            sum += Sum(above, size);
        }

        if (hasLeft)
        {
            sum += Sum(left, size);
        }

        return hasAbove && hasLeft
            ? (byte)((sum + size) / (size * 2))
            : (byte)((sum + (size >> 1)) / size);
    }

    private static void PredictDc(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        bool hasAbove,
        bool hasLeft)
    {
        ValidateDestination(destination, stride, size);
        var value = GetDcValue(size, above, left, hasAbove, hasLeft);
        for (var row = 0; row < size; row++)
        {
            destination.Slice(row * stride, size).Fill(value);
        }
    }

    private static void PredictVertical(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        bool hasAbove)
    {
        ValidateDestination(destination, stride, size);
        if (!hasAbove)
        {
            throw new NotSupportedException("VP8 vertical intra prediction requires an above edge sample row.");
        }

        ValidateRequiredEdge(above, size, nameof(above), Vp8MacroblockPredictionMode.Vertical);
        for (var row = 0; row < size; row++)
        {
            above.Slice(0, size).CopyTo(destination.Slice(row * stride, size));
        }
    }

    private static void PredictHorizontal(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> left,
        bool hasLeft)
    {
        ValidateDestination(destination, stride, size);
        if (!hasLeft)
        {
            throw new NotSupportedException("VP8 horizontal intra prediction requires a left edge sample column.");
        }

        ValidateRequiredEdge(left, size, nameof(left), Vp8MacroblockPredictionMode.Horizontal);
        for (var row = 0; row < size; row++)
        {
            destination.Slice(row * stride, size).Fill(left[row]);
        }
    }

    private static void PredictTrueMotion(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft)
    {
        ValidateDestination(destination, stride, size);
        if (!hasAbove || !hasLeft)
        {
            throw new NotSupportedException("VP8 TrueMotion intra prediction requires above and left edge samples.");
        }

        ValidateRequiredEdge(above, size, nameof(above), Vp8MacroblockPredictionMode.TrueMotion);
        ValidateRequiredEdge(left, size, nameof(left), Vp8MacroblockPredictionMode.TrueMotion);
        for (var row = 0; row < size; row++)
        {
            var output = destination.Slice(row * stride, size);
            for (var column = 0; column < size; column++)
            {
                output[column] = ClipPixel(left[row] + above[column] - topLeft);
            }
        }
    }

    private static void PredictVerticalEdge4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        byte topLeft,
        bool hasAbove)
    {
        ValidateBlock4Destination(destination, stride);
        if (!hasAbove)
        {
            throw new NotSupportedException("VP8 B_VE_PRED intra prediction requires an above edge sample row.");
        }

        ValidateRequiredEdge(above, 5, nameof(above), Vp8MacroblockPredictionMode.Vertical);
        Span<byte> row = stackalloc byte[4]
        {
            Avg3(topLeft, above[0], above[1]),
            Avg3(above[0], above[1], above[2]),
            Avg3(above[1], above[2], above[3]),
            Avg3(above[2], above[3], above[4])
        };
        for (var y = 0; y < 4; y++)
        {
            row.CopyTo(destination.Slice(y * stride, 4));
        }
    }

    private static void PredictHorizontalEdge4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasLeft)
    {
        ValidateBlock4Destination(destination, stride);
        if (!hasLeft)
        {
            throw new NotSupportedException("VP8 B_HE_PRED intra prediction requires a left edge sample column.");
        }

        ValidateRequiredEdge(left, 4, nameof(left), Vp8MacroblockPredictionMode.Horizontal);
        Span<byte> column = stackalloc byte[4]
        {
            Avg3(topLeft, left[0], left[1]),
            Avg3(left[0], left[1], left[2]),
            Avg3(left[1], left[2], left[3]),
            Avg3(left[2], left[3], left[3])
        };
        for (var y = 0; y < 4; y++)
        {
            destination.Slice(y * stride, 4).Fill(column[y]);
        }
    }

    private static void PredictLeftDown4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        bool hasAbove)
    {
        ValidateBlock4Destination(destination, stride);
        if (!hasAbove)
        {
            throw new NotSupportedException("VP8 B_LD_PRED intra prediction requires an above edge sample row.");
        }

        ValidateRequiredEdge(above, 8, nameof(above), Vp8MacroblockPredictionMode.Vertical);
        Set(destination, stride, 0, 0, Avg3(above[0], above[1], above[2]));
        Set(destination, stride, 1, 0, Avg3(above[1], above[2], above[3]));
        Set(destination, stride, 0, 1, Avg3(above[1], above[2], above[3]));
        Set(destination, stride, 2, 0, Avg3(above[2], above[3], above[4]));
        Set(destination, stride, 1, 1, Avg3(above[2], above[3], above[4]));
        Set(destination, stride, 0, 2, Avg3(above[2], above[3], above[4]));
        Set(destination, stride, 3, 0, Avg3(above[3], above[4], above[5]));
        Set(destination, stride, 2, 1, Avg3(above[3], above[4], above[5]));
        Set(destination, stride, 1, 2, Avg3(above[3], above[4], above[5]));
        Set(destination, stride, 0, 3, Avg3(above[3], above[4], above[5]));
        Set(destination, stride, 3, 1, Avg3(above[4], above[5], above[6]));
        Set(destination, stride, 2, 2, Avg3(above[4], above[5], above[6]));
        Set(destination, stride, 1, 3, Avg3(above[4], above[5], above[6]));
        Set(destination, stride, 3, 2, Avg3(above[5], above[6], above[7]));
        Set(destination, stride, 2, 3, Avg3(above[5], above[6], above[7]));
        Set(destination, stride, 3, 3, Avg3(above[6], above[7], above[7]));
    }

    private static void PredictRightDown4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft)
    {
        var edge = BuildDirectionalEdge(destination, stride, above, left, topLeft, hasAbove, hasLeft, "B_RD_PRED");
        Set(destination, stride, 0, 3, Avg3(edge[1], edge[2], edge[3]));
        Set(destination, stride, 1, 3, Avg3(edge[2], edge[3], edge[4]));
        Set(destination, stride, 0, 2, Avg3(edge[2], edge[3], edge[4]));
        Set(destination, stride, 2, 3, Avg3(edge[3], edge[4], edge[5]));
        Set(destination, stride, 1, 2, Avg3(edge[3], edge[4], edge[5]));
        Set(destination, stride, 0, 1, Avg3(edge[3], edge[4], edge[5]));
        Set(destination, stride, 3, 3, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 2, 2, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 1, 1, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 0, 0, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 3, 2, Avg3(edge[5], edge[6], edge[7]));
        Set(destination, stride, 2, 1, Avg3(edge[5], edge[6], edge[7]));
        Set(destination, stride, 1, 0, Avg3(edge[5], edge[6], edge[7]));
        Set(destination, stride, 3, 1, Avg3(edge[6], edge[7], edge[8]));
        Set(destination, stride, 2, 0, Avg3(edge[6], edge[7], edge[8]));
        Set(destination, stride, 3, 0, Avg3(edge[7], edge[8], edge[8]));
    }

    private static void PredictVerticalRight4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft)
    {
        var edge = BuildDirectionalEdge(destination, stride, above, left, topLeft, hasAbove, hasLeft, "B_VR_PRED");
        Set(destination, stride, 0, 3, Avg3(edge[2], edge[3], edge[4]));
        Set(destination, stride, 0, 2, Avg3(edge[3], edge[4], edge[5]));
        Set(destination, stride, 1, 3, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 0, 1, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 1, 2, Avg2(edge[4], edge[5]));
        Set(destination, stride, 0, 0, Avg2(edge[4], edge[5]));
        Set(destination, stride, 2, 3, Avg3(edge[5], edge[6], edge[7]));
        Set(destination, stride, 1, 1, Avg3(edge[5], edge[6], edge[7]));
        Set(destination, stride, 2, 2, Avg2(edge[5], edge[6]));
        Set(destination, stride, 1, 0, Avg2(edge[5], edge[6]));
        Set(destination, stride, 3, 3, Avg3(edge[6], edge[7], edge[8]));
        Set(destination, stride, 2, 1, Avg3(edge[6], edge[7], edge[8]));
        Set(destination, stride, 3, 2, Avg2(edge[6], edge[7]));
        Set(destination, stride, 2, 0, Avg2(edge[6], edge[7]));
        Set(destination, stride, 3, 1, Avg3(edge[7], edge[8], edge[8]));
        Set(destination, stride, 3, 0, Avg2(edge[7], edge[8]));
    }

    private static void PredictVerticalLeft4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        bool hasAbove)
    {
        ValidateBlock4Destination(destination, stride);
        if (!hasAbove)
        {
            throw new NotSupportedException("VP8 B_VL_PRED intra prediction requires an above edge sample row.");
        }

        ValidateRequiredEdge(above, 8, nameof(above), Vp8MacroblockPredictionMode.Vertical);
        Set(destination, stride, 0, 0, Avg2(above[0], above[1]));
        Set(destination, stride, 0, 1, Avg3(above[0], above[1], above[2]));
        Set(destination, stride, 0, 2, Avg2(above[1], above[2]));
        Set(destination, stride, 1, 0, Avg2(above[1], above[2]));
        Set(destination, stride, 1, 1, Avg3(above[1], above[2], above[3]));
        Set(destination, stride, 0, 3, Avg3(above[1], above[2], above[3]));
        Set(destination, stride, 1, 2, Avg2(above[2], above[3]));
        Set(destination, stride, 2, 0, Avg2(above[2], above[3]));
        Set(destination, stride, 2, 1, Avg3(above[2], above[3], above[4]));
        Set(destination, stride, 1, 3, Avg3(above[2], above[3], above[4]));
        Set(destination, stride, 2, 2, Avg2(above[3], above[4]));
        Set(destination, stride, 3, 0, Avg2(above[3], above[4]));
        Set(destination, stride, 3, 1, Avg3(above[3], above[4], above[5]));
        Set(destination, stride, 2, 3, Avg3(above[4], above[5], above[6]));
        Set(destination, stride, 3, 2, Avg3(above[4], above[5], above[6]));
        Set(destination, stride, 3, 3, Avg3(above[5], above[6], above[7]));
    }

    private static void PredictHorizontalDown4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft)
    {
        var edge = BuildDirectionalEdge(destination, stride, above, left, topLeft, hasAbove, hasLeft, "B_HD_PRED");
        Set(destination, stride, 0, 3, Avg2(edge[0], edge[1]));
        Set(destination, stride, 1, 3, Avg3(edge[0], edge[1], edge[2]));
        Set(destination, stride, 2, 3, Avg2(edge[1], edge[2]));
        Set(destination, stride, 0, 2, Avg2(edge[1], edge[2]));
        Set(destination, stride, 3, 3, Avg3(edge[1], edge[2], edge[3]));
        Set(destination, stride, 1, 2, Avg3(edge[1], edge[2], edge[3]));
        Set(destination, stride, 2, 2, Avg2(edge[2], edge[3]));
        Set(destination, stride, 0, 1, Avg2(edge[2], edge[3]));
        Set(destination, stride, 3, 2, Avg3(edge[2], edge[3], edge[4]));
        Set(destination, stride, 1, 1, Avg3(edge[2], edge[3], edge[4]));
        Set(destination, stride, 2, 1, Avg2(edge[3], edge[4]));
        Set(destination, stride, 0, 0, Avg2(edge[3], edge[4]));
        Set(destination, stride, 3, 1, Avg3(edge[3], edge[4], edge[5]));
        Set(destination, stride, 1, 0, Avg3(edge[3], edge[4], edge[5]));
        Set(destination, stride, 2, 0, Avg3(edge[4], edge[5], edge[6]));
        Set(destination, stride, 3, 0, Avg3(edge[5], edge[6], edge[7]));
    }

    private static void PredictHorizontalUp4X4(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> left,
        bool hasLeft)
    {
        ValidateBlock4Destination(destination, stride);
        if (!hasLeft)
        {
            throw new NotSupportedException("VP8 B_HU_PRED intra prediction requires a left edge sample column.");
        }

        ValidateRequiredEdge(left, 4, nameof(left), Vp8MacroblockPredictionMode.Horizontal);
        Set(destination, stride, 0, 0, Avg2(left[0], left[1]));
        Set(destination, stride, 1, 0, Avg3(left[0], left[1], left[2]));
        Set(destination, stride, 2, 0, Avg2(left[1], left[2]));
        Set(destination, stride, 0, 1, Avg2(left[1], left[2]));
        Set(destination, stride, 3, 0, Avg3(left[1], left[2], left[3]));
        Set(destination, stride, 1, 1, Avg3(left[1], left[2], left[3]));
        Set(destination, stride, 2, 1, Avg2(left[2], left[3]));
        Set(destination, stride, 0, 2, Avg2(left[2], left[3]));
        Set(destination, stride, 3, 1, Avg3(left[2], left[3], left[3]));
        Set(destination, stride, 1, 2, Avg3(left[2], left[3], left[3]));
        Set(destination, stride, 2, 2, left[3]);
        Set(destination, stride, 3, 2, left[3]);
        Set(destination, stride, 0, 3, left[3]);
        Set(destination, stride, 1, 3, left[3]);
        Set(destination, stride, 2, 3, left[3]);
        Set(destination, stride, 3, 3, left[3]);
    }

    private static byte[] BuildDirectionalEdge(
        Span<byte> destination,
        int stride,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte topLeft,
        bool hasAbove,
        bool hasLeft,
        string modeName)
    {
        ValidateBlock4Destination(destination, stride);
        if (!hasAbove || !hasLeft)
        {
            throw new NotSupportedException($"VP8 {modeName} intra prediction requires above and left edge samples.");
        }

        ValidateRequiredEdge(above, 4, nameof(above), Vp8MacroblockPredictionMode.TrueMotion);
        ValidateRequiredEdge(left, 4, nameof(left), Vp8MacroblockPredictionMode.TrueMotion);
        return
        [
            left[3],
            left[2],
            left[1],
            left[0],
            topLeft,
            above[0],
            above[1],
            above[2],
            above[3]
        ];
    }

    private static void ValidateBlock4Destination(Span<byte> destination, int stride)
    {
        ValidateDestination(destination, stride, size: 4);
    }

    private static void Set(Span<byte> destination, int stride, int x, int y, byte value)
    {
        destination[(y * stride) + x] = value;
    }

    private static byte Avg2(byte a, byte b)
    {
        return (byte)((a + b + 1) >> 1);
    }

    private static byte Avg3(byte a, byte b, byte c)
    {
        return (byte)((a + (2 * b) + c + 2) >> 2);
    }

    private static Span<byte> GetBlockDestination(Span<byte> plane, int stride, int x, int y, int size)
    {
        ValidateDestination(plane, stride, x, y, size);
        return plane[((y * stride) + x)..];
    }

    private static void ValidateDestination(Span<byte> destination, int stride, int size)
    {
        ValidateDestination(destination, stride, x: 0, y: 0, size);
    }

    private static void ValidateDestination(Span<byte> plane, int stride, int x, int y, int size)
    {
        if (stride < size)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride must fit the VP8 prediction block width.");
        }

        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (x > stride - size)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Prediction block exceeds the plane stride.");
        }

        var lastOffset = checked(((y + size - 1) * stride) + x + size);
        if (lastOffset > plane.Length)
        {
            throw new ArgumentException("Prediction block exceeds the plane buffer.", nameof(plane));
        }
    }

    private static void ValidateRequiredEdge(
        ReadOnlySpan<byte> edge,
        int size,
        string name,
        Vp8MacroblockPredictionMode mode)
    {
        if (edge.Length < size)
        {
            throw new ArgumentException($"VP8 {mode} intra prediction requires at least {size} {name} samples.", name);
        }
    }

    private static void ValidateSize(int size)
    {
        if (size is not (4 or 8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "VP8 intra prediction supports 4x4, 8x8, and 16x16 blocks.");
        }
    }

    private static int Sum(ReadOnlySpan<byte> values, int count)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += values[i];
        }

        return sum;
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}
