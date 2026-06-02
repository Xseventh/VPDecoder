namespace VPDecoder;

internal static class Vp9BlockReconstructor
{
    public static void ReconstructDcOnlyGroup(
        Vp9YuvFrameBuffer frameBuffer,
        Vp9TileGeometry geometry,
        Vp9ModeInfoProbe modeInfo,
        Vp9CoefficientBlockGroupProbe group,
        int plane)
    {
        if (group.BlockSize != modeInfo.BlockSize)
        {
            throw new ArgumentException("VP9 coefficient group block size does not match its mode info.", nameof(group));
        }

        var transformSize = GetTransformSizeInPixels(group.TransformSize);
        var width = GetPlaneBlockWidthInPixels(modeInfo.BlockSize, plane);
        var height = GetPlaneBlockHeightInPixels(modeInfo.BlockSize, plane);
        if (width % transformSize != 0 || height % transformSize != 0)
        {
            throw new NotSupportedException(
                $"VP9 DC-only reconstruction cannot split {modeInfo.BlockSize} plane {plane} into {group.TransformSize} blocks.");
        }

        var blocksWide = width / transformSize;
        var blocksHigh = height / transformSize;
        var expectedBlockCount = checked(blocksWide * blocksHigh);
        if (group.Blocks.Count != expectedBlockCount)
        {
            throw new ArgumentException("VP9 coefficient block count does not match the block geometry.", nameof(group));
        }

        var planeInfo = GetPlaneInfo(frameBuffer, plane);
        var originX = plane == 0 ? modeInfo.MiColumn * 8 : modeInfo.MiColumn * 4;
        var originY = plane == 0 ? modeInfo.MiRow * 8 : modeInfo.MiRow * 4;
        var tileStartX = plane == 0 ? geometry.MiColumnStart * 8 : geometry.MiColumnStart * 4;
        var planePixels = frameBuffer.Pixels.AsSpan(planeInfo.Metadata.Offset, planeInfo.Metadata.Length);

        for (var blockRow = 0; blockRow < blocksHigh; blockRow++)
        {
            for (var blockColumn = 0; blockColumn < blocksWide; blockColumn++)
            {
                var blockIndex = (blockRow * blocksWide) + blockColumn;
                var coefficients = group.Blocks[blockIndex];
                var x = originX + (blockColumn * transformSize);
                var y = originY + (blockRow * transformSize);
                if (x + transformSize > planeInfo.Metadata.Width || y + transformSize > planeInfo.Metadata.Height)
                {
                    throw new NotSupportedException(
                        "VP9 DC-only reconstruction does not support clipped transform blocks at frame edges yet.");
                }

                var destinationOffset = planeInfo.Metadata.Offset + (y * planeInfo.Stride) + x;
                var destination = frameBuffer.Pixels.AsSpan(destinationOffset);
                var above = y > 0 ? ReadAboveEdge(planePixels, planeInfo.Stride, x, y, transformSize) : [];
                var left = x > tileStartX ? ReadLeftEdge(planePixels, planeInfo.Stride, x, y, transformSize) : [];
                Vp9IntraPredictor.PredictDc(destination, planeInfo.Stride, transformSize, above, left);

                if (coefficients.Eob == 0)
                {
                    continue;
                }

                if (IsDcOnlyOrEmpty(coefficients))
                {
                    Vp9DcOnlyReconstructor.AddDcOnly(
                        planePixels,
                        planeInfo.Stride,
                        x,
                        y,
                        transformSize,
                        coefficients.DequantizedCoefficients[0]);
                    continue;
                }

                Vp9InverseTransform.AddBlock(
                    planePixels,
                    planeInfo.Stride,
                    x,
                    y,
                    group.TransformSize,
                    coefficients.TransformType,
                    coefficients.DequantizedCoefficients,
                    coefficients.Eob);
            }
        }
    }

    public static bool IsDcOnlyOrEmpty(Vp9CoefficientBlockProbe coefficients)
    {
        return coefficients.Eob switch
        {
            0 => coefficients.NonZeroCount == 0,
            1 => coefficients.NonZeroCount == 1 &&
                coefficients.FirstNonZeroRasterIndex == 0 &&
                coefficients.LastNonZeroRasterIndex == 0,
            _ => false
        };
    }

    private static Vp9PlaneInfo GetPlaneInfo(Vp9YuvFrameBuffer frameBuffer, int plane)
    {
        return plane switch
        {
            0 => new Vp9PlaneInfo(frameBuffer.YPlane, frameBuffer.YStride),
            1 => new Vp9PlaneInfo(frameBuffer.UPlane, frameBuffer.UvStride),
            2 => new Vp9PlaneInfo(frameBuffer.VPlane, frameBuffer.UvStride),
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.")
        };
    }

    private static int GetPlaneBlockWidthInPixels(Vp9BlockSize blockSize, int plane)
    {
        var miWidth = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(blockSize);
        return plane == 0 ? miWidth * 8 : miWidth * 4;
    }

    private static int GetPlaneBlockHeightInPixels(Vp9BlockSize blockSize, int plane)
    {
        var miHeight = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(blockSize);
        return plane == 0 ? miHeight * 8 : miHeight * 4;
    }

    private static int GetTransformSizeInPixels(Vp9TransformSize transformSize)
    {
        return transformSize switch
        {
            Vp9TransformSize.Tx4X4 => 4,
            Vp9TransformSize.Tx8X8 => 8,
            Vp9TransformSize.Tx16X16 => 16,
            Vp9TransformSize.Tx32X32 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(transformSize), transformSize, "Unsupported VP9 transform size.")
        };
    }

    private static byte[] ReadAboveEdge(ReadOnlySpan<byte> plane, int stride, int x, int y, int size)
    {
        var above = new byte[size];
        plane.Slice(((y - 1) * stride) + x, size).CopyTo(above);
        return above;
    }

    private static byte[] ReadLeftEdge(ReadOnlySpan<byte> plane, int stride, int x, int y, int size)
    {
        var left = new byte[size];
        for (var row = 0; row < size; row++)
        {
            left[row] = plane[((y + row) * stride) + x - 1];
        }

        return left;
    }

    private sealed record Vp9PlaneInfo(Vp9DecodedPlane Metadata, int Stride);
}
