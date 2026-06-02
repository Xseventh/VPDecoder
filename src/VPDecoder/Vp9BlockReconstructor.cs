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

        var transformStep4 = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(group.TransformSize);
        var width4 = width / 4;
        var height4 = height / 4;
        var seenBlocks = new bool[expectedBlockCount];
        var planeInfo = GetPlaneInfo(frameBuffer, plane);
        var originX = plane == 0 ? modeInfo.MiColumn * 8 : modeInfo.MiColumn * 4;
        var originY = plane == 0 ? modeInfo.MiRow * 8 : modeInfo.MiRow * 4;
        var tileStartX = plane == 0 ? geometry.MiColumnStart * 8 : geometry.MiColumnStart * 4;
        var planePixels = frameBuffer.Pixels.AsSpan(planeInfo.Metadata.Offset, planeInfo.Metadata.Length);

        foreach (var coefficients in group.Blocks)
        {
            if (coefficients.TransformSize != group.TransformSize)
            {
                throw new ArgumentException("VP9 coefficient block transform size does not match its group.", nameof(group));
            }

            if (coefficients.Row4 < 0 ||
                coefficients.Column4 < 0 ||
                coefficients.Row4 + transformStep4 > height4 ||
                coefficients.Column4 + transformStep4 > width4 ||
                coefficients.Row4 % transformStep4 != 0 ||
                coefficients.Column4 % transformStep4 != 0)
            {
                throw new ArgumentException("VP9 coefficient block transform offset does not fit the block geometry.", nameof(group));
            }

            var blockRow = coefficients.Row4 / transformStep4;
            var blockColumn = coefficients.Column4 / transformStep4;
            var blockIndex = (blockRow * blocksWide) + blockColumn;
            if (seenBlocks[blockIndex])
            {
                throw new ArgumentException("VP9 coefficient block transform offsets must be unique within a group.", nameof(group));
            }

            seenBlocks[blockIndex] = true;
            var x = originX + (coefficients.Column4 * 4);
            var y = originY + (coefficients.Row4 * 4);
            var predictionMode = GetPredictionMode(modeInfo, coefficients, plane);
            var above = y > 0
                ? ReadAboveEdge(
                    planePixels,
                    planeInfo.Stride,
                    planeInfo.Metadata.Width,
                    x,
                    y,
                    GetRequiredAboveEdgeLength(predictionMode, transformSize))
                : [];
            var left = x > tileStartX
                ? ReadLeftEdge(planePixels, planeInfo.Stride, planeInfo.Metadata.Height, x, y, transformSize)
                : [];
            var aboveLeft = GetAboveLeft(planePixels, planeInfo.Stride, x, y, above, left);
            NormalizeEdges(predictionMode, transformSize, ref above, ref left, ref aboveLeft);

            var visibleWidth = Math.Min(transformSize, planeInfo.Metadata.Width - x);
            var visibleHeight = Math.Min(transformSize, planeInfo.Metadata.Height - y);
            if (visibleWidth <= 0 || visibleHeight <= 0)
            {
                continue;
            }

            var isClipped = visibleWidth != transformSize || visibleHeight != transformSize;
            if (isClipped)
            {
                var temp = new byte[checked(transformSize * transformSize)];
                PredictAndAddResidual(
                    temp,
                    transformSize,
                    x: 0,
                    y: 0,
                    transformSize,
                    group.TransformSize,
                    coefficients,
                    predictionMode,
                    above,
                    left,
                    aboveLeft);
                CopyVisibleBlock(
                    temp,
                    transformSize,
                    planePixels,
                    planeInfo.Stride,
                    x,
                    y,
                    visibleWidth,
                    visibleHeight);
                continue;
            }

            PredictAndAddResidual(
                planePixels,
                planeInfo.Stride,
                x,
                y,
                transformSize,
                group.TransformSize,
                coefficients,
                predictionMode,
                above,
                left,
                aboveLeft);
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

    private static Vp9PredictionMode GetPredictionMode(
        Vp9ModeInfoProbe modeInfo,
        Vp9CoefficientBlockProbe coefficients,
        int plane)
    {
        if (plane is 1 or 2)
        {
            return modeInfo.UvMode;
        }

        if (plane != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.");
        }

        if (modeInfo.BlockSize >= Vp9BlockSize.Block8X8 ||
            coefficients.TransformSize != Vp9TransformSize.Tx4X4)
        {
            return modeInfo.YMode;
        }

        if (modeInfo.YSubModes.Count != 4)
        {
            throw new InvalidOperationException("VP9 sub-8x8 reconstruction requires exactly four Y sub-modes.");
        }

        if (coefficients.Row4 is < 0 or > 1 || coefficients.Column4 is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coefficients),
                "VP9 sub-8x8 reconstruction transform offsets must be within the owning 8x8 block.");
        }

        return modeInfo.YSubModes[(coefficients.Row4 * 2) + coefficients.Column4];
    }

    private static int GetRequiredAboveEdgeLength(Vp9PredictionMode mode, int transformSize)
    {
        return mode == Vp9PredictionMode.D63
            ? transformSize + 2
            : mode == Vp9PredictionMode.D45
                ? transformSize + 1
                : transformSize;
    }

    private static byte? GetAboveLeft(
        ReadOnlySpan<byte> plane,
        int stride,
        int x,
        int y,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left)
    {
        if (above.Length == 0)
        {
            return null;
        }

        return left.Length == 0
            ? (byte)129
            : plane[((y - 1) * stride) + x - 1];
    }

    private static void NormalizeEdges(
        Vp9PredictionMode mode,
        int transformSize,
        ref byte[] above,
        ref byte[] left,
        ref byte? aboveLeft)
    {
        if (mode == Vp9PredictionMode.Dc)
        {
            return;
        }

        if (NeedsAbove(mode) && above.Length == 0)
        {
            above = new byte[GetRequiredAboveEdgeLength(mode, transformSize)];
            Array.Fill(above, (byte)127);
            aboveLeft = 127;
        }

        if (NeedsLeft(mode) && left.Length == 0)
        {
            left = new byte[transformSize];
            Array.Fill(left, (byte)129);
            aboveLeft ??= 129;
        }
    }

    private static bool NeedsAbove(Vp9PredictionMode mode)
    {
        return mode is
            Vp9PredictionMode.Vertical or
            Vp9PredictionMode.D45 or
            Vp9PredictionMode.D63 or
            Vp9PredictionMode.D117 or
            Vp9PredictionMode.D135 or
            Vp9PredictionMode.D153 or
            Vp9PredictionMode.TrueMotion;
    }

    private static bool NeedsLeft(Vp9PredictionMode mode)
    {
        return mode is
            Vp9PredictionMode.Horizontal or
            Vp9PredictionMode.D117 or
            Vp9PredictionMode.D135 or
            Vp9PredictionMode.D153 or
            Vp9PredictionMode.D207 or
            Vp9PredictionMode.TrueMotion;
    }

    private static byte[] ReadAboveEdge(ReadOnlySpan<byte> plane, int stride, int planeWidth, int x, int y, int length)
    {
        var above = new byte[length];
        var available = Math.Min(length, planeWidth - x);
        plane.Slice(((y - 1) * stride) + x, available).CopyTo(above);
        if (available < length)
        {
            Array.Fill(above, above[available - 1], available, length - available);
        }

        return above;
    }

    private static byte[] ReadLeftEdge(ReadOnlySpan<byte> plane, int stride, int planeHeight, int x, int y, int size)
    {
        var left = new byte[size];
        var available = Math.Min(size, planeHeight - y);
        for (var row = 0; row < available; row++)
        {
            left[row] = plane[((y + row) * stride) + x - 1];
        }

        if (available < size)
        {
            Array.Fill(left, left[available - 1], available, size - available);
        }

        return left;
    }

    private static void PredictAndAddResidual(
        Span<byte> destination,
        int stride,
        int x,
        int y,
        int transformSizeInPixels,
        Vp9TransformSize transformSize,
        Vp9CoefficientBlockProbe coefficients,
        Vp9PredictionMode predictionMode,
        ReadOnlySpan<byte> above,
        ReadOnlySpan<byte> left,
        byte? aboveLeft)
    {
        var predictionDestination = destination.Slice((y * stride) + x);
        Vp9IntraPredictor.Predict(
            predictionMode,
            predictionDestination,
            stride,
            transformSizeInPixels,
            above,
            left,
            aboveLeft);

        if (coefficients.Eob == 0)
        {
            return;
        }

        if (IsDcOnlyOrEmpty(coefficients) &&
            (transformSize == Vp9TransformSize.Tx32X32 ||
                coefficients.TransformType == Vp9TransformType.DctDct))
        {
            Vp9DcOnlyReconstructor.AddDcOnly(
                destination,
                stride,
                x,
                y,
                transformSizeInPixels,
                coefficients.DequantizedCoefficients[0]);
            return;
        }

        Vp9InverseTransform.AddBlock(
            destination,
            stride,
            x,
            y,
            transformSize,
            coefficients.TransformType,
            coefficients.DequantizedCoefficients,
            coefficients.Eob);
    }

    private static void CopyVisibleBlock(
        ReadOnlySpan<byte> source,
        int sourceStride,
        Span<byte> destination,
        int destinationStride,
        int x,
        int y,
        int width,
        int height)
    {
        for (var row = 0; row < height; row++)
        {
            source.Slice(row * sourceStride, width).CopyTo(
                destination.Slice(((y + row) * destinationStride) + x, width));
        }
    }

    private sealed record Vp9PlaneInfo(Vp9DecodedPlane Metadata, int Stride);
}
