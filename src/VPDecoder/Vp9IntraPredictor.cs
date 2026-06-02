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
            case Vp9PredictionMode.D45:
                PredictD45(destination, stride, size, above);
                break;
            case Vp9PredictionMode.D63:
                PredictD63(destination, stride, size, above);
                break;
            case Vp9PredictionMode.D117:
                PredictD117(destination, stride, size, above, left, aboveLeft);
                break;
            case Vp9PredictionMode.D135:
                PredictD135(destination, stride, size, above, left, aboveLeft);
                break;
            case Vp9PredictionMode.D153:
                PredictD153(destination, stride, size, above, left, aboveLeft);
                break;
            case Vp9PredictionMode.D207:
                PredictD207(destination, stride, size, left);
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

    public static void PredictD45(Span<byte> destination, int stride, int size, ReadOnlySpan<byte> above)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(above, size, nameof(above), Vp9PredictionMode.D45);

        var aboveRight = above[Math.Min(size - 1, above.Length - 1)];
        for (var x = 0; x < size - 1; x++)
        {
            destination[x] = Avg3(GetAbove(above, x), GetAbove(above, x + 1), GetAbove(above, x + 2));
        }

        destination[size - 1] = aboveRight;
        for (var y = 1; y < size; y++)
        {
            var row = destination.Slice(y * stride, size);
            var copyLength = size - 1 - y;
            if (copyLength > 0)
            {
                destination.Slice(y, copyLength).CopyTo(row);
            }

            row.Slice(copyLength).Fill(aboveRight);
        }
    }

    public static void PredictD63(Span<byte> destination, int stride, int size, ReadOnlySpan<byte> above)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(above, size, nameof(above), Vp9PredictionMode.D63);

        var aboveRight = above[Math.Min(size - 1, above.Length - 1)];
        for (var x = 0; x < size; x++)
        {
            destination[x] = Avg2(GetAbove(above, x), GetAbove(above, x + 1));
            destination[stride + x] = Avg3(GetAbove(above, x), GetAbove(above, x + 1), GetAbove(above, x + 2));
        }

        var copySize = size - 2;
        for (var y = 2; y < size; y += 2, copySize--)
        {
            var evenRow = destination.Slice(y * stride, size);
            destination.Slice(y / 2, copySize).CopyTo(evenRow);
            evenRow.Slice(copySize).Fill(aboveRight);

            if (y + 1 >= size)
            {
                break;
            }

            var oddRow = destination.Slice((y + 1) * stride, size);
            destination.Slice(stride + (y / 2), copySize).CopyTo(oddRow);
            oddRow.Slice(copySize).Fill(aboveRight);
        }
    }

    public static void PredictD117(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft)
    {
        ValidateDirectionalEdges(destination, stride, size, above, left, aboveLeft, Vp9PredictionMode.D117);

        var topLeft = aboveLeft!.Value;
        var row0 = destination.Slice(0, size);
        row0[0] = Avg2(topLeft, above[0]);
        for (var x = 1; x < size; x++)
        {
            row0[x] = Avg2(above[x - 1], above[x]);
        }

        var row1 = destination.Slice(stride, size);
        row1[0] = Avg3(left[0], topLeft, above[0]);
        for (var x = 1; x < size; x++)
        {
            row1[x] = Avg3(GetAboveWithTopLeft(above, topLeft, x - 2), GetAboveWithTopLeft(above, topLeft, x - 1), above[x]);
        }

        if (size <= 2)
        {
            return;
        }

        destination[2 * stride] = Avg3(topLeft, left[0], left[1]);
        for (var y = 3; y < size; y++)
        {
            destination[y * stride] = Avg3(left[y - 3], left[y - 2], left[y - 1]);
        }

        for (var y = 2; y < size; y++)
        {
            var row = destination.Slice(y * stride, size);
            var source = destination.Slice((y - 2) * stride, size);
            for (var x = 1; x < size; x++)
            {
                row[x] = source[x - 1];
            }
        }
    }

    public static void PredictD135(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft)
    {
        ValidateDirectionalEdges(destination, stride, size, above, left, aboveLeft, Vp9PredictionMode.D135);

        var topLeft = aboveLeft!.Value;
        Span<byte> border = stackalloc byte[63];
        for (var i = 0; i < size - 2; i++)
        {
            border[i] = Avg3(left[size - 3 - i], left[size - 2 - i], left[size - 1 - i]);
        }

        border[size - 2] = Avg3(topLeft, left[0], left[1]);
        border[size - 1] = Avg3(left[0], topLeft, above[0]);
        border[size] = Avg3(topLeft, above[0], above[1]);
        for (var i = 0; i < size - 2; i++)
        {
            border[size + 1 + i] = Avg3(above[i], above[i + 1], above[i + 2]);
        }

        for (var y = 0; y < size; y++)
        {
            border.Slice(size - 1 - y, size).CopyTo(destination.Slice(y * stride, size));
        }
    }

    public static void PredictD153(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft)
    {
        ValidateDirectionalEdges(destination, stride, size, above, left, aboveLeft, Vp9PredictionMode.D153);

        var topLeft = aboveLeft!.Value;
        destination[0] = Avg2(topLeft, left[0]);
        for (var y = 1; y < size; y++)
        {
            destination[y * stride] = Avg2(left[y - 1], left[y]);
        }

        destination[1] = Avg3(left[0], topLeft, above[0]);
        destination[stride + 1] = Avg3(topLeft, left[0], left[1]);
        for (var y = 2; y < size; y++)
        {
            destination[(y * stride) + 1] = Avg3(left[y - 2], left[y - 1], left[y]);
        }

        for (var x = 0; x < size - 2; x++)
        {
            destination[2 + x] = Avg3(GetAboveWithTopLeft(above, topLeft, x - 1), above[x], above[x + 1]);
        }

        for (var y = 1; y < size; y++)
        {
            var row = destination.Slice(y * stride, size);
            var source = destination.Slice((y - 1) * stride, size);
            for (var x = 2; x < size; x++)
            {
                row[x] = source[x - 2];
            }
        }
    }

    public static void PredictD207(Span<byte> destination, int stride, int size, ReadOnlySpan<byte> left)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(left, size, nameof(left), Vp9PredictionMode.D207);

        for (var y = 0; y < size - 1; y++)
        {
            destination[y * stride] = Avg2(left[y], left[y + 1]);
        }

        destination[(size - 1) * stride] = left[size - 1];

        for (var y = 0; y < size - 2; y++)
        {
            destination[(y * stride) + 1] = Avg3(left[y], left[y + 1], left[y + 2]);
        }

        destination[((size - 2) * stride) + 1] = Avg3(left[size - 2], left[size - 1], left[size - 1]);
        destination[((size - 1) * stride) + 1] = left[size - 1];

        var lastRow = destination.Slice((size - 1) * stride, size);
        lastRow.Slice(2).Fill(left[size - 1]);

        for (var y = size - 2; y >= 0; y--)
        {
            var row = destination.Slice(y * stride, size);
            var nextRow = destination.Slice((y + 1) * stride, size);
            for (var x = 2; x < size; x++)
            {
                row[x] = nextRow[x - 2];
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

    private static void ValidateDirectionalEdges(
        ReadOnlySpan<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft,
        Vp9PredictionMode mode)
    {
        ValidateDestination(destination, stride, size);
        ValidateRequiredEdge(above, size, nameof(above), mode);
        ValidateRequiredEdge(left, size, nameof(left), mode);
        if (!aboveLeft.HasValue)
        {
            throw new NotSupportedException($"VP9 {mode} intra predictor requires an above-left sample.");
        }
    }

    private static byte Avg2(byte a, byte b)
    {
        return (byte)((a + b + 1) >> 1);
    }

    private static byte Avg3(byte a, byte b, byte c)
    {
        return (byte)((a + (2 * b) + c + 2) >> 2);
    }

    private static byte GetAbove(ReadOnlySpan<byte> above, int index)
    {
        return above[Math.Min(index, above.Length - 1)];
    }

    private static byte GetAboveWithTopLeft(ReadOnlySpan<byte> above, byte aboveLeft, int index)
    {
        return index < 0 ? aboveLeft : above[index];
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
