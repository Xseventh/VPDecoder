namespace VPDecoder;

internal readonly record struct Vp9LoopFilterThresholds(
    byte Limit,
    byte MacroblockLimit,
    byte HighEdgeVarianceThreshold);

internal static class Vp9LoopFilter
{
    private const int SuperblockSizeInMiUnits = 8;

    public static bool TryApply(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        if (!TryValidateYuv420Frame(reconstructedFrame.Frame, out diagnostic))
        {
            return false;
        }

        IReadOnlyList<Vp9LoopFilterSuperblockMask> masks;
        var masksBuilt = header.FrameType == Vp9FrameType.KeyFrame
            ? Vp9LoopFilterMaskBuilder.TryBuildKeyFrameMasks(header, reconstructedFrame, out masks, out diagnostic)
            : Vp9LoopFilterMaskBuilder.TryBuildInterFrameMasks(header, reconstructedFrame, out masks, out diagnostic);
        if (!masksBuilt)
        {
            return false;
        }

        ApplyMasks(reconstructedFrame.Frame, header, masks);

        return true;
    }

    public static bool TryApplyInterFrame(
        Vp9FrameHeader header,
        Vp9DecodedFrame frame,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> modeBlocks,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        if (!TryValidateYuv420Frame(frame, out diagnostic))
        {
            return false;
        }

        if (!Vp9LoopFilterMaskBuilder.TryBuildInterFrameMasks(
                header,
                frame,
                modeBlocks,
                out var masks,
                out diagnostic))
        {
            return false;
        }

        ApplyMasks(frame, header, masks);
        return true;
    }

    public static int GetKeyFrameFilterLevel(Vp9LoopFilterHeader header)
    {
        if (header.FilterLevel == 0)
        {
            return 0;
        }

        var level = header.FilterLevel;
        if (header.ModeRefDeltaEnabled)
        {
            if (header.RefDeltas.Count < 1)
            {
                throw new ArgumentException("VP9 loop filter ref delta table must include INTRA_FRAME.", nameof(header));
            }

            var scale = 1 << (header.FilterLevel >> 5);
            level += header.RefDeltas[0] * scale;
        }

        return Math.Clamp(level, 0, 63);
    }

    public static int GetInterFrameFilterLevel(
        Vp9LoopFilterHeader header,
        Vp9InterReferenceFrame referenceFrame,
        Vp9InterPredictionMode predictionMode)
    {
        if (header.FilterLevel == 0)
        {
            return 0;
        }

        if (!header.ModeRefDeltaEnabled)
        {
            return header.FilterLevel;
        }

        var referenceIndex = GetReferenceLoopFilterDeltaIndex(referenceFrame);
        if (header.RefDeltas.Count <= referenceIndex)
        {
            throw new ArgumentException("VP9 loop filter ref delta table must include inter reference frames.", nameof(header));
        }

        var modeIndex = GetInterModeLoopFilterDeltaIndex(predictionMode);
        if (header.ModeDeltas.Count <= modeIndex)
        {
            throw new ArgumentException("VP9 loop filter mode delta table must include inter mode deltas.", nameof(header));
        }

        var scale = 1 << (header.FilterLevel >> 5);
        var level = header.FilterLevel +
            (header.RefDeltas[referenceIndex] * scale) +
            (header.ModeDeltas[modeIndex] * scale);
        return Math.Clamp(level, 0, 63);
    }

    public static int GetInterFrameFilterLevel(Vp9LoopFilterHeader header, Vp9InterModeInfoProbe modeInfo)
    {
        if (modeInfo.IsInterBlock)
        {
            return GetInterFrameFilterLevel(header, modeInfo.ReferenceFrame, modeInfo.PredictionMode);
        }

        if (header.FilterLevel == 0)
        {
            return 0;
        }

        if (!header.ModeRefDeltaEnabled)
        {
            return header.FilterLevel;
        }

        if (header.RefDeltas.Count < 1)
        {
            throw new ArgumentException("VP9 loop filter ref delta table must include INTRA_FRAME.", nameof(header));
        }

        var scale = 1 << (header.FilterLevel >> 5);
        var level = header.FilterLevel + (header.RefDeltas[0] * scale);
        return Math.Clamp(level, 0, 63);
    }

    public static Vp9LoopFilterThresholds GetThresholds(int filterLevel, int sharpnessLevel)
    {
        if (filterLevel is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filterLevel),
                filterLevel,
                "VP9 loop filter level must be between 0 and 63.");
        }

        if (sharpnessLevel is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sharpnessLevel),
                sharpnessLevel,
                "VP9 loop filter sharpness must be between 0 and 7.");
        }

        var blockInsideLimit = filterLevel >> (sharpnessLevel > 0 ? 1 : 0);
        blockInsideLimit >>= sharpnessLevel > 4 ? 1 : 0;
        if (sharpnessLevel > 0)
        {
            blockInsideLimit = Math.Min(blockInsideLimit, 9 - sharpnessLevel);
        }

        blockInsideLimit = Math.Max(blockInsideLimit, 1);
        var macroblockLimit = checked((2 * (filterLevel + 2)) + blockInsideLimit);
        return new Vp9LoopFilterThresholds(
            (byte)blockInsideLimit,
            (byte)macroblockLimit,
            (byte)(filterLevel >> 4));
    }

    private static bool TryValidateYuv420Frame(Vp9DecodedFrame frame, out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (frame.PixelFormat == Vp9OutputPixelFormat.Yuv420 &&
            frame.Planes.Count == 3)
        {
            return true;
        }

        diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
            "VP9 loop filter requires a reconstructed YUV420 frame.");
        return false;
    }

    private static void ApplyMasks(
        Vp9DecodedFrame frame,
        Vp9FrameHeader header,
        IReadOnlyList<Vp9LoopFilterSuperblockMask> masks)
    {
        foreach (var mask in masks)
        {
            ApplyLumaPlane(frame, header, mask);
            ApplyChromaPlane(frame, header, mask, planeIndex: 1);
            ApplyChromaPlane(frame, header, mask, planeIndex: 2);
        }
    }

    private static int GetReferenceLoopFilterDeltaIndex(Vp9InterReferenceFrame referenceFrame)
    {
        return referenceFrame switch
        {
            Vp9InterReferenceFrame.Last => 1,
            Vp9InterReferenceFrame.Golden => 2,
            Vp9InterReferenceFrame.AltRef => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(referenceFrame), referenceFrame, "Unsupported VP9 inter reference frame.")
        };
    }

    private static int GetInterModeLoopFilterDeltaIndex(Vp9InterPredictionMode predictionMode)
    {
        return predictionMode switch
        {
            Vp9InterPredictionMode.ZeroMv => 0,
            Vp9InterPredictionMode.NearestMv or
            Vp9InterPredictionMode.NearMv or
            Vp9InterPredictionMode.NewMv => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(predictionMode), predictionMode, "Unsupported VP9 inter prediction mode.")
        };
    }

    private static void ApplyLumaPlane(
        Vp9DecodedFrame frame,
        Vp9FrameHeader header,
        Vp9LoopFilterSuperblockMask mask)
    {
        var plane = frame.Planes[0];
        var baseX = mask.MiColumn * 8;
        var baseY = mask.MiRow * 8;
        var baseIndex = plane.Offset + (baseY * plane.Stride) + baseX;

        var left16 = mask.LeftY[(int)Vp9TransformSize.Tx16X16];
        var left8 = mask.LeftY[(int)Vp9TransformSize.Tx8X8];
        var left4 = mask.LeftY[(int)Vp9TransformSize.Tx4X4];
        var internal4 = mask.Internal4x4Y;
        var verticalMask = left16 | left8 | left4 | internal4;
        for (var row = 0; row < SuperblockSizeInMiUnits && mask.MiRow + row < header.TileInfo.MiRows; row++)
        {
            for (var column = 0; column < SuperblockSizeInMiUnits; column++)
            {
                var bit = 1UL << ((row * SuperblockSizeInMiUnits) + column);
                if ((verticalMask & bit) == 0)
                {
                    continue;
                }

                var bitIndex = (row * SuperblockSizeInMiUnits) + column;
                var startIndex = baseIndex + (row * 8 * plane.Stride) + (column * 8);
                var rows = Math.Min(8, plane.Height - (baseY + (row * 8)));
                ApplyVerticalEdge(frame.Pixels, plane.Stride, startIndex, mask.GetLumaThresholds(bitIndex), left16, left8, left4, internal4, bit, rows);
            }
        }

        var above16 = mask.AboveY[(int)Vp9TransformSize.Tx16X16];
        var above8 = mask.AboveY[(int)Vp9TransformSize.Tx8X8];
        var above4 = mask.AboveY[(int)Vp9TransformSize.Tx4X4];
        internal4 = mask.Internal4x4Y;
        var horizontalMask = above16 | above8 | above4 | internal4;
        for (var row = 0; row < SuperblockSizeInMiUnits && mask.MiRow + row < header.TileInfo.MiRows; row++)
        {
            for (var column = 0; column < SuperblockSizeInMiUnits; column++)
            {
                var bit = 1UL << ((row * SuperblockSizeInMiUnits) + column);
                if (mask.MiRow + row == 0)
                {
                    if ((internal4 & bit) == 0)
                    {
                        continue;
                    }
                }
                else if ((horizontalMask & bit) == 0)
                {
                    continue;
                }

                var bitIndex = (row * SuperblockSizeInMiUnits) + column;
                var startIndex = baseIndex + (row * 8 * plane.Stride) + (column * 8);
                var columns = Math.Min(8, plane.Width - (baseX + (column * 8)));
                if (mask.MiRow + row == 0)
                {
                    ApplyHorizontalInternal4(frame.Pixels, plane.Stride, startIndex, mask.GetLumaThresholds(bitIndex), internal4, bit, columns);
                    continue;
                }

                ApplyHorizontalEdge(frame.Pixels, plane.Stride, startIndex, mask.GetLumaThresholds(bitIndex), above16, above8, above4, internal4, bit, columns);
            }
        }
    }

    private static void ApplyChromaPlane(
        Vp9DecodedFrame frame,
        Vp9FrameHeader header,
        Vp9LoopFilterSuperblockMask mask,
        int planeIndex)
    {
        var plane = frame.Planes[planeIndex];
        var baseX = mask.MiColumn * 4;
        var baseY = mask.MiRow * 4;
        var baseIndex = plane.Offset + (baseY * plane.Stride) + baseX;

        var left16 = (uint)mask.LeftUv[(int)Vp9TransformSize.Tx16X16];
        var left8 = (uint)mask.LeftUv[(int)Vp9TransformSize.Tx8X8];
        var left4 = (uint)mask.LeftUv[(int)Vp9TransformSize.Tx4X4];
        var internal4 = (uint)mask.Internal4x4Uv;
        var verticalMask = left16 | left8 | left4 | internal4;
        for (var row = 0; row < 4 && mask.MiRow + (row * 2) < header.TileInfo.MiRows; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var bit = 1U << ((row * 4) + column);
                if ((verticalMask & bit) == 0)
                {
                    continue;
                }

                var startIndex = baseIndex + (row * 8 * plane.Stride) + (column * 8);
                var rows = Math.Min(8, plane.Height - (baseY + (row * 8)));
                ApplyVerticalEdge(frame.Pixels, plane.Stride, startIndex, mask.GetChromaThresholds(row, column), left16, left8, left4, internal4, bit, rows);
            }
        }

        var above16 = (uint)mask.AboveUv[(int)Vp9TransformSize.Tx16X16];
        var above8 = (uint)mask.AboveUv[(int)Vp9TransformSize.Tx8X8];
        var above4 = (uint)mask.AboveUv[(int)Vp9TransformSize.Tx4X4];
        internal4 = mask.Internal4x4Uv;
        var horizontalMask = above16 | above8 | above4 | internal4;
        for (var row = 0; row < 4 && mask.MiRow + (row * 2) < header.TileInfo.MiRows; row++)
        {
            var skipBorderInternal4 = mask.MiRow + (row * 2) == header.TileInfo.MiRows - 1;
            for (var column = 0; column < 4; column++)
            {
                var bit = 1U << ((row * 4) + column);
                if (mask.MiRow + (row * 2) == 0)
                {
                    if (skipBorderInternal4 || (internal4 & bit) == 0)
                    {
                        continue;
                    }
                }
                else if (((skipBorderInternal4 ? above16 | above8 | above4 : horizontalMask) & bit) == 0)
                {
                    continue;
                }

                var startIndex = baseIndex + (row * 8 * plane.Stride) + (column * 8);
                var columns = Math.Min(8, plane.Width - (baseX + (column * 8)));
                if (mask.MiRow + (row * 2) == 0)
                {
                    if (!skipBorderInternal4)
                    {
                        ApplyHorizontalInternal4(frame.Pixels, plane.Stride, startIndex, mask.GetChromaThresholds(row, column), internal4, bit, columns);
                    }

                    continue;
                }

                ApplyHorizontalEdge(
                    frame.Pixels,
                    plane.Stride,
                    startIndex,
                    mask.GetChromaThresholds(row, column),
                    above16,
                    above8,
                    above4,
                    skipBorderInternal4 ? 0U : internal4,
                    bit,
                    columns);
            }
        }
    }

    private static void ApplyVerticalEdge(
        byte[] plane,
        int stride,
        int startIndex,
        Vp9LoopFilterThresholds thresholds,
        ulong mask16,
        ulong mask8,
        ulong mask4,
        ulong internal4,
        ulong bit,
        int rows)
    {
        if (rows <= 0)
        {
            return;
        }

        if ((mask16 & bit) != 0)
        {
            ApplyVertical16(plane, stride, startIndex, thresholds, rows);
        }
        else if ((mask8 & bit) != 0)
        {
            ApplyVertical8(plane, stride, startIndex, thresholds, rows);
        }
        else if ((mask4 & bit) != 0)
        {
            ApplyVertical4(plane, stride, startIndex, thresholds, rows);
        }

        if ((internal4 & bit) != 0)
        {
            ApplyVertical4(plane, stride, startIndex + 4, thresholds, rows);
        }
    }

    private static void ApplyHorizontalEdge(
        byte[] plane,
        int stride,
        int startIndex,
        Vp9LoopFilterThresholds thresholds,
        ulong mask16,
        ulong mask8,
        ulong mask4,
        ulong internal4,
        ulong bit,
        int columns)
    {
        if (columns <= 0)
        {
            return;
        }

        if ((mask16 & bit) != 0)
        {
            ApplyHorizontal16(plane, stride, startIndex, thresholds, columns);
        }
        else if ((mask8 & bit) != 0)
        {
            ApplyHorizontal8(plane, stride, startIndex, thresholds, columns);
        }
        else if ((mask4 & bit) != 0)
        {
            ApplyHorizontal4(plane, stride, startIndex, thresholds, columns);
        }

        ApplyHorizontalInternal4(plane, stride, startIndex, thresholds, internal4, bit, columns);
    }

    private static void ApplyHorizontalInternal4(
        byte[] plane,
        int stride,
        int startIndex,
        Vp9LoopFilterThresholds thresholds,
        ulong internal4,
        ulong bit,
        int columns)
    {
        if ((internal4 & bit) != 0)
        {
            ApplyHorizontal4(plane, stride, startIndex + (4 * stride), thresholds, columns);
        }
    }

    public static void ApplyVertical4(byte[] plane, int stride, int startIndex, Vp9LoopFilterThresholds thresholds, int rows = 8)
    {
        for (var row = 0; row < rows; row++)
        {
            var index = startIndex + (row * stride);
            Filter4(
                FilterMask(
                    thresholds.Limit,
                    thresholds.MacroblockLimit,
                    plane[index - 4],
                    plane[index - 3],
                    plane[index - 2],
                    plane[index - 1],
                    plane[index],
                    plane[index + 1],
                    plane[index + 2],
                    plane[index + 3]),
                thresholds.HighEdgeVarianceThreshold,
                plane,
                index - 2,
                index - 1,
                index,
                index + 1);
        }
    }

    public static void ApplyHorizontal4(byte[] plane, int stride, int startIndex, Vp9LoopFilterThresholds thresholds, int columns = 8)
    {
        for (var column = 0; column < columns; column++)
        {
            var index = startIndex + column;
            Filter4(
                FilterMask(
                    thresholds.Limit,
                    thresholds.MacroblockLimit,
                    plane[index - (4 * stride)],
                    plane[index - (3 * stride)],
                    plane[index - (2 * stride)],
                    plane[index - stride],
                    plane[index],
                    plane[index + stride],
                    plane[index + (2 * stride)],
                    plane[index + (3 * stride)]),
                thresholds.HighEdgeVarianceThreshold,
                plane,
                index - (2 * stride),
                index - stride,
                index,
                index + stride);
        }
    }

    public static void ApplyVertical8(byte[] plane, int stride, int startIndex, Vp9LoopFilterThresholds thresholds, int rows = 8)
    {
        for (var row = 0; row < rows; row++)
        {
            var index = startIndex + (row * stride);
            var p3 = plane[index - 4];
            var p2 = plane[index - 3];
            var p1 = plane[index - 2];
            var p0 = plane[index - 1];
            var q0 = plane[index];
            var q1 = plane[index + 1];
            var q2 = plane[index + 2];
            var q3 = plane[index + 3];
            Filter8(
                FilterMask(
                    thresholds.Limit,
                    thresholds.MacroblockLimit,
                    p3,
                    p2,
                    p1,
                    p0,
                    q0,
                    q1,
                    q2,
                    q3),
                thresholds.HighEdgeVarianceThreshold,
                FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3),
                plane,
                index - 4,
                index - 3,
                index - 2,
                index - 1,
                index,
                index + 1,
                index + 2,
                index + 3);
        }
    }

    public static void ApplyHorizontal8(byte[] plane, int stride, int startIndex, Vp9LoopFilterThresholds thresholds, int columns = 8)
    {
        for (var column = 0; column < columns; column++)
        {
            var index = startIndex + column;
            var p3 = plane[index - (4 * stride)];
            var p2 = plane[index - (3 * stride)];
            var p1 = plane[index - (2 * stride)];
            var p0 = plane[index - stride];
            var q0 = plane[index];
            var q1 = plane[index + stride];
            var q2 = plane[index + (2 * stride)];
            var q3 = plane[index + (3 * stride)];
            Filter8(
                FilterMask(
                    thresholds.Limit,
                    thresholds.MacroblockLimit,
                    p3,
                    p2,
                    p1,
                    p0,
                    q0,
                    q1,
                    q2,
                    q3),
                thresholds.HighEdgeVarianceThreshold,
                FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3),
                plane,
                index - (4 * stride),
                index - (3 * stride),
                index - (2 * stride),
                index - stride,
                index,
                index + stride,
                index + (2 * stride),
                index + (3 * stride));
        }
    }

    public static void ApplyVertical16(byte[] plane, int stride, int startIndex, Vp9LoopFilterThresholds thresholds, int rows = 8)
    {
        for (var row = 0; row < rows; row++)
        {
            var index = startIndex + (row * stride);
            var p3 = plane[index - 4];
            var p2 = plane[index - 3];
            var p1 = plane[index - 2];
            var p0 = plane[index - 1];
            var q0 = plane[index];
            var q1 = plane[index + 1];
            var q2 = plane[index + 2];
            var q3 = plane[index + 3];
            Filter16(
                FilterMask(
                    thresholds.Limit,
                    thresholds.MacroblockLimit,
                    p3,
                    p2,
                    p1,
                    p0,
                    q0,
                    q1,
                    q2,
                    q3),
                thresholds.HighEdgeVarianceThreshold,
                FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3),
                FlatMask5(
                    1,
                    plane[index - 8],
                    plane[index - 7],
                    plane[index - 6],
                    plane[index - 5],
                    p0,
                    q0,
                    plane[index + 4],
                    plane[index + 5],
                    plane[index + 6],
                    plane[index + 7]),
                plane,
                index - 8,
                index - 7,
                index - 6,
                index - 5,
                index - 4,
                index - 3,
                index - 2,
                index - 1,
                index,
                index + 1,
                index + 2,
                index + 3,
                index + 4,
                index + 5,
                index + 6,
                index + 7);
        }
    }

    public static void ApplyHorizontal16(byte[] plane, int stride, int startIndex, Vp9LoopFilterThresholds thresholds, int columns = 8)
    {
        for (var column = 0; column < columns; column++)
        {
            var index = startIndex + column;
            var p3 = plane[index - (4 * stride)];
            var p2 = plane[index - (3 * stride)];
            var p1 = plane[index - (2 * stride)];
            var p0 = plane[index - stride];
            var q0 = plane[index];
            var q1 = plane[index + stride];
            var q2 = plane[index + (2 * stride)];
            var q3 = plane[index + (3 * stride)];
            Filter16(
                FilterMask(
                    thresholds.Limit,
                    thresholds.MacroblockLimit,
                    p3,
                    p2,
                    p1,
                    p0,
                    q0,
                    q1,
                    q2,
                    q3),
                thresholds.HighEdgeVarianceThreshold,
                FlatMask4(1, p3, p2, p1, p0, q0, q1, q2, q3),
                FlatMask5(
                    1,
                    plane[index - (8 * stride)],
                    plane[index - (7 * stride)],
                    plane[index - (6 * stride)],
                    plane[index - (5 * stride)],
                    p0,
                    q0,
                    plane[index + (4 * stride)],
                    plane[index + (5 * stride)],
                    plane[index + (6 * stride)],
                    plane[index + (7 * stride)]),
                plane,
                index - (8 * stride),
                index - (7 * stride),
                index - (6 * stride),
                index - (5 * stride),
                index - (4 * stride),
                index - (3 * stride),
                index - (2 * stride),
                index - stride,
                index,
                index + stride,
                index + (2 * stride),
                index + (3 * stride),
                index + (4 * stride),
                index + (5 * stride),
                index + (6 * stride),
                index + (7 * stride));
        }
    }

    private static int FilterMask(
        byte limit,
        byte macroblockLimit,
        byte p3,
        byte p2,
        byte p1,
        byte p0,
        byte q0,
        byte q1,
        byte q2,
        byte q3)
    {
        return Math.Abs(p3 - p2) <= limit &&
            Math.Abs(p2 - p1) <= limit &&
            Math.Abs(p1 - p0) <= limit &&
            Math.Abs(q1 - q0) <= limit &&
            Math.Abs(q2 - q1) <= limit &&
            Math.Abs(q3 - q2) <= limit &&
            ((Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2)) <= macroblockLimit
                ? -1
                : 0;
    }

    private static int FlatMask4(
        byte threshold,
        byte p3,
        byte p2,
        byte p1,
        byte p0,
        byte q0,
        byte q1,
        byte q2,
        byte q3)
    {
        return Math.Abs(p1 - p0) <= threshold &&
            Math.Abs(q1 - q0) <= threshold &&
            Math.Abs(p2 - p0) <= threshold &&
            Math.Abs(q2 - q0) <= threshold &&
            Math.Abs(p3 - p0) <= threshold &&
            Math.Abs(q3 - q0) <= threshold
                ? -1
                : 0;
    }

    private static int FlatMask5(
        byte threshold,
        byte p4,
        byte p3,
        byte p2,
        byte p1,
        byte p0,
        byte q0,
        byte q1,
        byte q2,
        byte q3,
        byte q4)
    {
        return FlatMask4(threshold, p3, p2, p1, p0, q0, q1, q2, q3) != 0 &&
            Math.Abs(p4 - p0) <= threshold &&
            Math.Abs(q4 - q0) <= threshold
                ? -1
                : 0;
    }

    private static int HevMask(byte threshold, byte p1, byte p0, byte q0, byte q1)
    {
        return Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold
            ? -1
            : 0;
    }

    private static void Filter4(int mask, byte threshold, byte[] plane, int op1, int op0, int oq0, int oq1)
    {
        var ps1 = ToSigned(plane[op1]);
        var ps0 = ToSigned(plane[op0]);
        var qs0 = ToSigned(plane[oq0]);
        var qs1 = ToSigned(plane[oq1]);
        var hev = HevMask(threshold, plane[op1], plane[op0], plane[oq0], plane[oq1]);

        var filter = SignedCharClamp(ps1 - qs1) & hev;
        filter = SignedCharClamp(filter + (3 * (qs0 - ps0))) & mask;

        var filter1 = SignedCharClamp(filter + 4) >> 3;
        var filter2 = SignedCharClamp(filter + 3) >> 3;

        plane[oq0] = FromSigned(SignedCharClamp(qs0 - filter1));
        plane[op0] = FromSigned(SignedCharClamp(ps0 + filter2));

        filter = RoundPowerOfTwo(filter1, 1) & ~hev;
        plane[oq1] = FromSigned(SignedCharClamp(qs1 - filter));
        plane[op1] = FromSigned(SignedCharClamp(ps1 + filter));
    }

    private static void Filter8(
        int mask,
        byte threshold,
        int flat,
        byte[] plane,
        int op3,
        int op2,
        int op1,
        int op0,
        int oq0,
        int oq1,
        int oq2,
        int oq3)
    {
        if (flat != 0 && mask != 0)
        {
            var p3 = plane[op3];
            var p2 = plane[op2];
            var p1 = plane[op1];
            var p0 = plane[op0];
            var q0 = plane[oq0];
            var q1 = plane[oq1];
            var q2 = plane[oq2];
            var q3 = plane[oq3];

            plane[op2] = (byte)RoundPowerOfTwo((p3 * 3) + (2 * p2) + p1 + p0 + q0, 3);
            plane[op1] = (byte)RoundPowerOfTwo((p3 * 2) + p2 + (2 * p1) + p0 + q0 + q1, 3);
            plane[op0] = (byte)RoundPowerOfTwo(p3 + p2 + p1 + (2 * p0) + q0 + q1 + q2, 3);
            plane[oq0] = (byte)RoundPowerOfTwo(p2 + p1 + p0 + (2 * q0) + q1 + q2 + q3, 3);
            plane[oq1] = (byte)RoundPowerOfTwo(p1 + p0 + q0 + (2 * q1) + q2 + (2 * q3), 3);
            plane[oq2] = (byte)RoundPowerOfTwo(p0 + q0 + q1 + (2 * q2) + (3 * q3), 3);
            return;
        }

        Filter4(mask, threshold, plane, op1, op0, oq0, oq1);
    }

    private static void Filter16(
        int mask,
        byte threshold,
        int flat,
        int flat2,
        byte[] plane,
        int op7,
        int op6,
        int op5,
        int op4,
        int op3,
        int op2,
        int op1,
        int op0,
        int oq0,
        int oq1,
        int oq2,
        int oq3,
        int oq4,
        int oq5,
        int oq6,
        int oq7)
    {
        if (flat2 != 0 && flat != 0 && mask != 0)
        {
            var p7 = plane[op7];
            var p6 = plane[op6];
            var p5 = plane[op5];
            var p4 = plane[op4];
            var p3 = plane[op3];
            var p2 = plane[op2];
            var p1 = plane[op1];
            var p0 = plane[op0];
            var q0 = plane[oq0];
            var q1 = plane[oq1];
            var q2 = plane[oq2];
            var q3 = plane[oq3];
            var q4 = plane[oq4];
            var q5 = plane[oq5];
            var q6 = plane[oq6];
            var q7 = plane[oq7];

            plane[op6] = (byte)RoundPowerOfTwo((p7 * 7) + (p6 * 2) + p5 + p4 + p3 + p2 + p1 + p0 + q0, 4);
            plane[op5] = (byte)RoundPowerOfTwo((p7 * 6) + p6 + (p5 * 2) + p4 + p3 + p2 + p1 + p0 + q0 + q1, 4);
            plane[op4] = (byte)RoundPowerOfTwo((p7 * 5) + p6 + p5 + (p4 * 2) + p3 + p2 + p1 + p0 + q0 + q1 + q2, 4);
            plane[op3] = (byte)RoundPowerOfTwo((p7 * 4) + p6 + p5 + p4 + (p3 * 2) + p2 + p1 + p0 + q0 + q1 + q2 + q3, 4);
            plane[op2] = (byte)RoundPowerOfTwo((p7 * 3) + p6 + p5 + p4 + p3 + (p2 * 2) + p1 + p0 + q0 + q1 + q2 + q3 + q4, 4);
            plane[op1] = (byte)RoundPowerOfTwo((p7 * 2) + p6 + p5 + p4 + p3 + p2 + (p1 * 2) + p0 + q0 + q1 + q2 + q3 + q4 + q5, 4);
            plane[op0] = (byte)RoundPowerOfTwo(p7 + p6 + p5 + p4 + p3 + p2 + p1 + (p0 * 2) + q0 + q1 + q2 + q3 + q4 + q5 + q6, 4);
            plane[oq0] = (byte)RoundPowerOfTwo(p6 + p5 + p4 + p3 + p2 + p1 + p0 + (q0 * 2) + q1 + q2 + q3 + q4 + q5 + q6 + q7, 4);
            plane[oq1] = (byte)RoundPowerOfTwo(p5 + p4 + p3 + p2 + p1 + p0 + q0 + (q1 * 2) + q2 + q3 + q4 + q5 + q6 + (q7 * 2), 4);
            plane[oq2] = (byte)RoundPowerOfTwo(p4 + p3 + p2 + p1 + p0 + q0 + q1 + (q2 * 2) + q3 + q4 + q5 + q6 + (q7 * 3), 4);
            plane[oq3] = (byte)RoundPowerOfTwo(p3 + p2 + p1 + p0 + q0 + q1 + q2 + (q3 * 2) + q4 + q5 + q6 + (q7 * 4), 4);
            plane[oq4] = (byte)RoundPowerOfTwo(p2 + p1 + p0 + q0 + q1 + q2 + q3 + (q4 * 2) + q5 + q6 + (q7 * 5), 4);
            plane[oq5] = (byte)RoundPowerOfTwo(p1 + p0 + q0 + q1 + q2 + q3 + q4 + (q5 * 2) + q6 + (q7 * 6), 4);
            plane[oq6] = (byte)RoundPowerOfTwo(p0 + q0 + q1 + q2 + q3 + q4 + q5 + (q6 * 2) + (q7 * 7), 4);
            return;
        }

        Filter8(mask, threshold, flat, plane, op3, op2, op1, op0, oq0, oq1, oq2, oq3);
    }

    private static int SignedCharClamp(int value)
    {
        return Math.Clamp(value, -128, 127);
    }

    private static int ToSigned(byte value)
    {
        return unchecked((sbyte)(value ^ 0x80));
    }

    private static byte FromSigned(int value)
    {
        return (byte)(unchecked((byte)(sbyte)value) ^ 0x80);
    }

    private static int RoundPowerOfTwo(int value, int bit)
    {
        return (value + (1 << (bit - 1))) >> bit;
    }
}
