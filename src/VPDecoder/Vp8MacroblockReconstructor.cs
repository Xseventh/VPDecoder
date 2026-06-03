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
        if (!ValidateVisibleBlock(buffer.YPlane, x, y, size: 16, out diagnostic))
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

            for (var block = 0; block < 16; block++)
            {
                var blockX = x + ((block & 3) * 4);
                var blockY = y + ((block >> 2) * 4);
                if (!TryPredictBlock(
                    yPlane,
                    buffer.YPlane,
                    blockX,
                    blockY,
                    mode.BlockModes[block],
                    out diagnostic))
                {
                    return false;
                }

                if (!TryAddDcResidual(
                    yPlane,
                    buffer.YPlane.Stride,
                    blockX,
                    blockY,
                    residualBlocks[block],
                    dequantFactors.Y1Dc,
                    "VP8 Y1 AC reconstruction is not implemented yet.",
                    out diagnostic))
                {
                    return false;
                }
            }

            return true;
        }

        if (!TryPredictMacroblock(
            yPlane,
            buffer.YPlane,
            x,
            y,
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

        if (!ValidateVisibleBlock(buffer.UPlane, uvX, uvY, size: 8, out diagnostic) ||
            !ValidateVisibleBlock(buffer.VPlane, uvX, uvY, size: 8, out diagnostic))
        {
            return false;
        }

        if (!TryPredictMacroblock(
            uPlane,
            buffer.UPlane,
            uvX,
            uvY,
            size: 8,
            mode.UvMode,
            out diagnostic) ||
            !TryPredictMacroblock(
                vPlane,
                buffer.VPlane,
                uvX,
                uvY,
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
                uPlane,
                buffer.UPlane.Stride,
                blockX,
                blockY,
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
                vPlane,
                buffer.VPlane.Stride,
                blockX,
                blockY,
                residualBlocks[block],
                dequantFactors.UvDc,
                "VP8 UV AC reconstruction is not implemented yet.",
                out diagnostic))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryPredictMacroblock(
        Span<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int x,
        int y,
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
            ? ReadAboveEdge(plane, planeMetadata.Stride, x, y, size)
            : [];
        var left = hasLeft
            ? ReadLeftEdge(plane, planeMetadata.Stride, x, y, size)
            : [];
        var topLeft = hasAbove && hasLeft
            ? plane[((y - 1) * planeMetadata.Stride) + x - 1]
            : (byte)0;

        try
        {
            Vp8IntraPredictor.PredictMacroblock(
                plane,
                planeMetadata.Stride,
                x,
                y,
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

    private static bool TryPredictBlock(
        Span<byte> plane,
        Vp9DecodedPlane planeMetadata,
        int x,
        int y,
        Vp8BlockPredictionMode mode,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        var hasAbove = y > 0;
        var hasLeft = x > 0;
        var above = hasAbove
            ? ReadAboveEdge(plane, planeMetadata.Stride, x, y, size: 4)
            : [];
        var left = hasLeft
            ? ReadLeftEdge(plane, planeMetadata.Stride, x, y, size: 4)
            : [];
        var topLeft = hasAbove && hasLeft
            ? plane[((y - 1) * planeMetadata.Stride) + x - 1]
            : (byte)0;

        try
        {
            Vp8IntraPredictor.PredictBlock(
                plane,
                planeMetadata.Stride,
                x,
                y,
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

    private static bool ValidateVisibleBlock(Vp9DecodedPlane plane, int x, int y, int size, out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (x < 0 || y < 0 || x >= plane.Width || y >= plane.Height)
        {
            diagnostic = Vp8DecodeDiagnostic.InvalidPacket("VP8 macroblock origin is outside the visible frame.");
            return false;
        }

        if (x + size > plane.Width || y + size > plane.Height)
        {
            diagnostic = Vp8DecodeDiagnostic.UnsupportedFeature("VP8 clipped edge macroblock reconstruction is not implemented yet.");
            return false;
        }

        return true;
    }

    private static Span<byte> GetPlanePixels(Vp8ReconstructionBuffer buffer, Vp9DecodedPlane plane)
    {
        return buffer.Pixels.AsSpan(plane.Offset, plane.Length);
    }

    private static byte[] ReadAboveEdge(Span<byte> plane, int stride, int x, int y, int size)
    {
        return plane.Slice(((y - 1) * stride) + x, size).ToArray();
    }

    private static byte[] ReadLeftEdge(Span<byte> plane, int stride, int x, int y, int size)
    {
        var left = new byte[size];
        for (var row = 0; row < size; row++)
        {
            left[row] = plane[((y + row) * stride) + x - 1];
        }

        return left;
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
