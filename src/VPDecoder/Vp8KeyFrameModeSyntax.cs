namespace VPDecoder;

internal static class Vp8KeyFrameModeSyntax
{
    private static ReadOnlySpan<sbyte> KeyFrameYModeTree =>
    [
        -(sbyte)Vp8MacroblockPredictionMode.BPred, 2,
        4, 6,
        -(sbyte)Vp8MacroblockPredictionMode.Dc, -(sbyte)Vp8MacroblockPredictionMode.Vertical,
        -(sbyte)Vp8MacroblockPredictionMode.Horizontal, -(sbyte)Vp8MacroblockPredictionMode.TrueMotion
    ];

    private static ReadOnlySpan<byte> KeyFrameYModeProbabilities => [145, 156, 163, 128];

    private static ReadOnlySpan<sbyte> UvModeTree =>
    [
        -(sbyte)Vp8MacroblockPredictionMode.Dc, 2,
        -(sbyte)Vp8MacroblockPredictionMode.Vertical, 4,
        -(sbyte)Vp8MacroblockPredictionMode.Horizontal, -(sbyte)Vp8MacroblockPredictionMode.TrueMotion
    ];

    private static ReadOnlySpan<byte> KeyFrameUvModeProbabilities => [142, 114, 183];

    public static IReadOnlyList<Vp8KeyFrameMacroblockMode> ReadMacroblockModes(
        ref Vp8BoolReader reader,
        int width,
        int height,
        Vp8KeyFrameSyntaxHeader syntaxHeader)
    {
        var macroblockColumns = (width + 15) >> 4;
        var macroblockRows = (height + 15) >> 4;
        var macroblocks = new Vp8KeyFrameMacroblockMode[macroblockColumns * macroblockRows];
        var index = 0;

        for (var row = 0; row < macroblockRows; row++)
        {
            for (var column = 0; column < macroblockColumns; column++)
            {
                macroblocks[index++] = ReadMacroblockMode(ref reader, row, column, syntaxHeader);
            }
        }

        return macroblocks;
    }

    private static Vp8KeyFrameMacroblockMode ReadMacroblockMode(
        ref Vp8BoolReader reader,
        int row,
        int column,
        Vp8KeyFrameSyntaxHeader syntaxHeader)
    {
        var segmentId = ReadSegmentId(ref reader, syntaxHeader);
        var skipCoefficients = syntaxHeader.MbNoCoeffSkip &&
            reader.Read(syntaxHeader.ProbSkipFalse ?? 0);
        var yMode = (Vp8MacroblockPredictionMode)Vp8TreeReader.ReadTree(
            ref reader,
            KeyFrameYModeTree,
            KeyFrameYModeProbabilities,
            "key-frame Y mode");

        if (yMode == Vp8MacroblockPredictionMode.BPred)
        {
            throw new Vp8BoolReaderException(
                Vp8DecodeDiagnostic.UnsupportedFeature(
                    "VP8 key-frame 4x4 B_PRED macroblock modes are not supported yet."));
        }

        var uvMode = (Vp8MacroblockPredictionMode)Vp8TreeReader.ReadTree(
            ref reader,
            UvModeTree,
            KeyFrameUvModeProbabilities,
            "key-frame UV mode");

        return new Vp8KeyFrameMacroblockMode(
            row,
            column,
            segmentId,
            skipCoefficients,
            yMode,
            uvMode);
    }

    private static int ReadSegmentId(ref Vp8BoolReader reader, Vp8KeyFrameSyntaxHeader syntaxHeader)
    {
        if (!syntaxHeader.Segmentation.Enabled || !syntaxHeader.Segmentation.UpdateMap)
        {
            return 0;
        }

        var probabilities = syntaxHeader.Segmentation.SegmentTreeProbabilities;
        var p0 = probabilities[0] ?? byte.MaxValue;
        var p1 = probabilities[1] ?? byte.MaxValue;
        var p2 = probabilities[2] ?? byte.MaxValue;
        return reader.Read(p0)
            ? 2 + (reader.Read(p2) ? 1 : 0)
            : reader.Read(p1) ? 1 : 0;
    }
}

internal sealed record Vp8KeyFrameMacroblockMode(
    int Row,
    int Column,
    int SegmentId,
    bool SkipCoefficients,
    Vp8MacroblockPredictionMode YMode,
    Vp8MacroblockPredictionMode UvMode);

internal enum Vp8MacroblockPredictionMode
{
    Dc = 0,
    Vertical = 1,
    Horizontal = 2,
    TrueMotion = 3,
    BPred = 4
}
