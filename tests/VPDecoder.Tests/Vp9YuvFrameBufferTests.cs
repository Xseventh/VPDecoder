namespace VPDecoder.Tests;

public sealed class Vp9YuvFrameBufferTests
{
    [Fact]
    public void Create_ForSampleDimensions_ReturnsExpectedYuv420Layout()
    {
        var buffer = Vp9YuvFrameBuffer.Create(2656, 1352);

        Assert.Equal(2656, buffer.YStride);
        Assert.Equal(1328, buffer.UvStride);
        Assert.Equal(3_590_912, buffer.YPlane.Length);
        Assert.Equal(897_728, buffer.UPlane.Length);
        Assert.Equal(897_728, buffer.VPlane.Length);
        Assert.Equal(0, buffer.YPlane.Offset);
        Assert.Equal(3_590_912, buffer.UPlane.Offset);
        Assert.Equal(4_488_640, buffer.VPlane.Offset);
        Assert.Equal(5_386_368, buffer.Pixels.Length);
    }

    [Fact]
    public void ToDecodedFrame_ReturnsYuv420FrameWithPlaneMetadata()
    {
        var buffer = Vp9YuvFrameBuffer.Create(4, 4);

        var frame = buffer.ToDecodedFrame();

        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Same(buffer.Pixels, frame.Pixels);
        Assert.Equal([buffer.YPlane, buffer.UPlane, buffer.VPlane], frame.Planes);
    }
}
