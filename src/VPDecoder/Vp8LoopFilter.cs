namespace VPDecoder;

internal readonly record struct Vp8LoopFilterThresholds(
    int InteriorLimit,
    int HighEdgeVarianceThreshold,
    int MacroblockFilterLimit,
    int SubblockFilterLimit);

internal readonly record struct Vp8LoopFilterMacroblock(
    int Row,
    int Column,
    bool FilterSubblocks);

internal static class Vp8LoopFilter
{
    public static bool TryApply(
        Vp8ReconstructionBuffer buffer,
        Vp8KeyFrameSyntaxHeader syntaxHeader,
        IReadOnlyList<Vp8LoopFilterMacroblock> macroblocks,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = ValidateSupportedSyntax(syntaxHeader);
        if (diagnostic is not null)
        {
            return false;
        }

        if (syntaxHeader.LoopFilter.Level == 0)
        {
            return true;
        }

        var thresholds = GetThresholds(
            syntaxHeader.LoopFilter.Level,
            syntaxHeader.LoopFilter.SharpnessLevel);
        if (syntaxHeader.LoopFilter.Type == Vp8LoopFilterType.Simple)
        {
            ApplySimple(buffer, macroblocks, thresholds);
            return true;
        }

        ApplyNormal(buffer, macroblocks, thresholds);
        return true;
    }

    public static Vp8LoopFilterThresholds GetThresholds(int filterLevel, int sharpnessLevel)
    {
        if (filterLevel is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filterLevel),
                filterLevel,
                "VP8 loop filter level must be between 0 and 63.");
        }

        if (sharpnessLevel is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sharpnessLevel),
                sharpnessLevel,
                "VP8 loop filter sharpness level must be between 0 and 7.");
        }

        var interiorLimit = filterLevel;
        if (sharpnessLevel > 0)
        {
            interiorLimit >>= sharpnessLevel > 4 ? 2 : 1;
            interiorLimit = Math.Min(interiorLimit, 9 - sharpnessLevel);
        }

        interiorLimit = Math.Max(interiorLimit, 1);
        var hevThreshold = filterLevel >= 15 ? 1 : 0;
        if (filterLevel >= 40)
        {
            hevThreshold++;
        }

        return new Vp8LoopFilterThresholds(
            interiorLimit,
            hevThreshold,
            (2 * (filterLevel + 2)) + interiorLimit,
            (2 * filterLevel) + interiorLimit);
    }

    public static bool ShouldFilterSubblocks(
        Vp8KeyFrameMacroblockMode mode,
        Vp8MacroblockResidual residual)
    {
        return mode.YMode == Vp8MacroblockPredictionMode.BPred ||
            residual.Blocks.Any(block => block.EffectiveEob > 0);
    }

    private static Vp8DecodeDiagnostic? ValidateSupportedSyntax(Vp8KeyFrameSyntaxHeader syntaxHeader)
    {
        if (syntaxHeader.LoopFilter.DeltaEnabled)
        {
            return Vp8DecodeDiagnostic.UnsupportedFeature(
                "VP8 loop filter delta-enabled streams are not supported yet.");
        }

        if (syntaxHeader.Segmentation.Enabled &&
            syntaxHeader.Segmentation.UpdateFeatureData &&
            (syntaxHeader.Segmentation.AbsoluteDeltaMode ||
                syntaxHeader.Segmentation.LoopFilterUpdates.Any(update => update != 0)))
        {
            return Vp8DecodeDiagnostic.UnsupportedFeature(
                "VP8 segmentation loop filter feature data is not supported yet.");
        }

        return null;
    }

    private static void ApplySimple(
        Vp8ReconstructionBuffer buffer,
        IReadOnlyList<Vp8LoopFilterMacroblock> macroblocks,
        Vp8LoopFilterThresholds thresholds)
    {
        foreach (var macroblock in macroblocks)
        {
            var x = macroblock.Column * 16;
            var y = macroblock.Row * 16;
            var rows = Math.Min(16, buffer.YPlane.Height - y);
            var columns = Math.Min(16, buffer.YPlane.Width - x);

            if (macroblock.Column > 0)
            {
                ApplyVerticalSimple(
                    buffer.Pixels,
                    buffer.YPlane,
                    edgeX: x,
                    startY: y,
                    rows,
                    thresholds.MacroblockFilterLimit);
            }

            if (macroblock.FilterSubblocks)
            {
                for (var offset = 4; offset < 16; offset += 4)
                {
                    ApplyVerticalSimple(
                        buffer.Pixels,
                        buffer.YPlane,
                        edgeX: x + offset,
                        startY: y,
                        rows,
                        thresholds.SubblockFilterLimit);
                }
            }

            if (macroblock.Row > 0)
            {
                ApplyHorizontalSimple(
                    buffer.Pixels,
                    buffer.YPlane,
                    edgeY: y,
                    startX: x,
                    columns,
                    thresholds.MacroblockFilterLimit);
            }

            if (macroblock.FilterSubblocks)
            {
                for (var offset = 4; offset < 16; offset += 4)
                {
                    ApplyHorizontalSimple(
                        buffer.Pixels,
                        buffer.YPlane,
                        edgeY: y + offset,
                        startX: x,
                        columns,
                        thresholds.SubblockFilterLimit);
                }
            }
        }
    }

    private static void ApplyNormal(
        Vp8ReconstructionBuffer buffer,
        IReadOnlyList<Vp8LoopFilterMacroblock> macroblocks,
        Vp8LoopFilterThresholds thresholds)
    {
        foreach (var macroblock in macroblocks)
        {
            var yX = macroblock.Column * 16;
            var yY = macroblock.Row * 16;
            ApplyNormalPlane(
                buffer.Pixels,
                buffer.YPlane,
                yX,
                yY,
                blockSize: 16,
                macroblock,
                thresholds);

            var uvX = macroblock.Column * 8;
            var uvY = macroblock.Row * 8;
            ApplyNormalPlane(
                buffer.Pixels,
                buffer.UPlane,
                uvX,
                uvY,
                blockSize: 8,
                macroblock,
                thresholds);
            ApplyNormalPlane(
                buffer.Pixels,
                buffer.VPlane,
                uvX,
                uvY,
                blockSize: 8,
                macroblock,
                thresholds);
        }
    }

    private static void ApplyNormalPlane(
        byte[] pixels,
        Vp9DecodedPlane plane,
        int x,
        int y,
        int blockSize,
        Vp8LoopFilterMacroblock macroblock,
        Vp8LoopFilterThresholds thresholds)
    {
        var rows = Math.Min(blockSize, plane.Height - y);
        var columns = Math.Min(blockSize, plane.Width - x);
        if (macroblock.Column > 0)
        {
            ApplyVerticalNormal(
                pixels,
                plane,
                edgeX: x,
                startY: y,
                rows,
                thresholds.MacroblockFilterLimit,
                thresholds.InteriorLimit,
                thresholds.HighEdgeVarianceThreshold,
                macroblockEdge: true);
        }

        if (macroblock.FilterSubblocks)
        {
            ApplyVerticalNormal(
                pixels,
                plane,
                edgeX: x + 4,
                startY: y,
                rows,
                thresholds.SubblockFilterLimit,
                thresholds.InteriorLimit,
                thresholds.HighEdgeVarianceThreshold,
                macroblockEdge: false);

            if (blockSize == 16)
            {
                ApplyVerticalNormal(
                    pixels,
                    plane,
                    edgeX: x + 8,
                    startY: y,
                    rows,
                    thresholds.SubblockFilterLimit,
                    thresholds.InteriorLimit,
                    thresholds.HighEdgeVarianceThreshold,
                    macroblockEdge: false);
                ApplyVerticalNormal(
                    pixels,
                    plane,
                    edgeX: x + 12,
                    startY: y,
                    rows,
                    thresholds.SubblockFilterLimit,
                    thresholds.InteriorLimit,
                    thresholds.HighEdgeVarianceThreshold,
                    macroblockEdge: false);
            }
        }

        if (macroblock.Row > 0)
        {
            ApplyHorizontalNormal(
                pixels,
                plane,
                edgeY: y,
                startX: x,
                columns,
                thresholds.MacroblockFilterLimit,
                thresholds.InteriorLimit,
                thresholds.HighEdgeVarianceThreshold,
                macroblockEdge: true);
        }

        if (macroblock.FilterSubblocks)
        {
            ApplyHorizontalNormal(
                pixels,
                plane,
                edgeY: y + 4,
                startX: x,
                columns,
                thresholds.SubblockFilterLimit,
                thresholds.InteriorLimit,
                thresholds.HighEdgeVarianceThreshold,
                macroblockEdge: false);

            if (blockSize == 16)
            {
                ApplyHorizontalNormal(
                    pixels,
                    plane,
                    edgeY: y + 8,
                    startX: x,
                    columns,
                    thresholds.SubblockFilterLimit,
                    thresholds.InteriorLimit,
                    thresholds.HighEdgeVarianceThreshold,
                    macroblockEdge: false);
                ApplyHorizontalNormal(
                    pixels,
                    plane,
                    edgeY: y + 12,
                    startX: x,
                    columns,
                    thresholds.SubblockFilterLimit,
                    thresholds.InteriorLimit,
                    thresholds.HighEdgeVarianceThreshold,
                    macroblockEdge: false);
            }
        }
    }

    private static void ApplyVerticalSimple(
        byte[] pixels,
        Vp9DecodedPlane plane,
        int edgeX,
        int startY,
        int rows,
        int filterLimit)
    {
        if (edgeX < 2 || edgeX + 1 >= plane.Width || rows <= 0)
        {
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            var index = plane.Offset + ((startY + row) * plane.Stride) + edgeX;
            SimpleFilter(pixels, filterLimit, index - 2, index - 1, index, index + 1);
        }
    }

    private static void ApplyHorizontalSimple(
        byte[] pixels,
        Vp9DecodedPlane plane,
        int edgeY,
        int startX,
        int columns,
        int filterLimit)
    {
        if (edgeY < 2 || edgeY + 1 >= plane.Height || columns <= 0)
        {
            return;
        }

        for (var column = 0; column < columns; column++)
        {
            var index = plane.Offset + (edgeY * plane.Stride) + startX + column;
            SimpleFilter(pixels, filterLimit, index - (2 * plane.Stride), index - plane.Stride, index, index + plane.Stride);
        }
    }

    private static void ApplyVerticalNormal(
        byte[] pixels,
        Vp9DecodedPlane plane,
        int edgeX,
        int startY,
        int rows,
        int filterLimit,
        int interiorLimit,
        int hevThreshold,
        bool macroblockEdge)
    {
        if (edgeX < 4 || edgeX + 3 >= plane.Width || rows <= 0)
        {
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            var index = plane.Offset + ((startY + row) * plane.Stride) + edgeX;
            FilterNormal(
                pixels,
                index - 4,
                index - 3,
                index - 2,
                index - 1,
                index,
                index + 1,
                index + 2,
                index + 3,
                filterLimit,
                interiorLimit,
                hevThreshold,
                macroblockEdge);
        }
    }

    private static void ApplyHorizontalNormal(
        byte[] pixels,
        Vp9DecodedPlane plane,
        int edgeY,
        int startX,
        int columns,
        int filterLimit,
        int interiorLimit,
        int hevThreshold,
        bool macroblockEdge)
    {
        if (edgeY < 4 || edgeY + 3 >= plane.Height || columns <= 0)
        {
            return;
        }

        for (var column = 0; column < columns; column++)
        {
            var index = plane.Offset + (edgeY * plane.Stride) + startX + column;
            FilterNormal(
                pixels,
                index - (4 * plane.Stride),
                index - (3 * plane.Stride),
                index - (2 * plane.Stride),
                index - plane.Stride,
                index,
                index + plane.Stride,
                index + (2 * plane.Stride),
                index + (3 * plane.Stride),
                filterLimit,
                interiorLimit,
                hevThreshold,
                macroblockEdge);
        }
    }

    private static void SimpleFilter(
        byte[] pixels,
        int filterLimit,
        int op1,
        int op0,
        int oq0,
        int oq1)
    {
        if (!SimpleThreshold(
                pixels[op1],
                pixels[op0],
                pixels[oq0],
                pixels[oq1],
                filterLimit))
        {
            return;
        }

        CommonAdjust(useOuterTaps: true, pixels, op1, op0, oq0, oq1);
    }

    private static void FilterNormal(
        byte[] pixels,
        int op3,
        int op2,
        int op1,
        int op0,
        int oq0,
        int oq1,
        int oq2,
        int oq3,
        int filterLimit,
        int interiorLimit,
        int hevThreshold,
        bool macroblockEdge)
    {
        if (!NormalThreshold(
                pixels[op3],
                pixels[op2],
                pixels[op1],
                pixels[op0],
                pixels[oq0],
                pixels[oq1],
                pixels[oq2],
                pixels[oq3],
                filterLimit,
                interiorLimit))
        {
            return;
        }

        var p1 = ToSigned(pixels[op1]);
        var p0 = ToSigned(pixels[op0]);
        var q0 = ToSigned(pixels[oq0]);
        var q1 = ToSigned(pixels[oq1]);
        var hev = HighEdgeVariance(pixels[op1], pixels[op0], pixels[oq0], pixels[oq1], hevThreshold);
        if (!macroblockEdge)
        {
            var subblockAdjustment = (CommonAdjust(hev, pixels, op1, op0, oq0, oq1) + 1) >> 1;
            if (!hev)
            {
                pixels[oq1] = FromSigned(q1 - subblockAdjustment);
                pixels[op1] = FromSigned(p1 + subblockAdjustment);
            }

            return;
        }

        if (hev)
        {
            CommonAdjust(useOuterTaps: true, pixels, op1, op0, oq0, oq1);
            return;
        }

        var p2 = ToSigned(pixels[op2]);
        var q2 = ToSigned(pixels[oq2]);
        var w = SignedClamp(SignedClamp(p1 - q1) + (3 * (q0 - p0)));

        var macroblockAdjustment = SignedClamp((27 * w + 63) >> 7);
        pixels[oq0] = FromSigned(q0 - macroblockAdjustment);
        pixels[op0] = FromSigned(p0 + macroblockAdjustment);

        macroblockAdjustment = SignedClamp((18 * w + 63) >> 7);
        pixels[oq1] = FromSigned(q1 - macroblockAdjustment);
        pixels[op1] = FromSigned(p1 + macroblockAdjustment);

        macroblockAdjustment = SignedClamp((9 * w + 63) >> 7);
        pixels[oq2] = FromSigned(q2 - macroblockAdjustment);
        pixels[op2] = FromSigned(p2 + macroblockAdjustment);
    }

    private static bool SimpleThreshold(byte p1, byte p0, byte q0, byte q1, int filterLimit)
    {
        return (2 * Math.Abs(p0 - q0)) + (Math.Abs(p1 - q1) / 2) <= filterLimit;
    }

    private static bool NormalThreshold(
        byte p3,
        byte p2,
        byte p1,
        byte p0,
        byte q0,
        byte q1,
        byte q2,
        byte q3,
        int filterLimit,
        int interiorLimit)
    {
        return SimpleThreshold(p1, p0, q0, q1, filterLimit) &&
            Math.Abs(p3 - p2) <= interiorLimit &&
            Math.Abs(p2 - p1) <= interiorLimit &&
            Math.Abs(p1 - p0) <= interiorLimit &&
            Math.Abs(q1 - q0) <= interiorLimit &&
            Math.Abs(q2 - q1) <= interiorLimit &&
            Math.Abs(q3 - q2) <= interiorLimit;
    }

    private static bool HighEdgeVariance(byte p1, byte p0, byte q0, byte q1, int threshold)
    {
        return Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold;
    }

    private static int CommonAdjust(
        bool useOuterTaps,
        byte[] pixels,
        int op1,
        int op0,
        int oq0,
        int oq1)
    {
        var p1 = ToSigned(pixels[op1]);
        var p0 = ToSigned(pixels[op0]);
        var q0 = ToSigned(pixels[oq0]);
        var q1 = ToSigned(pixels[oq1]);

        var adjustment = useOuterTaps
            ? SignedClamp(p1 - q1)
            : 0;
        adjustment = SignedClamp(adjustment + (3 * (q0 - p0)));

        var p0Adjustment = SignedClamp(adjustment + 3) >> 3;
        adjustment = SignedClamp(adjustment + 4) >> 3;

        pixels[oq0] = FromSigned(q0 - adjustment);
        pixels[op0] = FromSigned(p0 + p0Adjustment);
        return adjustment;
    }

    private static int ToSigned(byte value)
    {
        return value - 128;
    }

    private static byte FromSigned(int value)
    {
        return (byte)(SignedClamp(value) + 128);
    }

    private static int SignedClamp(int value)
    {
        return Math.Clamp(value, -128, 127);
    }
}
