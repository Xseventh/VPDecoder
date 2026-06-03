namespace VPDecoder.Tests;

public sealed class Vp8InverseWalshTransformTests
{
    [Fact]
    public void Transform_WhenOnlyDcIsPresent_ReplicatesRoundedDc()
    {
        var input = new int[16];
        input[0] = 64;
        var output = new int[16];

        Vp8InverseWalshTransform.Transform(input, output);

        Assert.Equal(Enumerable.Repeat(8, 16).ToArray(), output);
    }

    [Fact]
    public void Transform_WhenAcCoefficientsArePresent_AppliesVp8WalshTransform()
    {
        var input = new int[16];
        input[0] = 640;
        input[1] = 80;
        input[5] = -48;
        var output = new int[16];

        Vp8InverseWalshTransform.Transform(input, output);

        Assert.Equal(
            [
                84, 84, 76, 76,
                84, 84, 76, 76,
                96, 96, 64, 64,
                96, 96, 64, 64
            ],
            output);
    }
}
