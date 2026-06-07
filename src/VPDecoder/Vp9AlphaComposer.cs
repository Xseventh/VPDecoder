namespace VPDecoder;

public static class Vp9AlphaComposer
{
    public static Vp9DecodedFrame ConvertBgraToRgba(Vp9DecodedFrame frame)
    {
        if (frame.PixelFormat != Vp9OutputPixelFormat.Bgra8888)
        {
            throw new ArgumentException("VP9 BGRA to RGBA conversion requires a BGRA8888 frame.", nameof(frame));
        }

        var converted = new byte[frame.Pixels.Length];
        for (var y = 0; y < frame.Height; y++)
        {
            var row = y * frame.Stride;
            for (var x = 0; x < frame.Width; x++)
            {
                var offset = row + (x * 4);
                converted[offset] = frame.Pixels[offset + 2];
                converted[offset + 1] = frame.Pixels[offset + 1];
                converted[offset + 2] = frame.Pixels[offset];
                converted[offset + 3] = frame.Pixels[offset + 3];
            }
        }

        return Vp9DecodedFrame.CreatePacked(
            frame.Width,
            frame.Height,
            Vp9OutputPixelFormat.Rgba8888,
            converted,
            frame.Stride);
    }

    public static Vp9DecodedFrame MergeBgraWithBgraAlpha(Vp9DecodedFrame colorFrame, Vp9DecodedFrame alphaFrame)
    {
        if (colorFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888 || alphaFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888)
        {
            throw new ArgumentException("VP9 BGRA alpha merge requires BGRA8888 color and alpha frames.");
        }

        ValidateMatchingDimensions(colorFrame, alphaFrame);
        var merged = (byte[])colorFrame.Pixels.Clone();
        for (var y = 0; y < colorFrame.Height; y++)
        {
            var colorRow = y * colorFrame.Stride;
            var alphaRow = y * alphaFrame.Stride;
            for (var x = 0; x < colorFrame.Width; x++)
            {
                merged[colorRow + (x * 4) + 3] = alphaFrame.Pixels[alphaRow + (x * 4) + 2];
            }
        }

        return Vp9DecodedFrame.CreatePacked(
            colorFrame.Width,
            colorFrame.Height,
            Vp9OutputPixelFormat.Bgra8888,
            merged,
            colorFrame.Stride);
    }

    internal static Vp9DecodedFrame MergeBgraWithBgraAlphaInPlace(Vp9DecodedFrame colorFrame, Vp9DecodedFrame alphaFrame)
    {
        if (colorFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888 || alphaFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888)
        {
            throw new ArgumentException("VP9 BGRA alpha merge requires BGRA8888 color and alpha frames.");
        }

        ValidateMatchingDimensions(colorFrame, alphaFrame);
        for (var y = 0; y < colorFrame.Height; y++)
        {
            var colorRow = y * colorFrame.Stride;
            var alphaRow = y * alphaFrame.Stride;
            for (var x = 0; x < colorFrame.Width; x++)
            {
                colorFrame.Pixels[colorRow + (x * 4) + 3] = alphaFrame.Pixels[alphaRow + (x * 4) + 2];
            }
        }

        return colorFrame;
    }

    public static Vp9DecodedFrame MergeBgraWithYuvAlpha(Vp9DecodedFrame colorFrame, Vp9DecodedFrame alphaFrame)
    {
        if (colorFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888 || alphaFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            throw new ArgumentException("VP9 YUV alpha merge requires BGRA8888 color and YUV420 alpha frames.");
        }

        return MergeBgraWithYuvAlphaInPlace(
            Vp9DecodedFrame.CreatePacked(
                colorFrame.Width,
                colorFrame.Height,
                Vp9OutputPixelFormat.Bgra8888,
                (byte[])colorFrame.Pixels.Clone(),
                colorFrame.Stride),
            alphaFrame);
    }

    internal static Vp9DecodedFrame MergeBgraWithYuvAlphaInPlace(Vp9DecodedFrame colorFrame, Vp9DecodedFrame alphaFrame)
    {
        if (colorFrame.PixelFormat != Vp9OutputPixelFormat.Bgra8888 || alphaFrame.PixelFormat != Vp9OutputPixelFormat.Yuv420)
        {
            throw new ArgumentException("VP9 YUV alpha merge requires a BGRA8888 color frame and a YUV420 alpha frame.");
        }

        ValidateMatchingDimensions(colorFrame, alphaFrame);
        var yPlane = GetAlphaYPlane(alphaFrame);
        for (var y = 0; y < colorFrame.Height; y++)
        {
            var colorRow = y * colorFrame.Stride;
            var alphaRow = yPlane.Offset + (y * yPlane.Stride);
            for (var x = 0; x < colorFrame.Width; x++)
            {
                colorFrame.Pixels[colorRow + (x * 4) + 3] = alphaFrame.Pixels[alphaRow + x];
            }
        }

        return colorFrame;
    }

    private static void ValidateMatchingDimensions(Vp9DecodedFrame colorFrame, Vp9DecodedFrame alphaFrame)
    {
        if (colorFrame.Width != alphaFrame.Width || colorFrame.Height != alphaFrame.Height)
        {
            throw new ArgumentException("VP9 alpha frame dimensions must match the color frame.");
        }
    }

    private static Vp9DecodedPlane GetAlphaYPlane(Vp9DecodedFrame alphaFrame)
    {
        for (var i = 0; i < alphaFrame.Planes.Count; i++)
        {
            if (alphaFrame.Planes[i].Plane == Vp9Plane.Y)
            {
                return alphaFrame.Planes[i];
            }
        }

        throw new ArgumentException("VP9 YUV alpha frame must contain a Y plane.", nameof(alphaFrame));
    }
}
