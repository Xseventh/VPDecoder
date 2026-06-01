namespace VPDecoder;

internal static class Vp9DcOnlyReconstructor
{
    private const int CosPi16_64 = 11585;
    private const int DctConstBits = 14;

    public static void AddDcOnly(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        int size,
        int dequantizedDc)
    {
        var residual = GetDcResidual(size, dequantizedDc);
        for (var row = 0; row < size; row++)
        {
            var offset = ((y + row) * stride) + x;
            for (var column = 0; column < size; column++)
            {
                plane[offset + column] = ClipPixel(plane[offset + column] + residual);
            }
        }
    }

    public static int GetDcResidual(int size, int dequantizedDc)
    {
        var out0 = DctConstRoundShift((long)dequantizedDc * CosPi16_64);
        var out1 = DctConstRoundShift(out0 * CosPi16_64);

        return (int)RoundPowerOfTwo(out1, GetFinalShift(size));
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }

    private static int GetFinalShift(int value)
    {
        return value switch
        {
            4 => 4,
            8 => 5,
            16 => 6,
            32 => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "VP9 transform size must be 4, 8, 16, or 32.")
        };
    }

    private static long DctConstRoundShift(long value)
    {
        return RoundPowerOfTwo(value, DctConstBits);
    }

    private static long RoundPowerOfTwo(long value, int bitCount)
    {
        return (value + (1L << (bitCount - 1))) >> bitCount;
    }
}
