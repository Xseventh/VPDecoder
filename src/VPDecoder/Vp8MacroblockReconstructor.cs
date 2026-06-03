namespace VPDecoder;

internal static class Vp8MacroblockReconstructor
{
    public static bool TryReconstruct(
        Vp8ReconstructionBuffer buffer,
        Vp8KeyFrameMacroblockMode mode,
        Vp8MacroblockResidual residual,
        Vp8DequantFactors dequantFactors,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (mode.Row != residual.Row || mode.Column != residual.Column)
        {
            diagnostic = Vp8DecodeDiagnostic.InvalidPacket("VP8 macroblock mode and residual coordinates do not match.");
            return false;
        }

        if (!TryBuildBlockMap(residual, out var residualBlocks, out diagnostic))
        {
            return false;
        }

        if (!TryPredictY(buffer, mode, residualBlocks, dequantFactors, out diagnostic))
        {
            return false;
        }

        return TryPredictUv(buffer, mode, residualBlocks, dequantFactors, out diagnostic);
    }

    private static bool TryPredictY(
        Vp8ReconstructionBuffer buffer,
        Vp8KeyFrameMacroblockMode mode,
        IReadOnlyList<Vp8ResidualBlockProbe?> residualBlocks,
        Vp8DequantFactors dequantFactors,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        var yPlane = GetPlanePixels(buffer, buffer.YPlane);
        var x = mode.Column * 16;
        var y = mode.Row * 16;
        if (!ValidateBlockOrigin(buffer.YPlane, x, y, out diagnostic))
        {
            return false;
        }

        if (mode.YMode == Vp8MacroblockPredictionMode.BPred)
        {
            if (mode.BlockModes.Count != 16)
            {
                diagnostic = Vp8DecodeDiagnostic.InvalidPacket("VP8 B_PRED macroblock requires exactly 16 block prediction modes.");
                return false;
            }

            var temp = new byte[16 * 16];
            for (var block = 0; block < 16; block++)
            {
                var localX = (block & 3) * 4;
                var localY = (block >> 2) * 4;
                if (!TryPredictBlockToTemp(
                    yPlane,
                    buffer.YPlane,
                    x,
                    y,
                    temp,
                    tempStride: 16,
                    localX,
                    localY,
                    mode.BlockModes[block],
                    out diagnostic))
                {
                    return false;
                }

                if (!TryAddDcResidual(
                    temp,
                    stride: 16,
                    localX,
                    localY,
                    residualBlocks[block],
                    dequantFactors.Y1Dc,
                    "VP8 Y1 AC reconstruction is not implemented yet.",
                    out diagnostic))
                {
                    return false;
                }
            }

            CopyVisibleBlock(temp, tempStride: 16, yPlane, buffer.YPlane, x, y, size: 16);
            return true;
        }

        var predicted = new byte[16 * 16];
        if (!TryPredictMacroblockToTemp(
            yPlane,
            buffer.YPlane,
            x,
            y,
            predicted,
            size: 16,
            mode.YMode,
            out diagnostic))
        {
            return false;
        }

        if (!IsEmpty(residualBlocks[24]?.Block))
        {
            diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature("VP8 Y2 inverse Walsh reconstruction is not implemented yet.");
            return false;
        }

        for (var block = 0; block < 16; block++)
        {
            if (!IsEmpty(residualBlocks[block]?.Block))
            {
                diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature("VP8 non-B_PRED Y1 AC reconstruction is not implemented yet.");
                return false;
            }
        }

        CopyVisibleBlock(predicted, tempStride: 16, yPlane, buffer.YPlane, x, y, size: 16);
        return true;
    }

    private static bool TryPredictUv(
        Vp8ReconstructionBuffer buffer,
        Vp8KeyFrameMacroblockMode mode,
        IReadOnlyList<Vp8ResidualBlockProbe?> residualBlocks,
        Vp8DequantFactors dequantFactors,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        var uvX = mode.Column * 8;
        var uvY = mode.Row * 8;
        var uPlane = GetPlanePixels(buffer, buffer.UPlane);
        var vPlane = GetPlanePixels(buffer, buffer.VPlane);

        if (!ValidateBlockOrigin(buffer.UPlane, uvX, uvY, out diagnostic) ||
            !ValidateBlockOrigin(buffer.VPlane, uvX, uvY, out diagnostic))
        {
            return false;
        }

        var uTemp = new byte[8 * 8];
        var vTemp = new byte[8 * 8];
        if (!TryPredictMacroblockToTemp(
            uPlane,
            buffer.UPlane,
            uvX,
            uvY,
            uTemp,
            size: 8,
            mode.UvMode,
            out diagnostic) ||
            !TryPredictMacroblockToTemp(
                vPlane,
                buffer.VPlane,
                uvX,
                uvY,
                vTemp,
                size: 8,
                mode.UvMode,
                out diagnostic))
        {
            return false;
        }

        for (var block = 16; block < 20; block++)
        {
            var blockX = uvX + ((block & 1) * 4);
            var blockY = uvY + (((block - 16) >> 1) * 4);
            if (!TryAddDcResidual(
                uTemp,
                stride: 8,
                blockX - uvX,
                blockY - uvY,
                residualBlocks[block],
                dequantFactors.UvDc,
                "VP8 UV AC reconstruction is not implemented yet.",
                out diagnostic))
            {
                return false;
            }
        }

        for (var block = 20; block < 24; block++)
        {
            var blockX = uvX + ((block & 1) * 4);
            var blockY = uvY + (((block - 20) >> 1) * 4);
            if (!TryAddDcResidual(
                vTemp,
                stride: 8,
                blockX - uvX,
                blockY - uvY,
                residualBlocks[block],
                dequantFactors.UvDc,
                "VP8 UV AC reconstruction is not implemented yet.",
                out diagnostic))
            {
                return false;
            }
        }

        CopyVisibleBlock(uTemp, tempStride: 8, uPlane, buffer.UPlane, uvX, uvY, size: 8);
        CopyVisibleBlock(vTemp, tempStride: 8, vPlane, buffer.VPlane, uvX, uvY, size: 8);
        return true;
    }

    private static bool TryPredictMacroblockToTemp(
        ReadOnlySpan<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int x,
        int y,
        Span<byte> temp,
        int size,
        Vp8MacroblockPredictionMode mode,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (mode == Vp8MacroblockPredictionMode.BPred)
        {
            diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature("VP8 UV B_PRED prediction is invalid for key-frame macroblocks.");
            return false;
        }

        var hasAbove = y > 0;
        var hasLeft = x > 0;
        var above = hasAbove
            ? ReadAboveEdgeClamped(plane, planeMetadata, x, y, size)
            : [];
        var left = hasLeft
            ? ReadLeftEdgeClamped(plane, planeMetadata, x, y, size)
            : [];
        var topLeft = hasAbove && hasLeft
            ? ReadClampedPixel(plane, planeMetadata, x - 1, y - 1)
            : (byte)0;

        try
        {
            Vp8IntraPredictor.PredictMacroblock(
                temp,
                size,
                x: 0,
                y: 0,
                size,
                mode,
                above,
                left,
                topLeft,
                hasAbove,
                hasLeft);
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }

        return true;
    }

    private static bool TryPredictBlockToTemp(
        ReadOnlySpan<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int macroblockX,
        int macroblockY,
        Span<byte> temp,
        int tempStride,
        int localX,
        int localY,
        Vp8BlockPredictionMode mode,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        var hasAbove = localY > 0 || macroblockY > 0;
        var hasLeft = localX > 0 || macroblockX > 0;
        var above = hasAbove
            ? ReadTempAboveEdge(plane, planeMetadata, macroblockX, macroblockY, temp, tempStride, localX, localY)
            : [];
        var left = hasLeft
            ? ReadTempLeftEdge(plane, planeMetadata, macroblockX, macroblockY, temp, tempStride, localX, localY)
            : [];
        var topLeft = hasAbove && hasLeft
            ? ReadTempAboveLeft(plane, planeMetadata, macroblockX, macroblockY, temp, tempStride, localX, localY)
            : (byte)0;

        try
        {
            Vp8IntraPredictor.PredictBlock(
                temp,
                tempStride,
                localX,
                localY,
                mode,
                above,
                left,
                topLeft,
                hasAbove,
                hasLeft);
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }

        return true;
    }

    private static bool TryAddDcResidual(
        Span<byte> plane,
        int stride,
        int x,
        int y,
        Vp8ResidualBlockProbe? residual,
        int dcQuant,
        string acUnsupportedMessage,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (residual is null || IsEmpty(residual.Block))
        {
            return true;
        }

        if (!IsDcOnly(residual.Block))
        {
            diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature(acUnsupportedMessage);
            return false;
        }

        Vp8DcOnlyReconstructor.AddDcOnly(
            plane,
            stride,
            x,
            y,
            residual.Block.Coefficients[0] * dcQuant);
        return true;
    }

    private static bool TryBuildBlockMap(
        Vp8MacroblockResidual residual,
        out Vp8ResidualBlockProbe?[] residualBlocks,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        residualBlocks = new Vp8ResidualBlockProbe?[25];
        diagnostic = null;
        if (residual.Skipped)
        {
            if (residual.Blocks.Count != 0)
            {
                diagnostic = Vp8DecodeDiagnostic.InvalidPacket("Skipped VP8 macroblock residual must not contain coefficient blocks.");
                return false;
            }

            return true;
        }

        foreach (var block in residual.Blocks)
        {
            if (block.BlockIndex is < 0 or >= 25)
            {
                diagnostic = Vp8DecodeDiagnostic.InvalidPacket($"VP8 residual block index {block.BlockIndex} is outside the macroblock.");
                return false;
            }

            if (residualBlocks[block.BlockIndex] is not null)
            {
                diagnostic = Vp8DecodeDiagnostic.InvalidPacket($"VP8 residual block index {block.BlockIndex} is duplicated.");
                return false;
            }

            residualBlocks[block.BlockIndex] = block;
        }

        return true;
    }

    private static bool ValidateBlockOrigin(Vp9DecodedPlane plane, int x, int y, out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (x < 0 || y < 0 || x >= plane.Width || y >= plane.Height)
        {
            diagnostic = Vp8DecodeDiagnostic.InvalidPacket("VP8 macroblock origin is outside the visible frame.");
            return false;
        }

        return true;
    }

    private static Span<byte> GetPlanePixels(Vp8ReconstructionBuffer buffer, Vp9DecodedPlane plane)
    {
        return buffer.Pixels.AsSpan(plane.Offset, plane.Length);
    }

    private static byte[] ReadAboveEdgeClamped(ReadOnlySpan<byte> plane, Vp9DecodedPlane planeMetadata, int x, int y, int size)
    {
        var above = new byte[size];
        for (var column = 0; column < size; column++)
        {
            above[column] = ReadClampedPixel(plane, planeMetadata, x + column, y - 1);
        }

        return above;
    }

    private static byte[] ReadLeftEdgeClamped(ReadOnlySpan<byte> plane, Vp9DecodedPlane planeMetadata, int x, int y, int size)
    {
        var left = new byte[size];
        for (var row = 0; row < size; row++)
        {
            left[row] = ReadClampedPixel(plane, planeMetadata, x - 1, y + row);
        }

        return left;
    }

    private static byte[] ReadTempAboveEdge(
        ReadOnlySpan<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int macroblockX,
        int macroblockY,
        ReadOnlySpan<byte> temp,
        int tempStride,
        int localX,
        int localY)
    {
        var above = new byte[8];
        var globalY = macroblockY + localY - 1;
        for (var column = 0; column < above.Length; column++)
        {
            var globalX = macroblockX + localX + column;
            above[column] = localY > 0 &&
                globalX >= macroblockX &&
                globalX < macroblockX + 16
                    ? temp[((localY - 1) * tempStride) + localX + column]
                    : ReadClampedPixel(plane, planeMetadata, globalX, globalY);
        }

        return above;
    }

    private static byte[] ReadTempLeftEdge(
        ReadOnlySpan<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int macroblockX,
        int macroblockY,
        ReadOnlySpan<byte> temp,
        int tempStride,
        int localX,
        int localY)
    {
        if (localX == 0)
        {
            return ReadLeftEdgeClamped(plane, planeMetadata, macroblockX, macroblockY + localY, size: 4);
        }

        var left = new byte[4];
        for (var row = 0; row < left.Length; row++)
        {
            left[row] = temp[((localY + row) * tempStride) + localX - 1];
        }

        return left;
    }

    private static byte ReadTempAboveLeft(
        ReadOnlySpan<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int macroblockX,
        int macroblockY,
        ReadOnlySpan<byte> temp,
        int tempStride,
        int localX,
        int localY)
    {
        if (localX > 0 && localY > 0)
        {
            return temp[((localY - 1) * tempStride) + localX - 1];
        }

        return ReadClampedPixel(
            plane,
            planeMetadata,
            macroblockX + localX - 1,
            macroblockY + localY - 1);
    }

    private static void CopyVisibleBlock(
        ReadOnlySpan<byte> source,
        int tempStride,
        Span<byte> destination,
        Vp9DecodedPlane destinationMetadata,
        int x,
        int y,
        int size)
    {
        var visibleWidth = Math.Min(size, destinationMetadata.Width - x);
        var visibleHeight = Math.Min(size, destinationMetadata.Height - y);
        for (var row = 0; row < visibleHeight; row++)
        {
            source.Slice(row * tempStride, visibleWidth)
                .CopyTo(destination.Slice(((y + row) * destinationMetadata.Stride) + x, visibleWidth));
        }
    }

    private static byte ReadClampedPixel(ReadOnlySpan<byte> plane, Vp9DecodedPlane planeMetadata, int x, int y)
    {
        var clampedX = Math.Clamp(x, 0, planeMetadata.Width - 1);
        var clampedY = Math.Clamp(y, 0, planeMetadata.Height - 1);
        return plane[(clampedY * planeMetadata.Stride) + clampedX];
    }

    private static bool IsEmpty(Vp8CoefficientBlock? block)
    {
        return block is null || block.Coefficients.All(coefficient => coefficient == 0);
    }

    private static bool IsDcOnly(Vp8CoefficientBlock block)
    {
        if (block.Eob > 1)
        {
            return false;
        }

        for (var i = 1; i < block.Coefficients.Length; i++)
        {
            if (block.Coefficients[i] != 0)
            {
                return false;
            }
        }

        return block.Coefficients[0] != 0;
    }
}
