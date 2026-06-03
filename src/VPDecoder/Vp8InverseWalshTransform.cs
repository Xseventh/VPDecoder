namespace VPDecoder;

internal static class Vp8InverseWalshTransform
{
    public static void Transform(
        ReadOnlySpan<int> dequantizedCoefficients,
        Span<int> output)
    {
        if (dequantizedCoefficients.Length < 16)
        {
            throw new ArgumentException("VP8 inverse Walsh transform requires 16 dequantized coefficients.", nameof(dequantizedCoefficients));
        }

        if (output.Length < 16)
        {
            throw new ArgumentException("VP8 inverse Walsh transform requires a 16-value output buffer.", nameof(output));
        }

        Span<int> temp = stackalloc int[16];
        for (var column = 0; column < 4; column++)
        {
            var a1 = dequantizedCoefficients[column] + dequantizedCoefficients[column + 12];
            var b1 = dequantizedCoefficients[column + 4] + dequantizedCoefficients[column + 8];
            var c1 = dequantizedCoefficients[column + 4] - dequantizedCoefficients[column + 8];
            var d1 = dequantizedCoefficients[column] - dequantizedCoefficients[column + 12];

            temp[column] = a1 + b1;
            temp[column + 4] = c1 + d1;
            temp[column + 8] = a1 - b1;
            temp[column + 12] = d1 - c1;
        }

        for (var row = 0; row < 4; row++)
        {
            var offset = row * 4;
            var a1 = temp[offset] + temp[offset + 3];
            var b1 = temp[offset + 1] + temp[offset + 2];
            var c1 = temp[offset + 1] - temp[offset + 2];
            var d1 = temp[offset] - temp[offset + 3];

            output[offset] = (a1 + b1 + 3) >> 3;
            output[offset + 1] = (c1 + d1 + 3) >> 3;
            output[offset + 2] = (a1 - b1 + 3) >> 3;
            output[offset + 3] = (d1 - c1 + 3) >> 3;
        }
    }
}
