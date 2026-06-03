namespace VPDecoder.Tests;

public sealed class Vp8ReconstructionBufferTests
{
    [Fact]
    public void Create_UsesYuv420PlaneLayout()
    {
        var buffer = Vp8ReconstructionBuffer.Create(3, 3);

        Assert.Equal(3, buffer.YPlane.Width);
        Assert.Equal(3, buffer.YPlane.Height);
        Assert.Equal(2, buffer.UPlane.Width);
        Assert.Equal(2, buffer.UPlane.Height);
        Assert.Equal(2, buffer.VPlane.Width);
        Assert.Equal(2, buffer.VPlane.Height);
        Assert.Equal(17, buffer.Pixels.Length);
    }

    [Fact]
    public void FillNeutralChroma_SetsUvPlanesTo128()
    {
        var buffer = Vp8ReconstructionBuffer.Create(4, 4);

        buffer.FillNeutralChroma();

        Assert.All(
            buffer.Pixels.AsSpan(buffer.UPlane.Offset, buffer.UPlane.Length).ToArray(),
            value => Assert.Equal(128, value));
        Assert.All(
            buffer.Pixels.AsSpan(buffer.VPlane.Offset, buffer.VPlane.Length).ToArray(),
            value => Assert.Equal(128, value));
    }

    [Fact]
    public void ToDecodedFrame_WhenYuvRequested_ReturnsYuv420Frame()
    {
        var buffer = Vp8ReconstructionBuffer.Create(4, 4);

        var frame = buffer.ToDecodedFrame(new Vp8DecodeOptions(OutputFormat: Vp9OutputPixelFormat.Yuv420));

        Assert.Equal(Vp9OutputPixelFormat.Yuv420, frame.PixelFormat);
        Assert.Same(buffer.Pixels, frame.Pixels);
        Assert.Equal([buffer.YPlane, buffer.UPlane, buffer.VPlane], frame.Planes);
    }

    [Fact]
    public void ToDecodedFrame_WhenPackedRequested_ConvertsToBgra()
    {
        var buffer = Vp8ReconstructionBuffer.Create(2, 2);
        buffer.Pixels.AsSpan(buffer.YPlane.Offset, buffer.YPlane.Length).Fill(16);
        buffer.FillNeutralChroma();

        var frame = buffer.ToDecodedFrame(new Vp8DecodeOptions(OutputFormat: Vp9OutputPixelFormat.Bgra8888));

        Assert.Equal(Vp9OutputPixelFormat.Bgra8888, frame.PixelFormat);
        Assert.Equal(8, frame.Stride);
        Assert.Equal(16, frame.Pixels.Length);
        for (var i = 0; i < frame.Pixels.Length; i += 4)
        {
            Assert.Equal(0, frame.Pixels[i]);
            Assert.Equal(0, frame.Pixels[i + 1]);
            Assert.Equal(0, frame.Pixels[i + 2]);
            Assert.Equal(255, frame.Pixels[i + 3]);
        }
    }
}
