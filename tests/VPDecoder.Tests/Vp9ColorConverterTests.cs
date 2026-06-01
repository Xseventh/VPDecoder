namespace VPDecoder.Tests;

public sealed class Vp9ColorConverterTests
{
    [Fact]
    public void ConvertYuv420ToPacked_WhenFullRangeNeutralGray_ReturnsOpaqueGray()
    {
        var yuv = CreateSolidYuv420Frame(2, 2, y: 128, u: 128, v: 128);

        var bgra = Vp9ColorConverter.ConvertYuv420ToPacked(
            yuv,
            Vp9ColorRange.Full,
            Vp9OutputPixelFormat.Bgra8888);

        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, bgra.PixelFormat);
        Assert.Equal(8, bgra.Stride);
        Assert.Equal(16, bgra.Pixels.Length);
        for (var i = 0; i < bgra.Pixels.Length; i += 4)
        {
            Assert.Equal(128, bgra.Pixels[i]);
            Assert.Equal(128, bgra.Pixels[i + 1]);
            Assert.Equal(128, bgra.Pixels[i + 2]);
            Assert.Equal(255, bgra.Pixels[i + 3]);
        }
    }

    [Fact]
    public void ConvertYuv420ToPacked_WhenStudioRangeBlack_ReturnsOpaqueBlack()
    {
        var yuv = CreateSolidYuv420Frame(2, 2, y: 16, u: 128, v: 128);

        var rgba = Vp9ColorConverter.ConvertYuv420ToPacked(
            yuv,
            Vp9ColorRange.Studio,
            Vp9OutputPixelFormat.Rgba8888);

        Assert.Equal(Vp9OutputPixelFormat.Rgba8888, rgba.PixelFormat);
        for (var i = 0; i < rgba.Pixels.Length; i += 4)
        {
            Assert.Equal(0, rgba.Pixels[i]);
            Assert.Equal(0, rgba.Pixels[i + 1]);
            Assert.Equal(0, rgba.Pixels[i + 2]);
            Assert.Equal(255, rgba.Pixels[i + 3]);
        }
    }

    [Fact]
    public void ConvertYuv420ToPacked_WhenFullRangeChromaVaries_UsesSharedUvSamples()
    {
        var yuv = CreateSolidYuv420Frame(2, 2, y: 76, u: 84, v: 255);

        var bgra = Vp9ColorConverter.ConvertYuv420ToPacked(
            yuv,
            Vp9ColorRange.Full,
            Vp9OutputPixelFormat.Bgra8888);

        for (var i = 0; i < bgra.Pixels.Length; i += 4)
        {
            Assert.Equal(0, bgra.Pixels[i]);
            Assert.Equal(1, bgra.Pixels[i + 1]);
            Assert.Equal(254, bgra.Pixels[i + 2]);
            Assert.Equal(255, bgra.Pixels[i + 3]);
        }
    }

    private static Vp9DecodedFrame CreateSolidYuv420Frame(int width, int height, byte y, byte u, byte v)
    {
        var buffer = Vp9YuvFrameBuffer.Create(width, height);
        Array.Fill(buffer.Pixels, y, buffer.YPlane.Offset, buffer.YPlane.Length);
        Array.Fill(buffer.Pixels, u, buffer.UPlane.Offset, buffer.UPlane.Length);
        Array.Fill(buffer.Pixels, v, buffer.VPlane.Offset, buffer.VPlane.Length);
        return buffer.ToDecodedFrame();
    }
}
