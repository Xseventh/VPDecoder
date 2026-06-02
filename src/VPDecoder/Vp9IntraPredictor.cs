namespace VPDecoder;

internal static class Vp9IntraPredictor
{
    public static void Predict(
        Vp9PredictionMode mode,
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft)
    {
        switch (mode)
        {
            case Vp9PredictionMode.Dc:
                PredictDc(destination, stride, size, above, left);
                break;
            case Vp9PredictionMode.Vertical:
                PredictVertical(destination, stride, size, above);
                break;
            case Vp9PredictionMode.Horizontal:
                PredictHorizontal(destination, stride, size, left);
                break;
            case Vp9PredictionMode.TrueMotion:
                PredictTrueMotion(destination, stride, size, above, left, aboveLeft);
                break;
            default:
                throw new NotSupportedException($"VP9 intra predictor {mode} is not implemented yet.");
        }
    }

    public static void PredictDc(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left)
    {
        ValidateDestination(destination, stride, size);
        ValidateOptionalEdge(above, size, nameof(above));
        ValidateOptionalEdge(left, size, nameof(left));

        var value = GetDcValue(size, above, left);
        for (var y = 0; y < size; y++)
        {
            destination.Slice(y * stride, size).Fill(value);
        }
    }

    public static void PredictVertical(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(above, size, nameof(above), Vp9PredictionMode.Vertical);

        for (var y = 0; y < size; y++)
        {
            above.Slice(0, size).CopyTo(destination.Slice(y * stride, size));
        }
    }

    public static void PredictHorizontal(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> left)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(left, size, nameof(left), Vp9PredictionMode.Horizontal);

        for (var y = 0; y < size; y++)
        {
            destination.Slice(y * stride, size).Fill(left[y]);
        }
    }

    public static void PredictTrueMotion(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(above, size, nameof(above), Vp9PredictionMode.TrueMotion);
        ValidateRequiredEdge(left, size, nameof(left), Vp9PredictionMode.TrueMotion);
        if (!aboveLeft.HasValue)
        {
            throw new NotSupportedException("VP9 TrueMotion intra predictor requires an above-left sample.");
        }

        for (var y = 0; y < size; y++)
        {
            var row = destination.Slice(y * stride, size);
            for (var x = 0; x < size; x++)
            {
                row[x] = ClipPixel(left[y] + above[x] - aboveLeft.Value);
            }
        }
    }

    public static byte GetDcValue(int size, ReadOnlySpan<byte> above, ReadOnlySpan<byte> left)
    {
        ValidateOptionalEdge(above, size, nameof(above));
        ValidateOptionalEdge(left, size, nameof(left));

        if (above.IsEmpty && left.IsEmpty)
        {
            return 128;
        }

        var sum = 0;
        if (!above.IsEmpty)
        {
            for (var i = 0; i < size; i++)
            {
                sum += above[i];
            }
        }

        if (!left.IsEmpty)
        {
            for (var i = 0; i < size; i++)
            {
                sum += left[i];
            }
        }

        var divisorShift = above.IsEmpty || left.IsEmpty
            ? Log2(size)
            : Log2(size) + 1;
        return (byte)((sum + (1 << (divisorShift - 1))) >> divisorShift);
    }

    private static void ValidateDestination(ReadOnlySpan<byte> destination, int stride, int size)
    {
        if (stride < size)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "VP9 predictor stride must be at least the block size.");
        }

        _ = Log2(size);
        if (destination.Length < ((size - 1) * stride) + size)
        {
            throw new ArgumentException("VP9 predictor destination is too small.", nameof(destination));
        }
    }

    private static void ValidateOptionalEdge(ReadOnlySpan<byte> edge, int size, string parameterName)
    {
        _ = Log2(size);
        if (!edge.IsEmpty && edge.Length < size)
        {
            throw new ArgumentException("VP9 predictor edge span is too small.", parameterName);
        }
    }

    private static void ValidateRequiredEdge(
        ReadOnlySpan<byte> edge,
        int size,
        string parameterName,
        Vp9PredictionMode mode)
    {
        if (edge.IsEmpty)
        {
            throw new NotSupportedException($"VP9 {mode} intra predictor requires {parameterName} edge samples.");
        }

        ValidateOptionalEdge(edge, size, parameterName);
    }

    private static byte ClipPixel(int value)
    {
        return value switch
        {
            < 0 => 0,
            > 255 => 255,
            _ => (byte)value
        };
    }

    private static int Log2(int value)
    {
        return value switch
        {
            4 => 2,
            8 => 3,
            16 => 4,
            32 => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "VP9 predictor size must be 4, 8, 16, or 32.")
        };
    }
}
