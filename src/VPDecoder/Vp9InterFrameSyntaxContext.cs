namespace VPDecoder;

internal sealed class Vp9InterFrameSyntaxContext
{
    private static ReadOnlySpan<sbyte> FirstMotionVectorReferencePositions =>
    [
        -1, 0, 0, -1,
        -1, 0, 0, -1,
        -1, 0, 0, -1,
        -1, 0, 0, -1,
        0, -1, -1, 0,
        -1, 0, 0, -1,
        -1, 0, 0, -1,
        0, -1, -1, 0,
        -1, 0, 0, -1,
        -1, 1, 1, -1,
        0, -1, -1, 0,
        -1, 0, 0, -1,
        -1, 3, 3, -1
    ];

    private static ReadOnlySpan<byte> CounterToInterModeContext =>
    [
        2, 3, 4, 1, 3, 9, 0, 9, 9, 5, 5, 9, 5, 9, 9, 9, 9, 9, 6
    ];

    private readonly int _miColumns;
    private readonly int _miRows;
    private readonly Vp9InterModeInfoContextEntry?[] _modeInfoGrid;
    private readonly byte[] _abovePartitionContext;
    private readonly byte[] _leftPartitionContext = new byte[8];

    private Vp9InterFrameSyntaxContext(int miColumns, int miRows)
    {
        _miColumns = miColumns;
        _miRows = miRows;
        _modeInfoGrid = new Vp9InterModeInfoContextEntry?[checked(miColumns * miRows)];
        _abovePartitionContext = new byte[miColumns];
    }

    public static Vp9InterFrameSyntaxContext Create(Vp9FrameHeader header)
    {
        return new Vp9InterFrameSyntaxContext(header.TileInfo.MiColumns, header.TileInfo.MiRows);
    }

    public void ResetLeftPartitionContext()
    {
        Array.Clear(_leftPartitionContext);
    }

    public int GetPartitionContext(int miRow, int miColumn, Vp9BlockSize blockSize)
    {
        ValidateMiPosition(miRow, miColumn);
        var above = _abovePartitionContext[miColumn];
        var left = _leftPartitionContext[miRow & 7];
        return Vp9PartitionSyntax.GetPartitionContext(
            above,
            left,
            Vp9ModeInfoSyntax.GetPartitionContextLog2(blockSize));
    }

    public void UpdatePartitionContext(int miRow, int miColumn, Vp9BlockSize partitionBlockSize, Vp9BlockSize subsize)
    {
        ValidateMiPosition(miRow, miColumn);
        var widthInMiUnits = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(partitionBlockSize);
        var heightInMiUnits = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(partitionBlockSize);
        var update = GetPartitionContextUpdate(subsize);
        var aboveWidth = Math.Min(widthInMiUnits, _miColumns - miColumn);
        for (var i = 0; i < aboveWidth; i++)
        {
            _abovePartitionContext[miColumn + i] = update.Above;
        }

        for (var i = 0; i < heightInMiUnits; i++)
        {
            _leftPartitionContext[(miRow + i) & 7] = update.Left;
        }
    }

    public int GetSkipContext(int miRow, int miColumn, int tileMiColumnStart)
    {
        ValidateMiPosition(miRow, miColumn);
        var above = TryGetAbove(miRow, miColumn, out var aboveInfo) && aboveInfo.Skip ? 1 : 0;
        var left = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo) && leftInfo.Skip ? 1 : 0;
        return above + left;
    }

    public int GetTransformSizeContext(int miRow, int miColumn, int tileMiColumnStart, Vp9BlockSize blockSize)
    {
        ValidateMiPosition(miRow, miColumn);
        var maxTransformSize = Vp9ModeInfoSyntax.GetMaximumTransformSize(blockSize);
        var max = (int)maxTransformSize;
        var hasAbove = TryGetAbove(miRow, miColumn, out var aboveInfo);
        var hasLeft = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo);
        var above = hasAbove && !aboveInfo.Skip ? (int)aboveInfo.TransformSize : max;
        var left = hasLeft && !leftInfo.Skip ? (int)leftInfo.TransformSize : max;

        if (!hasLeft)
        {
            left = above;
        }

        if (!hasAbove)
        {
            above = left;
        }

        return (above + left) > max ? 1 : 0;
    }

    public int GetIntraInterContext(int miRow, int miColumn, int tileMiColumnStart)
    {
        ValidateMiPosition(miRow, miColumn);
        var hasAbove = TryGetAbove(miRow, miColumn, out var aboveInfo);
        var hasLeft = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo);

        if (hasAbove && hasLeft)
        {
            var aboveIntra = !aboveInfo.IsInterBlock;
            var leftIntra = !leftInfo.IsInterBlock;
            return leftIntra && aboveIntra ? 3 : leftIntra || aboveIntra ? 1 : 0;
        }

        if (hasAbove || hasLeft)
        {
            var edgeInfo = hasAbove ? aboveInfo : leftInfo;
            return edgeInfo.IsInterBlock ? 0 : 2;
        }

        return 0;
    }

    public (int Context0, int Context1) GetSingleReferenceContexts(
        int miRow,
        int miColumn,
        int tileMiColumnStart)
    {
        ValidateMiPosition(miRow, miColumn);
        var hasAbove = TryGetAbove(miRow, miColumn, out var aboveInfo);
        var hasLeft = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo);
        return (
            GetSingleReferenceContext0(hasAbove, aboveInfo, hasLeft, leftInfo),
            GetSingleReferenceContext1(hasAbove, aboveInfo, hasLeft, leftInfo));
    }

    public int GetCompoundInterContext(
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        Vp9InterReferenceFrame fixedReferenceFrame)
    {
        ValidateMiPosition(miRow, miColumn);
        var hasAbove = TryGetAbove(miRow, miColumn, out var aboveInfo);
        var hasLeft = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo);
        if (hasAbove && hasLeft)
        {
            if (!aboveInfo.IsCompoundReference && !leftInfo.IsCompoundReference)
            {
                return BoolToInt(aboveInfo.IsInterBlock && aboveInfo.ReferenceFrame == fixedReferenceFrame) ^
                    BoolToInt(leftInfo.IsInterBlock && leftInfo.ReferenceFrame == fixedReferenceFrame);
            }

            if (!aboveInfo.IsCompoundReference)
            {
                return 2 + BoolToInt(aboveInfo.ReferenceFrame == fixedReferenceFrame || !aboveInfo.IsInterBlock);
            }

            if (!leftInfo.IsCompoundReference)
            {
                return 2 + BoolToInt(leftInfo.ReferenceFrame == fixedReferenceFrame || !leftInfo.IsInterBlock);
            }

            return 4;
        }

        if (hasAbove || hasLeft)
        {
            var edgeInfo = hasAbove ? aboveInfo : leftInfo;
            return edgeInfo.IsCompoundReference
                ? 3
                : BoolToInt(edgeInfo.IsInterBlock && edgeInfo.ReferenceFrame == fixedReferenceFrame);
        }

        return 1;
    }

    public int GetCompoundReferenceContext(
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        Vp9CompoundReferenceSetup referenceSetup,
        IReadOnlyList<bool> referenceFrameSignBiases)
    {
        ValidateMiPosition(miRow, miColumn);
        var hasAbove = TryGetAbove(miRow, miColumn, out var aboveInfo);
        var hasLeft = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo);
        var fixedReferenceFrame = referenceSetup.FixedReferenceFrame;
        var variableReference0 = referenceSetup.VariableReferenceFrame0;
        var variableReference1 = referenceSetup.VariableReferenceFrame1;
        var fixedReferenceSignBias = GetReferenceFrameSignBias(referenceFrameSignBiases, fixedReferenceFrame);
        if (hasAbove && hasLeft)
        {
            var aboveIntra = !aboveInfo.IsInterBlock;
            var leftIntra = !leftInfo.IsInterBlock;
            if (aboveIntra && leftIntra)
            {
                return 2;
            }

            if (aboveIntra || leftIntra)
            {
                var edgeInfo = aboveIntra ? leftInfo : aboveInfo;
                var edgeReference = GetVariableReferenceFrame(edgeInfo, fixedReferenceSignBias);
                return 1 + (2 * BoolToInt(edgeReference != variableReference1));
            }

            var aboveSingleReference = !aboveInfo.IsCompoundReference;
            var leftSingleReference = !leftInfo.IsCompoundReference;
            var aboveReference = GetVariableReferenceFrame(aboveInfo, fixedReferenceSignBias);
            var leftReference = GetVariableReferenceFrame(leftInfo, fixedReferenceSignBias);
            if (aboveReference == leftReference && variableReference1 == aboveReference)
            {
                return 0;
            }

            if (leftSingleReference && aboveSingleReference)
            {
                if ((aboveReference == fixedReferenceFrame && leftReference == variableReference0) ||
                    (leftReference == fixedReferenceFrame && aboveReference == variableReference0))
                {
                    return 4;
                }

                return aboveReference == leftReference ? 3 : 1;
            }

            if (leftSingleReference || aboveSingleReference)
            {
                var compoundReference = leftSingleReference ? aboveReference : leftReference;
                var singleReference = leftSingleReference ? leftReference : aboveReference;
                if (compoundReference == variableReference1 && singleReference != variableReference1)
                {
                    return 1;
                }

                if (singleReference == variableReference1 && compoundReference != variableReference1)
                {
                    return 2;
                }

                return 4;
            }

            return aboveReference == leftReference ? 4 : 2;
        }

        if (hasAbove || hasLeft)
        {
            var edgeInfo = hasAbove ? aboveInfo : leftInfo;
            if (!edgeInfo.IsInterBlock)
            {
                return 2;
            }

            var edgeReference = GetVariableReferenceFrame(edgeInfo, fixedReferenceSignBias);
            return edgeInfo.IsCompoundReference
                ? 4 * BoolToInt(edgeReference != variableReference1)
                : 3 * BoolToInt(edgeReference != variableReference1);
        }

        return 2;
    }

    public int GetInterModeContext(
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        int tileMiColumnStart,
        int tileMiColumnEnd)
    {
        ValidateMiPosition(miRow, miColumn);
        var positionOffset = (int)blockSize * 4;
        var contextCounter = 0;
        for (var i = 0; i < 2; i++)
        {
            var rowOffset = FirstMotionVectorReferencePositions[positionOffset + (i * 2)];
            var columnOffset = FirstMotionVectorReferencePositions[positionOffset + (i * 2) + 1];
            var candidateRow = miRow + rowOffset;
            var candidateColumn = miColumn + columnOffset;
            if (TryGetCandidate(candidateRow, candidateColumn, tileMiColumnStart, tileMiColumnEnd, out var candidate))
            {
                contextCounter += GetInterModeCounter(candidate);
            }
        }

        var context = CounterToInterModeContext[contextCounter];
        if (context == 9)
        {
            throw new InvalidOperationException("VP9 inter mode context counter resolved to an invalid context.");
        }

        return context;
    }

    public int GetSwitchableInterpolationContext(int miRow, int miColumn, int tileMiColumnStart)
    {
        ValidateMiPosition(miRow, miColumn);
        var leftType = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo)
            ? GetSwitchableInterpolationType(leftInfo)
            : Vp9FrameContextConstants.SwitchableFilters;
        var aboveType = TryGetAbove(miRow, miColumn, out var aboveInfo)
            ? GetSwitchableInterpolationType(aboveInfo)
            : Vp9FrameContextConstants.SwitchableFilters;

        if (leftType == aboveType)
        {
            return leftType;
        }

        if (leftType == Vp9FrameContextConstants.SwitchableFilters)
        {
            return aboveType;
        }

        if (aboveType == Vp9FrameContextConstants.SwitchableFilters)
        {
            return leftType;
        }

        return Vp9FrameContextConstants.SwitchableFilters;
    }

    public void SetModeInfo(int miRow, int miColumn, Vp9InterModeInfoProbe modeInfo)
    {
        ValidateMiPosition(miRow, miColumn);
        var width = Math.Min(Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeInfo.BlockSize), _miColumns - miColumn);
        var height = Math.Min(Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeInfo.BlockSize), _miRows - miRow);
        var entry = new Vp9InterModeInfoContextEntry(
            modeInfo.Skip,
            modeInfo.TransformSize,
            modeInfo.IsInterBlock,
            modeInfo.ReferenceMode,
            modeInfo.ReferenceFrame,
            modeInfo.CompoundReferenceFrame,
            modeInfo.PredictionMode,
            modeInfo.InterpolationFilter);
        for (var row = 0; row < height; row++)
        {
            var offset = ((miRow + row) * _miColumns) + miColumn;
            for (var column = 0; column < width; column++)
            {
                _modeInfoGrid[offset + column] = entry;
            }
        }
    }

    private bool TryGetAbove(int miRow, int miColumn, out Vp9InterModeInfoContextEntry entry)
    {
        if (miRow > 0)
        {
            var existing = _modeInfoGrid[((miRow - 1) * _miColumns) + miColumn];
            if (existing.HasValue)
            {
                entry = existing.Value;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private bool TryGetCandidate(
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        int tileMiColumnEnd,
        out Vp9InterModeInfoContextEntry entry)
    {
        if (miRow < 0 || miRow >= _miRows || miColumn < tileMiColumnStart || miColumn >= tileMiColumnEnd)
        {
            entry = default;
            return false;
        }

        var existing = _modeInfoGrid[(miRow * _miColumns) + miColumn];
        if (existing.HasValue)
        {
            entry = existing.Value;
            return true;
        }

        entry = default;
        return false;
    }

    private bool TryGetLeft(
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        out Vp9InterModeInfoContextEntry entry)
    {
        if (miColumn > tileMiColumnStart)
        {
            var existing = _modeInfoGrid[(miRow * _miColumns) + miColumn - 1];
            if (existing.HasValue)
            {
                entry = existing.Value;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private void ValidateMiPosition(int miRow, int miColumn)
    {
        if (miRow < 0 || miRow >= _miRows)
        {
            throw new ArgumentOutOfRangeException(nameof(miRow), "VP9 MI row is outside the frame.");
        }

        if (miColumn < 0 || miColumn >= _miColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(miColumn), "VP9 MI column is outside the frame.");
        }
    }

    private static (byte Above, byte Left) GetPartitionContextUpdate(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block4X4 => (15, 15),
            Vp9BlockSize.Block4X8 => (15, 14),
            Vp9BlockSize.Block8X4 => (14, 15),
            Vp9BlockSize.Block8X8 => (14, 14),
            Vp9BlockSize.Block8X16 => (14, 12),
            Vp9BlockSize.Block16X8 => (12, 14),
            Vp9BlockSize.Block16X16 => (12, 12),
            Vp9BlockSize.Block16X32 => (12, 8),
            Vp9BlockSize.Block32X16 => (8, 12),
            Vp9BlockSize.Block32X32 => (8, 8),
            Vp9BlockSize.Block32X64 => (8, 0),
            Vp9BlockSize.Block64X32 => (0, 8),
            Vp9BlockSize.Block64X64 => (0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "Unsupported VP9 block size.")
        };
    }

    private static int GetSingleReferenceContext0(
        bool hasAbove,
        Vp9InterModeInfoContextEntry aboveInfo,
        bool hasLeft,
        Vp9InterModeInfoContextEntry leftInfo)
    {
        if (hasAbove && hasLeft)
        {
            var aboveIntra = !aboveInfo.IsInterBlock;
            var leftIntra = !leftInfo.IsInterBlock;
            if (aboveIntra && leftIntra)
            {
                return 2;
            }

            if (aboveIntra || leftIntra)
            {
                var edgeInfo = aboveIntra ? leftInfo : aboveInfo;
                return edgeInfo.IsCompoundReference
                    ? 1 + BoolToInt(IsLastReference(edgeInfo))
                    : 4 * BoolToInt(edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Last);
            }

            if (aboveInfo.IsCompoundReference && leftInfo.IsCompoundReference)
            {
                return 1 + BoolToInt(IsLastReference(aboveInfo) || IsLastReference(leftInfo));
            }

            if (aboveInfo.IsCompoundReference || leftInfo.IsCompoundReference)
            {
                var singleInfo = aboveInfo.IsCompoundReference ? leftInfo : aboveInfo;
                var compoundInfo = aboveInfo.IsCompoundReference ? aboveInfo : leftInfo;
                return singleInfo.ReferenceFrame == Vp9InterReferenceFrame.Last
                    ? 3 + BoolToInt(IsLastReference(compoundInfo))
                    : BoolToInt(IsLastReference(compoundInfo));
            }

            return (aboveInfo.ReferenceFrame == Vp9InterReferenceFrame.Last ? 2 : 0) +
                (leftInfo.ReferenceFrame == Vp9InterReferenceFrame.Last ? 2 : 0);
        }

        if (hasAbove || hasLeft)
        {
            var edgeInfo = hasAbove ? aboveInfo : leftInfo;
            if (!edgeInfo.IsInterBlock)
            {
                return 2;
            }

            return edgeInfo.IsCompoundReference
                ? 1 + BoolToInt(IsLastReference(edgeInfo))
                : 4 * BoolToInt(edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Last);
        }

        return 2;
    }

    private static int GetSingleReferenceContext1(
        bool hasAbove,
        Vp9InterModeInfoContextEntry aboveInfo,
        bool hasLeft,
        Vp9InterModeInfoContextEntry leftInfo)
    {
        if (hasAbove && hasLeft)
        {
            var aboveIntra = !aboveInfo.IsInterBlock;
            var leftIntra = !leftInfo.IsInterBlock;
            if (aboveIntra && leftIntra)
            {
                return 2;
            }

            if (aboveIntra || leftIntra)
            {
                return GetSingleReferenceContext1ForSingleEdge(aboveIntra ? leftInfo : aboveInfo);
            }

            if (aboveInfo.IsCompoundReference && leftInfo.IsCompoundReference)
            {
                return AreSameReferencePair(aboveInfo, leftInfo)
                    ? 3 * BoolToInt(IsGoldenReference(aboveInfo) || IsGoldenReference(leftInfo))
                    : 2;
            }

            if (aboveInfo.IsCompoundReference || leftInfo.IsCompoundReference)
            {
                var singleInfo = aboveInfo.IsCompoundReference ? leftInfo : aboveInfo;
                var compoundInfo = aboveInfo.IsCompoundReference ? aboveInfo : leftInfo;
                if (singleInfo.ReferenceFrame == Vp9InterReferenceFrame.Golden)
                {
                    return 3 + BoolToInt(IsGoldenReference(compoundInfo));
                }

                if (singleInfo.ReferenceFrame == Vp9InterReferenceFrame.AltRef)
                {
                    return BoolToInt(IsGoldenReference(compoundInfo));
                }

                return 1 + (2 * BoolToInt(IsGoldenReference(compoundInfo)));
            }

            if (aboveInfo.ReferenceFrame == Vp9InterReferenceFrame.Last && leftInfo.ReferenceFrame == Vp9InterReferenceFrame.Last)
            {
                return 3;
            }

            if (aboveInfo.ReferenceFrame == Vp9InterReferenceFrame.Last || leftInfo.ReferenceFrame == Vp9InterReferenceFrame.Last)
            {
                var edgeInfo = aboveInfo.ReferenceFrame == Vp9InterReferenceFrame.Last ? leftInfo : aboveInfo;
                return edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Golden ? 4 : 0;
            }

            return (aboveInfo.ReferenceFrame == Vp9InterReferenceFrame.Golden ? 2 : 0) +
                (leftInfo.ReferenceFrame == Vp9InterReferenceFrame.Golden ? 2 : 0);
        }

        if (hasAbove || hasLeft)
        {
            var edgeInfo = hasAbove ? aboveInfo : leftInfo;
            if (!edgeInfo.IsInterBlock ||
                (edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Last && !edgeInfo.IsCompoundReference))
            {
                return 2;
            }

            return edgeInfo.IsCompoundReference
                ? 3 * BoolToInt(IsGoldenReference(edgeInfo))
                : 4 * BoolToInt(edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Golden);
        }

        return 2;
    }

    private static int GetSingleReferenceContext1ForSingleEdge(Vp9InterModeInfoContextEntry edgeInfo)
    {
        if (!edgeInfo.IsCompoundReference)
        {
            if (edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Last)
            {
                return 3;
            }

            return edgeInfo.ReferenceFrame == Vp9InterReferenceFrame.Golden ? 4 : 0;
        }

        return 1 + (2 * BoolToInt(IsGoldenReference(edgeInfo)));
    }

    private static bool IsLastReference(Vp9InterModeInfoContextEntry entry)
    {
        return entry.ReferenceFrame == Vp9InterReferenceFrame.Last ||
            entry.CompoundReferenceFrame == Vp9InterReferenceFrame.Last;
    }

    private static bool IsGoldenReference(Vp9InterModeInfoContextEntry entry)
    {
        return entry.ReferenceFrame == Vp9InterReferenceFrame.Golden ||
            entry.CompoundReferenceFrame == Vp9InterReferenceFrame.Golden;
    }

    private static bool AreSameReferencePair(
        Vp9InterModeInfoContextEntry first,
        Vp9InterModeInfoContextEntry second)
    {
        return first.ReferenceFrame == second.ReferenceFrame &&
            first.CompoundReferenceFrame == second.CompoundReferenceFrame;
    }

    private static int GetInterModeCounter(Vp9InterModeInfoContextEntry entry)
    {
        if (!entry.IsInterBlock)
        {
            return 9;
        }

        return entry.PredictionMode switch
        {
            Vp9InterPredictionMode.NearestMv => 0,
            Vp9InterPredictionMode.NearMv => 0,
            Vp9InterPredictionMode.ZeroMv => 3,
            Vp9InterPredictionMode.NewMv => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.PredictionMode, "Unsupported VP9 inter prediction mode.")
        };
    }

    private static int GetSwitchableInterpolationType(Vp9InterModeInfoContextEntry entry)
    {
        if (!entry.IsInterBlock)
        {
            return Vp9FrameContextConstants.SwitchableFilters;
        }

        return entry.InterpolationFilter switch
        {
            Vp9InterpolationFilter.EightTap => 0,
            Vp9InterpolationFilter.EightTapSmooth => 1,
            Vp9InterpolationFilter.EightTapSharp => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entry),
                entry.InterpolationFilter,
                "VP9 switchable interpolation context can reference only switchable inter filters.")
        };
    }

    private static int BoolToInt(bool value)
    {
        return value ? 1 : 0;
    }

    private static Vp9InterReferenceFrame GetVariableReferenceFrame(
        Vp9InterModeInfoContextEntry entry,
        bool fixedReferenceSignBias)
    {
        if (!entry.IsCompoundReference)
        {
            return entry.ReferenceFrame;
        }

        return fixedReferenceSignBias
            ? entry.ReferenceFrame
            : entry.CompoundReferenceFrame ?? entry.ReferenceFrame;
    }

    private static bool GetReferenceFrameSignBias(
        IReadOnlyList<bool> referenceFrameSignBiases,
        Vp9InterReferenceFrame referenceFrame)
    {
        var index = (int)referenceFrame - 1;
        if (index < 0 || index >= referenceFrameSignBiases.Count)
        {
            throw new ArgumentException(
                "VP9 reference frame sign bias table does not contain the requested reference.",
                nameof(referenceFrameSignBiases));
        }

        return referenceFrameSignBiases[index];
    }

    private readonly record struct Vp9InterModeInfoContextEntry(
        bool Skip,
        Vp9TransformSize TransformSize,
        bool IsInterBlock,
        Vp9ReferenceMode ReferenceMode,
        Vp9InterReferenceFrame ReferenceFrame,
        Vp9InterReferenceFrame? CompoundReferenceFrame,
        Vp9InterPredictionMode PredictionMode,
        Vp9InterpolationFilter InterpolationFilter)
    {
        public bool IsLastReference => ReferenceFrame == Vp9InterReferenceFrame.Last;

        public bool IsCompoundReference => IsInterBlock && ReferenceMode == Vp9ReferenceMode.Compound;
    }
}
