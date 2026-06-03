namespace VPDecoder;

internal static class Vp8DcOnlyReconstructor
{
    public static void AddDcOnly(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        int dequantizedDc)
    {
        var residual = GetDcResidual(dequantizedDc);
        for (var row = 0; row < 4; row++)
        {
            var offset = ((y + row) * stride) + x;
            for (var column = 0; column < 4; column++)
            {
                plane[offset + column] = ClipPixel(plane[offset + column] + residual);
            }
        }
    }

    public static int GetDcResidual(int dequantizedDc)
    {
        return (dequantizedDc + 4) >> 3;
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}
