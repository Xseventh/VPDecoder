namespace VPDecoder.Tests;

public sealed class Vp9QuantizationTests
{
    [Fact]
    public void Create_ForExternalMainFrame_ReturnsExpectedDequantValues()
    {
        var header = Vp9FrameHeaderParser.Parse(ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9"));

        var dequant = Vp9DequantTables.Create(header.Quantization, header.BitDepth);

        Assert.Equal(51, header.Quantization.BaseQIndex);
        Assert.Equal(new Vp9DequantTables(50, 58, 50, 58), dequant);
    }

    [Fact]
    public void Create_ForExternalAlphaFrame_ReturnsExpectedDequantValues()
    {
        var header = Vp9FrameHeaderParser.Parse(ReadRequiredSample(
            "/tmp/vp9-alpha-frame-0.vp9",
            6233,
            "94079f539a2165b10f5db2d9e9b5d54ca8df534ca3d36e4eaa1234b0b17a7329"));

        var dequant = Vp9DequantTables.Create(header.Quantization, header.BitDepth);

        Assert.Equal(58, header.Quantization.BaseQIndex);
        Assert.Equal(new Vp9DequantTables(56, 65, 56, 65), dequant);
    }

    [Theory]
    [InlineData(0, 4, 4)]
    [InlineData(255, 1336, 1828)]
    public void QuantLookups_ClampToEightBitVp9Range(int qIndex, int expectedDc, int expectedAc)
    {
        Assert.Equal(expectedDc, Vp9Quantization.DcQuant(qIndex, 0));
        Assert.Equal(expectedAc, Vp9Quantization.AcQuant(qIndex, 0));
    }

    [Fact]
    public void QuantLookups_WhenDeltaMovesOutOfRange_ClampQIndex()
    {
        Assert.Equal(4, Vp9Quantization.DcQuant(0, -999));
        Assert.Equal(4, Vp9Quantization.AcQuant(0, -999));
        Assert.Equal(1336, Vp9Quantization.DcQuant(255, 999));
        Assert.Equal(1828, Vp9Quantization.AcQuant(255, 999));
    }

    private static byte[] ReadRequiredSample(string path, int expectedLength, string expectedSha256)
    {
        Assert.True(File.Exists(path), $"Required VP9 acceptance sample is missing: {path}");
        var packet = File.ReadAllBytes(path);
        Assert.Equal(expectedLength, packet.Length);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet)).ToLowerInvariant();
        Assert.Equal(expectedSha256, hash);
        return packet;
    }
}
