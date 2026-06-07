namespace VPDecoder;

internal readonly struct Vp9MotionVectorCandidateSet
{
    private readonly Vp9MotionVector _first;
    private readonly Vp9MotionVector _second;

    public Vp9MotionVectorCandidateSet(int count, Vp9MotionVector first, Vp9MotionVector second = default)
    {
        if (count is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "VP9 MV candidate count must be 0..2.");
        }

        Count = count;
        _first = first;
        _second = second;
    }

    public int Count { get; }

    public Vp9MotionVector this[int index] => index switch
    {
        0 when Count >= 1 => _first,
        1 when Count >= 2 => _second,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "VP9 MV candidate index is out of range.")
    };

    public IReadOnlyList<Vp9MotionVector> ToReadOnlyList()
    {
        return Count switch
        {
            0 => [],
            1 => [_first],
            _ => [_first, _second]
        };
    }
}

internal sealed class Vp9InterBlockModeInfoGrid
{
    private readonly Vp9InterBlockModeInfoProbe?[] _grid;

    public Vp9InterBlockModeInfoGrid(
        int tileIndex,
        int miRowStart,
        int miRowEnd,
        int miColumnStart,
        int miColumnEnd)
    {
        if (miRowStart < 0 || miRowEnd <= miRowStart)
        {
            throw new ArgumentOutOfRangeException(nameof(miRowEnd), "VP9 MV lookup grid requires a non-empty MI row range.");
        }

        if (miColumnStart < 0 || miColumnEnd <= miColumnStart)
        {
            throw new ArgumentOutOfRangeException(nameof(miColumnEnd), "VP9 MV lookup grid requires a non-empty MI column range.");
        }

        TileIndex = tileIndex;
        MiRowStart = miRowStart;
        MiRowEnd = miRowEnd;
        MiColumnStart = miColumnStart;
        MiColumnEnd = miColumnEnd;
        MiRows = miRowEnd - miRowStart;
        MiColumns = miColumnEnd - miColumnStart;
        _grid = new Vp9InterBlockModeInfoProbe?[checked(MiRows * MiColumns)];
    }

    public int TileIndex { get; }

    public int MiRowStart { get; }

    public int MiRowEnd { get; }

    public int MiColumnStart { get; }

    public int MiColumnEnd { get; }

    public int MiRows { get; }

    public int MiColumns { get; }

    public void Set(Vp9InterBlockModeInfoProbe modeBlock)
    {
        if (modeBlock.TileIndex != TileIndex)
        {
            return;
        }

        var width = Math.Min(
            Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeBlock.ModeInfo.BlockSize),
            MiColumnEnd - modeBlock.MiColumn);
        var height = Math.Min(
            Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeBlock.ModeInfo.BlockSize),
            MiRowEnd - modeBlock.MiRow);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var rowStart = Math.Max(modeBlock.MiRow, MiRowStart);
        var columnStart = Math.Max(modeBlock.MiColumn, MiColumnStart);
        var rowEnd = Math.Min(modeBlock.MiRow + height, MiRowEnd);
        var columnEnd = Math.Min(modeBlock.MiColumn + width, MiColumnEnd);
        for (var row = rowStart; row < rowEnd; row++)
        {
            var offset = ((row - MiRowStart) * MiColumns) + columnStart - MiColumnStart;
            for (var column = columnStart; column < columnEnd; column++)
            {
                _grid[offset + column - columnStart] = modeBlock;
            }
        }
    }

    public bool TryGetAtMi(int miRow, int miColumn, out Vp9InterBlockModeInfoProbe modeBlock)
    {
        if (miRow < MiRowStart ||
            miRow >= MiRowEnd ||
            miColumn < MiColumnStart ||
            miColumn >= MiColumnEnd)
        {
            modeBlock = default!;
            return false;
        }

        var existing = _grid[((miRow - MiRowStart) * MiColumns) + miColumn - MiColumnStart];
        if (existing is null)
        {
            modeBlock = default!;
            return false;
        }

        modeBlock = existing;
        return true;
    }
}

internal static class Vp9InterPredictor
{
    private const int MotionVectorLowerBound = -(1 << 14);
    private const int MotionVectorUpperBound = (1 << 14) - 1;
    private const int MotionVectorReferenceBorder = 16 << 3;
    private const int MotionVectorReferenceNeighborCount = 8;
    private static readonly Vp9MotionVector ZeroMotionVector = new(0, 0);

    private struct MotionVectorCandidateBuilder
    {
        private Vp9MotionVector _first;
        private Vp9MotionVector _second;

        public int Count { get; private set; }

        public void Add(Vp9MotionVector? candidate)
        {
            if (candidate is not { } motionVector || Contains(motionVector) || Count == 2)
            {
                return;
            }

            if (Count == 0)
            {
                _first = motionVector;
            }
            else
            {
                _second = motionVector;
            }

            Count++;
        }

        public Vp9MotionVectorCandidateSet ToSet()
        {
            return new Vp9MotionVectorCandidateSet(Count, _first, _second);
        }

        private bool Contains(Vp9MotionVector motionVector)
        {
            return Count switch
            {
                0 => false,
                1 => _first == motionVector,
                _ => _first == motionVector || _second == motionVector
            };
        }
    }

    private static ReadOnlySpan<sbyte> MotionVectorReferencePositions =>
    [
        -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
        -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
        -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
        -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
        0, -1, -1, 0, 1, -1, -1, -1, 0, -2, -2, 0, -2, -1, -1, -2,
        -1, 0, 0, -1, -1, 1, -1, -1, -2, 0, 0, -2, -1, -2, -2, -1,
        -1, 0, 0, -1, -1, 1, 1, -1, -1, -1, -3, 0, 0, -3, -3, -3,
        0, -1, -1, 0, 2, -1, -1, -1, -1, 1, 0, -3, -3, 0, -3, -3,
        -1, 0, 0, -1, -1, 2, -1, -1, 1, -1, -3, 0, 0, -3, -3, -3,
        -1, 1, 1, -1, -1, 2, 2, -1, -1, -1, -3, 0, 0, -3, -3, -3,
        0, -1, -1, 0, 4, -1, -1, 2, -1, -1, 0, -3, -3, 0, 2, -1,
        -1, 0, 0, -1, -1, 4, 2, -1, -1, -1, -3, 0, 0, -3, -1, 2,
        -1, 3, 3, -1, -1, 4, 4, -1, -1, -1, -1, 0, 0, -1, -1, 6
    ];

    public static bool TryResolveReferenceFrame(
        Vp9ReferenceFrameStore referenceFrames,
        Vp9FrameHeader header,
        Vp9InterReferenceFrame referenceFrame,
        out Vp9ReferenceFrame? resolvedReferenceFrame,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        resolvedReferenceFrame = null;
        diagnostic = null;

        var referenceIndex = GetReferenceFrameIndex(referenceFrame);
        if (referenceIndex >= header.ReferenceFrameIndices.Count)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                $"VP9 inter frame did not provide reference index {referenceIndex} for {referenceFrame}.");
            return false;
        }

        var slot = header.ReferenceFrameIndices[referenceIndex];
        if (!referenceFrames.TryGet(slot, out resolvedReferenceFrame))
        {
            diagnostic = Vp9DecodeDiagnostic.MissingReferenceFrame(
                $"VP9 inter block references empty reference frame slot {slot} for {referenceFrame}.");
            return false;
        }

        return true;
    }

    public static bool TrySelectMotionVector(
        Vp9InterPredictionMode predictionMode,
        IReadOnlyList<Vp9MotionVector> candidates,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TrySelectMotionVector(
            predictionMode,
            new Vp9MotionVectorCandidateSet(
                Math.Min(candidates.Count, 2),
                candidates.Count >= 1 ? candidates[0] : default,
                candidates.Count >= 2 ? candidates[1] : default),
            out motionVector,
            out diagnostic);
    }

    public static bool TrySelectMotionVector(
        Vp9InterPredictionMode predictionMode,
        Vp9MotionVectorCandidateSet candidates,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        motionVector = default;
        diagnostic = null;

        switch (predictionMode)
        {
            case Vp9InterPredictionMode.ZeroMv:
                motionVector = ZeroMotionVector;
                return true;

            case Vp9InterPredictionMode.NearestMv:
                motionVector = candidates.Count >= 1 ? candidates[0] : ZeroMotionVector;
                return true;

            case Vp9InterPredictionMode.NearMv:
                motionVector = candidates.Count >= 2 ? candidates[1] : ZeroMotionVector;
                return true;

            case Vp9InterPredictionMode.NewMv:
                diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 NEWMV inter prediction mode is not supported yet.");
                return false;

            default:
                diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    $"VP9 inter prediction mode {predictionMode} is not supported.");
                return false;
        }
    }

    public static bool TrySelectMotionVector(
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9MotionVector> candidates,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TrySelectMotionVector(
            modeBlock,
            new Vp9MotionVectorCandidateSet(
                Math.Min(candidates.Count, 2),
                candidates.Count >= 1 ? candidates[0] : default,
                candidates.Count >= 2 ? candidates[1] : default),
            out motionVector,
            out diagnostic);
    }

    public static bool TrySelectMotionVector(
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9MotionVectorCandidateSet candidates,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        if (modeBlock.InterSubMotionVectors.Count > 0)
        {
            motionVector = modeBlock.InterSubMotionVectors[^1];
            diagnostic = null;
            return true;
        }

        if (modeBlock.MotionVector is { } decodedMotionVector)
        {
            motionVector = decodedMotionVector;
            diagnostic = null;
            return true;
        }

        if (modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8 &&
            modeBlock.ModeInfo.InterSubModes.Count > 0)
        {
            return TrySelectSharedSub8X8MotionVector(
                modeBlock.ModeInfo.InterSubModes,
                candidates,
                out motionVector,
                out diagnostic);
        }

        return TrySelectMotionVector(
            modeBlock.ModeInfo.PredictionMode,
            candidates,
            out motionVector,
            out diagnostic);
    }

    public static IReadOnlyList<Vp9MotionVector> BuildSpatialMotionVectorCandidates(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        IReadOnlyList<bool>? referenceFrameSignBiases = null,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks,
            sub8X8BlockIndex: null,
            referenceFrameSignBiases,
            previousFrameMotionVectors).ToReadOnlyList();
    }

    public static Vp9MotionVectorCandidateSet BuildSpatialMotionVectorCandidateSet(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        IReadOnlyList<bool>? referenceFrameSignBiases = null,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks,
            sub8X8BlockIndex: null,
            referenceFrameSignBiases,
            previousFrameMotionVectors);
    }

    public static Vp9MotionVectorCandidateSet BuildSpatialMotionVectorCandidateSet(
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9InterBlockModeInfoGrid decodedBlocks,
        IReadOnlyList<bool>? referenceFrameSignBiases = null,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks: null,
            decodedBlockGrid: decodedBlocks,
            sub8X8BlockIndex: null,
            referenceFrameSignBiases,
            previousFrameMotionVectors);
    }

    public static IReadOnlyList<Vp9MotionVector> BuildSub8X8MotionVectorCandidates(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        int blockIndex,
        IReadOnlyList<bool>? referenceFrameSignBiases = null,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        if (blockIndex is < 0 or > 3)
        {
            return [];
        }

        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks,
            blockIndex,
            referenceFrameSignBiases,
            previousFrameMotionVectors).ToReadOnlyList();
    }

    public static Vp9MotionVectorCandidateSet BuildSub8X8MotionVectorCandidateSet(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        int blockIndex,
        IReadOnlyList<bool>? referenceFrameSignBiases = null,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        if (blockIndex is < 0 or > 3)
        {
            return default;
        }

        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks,
            blockIndex,
            referenceFrameSignBiases,
            previousFrameMotionVectors);
    }

    public static Vp9MotionVectorCandidateSet BuildSub8X8MotionVectorCandidateSet(
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9InterBlockModeInfoGrid decodedBlocks,
        int blockIndex,
        IReadOnlyList<bool>? referenceFrameSignBiases = null,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        if (blockIndex is < 0 or > 3)
        {
            return default;
        }

        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks: null,
            decodedBlockGrid: decodedBlocks,
            blockIndex,
            referenceFrameSignBiases,
            previousFrameMotionVectors);
    }

    private static Vp9MotionVectorCandidateSet BuildSpatialMotionVectorCandidateSet(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        int? sub8X8BlockIndex,
        IReadOnlyList<bool>? referenceFrameSignBiases,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        return BuildSpatialMotionVectorCandidateSet(
            currentBlock,
            decodedBlocks,
            decodedBlockGrid: null,
            sub8X8BlockIndex,
            referenceFrameSignBiases,
            previousFrameMotionVectors);
    }

    private static Vp9MotionVectorCandidateSet BuildSpatialMotionVectorCandidateSet(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe>? decodedBlocks,
        Vp9InterBlockModeInfoGrid? decodedBlockGrid,
        int? sub8X8BlockIndex,
        IReadOnlyList<bool>? referenceFrameSignBiases,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        var candidates = new MotionVectorCandidateBuilder();
        var positionOffset = (int)currentBlock.ModeInfo.BlockSize * MotionVectorReferenceNeighborCount * 2;
        for (var i = 0; i < MotionVectorReferenceNeighborCount; i++)
        {
            var rowOffset = MotionVectorReferencePositions[positionOffset + (i * 2)];
            var columnOffset = MotionVectorReferencePositions[positionOffset + (i * 2) + 1];
            if (TryFindSpatialCandidateAtMiPosition(
                    currentBlock,
                    decodedBlocks,
                    decodedBlockGrid,
                    currentBlock.MiRow + rowOffset,
                    currentBlock.MiColumn + columnOffset,
                    out var candidate) &&
                CanUseSpatialCandidate(candidate, currentBlock) &&
                TryGetSameReferenceCandidateMotionVector(
                    currentBlock,
                    candidate,
                    i,
                    columnOffset,
                    sub8X8BlockIndex,
                    out var candidateMotionVector))
            {
                AddCandidate(ref candidates, candidateMotionVector);
            }

            if (candidates.Count == 2)
            {
                return candidates.ToSet();
            }
        }

        AddPreviousFrameCandidate(
            ref candidates,
            currentBlock,
            previousFrameMotionVectors,
            referenceFrameSignBiases,
            sameReferenceOnly: true);
        if (candidates.Count == 2)
        {
            return candidates.ToSet();
        }

        if (referenceFrameSignBiases is null)
        {
            return candidates.ToSet();
        }

        for (var i = 0; i < MotionVectorReferenceNeighborCount; i++)
        {
            var rowOffset = MotionVectorReferencePositions[positionOffset + (i * 2)];
            var columnOffset = MotionVectorReferencePositions[positionOffset + (i * 2) + 1];
            if (TryFindSpatialCandidateAtMiPosition(
                    currentBlock,
                    decodedBlocks,
                    decodedBlockGrid,
                    currentBlock.MiRow + rowOffset,
                    currentBlock.MiColumn + columnOffset,
                    out var candidate) &&
                CanUseSpatialCandidate(candidate, currentBlock))
            {
                AddDifferentReferenceSpatialCandidates(
                    ref candidates,
                    currentBlock,
                    candidate,
                    referenceFrameSignBiases);
            }

            if (candidates.Count == 2)
            {
                return candidates.ToSet();
            }
        }

        AddPreviousFrameCandidate(
            ref candidates,
            currentBlock,
            previousFrameMotionVectors,
            referenceFrameSignBiases,
            sameReferenceOnly: false);
        return candidates.ToSet();
    }

    public static bool TrySelectSub8X8MotionVector(
        Vp9InterPredictionMode predictionMode,
        int blockIndex,
        IReadOnlyList<Vp9MotionVector> candidates,
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TrySelectSub8X8MotionVector(
            predictionMode,
            blockIndex,
            new Vp9MotionVectorCandidateSet(
                Math.Min(candidates.Count, 2),
                candidates.Count >= 1 ? candidates[0] : default,
                candidates.Count >= 2 ? candidates[1] : default),
            currentSubMotionVectors,
            out motionVector,
            out diagnostic);
    }

    public static bool TrySelectSub8X8MotionVector(
        Vp9InterPredictionMode predictionMode,
        int blockIndex,
        Vp9MotionVectorCandidateSet candidates,
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        motionVector = default;
        diagnostic = null;
        if (blockIndex is < 0 or > 3)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 sub-8x8 motion-vector block index is outside the 4x4 group.");
            return false;
        }

        return predictionMode switch
        {
            Vp9InterPredictionMode.ZeroMv => TryReturnMotionVector(ZeroMotionVector, out motionVector, out diagnostic),
            Vp9InterPredictionMode.NearestMv => TryReturnMotionVector(
                GetSub8X8NearestMotionVector(blockIndex, candidates, currentSubMotionVectors),
                out motionVector,
                out diagnostic),
            Vp9InterPredictionMode.NearMv => TryReturnMotionVector(
                GetSub8X8NearMotionVector(blockIndex, candidates, currentSubMotionVectors),
                out motionVector,
                out diagnostic),
            Vp9InterPredictionMode.NewMv => TryUnsupportedSub8X8NewMv(out motionVector, out diagnostic),
            _ => TryUnsupportedPredictionMode(predictionMode, out motionVector, out diagnostic)
        };
    }

    public static bool IsWholePixelMotionVector(Vp9MotionVector motionVector)
    {
        return (motionVector.Row & 7) == 0 && (motionVector.Column & 7) == 0;
    }

    public static bool IsValidMotionVector(Vp9MotionVector motionVector)
    {
        return motionVector.Row > MotionVectorLowerBound &&
            motionVector.Row < MotionVectorUpperBound &&
            motionVector.Column > MotionVectorLowerBound &&
            motionVector.Column < MotionVectorUpperBound;
    }

    public static Vp9MotionVector ClampReferenceMotionVector(
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9MotionVector motionVector)
    {
        var blockWidth = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(currentBlock.ModeInfo.BlockSize);
        var blockHeight = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(currentBlock.ModeInfo.BlockSize);
        var leftEdge = -(currentBlock.MiColumn * 8 * 8);
        var rightEdge = (header.TileInfo.MiColumns - blockWidth - currentBlock.MiColumn) * 8 * 8;
        var topEdge = -(currentBlock.MiRow * 8 * 8);
        var bottomEdge = (header.TileInfo.MiRows - blockHeight - currentBlock.MiRow) * 8 * 8;

        return new Vp9MotionVector(
            Math.Clamp(motionVector.Row, topEdge - MotionVectorReferenceBorder, bottomEdge + MotionVectorReferenceBorder),
            Math.Clamp(motionVector.Column, leftEdge - MotionVectorReferenceBorder, rightEdge + MotionVectorReferenceBorder));
    }

    public static IReadOnlyList<Vp9MotionVector> ClampReferenceMotionVectorCandidates(
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9MotionVector> candidates)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var clamped = new Vp9MotionVector[candidates.Count];
        for (var index = 0; index < clamped.Length; index++)
        {
            clamped[index] = ClampReferenceMotionVector(header, currentBlock, candidates[index]);
        }

        return clamped;
    }

    public static Vp9MotionVectorCandidateSet ClampReferenceMotionVectorCandidates(
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9MotionVectorCandidateSet candidates)
    {
        return candidates.Count switch
        {
            0 => candidates,
            1 => new Vp9MotionVectorCandidateSet(
                1,
                ClampReferenceMotionVector(header, currentBlock, candidates[0])),
            _ => new Vp9MotionVectorCandidateSet(
                2,
                ClampReferenceMotionVector(header, currentBlock, candidates[0]),
                ClampReferenceMotionVector(header, currentBlock, candidates[1]))
        };
    }

    private static bool TryReturnMotionVector(
        Vp9MotionVector value,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        motionVector = value;
        diagnostic = null;
        return true;
    }

    private static bool TryUnsupportedSub8X8NewMv(
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        motionVector = default;
        diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
            "VP9 sub-8x8 NEWMV inter prediction mode requires explicit motion-vector syntax.");
        return false;
    }

    private static bool TryUnsupportedPredictionMode(
        Vp9InterPredictionMode predictionMode,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        motionVector = default;
        diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
            $"VP9 inter prediction mode {predictionMode} is not supported.");
        return false;
    }

    private static Vp9MotionVector GetSub8X8NearestMotionVector(
        int blockIndex,
        IReadOnlyList<Vp9MotionVector> candidates,
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors)
    {
        return GetSub8X8NearestMotionVector(
            blockIndex,
            new Vp9MotionVectorCandidateSet(
                Math.Min(candidates.Count, 2),
                candidates.Count >= 1 ? candidates[0] : default,
                candidates.Count >= 2 ? candidates[1] : default),
            currentSubMotionVectors);
    }

    private static Vp9MotionVector GetSub8X8NearestMotionVector(
        int blockIndex,
        Vp9MotionVectorCandidateSet candidates,
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors)
    {
        return blockIndex switch
        {
            0 => candidates.Count >= 1 ? candidates[0] : ZeroMotionVector,
            1 or 2 => GetCurrentSubMotionVector(currentSubMotionVectors, 0),
            3 => GetCurrentSubMotionVector(currentSubMotionVectors, 2),
            _ => ZeroMotionVector
        };
    }

    private static Vp9MotionVector GetSub8X8NearMotionVector(
        int blockIndex,
        IReadOnlyList<Vp9MotionVector> candidates,
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors)
    {
        return GetSub8X8NearMotionVector(
            blockIndex,
            new Vp9MotionVectorCandidateSet(
                Math.Min(candidates.Count, 2),
                candidates.Count >= 1 ? candidates[0] : default,
                candidates.Count >= 2 ? candidates[1] : default),
            currentSubMotionVectors);
    }

    private static Vp9MotionVector GetSub8X8NearMotionVector(
        int blockIndex,
        Vp9MotionVectorCandidateSet candidates,
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors)
    {
        return blockIndex switch
        {
            0 => candidates.Count >= 2 ? candidates[1] : ZeroMotionVector,
            1 or 2 => FirstDifferent(
                GetCurrentSubMotionVector(currentSubMotionVectors, 0),
                candidates),
            3 => FirstDifferent(
                GetCurrentSubMotionVector(currentSubMotionVectors, 2),
                GetCurrentSubMotionVector(currentSubMotionVectors, 1),
                GetCurrentSubMotionVector(currentSubMotionVectors, 0),
                candidates),
            _ => ZeroMotionVector
        };
    }

    private static Vp9MotionVector GetCurrentSubMotionVector(
        IReadOnlyList<Vp9MotionVector> currentSubMotionVectors,
        int blockIndex)
    {
        return blockIndex >= 0 && blockIndex < currentSubMotionVectors.Count
            ? currentSubMotionVectors[blockIndex]
            : ZeroMotionVector;
    }

    private static Vp9MotionVector FirstDifferent(
        Vp9MotionVector nearest,
        IReadOnlyList<Vp9MotionVector> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate != nearest)
            {
                return candidate;
            }
        }

        return ZeroMotionVector;
    }

    private static Vp9MotionVector FirstDifferent(
        Vp9MotionVector nearest,
        Vp9MotionVectorCandidateSet candidates)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate != nearest)
            {
                return candidate;
            }
        }

        return ZeroMotionVector;
    }

    private static Vp9MotionVector FirstDifferent(
        Vp9MotionVector nearest,
        Vp9MotionVector candidate0,
        Vp9MotionVector candidate1,
        Vp9MotionVectorCandidateSet candidates)
    {
        if (candidate0 != nearest)
        {
            return candidate0;
        }

        if (candidate1 != nearest)
        {
            return candidate1;
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate != nearest)
            {
                return candidate;
            }
        }

        return ZeroMotionVector;
    }

    private static bool TrySelectSharedSub8X8MotionVector(
        IReadOnlyList<Vp9InterPredictionMode> subModes,
        IReadOnlyList<Vp9MotionVector> candidates,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TrySelectSharedSub8X8MotionVector(
            subModes,
            new Vp9MotionVectorCandidateSet(
                Math.Min(candidates.Count, 2),
                candidates.Count >= 1 ? candidates[0] : default,
                candidates.Count >= 2 ? candidates[1] : default),
            out motionVector,
            out diagnostic);
    }

    private static bool TrySelectSharedSub8X8MotionVector(
        IReadOnlyList<Vp9InterPredictionMode> subModes,
        Vp9MotionVectorCandidateSet candidates,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        motionVector = default;
        diagnostic = null;
        Vp9MotionVector? sharedMotionVector = null;
        foreach (var subMode in subModes)
        {
            if (subMode == Vp9InterPredictionMode.NewMv)
            {
                diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 sub-8x8 NEWMV inter prediction mode is not supported yet.");
                return false;
            }

            if (!TrySelectMotionVector(subMode, candidates, out var subMotionVector, out diagnostic))
            {
                return false;
            }

            if (sharedMotionVector is null)
            {
                sharedMotionVector = subMotionVector;
                continue;
            }

            if (sharedMotionVector.Value != subMotionVector)
            {
                diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 sub-8x8 mixed inter prediction modes with distinct motion vectors are not supported yet.");
                return false;
            }
        }

        motionVector = sharedMotionVector ?? ZeroMotionVector;
        return true;
    }

    private static void AddCandidate(ref MotionVectorCandidateBuilder candidates, Vp9MotionVector? candidate)
    {
        candidates.Add(candidate);
    }

    private static bool TryFindSpatialCandidateAtMiPosition(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe>? decodedBlocks,
        Vp9InterBlockModeInfoGrid? decodedBlockGrid,
        int miRow,
        int miColumn,
        out Vp9InterBlockModeInfoProbe candidate)
    {
        if (decodedBlockGrid is not null)
        {
            return decodedBlockGrid.TryGetAtMi(miRow, miColumn, out candidate);
        }

        if (decodedBlocks is null)
        {
            candidate = default!;
            return false;
        }

        for (var index = decodedBlocks.Count - 1; index >= 0; index--)
        {
            var block = decodedBlocks[index];
            if (block.TileIndex == currentBlock.TileIndex &&
                ContainsMiPosition(block, miRow, miColumn))
            {
                candidate = block;
                return true;
            }
        }

        candidate = default!;
        return false;
    }

    private static bool CanUseSpatialCandidate(
        Vp9InterBlockModeInfoProbe candidate,
        Vp9InterBlockModeInfoProbe currentBlock)
    {
        return candidate.TileIndex == currentBlock.TileIndex &&
            candidate.ModeInfo.IsInterBlock &&
            (candidate.MotionVector.HasValue || candidate.CompoundMotionVector.HasValue);
    }

    private static void AddDifferentReferenceSpatialCandidates(
        ref MotionVectorCandidateBuilder candidates,
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9InterBlockModeInfoProbe candidate,
        IReadOnlyList<bool> referenceFrameSignBiases)
    {
        if (candidate.ModeInfo.ReferenceFrame != currentBlock.ModeInfo.ReferenceFrame &&
            candidate.MotionVector.HasValue &&
            TryScaleMotionVector(
                currentBlock.ModeInfo.ReferenceFrame,
                candidate.ModeInfo.ReferenceFrame,
                candidate.MotionVector.Value,
                referenceFrameSignBiases,
                out var primaryMotionVector))
        {
            AddCandidate(ref candidates, primaryMotionVector);
        }

        if (candidate.ModeInfo.CompoundReferenceFrame is { } candidateCompoundReferenceFrame &&
            candidateCompoundReferenceFrame != currentBlock.ModeInfo.ReferenceFrame &&
            candidate.CompoundMotionVector.HasValue &&
            candidate.CompoundMotionVector.Value != candidate.MotionVector &&
            TryScaleMotionVector(
                currentBlock.ModeInfo.ReferenceFrame,
                candidateCompoundReferenceFrame,
                candidate.CompoundMotionVector.Value,
                referenceFrameSignBiases,
                out var compoundMotionVector))
        {
            AddCandidate(ref candidates, compoundMotionVector);
        }
    }

    private static bool TryScaleMotionVector(
        Vp9InterReferenceFrame currentReferenceFrame,
        Vp9InterReferenceFrame candidateReferenceFrame,
        Vp9MotionVector candidateMotionVector,
        IReadOnlyList<bool> referenceFrameSignBiases,
        out Vp9MotionVector motionVector)
    {
        motionVector = default;
        if (!TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                currentReferenceFrame,
                out var currentSignBias) ||
            !TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                candidateReferenceFrame,
                out var candidateSignBias))
        {
            return false;
        }

        motionVector = candidateMotionVector;
        if (candidateSignBias != currentSignBias)
        {
            motionVector = new Vp9MotionVector(-motionVector.Row, -motionVector.Column);
        }

        return true;
    }

    private static bool TryGetSameReferenceCandidateMotionVector(
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9InterBlockModeInfoProbe candidate,
        int neighborIndex,
        int columnOffset,
        int? sub8X8BlockIndex,
        out Vp9MotionVector motionVector)
    {
        motionVector = default;
        if (candidate.ModeInfo.ReferenceFrame == currentBlock.ModeInfo.ReferenceFrame &&
            candidate.MotionVector.HasValue)
        {
            if (sub8X8BlockIndex is { } blockIndex &&
                neighborIndex < 2 &&
                candidate.ModeInfo.BlockSize < Vp9BlockSize.Block8X8 &&
                candidate.InterSubMotionVectors.Count == 4)
            {
                motionVector = candidate.InterSubMotionVectors[
                    GetSub8X8CandidateMotionVectorIndex(blockIndex, columnOffset)];
                return true;
            }

            motionVector = candidate.MotionVector.Value;
            return true;
        }

        if (candidate.ModeInfo.CompoundReferenceFrame == currentBlock.ModeInfo.ReferenceFrame &&
            candidate.CompoundMotionVector.HasValue)
        {
            if (sub8X8BlockIndex is { } blockIndex &&
                neighborIndex < 2 &&
                candidate.ModeInfo.BlockSize < Vp9BlockSize.Block8X8 &&
                candidate.CompoundInterSubMotionVectors.Count == 4)
            {
                motionVector = candidate.CompoundInterSubMotionVectors[
                    GetSub8X8CandidateMotionVectorIndex(blockIndex, columnOffset)];
                return true;
            }

            motionVector = candidate.CompoundMotionVector.Value;
            return true;
        }

        return false;
    }

    private static int GetSub8X8CandidateMotionVectorIndex(int blockIndex, int columnOffset)
    {
        var isAboveCandidate = columnOffset == 0;
        return blockIndex switch
        {
            0 => isAboveCandidate ? 2 : 1,
            1 => isAboveCandidate ? 3 : 1,
            2 => isAboveCandidate ? 2 : 3,
            3 => 3,
            _ => 3
        };
    }

    private static void AddPreviousFrameCandidate(
        ref MotionVectorCandidateBuilder candidates,
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors,
        IReadOnlyList<bool>? referenceFrameSignBiases,
        bool sameReferenceOnly)
    {
        if (previousFrameMotionVectors is null ||
            !previousFrameMotionVectors.TryGetEntryAtMi(currentBlock.MiRow, currentBlock.MiColumn, out var entry))
        {
            return;
        }

        if (sameReferenceOnly)
        {
            if (entry.ReferenceFrame == currentBlock.ModeInfo.ReferenceFrame)
            {
                AddCandidate(ref candidates, entry.MotionVector);
            }

            if (entry.CompoundReferenceFrame == currentBlock.ModeInfo.ReferenceFrame)
            {
                AddCandidate(ref candidates, entry.CompoundMotionVector);
            }

            return;
        }

        if (referenceFrameSignBiases is null ||
            !TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                currentBlock.ModeInfo.ReferenceFrame,
                out var currentSignBias))
        {
            return;
        }

        AddPreviousDifferentReferenceCandidate(
            ref candidates,
            entry.ReferenceFrame,
            entry.MotionVector,
            currentBlock.ModeInfo.ReferenceFrame,
            currentSignBias,
            referenceFrameSignBiases);
        if (entry.CompoundReferenceFrame is { } compoundReferenceFrame &&
            entry.CompoundMotionVector is { } compoundMotionVector &&
            compoundMotionVector != entry.MotionVector)
        {
            AddPreviousDifferentReferenceCandidate(
                ref candidates,
                compoundReferenceFrame,
                compoundMotionVector,
                currentBlock.ModeInfo.ReferenceFrame,
                currentSignBias,
                referenceFrameSignBiases);
        }
    }

    private static void AddPreviousDifferentReferenceCandidate(
        ref MotionVectorCandidateBuilder candidates,
        Vp9InterReferenceFrame previousReferenceFrame,
        Vp9MotionVector previousMotionVector,
        Vp9InterReferenceFrame currentReferenceFrame,
        bool currentSignBias,
        IReadOnlyList<bool> referenceFrameSignBiases)
    {
        if (previousReferenceFrame == currentReferenceFrame ||
            !TryGetReferenceFrameSignBias(referenceFrameSignBiases, previousReferenceFrame, out var previousSignBias))
        {
            return;
        }

        var motionVector = previousMotionVector;
        if (previousSignBias != currentSignBias)
        {
            motionVector = new Vp9MotionVector(-motionVector.Row, -motionVector.Column);
        }

        AddCandidate(ref candidates, motionVector);
    }

    private static bool TryGetReferenceFrameSignBias(
        IReadOnlyList<bool> referenceFrameSignBiases,
        Vp9InterReferenceFrame referenceFrame,
        out bool signBias)
    {
        var index = (int)referenceFrame - 1;
        if (index < 0 || index >= referenceFrameSignBiases.Count)
        {
            signBias = default;
            return false;
        }

        signBias = referenceFrameSignBiases[index];
        return true;
    }

    private static bool ContainsMiPosition(Vp9InterBlockModeInfoProbe block, int miRow, int miColumn)
    {
        return miRow >= block.MiRow &&
            miRow < block.MiRow + Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(block.ModeInfo.BlockSize) &&
            miColumn >= block.MiColumn &&
            miColumn < block.MiColumn + Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(block.ModeInfo.BlockSize);
    }

    private static int GetReferenceFrameIndex(Vp9InterReferenceFrame referenceFrame)
    {
        return referenceFrame switch
        {
            Vp9InterReferenceFrame.Last => 0,
            Vp9InterReferenceFrame.Golden => 1,
            Vp9InterReferenceFrame.AltRef => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(referenceFrame), referenceFrame, "Unsupported VP9 inter reference frame.")
        };
    }
}
