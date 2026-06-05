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

    [Fact]
    public void AddBlock_ForTx32FullPath_UsesCoefficientsOutsideUpperLeft8x8()
    {
        var coefficients = new int[1024];
        coefficients[0] = 1300;
        coefficients[9 * 32] = 180;
        coefficients[(17 * 32) + 11] = -240;
        coefficients[(31 * 32) + 31] = 95;
        var first = Enumerable.Repeat((byte)96, 32 * 32).ToArray();
        var second = first.ToArray();

        Vp9InverseTransform.AddBlock(
            first,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 512);
        Vp9InverseTransform.AddBlock(
            second,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 512);

        Assert.Equal(Hash(first), Hash(second));
        Assert.Equal("2d7d2110450b69a000ba0c7ac6b919ab2b263ddfee3da724568fc4176a13df70", Hash(first));
        Assert.True(first.Distinct().Count() > 1);
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

    [Theory]
    [InlineData(Vp9TransformSize.Tx4X4, Vp9TransformType.AdstAdst, 14, "5a0c3297895f2039590942b1f4267e6caa5ca52881b92488da71da6912f548c6")]
    [InlineData(Vp9TransformSize.Tx8X8, Vp9TransformType.AdstDct, 3, "c19ae82d2385a0f4629d318c565c47629cd12cfc4d167170e72a8daf55ab375d")]
    [InlineData(Vp9TransformSize.Tx16X16, Vp9TransformType.DctAdst, 2, "f21d2153c41150736efaac946a981452e4b4fcf7e743b671d3afff072b7a359b")]
    public void AddBlock_ForHybridTransforms_IsDeterministic(
        Vp9TransformSize transformSize,
        Vp9TransformType transformType,
        int eob,
        string expectedHash)
    {
        var side = transformSize switch
        {
            Vp9TransformSize.Tx4X4 => 4,
            Vp9TransformSize.Tx8X8 => 8,
            Vp9TransformSize.Tx16X16 => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(transformSize))
        };
        var coefficients = new int[side * side];
        coefficients[0] = 800;
        coefficients[1] = -120;
        coefficients[Math.Min(coefficients.Length - 1, 5)] = 95;
        coefficients[^1] = -40;
        var first = Enumerable.Repeat((byte)128, side * side).ToArray();
        var second = first.ToArray();

        Vp9InverseTransform.AddBlock(
            first,
            stride: side,
            x: 0,
            y: 0,
            transformSize,
            transformType,
            coefficients,
            eob);
        Vp9InverseTransform.AddBlock(
            second,
            stride: side,
            x: 0,
            y: 0,
            transformSize,
            transformType,
            coefficients,
            eob);

        Assert.Equal(Hash(first), Hash(second));
        Assert.Equal(expectedHash, Hash(first));
        Assert.True(first.Distinct().Count() > 1);
    }

    [Fact]
    public void AddBlock_ForTx4DctAdst_MatchesLibvpxDirectionalReferenceBlock()
    {
        int[] coefficients =
        [
            350, 580, 116, -232,
            -464, -754, -174, 290,
            348, 580, 116, -232,
            -174, -348, -58, 116
        ];
        byte[] plane =
        [
            0, 0, 0, 0,
            0, 0, 0, 0,
            2, 1, 0, 0,
            107, 54, 2, 1
        ];
        byte[] expected =
        [
            0, 1, 0, 0,
            1, 1, 0, 0,
            1, 0, 0, 3,
            207, 206, 2, 0
        ];

        Vp9InverseTransform.AddBlock(
            plane,
            stride: 4,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx4X4,
            Vp9TransformType.DctAdst,
            coefficients,
            eob: 16);

        Assert.Equal(expected, plane);
    }

    [Fact]
    public void AddBlock_ForUnsupportedPaths_ReturnsSpecificExceptions()
    {
        var coefficients = new int[1024];
        var plane = new byte[32 * 32];

        var type = Assert.Throws<NotSupportedException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            (Vp9TransformType)99,
            coefficients,
            eob: 1));
        Assert.Contains("does not recognize transform type", type.Message);

        var eob = Assert.Throws<NotSupportedException>(() => Vp9InverseTransform.AddBlock(
            plane,
            stride: 32,
            x: 0,
            y: 0,
            Vp9TransformSize.Tx32X32,
            Vp9TransformType.DctDct,
            coefficients,
            eob: 1025));
        Assert.Contains("exceeds the 1024 coefficient block size", eob.Message);
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
