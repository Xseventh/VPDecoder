namespace VPDecoder.Tests;

public sealed class Vp9MotionCompensatorTests
{
    [Fact]
    public void TryCopyWholePixelPlaneBlock_WithZeroMv_CopiesDeterministicYuvBlocks()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(4, 4);

        Assert.True(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 1,
            destinationY: 1,
            width: 2,
            height: 2,
            new Vp9MotionVector(0, 0),
            out var yDiagnostic), yDiagnostic?.Message);
        Assert.True(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.U,
            destinationX: 0,
            destinationY: 0,
            width: 2,
            height: 1,
            new Vp9MotionVector(0, 0),
            out var uDiagnostic), uDiagnostic?.Message);
        Assert.True(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.V,
            destinationX: 0,
            destinationY: 1,
            width: 2,
            height: 1,
            new Vp9MotionVector(0, 0),
            out var vDiagnostic), vDiagnostic?.Message);

        Assert.Equal(5, destination.Pixels[5]);
        Assert.Equal(6, destination.Pixels[6]);
        Assert.Equal(9, destination.Pixels[9]);
        Assert.Equal(10, destination.Pixels[10]);
        Assert.Equal([100, 101], destination.Pixels.AsSpan(destination.UPlane.Offset, 2).ToArray());
        Assert.Equal([202, 203], destination.Pixels.AsSpan(destination.VPlane.Offset + destination.VPlane.Stride, 2).ToArray());
    }

    [Fact]
    public void TryCopyWholePixelPlaneBlock_WithWholePixelMv_CopiesShiftedSourceBlock()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(4, 4);

        Assert.True(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 0,
            destinationY: 0,
            width: 2,
            height: 2,
            new Vp9MotionVector(Row: 8, Column: 8),
            out var diagnostic), diagnostic?.Message);

        Assert.Equal(5, destination.Pixels[0]);
        Assert.Equal(6, destination.Pixels[1]);
        Assert.Equal(9, destination.Pixels[4]);
        Assert.Equal(10, destination.Pixels[5]);
    }

    [Fact]
    public void TryCopyWholePixelPlaneBlock_WithNegativeWholePixelMv_ExtendsReferenceEdges()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(4, 4);

        Assert.True(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 0,
            destinationY: 0,
            width: 3,
            height: 3,
            new Vp9MotionVector(Row: -8, Column: -8),
            out var diagnostic), diagnostic?.Message);

        Assert.Equal(0, destination.Pixels[0]);
        Assert.Equal(0, destination.Pixels[1]);
        Assert.Equal(1, destination.Pixels[2]);
        Assert.Equal(0, destination.Pixels[4]);
        Assert.Equal(0, destination.Pixels[5]);
        Assert.Equal(1, destination.Pixels[6]);
        Assert.Equal(4, destination.Pixels[8]);
        Assert.Equal(4, destination.Pixels[9]);
        Assert.Equal(5, destination.Pixels[10]);
    }

    [Fact]
    public void TryCopyWholePixelPlaneBlock_WhenDestinationExtendsOutsidePlane_ReturnsInvalidPacket()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(4, 4);

        Assert.False(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 3,
            destinationY: 0,
            width: 2,
            height: 2,
            new Vp9MotionVector(0, 0),
            out var diagnostic));

        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, diagnostic?.Code);
        Assert.Contains("destination", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCopyWholePixelPlaneBlock_WithFractionalMv_ReturnsUnsupportedDiagnostic()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(4, 4);

        Assert.False(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 0,
            destinationY: 0,
            width: 2,
            height: 2,
            new Vp9MotionVector(Row: 1, Column: 0),
            out var diagnostic));

        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("fractional", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCopyWholePixelPlaneBlock_WithOutOfRangeWholePixelMv_ReturnsInvalidPacket()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(4, 4);

        Assert.False(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 0,
            destinationY: 0,
            width: 2,
            height: 2,
            new Vp9MotionVector(Row: 16_384, Column: 0),
            out var diagnostic));

        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, diagnostic?.Code);
        Assert.Contains("range", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCopyWholePixelPlaneBlock_WithReferenceScaling_ReturnsUnsupportedDiagnostic()
    {
        var reference = CreatePatternYuvFrame(width: 4, height: 4);
        var destination = Vp9YuvFrameBuffer.Create(8, 4);

        Assert.False(Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            reference,
            destination,
            Vp9Plane.Y,
            destinationX: 0,
            destinationY: 0,
            width: 2,
            height: 2,
            new Vp9MotionVector(0, 0),
            out var diagnostic));

        Assert.Equal(Vp9DecodeDiagnosticCode.UnsupportedInterFrameFeature, diagnostic?.Code);
        Assert.Contains("scaling", diagnostic?.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Vp9DecodedFrame CreatePatternYuvFrame(int width, int height)
    {
        var buffer = Vp9YuvFrameBuffer.Create(width, height);
        for (var i = 0; i < buffer.YPlane.Length; i++)
        {
            buffer.Pixels[buffer.YPlane.Offset + i] = (byte)i;
        }

        for (var i = 0; i < buffer.UPlane.Length; i++)
        {
            buffer.Pixels[buffer.UPlane.Offset + i] = (byte)(100 + i);
        }

        for (var i = 0; i < buffer.VPlane.Length; i++)
        {
            buffer.Pixels[buffer.VPlane.Offset + i] = (byte)(200 + i);
        }

        return buffer.ToDecodedFrame();
    }
}
