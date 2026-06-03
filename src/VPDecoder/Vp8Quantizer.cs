namespace VPDecoder;

internal static class Vp8Quantizer
{
    private static ReadOnlySpan<short> DcLookup =>
    [
        4, 5, 6, 7, 8, 9, 10, 10, 11, 12, 13, 14, 15, 16, 17, 17,
        18, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 25, 25, 26, 27, 28,
        29, 30, 31, 32, 33, 34, 35, 36, 37, 37, 38, 39, 40, 41, 42, 43,
        44, 45, 46, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58,
        59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74,
        75, 76, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89,
        91, 93, 95, 96, 98, 100, 101, 102, 104, 106, 108, 110, 112, 114, 116, 118,
        122, 124, 126, 128, 130, 132, 134, 136, 138, 140, 143, 145, 148, 151, 154, 157
    ];

    private static ReadOnlySpan<short> AcLookup =>
    [
        4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
        20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35,
        36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51,
        52, 53, 54, 55, 56, 57, 58, 60, 62, 64, 66, 68, 70, 72, 74, 76,
        78, 80, 82, 84, 86, 88, 90, 92, 94, 96, 98, 100, 102, 104, 106, 108,
        110, 112, 114, 116, 119, 122, 125, 128, 131, 134, 137, 140, 143, 146, 149, 152,
        155, 158, 161, 164, 167, 170, 173, 177, 181, 185, 189, 193, 197, 201, 205, 209,
        213, 217, 221, 225, 229, 234, 239, 245, 249, 254, 259, 264, 269, 274, 279, 284
    ];

    public static Vp8DequantFactors CreateDequantFactors(
        Vp8QuantizationHeader quantization,
        Vp8SegmentationHeader segmentation,
        int segmentId)
    {
        var qIndex = GetSegmentQIndex(quantization.YAcQuantizerIndex, segmentation, segmentId);
        return new Vp8DequantFactors(
            DcQuant(qIndex, quantization.YDcDelta),
            AcYQuant(qIndex),
            Dc2Quant(qIndex, quantization.Y2DcDelta),
            Ac2Quant(qIndex, quantization.Y2AcDelta),
            DcUvQuant(qIndex, quantization.UvDcDelta),
            AcUvQuant(qIndex, quantization.UvAcDelta));
    }

    private static int GetSegmentQIndex(int baseQIndex, Vp8SegmentationHeader segmentation, int segmentId)
    {
        if (segmentId is < 0 or >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentId));
        }

        if (!segmentation.Enabled || !segmentation.UpdateFeatureData)
        {
            return ClampQIndex(baseQIndex);
        }

        var update = segmentation.QuantizerUpdates[segmentId];
        return ClampQIndex(segmentation.AbsoluteDeltaMode ? update : baseQIndex + update);
    }

    private static int DcQuant(int qIndex, int delta)
    {
        return DcLookup[ClampQIndex(qIndex + delta)];
    }

    private static int Dc2Quant(int qIndex, int delta)
    {
        return DcLookup[ClampQIndex(qIndex + delta)] * 2;
    }

    private static int DcUvQuant(int qIndex, int delta)
    {
        return Math.Min((int)DcLookup[ClampQIndex(qIndex + delta)], 132);
    }

    private static int AcYQuant(int qIndex)
    {
        return AcLookup[ClampQIndex(qIndex)];
    }

    private static int Ac2Quant(int qIndex, int delta)
    {
        return Math.Max((AcLookup[ClampQIndex(qIndex + delta)] * 101_581) >> 16, 8);
    }

    private static int AcUvQuant(int qIndex, int delta)
    {
        return AcLookup[ClampQIndex(qIndex + delta)];
    }

    private static int ClampQIndex(int qIndex)
    {
        return Math.Clamp(qIndex, 0, 127);
    }
}

internal sealed record Vp8DequantFactors(
    int Y1Dc,
    int Y1Ac,
    int Y2Dc,
    int Y2Ac,
    int UvDc,
    int UvAc);
