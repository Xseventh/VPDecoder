namespace VPDecoder.Tests;

public sealed class Vp8InverseTransformTests
{
    [Fact]
    public void AddBlock_WhenOnlyDcIsPresent_MatchesVp8DcOnlyReconstructor()
    {
        var plane = new byte[4 * 4];
        Array.Fill<byte>(plane, 100);
        var coefficients = new int[16];
        coefficients[0] = 320;

        Vp8InverseTransform.AddBlock(plane, stride: 4, x: 0, y: 0, coefficients);

        Assert.Equal(Enumerable.Repeat((byte)140, 16).ToArray(), plane);
    }

    [Fact]
    public void AddBlock_WhenAcCoefficientsArePresent_AppliesVp8Idct()
    {
        var plane = new byte[4 * 4];
        Array.Fill<byte>(plane, 100);
        var coefficients = new int[16];
        coefficients[0] = 320;
        coefficients[1] = 80;
        coefficients[4] = -48;
        coefficients[5] = 24;

        Vp8InverseTransform.AddBlock(plane, stride: 4, x: 0, y: 0, coefficients);

        Assert.Equal(
            [
                150, 140, 125, 114,
                152, 143, 131, 122,
                154, 148, 139, 132,
                156, 151, 145, 140
            ],
            plane);
    }

    [Fact]
    public void AddBlock_ClipsOutputPixels()
    {
        var plane = new byte[4 * 4];
        Array.Fill<byte>(plane, 250);
        var coefficients = new int[16];
        coefficients[0] = 320;

        Vp8InverseTransform.AddBlock(plane, stride: 4, x: 0, y: 0, coefficients);

        Assert.All(plane, value => Assert.Equal(255, value));
    }
}
