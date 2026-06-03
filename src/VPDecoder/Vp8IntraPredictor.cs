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
        var destination = GetBlockDestination(plane, stride, x, y, size: 4);
        switch (mode)
        {
            case Vp8BlockPredictionMode.Dc:
                PredictDc(destination, stride, size: 4, above, left, hasAbove: true, hasLeft: true);
                break;
            case Vp8BlockPredictionMode.Vertical:
                PredictVertical(destination, stride, size: 4, above, hasAbove: true);
                break;
            case Vp8BlockPredictionMode.Horizontal:
                PredictHorizontal(destination, stride, size: 4, left, hasLeft: true);
                break;
            case Vp8BlockPredictionMode.TrueMotion:
                PredictTrueMotion(destination, stride, size: 4, above, left, topLeft, hasAbove: true, hasLeft: true);
                break;
            default:
                throw new NotSupportedException($"VP8 4x4 intra prediction mode {mode} is not implemented yet.");
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
