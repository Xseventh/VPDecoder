namespace VPDecoder;

public static class Vp9CompressedHeaderParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader frameHeader,
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
            var frameContext = Vp9FrameContext.CreateDefault();
            var transformMode = frameHeader.Quantization.Lossless
                ? Vp9TransformMode.Only4X4
                : ReadTransformMode(ref reader);
            var txUpdateCount = transformMode == Vp9TransformMode.Select
                ? ReadTransformModeProbabilities(ref reader, frameContext.TxProbabilities)
                : 0;
            var coefficientUpdateCount = ReadCoefficientProbabilities(ref reader, frameContext, transformMode);
            var skipUpdateCount = ReadSkipProbabilities(ref reader, frameContext);

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
                skipUpdateCount);
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
}
