namespace VPDecoder.Tests;

public sealed class Vp9DcOnlyReconstructorTests
{
    [Theory]
    [InlineData(4, 16, 1)]
    [InlineData(8, 32, 1)]
    [InlineData(16, 64, 1)]
    [InlineData(32, 128, 1)]
    public void GetDcResidual_ReturnsRoundedResidual(int size, int dequantizedDc, int expected)
    {
        Assert.Equal(expected, Vp9DcOnlyReconstructor.GetDcResidual(size, dequantizedDc));
    }

    [Fact]
    public void AddDcOnly_AddsResidualAndClipsPixels()
    {
        var plane = new byte[8 * 8];
        Array.Fill<byte>(plane, 128);

        Vp9DcOnlyReconstructor.AddDcOnly(plane, 8, 0, 0, 4, 1600);

        for (var y = 0; y < 4; y++)
        {
            Assert.Equal([228, 228, 228, 228], plane.Skip(y * 8).Take(4).ToArray());
        }
    }
}
