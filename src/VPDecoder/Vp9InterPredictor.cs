namespace VPDecoder;

internal static class Vp9InterPredictor
{
    private const int MotionVectorLowerBound = -(1 << 14);
    private const int MotionVectorUpperBound = (1 << 14) - 1;
    private const int MotionVectorReferenceNeighborCount = 8;
    private static readonly Vp9MotionVector ZeroMotionVector = new(0, 0);

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
        return BuildSpatialMotionVectorCandidates(
            currentBlock,
            decodedBlocks,
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

        return BuildSpatialMotionVectorCandidates(
            currentBlock,
            decodedBlocks,
            blockIndex,
            referenceFrameSignBiases,
            previousFrameMotionVectors);
    }

    private static IReadOnlyList<Vp9MotionVector> BuildSpatialMotionVectorCandidates(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        int? sub8X8BlockIndex,
        IReadOnlyList<bool>? referenceFrameSignBiases,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        var candidates = new List<Vp9MotionVector>(capacity: 2);
        var positionOffset = (int)currentBlock.ModeInfo.BlockSize * MotionVectorReferenceNeighborCount * 2;
        for (var i = 0; i < MotionVectorReferenceNeighborCount; i++)
        {
            var rowOffset = MotionVectorReferencePositions[positionOffset + (i * 2)];
            var columnOffset = MotionVectorReferencePositions[positionOffset + (i * 2) + 1];
            if (TryFindSpatialCandidateAtMiPosition(
                    currentBlock,
                    decodedBlocks,
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
                AddCandidate(candidates, candidateMotionVector);
            }

            if (candidates.Count == 2)
            {
                return candidates;
            }
        }

        AddPreviousFrameCandidate(
            candidates,
            currentBlock,
            previousFrameMotionVectors,
            referenceFrameSignBiases,
            sameReferenceOnly: true);
        if (candidates.Count == 2)
        {
            return candidates;
        }

        if (referenceFrameSignBiases is null)
        {
            return candidates;
        }

        for (var i = 0; i < MotionVectorReferenceNeighborCount; i++)
        {
            var rowOffset = MotionVectorReferencePositions[positionOffset + (i * 2)];
            var columnOffset = MotionVectorReferencePositions[positionOffset + (i * 2) + 1];
            if (TryFindSpatialCandidateAtMiPosition(
                    currentBlock,
                    decodedBlocks,
                    currentBlock.MiRow + rowOffset,
                    currentBlock.MiColumn + columnOffset,
                    out var candidate) &&
                CanUseSpatialCandidate(candidate, currentBlock))
            {
                AddDifferentReferenceSpatialCandidates(
                    candidates,
                    currentBlock,
                    candidate,
                    referenceFrameSignBiases);
            }

            if (candidates.Count == 2)
            {
                return candidates;
            }
        }

        AddPreviousFrameCandidate(
            candidates,
            currentBlock,
            previousFrameMotionVectors,
            referenceFrameSignBiases,
            sameReferenceOnly: false);
        return candidates;
    }

    public static bool TrySelectSub8X8MotionVector(
        Vp9InterPredictionMode predictionMode,
        int blockIndex,
        IReadOnlyList<Vp9MotionVector> candidates,
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
        return blockIndex switch
        {
            0 => candidates.Count >= 2 ? candidates[1] : ZeroMotionVector,
            1 or 2 => FirstDifferent(
                GetCurrentSubMotionVector(currentSubMotionVectors, 0),
                candidates),
            3 => FirstDifferent(
                GetCurrentSubMotionVector(currentSubMotionVectors, 2),
                [
                    GetCurrentSubMotionVector(currentSubMotionVectors, 1),
                    GetCurrentSubMotionVector(currentSubMotionVectors, 0),
                    candidates.Count >= 1 ? candidates[0] : ZeroMotionVector,
                    candidates.Count >= 2 ? candidates[1] : ZeroMotionVector
                ]),
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

    private static bool TrySelectSharedSub8X8MotionVector(
        IReadOnlyList<Vp9InterPredictionMode> subModes,
        IReadOnlyList<Vp9MotionVector> candidates,
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

    private static void AddCandidate(List<Vp9MotionVector> candidates, Vp9MotionVector? candidate)
    {
        if (candidate is not { } motionVector || candidates.Contains(motionVector))
        {
            return;
        }

        candidates.Add(motionVector);
    }

    private static bool TryFindSpatialCandidateAtMiPosition(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks,
        int miRow,
        int miColumn,
        out Vp9InterBlockModeInfoProbe candidate)
    {
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
        List<Vp9MotionVector> candidates,
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
            AddCandidate(candidates, primaryMotionVector);
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
            AddCandidate(candidates, compoundMotionVector);
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
        List<Vp9MotionVector> candidates,
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
                AddCandidate(candidates, entry.MotionVector);
            }

            if (entry.CompoundReferenceFrame == currentBlock.ModeInfo.ReferenceFrame)
            {
                AddCandidate(candidates, entry.CompoundMotionVector);
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
            candidates,
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
                candidates,
                compoundReferenceFrame,
                compoundMotionVector,
                currentBlock.ModeInfo.ReferenceFrame,
                currentSignBias,
                referenceFrameSignBiases);
        }
    }

    private static void AddPreviousDifferentReferenceCandidate(
        List<Vp9MotionVector> candidates,
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

        AddCandidate(candidates, motionVector);
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
