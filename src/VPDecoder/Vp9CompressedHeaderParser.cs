namespace VPDecoder;

public static class Vp9CompressedHeaderParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader frameHeader,
        out Vp9CompressedHeader? compressedHeader,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        return TryParse(packet, frameHeader, Vp9FrameContext.CreateDefault(), out compressedHeader, out diagnostic);
    }

    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader frameHeader,
        Vp9FrameContext baseFrameContext,
        out Vp9CompressedHeader? compressedHeader,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        compressedHeader = null;
        diagnostic = null;

        if (frameHeader.HeaderSizeInBytes + frameHeader.FirstPartitionSize > packet.Length)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compressed header extends past the packet boundary.");
            return false;
        }

        try
        {
            var compressedHeaderBytes = packet.Slice(frameHeader.HeaderSizeInBytes, frameHeader.FirstPartitionSize);
            var reader = new Vp9BoolReader(compressedHeaderBytes);
            var frameContext = baseFrameContext.Clone();
            var transformMode = frameHeader.Quantization.Lossless
                ? Vp9TransformMode.Only4X4
                : ReadTransformMode(ref reader);
            var txUpdateCount = transformMode == Vp9TransformMode.Select
                ? ReadTransformModeProbabilities(ref reader, frameContext.TxProbabilities)
                : 0;
            var coefficientUpdateCount = ReadCoefficientProbabilities(ref reader, frameContext, transformMode);
            var skipUpdateCount = ReadSkipProbabilities(ref reader, frameContext);
            var referenceMode = Vp9ReferenceMode.Single;
            var interProbabilityUpdateCount = 0;
            var motionVectorProbabilityUpdateCount = 0;
            if (frameHeader.FrameType == Vp9FrameType.InterFrame && !frameHeader.IntraOnly)
            {
                var interUpdates = ReadInterFrameProbabilities(ref reader, frameHeader, frameContext);
                referenceMode = interUpdates.ReferenceMode;
                interProbabilityUpdateCount = interUpdates.InterProbabilityUpdateCount;
                motionVectorProbabilityUpdateCount = interUpdates.MotionVectorProbabilityUpdateCount;
            }

            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compressed header ended unexpectedly.");
                return false;
            }

            compressedHeader = new Vp9CompressedHeader(
                transformMode,
                frameContext,
                txUpdateCount,
                coefficientUpdateCount,
                skipUpdateCount,
                referenceMode,
                interProbabilityUpdateCount,
                motionVectorProbabilityUpdateCount);
            return true;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    private static Vp9TransformMode ReadTransformMode(ref Vp9BoolReader reader)
    {
        var transformMode = reader.ReadLiteral(2);
        if (transformMode == (int)Vp9TransformMode.Allow32X32 && reader.ReadBit())
        {
            transformMode++;
        }

        if (transformMode > (int)Vp9TransformMode.Select)
        {
            throw new Vp9BoolReaderException(
                Vp9DecodeDiagnostic.InvalidPacket($"Invalid VP9 transform mode: {transformMode}."));
        }

        return (Vp9TransformMode)transformMode;
    }

    private static int ReadTransformModeProbabilities(ref Vp9BoolReader reader, Vp9TxProbabilities probabilities)
    {
        var updateCount = 0;
        for (var i = 0; i < Vp9FrameContextConstants.TransformSizeContexts; i++)
        {
            for (var j = 0; j < probabilities.EightByEight.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref probabilities.EightByEight[i, j]))
                {
                    updateCount++;
                }
            }
        }

        for (var i = 0; i < Vp9FrameContextConstants.TransformSizeContexts; i++)
        {
            for (var j = 0; j < probabilities.SixteenBySixteen.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref probabilities.SixteenBySixteen[i, j]))
                {
                    updateCount++;
                }
            }
        }

        for (var i = 0; i < Vp9FrameContextConstants.TransformSizeContexts; i++)
        {
            for (var j = 0; j < probabilities.ThirtyTwoByThirtyTwo.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref probabilities.ThirtyTwoByThirtyTwo[i, j]))
                {
                    updateCount++;
                }
            }
        }

        return updateCount;
    }

    private static int ReadCoefficientProbabilities(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        Vp9TransformMode transformMode)
    {
        var updateCount = 0;
        var maxTransformSize = GetMaximumCoefficientTransformSize(transformMode);
        for (var transformSize = 0; transformSize <= maxTransformSize; transformSize++)
        {
            if (!reader.ReadBit())
            {
                continue;
            }

            for (var planeType = 0; planeType < Vp9FrameContextConstants.PlaneTypes; planeType++)
            {
                for (var referenceType = 0; referenceType < Vp9FrameContextConstants.ReferenceTypes; referenceType++)
                {
                    for (var band = 0; band < Vp9FrameContextConstants.CoefficientBands; band++)
                    {
                        var coefficientContexts = band == 0 ? 3 : Vp9FrameContextConstants.CoefficientContexts;
                        for (var context = 0; context < coefficientContexts; context++)
                        {
                            for (var node = 0; node < Vp9FrameContextConstants.UnconstrainedNodes; node++)
                            {
                                var index = frameContext.GetCoefficientProbabilityIndex(
                                    transformSize,
                                    planeType,
                                    referenceType,
                                    band,
                                    context,
                                    node);
                                if (Vp9ProbabilityUpdater.DiffUpdateProbability(
                                        ref reader,
                                        ref frameContext.CoefficientProbabilities[index]))
                                {
                                    updateCount++;
                                }
                            }
                        }
                    }
                }
            }
        }

        return updateCount;
    }

    private static int ReadSkipProbabilities(ref Vp9BoolReader reader, Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        for (var i = 0; i < Vp9FrameContextConstants.SkipContexts; i++)
        {
            if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.SkipProbabilities[i]))
            {
                updateCount++;
            }
        }

        return updateCount;
    }

    private static Vp9InterProbabilityUpdateSummary ReadInterFrameProbabilities(
        ref Vp9BoolReader reader,
        Vp9FrameHeader frameHeader,
        Vp9FrameContext frameContext)
    {
        var interUpdateCount = 0;
        interUpdateCount += ReadInterModeProbabilities(ref reader, frameContext);
        if (frameHeader.InterpolationFilter == Vp9InterpolationFilter.Switchable)
        {
            interUpdateCount += ReadSwitchableInterpolationProbabilities(ref reader, frameContext);
        }

        interUpdateCount += ReadIntraInterProbabilities(ref reader, frameContext);
        var referenceMode = ReadFrameReferenceMode(ref reader, frameHeader);
        interUpdateCount += ReadFrameReferenceModeProbabilities(ref reader, referenceMode, frameContext);
        interUpdateCount += ReadInterFrameYModeProbabilities(ref reader, frameContext);
        interUpdateCount += ReadPartitionProbabilities(ref reader, frameContext);
        var motionVectorUpdateCount = ReadMotionVectorProbabilities(
            ref reader,
            frameContext.MotionVectorProbabilities,
            frameHeader.AllowHighPrecisionMv);

        return new Vp9InterProbabilityUpdateSummary(
            referenceMode,
            interUpdateCount,
            motionVectorUpdateCount);
    }

    private static int ReadInterModeProbabilities(ref Vp9BoolReader reader, Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        for (var i = 0; i < frameContext.InterModeProbabilities.GetLength(0); i++)
        {
            for (var j = 0; j < frameContext.InterModeProbabilities.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.InterModeProbabilities[i, j]))
                {
                    updateCount++;
                }
            }
        }

        return updateCount;
    }

    private static int ReadSwitchableInterpolationProbabilities(ref Vp9BoolReader reader, Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        for (var i = 0; i < frameContext.SwitchableInterpolationProbabilities.GetLength(0); i++)
        {
            for (var j = 0; j < frameContext.SwitchableInterpolationProbabilities.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.SwitchableInterpolationProbabilities[i, j]))
                {
                    updateCount++;
                }
            }
        }

        return updateCount;
    }

    private static int ReadIntraInterProbabilities(ref Vp9BoolReader reader, Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        for (var i = 0; i < frameContext.IntraInterProbabilities.Length; i++)
        {
            if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.IntraInterProbabilities[i]))
            {
                updateCount++;
            }
        }

        return updateCount;
    }

    private static Vp9ReferenceMode ReadFrameReferenceMode(ref Vp9BoolReader reader, Vp9FrameHeader frameHeader)
    {
        if (!IsCompoundReferenceAllowed(frameHeader))
        {
            return Vp9ReferenceMode.Single;
        }

        if (!reader.ReadBit())
        {
            return Vp9ReferenceMode.Single;
        }

        return reader.ReadBit() ? Vp9ReferenceMode.Select : Vp9ReferenceMode.Compound;
    }

    private static int ReadFrameReferenceModeProbabilities(
        ref Vp9BoolReader reader,
        Vp9ReferenceMode referenceMode,
        Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        if (referenceMode == Vp9ReferenceMode.Select)
        {
            for (var i = 0; i < frameContext.CompoundInterProbabilities.Length; i++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.CompoundInterProbabilities[i]))
                {
                    updateCount++;
                }
            }
        }

        if (referenceMode != Vp9ReferenceMode.Compound)
        {
            for (var i = 0; i < frameContext.SingleReferenceProbabilities.GetLength(0); i++)
            {
                for (var j = 0; j < frameContext.SingleReferenceProbabilities.GetLength(1); j++)
                {
                    if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.SingleReferenceProbabilities[i, j]))
                    {
                        updateCount++;
                    }
                }
            }
        }

        if (referenceMode != Vp9ReferenceMode.Single)
        {
            for (var i = 0; i < frameContext.CompoundReferenceProbabilities.Length; i++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.CompoundReferenceProbabilities[i]))
                {
                    updateCount++;
                }
            }
        }

        return updateCount;
    }

    private static int ReadInterFrameYModeProbabilities(ref Vp9BoolReader reader, Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        for (var i = 0; i < frameContext.InterFrameYModeProbabilities.GetLength(0); i++)
        {
            for (var j = 0; j < frameContext.InterFrameYModeProbabilities.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.InterFrameYModeProbabilities[i, j]))
                {
                    updateCount++;
                }
            }
        }

        return updateCount;
    }

    private static int ReadPartitionProbabilities(ref Vp9BoolReader reader, Vp9FrameContext frameContext)
    {
        var updateCount = 0;
        for (var i = 0; i < frameContext.PartitionProbabilities.GetLength(0); i++)
        {
            for (var j = 0; j < frameContext.PartitionProbabilities.GetLength(1); j++)
            {
                if (Vp9ProbabilityUpdater.DiffUpdateProbability(ref reader, ref frameContext.PartitionProbabilities[i, j]))
                {
                    updateCount++;
                }
            }
        }

        return updateCount;
    }

    private static int ReadMotionVectorProbabilities(
        ref Vp9BoolReader reader,
        Vp9MotionVectorProbabilities probabilities,
        bool allowHighPrecisionMv)
    {
        var updateCount = 0;
        updateCount += UpdateMvProbabilities(ref reader, probabilities.Joints);

        foreach (var component in probabilities.Components)
        {
            var sign = component.Sign;
            updateCount += UpdateMvProbability(ref reader, ref sign);
            component.Sign = sign;
            updateCount += UpdateMvProbabilities(ref reader, component.Classes);
            updateCount += UpdateMvProbabilities(ref reader, component.Class0);
            updateCount += UpdateMvProbabilities(ref reader, component.Bits);
        }

        foreach (var component in probabilities.Components)
        {
            for (var i = 0; i < component.Class0Fp.GetLength(0); i++)
            {
                for (var j = 0; j < component.Class0Fp.GetLength(1); j++)
                {
                    updateCount += UpdateMvProbability(ref reader, ref component.Class0Fp[i, j]);
                }
            }

            updateCount += UpdateMvProbabilities(ref reader, component.Fp);
        }

        if (allowHighPrecisionMv)
        {
            foreach (var component in probabilities.Components)
            {
                var class0Hp = component.Class0Hp;
                updateCount += UpdateMvProbability(ref reader, ref class0Hp);
                component.Class0Hp = class0Hp;

                var hp = component.Hp;
                updateCount += UpdateMvProbability(ref reader, ref hp);
                component.Hp = hp;
            }
        }

        return updateCount;
    }

    private static int UpdateMvProbabilities(ref Vp9BoolReader reader, byte[] probabilities)
    {
        var updateCount = 0;
        for (var i = 0; i < probabilities.Length; i++)
        {
            updateCount += UpdateMvProbability(ref reader, ref probabilities[i]);
        }

        return updateCount;
    }

    private static int UpdateMvProbability(ref Vp9BoolReader reader, ref byte probability)
    {
        if (!reader.Read(252))
        {
            return 0;
        }

        probability = (byte)((reader.ReadLiteral(7) << 1) | 1);
        return 1;
    }

    private static bool IsCompoundReferenceAllowed(Vp9FrameHeader frameHeader)
    {
        return frameHeader.ReferenceFrameSignBiases.Count >= 3 &&
            (frameHeader.ReferenceFrameSignBiases[1] != frameHeader.ReferenceFrameSignBiases[0] ||
                frameHeader.ReferenceFrameSignBiases[2] != frameHeader.ReferenceFrameSignBiases[0]);
    }

    private static int GetMaximumCoefficientTransformSize(Vp9TransformMode transformMode)
    {
        return transformMode switch
        {
            Vp9TransformMode.Only4X4 => 0,
            Vp9TransformMode.Allow8X8 => 1,
            Vp9TransformMode.Allow16X16 => 2,
            Vp9TransformMode.Allow32X32 or Vp9TransformMode.Select => 3,
            _ => throw new Vp9BoolReaderException(
                Vp9DecodeDiagnostic.InvalidPacket($"Invalid VP9 transform mode: {transformMode}."))
        };
    }

    private sealed record Vp9InterProbabilityUpdateSummary(
        Vp9ReferenceMode ReferenceMode,
        int InterProbabilityUpdateCount,
        int MotionVectorProbabilityUpdateCount);
}
