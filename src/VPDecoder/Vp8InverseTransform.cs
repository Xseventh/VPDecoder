namespace VPDecoder;

internal static class Vp8InverseTransform
{
    private const int CosPi8Sqrt2Minus1 = 20_091;
    private const int SinPi8Sqrt2 = 35_468;

    public static void AddBlock(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        ReadOnlySpan<int> dequantizedCoefficients)
    {
        if (dequantizedCoefficients.Length < 16)
        {
            throw new ArgumentException("VP8 inverse transform requires 16 dequantized coefficients.", nameof(dequantizedCoefficients));
        }

        ValidateDestination(plane, stride, x, y);

        Span<int> temp = stackalloc int[16];
        IdctColumns(dequantizedCoefficients, temp);
        for (var row = 0; row < 4; row++)
        {
            var rowOffset = row * 4;
            var a1 = temp[rowOffset] + temp[rowOffset + 2];
            var b1 = temp[rowOffset] - temp[rowOffset + 2];
            var temp1 = temp[rowOffset + 1] * SinPi8Sqrt2 >> 16;
            var temp2 = temp[rowOffset + 3] + ((temp[rowOffset + 3] * CosPi8Sqrt2Minus1) >> 16);
            var c1 = temp1 - temp2;
            temp1 = temp[rowOffset + 1] + ((temp[rowOffset + 1] * CosPi8Sqrt2Minus1) >> 16);
            temp2 = temp[rowOffset + 3] * SinPi8Sqrt2 >> 16;
            var d1 = temp1 + temp2;

            var outputOffset = ((y + row) * stride) + x;
            plane[outputOffset] = ClipPixel(plane[outputOffset] + ((a1 + d1 + 4) >> 3));
            plane[outputOffset + 3] = ClipPixel(plane[outputOffset + 3] + ((a1 - d1 + 4) >> 3));
            plane[outputOffset + 1] = ClipPixel(plane[outputOffset + 1] + ((b1 + c1 + 4) >> 3));
            plane[outputOffset + 2] = ClipPixel(plane[outputOffset + 2] + ((b1 - c1 + 4) >> 3));
        }
    }

    private static void IdctColumns(ReadOnlySpan<int> input, Span<int> output)
    {
        for (var column = 0; column < 4; column++)
        {
            var a1 = input[column] + input[column + 8];
            var b1 = input[column] - input[column + 8];
            var temp1 = input[column + 4] * SinPi8Sqrt2 >> 16;
            var temp2 = input[column + 12] + ((input[column + 12] * CosPi8Sqrt2Minus1) >> 16);
            var c1 = temp1 - temp2;
            temp1 = input[column + 4] + ((input[column + 4] * CosPi8Sqrt2Minus1) >> 16);
            temp2 = input[column + 12] * SinPi8Sqrt2 >> 16;
            var d1 = temp1 + temp2;

            output[column] = a1 + d1;
            output[column + 12] = a1 - d1;
            output[column + 4] = b1 + c1;
            output[column + 8] = b1 - c1;
        }
    }

    private static void ValidateDestination(Span<byte> plane, int stride, int x, int y)
    {
        if (stride < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "VP8 inverse transform stride must fit a 4x4 block.");
        }

        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (x > stride - 4)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "VP8 inverse transform block exceeds the plane stride.");
        }

        var lastOffset = checked(((y + 3) * stride) + x + 4);
        if (lastOffset > plane.Length)
        {
            throw new ArgumentException("VP8 inverse transform block exceeds the plane buffer.", nameof(plane));
        }
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}
