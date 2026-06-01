namespace VPDecoder;

internal static class Vp9DcOnlyReconstructor
{
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
        var rounding = size switch
        {
            4 => 8,
            8 => 16,
            16 => 32,
            32 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(size), "VP9 transform size must be 4, 8, 16, or 32.")
        };

        return (dequantizedDc + rounding) >> Log2(size);
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }

    private static int Log2(int value)
    {
        return value switch
        {
            4 => 4,
            8 => 5,
            16 => 6,
            32 => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "VP9 transform size must be 4, 8, 16, or 32.")
        };
    }
}
