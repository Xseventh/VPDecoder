namespace VPDecoder.Tests;

public sealed class Vp9InverseTransformTests
{
    [Theory]
    [InlineData(16625)]
    [InlineData(200)]
    [InlineData(-16380)]
    public void AddBlock_ForTx32DcOnly_MatchesDcOnlyReconstructor(int dc)
    {
        var coefficients = new int[1024];
        coefficients[0] = dc;
        var plane = Enumerable.Repeat((byte)128, 32 * 32).ToArray();

        Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 1);

        var expected = (byte)Math.Clamp(128 + Vp9DcOnlyReconstructor.GetDcResidual(32, dc), 0, 255);
        Assert.All(plane, pixel => Assert.Equal(expected, pixel));
    }

    [Fact]
    public void AddBlock_ForTx32Eob34Path_IsDeterministicAndAddsAcVariation()
    {
        var coefficients = new int[1024];
        coefficients[0] = 16625;
        coefficients[1] = 420;
        coefficients[32] = -315;
        coefficients[33] = 210;
        var first = Enumerable.Repeat((byte)128, 40 * 38).ToArray();
        var second = first.ToArray();

        Vp9InverseTransform.AddBlock(
            first,
            stride: 40,
            x: 4,
            y: 3,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 4);
        Vp9InverseTransform.AddBlock(
            second,
            stride: 40,
            x: 4,
            y: 3,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 4);

        Assert.Equal(Hash(first), Hash(second));
        Assert.Equal("cf00752d381318bec0009f568446a2f718939b13f4ecd59b34da004d204d649d", Hash(first));
        var transformedPixels = ReadBlock(first, stride: 40, x: 4, y: 3, size: 32);
        Assert.True(transformedPixels.Distinct().Count() > 1);
    }

    [Theory]
    [InlineData(Vp9TransformType.AdstDct)]
    [InlineData(Vp9TransformType.DctAdst)]
    [InlineData(Vp9TransformType.AdstAdst)]
    public void AddBlock_ForTx32NonDctTransformTypes_MatchesDctDct(Vp9TransformType transformType)
    {
        var coefficients = new int[1024];
        coefficients[0] = 16625;
        coefficients[1] = 420;
        coefficients[32] = -315;
        coefficients[33] = 210;
        var baseline = Enumerable.Repeat((byte)128, 32 * 32).ToArray();
        var variant = baseline.ToArray();

        Vp9InverseTransform.AddBlock(
            baseline,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 4);
        Vp9InverseTransform.AddBlock(
            variant,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            transformType,
            coefficients,
            eob: 4);

        Assert.Equal(Hash(baseline), Hash(variant));
    }

    [Fact]
    public void AddBlock_ForUnsupportedPaths_ReturnsSpecificExceptions()
    {
        var coefficients = new int[1024];
        var plane = new byte[32 * 32];

        var size = Assert.Throws<NotSupportedException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx16X16,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 1));
        Assert.Contains("supports only TX32", size.Message);

        var type = Assert.Throws<NotSupportedException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            (Vp9TransformType)99,
            coefficients,
            eob: 1));
        Assert.Contains("does not recognize TX32 transform type", type.Message);

        var eob = Assert.Throws<NotSupportedException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 35));
        Assert.Contains("eob <= 34", eob.Message);
    }

    [Fact]
    public void AddBlock_ForInvalidGeometry_ReturnsArgumentDiagnostics()
    {
        var coefficients = new int[1024];
        var plane = new byte[32 * 32];

        var stride = Assert.Throws<ArgumentOutOfRangeException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 31,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 1));
        Assert.Contains("stride must fit", stride.Message);

        var origin = Assert.Throws<ArgumentOutOfRangeException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: -1,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 1));
        Assert.Contains("origin must be non-negative", origin.Message);

        var destination = Assert.Throws<ArgumentException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 1,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 1));
        Assert.Contains("destination plane is too small", destination.Message);
    }

    private static byte[] ReadBlock(byte[] plane, int stride, int x, int y, int size)
    {
        var block = new byte[size * size];
        for (var row = 0; row < size; row++)
        {
            Array.Copy(plane, ((y + row) * stride) + x, block, row * size, size);
        }

        return block;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
