namespace VPDecoder;

internal static class Vp9MotionCompensator
{
    public static bool TryCopyWholePixelPlaneBlock(
        Vp9DecodedFrame referenceFrame,
        Vp9YuvFrameBuffer destination,
        Vp9Plane plane,
        int destinationX,
        int destinationY,
        int width,
        int height,
        Vp9MotionVector planeMotionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (referenceFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 motion compensation currently supports only YUV420 reference frames.");
            return false;
        }

        if (referenceFrame.Width != destination.Width || referenceFrame.Height != destination.Height)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 reference frame scaling is not supported by motion compensation yet.");
            return false;
        }

        if (!Vp9InterPredictor.IsWholePixelMotionVector(planeMotionVector))
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 fractional-pixel motion compensation is not supported yet.");
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 motion compensation block dimensions must be positive.");
            return false;
        }

        var sourcePlane = GetPlane(referenceFrame, plane);
        var destinationPlane = GetPlane(destination, plane);
        var sourceX = destinationX + (planeMotionVector.Column >> 3);
        var sourceY = destinationY + (planeMotionVector.Row >> 3);
        if (!IsInside(sourcePlane, sourceX, sourceY, width, height) ||
            !IsInside(destinationPlane, destinationX, destinationY, width, height))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 motion compensation block extends outside the reference or destination plane.");
            return false;
        }

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = sourcePlane.Offset + ((sourceY + row) * sourcePlane.Stride) + sourceX;
            var destinationOffset = destinationPlane.Offset + ((destinationY + row) * destinationPlane.Stride) + destinationX;
            referenceFrame.Pixels.AsSpan(sourceOffset, width).CopyTo(destination.Pixels.AsSpan(destinationOffset, width));
        }

        return true;
    }

    private static Vp9DecodedPlane GetPlane(Vp9DecodedFrame frame, Vp9Plane plane)
    {
        var index = GetPlaneIndex(plane);
        return frame.Planes[index];
    }

    private static Vp9DecodedPlane GetPlane(Vp9YuvFrameBuffer frame, Vp9Plane plane)
    {
        return plane switch
        {
            Vp9Plane.Y => frame.YPlane,
            Vp9Plane.U => frame.UPlane,
            Vp9Plane.V => frame.VPlane,
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, "Unsupported VP9 plane.")
        };
    }

    private static int GetPlaneIndex(Vp9Plane plane)
    {
        return plane switch
        {
            Vp9Plane.Y => 0,
            Vp9Plane.U => 1,
            Vp9Plane.V => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, "Unsupported VP9 plane.")
        };
    }

    private static bool IsInside(Vp9DecodedPlane plane, int x, int y, int width, int height)
    {
        return x >= 0 &&
            y >= 0 &&
            width <= plane.Width - x &&
            height <= plane.Height - y;
    }
}
