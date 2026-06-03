namespace VPDecoder.Tests;

public sealed class Vp8DcOnlyReconstructorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 1)]
    [InlineData(12, 2)]
    [InlineData(-4, 0)]
    [InlineData(-12, -1)]
    public void GetDcResidual_ReturnsLibvpxRoundedResidual(int dequantizedDc, int expected)
    {
        Assert.Equal(expected, Vp8DcOnlyReconstructor.GetDcResidual(dequantizedDc));
    }

    [Fact]
    public void AddDcOnly_AddsResidualToFourByFourBlock()
    {
        var plane = new byte[8 * 8];
        Array.Fill<byte>(plane, 100);

        Vp8DcOnlyReconstructor.AddDcOnly(plane, stride: 8, x: 2, y: 1, dequantizedDc: 80);

        for (var y = 1; y < 5; y++)
        {
            Assert.Equal([110, 110, 110, 110], plane.Skip((y * 8) + 2).Take(4).ToArray());
        }
    }

    [Fact]
    public void AddDcOnly_ClipsOutputPixels()
    {
        var plane = new byte[4 * 8];
        Array.Fill<byte>(plane, 250);

        Vp8DcOnlyReconstructor.AddDcOnly(plane, stride: 4, x: 0, y: 0, dequantizedDc: 160);

        Assert.All(plane.Take(16), value => Assert.Equal(255, value));
    }
}
