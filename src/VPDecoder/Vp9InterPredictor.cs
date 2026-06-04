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
        if (modeBlock.MotionVector is { } decodedMotionVector)
        {
            motionVector = decodedMotionVector;
            diagnostic = null;
            return true;
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
                candidate.ModeInfo.ReferenceFrame == currentBlock.ModeInfo.ReferenceFrame)
            {
                AddCandidate(candidates, candidate.MotionVector);
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
                TryScaleDifferentReferenceCandidate(
                    currentBlock,
                    candidate,
                    referenceFrameSignBiases,
                    out var scaledMotionVector))
            {
                AddCandidate(candidates, scaledMotionVector);
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
        foreach (var block in decodedBlocks)
        {
            if (!CanUseSpatialCandidate(block, currentBlock))
            {
                continue;
            }

            if (ContainsMiPosition(block, miRow, miColumn))
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
        return candidate.MotionVector.HasValue &&
            candidate.TileIndex == currentBlock.TileIndex &&
            candidate.ModeInfo.IsInterBlock &&
            candidate.ModeInfo.ReferenceMode == Vp9ReferenceMode.Single;
    }

    private static bool TryScaleDifferentReferenceCandidate(
        Vp9InterBlockModeInfoProbe currentBlock,
        Vp9InterBlockModeInfoProbe candidate,
        IReadOnlyList<bool> referenceFrameSignBiases,
        out Vp9MotionVector motionVector)
    {
        motionVector = default;
        if (candidate.ModeInfo.ReferenceFrame == currentBlock.ModeInfo.ReferenceFrame ||
            !candidate.MotionVector.HasValue)
        {
            return false;
        }

        if (!TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                currentBlock.ModeInfo.ReferenceFrame,
                out var currentSignBias) ||
            !TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                candidate.ModeInfo.ReferenceFrame,
                out var candidateSignBias))
        {
            return false;
        }

        motionVector = candidate.MotionVector.Value;
        if (candidateSignBias != currentSignBias)
        {
            motionVector = new Vp9MotionVector(-motionVector.Row, -motionVector.Column);
        }

        return true;
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

        if (entry.ReferenceFrame == currentBlock.ModeInfo.ReferenceFrame)
        {
            if (sameReferenceOnly)
            {
                AddCandidate(candidates, entry.MotionVector);
            }

            return;
        }

        if (sameReferenceOnly ||
            referenceFrameSignBiases is null ||
            !TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                currentBlock.ModeInfo.ReferenceFrame,
                out var currentSignBias) ||
            !TryGetReferenceFrameSignBias(
                referenceFrameSignBiases,
                entry.ReferenceFrame,
                out var previousSignBias))
        {
            return;
        }

        var motionVector = entry.MotionVector;
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
