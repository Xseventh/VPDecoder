namespace VPDecoder.Tests;

public sealed class Vp9AlphaComposerTests
{
    [Fact]
    public void ConvertBgraToRgba_SwapsRedAndBlueChannels()
    {
        var frame = Vp9DecodedFrame.CreatePacked(
            2,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [10, 20, 30, 40, 50, 60, 70, 80],
            8);

        var converted = Vp9AlphaComposer.ConvertBgraToRgba(frame);

        Assert.Equal(Vp9OutputPixelFormat.Rgba8888, converted.PixelFormat);
        Assert.Equal([30, 20, 10, 40, 70, 60, 50, 80], converted.Pixels);
    }

    [Fact]
    public void MergeBgraWithBgraAlpha_UsesAlphaRedChannelAsOutputAlpha()
    {
        var color = Vp9DecodedFrame.CreatePacked(
            2,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [10, 20, 30, 255, 40, 50, 60, 255],
            8);
        var alpha = Vp9DecodedFrame.CreatePacked(
            2,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [0, 0, 77, 255, 0, 0, 155, 255],
            8);

        var merged = Vp9AlphaComposer.MergeBgraWithBgraAlpha(color, alpha);

        Assert.Equal([10, 20, 30, 77, 40, 50, 60, 155], merged.Pixels);
    }

    [Fact]
    public void MergeBgraWithBgraAlpha_DoesNotMutateColorInput()
    {
        var color = Vp9DecodedFrame.CreatePacked(
            1,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [10, 20, 30, 255],
            4);
        var alpha = Vp9DecodedFrame.CreatePacked(
            1,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [0, 0, 77, 255],
            4);

        _ = Vp9AlphaComposer.MergeBgraWithBgraAlpha(color, alpha);

        Assert.Equal([10, 20, 30, 255], color.Pixels);
    }

    [Fact]
    public void MergeBgraWithBgraAlphaInPlace_MutatesAndReturnsColorInput()
    {
        var color = Vp9DecodedFrame.CreatePacked(
            1,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [10, 20, 30, 255],
            4);
        var alpha = Vp9DecodedFrame.CreatePacked(
            1,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [0, 0, 77, 255],
            4);

        var merged = Vp9AlphaComposer.MergeBgraWithBgraAlphaInPlace(color, alpha);

        Assert.Same(color, merged);
        Assert.Equal([10, 20, 30, 77], color.Pixels);
    }

    [Fact]
    public void MergeBgraWithYuvAlpha_UsesAlphaLumaAsOutputAlpha()
    {
        var color = Vp9DecodedFrame.CreatePacked(
            2,
            2,
            Vp9OutputPixelFormat.Bgra8888,
            [
                10, 20, 30, 255, 40, 50, 60, 255,
                70, 80, 90, 255, 100, 110, 120, 255
            ],
            8);
        var alphaBuffer = Vp9YuvFrameBuffer.Create(2, 2);
        alphaBuffer.Pixels[alphaBuffer.YPlane.Offset] = 1;
        alphaBuffer.Pixels[alphaBuffer.YPlane.Offset + 1] = 2;
        alphaBuffer.Pixels[alphaBuffer.YPlane.Offset + 2] = 3;
        alphaBuffer.Pixels[alphaBuffer.YPlane.Offset + 3] = 4;

        var merged = Vp9AlphaComposer.MergeBgraWithYuvAlpha(color, alphaBuffer.ToDecodedFrame());

        Assert.Equal(
            [
                10, 20, 30, 1, 40, 50, 60, 2,
                70, 80, 90, 3, 100, 110, 120, 4
            ],
            merged.Pixels);
    }

    [Fact]
    public void MergeBgraWithYuvAlphaInPlace_MutatesAndReturnsColorInput()
    {
        var color = Vp9DecodedFrame.CreatePacked(
            2,
            1,
            Vp9OutputPixelFormat.Bgra8888,
            [10, 20, 30, 255, 40, 50, 60, 255],
            8);
        var alphaBuffer = Vp9YuvFrameBuffer.Create(2, 1);
        alphaBuffer.Pixels[alphaBuffer.YPlane.Offset] = 11;
        alphaBuffer.Pixels[alphaBuffer.YPlane.Offset + 1] = 22;

        var merged = Vp9AlphaComposer.MergeBgraWithYuvAlphaInPlace(color, alphaBuffer.ToDecodedFrame());

        Assert.Same(color, merged);
        Assert.Equal([10, 20, 30, 11, 40, 50, 60, 22], color.Pixels);
    }

    [Fact]
    public void MergeBgraWithBgraAlpha_WhenDimensionsDiffer_ThrowsArgumentException()
    {
        var color = Vp9DecodedFrame.CreatePacked(2, 1, Vp9OutputPixelFormat.Bgra8888, new byte[8], 8);
        var alpha = Vp9DecodedFrame.CreatePacked(1, 1, Vp9OutputPixelFormat.Bgra8888, new byte[4], 4);

        Assert.Throws<ArgumentException>(() => Vp9AlphaComposer.MergeBgraWithBgraAlpha(color, alpha));
    }
}
