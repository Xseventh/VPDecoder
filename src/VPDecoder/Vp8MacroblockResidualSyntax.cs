namespace VPDecoder;

internal static class Vp8MacroblockResidualSyntax
{
    public static Vp8MacroblockResidual ReadMacroblock(
        ref Vp8BoolReader reader,
        Vp8CoefficientProbabilityContext probabilities,
        Vp8ResidualEntropyContext entropyContext,
        Vp8KeyFrameMacroblockMode mode)
    {
        if (mode.SkipCoefficients)
        {
            entropyContext.ClearMacroblock(mode.Column);
            return new Vp8MacroblockResidual(mode.Row, mode.Column, Skipped: true, Blocks: []);
        }

        if (mode.Column == 0)
        {
            entropyContext.ResetLeft();
        }

        var blocks = new List<Vp8ResidualBlockProbe>(mode.YMode == Vp8MacroblockPredictionMode.BPred ? 24 : 25);
        var yBlockType = 3;
        var skipY1Dc = false;
        if (mode.YMode != Vp8MacroblockPredictionMode.BPred)
        {
            var y2Block = ReadContextualBlock(
                ref reader,
                probabilities,
                entropyContext,
                mode.Column,
                contextIndex: 8,
                blockIndex: 24,
                blockType: 1,
                startCoefficient: 0);
            blocks.Add(y2Block);
            yBlockType = 0;
            skipY1Dc = true;
        }

        for (var i = 0; i < 16; i++)
        {
            var contextIndex = i & 3;
            var leftContextIndex = (i & 0xc) >> 2;
            blocks.Add(ReadContextualBlock(
                ref reader,
                probabilities,
                entropyContext,
                mode.Column,
                contextIndex,
                leftContextIndex,
                i,
                yBlockType,
                skipY1Dc ? 1 : 0));
        }

        for (var i = 16; i < 24; i++)
        {
            var contextIndex = 4 + (((i > 19) ? 1 : 0) << 1) + (i & 1);
            var leftContextIndex = 4 +
                (((i > 19) ? 1 : 0) << 1) +
                (((i & 3) > 1) ? 1 : 0);
            blocks.Add(ReadContextualBlock(
                ref reader,
                probabilities,
                entropyContext,
                mode.Column,
                contextIndex,
                leftContextIndex,
                i,
                blockType: 2,
                startCoefficient: 0));
        }

        return new Vp8MacroblockResidual(mode.Row, mode.Column, Skipped: false, blocks);
    }

    private static Vp8ResidualBlockProbe ReadContextualBlock(
        ref Vp8BoolReader reader,
        Vp8CoefficientProbabilityContext probabilities,
        Vp8ResidualEntropyContext entropyContext,
        int macroblockColumn,
        int contextIndex,
        int blockIndex,
        int blockType,
        int startCoefficient)
    {
        return ReadContextualBlock(
            ref reader,
            probabilities,
            entropyContext,
            macroblockColumn,
            contextIndex,
            contextIndex,
            blockIndex,
            blockType,
            startCoefficient);
    }

    private static Vp8ResidualBlockProbe ReadContextualBlock(
        ref Vp8BoolReader reader,
        Vp8CoefficientProbabilityContext probabilities,
        Vp8ResidualEntropyContext entropyContext,
        int macroblockColumn,
        int aboveContextIndex,
        int leftContextIndex,
        int blockIndex,
        int blockType,
        int startCoefficient)
    {
        var initialContext = entropyContext.GetAbove(macroblockColumn, aboveContextIndex) +
            entropyContext.GetLeft(leftContextIndex);
        var block = Vp8ResidualSyntax.ReadBlock(
            ref reader,
            probabilities,
            blockType,
            initialContext,
            startCoefficient);
        var hasNonZero = block.Eob > 0;
        entropyContext.SetAbove(macroblockColumn, aboveContextIndex, hasNonZero);
        entropyContext.SetLeft(leftContextIndex, hasNonZero);

        return new Vp8ResidualBlockProbe(
            blockIndex,
            blockType,
            startCoefficient,
            initialContext,
            startCoefficient == 1 ? block.Eob + 1 : block.Eob,
            block);
    }
}

internal sealed class Vp8ResidualEntropyContext
{
    private const int ContextsPerMacroblock = 9;

    private readonly byte[] _above;
    private readonly byte[] _left = new byte[ContextsPerMacroblock];

    private Vp8ResidualEntropyContext(int macroblockColumns)
    {
        MacroblockColumns = macroblockColumns;
        _above = new byte[checked(macroblockColumns * ContextsPerMacroblock)];
    }

    public int MacroblockColumns { get; }

    public static Vp8ResidualEntropyContext Create(int macroblockColumns)
    {
        if (macroblockColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(macroblockColumns));
        }

        return new Vp8ResidualEntropyContext(macroblockColumns);
    }

    public void ResetLeft()
    {
        Array.Clear(_left);
    }

    public void ClearMacroblock(int macroblockColumn)
    {
        ValidateMacroblockColumn(macroblockColumn);
        _above.AsSpan(macroblockColumn * ContextsPerMacroblock, ContextsPerMacroblock).Clear();
        Array.Clear(_left);
    }

    public int GetAbove(int macroblockColumn, int contextIndex)
    {
        return _above[GetAboveIndex(macroblockColumn, contextIndex)];
    }

    public int GetLeft(int contextIndex)
    {
        ValidateContextIndex(contextIndex);
        return _left[contextIndex];
    }

    public void SetAbove(int macroblockColumn, int contextIndex, bool hasNonZero)
    {
        _above[GetAboveIndex(macroblockColumn, contextIndex)] = hasNonZero ? (byte)1 : (byte)0;
    }

    public void SetLeft(int contextIndex, bool hasNonZero)
    {
        ValidateContextIndex(contextIndex);
        _left[contextIndex] = hasNonZero ? (byte)1 : (byte)0;
    }

    private int GetAboveIndex(int macroblockColumn, int contextIndex)
    {
        ValidateMacroblockColumn(macroblockColumn);
        ValidateContextIndex(contextIndex);
        return (macroblockColumn * ContextsPerMacroblock) + contextIndex;
    }

    private void ValidateMacroblockColumn(int macroblockColumn)
    {
        if (macroblockColumn < 0 || macroblockColumn >= MacroblockColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(macroblockColumn));
        }
    }

    private static void ValidateContextIndex(int contextIndex)
    {
        if (contextIndex is < 0 or >= ContextsPerMacroblock)
        {
            throw new ArgumentOutOfRangeException(nameof(contextIndex));
        }
    }
}

internal sealed record Vp8MacroblockResidual(
    int Row,
    int Column,
    bool Skipped,
    IReadOnlyList<Vp8ResidualBlockProbe> Blocks);

internal sealed record Vp8ResidualBlockProbe(
    int BlockIndex,
    int BlockType,
    int StartCoefficient,
    int InitialContext,
    int EffectiveEob,
    Vp8CoefficientBlock Block);
