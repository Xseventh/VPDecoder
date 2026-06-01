namespace VPDecoder;

internal static class Vp9IntraPredictor
{
    public static void PredictDc(
        Span<byte> destination,
        int stride,
        int size,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left)
    {
        if (destination.Length < ((size - 1) * stride) + size)
        {
            throw new ArgumentException("VP9 predictor destination is too small.", nameof(destination));
        }

        if (!above.IsEmpty && above.Length < size)
        {
            throw new ArgumentException("VP9 above predictor span is too small.", nameof(above));
        }

        if (!left.IsEmpty && left.Length < size)
        {
            throw new ArgumentException("VP9 left predictor span is too small.", nameof(left));
        }

        var value = GetDcValue(size, above, left);
        for (var y = 0; y < size; y++)
        {
            destination.Slice(y * stride, size).Fill(value);
        }
    }

    public static byte GetDcValue(int size, ReadOnlySpan<byte> above, ReadOnlySpan<byte> left)
    {
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
