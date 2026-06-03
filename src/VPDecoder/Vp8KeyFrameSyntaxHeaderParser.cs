namespace VPDecoder;

internal static class Vp8KeyFrameSyntaxHeaderParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> firstPartition,
        out Vp8KeyFrameSyntaxHeader? syntaxHeader,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        syntaxHeader = null;
        diagnostic = null;

        try
        {
            syntaxHeader = Parse(firstPartition);
            return true;
        }
        catch (Vp8BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static Vp8KeyFrameSyntaxHeader Parse(ReadOnlySpan<byte> firstPartition)
    {
        var reader = new Vp8BoolReader(firstPartition);
        var colorSpace = ReadColorSpace(ref reader);
        var clampType = reader.ReadBit();
        var segmentationHeader = ReadSegmentationHeader(ref reader);
        var loopFilterHeader = ReadLoopFilterHeader(ref reader);
        var log2TokenPartitionCount = reader.ReadLiteral(2);
        var quantizationHeader = ReadQuantizationHeader(ref reader);
        var refreshEntropyProbabilities = reader.ReadBit();
        var coefficientProbabilityUpdates = ReadCoefficientProbabilityUpdates(ref reader);
        var mbNoCoeffSkip = reader.ReadBit();
        var probSkipFalse = mbNoCoeffSkip
            ? (byte)reader.ReadLiteral(8)
            : (byte?)null;

        var syntaxHeader = new Vp8KeyFrameSyntaxHeader(
            colorSpace,
            clampType,
            segmentationHeader,
            loopFilterHeader,
            log2TokenPartitionCount,
            1 << log2TokenPartitionCount,
            quantizationHeader,
            refreshEntropyProbabilities,
            coefficientProbabilityUpdates,
            mbNoCoeffSkip,
            probSkipFalse);

        if (reader.HasError)
        {
            throw new Vp8BoolReaderException(
                Vp8DecodeDiagnostic.TruncatedPacket("VP8 key-frame syntax header extends past the first partition."));
        }

        return syntaxHeader;
    }

    private static IReadOnlyList<Vp8CoefficientProbabilityUpdate> ReadCoefficientProbabilityUpdates(ref Vp8BoolReader reader)
    {
        var updates = new List<Vp8CoefficientProbabilityUpdate>();
        for (var blockType = 0; blockType < Vp8CoefficientUpdateProbabilities.BlockTypes; blockType++)
        {
            for (var coefficientBand = 0; coefficientBand < Vp8CoefficientUpdateProbabilities.CoefficientBands; coefficientBand++)
            {
                for (var previousCoefficientContext = 0; previousCoefficientContext < Vp8CoefficientUpdateProbabilities.PreviousCoefficientContexts; previousCoefficientContext++)
                {
                    for (var entropyNode = 0; entropyNode < Vp8CoefficientUpdateProbabilities.EntropyNodes; entropyNode++)
                    {
                        if (!reader.Read(Vp8CoefficientUpdateProbabilities.GetProbability(
                                blockType,
                                coefficientBand,
                                previousCoefficientContext,
                                entropyNode)))
                        {
                            continue;
                        }

                        updates.Add(new Vp8CoefficientProbabilityUpdate(
                            blockType,
                            coefficientBand,
                            previousCoefficientContext,
                            entropyNode,
                            (byte)reader.ReadLiteral(8)));
                    }
                }
            }
        }

        return updates;
    }

    private static Vp8KeyFrameColorSpace ReadColorSpace(ref Vp8BoolReader reader)
    {
        return reader.ReadBit()
            ? Vp8KeyFrameColorSpace.Reserved
            : Vp8KeyFrameColorSpace.Bt601;
    }

    private static Vp8SegmentationHeader ReadSegmentationHeader(ref Vp8BoolReader reader)
    {
        var enabled = reader.ReadBit();
        var quantizerUpdates = new int[4];
        var loopFilterUpdates = new int[4];
        byte?[] segmentTreeProbabilities = [null, null, null];
        if (!enabled)
        {
            return new Vp8SegmentationHeader(
                Enabled: false,
                UpdateMap: false,
                UpdateFeatureData: false,
                AbsoluteDeltaMode: false,
                quantizerUpdates,
                loopFilterUpdates,
                segmentTreeProbabilities);
        }

        var updateMap = reader.ReadBit();
        var updateFeatureData = reader.ReadBit();
        var absoluteDeltaMode = false;
        if (updateFeatureData)
        {
            absoluteDeltaMode = reader.ReadBit();
            ReadSegmentFeatureUpdates(ref reader, quantizerUpdates, bits: 7);
            ReadSegmentFeatureUpdates(ref reader, loopFilterUpdates, bits: 6);
        }

        if (updateMap)
        {
            for (var i = 0; i < segmentTreeProbabilities.Length; i++)
            {
                segmentTreeProbabilities[i] = reader.ReadBit()
                    ? (byte)reader.ReadLiteral(8)
                    : null;
            }
        }

        return new Vp8SegmentationHeader(
            enabled,
            updateMap,
            updateFeatureData,
            absoluteDeltaMode,
            quantizerUpdates,
            loopFilterUpdates,
            segmentTreeProbabilities);
    }

    private static void ReadSegmentFeatureUpdates(ref Vp8BoolReader reader, int[] destination, int bits)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = reader.ReadBit()
                ? ReadSignedLiteral(ref reader, bits)
                : 0;
        }
    }

    private static Vp8LoopFilterHeader ReadLoopFilterHeader(ref Vp8BoolReader reader)
    {
        var type = reader.ReadBit() ? Vp8LoopFilterType.Simple : Vp8LoopFilterType.Normal;
        var level = reader.ReadLiteral(6);
        var sharpness = reader.ReadLiteral(3);
        var referenceFrameDeltas = new int[4];
        var modeDeltas = new int[4];
        var deltaEnabled = reader.ReadBit();
        var deltaUpdate = false;
        if (deltaEnabled)
        {
            deltaUpdate = reader.ReadBit();
            if (deltaUpdate)
            {
                ReadLoopFilterDeltas(ref reader, referenceFrameDeltas);
                ReadLoopFilterDeltas(ref reader, modeDeltas);
            }
        }

        return new Vp8LoopFilterHeader(
            type,
            level,
            sharpness,
            deltaEnabled,
            deltaUpdate,
            referenceFrameDeltas,
            modeDeltas);
    }

    private static void ReadLoopFilterDeltas(ref Vp8BoolReader reader, int[] destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = reader.ReadBit()
                ? ReadSignedLiteral(ref reader, bits: 6)
                : 0;
        }
    }

    private static Vp8QuantizationHeader ReadQuantizationHeader(ref Vp8BoolReader reader)
    {
        return new Vp8QuantizationHeader(
            reader.ReadLiteral(7),
            ReadDeltaQ(ref reader),
            ReadDeltaQ(ref reader),
            ReadDeltaQ(ref reader),
            ReadDeltaQ(ref reader),
            ReadDeltaQ(ref reader));
    }

    private static int ReadDeltaQ(ref Vp8BoolReader reader)
    {
        return reader.ReadBit()
            ? ReadSignedLiteral(ref reader, bits: 4)
            : 0;
    }

    private static int ReadSignedLiteral(ref Vp8BoolReader reader, int bits)
    {
        var value = reader.ReadLiteral(bits);
        return reader.ReadBit() ? -value : value;
    }
}
