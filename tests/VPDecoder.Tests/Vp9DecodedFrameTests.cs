namespace VPDecoder.Tests;

public sealed class Vp9DecodedFrameTests
{
    [Fact]
    public void CreatePacked_WhenBufferMatchesDimensions_ReturnsFrameWithStride()
    {
        var pixels = new byte[16 * 4];

        var frame = Vp9DecodedFrame.CreatePacked(4, 4, Vp9OutputPixelFormat.Bgra8888, pixels, 16);

        Assert.Equal(4, frame.Width);
        Assert.Equal(4, frame.Height);
        Assert.Equal(16, frame.Stride);
        Assert.Same(pixels, frame.Pixels);
        Assert.Empty(frame.Planes);
    }

    [Fact]
    public void CreatePacked_WhenBufferIsTooSmall_ThrowsArgumentException()
    {
        var pixels = new byte[15];

        Assert.Throws<ArgumentException>(() =>
            Vp9DecodedFrame.CreatePacked(2, 2, Vp9OutputPixelFormat.Bgra8888, pixels, 8));
    }

    [Fact]
    public void CreateYuv420_WhenPlanesMatchDimensions_ReturnsFrameWithPlaneMetadata()
    {
        var pixels = new byte[24];
        var y = new Vp9DecodedPlane(Vp9Plane.Y, 4, 4, 4, 0, 16);
        var u = new Vp9DecodedPlane(Vp9Plane.U, 2, 2, 2, 16, 4);
        var v = new Vp9DecodedPlane(Vp9Plane.V, 2, 2, 2, 20, 4);

        var frame = Vp9DecodedFrame.CreateYuv420(4, 4, pixels, y, u, v);

        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Equal(0, frame.Stride);
        Assert.Equal([y, u, v], frame.Planes);
    }

    [Fact]
    public void CreateYuv420_WhenPlaneExtendsPastBuffer_ThrowsArgumentException()
    {
        var pixels = new byte[23];
        var y = new Vp9DecodedPlane(Vp9Plane.Y, 4, 4, 4, 0, 16);
        var u = new Vp9DecodedPlane(Vp9Plane.U, 2, 2, 2, 16, 4);
        var v = new Vp9DecodedPlane(Vp9Plane.V, 2, 2, 2, 20, 4);

        Assert.Throws<ArgumentException>(() =>
            Vp9DecodedFrame.CreateYuv420(4, 4, pixels, y, u, v));
    }
}
