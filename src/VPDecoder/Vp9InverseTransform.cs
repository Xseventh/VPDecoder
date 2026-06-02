namespace VPDecoder;

internal static class Vp9InverseTransform
{
    private const int Size32 = 32;
    private const int DctConstBits = 14;
    private const int CosPi1_64 = 16364;
    private const int CosPi2_64 = 16305;
    private const int CosPi3_64 = 16207;
    private const int CosPi4_64 = 16069;
    private const int CosPi5_64 = 15893;
    private const int CosPi6_64 = 15679;
    private const int CosPi7_64 = 15426;
    private const int CosPi8_64 = 15137;
    private const int CosPi9_64 = 14811;
    private const int CosPi10_64 = 14449;
    private const int CosPi11_64 = 14053;
    private const int CosPi12_64 = 13623;
    private const int CosPi13_64 = 13160;
    private const int CosPi14_64 = 12665;
    private const int CosPi15_64 = 12140;
    private const int CosPi16_64 = 11585;
    private const int CosPi17_64 = 11003;
    private const int CosPi18_64 = 10394;
    private const int CosPi19_64 = 9760;
    private const int CosPi20_64 = 9102;
    private const int CosPi21_64 = 8423;
    private const int CosPi22_64 = 7723;
    private const int CosPi23_64 = 7005;
    private const int CosPi24_64 = 6270;
    private const int CosPi25_64 = 5520;
    private const int CosPi26_64 = 4756;
    private const int CosPi27_64 = 3981;
    private const int CosPi28_64 = 3196;
    private const int CosPi29_64 = 2404;
    private const int CosPi30_64 = 1606;
    private const int CosPi31_64 = 804;

    public static void AddBlock(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        Vp9TransformSize transformSize,
        Vp9TransformType transformType,
        ReadOnlySpan<int> coefficients,
        int eob)
    {
        if (transformSize != Vp9TransformSize.Tx32X32)
        {
            throw new NotSupportedException(
                $"VP9 inverse transform currently supports only TX32 blocks, not {transformSize}.");
        }

        if (!IsSupportedTx32TransformType(transformType))
        {
            throw new NotSupportedException(
                $"VP9 inverse transform does not recognize TX32 transform type {transformType}.");
        }

        if (eob < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eob), eob, "VP9 coefficient eob must be non-negative.");
        }

        if (stride < Size32)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "VP9 TX32 inverse transform stride must fit a 32-pixel block.");
        }

        if (x < 0 || y < 0)
        {
            throw new ArgumentOutOfRangeException(
                x < 0 ? nameof(x) : nameof(y),
                "VP9 inverse transform block origin must be non-negative.");
        }

        if (coefficients.Length < Size32 * Size32)
        {
            throw new ArgumentException("VP9 TX32 inverse transform requires 1024 coefficients.", nameof(coefficients));
        }

        if (plane.Length < ((y + Size32 - 1) * stride) + x + Size32)
        {
            throw new ArgumentException("VP9 inverse transform destination plane is too small.", nameof(plane));
        }

        if (eob == 0)
        {
            return;
        }

        if (eob == 1)
        {
            Vp9DcOnlyReconstructor.AddDcOnly(plane, stride, x, y, Size32, coefficients[0]);
            return;
        }

        if (eob > Size32 * Size32)
        {
            throw new NotSupportedException(
                $"VP9 TX32 inverse transform eob {eob} exceeds the 1024 coefficient block size.");
        }

        var rowsToTransform = Size32;
        if (eob <= 34)
        {
            ThrowIfNonZeroOutsideUpperLeft8x8(coefficients);
            rowsToTransform = 8;
        }

        AddIdct32x32(plane, stride, x, y, coefficients, rowsToTransform);
    }

    private static void AddIdct32x32(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        ReadOnlySpan<int> coefficients,
        int rowsToTransform)
    {
        Span<int> output = stackalloc int[Size32 * Size32];
        Span<int> rowInput = stackalloc int[Size32];
        Span<int> rowOutput = stackalloc int[Size32];
        for (var row = 0; row < rowsToTransform; row++)
        {
            coefficients.Slice(row * Size32, Size32).CopyTo(rowInput);
            Idct32(rowInput, rowOutput);
            rowOutput.CopyTo(output.Slice(row * Size32, Size32));
            rowInput.Clear();
        }

        Span<int> columnInput = stackalloc int[Size32];
        Span<int> columnOutput = stackalloc int[Size32];
        for (var column = 0; column < Size32; column++)
        {
            for (var row = 0; row < Size32; row++)
            {
                columnInput[row] = output[(row * Size32) + column];
            }

            Idct32(columnInput, columnOutput);
            for (var row = 0; row < Size32; row++)
            {
                var residual = RoundPowerOfTwo(columnOutput[row], 6);
                var offset = ((y + row) * stride) + x + column;
                plane[offset] = ClipPixel(plane[offset] + residual);
            }
        }
    }

    private static void Idct32(ReadOnlySpan<int> input, Span<int> output)
    {
        Span<int> step1 = stackalloc int[Size32];
        Span<int> step2 = stackalloc int[Size32];

        step1[0] = WrapLow(ToTranLow(input[0]));
        step1[1] = WrapLow(ToTranLow(input[16]));
        step1[2] = WrapLow(ToTranLow(input[8]));
        step1[3] = WrapLow(ToTranLow(input[24]));
        step1[4] = WrapLow(ToTranLow(input[4]));
        step1[5] = WrapLow(ToTranLow(input[20]));
        step1[6] = WrapLow(ToTranLow(input[12]));
        step1[7] = WrapLow(ToTranLow(input[28]));
        step1[8] = WrapLow(ToTranLow(input[2]));
        step1[9] = WrapLow(ToTranLow(input[18]));
        step1[10] = WrapLow(ToTranLow(input[10]));
        step1[11] = WrapLow(ToTranLow(input[26]));
        step1[12] = WrapLow(ToTranLow(input[6]));
        step1[13] = WrapLow(ToTranLow(input[22]));
        step1[14] = WrapLow(ToTranLow(input[14]));
        step1[15] = WrapLow(ToTranLow(input[30]));

        var temp1 = ((long)ToTranLow(input[1]) * CosPi31_64) - ((long)ToTranLow(input[31]) * CosPi1_64);
        var temp2 = ((long)ToTranLow(input[1]) * CosPi1_64) + ((long)ToTranLow(input[31]) * CosPi31_64);
        step1[16] = WrapLow(DctConstRoundShift(temp1));
        step1[31] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[17]) * CosPi15_64) - ((long)ToTranLow(input[15]) * CosPi17_64);
        temp2 = ((long)ToTranLow(input[17]) * CosPi17_64) + ((long)ToTranLow(input[15]) * CosPi15_64);
        step1[17] = WrapLow(DctConstRoundShift(temp1));
        step1[30] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[9]) * CosPi23_64) - ((long)ToTranLow(input[23]) * CosPi9_64);
        temp2 = ((long)ToTranLow(input[9]) * CosPi9_64) + ((long)ToTranLow(input[23]) * CosPi23_64);
        step1[18] = WrapLow(DctConstRoundShift(temp1));
        step1[29] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[25]) * CosPi7_64) - ((long)ToTranLow(input[7]) * CosPi25_64);
        temp2 = ((long)ToTranLow(input[25]) * CosPi25_64) + ((long)ToTranLow(input[7]) * CosPi7_64);
        step1[19] = WrapLow(DctConstRoundShift(temp1));
        step1[28] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[5]) * CosPi27_64) - ((long)ToTranLow(input[27]) * CosPi5_64);
        temp2 = ((long)ToTranLow(input[5]) * CosPi5_64) + ((long)ToTranLow(input[27]) * CosPi27_64);
        step1[20] = WrapLow(DctConstRoundShift(temp1));
        step1[27] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[21]) * CosPi11_64) - ((long)ToTranLow(input[11]) * CosPi21_64);
        temp2 = ((long)ToTranLow(input[21]) * CosPi21_64) + ((long)ToTranLow(input[11]) * CosPi11_64);
        step1[21] = WrapLow(DctConstRoundShift(temp1));
        step1[26] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[13]) * CosPi19_64) - ((long)ToTranLow(input[19]) * CosPi13_64);
        temp2 = ((long)ToTranLow(input[13]) * CosPi13_64) + ((long)ToTranLow(input[19]) * CosPi19_64);
        step1[22] = WrapLow(DctConstRoundShift(temp1));
        step1[25] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)ToTranLow(input[29]) * CosPi3_64) - ((long)ToTranLow(input[3]) * CosPi29_64);
        temp2 = ((long)ToTranLow(input[29]) * CosPi29_64) + ((long)ToTranLow(input[3]) * CosPi3_64);
        step1[23] = WrapLow(DctConstRoundShift(temp1));
        step1[24] = WrapLow(DctConstRoundShift(temp2));

        step2[0] = step1[0];
        step2[1] = step1[1];
        step2[2] = step1[2];
        step2[3] = step1[3];
        step2[4] = step1[4];
        step2[5] = step1[5];
        step2[6] = step1[6];
        step2[7] = step1[7];

        temp1 = ((long)step1[8] * CosPi30_64) - ((long)step1[15] * CosPi2_64);
        temp2 = ((long)step1[8] * CosPi2_64) + ((long)step1[15] * CosPi30_64);
        step2[8] = WrapLow(DctConstRoundShift(temp1));
        step2[15] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)step1[9] * CosPi14_64) - ((long)step1[14] * CosPi18_64);
        temp2 = ((long)step1[9] * CosPi18_64) + ((long)step1[14] * CosPi14_64);
        step2[9] = WrapLow(DctConstRoundShift(temp1));
        step2[14] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)step1[10] * CosPi22_64) - ((long)step1[13] * CosPi10_64);
        temp2 = ((long)step1[10] * CosPi10_64) + ((long)step1[13] * CosPi22_64);
        step2[10] = WrapLow(DctConstRoundShift(temp1));
        step2[13] = WrapLow(DctConstRoundShift(temp2));

        temp1 = ((long)step1[11] * CosPi6_64) - ((long)step1[12] * CosPi26_64);
        temp2 = ((long)step1[11] * CosPi26_64) + ((long)step1[12] * CosPi6_64);
        step2[11] = WrapLow(DctConstRoundShift(temp1));
        step2[12] = WrapLow(DctConstRoundShift(temp2));

        step2[16] = WrapLow(step1[16] + step1[17]);
        step2[17] = WrapLow(step1[16] - step1[17]);
        step2[18] = WrapLow(-step1[18] + step1[19]);
        step2[19] = WrapLow(step1[18] + step1[19]);
        step2[20] = WrapLow(step1[20] + step1[21]);
        step2[21] = WrapLow(step1[20] - step1[21]);
        step2[22] = WrapLow(-step1[22] + step1[23]);
        step2[23] = WrapLow(step1[22] + step1[23]);
        step2[24] = WrapLow(step1[24] + step1[25]);
        step2[25] = WrapLow(step1[24] - step1[25]);
        step2[26] = WrapLow(-step1[26] + step1[27]);
        step2[27] = WrapLow(step1[26] + step1[27]);
        step2[28] = WrapLow(step1[28] + step1[29]);
        step2[29] = WrapLow(step1[28] - step1[29]);
        step2[30] = WrapLow(-step1[30] + step1[31]);
        step2[31] = WrapLow(step1[30] + step1[31]);

        step1[0] = step2[0];
        step1[1] = step2[1];
        step1[2] = step2[2];
        step1[3] = step2[3];

        temp1 = ((long)step2[4] * CosPi28_64) - ((long)step2[7] * CosPi4_64);
        temp2 = ((long)step2[4] * CosPi4_64) + ((long)step2[7] * CosPi28_64);
        step1[4] = WrapLow(DctConstRoundShift(temp1));
        step1[7] = WrapLow(DctConstRoundShift(temp2));
        temp1 = ((long)step2[5] * CosPi12_64) - ((long)step2[6] * CosPi20_64);
        temp2 = ((long)step2[5] * CosPi20_64) + ((long)step2[6] * CosPi12_64);
        step1[5] = WrapLow(DctConstRoundShift(temp1));
        step1[6] = WrapLow(DctConstRoundShift(temp2));

        step1[8] = WrapLow(step2[8] + step2[9]);
        step1[9] = WrapLow(step2[8] - step2[9]);
        step1[10] = WrapLow(-step2[10] + step2[11]);
        step1[11] = WrapLow(step2[10] + step2[11]);
        step1[12] = WrapLow(step2[12] + step2[13]);
        step1[13] = WrapLow(step2[12] - step2[13]);
        step1[14] = WrapLow(-step2[14] + step2[15]);
        step1[15] = WrapLow(step2[14] + step2[15]);

        step1[16] = step2[16];
        step1[31] = step2[31];
        temp1 = (-(long)step2[17] * CosPi4_64) + ((long)step2[30] * CosPi28_64);
        temp2 = ((long)step2[17] * CosPi28_64) + ((long)step2[30] * CosPi4_64);
        step1[17] = WrapLow(DctConstRoundShift(temp1));
        step1[30] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (-(long)step2[18] * CosPi28_64) - ((long)step2[29] * CosPi4_64);
        temp2 = (-(long)step2[18] * CosPi4_64) + ((long)step2[29] * CosPi28_64);
        step1[18] = WrapLow(DctConstRoundShift(temp1));
        step1[29] = WrapLow(DctConstRoundShift(temp2));
        step1[19] = step2[19];
        step1[20] = step2[20];
        temp1 = (-(long)step2[21] * CosPi20_64) + ((long)step2[26] * CosPi12_64);
        temp2 = ((long)step2[21] * CosPi12_64) + ((long)step2[26] * CosPi20_64);
        step1[21] = WrapLow(DctConstRoundShift(temp1));
        step1[26] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (-(long)step2[22] * CosPi12_64) - ((long)step2[25] * CosPi20_64);
        temp2 = (-(long)step2[22] * CosPi20_64) + ((long)step2[25] * CosPi12_64);
        step1[22] = WrapLow(DctConstRoundShift(temp1));
        step1[25] = WrapLow(DctConstRoundShift(temp2));
        step1[23] = step2[23];
        step1[24] = step2[24];
        step1[27] = step2[27];
        step1[28] = step2[28];

        temp1 = (long)(step1[0] + step1[1]) * CosPi16_64;
        temp2 = (long)(step1[0] - step1[1]) * CosPi16_64;
        step2[0] = WrapLow(DctConstRoundShift(temp1));
        step2[1] = WrapLow(DctConstRoundShift(temp2));
        temp1 = ((long)step1[2] * CosPi24_64) - ((long)step1[3] * CosPi8_64);
        temp2 = ((long)step1[2] * CosPi8_64) + ((long)step1[3] * CosPi24_64);
        step2[2] = WrapLow(DctConstRoundShift(temp1));
        step2[3] = WrapLow(DctConstRoundShift(temp2));
        step2[4] = WrapLow(step1[4] + step1[5]);
        step2[5] = WrapLow(step1[4] - step1[5]);
        step2[6] = WrapLow(-step1[6] + step1[7]);
        step2[7] = WrapLow(step1[6] + step1[7]);

        step2[8] = step1[8];
        step2[15] = step1[15];
        temp1 = (-(long)step1[9] * CosPi8_64) + ((long)step1[14] * CosPi24_64);
        temp2 = ((long)step1[9] * CosPi24_64) + ((long)step1[14] * CosPi8_64);
        step2[9] = WrapLow(DctConstRoundShift(temp1));
        step2[14] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (-(long)step1[10] * CosPi24_64) - ((long)step1[13] * CosPi8_64);
        temp2 = (-(long)step1[10] * CosPi8_64) + ((long)step1[13] * CosPi24_64);
        step2[10] = WrapLow(DctConstRoundShift(temp1));
        step2[13] = WrapLow(DctConstRoundShift(temp2));
        step2[11] = step1[11];
        step2[12] = step1[12];

        step2[16] = WrapLow(step1[16] + step1[19]);
        step2[17] = WrapLow(step1[17] + step1[18]);
        step2[18] = WrapLow(step1[17] - step1[18]);
        step2[19] = WrapLow(step1[16] - step1[19]);
        step2[20] = WrapLow(-step1[20] + step1[23]);
        step2[21] = WrapLow(-step1[21] + step1[22]);
        step2[22] = WrapLow(step1[21] + step1[22]);
        step2[23] = WrapLow(step1[20] + step1[23]);

        step2[24] = WrapLow(step1[24] + step1[27]);
        step2[25] = WrapLow(step1[25] + step1[26]);
        step2[26] = WrapLow(step1[25] - step1[26]);
        step2[27] = WrapLow(step1[24] - step1[27]);
        step2[28] = WrapLow(-step1[28] + step1[31]);
        step2[29] = WrapLow(-step1[29] + step1[30]);
        step2[30] = WrapLow(step1[29] + step1[30]);
        step2[31] = WrapLow(step1[28] + step1[31]);

        step1[0] = WrapLow(step2[0] + step2[3]);
        step1[1] = WrapLow(step2[1] + step2[2]);
        step1[2] = WrapLow(step2[1] - step2[2]);
        step1[3] = WrapLow(step2[0] - step2[3]);
        step1[4] = step2[4];
        temp1 = (long)(step2[6] - step2[5]) * CosPi16_64;
        temp2 = (long)(step2[5] + step2[6]) * CosPi16_64;
        step1[5] = WrapLow(DctConstRoundShift(temp1));
        step1[6] = WrapLow(DctConstRoundShift(temp2));
        step1[7] = step2[7];

        step1[8] = WrapLow(step2[8] + step2[11]);
        step1[9] = WrapLow(step2[9] + step2[10]);
        step1[10] = WrapLow(step2[9] - step2[10]);
        step1[11] = WrapLow(step2[8] - step2[11]);
        step1[12] = WrapLow(-step2[12] + step2[15]);
        step1[13] = WrapLow(-step2[13] + step2[14]);
        step1[14] = WrapLow(step2[13] + step2[14]);
        step1[15] = WrapLow(step2[12] + step2[15]);

        step1[16] = step2[16];
        step1[17] = step2[17];
        temp1 = (-(long)step2[18] * CosPi8_64) + ((long)step2[29] * CosPi24_64);
        temp2 = ((long)step2[18] * CosPi24_64) + ((long)step2[29] * CosPi8_64);
        step1[18] = WrapLow(DctConstRoundShift(temp1));
        step1[29] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (-(long)step2[19] * CosPi8_64) + ((long)step2[28] * CosPi24_64);
        temp2 = ((long)step2[19] * CosPi24_64) + ((long)step2[28] * CosPi8_64);
        step1[19] = WrapLow(DctConstRoundShift(temp1));
        step1[28] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (-(long)step2[20] * CosPi24_64) - ((long)step2[27] * CosPi8_64);
        temp2 = (-(long)step2[20] * CosPi8_64) + ((long)step2[27] * CosPi24_64);
        step1[20] = WrapLow(DctConstRoundShift(temp1));
        step1[27] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (-(long)step2[21] * CosPi24_64) - ((long)step2[26] * CosPi8_64);
        temp2 = (-(long)step2[21] * CosPi8_64) + ((long)step2[26] * CosPi24_64);
        step1[21] = WrapLow(DctConstRoundShift(temp1));
        step1[26] = WrapLow(DctConstRoundShift(temp2));
        step1[22] = step2[22];
        step1[23] = step2[23];
        step1[24] = step2[24];
        step1[25] = step2[25];
        step1[30] = step2[30];
        step1[31] = step2[31];

        step2[0] = WrapLow(step1[0] + step1[7]);
        step2[1] = WrapLow(step1[1] + step1[6]);
        step2[2] = WrapLow(step1[2] + step1[5]);
        step2[3] = WrapLow(step1[3] + step1[4]);
        step2[4] = WrapLow(step1[3] - step1[4]);
        step2[5] = WrapLow(step1[2] - step1[5]);
        step2[6] = WrapLow(step1[1] - step1[6]);
        step2[7] = WrapLow(step1[0] - step1[7]);
        step2[8] = step1[8];
        step2[9] = step1[9];
        temp1 = (long)(-step1[10] + step1[13]) * CosPi16_64;
        temp2 = (long)(step1[10] + step1[13]) * CosPi16_64;
        step2[10] = WrapLow(DctConstRoundShift(temp1));
        step2[13] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (long)(-step1[11] + step1[12]) * CosPi16_64;
        temp2 = (long)(step1[11] + step1[12]) * CosPi16_64;
        step2[11] = WrapLow(DctConstRoundShift(temp1));
        step2[12] = WrapLow(DctConstRoundShift(temp2));
        step2[14] = step1[14];
        step2[15] = step1[15];

        step2[16] = WrapLow(step1[16] + step1[23]);
        step2[17] = WrapLow(step1[17] + step1[22]);
        step2[18] = WrapLow(step1[18] + step1[21]);
        step2[19] = WrapLow(step1[19] + step1[20]);
        step2[20] = WrapLow(step1[19] - step1[20]);
        step2[21] = WrapLow(step1[18] - step1[21]);
        step2[22] = WrapLow(step1[17] - step1[22]);
        step2[23] = WrapLow(step1[16] - step1[23]);

        step2[24] = WrapLow(-step1[24] + step1[31]);
        step2[25] = WrapLow(-step1[25] + step1[30]);
        step2[26] = WrapLow(-step1[26] + step1[29]);
        step2[27] = WrapLow(-step1[27] + step1[28]);
        step2[28] = WrapLow(step1[27] + step1[28]);
        step2[29] = WrapLow(step1[26] + step1[29]);
        step2[30] = WrapLow(step1[25] + step1[30]);
        step2[31] = WrapLow(step1[24] + step1[31]);

        step1[0] = WrapLow(step2[0] + step2[15]);
        step1[1] = WrapLow(step2[1] + step2[14]);
        step1[2] = WrapLow(step2[2] + step2[13]);
        step1[3] = WrapLow(step2[3] + step2[12]);
        step1[4] = WrapLow(step2[4] + step2[11]);
        step1[5] = WrapLow(step2[5] + step2[10]);
        step1[6] = WrapLow(step2[6] + step2[9]);
        step1[7] = WrapLow(step2[7] + step2[8]);
        step1[8] = WrapLow(step2[7] - step2[8]);
        step1[9] = WrapLow(step2[6] - step2[9]);
        step1[10] = WrapLow(step2[5] - step2[10]);
        step1[11] = WrapLow(step2[4] - step2[11]);
        step1[12] = WrapLow(step2[3] - step2[12]);
        step1[13] = WrapLow(step2[2] - step2[13]);
        step1[14] = WrapLow(step2[1] - step2[14]);
        step1[15] = WrapLow(step2[0] - step2[15]);

        step1[16] = step2[16];
        step1[17] = step2[17];
        step1[18] = step2[18];
        step1[19] = step2[19];
        temp1 = (long)(-step2[20] + step2[27]) * CosPi16_64;
        temp2 = (long)(step2[20] + step2[27]) * CosPi16_64;
        step1[20] = WrapLow(DctConstRoundShift(temp1));
        step1[27] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (long)(-step2[21] + step2[26]) * CosPi16_64;
        temp2 = (long)(step2[21] + step2[26]) * CosPi16_64;
        step1[21] = WrapLow(DctConstRoundShift(temp1));
        step1[26] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (long)(-step2[22] + step2[25]) * CosPi16_64;
        temp2 = (long)(step2[22] + step2[25]) * CosPi16_64;
        step1[22] = WrapLow(DctConstRoundShift(temp1));
        step1[25] = WrapLow(DctConstRoundShift(temp2));
        temp1 = (long)(-step2[23] + step2[24]) * CosPi16_64;
        temp2 = (long)(step2[23] + step2[24]) * CosPi16_64;
        step1[23] = WrapLow(DctConstRoundShift(temp1));
        step1[24] = WrapLow(DctConstRoundShift(temp2));
        step1[28] = step2[28];
        step1[29] = step2[29];
        step1[30] = step2[30];
        step1[31] = step2[31];

        output[0] = WrapLow(step1[0] + step1[31]);
        output[1] = WrapLow(step1[1] + step1[30]);
        output[2] = WrapLow(step1[2] + step1[29]);
        output[3] = WrapLow(step1[3] + step1[28]);
        output[4] = WrapLow(step1[4] + step1[27]);
        output[5] = WrapLow(step1[5] + step1[26]);
        output[6] = WrapLow(step1[6] + step1[25]);
        output[7] = WrapLow(step1[7] + step1[24]);
        output[8] = WrapLow(step1[8] + step1[23]);
        output[9] = WrapLow(step1[9] + step1[22]);
        output[10] = WrapLow(step1[10] + step1[21]);
        output[11] = WrapLow(step1[11] + step1[20]);
        output[12] = WrapLow(step1[12] + step1[19]);
        output[13] = WrapLow(step1[13] + step1[18]);
        output[14] = WrapLow(step1[14] + step1[17]);
        output[15] = WrapLow(step1[15] + step1[16]);
        output[16] = WrapLow(step1[15] - step1[16]);
        output[17] = WrapLow(step1[14] - step1[17]);
        output[18] = WrapLow(step1[13] - step1[18]);
        output[19] = WrapLow(step1[12] - step1[19]);
        output[20] = WrapLow(step1[11] - step1[20]);
        output[21] = WrapLow(step1[10] - step1[21]);
        output[22] = WrapLow(step1[9] - step1[22]);
        output[23] = WrapLow(step1[8] - step1[23]);
        output[24] = WrapLow(step1[7] - step1[24]);
        output[25] = WrapLow(step1[6] - step1[25]);
        output[26] = WrapLow(step1[5] - step1[26]);
        output[27] = WrapLow(step1[4] - step1[27]);
        output[28] = WrapLow(step1[3] - step1[28]);
        output[29] = WrapLow(step1[2] - step1[29]);
        output[30] = WrapLow(step1[1] - step1[30]);
        output[31] = WrapLow(step1[0] - step1[31]);
    }

    private static void ThrowIfNonZeroOutsideUpperLeft8x8(ReadOnlySpan<int> coefficients)
    {
        for (var row = 0; row < Size32; row++)
        {
            for (var column = 0; column < Size32; column++)
            {
                if ((row >= 8 || column >= 8) && coefficients[(row * Size32) + column] != 0)
                {
                    throw new NotSupportedException(
                        "VP9 TX32 eob <= 34 inverse transform requires non-zero coefficients to fit in the upper-left 8x8 region.");
                }
            }
        }
    }

    private static bool IsSupportedTx32TransformType(Vp9TransformType transformType)
    {
        return transformType is
            Vp9TransformType.DctDct or
            Vp9TransformType.AdstDct or
            Vp9TransformType.DctAdst or
            Vp9TransformType.AdstAdst;
    }

    private static int ToTranLow(int value)
    {
        if (value is < short.MinValue or > short.MaxValue)
        {
            throw new NotSupportedException(
                $"VP9 8-bit inverse transform coefficient {value} exceeds the supported signed 16-bit transform range.");
        }

        return value;
    }

    private static int WrapLow(int value)
    {
        if (value is < short.MinValue or > short.MaxValue)
        {
            throw new NotSupportedException(
                $"VP9 8-bit inverse transform intermediate {value} exceeds the supported signed 16-bit range.");
        }

        return value;
    }

    private static int DctConstRoundShift(long value)
    {
        return RoundPowerOfTwo(value, DctConstBits);
    }

    private static int RoundPowerOfTwo(long value, int bitCount)
    {
        return (int)((value + (1L << (bitCount - 1))) >> bitCount);
    }

    private static byte ClipPixel(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}
