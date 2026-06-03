namespace VPDecoder;

internal static class Vp9InterPredictor
{
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
                motionVector = new Vp9MotionVector(0, 0);
                return true;

            case Vp9InterPredictionMode.NearestMv:
                if (candidates.Count < 1)
                {
                    diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                        "VP9 NEARESTMV requires a derived reference MV candidate, which is not available yet.");
                    return false;
                }

                motionVector = candidates[0];
                return true;

            case Vp9InterPredictionMode.NearMv:
                if (candidates.Count < 2)
                {
                    diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                        "VP9 NEARMV requires derived reference MV candidates, which are not available yet.");
                    return false;
                }

                motionVector = candidates[1];
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

    public static IReadOnlyList<Vp9MotionVector> BuildSpatialMotionVectorCandidates(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks)
    {
        var candidates = new List<Vp9MotionVector>(capacity: 2);
        AddCandidate(
            candidates,
            FindLeftCandidate(currentBlock, decodedBlocks));
        AddCandidate(
            candidates,
            FindAboveCandidate(currentBlock, decodedBlocks));
        return candidates;
    }

    public static bool IsWholePixelMotionVector(Vp9MotionVector motionVector)
    {
        return (motionVector.Row & 7) == 0 && (motionVector.Column & 7) == 0;
    }

    private static void AddCandidate(List<Vp9MotionVector> candidates, Vp9MotionVector? candidate)
    {
        if (candidate is not { } motionVector || candidates.Contains(motionVector))
        {
            return;
        }

        candidates.Add(motionVector);
    }

    private static Vp9MotionVector? FindLeftCandidate(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks)
    {
        Vp9InterBlockModeInfoProbe? best = null;
        foreach (var block in decodedBlocks)
        {
            if (!CanUseCandidate(block, currentBlock))
            {
                continue;
            }

            var rightEdge = block.MiColumn + Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(block.ModeInfo.BlockSize);
            if (rightEdge != currentBlock.MiColumn ||
                !Overlaps(
                    block.MiRow,
                    Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(block.ModeInfo.BlockSize),
                    currentBlock.MiRow,
                    Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(currentBlock.ModeInfo.BlockSize)))
            {
                continue;
            }

            if (best is null || block.MiColumn > best.MiColumn || block.MiRow > best.MiRow)
            {
                best = block;
            }
        }

        return best?.MotionVector;
    }

    private static Vp9MotionVector? FindAboveCandidate(
        Vp9InterBlockModeInfoProbe currentBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedBlocks)
    {
        Vp9InterBlockModeInfoProbe? best = null;
        foreach (var block in decodedBlocks)
        {
            if (!CanUseCandidate(block, currentBlock))
            {
                continue;
            }

            var bottomEdge = block.MiRow + Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(block.ModeInfo.BlockSize);
            if (bottomEdge != currentBlock.MiRow ||
                !Overlaps(
                    block.MiColumn,
                    Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(block.ModeInfo.BlockSize),
                    currentBlock.MiColumn,
                    Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(currentBlock.ModeInfo.BlockSize)))
            {
                continue;
            }

            if (best is null || block.MiRow > best.MiRow || block.MiColumn > best.MiColumn)
            {
                best = block;
            }
        }

        return best?.MotionVector;
    }

    private static bool CanUseCandidate(
        Vp9InterBlockModeInfoProbe candidate,
        Vp9InterBlockModeInfoProbe currentBlock)
    {
        return candidate.MotionVector.HasValue &&
            candidate.ModeInfo.IsInterBlock &&
            candidate.ModeInfo.ReferenceMode == Vp9ReferenceMode.Single &&
            candidate.ModeInfo.ReferenceFrame == currentBlock.ModeInfo.ReferenceFrame;
    }

    private static bool Overlaps(int firstStart, int firstSize, int secondStart, int secondSize)
    {
        return firstStart < secondStart + secondSize && secondStart < firstStart + firstSize;
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
