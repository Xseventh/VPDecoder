namespace VPDecoder;

public sealed record Vp9SuperblockSyntaxProbe(
    int TileIndex,
    IReadOnlyList<Vp9ModeInfoProbe> ModeInfos,
    IReadOnlyList<Vp9CoefficientBlockGroupProbe> CoefficientGroups);

internal sealed record Vp9InterSuperblockModeInfoProbe(
    int TileIndex,
    IReadOnlyList<Vp9PartitionProbe> Partitions,
    IReadOnlyList<Vp9InterBlockModeInfoProbe> ModeInfos);

internal sealed record Vp9InterSuperblockSyntaxProbe(
    int TileIndex,
    IReadOnlyList<Vp9PartitionProbe> Partitions,
    IReadOnlyList<Vp9InterBlockModeInfoProbe> ModeInfos,
    IReadOnlyList<Vp9CoefficientBlockGroupProbe> CoefficientGroups);

internal sealed record Vp9InterBlockModeInfoProbe(
    int TileIndex,
    int MiRow,
    int MiColumn,
    IReadOnlyList<Vp9PartitionType> PartitionPath,
    Vp9InterModeInfoProbe ModeInfo,
    Vp9MotionVector? MotionVector = null)
{
    public Vp9MotionVector? CompoundMotionVector { get; init; }

    public IReadOnlyList<Vp9MotionVector> InterSubMotionVectors { get; init; } = [];

    public IReadOnlyList<Vp9MotionVector> CompoundInterSubMotionVectors { get; init; } = [];

    public Vp9ModeInfoProbe ToIntraModeInfoProbe()
    {
        if (ModeInfo.IsInterBlock)
        {
            throw new InvalidOperationException("VP9 inter block mode-info cannot be converted to intra mode-info.");
        }

        return new Vp9ModeInfoProbe(
            TileIndex,
            MiRow,
            MiColumn,
            ModeInfo.BlockSize,
            PartitionPath,
            ModeInfo.Skip,
            ModeInfo.SkipContext,
            ModeInfo.TransformSize,
            ModeInfo.TransformSizeContext,
            ModeInfo.YMode,
            ModeInfo.UvMode,
            ModeInfo.YSubModes);
    }
}

internal static class Vp9TileSyntaxScanner
{
    private const int SuperblockSizeInMiUnits = 8;
    private const int SuperblockPartitionContextLog2 = 3;

    public static bool TryProbeFirstSuperblockPartitions(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9PartitionProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new Vp9PartitionProbe[state.TileGeometries.Count];
        probes = parsed;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var miRow = geometry.MiRowStart;
                var miColumn = geometry.MiColumnStart;
                var hasRows = miRow + (SuperblockSizeInMiUnits / 2) < state.Header.TileInfo.MiRows;
                var hasColumns = miColumn + (SuperblockSizeInMiUnits / 2) < state.Header.TileInfo.MiColumns;
                var context = Vp9PartitionSyntax.GetPartitionContext(
                    aboveContext: 0,
                    leftContext: 0,
                    blockSizeLog2: SuperblockPartitionContextLog2);
                var partition = Vp9PartitionSyntax.ReadPartition(ref reader, context, hasRows, hasColumns);
                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile partition probe ended unexpectedly.");
                    return false;
                }

                parsed[i] = new Vp9PartitionProbe(
                    geometry.Buffer.Index,
                    miRow,
                    miColumn,
                    context,
                    partition);
            }

            return true;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFirstLeafModeInfo(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9ModeInfoProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new Vp9ModeInfoProbe[state.TileGeometries.Count];
        probes = parsed;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                parsed[i] = ReadFirstLeafModeInfo(ref reader, state, geometry);
                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile mode-info probe ended unexpectedly.");
                    return false;
                }
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedPredictionMode(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFirstInterSuperblockModeInfo(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        out IReadOnlyList<Vp9InterSuperblockModeInfoProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        probes = [];
        diagnostic = null;

        if (header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 inter mode-info probe requires an ordinary inter frame.");
            return false;
        }

        try
        {
            var eligiblePreviousFrameMotionVectors = GetEligiblePreviousFrameMotionVectors(
                header,
                previousFrameMotionVectors);
            var geometries = Vp9TileGeometryBuilder.Build(header, tileBuffers);
            var parsed = new List<Vp9InterSuperblockModeInfoProbe>(geometries.Count);
            for (var i = 0; i < geometries.Count; i++)
            {
                var geometry = geometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9InterFrameSyntaxContext.Create(header);
                var partitions = new List<Vp9PartitionProbe>();
                var modes = new List<Vp9InterBlockModeInfoProbe>();
                var decodedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
                if (!TryReadInterPartitionModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        geometry.MiRowStart,
                        geometry.MiColumnStart,
                        Vp9BlockSize.Block64X64,
                        [],
                        partitions,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        eligiblePreviousFrameMotionVectors))
                {
                    probes = parsed;
                    return false;
                }

                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter first-superblock mode-info probe ended unexpectedly.");
                    probes = parsed;
                    return false;
                }

                parsed.Add(new Vp9InterSuperblockModeInfoProbe(geometry.Buffer.Index, partitions, modes));
            }

            probes = parsed;
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryPredictFirstInterSuperblockZeroMv(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9DecodedFrame? frame,
        out IReadOnlyList<Vp9InterSuperblockModeInfoProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        probes = [];
        diagnostic = null;

        if (!TryValidateInter8BitYuv420Header(header, out diagnostic))
        {
            return false;
        }

        if (!TryProbeFirstInterSuperblockModeInfo(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out probes,
                out diagnostic))
        {
            return false;
        }

        try
        {
            var destination = Vp9YuvFrameBuffer.Create(header.Width, header.Height);
            var parsedProbes = probes;
            var predictedProbes = new List<Vp9InterSuperblockModeInfoProbe>(parsedProbes.Count);
            var predictedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
            foreach (var probe in parsedProbes)
            {
                var predictedProbeModeBlocks = new List<Vp9InterBlockModeInfoProbe>(probe.ModeInfos.Count);
                foreach (var modeBlock in probe.ModeInfos)
                {
                    if (!TryPredictInterBlock(
                            referenceFrames,
                            header,
                            destination,
                            modeBlock,
                            predictedModeBlocks,
                            out var predictedModeBlock,
                            out diagnostic))
                    {
                        return false;
                    }

                    predictedModeBlocks.Add(predictedModeBlock);
                    predictedProbeModeBlocks.Add(predictedModeBlock);
                }

                predictedProbes.Add(probe with
                {
                    ModeInfos = predictedProbeModeBlocks
                });
            }

            probes = predictedProbes;
            frame = destination.ToDecodedFrame();
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter prediction probe YUV frame buffer size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter prediction probe YUV frame buffer allocation failed.");
            return false;
        }
    }

    public static bool TryReconstructFirstSkippedInterSuperblockZeroMv(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9DecodedFrame? frame,
        out IReadOnlyList<Vp9InterSuperblockModeInfoProbe> probes,
        out IReadOnlyList<Vp9CoefficientBlockGroupProbe> residualGroups,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        probes = [];
        residualGroups = [];
        diagnostic = null;

        if (!TryValidateInter8BitYuv420Header(header, out diagnostic))
        {
            return false;
        }

        if (!TryProbeFirstInterSuperblockModeInfo(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out probes,
                out diagnostic))
        {
            return false;
        }

        try
        {
            var destination = Vp9YuvFrameBuffer.Create(header.Width, header.Height);
            var parsedResidualGroups = new List<Vp9CoefficientBlockGroupProbe>();
            var parsedProbes = probes;
            var predictedProbes = new List<Vp9InterSuperblockModeInfoProbe>(parsedProbes.Count);
            var predictedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
            foreach (var probe in parsedProbes)
            {
                var predictedProbeModeBlocks = new List<Vp9InterBlockModeInfoProbe>(probe.ModeInfos.Count);
                foreach (var modeBlock in probe.ModeInfos)
                {
                    if (!modeBlock.ModeInfo.Skip)
                    {
                        diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                            "VP9 skipped inter reconstruction probe requires skip=true; non-skipped inter residual reconstruction is not supported yet.");
                        return false;
                    }

                    if (!TryPredictInterBlock(
                            referenceFrames,
                            header,
                            destination,
                            modeBlock,
                            predictedModeBlocks,
                            out var predictedModeBlock,
                            out diagnostic))
                    {
                        return false;
                    }

                    predictedModeBlocks.Add(predictedModeBlock);
                    predictedProbeModeBlocks.Add(predictedModeBlock);

                    for (var plane = 0; plane < 3; plane++)
                    {
                        parsedResidualGroups.Add(
                            Vp9ResidualSyntax.CreateSkippedInterPlaneCoefficientBlocks(header, modeBlock, plane));
                    }
                }

                predictedProbes.Add(probe with
                {
                    ModeInfos = predictedProbeModeBlocks
                });
            }

            probes = predictedProbes;
            residualGroups = parsedResidualGroups;
            frame = destination.ToDecodedFrame();
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 skipped inter reconstruction probe YUV frame buffer size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 skipped inter reconstruction probe YUV frame buffer allocation failed.");
            return false;
        }
    }

    public static bool TryProbeFirstInterSuperblockResidualSyntax(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        out IReadOnlyList<Vp9InterSuperblockModeInfoProbe> probes,
        out IReadOnlyList<Vp9CoefficientBlockGroupProbe> residualGroups,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        probes = [];
        residualGroups = [];
        diagnostic = null;

        if (header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 inter residual probe requires an ordinary inter frame.");
            return false;
        }

        if (!TryValidateInter8BitYuv420Header(header, out diagnostic))
        {
            return false;
        }

        try
        {
            var eligiblePreviousFrameMotionVectors = GetEligiblePreviousFrameMotionVectors(
                header,
                previousFrameMotionVectors);
            var geometries = Vp9TileGeometryBuilder.Build(header, tileBuffers);
            var parsed = new List<Vp9InterSuperblockModeInfoProbe>(geometries.Count);
            var parsedResidualGroups = new List<Vp9CoefficientBlockGroupProbe>();
            var dequantTables = Vp9DequantTables.Create(header.Quantization, header.BitDepth);
            for (var i = 0; i < geometries.Count; i++)
            {
                var geometry = geometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9InterFrameSyntaxContext.Create(header);
                var entropyContext = Vp9CoefficientEntropyContext.Create(header);
                var decodedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
                if (!TryReadInterSuperblockResidualSyntax(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        decodedModeBlocks,
                        geometry.MiRowStart,
                        geometry.MiColumnStart,
                        out var syntaxProbe,
                        out diagnostic,
                        eligiblePreviousFrameMotionVectors))
                {
                    probes = parsed;
                    residualGroups = parsedResidualGroups;
                    return false;
                }

                if (syntaxProbe is null)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 inter residual probe succeeded without returning a syntax probe.");
                    return false;
                }

                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter first-superblock residual probe ended unexpectedly.");
                    probes = parsed;
                    residualGroups = parsedResidualGroups;
                    return false;
                }

                parsed.Add(new Vp9InterSuperblockModeInfoProbe(
                    syntaxProbe.TileIndex,
                    syntaxProbe.Partitions,
                    syntaxProbe.ModeInfos));
                parsedResidualGroups.AddRange(syntaxProbe.CoefficientGroups);
            }

            probes = parsed;
            residualGroups = parsedResidualGroups;
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter residual probe allocation size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter residual probe allocation failed.");
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFullInterFrameResidualSyntax(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        out IReadOnlyList<Vp9InterSuperblockSyntaxProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        var parsed = new List<Vp9InterSuperblockSyntaxProbe>();
        probes = parsed;
        diagnostic = null;

        if (header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 full inter residual probe requires an ordinary inter frame.");
            return false;
        }

        if (!TryValidateInter8BitYuv420Header(header, out diagnostic))
        {
            return false;
        }

        try
        {
            var eligiblePreviousFrameMotionVectors = GetEligiblePreviousFrameMotionVectors(
                header,
                previousFrameMotionVectors);
            var dequantTables = Vp9DequantTables.Create(header.Quantization, header.BitDepth);
            var geometries = Vp9TileGeometryBuilder.Build(header, tileBuffers);
            foreach (var geometry in geometries)
            {
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9InterFrameSyntaxContext.Create(header);
                var entropyContext = Vp9CoefficientEntropyContext.Create(header);
                var decodedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();

                for (var miRow = geometry.MiRowStart; miRow < geometry.MiRowEnd; miRow += SuperblockSizeInMiUnits)
                {
                    syntaxContext.ResetLeftPartitionContext();
                    entropyContext.ResetLeftContexts();

                    for (var miColumn = geometry.MiColumnStart; miColumn < geometry.MiColumnEnd; miColumn += SuperblockSizeInMiUnits)
                    {
                        if (!TryReadInterSuperblockResidualSyntax(
                                ref reader,
                                header,
                                compressedHeader,
                                dequantTables,
                                geometry,
                                syntaxContext,
                                entropyContext,
                                decodedModeBlocks,
                                miRow,
                                miColumn,
                                out var syntaxProbe,
                                out diagnostic,
                                eligiblePreviousFrameMotionVectors))
                        {
                            return false;
                        }

                        if (syntaxProbe is null)
                        {
                            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                                "VP9 full inter residual probe succeeded without returning a syntax probe.");
                            return false;
                        }

                        if (reader.HasError)
                        {
                            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                                CreateInterFullFrameSyntaxTruncatedMessage(
                                    geometry.Buffer.Index,
                                    miRow,
                                    miColumn,
                                    syntaxProbe.ModeInfos,
                                    syntaxProbe.CoefficientGroups));
                            return false;
                        }

                        parsed.Add(syntaxProbe);
                    }
                }

                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 full inter residual probe ended unexpectedly.");
                    return false;
                }
            }

            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 full inter residual probe allocation size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 full inter residual probe allocation failed.");
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryReconstructFirstInterSuperblockZeroMvWithResidual(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9DecodedFrame? frame,
        out IReadOnlyList<Vp9InterSuperblockModeInfoProbe> probes,
        out IReadOnlyList<Vp9CoefficientBlockGroupProbe> residualGroups,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        probes = [];
        residualGroups = [];
        diagnostic = null;

        if (!TryProbeFirstInterSuperblockResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out probes,
                out residualGroups,
                out diagnostic))
        {
            return false;
        }

        try
        {
            var destination = Vp9YuvFrameBuffer.Create(header.Width, header.Height);
            var residualGroupIndex = 0;
            var parsedProbes = probes;
            var predictedProbes = new List<Vp9InterSuperblockModeInfoProbe>(parsedProbes.Count);
            var predictedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
            foreach (var probe in parsedProbes)
            {
                var predictedProbeModeBlocks = new List<Vp9InterBlockModeInfoProbe>(probe.ModeInfos.Count);
                foreach (var modeBlock in probe.ModeInfos)
                {
                    if (!TryPredictInterBlock(
                            referenceFrames,
                            header,
                            destination,
                            modeBlock,
                            predictedModeBlocks,
                            out var predictedModeBlock,
                            out diagnostic))
                    {
                        return false;
                    }

                    predictedModeBlocks.Add(predictedModeBlock);
                    predictedProbeModeBlocks.Add(predictedModeBlock);

                    for (var plane = 0; plane < 3; plane++)
                    {
                        if (residualGroupIndex >= residualGroups.Count)
                        {
                            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                                "VP9 inter reconstruction probe received fewer residual groups than mode blocks require.");
                            return false;
                        }

                        Vp9BlockReconstructor.AddInterResidualGroup(
                            destination,
                            modeBlock,
                            residualGroups[residualGroupIndex],
                            plane);
                        residualGroupIndex++;
                    }
                }

                predictedProbes.Add(probe with
                {
                    ModeInfos = predictedProbeModeBlocks
                });
            }

            if (residualGroupIndex != residualGroups.Count)
            {
                diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 inter reconstruction probe received extra residual groups.");
                return false;
            }

            probes = predictedProbes;
            frame = destination.ToDecodedFrame();
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter reconstruction probe YUV frame buffer size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter reconstruction probe YUV frame buffer allocation failed.");
            return false;
        }
    }

    public static bool TryReconstructFullInterFrameZeroMvWithResidual(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9DecodedFrame? frame,
        out IReadOnlyList<Vp9InterSuperblockSyntaxProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        probes = [];
        diagnostic = null;

        if (!TryReconstructFullInterFrameZeroMvWithResidualMetadata(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                referenceFrames,
                out var reconstructedFrame,
                out probes,
                out diagnostic))
        {
            return false;
        }

        if (reconstructedFrame is null)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 full inter reconstruction probe succeeded without returning metadata.");
            return false;
        }

        frame = reconstructedFrame.Frame;
        return true;
    }

    public static bool TryReconstructFullInterFrameZeroMvWithResidualMetadata(
        ReadOnlyMemory<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9ReconstructedFrame? reconstructedFrame,
        out IReadOnlyList<Vp9InterSuperblockSyntaxProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        reconstructedFrame = null;
        probes = [];
        diagnostic = null;

        if (!TryProbeFullInterFrameResidualSyntax(
                packet,
                header,
                compressedHeader,
                tileBuffers,
                out probes,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        if (!TryReconstructInterFrameFromProbes(
                header,
                probes,
                referenceFrames,
                out reconstructedFrame,
                out var predictedProbes,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        probes = predictedProbes;
        return true;
    }

    public static bool TryReconstructFullInterFrameDirectWithResidualMetadata(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        IReadOnlyList<Vp9TileBuffer> tileBuffers,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9DecodedFrame? frame,
        out IReadOnlyList<Vp9InterBlockModeInfoProbe> predictedModeBlocks,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        frame = null;
        predictedModeBlocks = [];
        diagnostic = null;

        if (header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 direct inter reconstruction requires an ordinary inter frame.");
            return false;
        }

        if (!TryValidateInter8BitYuv420Header(header, out diagnostic))
        {
            return false;
        }

        try
        {
            var eligiblePreviousFrameMotionVectors = GetEligiblePreviousFrameMotionVectors(
                header,
                previousFrameMotionVectors);
            var dequantTables = Vp9DequantTables.Create(header.Quantization, header.BitDepth);
            var geometries = Vp9TileGeometryBuilder.Build(header, tileBuffers);
            var destination = Vp9YuvFrameBuffer.Create(header.Width, header.Height);
            var modeBlocks = new List<Vp9InterBlockModeInfoProbe>();
            var residualScratch = new Vp9DirectInterResidualScratch();
            foreach (var geometry in geometries)
            {
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9InterFrameSyntaxContext.Create(header);
                var entropyContext = Vp9CoefficientEntropyContext.Create(header);
                var decodedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();

                for (var miRow = geometry.MiRowStart; miRow < geometry.MiRowEnd; miRow += SuperblockSizeInMiUnits)
                {
                    syntaxContext.ResetLeftPartitionContext();
                    entropyContext.ResetLeftContexts();

                    for (var miColumn = geometry.MiColumnStart; miColumn < geometry.MiColumnEnd; miColumn += SuperblockSizeInMiUnits)
                    {
                        if (!TryReconstructInterPartitionResidualSyntaxDirect(
                                ref reader,
                                header,
                                compressedHeader,
                                dequantTables,
                                geometry,
                                syntaxContext,
                                entropyContext,
                                destination,
                                referenceFrames,
                                miRow,
                                miColumn,
                                Vp9BlockSize.Block64X64,
                                decodedModeBlocks,
                                modeBlocks,
                                residualScratch,
                                out diagnostic,
                                eligiblePreviousFrameMotionVectors))
                        {
                            return false;
                        }

                        if (reader.HasError)
                        {
                            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                                $"VP9 direct inter reconstruction ended unexpectedly at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}).");
                            return false;
                        }
                    }
                }

                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 direct inter reconstruction ended unexpectedly.");
                    return false;
                }
            }

            frame = destination.ToDecodedFrame();
            predictedModeBlocks = modeBlocks;
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 direct inter reconstruction YUV frame buffer size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 direct inter reconstruction YUV frame buffer allocation failed.");
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryReconstructInterFrameFromProbes(
        Vp9FrameHeader header,
        IReadOnlyList<Vp9InterSuperblockSyntaxProbe> syntaxProbes,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9ReconstructedFrame? reconstructedFrame,
        out IReadOnlyList<Vp9InterSuperblockSyntaxProbe> predictedProbes,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        reconstructedFrame = null;
        predictedProbes = [];
        diagnostic = null;

        try
        {
            var eligiblePreviousFrameMotionVectors = GetEligiblePreviousFrameMotionVectors(
                header,
                previousFrameMotionVectors);
            var destination = Vp9YuvFrameBuffer.Create(header.Width, header.Height);
            var tileGeometries = CreateReconstructionTileGeometries(header, syntaxProbes);
            var reconstructedProbes = new List<Vp9InterSuperblockSyntaxProbe>(syntaxProbes.Count);
            var predictedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
            foreach (var probe in syntaxProbes)
            {
                if (probe.TileIndex < 0 || probe.TileIndex >= tileGeometries.Count)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 full inter reconstruction probe received an invalid tile index.");
                    return false;
                }

                var expectedGroupCount = checked(probe.ModeInfos.Count * 3);
                if (probe.CoefficientGroups.Count != expectedGroupCount)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 full inter reconstruction probe received mismatched mode/coefficient groups.");
                    return false;
                }

                var predictedProbeModeBlocks = new List<Vp9InterBlockModeInfoProbe>(probe.ModeInfos.Count);
                for (var modeIndex = 0; modeIndex < probe.ModeInfos.Count; modeIndex++)
                {
                    var modeBlock = probe.ModeInfos[modeIndex];
                    var groupOffset = modeIndex * 3;
                    if (!modeBlock.ModeInfo.IsInterBlock)
                    {
                        var intraModeInfo = modeBlock.ToIntraModeInfoProbe();
                        var geometry = tileGeometries[probe.TileIndex];
                        for (var plane = 0; plane < 3; plane++)
                        {
                            Vp9BlockReconstructor.ReconstructDcOnlyGroup(
                                destination,
                                geometry,
                                intraModeInfo,
                                probe.CoefficientGroups[groupOffset + plane],
                                plane);
                        }

                        predictedModeBlocks.Add(modeBlock);
                        predictedProbeModeBlocks.Add(modeBlock);
                        continue;
                    }

                    if (!TryPredictInterBlock(
                            referenceFrames,
                            header,
                            destination,
                            modeBlock,
                            predictedModeBlocks,
                            out var predictedModeBlock,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }

                    predictedModeBlocks.Add(predictedModeBlock);
                    predictedProbeModeBlocks.Add(predictedModeBlock);

                    for (var plane = 0; plane < 3; plane++)
                    {
                        Vp9BlockReconstructor.AddInterResidualGroup(
                            destination,
                            modeBlock,
                            probe.CoefficientGroups[groupOffset + plane],
                            plane);
                    }
                }

                reconstructedProbes.Add(probe with
                {
                    ModeInfos = predictedProbeModeBlocks
                });
            }

            predictedProbes = reconstructedProbes;
            reconstructedFrame = Vp9ReconstructedFrame.FromInterModeBlocks(
                destination.ToDecodedFrame(),
                predictedModeBlocks,
                header.TileInfo.MiRows,
                header.TileInfo.MiColumns);
            return true;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(ex.Message);
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 full inter reconstruction probe YUV frame buffer size overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 full inter reconstruction probe YUV frame buffer allocation failed.");
            return false;
        }
    }

    private static IReadOnlyList<Vp9TileGeometry> CreateReconstructionTileGeometries(
        Vp9FrameHeader header,
        IReadOnlyList<Vp9InterSuperblockSyntaxProbe> syntaxProbes)
    {
        var tileCount = checked(header.TileInfo.TileRows * header.TileInfo.TileColumns);
        var tileBuffers = new Vp9TileBuffer[tileCount];
        for (var i = 0; i < tileBuffers.Length; i++)
        {
            tileBuffers[i] = new Vp9TileBuffer(Index: i, SizeFieldOffset: null, DataOffset: 0, Size: 0);
        }

        var geometries = Vp9TileGeometryBuilder.Build(header, tileBuffers);
        var requiredTileCount = syntaxProbes.Count == 0
            ? geometries.Count
            : Math.Max(geometries.Count, syntaxProbes.Max(probe => probe.TileIndex + 1));
        if (requiredTileCount <= geometries.Count)
        {
            return geometries;
        }

        var expanded = geometries.ToList();
        for (var i = geometries.Count; i < requiredTileCount; i++)
        {
            expanded.Add(new Vp9TileGeometry(
                TileRow: 0,
                TileColumn: i,
                MiRowStart: 0,
                MiRowEnd: header.TileInfo.MiRows,
                MiColumnStart: 0,
                MiColumnEnd: header.TileInfo.MiColumns,
                new Vp9TileBuffer(Index: i, SizeFieldOffset: null, DataOffset: 0, Size: 0)));
        }

        return expanded;
    }

    private static Vp9PreviousFrameMotionVectors? GetEligiblePreviousFrameMotionVectors(
        Vp9FrameHeader header,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        return previousFrameMotionVectors?.CanUseFor(header) == true
            ? previousFrameMotionVectors
            : null;
    }

    public static bool TryProbeFirstLeafCoefficientToken(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9CoefficientTokenProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new Vp9CoefficientTokenProbe[state.TileGeometries.Count];
        probes = parsed;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var modeInfo = ReadFirstLeafModeInfo(ref reader, state, geometry);
                parsed[i] = Vp9ResidualSyntax.ReadFirstYCoefficientToken(ref reader, state, modeInfo);
                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 first coefficient probe ended unexpectedly.");
                    return false;
                }
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFirstLeafCoefficientBlock(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9CoefficientBlockProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new Vp9CoefficientBlockProbe[state.TileGeometries.Count];
        probes = parsed;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var modeInfo = ReadFirstLeafModeInfo(ref reader, state, geometry);
                parsed[i] = Vp9ResidualSyntax.ReadFirstYCoefficientBlock(ref reader, state, modeInfo);
                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 first coefficient block probe ended unexpectedly.");
                    return false;
                }
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFirstLeafYCoefficientBlocks(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9CoefficientBlockGroupProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new Vp9CoefficientBlockGroupProbe[state.TileGeometries.Count];
        probes = parsed;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var modeInfo = ReadFirstLeafModeInfo(ref reader, state, geometry);
                parsed[i] = Vp9ResidualSyntax.ReadFirstYCoefficientBlocks(ref reader, state, modeInfo);
                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 first Y coefficient block group probe ended unexpectedly.");
                    return false;
                }
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFirstSuperblockSyntax(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9SuperblockSyntaxProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new Vp9SuperblockSyntaxProbe[state.TileGeometries.Count];
        probes = parsed;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9KeyFrameSyntaxContext.Create(state.Header);
                var coefficientContext = Vp9CoefficientEntropyContext.Create(state.Header);
                var modes = new List<Vp9ModeInfoProbe>();
                var coefficientGroups = new List<Vp9CoefficientBlockGroupProbe>();
                ReadPartitionSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    geometry.MiRowStart,
                    geometry.MiColumnStart,
                    Vp9BlockSize.Block64X64,
                    [],
                    modes,
                    coefficientGroups);
                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 first superblock syntax probe ended unexpectedly.");
                    return false;
                }

                parsed[i] = new Vp9SuperblockSyntaxProbe(geometry.Buffer.Index, modes, coefficientGroups);
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFullFrameSyntax(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out IReadOnlyList<Vp9SuperblockSyntaxProbe> probes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var parsed = new List<Vp9SuperblockSyntaxProbe>();
        probes = parsed;
        diagnostic = null;

        try
        {
            foreach (var geometry in state.TileGeometries)
            {
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9KeyFrameSyntaxContext.Create(state.Header);
                var coefficientContext = Vp9CoefficientEntropyContext.Create(state.Header);

                for (var miRow = geometry.MiRowStart; miRow < geometry.MiRowEnd; miRow += SuperblockSizeInMiUnits)
                {
                    syntaxContext.ResetLeftPartitionContext();
                    coefficientContext.ResetLeftContexts();

                    for (var miColumn = geometry.MiColumnStart; miColumn < geometry.MiColumnEnd; miColumn += SuperblockSizeInMiUnits)
                    {
                        var modes = new List<Vp9ModeInfoProbe>();
                        var coefficientGroups = new List<Vp9CoefficientBlockGroupProbe>();
                        ReadPartitionSyntax(
                            ref reader,
                            state,
                            geometry,
                            syntaxContext,
                            coefficientContext,
                            miRow,
                            miColumn,
                            Vp9BlockSize.Block64X64,
                            [],
                            modes,
                            coefficientGroups);
                        parsed.Add(new Vp9SuperblockSyntaxProbe(geometry.Buffer.Index, modes, coefficientGroups));
                        if (reader.HasError)
                        {
                            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                                CreateFullFrameSyntaxTruncatedMessage(
                                    geometry.Buffer.Index,
                                    miRow,
                                    miColumn,
                                    modes,
                                    coefficientGroups));
                            return false;
                        }
                    }
                }

                if (reader.HasError)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 full-frame syntax probe ended unexpectedly.");
                    return false;
                }
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryProbeFirstBlock16X16LumaTx4Group(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out Vp9ModeInfoProbe? modeInfo,
        out Vp9CoefficientBlockGroupProbe? coefficientGroup,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        modeInfo = null;
        coefficientGroup = null;
        diagnostic = null;

        try
        {
            foreach (var geometry in state.TileGeometries)
            {
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var syntaxContext = Vp9KeyFrameSyntaxContext.Create(state.Header);
                var coefficientContext = Vp9CoefficientEntropyContext.Create(state.Header);

                for (var miRow = geometry.MiRowStart; miRow < geometry.MiRowEnd; miRow += SuperblockSizeInMiUnits)
                {
                    syntaxContext.ResetLeftPartitionContext();
                    coefficientContext.ResetLeftContexts();

                    for (var miColumn = geometry.MiColumnStart; miColumn < geometry.MiColumnEnd; miColumn += SuperblockSizeInMiUnits)
                    {
                        if (TryReadFirstBlock16X16LumaTx4Group(
                                ref reader,
                                state,
                                geometry,
                                syntaxContext,
                                coefficientContext,
                                miRow,
                                miColumn,
                                Vp9BlockSize.Block64X64,
                                [],
                                out modeInfo,
                                out coefficientGroup))
                        {
                            return true;
                        }

                        if (reader.HasError)
                        {
                            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                                $"VP9 Block16X16 luma Tx4 group probe ended unexpectedly at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}).");
                            return false;
                        }
                    }
                }
            }

            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature("VP9 Block16X16 luma Tx4 group probe did not find a matching block.");
            return false;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryReconstructFirstLeafYDc(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out Vp9DecodedFrame? frame,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        diagnostic = null;

        try
        {
            for (var i = 0; i < state.TileGeometries.Count; i++)
            {
                var geometry = state.TileGeometries[i];
                if (geometry.Buffer.DataOffset + geometry.Buffer.Size > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile buffer extends past the packet boundary.");
                    return false;
                }

                var tileBytes = packet.Span.Slice(geometry.Buffer.DataOffset, geometry.Buffer.Size);
                var reader = new Vp9BoolReader(tileBytes);
                var modeInfo = ReadFirstLeafModeInfo(ref reader, state, geometry);
                if (modeInfo.BlockSize is not (Vp9BlockSize.Block64X64 or Vp9BlockSize.Block32X32) ||
                    modeInfo.YMode != Vp9PredictionMode.Dc ||
                    modeInfo.UvMode != Vp9PredictionMode.Dc)
                {
                    diagnostic = Vp9DecodeDiagnostic.UnsupportedPredictionMode(
                        "VP9 first-leaf Y DC reconstruction probe supports only DC intra modes on 32x32 or 64x64 blocks.");
                    return false;
                }

                var group = Vp9ResidualSyntax.ReadFirstYCoefficientBlocks(ref reader, state, modeInfo);
                var gridSize = GetFirstLeafTx32GridSize(group.BlockSize);
                var plane = state.FrameBuffer.Pixels.AsSpan(
                    state.FrameBuffer.YPlane.Offset,
                    state.FrameBuffer.YPlane.Length);
                const int transformSize = 32;
                for (var blockRow = 0; blockRow < gridSize; blockRow++)
                {
                    for (var blockColumn = 0; blockColumn < gridSize; blockColumn++)
                    {
                        var blockIndex = (blockRow * gridSize) + blockColumn;
                        var coefficients = group.Blocks[blockIndex];
                        if (!Vp9BlockReconstructor.IsDcOnlyOrEmpty(coefficients))
                        {
                            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(
                                "VP9 first-leaf Y DC reconstruction probe supports only empty or DC-only TX32 coefficient blocks.");
                            return false;
                        }

                        var x = (modeInfo.MiColumn * 8) + (blockColumn * transformSize);
                        var y = (modeInfo.MiRow * 8) + (blockRow * transformSize);
                        var yOffset = state.FrameBuffer.YPlane.Offset + (y * state.FrameBuffer.YStride) + x;
                        var block = state.FrameBuffer.Pixels.AsSpan(yOffset);
                        var above = blockRow > 0 ? ReadAboveEdge(plane, state.FrameBuffer.YStride, x, y, transformSize) : [];
                        var left = blockColumn > 0 ? ReadLeftEdge(plane, state.FrameBuffer.YStride, x, y, transformSize) : [];
                        Vp9IntraPredictor.PredictDc(block, state.FrameBuffer.YStride, transformSize, above, left);
                        if (coefficients.DequantizedCoefficients[0] != 0)
                        {
                            Vp9DcOnlyReconstructor.AddDcOnly(
                                plane,
                                state.FrameBuffer.YStride,
                                x,
                                y,
                                transformSize,
                                coefficients.DequantizedCoefficients[0]);
                        }
                    }
                }
            }

            frame = state.FrameBuffer.ToDecodedFrame();
            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (Vp9BoolReaderException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static bool TryReconstructFirstSuperblockDcOnly(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out Vp9DecodedFrame? frame,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        diagnostic = null;

        if (!TryProbeFirstSuperblockSyntax(packet, state, out var probes, out diagnostic))
        {
            return false;
        }

        try
        {
            foreach (var probe in probes)
            {
                if (probe.TileIndex < 0 || probe.TileIndex >= state.TileGeometries.Count)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 first-superblock reconstruction received an invalid tile index.");
                    return false;
                }

                var geometry = state.TileGeometries[probe.TileIndex];
                var expectedGroupCount = checked(probe.ModeInfos.Count * 3);
                if (probe.CoefficientGroups.Count != expectedGroupCount)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 first-superblock reconstruction received mismatched mode/coefficient groups.");
                    return false;
                }

                for (var modeIndex = 0; modeIndex < probe.ModeInfos.Count; modeIndex++)
                {
                    var modeInfo = probe.ModeInfos[modeIndex];
                    if (modeInfo.YMode != Vp9PredictionMode.Dc || modeInfo.UvMode != Vp9PredictionMode.Dc)
                    {
                        diagnostic = Vp9DecodeDiagnostic.UnsupportedPredictionMode(
                            "VP9 first-superblock DC-only reconstruction supports only DC intra prediction modes.");
                        return false;
                    }

                    for (var plane = 0; plane < 3; plane++)
                    {
                        var group = probe.CoefficientGroups[(modeIndex * 3) + plane];
                        Vp9BlockReconstructor.ReconstructDcOnlyGroup(
                            state.FrameBuffer,
                            geometry,
                            modeInfo,
                            group,
                            plane);
                    }
                }
            }

            frame = state.FrameBuffer.ToDecodedFrame();
            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 first-superblock reconstruction overflowed a block geometry calculation.");
            return false;
        }
    }

    public static bool TryReconstructFullFrame(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out Vp9DecodedFrame? frame,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        frame = null;
        if (!TryReconstructFullFrameWithSyntax(packet, state, out var reconstructedFrame, out diagnostic))
        {
            return false;
        }

        frame = reconstructedFrame?.Frame;
        return true;
    }

    public static bool TryReconstructFullFrameWithSyntax(
        ReadOnlyMemory<byte> packet,
        Vp9KeyFrameDecodeState state,
        out Vp9ReconstructedFrame? reconstructedFrame,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        reconstructedFrame = null;
        if (!TryProbeFullFrameSyntax(packet, state, out var probes, out diagnostic))
        {
            return false;
        }

        try
        {
            foreach (var probe in probes)
            {
                if (probe.TileIndex < 0 || probe.TileIndex >= state.TileGeometries.Count)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 full-frame reconstruction received an invalid tile index.");
                    return false;
                }

                var geometry = state.TileGeometries[probe.TileIndex];
                var expectedGroupCount = checked(probe.ModeInfos.Count * 3);
                if (probe.CoefficientGroups.Count != expectedGroupCount)
                {
                    diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                        "VP9 full-frame reconstruction received mismatched mode/coefficient groups.");
                    return false;
                }

                for (var modeIndex = 0; modeIndex < probe.ModeInfos.Count; modeIndex++)
                {
                    var modeInfo = probe.ModeInfos[modeIndex];
                    for (var plane = 0; plane < 3; plane++)
                    {
                        var group = probe.CoefficientGroups[(modeIndex * 3) + plane];
                        Vp9BlockReconstructor.ReconstructDcOnlyGroup(
                            state.FrameBuffer,
                            geometry,
                            modeInfo,
                            group,
                            plane);
                    }
                }
            }

            var frame = state.FrameBuffer.ToDecodedFrame();
            reconstructedFrame = new Vp9ReconstructedFrame(
                frame,
                probes,
                state.Header.TileInfo.MiRows,
                state.Header.TileInfo.MiColumns);
            return true;
        }
        catch (NotSupportedException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedFeature(ex.Message);
            return false;
        }
        catch (ArgumentException ex)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(ex.Message);
            return false;
        }
        catch (OverflowException)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 full-frame reconstruction overflowed a block geometry calculation.");
            return false;
        }
    }

    private static bool TryReadInterPartitionModeInfo(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        List<Vp9PartitionProbe> partitions,
        List<Vp9InterBlockModeInfoProbe> modes,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        diagnostic = null;
        if (miRow >= header.TileInfo.MiRows || miColumn >= header.TileInfo.MiColumns)
        {
            return true;
        }

        var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
        var hasRows = miRow + hbs < header.TileInfo.MiRows;
        var hasColumns = miColumn + hbs < header.TileInfo.MiColumns;
        var partitionContext = syntaxContext.GetPartitionContext(miRow, miColumn, blockSize);
        var partition = Vp9PartitionSyntax.ReadPartition(
            ref reader,
            compressedHeader.FrameContext,
            partitionContext,
            hasRows,
            hasColumns);
        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter partition mode-info probe ended unexpectedly.");
            return false;
        }

        partitions.Add(new Vp9PartitionProbe(
            geometry.Buffer.Index,
            miRow,
            miColumn,
            partitionContext,
            partition));

        var subsize = Vp9ModeInfoSyntax.GetSubsize(blockSize, partition);
        var childPath = partitionPath.Concat([partition]).ToArray();
        if (hbs == 0)
        {
            if (!TryReadInterBlockModeInfo(
                    ref reader,
                    header,
                    compressedHeader,
                    geometry,
                    syntaxContext,
                    miRow,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    decodedModeBlocks,
                    out diagnostic,
                    previousFrameMotionVectors))
            {
                return false;
            }

            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
            return true;
        }

        switch (partition)
        {
            case Vp9PartitionType.None:
                if (!TryReadInterBlockModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                break;

            case Vp9PartitionType.Horizontal:
                if (!TryReadInterBlockModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows)
                {
                    if (!TryReadInterBlockModeInfo(
                            ref reader,
                            header,
                            compressedHeader,
                            geometry,
                            syntaxContext,
                            miRow + hbs,
                            miColumn,
                            subsize,
                            childPath,
                            modes,
                            decodedModeBlocks,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            case Vp9PartitionType.Vertical:
                if (!TryReadInterBlockModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasColumns)
                {
                    if (!TryReadInterBlockModeInfo(
                            ref reader,
                            header,
                            compressedHeader,
                            geometry,
                            syntaxContext,
                            miRow,
                            miColumn + hbs,
                            subsize,
                            childPath,
                            modes,
                            decodedModeBlocks,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            case Vp9PartitionType.Split:
                if (!TryReadInterPartitionModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        partitions,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasColumns &&
                    !TryReadInterPartitionModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        miRow,
                        miColumn + hbs,
                        subsize,
                        childPath,
                        partitions,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows &&
                    !TryReadInterPartitionModeInfo(
                        ref reader,
                        header,
                        compressedHeader,
                        geometry,
                        syntaxContext,
                        miRow + hbs,
                        miColumn,
                        subsize,
                        childPath,
                        partitions,
                        modes,
                        decodedModeBlocks,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows && hasColumns)
                {
                    if (!TryReadInterPartitionModeInfo(
                            ref reader,
                            header,
                            compressedHeader,
                            geometry,
                            syntaxContext,
                            miRow + hbs,
                            miColumn + hbs,
                            subsize,
                            childPath,
                            partitions,
                            modes,
                            decodedModeBlocks,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(partition), partition, "Unsupported VP9 partition type.");
        }

        if (blockSize >= Vp9BlockSize.Block8X8 &&
            (blockSize == Vp9BlockSize.Block8X8 || partition != Vp9PartitionType.Split))
        {
            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
        }

        return true;
    }

    private static bool TryReadInterPartitionResidualSyntax(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext entropyContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        List<Vp9PartitionProbe> partitions,
        List<Vp9InterBlockModeInfoProbe> modes,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        List<Vp9CoefficientBlockGroupProbe> coefficientGroups,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        diagnostic = null;
        if (miRow >= header.TileInfo.MiRows || miColumn >= header.TileInfo.MiColumns)
        {
            return true;
        }

        var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
        var hasRows = miRow + hbs < header.TileInfo.MiRows;
        var hasColumns = miColumn + hbs < header.TileInfo.MiColumns;
        var partitionContext = syntaxContext.GetPartitionContext(miRow, miColumn, blockSize);
        var partition = Vp9PartitionSyntax.ReadPartition(
            ref reader,
            compressedHeader.FrameContext,
            partitionContext,
            hasRows,
            hasColumns);
        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                $"VP9 inter partition residual probe ended unexpectedly at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}) block {blockSize}.");
            return false;
        }

        partitions.Add(new Vp9PartitionProbe(
            geometry.Buffer.Index,
            miRow,
            miColumn,
            partitionContext,
            partition));

        var subsize = Vp9ModeInfoSyntax.GetSubsize(blockSize, partition);
        var childPath = partitionPath.Concat([partition]).ToArray();
        if (hbs == 0)
        {
            if (!TryReadInterBlockModeInfoAndResidual(
                    ref reader,
                    header,
                    compressedHeader,
                    dequantTables,
                    geometry,
                    syntaxContext,
                    entropyContext,
                    miRow,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    decodedModeBlocks,
                    coefficientGroups,
                    out diagnostic,
                    previousFrameMotionVectors))
            {
                return false;
            }

            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
            return true;
        }

        switch (partition)
        {
            case Vp9PartitionType.None:
                if (!TryReadInterBlockModeInfoAndResidual(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        decodedModeBlocks,
                        coefficientGroups,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                break;

            case Vp9PartitionType.Horizontal:
                if (!TryReadInterBlockModeInfoAndResidual(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        decodedModeBlocks,
                        coefficientGroups,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows)
                {
                    if (!TryReadInterBlockModeInfoAndResidual(
                            ref reader,
                            header,
                            compressedHeader,
                            dequantTables,
                            geometry,
                            syntaxContext,
                            entropyContext,
                            miRow + hbs,
                            miColumn,
                            subsize,
                            childPath,
                            modes,
                            decodedModeBlocks,
                            coefficientGroups,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            case Vp9PartitionType.Vertical:
                if (!TryReadInterBlockModeInfoAndResidual(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        decodedModeBlocks,
                        coefficientGroups,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasColumns)
                {
                    if (!TryReadInterBlockModeInfoAndResidual(
                            ref reader,
                            header,
                            compressedHeader,
                            dequantTables,
                            geometry,
                            syntaxContext,
                            entropyContext,
                            miRow,
                            miColumn + hbs,
                            subsize,
                            childPath,
                            modes,
                            decodedModeBlocks,
                            coefficientGroups,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            case Vp9PartitionType.Split:
                if (!TryReadInterPartitionResidualSyntax(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        partitions,
                        modes,
                        decodedModeBlocks,
                        coefficientGroups,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasColumns &&
                    !TryReadInterPartitionResidualSyntax(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        miRow,
                        miColumn + hbs,
                        subsize,
                        childPath,
                        partitions,
                        modes,
                        decodedModeBlocks,
                        coefficientGroups,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows &&
                    !TryReadInterPartitionResidualSyntax(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        miRow + hbs,
                        miColumn,
                        subsize,
                        childPath,
                        partitions,
                        modes,
                        decodedModeBlocks,
                        coefficientGroups,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows && hasColumns)
                {
                    if (!TryReadInterPartitionResidualSyntax(
                            ref reader,
                            header,
                            compressedHeader,
                            dequantTables,
                            geometry,
                            syntaxContext,
                            entropyContext,
                            miRow + hbs,
                            miColumn + hbs,
                            subsize,
                            childPath,
                            partitions,
                            modes,
                            decodedModeBlocks,
                            coefficientGroups,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(partition), partition, "Unsupported VP9 partition type.");
        }

        if (blockSize >= Vp9BlockSize.Block8X8 &&
            (blockSize == Vp9BlockSize.Block8X8 || partition != Vp9PartitionType.Split))
        {
            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
        }

        return true;
    }

    private static bool TryReconstructInterPartitionResidualSyntaxDirect(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext entropyContext,
        Vp9YuvFrameBuffer destination,
        Vp9ReferenceFrameStore referenceFrames,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        List<Vp9InterBlockModeInfoProbe> predictedModeBlocks,
        Vp9DirectInterResidualScratch residualScratch,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        diagnostic = null;
        if (miRow >= header.TileInfo.MiRows || miColumn >= header.TileInfo.MiColumns)
        {
            return true;
        }

        var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
        var hasRows = miRow + hbs < header.TileInfo.MiRows;
        var hasColumns = miColumn + hbs < header.TileInfo.MiColumns;
        var partitionContext = syntaxContext.GetPartitionContext(miRow, miColumn, blockSize);
        var partition = Vp9PartitionSyntax.ReadPartition(
            ref reader,
            compressedHeader.FrameContext,
            partitionContext,
            hasRows,
            hasColumns);
        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                $"VP9 direct inter partition read ended unexpectedly at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}) block {blockSize}.");
            return false;
        }

        var subsize = Vp9ModeInfoSyntax.GetSubsize(blockSize, partition);
        if (hbs == 0)
        {
            if (!TryReconstructInterBlockModeInfoAndResidualDirect(
                    ref reader,
                    header,
                    compressedHeader,
                    dequantTables,
                    geometry,
                    syntaxContext,
                    entropyContext,
                    destination,
                    referenceFrames,
                    miRow,
                    miColumn,
                    subsize,
                    decodedModeBlocks,
                    predictedModeBlocks,
                    residualScratch,
                    out diagnostic,
                    previousFrameMotionVectors))
            {
                return false;
            }

            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
            return true;
        }

        switch (partition)
        {
            case Vp9PartitionType.None:
                if (!TryReconstructInterBlockModeInfoAndResidualDirect(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        destination,
                        referenceFrames,
                        miRow,
                        miColumn,
                        subsize,
                        decodedModeBlocks,
                        predictedModeBlocks,
                        residualScratch,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                break;

            case Vp9PartitionType.Horizontal:
                if (!TryReconstructInterBlockModeInfoAndResidualDirect(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        destination,
                        referenceFrames,
                        miRow,
                        miColumn,
                        subsize,
                        decodedModeBlocks,
                        predictedModeBlocks,
                        residualScratch,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows)
                {
                    if (!TryReconstructInterBlockModeInfoAndResidualDirect(
                            ref reader,
                            header,
                            compressedHeader,
                            dequantTables,
                            geometry,
                            syntaxContext,
                            entropyContext,
                            destination,
                            referenceFrames,
                            miRow + hbs,
                            miColumn,
                            subsize,
                            decodedModeBlocks,
                            predictedModeBlocks,
                            residualScratch,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            case Vp9PartitionType.Vertical:
                if (!TryReconstructInterBlockModeInfoAndResidualDirect(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        destination,
                        referenceFrames,
                        miRow,
                        miColumn,
                        subsize,
                        decodedModeBlocks,
                        predictedModeBlocks,
                        residualScratch,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasColumns)
                {
                    if (!TryReconstructInterBlockModeInfoAndResidualDirect(
                            ref reader,
                            header,
                            compressedHeader,
                            dequantTables,
                            geometry,
                            syntaxContext,
                            entropyContext,
                            destination,
                            referenceFrames,
                            miRow,
                            miColumn + hbs,
                            subsize,
                            decodedModeBlocks,
                            predictedModeBlocks,
                            residualScratch,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            case Vp9PartitionType.Split:
                if (!TryReconstructInterPartitionResidualSyntaxDirect(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        destination,
                        referenceFrames,
                        miRow,
                        miColumn,
                        subsize,
                        decodedModeBlocks,
                        predictedModeBlocks,
                        residualScratch,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasColumns &&
                    !TryReconstructInterPartitionResidualSyntaxDirect(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        destination,
                        referenceFrames,
                        miRow,
                        miColumn + hbs,
                        subsize,
                        decodedModeBlocks,
                        predictedModeBlocks,
                        residualScratch,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows &&
                    !TryReconstructInterPartitionResidualSyntaxDirect(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        destination,
                        referenceFrames,
                        miRow + hbs,
                        miColumn,
                        subsize,
                        decodedModeBlocks,
                        predictedModeBlocks,
                        residualScratch,
                        out diagnostic,
                        previousFrameMotionVectors))
                {
                    return false;
                }

                if (hasRows && hasColumns)
                {
                    if (!TryReconstructInterPartitionResidualSyntaxDirect(
                            ref reader,
                            header,
                            compressedHeader,
                            dequantTables,
                            geometry,
                            syntaxContext,
                            entropyContext,
                            destination,
                            referenceFrames,
                            miRow + hbs,
                            miColumn + hbs,
                            subsize,
                            decodedModeBlocks,
                            predictedModeBlocks,
                            residualScratch,
                            out diagnostic,
                            previousFrameMotionVectors))
                    {
                        return false;
                    }
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(partition), partition, "Unsupported VP9 partition type.");
        }

        if (blockSize >= Vp9BlockSize.Block8X8 &&
            (blockSize == Vp9BlockSize.Block8X8 || partition != Vp9PartitionType.Split))
        {
            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
        }

        return true;
    }

    private static bool TryReconstructInterBlockModeInfoAndResidualDirect(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext entropyContext,
        Vp9YuvFrameBuffer destination,
        Vp9ReferenceFrameStore referenceFrames,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        List<Vp9InterBlockModeInfoProbe> predictedModeBlocks,
        Vp9DirectInterResidualScratch residualScratch,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        if (!TryReadInterBlockModeInfoCore(
                ref reader,
                header,
                compressedHeader,
                geometry,
                syntaxContext,
                miRow,
                miColumn,
                blockSize,
                [],
                decodedModeBlocks,
                out var modeBlock,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        decodedModeBlocks.Add(modeBlock);
        if (!modeBlock.ModeInfo.IsInterBlock)
        {
            residualScratch.Groups.Clear();
            ReadInterBlockCoefficientGroups(
                ref reader,
                header,
                compressedHeader,
                dequantTables,
                modeBlock,
                entropyContext,
                residualScratch.Groups);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                    $"VP9 direct intra residual read ended unexpectedly at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}) block {blockSize}.");
                return false;
            }

            if (residualScratch.Groups.Count != 3)
            {
                diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 direct intra reconstruction expected exactly three residual coefficient groups.");
                return false;
            }

            var intraModeInfo = modeBlock.ToIntraModeInfoProbe();
            for (var plane = 0; plane < 3; plane++)
            {
                Vp9BlockReconstructor.ReconstructDcOnlyGroup(
                    destination,
                    geometry,
                    intraModeInfo,
                    residualScratch.Groups[plane],
                    plane);
            }

            predictedModeBlocks.Add(modeBlock);
            return true;
        }

        if (!TryPredictInterBlock(
                referenceFrames,
                header,
                destination,
                modeBlock,
                predictedModeBlocks,
                out var predictedModeBlock,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        var eobTotal = 0;
        for (var plane = 0; plane < 3; plane++)
        {
            eobTotal += Vp9ResidualSyntax.ReadAndAddInterPlaneCoefficientBlocks(
                ref reader,
                header,
                compressedHeader,
                dequantTables,
                modeBlock,
                entropyContext,
                destination,
                plane);
        }

        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                $"VP9 direct inter residual read ended unexpectedly at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}) block {blockSize}.");
            return false;
        }

        if (ShouldMarkInterBlockSkippedForSyntaxContext(modeBlock, eobTotal))
        {
            modeBlock = modeBlock with
            {
                ModeInfo = modeBlock.ModeInfo with
                {
                    Skip = true
                }
            };
            decodedModeBlocks[^1] = modeBlock;
            syntaxContext.SetModeInfo(miRow, miColumn, modeBlock.ModeInfo);
            predictedModeBlock = predictedModeBlock with
            {
                ModeInfo = modeBlock.ModeInfo
            };
        }

        predictedModeBlocks.Add(predictedModeBlock);
        return true;
    }

    private static bool TryReadInterBlockModeInfoAndResidual(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext entropyContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        List<Vp9InterBlockModeInfoProbe> modes,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        List<Vp9CoefficientBlockGroupProbe> coefficientGroups,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        var modeCount = modes.Count;
        if (!TryReadInterBlockModeInfo(
                ref reader,
                header,
                compressedHeader,
                geometry,
                syntaxContext,
                miRow,
                miColumn,
                blockSize,
                partitionPath,
                modes,
                decodedModeBlocks,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        if (modes.Count != modeCount + 1)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 inter block mode-info did not append exactly one mode block.");
            return false;
        }

        var modeBlock = modes[^1];
        var appendedGroupStart = coefficientGroups.Count;
        ReadInterBlockCoefficientGroups(
            ref reader,
            header,
            compressedHeader,
            dequantTables,
            modeBlock,
            entropyContext,
            coefficientGroups);
        if (ShouldMarkInterBlockSkippedForSyntaxContext(modeBlock, coefficientGroups, appendedGroupStart))
        {
            modeBlock = modeBlock with
            {
                ModeInfo = modeBlock.ModeInfo with
                {
                    Skip = true
                }
            };
            modes[^1] = modeBlock;
            decodedModeBlocks[^1] = modeBlock;
            syntaxContext.SetModeInfo(miRow, miColumn, modeBlock.ModeInfo);
        }

        return true;
    }

    private static bool ShouldMarkInterBlockSkippedForSyntaxContext(
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9CoefficientBlockGroupProbe> coefficientGroups,
        int groupStart)
    {
        if (!modeBlock.ModeInfo.IsInterBlock ||
            modeBlock.ModeInfo.Skip ||
            modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8)
        {
            return false;
        }

        var eobTotal = 0;
        for (var groupIndex = groupStart; groupIndex < coefficientGroups.Count; groupIndex++)
        {
            foreach (var block in coefficientGroups[groupIndex].Blocks)
            {
                eobTotal += block.Eob;
            }
        }

        return eobTotal == 0;
    }

    private static bool ShouldMarkInterBlockSkippedForSyntaxContext(
        Vp9InterBlockModeInfoProbe modeBlock,
        int eobTotal)
    {
        return modeBlock.ModeInfo.IsInterBlock &&
            !modeBlock.ModeInfo.Skip &&
            modeBlock.ModeInfo.BlockSize >= Vp9BlockSize.Block8X8 &&
            eobTotal == 0;
    }

    private static void ReadInterBlockCoefficientGroups(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9CoefficientEntropyContext entropyContext,
        List<Vp9CoefficientBlockGroupProbe> coefficientGroups)
    {
        for (var plane = 0; plane < 3; plane++)
        {
            var group = Vp9ResidualSyntax.ReadInterPlaneCoefficientBlocks(
                ref reader,
                header,
                compressedHeader,
                dequantTables,
                modeBlock,
                entropyContext,
                plane);
            coefficientGroups.Add(group);
        }
    }

    private static bool TryReadInterBlockModeInfo(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        List<Vp9InterBlockModeInfoProbe> modes,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        if (!TryReadInterBlockModeInfoCore(
                ref reader,
                header,
                compressedHeader,
                geometry,
                syntaxContext,
                miRow,
                miColumn,
                blockSize,
                partitionPath,
                decodedModeBlocks,
                out var modeBlock,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        modes.Add(modeBlock);
        decodedModeBlocks.Add(modeBlock);
        return true;
    }

    private static bool TryReadInterBlockModeInfoCore(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        out Vp9InterBlockModeInfoProbe modeBlock,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        modeBlock = null!;
        var singleReferenceContexts = syntaxContext.GetSingleReferenceContexts(
            miRow,
            miColumn,
            geometry.MiColumnStart);
        var compoundReferenceSetup = Vp9InterModeInfoSyntax.GetCompoundReferenceSetup(header.ReferenceFrameSignBiases);
        var contexts = new Vp9InterModeInfoContexts(
            Skip: syntaxContext.GetSkipContext(miRow, miColumn, geometry.MiColumnStart),
            IntraInter: syntaxContext.GetIntraInterContext(miRow, miColumn, geometry.MiColumnStart),
            TransformSize: syntaxContext.GetTransformSizeContext(miRow, miColumn, geometry.MiColumnStart, blockSize),
            CompoundInter: syntaxContext.GetCompoundInterContext(
                miRow,
                miColumn,
                geometry.MiColumnStart,
                compoundReferenceSetup.FixedReferenceFrame),
            CompoundReference: syntaxContext.GetCompoundReferenceContext(
                miRow,
                miColumn,
                geometry.MiColumnStart,
                compoundReferenceSetup,
                header.ReferenceFrameSignBiases),
            SingleReference0: singleReferenceContexts.Context0,
            SingleReference1: singleReferenceContexts.Context1,
            InterMode: syntaxContext.GetInterModeContext(
                miRow,
                miColumn,
                blockSize,
                geometry.MiColumnStart,
                geometry.MiColumnEnd),
            SwitchableInterpolation: header.InterpolationFilter == Vp9InterpolationFilter.Switchable
                ? syntaxContext.GetSwitchableInterpolationContext(miRow, miColumn, geometry.MiColumnStart)
                : 0);
        if (!Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
                ref reader,
                header,
                compressedHeader,
                blockSize,
                contexts,
                out var modeInfo,
                out diagnostic))
        {
            if (diagnostic?.Code == Vp9DecodeDiagnosticCode.TruncatedPacket)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                    $"{diagnostic.Message} at tile {geometry.Buffer.Index} MI ({miRow},{miColumn}) block {blockSize}.");
            }

            return false;
        }

        if (modeInfo is null)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 inter mode-info probe succeeded without returning a mode-info record.");
            return false;
        }

        modeBlock = new Vp9InterBlockModeInfoProbe(
            geometry.Buffer.Index,
            miRow,
            miColumn,
            partitionPath,
            modeInfo);
        if (!modeInfo.IsInterBlock)
        {
            syntaxContext.SetModeInfo(miRow, miColumn, modeBlock.ModeInfo);
            return true;
        }

        if (!TryReadInterBlockMotionVector(
                ref reader,
                header,
                compressedHeader,
                modeBlock,
                decodedModeBlocks,
                out modeBlock,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        syntaxContext.SetModeInfo(miRow, miColumn, modeBlock.ModeInfo);
        return true;
    }

    private static bool TryReadInterSuperblockResidualSyntax(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9TileGeometry geometry,
        Vp9InterFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext entropyContext,
        List<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        int miRow,
        int miColumn,
        out Vp9InterSuperblockSyntaxProbe? probe,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        probe = null;
        var partitions = new List<Vp9PartitionProbe>();
        var modes = new List<Vp9InterBlockModeInfoProbe>();
        var coefficientGroups = new List<Vp9CoefficientBlockGroupProbe>();
        if (!TryReadInterPartitionResidualSyntax(
                ref reader,
                header,
                compressedHeader,
                dequantTables,
                geometry,
                syntaxContext,
                entropyContext,
                miRow,
                miColumn,
                Vp9BlockSize.Block64X64,
                [],
                partitions,
                modes,
                decodedModeBlocks,
                coefficientGroups,
                out diagnostic,
                previousFrameMotionVectors))
        {
            return false;
        }

        var expectedGroupCount = checked(modes.Count * 3);
        if (coefficientGroups.Count != expectedGroupCount)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 inter residual probe produced mismatched mode/coefficient groups.");
            return false;
        }

        probe = new Vp9InterSuperblockSyntaxProbe(
            geometry.Buffer.Index,
            partitions,
            modes,
            coefficientGroups);
        return true;
    }

    private static bool TryValidateInter8BitYuv420Header(
        Vp9FrameHeader header,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (header.BitDepth != 8)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedBitDepth(
                "VP9 inter prediction probe currently supports only 8-bit frames.");
            return false;
        }

        if (header.SubsamplingX != 1 || header.SubsamplingY != 1)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedChromaSubsampling(
                "VP9 inter prediction probe currently supports only YUV420 frames.");
            return false;
        }

        return true;
    }

    private static bool TryCopyInterPredictionBlock(
        Vp9DecodedFrame referenceFrame,
        Vp9YuvFrameBuffer destination,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var destinationX = modeBlock.MiColumn * 8;
        var destinationY = modeBlock.MiRow * 8;
        var blockWidth = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeBlock.ModeInfo.BlockSize) * 8;
        var blockHeight = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeBlock.ModeInfo.BlockSize) * 8;
        var visibleWidth = Math.Min(blockWidth, header.Width - destinationX);
        var visibleHeight = Math.Min(blockHeight, header.Height - destinationY);
        if (visibleWidth <= 0 || visibleHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 inter prediction block lies outside the visible frame.");
            return false;
        }

        if (modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8 &&
            modeBlock.InterSubMotionVectors.Count > 0)
        {
            return TryCopySub8X8InterPredictionBlock(
                referenceFrame,
                destination,
                header,
                modeBlock,
                destinationX,
                destinationY,
                visibleWidth,
                visibleHeight,
                out diagnostic);
        }

        if (!Vp9MotionCompensator.TryCopyPlaneBlock(
                referenceFrame,
                destination,
                Vp9Plane.Y,
                destinationX,
                destinationY,
                visibleWidth,
                visibleHeight,
                ScaleLumaMotionVectorToQ4(motionVector),
                modeBlock.ModeInfo.InterpolationFilter,
                out diagnostic))
        {
            return false;
        }

        var chromaMotionVector = ScaleYuv420ChromaMotionVectorToQ4(motionVector);
        var chromaDestinationX = destinationX / 2;
        var chromaDestinationY = destinationY / 2;
        var chromaWidth = Math.Min((visibleWidth + 1) / 2, ((header.Width + 1) / 2) - chromaDestinationX);
        var chromaHeight = Math.Min((visibleHeight + 1) / 2, ((header.Height + 1) / 2) - chromaDestinationY);
        if (chromaWidth <= 0 || chromaHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 inter prediction chroma block lies outside the visible frame.");
            return false;
        }

        if (!Vp9MotionCompensator.TryCopyPlaneBlock(
                referenceFrame,
                destination,
                Vp9Plane.U,
                chromaDestinationX,
                chromaDestinationY,
                chromaWidth,
                chromaHeight,
                chromaMotionVector,
                modeBlock.ModeInfo.InterpolationFilter,
                out diagnostic))
        {
            return false;
        }

        return Vp9MotionCompensator.TryCopyPlaneBlock(
            referenceFrame,
            destination,
            Vp9Plane.V,
            chromaDestinationX,
            chromaDestinationY,
            chromaWidth,
            chromaHeight,
            chromaMotionVector,
            modeBlock.ModeInfo.InterpolationFilter,
            out diagnostic);
    }

    private static bool TryCopyCompoundInterPredictionBlock(
        Vp9DecodedFrame referenceFrame0,
        Vp9DecodedFrame referenceFrame1,
        Vp9YuvFrameBuffer destination,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9MotionVector motionVector0,
        Vp9MotionVector motionVector1,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        if (modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 compound sub-8x8 inter prediction is not supported yet.");
            return false;
        }

        var destinationX = modeBlock.MiColumn * 8;
        var destinationY = modeBlock.MiRow * 8;
        var blockWidth = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeBlock.ModeInfo.BlockSize) * 8;
        var blockHeight = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeBlock.ModeInfo.BlockSize) * 8;
        var visibleWidth = Math.Min(blockWidth, header.Width - destinationX);
        var visibleHeight = Math.Min(blockHeight, header.Height - destinationY);
        if (visibleWidth <= 0 || visibleHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound inter prediction block lies outside the visible frame.");
            return false;
        }

        if (!Vp9MotionCompensator.TryAveragePlaneBlock(
                referenceFrame0,
                referenceFrame1,
                destination,
                Vp9Plane.Y,
                destinationX,
                destinationY,
                visibleWidth,
                visibleHeight,
                ScaleLumaMotionVectorToQ4(motionVector0),
                ScaleLumaMotionVectorToQ4(motionVector1),
                modeBlock.ModeInfo.InterpolationFilter,
                out diagnostic))
        {
            return false;
        }

        var chromaMotionVector0 = ScaleYuv420ChromaMotionVectorToQ4(motionVector0);
        var chromaMotionVector1 = ScaleYuv420ChromaMotionVectorToQ4(motionVector1);
        var chromaDestinationX = destinationX / 2;
        var chromaDestinationY = destinationY / 2;
        var chromaWidth = Math.Min((visibleWidth + 1) / 2, ((header.Width + 1) / 2) - chromaDestinationX);
        var chromaHeight = Math.Min((visibleHeight + 1) / 2, ((header.Height + 1) / 2) - chromaDestinationY);
        if (chromaWidth <= 0 || chromaHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound inter prediction chroma block lies outside the visible frame.");
            return false;
        }

        if (!Vp9MotionCompensator.TryAveragePlaneBlock(
                referenceFrame0,
                referenceFrame1,
                destination,
                Vp9Plane.U,
                chromaDestinationX,
                chromaDestinationY,
                chromaWidth,
                chromaHeight,
                chromaMotionVector0,
                chromaMotionVector1,
                modeBlock.ModeInfo.InterpolationFilter,
                out diagnostic))
        {
            return false;
        }

        return Vp9MotionCompensator.TryAveragePlaneBlock(
            referenceFrame0,
            referenceFrame1,
            destination,
            Vp9Plane.V,
            chromaDestinationX,
            chromaDestinationY,
            chromaWidth,
            chromaHeight,
            chromaMotionVector0,
            chromaMotionVector1,
            modeBlock.ModeInfo.InterpolationFilter,
            out diagnostic);
    }

    private static bool TryCopyCompoundSub8X8InterPredictionBlock(
        Vp9DecodedFrame referenceFrame0,
        Vp9DecodedFrame referenceFrame1,
        Vp9YuvFrameBuffer destination,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (modeBlock.InterSubMotionVectors.Count != 4 ||
            modeBlock.CompoundInterSubMotionVectors.Count != 4)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound sub-8x8 inter prediction requires four sub-block motion vectors for both references.");
            return false;
        }

        var destinationX = modeBlock.MiColumn * 8;
        var destinationY = modeBlock.MiRow * 8;
        var blockWidth = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeBlock.ModeInfo.BlockSize) * 8;
        var blockHeight = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeBlock.ModeInfo.BlockSize) * 8;
        var visibleWidth = Math.Min(blockWidth, header.Width - destinationX);
        var visibleHeight = Math.Min(blockHeight, header.Height - destinationY);
        if (visibleWidth <= 0 || visibleHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound sub-8x8 inter prediction block lies outside the visible frame.");
            return false;
        }

        for (var block = 0; block < 4; block++)
        {
            var subX = destinationX + ((block & 1) * 4);
            var subY = destinationY + ((block >> 1) * 4);
            var subVisibleWidth = Math.Min(4, header.Width - subX);
            var subVisibleHeight = Math.Min(4, header.Height - subY);
            if (subVisibleWidth <= 0 || subVisibleHeight <= 0)
            {
                continue;
            }

            if (!Vp9MotionCompensator.TryAveragePlaneBlock(
                    referenceFrame0,
                    referenceFrame1,
                    destination,
                    Vp9Plane.Y,
                    subX,
                    subY,
                    subVisibleWidth,
                    subVisibleHeight,
                    ScaleLumaMotionVectorToQ4(modeBlock.InterSubMotionVectors[block]),
                    ScaleLumaMotionVectorToQ4(modeBlock.CompoundInterSubMotionVectors[block]),
                    modeBlock.ModeInfo.InterpolationFilter,
                    out diagnostic))
            {
                return false;
            }
        }

        var averageMotionVector0 = AverageSub8X8MotionVectors(modeBlock.InterSubMotionVectors);
        var averageMotionVector1 = AverageSub8X8MotionVectors(modeBlock.CompoundInterSubMotionVectors);
        var chromaMotionVector0 = ScaleYuv420ChromaMotionVectorToQ4(averageMotionVector0);
        var chromaMotionVector1 = ScaleYuv420ChromaMotionVectorToQ4(averageMotionVector1);
        var chromaDestinationX = destinationX / 2;
        var chromaDestinationY = destinationY / 2;
        var chromaWidth = Math.Min((visibleWidth + 1) / 2, ((header.Width + 1) / 2) - chromaDestinationX);
        var chromaHeight = Math.Min((visibleHeight + 1) / 2, ((header.Height + 1) / 2) - chromaDestinationY);
        if (chromaWidth <= 0 || chromaHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound sub-8x8 inter prediction chroma block lies outside the visible frame.");
            return false;
        }

        if (!Vp9MotionCompensator.TryAveragePlaneBlock(
                referenceFrame0,
                referenceFrame1,
                destination,
                Vp9Plane.U,
                chromaDestinationX,
                chromaDestinationY,
                chromaWidth,
                chromaHeight,
                chromaMotionVector0,
                chromaMotionVector1,
                modeBlock.ModeInfo.InterpolationFilter,
                out diagnostic))
        {
            return false;
        }

        return Vp9MotionCompensator.TryAveragePlaneBlock(
            referenceFrame0,
            referenceFrame1,
            destination,
            Vp9Plane.V,
            chromaDestinationX,
            chromaDestinationY,
            chromaWidth,
            chromaHeight,
            chromaMotionVector0,
            chromaMotionVector1,
            modeBlock.ModeInfo.InterpolationFilter,
            out diagnostic);
    }

    private static bool TryCopySub8X8InterPredictionBlock(
        Vp9DecodedFrame referenceFrame,
        Vp9YuvFrameBuffer destination,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        int destinationX,
        int destinationY,
        int visibleWidth,
        int visibleHeight,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (modeBlock.InterSubMotionVectors.Count != 4)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 sub-8x8 inter prediction requires four sub-block motion vectors.");
            return false;
        }

        for (var block = 0; block < 4; block++)
        {
            var subX = destinationX + ((block & 1) * 4);
            var subY = destinationY + ((block >> 1) * 4);
            var subVisibleWidth = Math.Min(4, header.Width - subX);
            var subVisibleHeight = Math.Min(4, header.Height - subY);
            if (subVisibleWidth <= 0 || subVisibleHeight <= 0)
            {
                continue;
            }

            if (!Vp9MotionCompensator.TryCopyPlaneBlock(
                    referenceFrame,
                    destination,
                    Vp9Plane.Y,
                    subX,
                    subY,
                    subVisibleWidth,
                    subVisibleHeight,
                    ScaleLumaMotionVectorToQ4(modeBlock.InterSubMotionVectors[block]),
                    modeBlock.ModeInfo.InterpolationFilter,
                    out diagnostic))
            {
                return false;
            }
        }

        var averageMotionVector = AverageSub8X8MotionVectors(modeBlock.InterSubMotionVectors);
        var chromaMotionVector = ScaleYuv420ChromaMotionVectorToQ4(averageMotionVector);
        var chromaDestinationX = destinationX / 2;
        var chromaDestinationY = destinationY / 2;
        var chromaWidth = Math.Min((visibleWidth + 1) / 2, ((header.Width + 1) / 2) - chromaDestinationX);
        var chromaHeight = Math.Min((visibleHeight + 1) / 2, ((header.Height + 1) / 2) - chromaDestinationY);
        if (chromaWidth <= 0 || chromaHeight <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 sub-8x8 inter prediction chroma block lies outside the visible frame.");
            return false;
        }

        if (!Vp9MotionCompensator.TryCopyPlaneBlock(
                referenceFrame,
                destination,
                Vp9Plane.U,
                chromaDestinationX,
                chromaDestinationY,
                chromaWidth,
                chromaHeight,
                chromaMotionVector,
                modeBlock.ModeInfo.InterpolationFilter,
                out diagnostic))
        {
            return false;
        }

        return Vp9MotionCompensator.TryCopyPlaneBlock(
            referenceFrame,
            destination,
            Vp9Plane.V,
            chromaDestinationX,
            chromaDestinationY,
            chromaWidth,
            chromaHeight,
            chromaMotionVector,
            modeBlock.ModeInfo.InterpolationFilter,
            out diagnostic);
    }

    private static Vp9MotionVector ScaleLumaMotionVectorToQ4(Vp9MotionVector motionVector)
    {
        return new Vp9MotionVector(checked(motionVector.Row * 2), checked(motionVector.Column * 2));
    }

    private static Vp9MotionVector ScaleYuv420ChromaMotionVectorToQ4(Vp9MotionVector motionVector)
    {
        return motionVector;
    }

    private static Vp9MotionVector AverageSub8X8MotionVectors(IReadOnlyList<Vp9MotionVector> motionVectors)
    {
        var row = 0;
        var column = 0;
        for (var i = 0; i < 4; i++)
        {
            row += motionVectors[i].Row;
            column += motionVectors[i].Column;
        }

        return new Vp9MotionVector(
            RoundQuarterMotionVectorComponent(row),
            RoundQuarterMotionVectorComponent(column));
    }

    private static int RoundQuarterMotionVectorComponent(int value)
    {
        return (value < 0 ? value - 2 : value + 2) / 4;
    }

    private static bool TryPredictInterBlock(
        Vp9ReferenceFrameStore referenceFrames,
        Vp9FrameHeader header,
        Vp9YuvFrameBuffer destination,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> predictedModeBlocks,
        out Vp9InterBlockModeInfoProbe predictedModeBlock,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors = null)
    {
        predictedModeBlock = modeBlock;
        if (modeBlock.ModeInfo.CompoundReferenceFrame is { } compoundReferenceFrame)
        {
            return TryPredictCompoundInterBlock(
                referenceFrames,
                header,
                destination,
                modeBlock,
                compoundReferenceFrame,
                out predictedModeBlock,
                out diagnostic);
        }

        if (!Vp9InterPredictor.TryResolveReferenceFrame(
                referenceFrames,
                header,
                modeBlock.ModeInfo.ReferenceFrame,
                out var referenceFrame,
                out diagnostic))
        {
            return false;
        }

        if (referenceFrame is null)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 inter reference lookup succeeded without returning a reference frame.");
            return false;
        }

        var candidates = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
            header,
            modeBlock,
            Vp9InterPredictor.BuildSpatialMotionVectorCandidateSet(
                modeBlock,
                predictedModeBlocks,
                header.ReferenceFrameSignBiases,
                GetEligiblePreviousFrameMotionVectors(header, previousFrameMotionVectors)));
        if (!Vp9InterPredictor.TrySelectMotionVector(
                modeBlock,
                candidates,
                out var motionVector,
                out diagnostic))
        {
            return false;
        }

        if (!TryValidateSub8X8InterMotionVector(modeBlock, motionVector, out diagnostic))
        {
            return false;
        }

        if (!TryCopyInterPredictionBlock(
                referenceFrame.Frame,
                destination,
                header,
                modeBlock,
                motionVector,
                out diagnostic))
        {
            return false;
        }

        predictedModeBlock = modeBlock with
        {
            MotionVector = motionVector
        };
        return true;
    }

    private static bool TryPredictCompoundInterBlock(
        Vp9ReferenceFrameStore referenceFrames,
        Vp9FrameHeader header,
        Vp9YuvFrameBuffer destination,
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9InterReferenceFrame compoundReferenceFrame,
        out Vp9InterBlockModeInfoProbe predictedModeBlock,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        predictedModeBlock = modeBlock;
        if (!Vp9InterPredictor.TryResolveReferenceFrame(
                referenceFrames,
                header,
                modeBlock.ModeInfo.ReferenceFrame,
                out var referenceFrame0,
                out diagnostic) ||
            !Vp9InterPredictor.TryResolveReferenceFrame(
                referenceFrames,
                header,
                compoundReferenceFrame,
                out var referenceFrame1,
                out diagnostic))
        {
            return false;
        }

        if (referenceFrame0 is null || referenceFrame1 is null)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 compound reference lookup succeeded without returning both reference frames.");
            return false;
        }

        if (modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8 &&
            modeBlock.InterSubMotionVectors.Count > 0 &&
            modeBlock.CompoundInterSubMotionVectors.Count > 0)
        {
            if (!TryCopyCompoundSub8X8InterPredictionBlock(
                    referenceFrame0.Frame,
                    referenceFrame1.Frame,
                    destination,
                    header,
                    modeBlock,
                    out diagnostic))
            {
                return false;
            }

            predictedModeBlock = modeBlock;
            return true;
        }

        var motionVector0 = modeBlock.MotionVector;
        var motionVector1 = modeBlock.CompoundMotionVector;
        if (motionVector0 is null || motionVector1 is null)
        {
            if (modeBlock.ModeInfo.PredictionMode != Vp9InterPredictionMode.ZeroMv)
            {
                diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                    "VP9 compound inter prediction is missing decoded motion vectors.");
                return false;
            }

            motionVector0 = new Vp9MotionVector(0, 0);
            motionVector1 = new Vp9MotionVector(0, 0);
        }

        if (!Vp9InterPredictor.IsValidMotionVector(motionVector0.Value) ||
            !Vp9InterPredictor.IsValidMotionVector(motionVector1.Value))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound inter mode-info decoded an out-of-range motion vector.");
            return false;
        }

        if (!TryCopyCompoundInterPredictionBlock(
                referenceFrame0.Frame,
                referenceFrame1.Frame,
                destination,
                header,
                modeBlock,
                motionVector0.Value,
                motionVector1.Value,
                out diagnostic))
        {
            return false;
        }

        predictedModeBlock = modeBlock with
        {
            MotionVector = motionVector0.Value,
            CompoundMotionVector = motionVector1.Value
        };
        return true;
    }

    private static bool TryReadInterBlockMotionVector(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        out Vp9InterBlockModeInfoProbe resolvedModeBlock,
        out Vp9DecodeDiagnostic? diagnostic,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors)
    {
        resolvedModeBlock = modeBlock;
        var candidates = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
            header,
            modeBlock,
            Vp9InterPredictor.BuildSpatialMotionVectorCandidateSet(
                modeBlock,
                decodedModeBlocks,
                header.ReferenceFrameSignBiases,
                previousFrameMotionVectors));
        Vp9MotionVector motionVector;
        Vp9MotionVector? compoundMotionVector = null;
        IReadOnlyList<Vp9MotionVector> interSubMotionVectors = [];
        IReadOnlyList<Vp9MotionVector> compoundInterSubMotionVectors = [];
        var resolvedModeInfo = modeBlock.ModeInfo;
        if (modeBlock.ModeInfo.CompoundReferenceFrame.HasValue)
        {
            Vp9MotionVector decodedCompoundMotionVector;
            if (!TryReadCompoundInterBlockMotionVectors(
                    ref reader,
                    header,
                    compressedHeader,
                    modeBlock,
                    decodedModeBlocks,
                    previousFrameMotionVectors,
                    out resolvedModeInfo,
                    out interSubMotionVectors,
                    out compoundInterSubMotionVectors,
                    out motionVector,
                    out decodedCompoundMotionVector,
                    out diagnostic))
            {
                return false;
            }

            compoundMotionVector = decodedCompoundMotionVector;
        }
        else if (modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8)
        {
            if (!TryReadSub8X8InterBlockMotionVectors(
                    ref reader,
                    header,
                    compressedHeader,
                    modeBlock,
                    decodedModeBlocks,
                    candidates,
                    previousFrameMotionVectors,
                    out resolvedModeInfo,
                    out interSubMotionVectors,
                    out motionVector,
                    out diagnostic))
            {
                return false;
            }
        }
        else if (modeBlock.ModeInfo.PredictionMode == Vp9InterPredictionMode.NewMv)
        {
            var referenceMotionVector = Vp9MotionVectorSyntax.LowerPrecision(
                candidates.Count >= 1 ? candidates[0] : new Vp9MotionVector(0, 0),
                header.AllowHighPrecisionMv);
            motionVector = Vp9MotionVectorSyntax.ReadMotionVector(
                ref reader,
                compressedHeader.FrameContext.MotionVectorProbabilities,
                referenceMotionVector,
                header.AllowHighPrecisionMv);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 NEWMV motion vector ended unexpectedly.");
                return false;
            }
        }
        else if (!Vp9InterPredictor.TrySelectMotionVector(
                     modeBlock,
                     candidates,
                     out motionVector,
                     out diagnostic))
        {
            return false;
        }
        else if (modeBlock.ModeInfo.PredictionMode != Vp9InterPredictionMode.ZeroMv)
        {
            motionVector = Vp9MotionVectorSyntax.LowerPrecision(
                motionVector,
                header.AllowHighPrecisionMv);
        }

        if (!TryValidateSub8X8InterMotionVector(modeBlock, motionVector, out diagnostic))
        {
            return false;
        }

        if (!Vp9InterPredictor.IsValidMotionVector(motionVector))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 inter mode-info decoded an out-of-range motion vector.");
            return false;
        }

        resolvedModeBlock = modeBlock with
        {
            ModeInfo = resolvedModeInfo,
            MotionVector = motionVector,
            CompoundMotionVector = compoundMotionVector,
            InterSubMotionVectors = interSubMotionVectors,
            CompoundInterSubMotionVectors = compoundInterSubMotionVectors
        };

        diagnostic = null;
        return true;
    }

    private static bool TryReadCompoundInterBlockMotionVectors(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors,
        out Vp9InterModeInfoProbe resolvedModeInfo,
        out IReadOnlyList<Vp9MotionVector> interSubMotionVectors,
        out IReadOnlyList<Vp9MotionVector> compoundInterSubMotionVectors,
        out Vp9MotionVector motionVector0,
        out Vp9MotionVector motionVector1,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        resolvedModeInfo = modeBlock.ModeInfo;
        interSubMotionVectors = [];
        compoundInterSubMotionVectors = [];
        motionVector0 = default;
        motionVector1 = default;
        diagnostic = null;
        if (modeBlock.ModeInfo.CompoundReferenceFrame is not { } compoundReferenceFrame)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 compound motion-vector reader received a single-reference block.");
            return false;
        }

        if (modeBlock.ModeInfo.BlockSize < Vp9BlockSize.Block8X8)
        {
            return TryReadCompoundSub8X8InterBlockMotionVectors(
                ref reader,
                header,
                compressedHeader,
                modeBlock,
                decodedModeBlocks,
                previousFrameMotionVectors,
                compoundReferenceFrame,
                out resolvedModeInfo,
                out interSubMotionVectors,
                out compoundInterSubMotionVectors,
                out motionVector0,
                out motionVector1,
                out diagnostic);
        }

        var referenceBlock0 = CreateSingleReferenceView(modeBlock, modeBlock.ModeInfo.ReferenceFrame);
        var referenceBlock1 = CreateSingleReferenceView(modeBlock, compoundReferenceFrame);
        var candidates0 = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
            header,
            referenceBlock0,
            Vp9InterPredictor.BuildSpatialMotionVectorCandidateSet(
                referenceBlock0,
                decodedModeBlocks,
                header.ReferenceFrameSignBiases,
                previousFrameMotionVectors));
        var candidates1 = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
            header,
            referenceBlock1,
            Vp9InterPredictor.BuildSpatialMotionVectorCandidateSet(
                referenceBlock1,
                decodedModeBlocks,
                header.ReferenceFrameSignBiases,
                previousFrameMotionVectors));
        if (modeBlock.ModeInfo.PredictionMode == Vp9InterPredictionMode.NewMv)
        {
            var referenceMotionVector0 = Vp9MotionVectorSyntax.LowerPrecision(
                candidates0.Count >= 1 ? candidates0[0] : new Vp9MotionVector(0, 0),
                header.AllowHighPrecisionMv);
            motionVector0 = Vp9MotionVectorSyntax.ReadMotionVector(
                ref reader,
                compressedHeader.FrameContext.MotionVectorProbabilities,
                referenceMotionVector0,
                header.AllowHighPrecisionMv);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compound NEWMV first motion vector ended unexpectedly.");
                return false;
            }

            var referenceMotionVector1 = Vp9MotionVectorSyntax.LowerPrecision(
                candidates1.Count >= 1 ? candidates1[0] : new Vp9MotionVector(0, 0),
                header.AllowHighPrecisionMv);
            motionVector1 = Vp9MotionVectorSyntax.ReadMotionVector(
                ref reader,
                compressedHeader.FrameContext.MotionVectorProbabilities,
                referenceMotionVector1,
                header.AllowHighPrecisionMv);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compound NEWMV second motion vector ended unexpectedly.");
                return false;
            }
        }
        else
        {
            if (!Vp9InterPredictor.TrySelectMotionVector(
                    modeBlock.ModeInfo.PredictionMode,
                    candidates0,
                    out motionVector0,
                    out diagnostic) ||
                !Vp9InterPredictor.TrySelectMotionVector(
                    modeBlock.ModeInfo.PredictionMode,
                    candidates1,
                    out motionVector1,
                    out diagnostic))
            {
                return false;
            }

            if (modeBlock.ModeInfo.PredictionMode != Vp9InterPredictionMode.ZeroMv)
            {
                motionVector0 = Vp9MotionVectorSyntax.LowerPrecision(motionVector0, header.AllowHighPrecisionMv);
                motionVector1 = Vp9MotionVectorSyntax.LowerPrecision(motionVector1, header.AllowHighPrecisionMv);
            }
        }

        if (!Vp9InterPredictor.IsValidMotionVector(motionVector0) ||
            !Vp9InterPredictor.IsValidMotionVector(motionVector1))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound inter mode-info decoded an out-of-range motion vector.");
            return false;
        }

        return true;
    }

    private static bool TryReadCompoundSub8X8InterBlockMotionVectors(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors,
        Vp9InterReferenceFrame compoundReferenceFrame,
        out Vp9InterModeInfoProbe resolvedModeInfo,
        out IReadOnlyList<Vp9MotionVector> interSubMotionVectors,
        out IReadOnlyList<Vp9MotionVector> compoundInterSubMotionVectors,
        out Vp9MotionVector motionVector0,
        out Vp9MotionVector motionVector1,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        resolvedModeInfo = modeBlock.ModeInfo;
        interSubMotionVectors = [];
        compoundInterSubMotionVectors = [];
        motionVector0 = default;
        motionVector1 = default;
        diagnostic = null;

        var referenceBlock0 = CreateSingleReferenceView(modeBlock, modeBlock.ModeInfo.ReferenceFrame);
        var referenceBlock1 = CreateSingleReferenceView(modeBlock, compoundReferenceFrame);
        var candidates0 = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
            header,
            referenceBlock0,
            Vp9InterPredictor.BuildSpatialMotionVectorCandidateSet(
                referenceBlock0,
                decodedModeBlocks,
                header.ReferenceFrameSignBiases,
                previousFrameMotionVectors));
        var candidates1 = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
            header,
            referenceBlock1,
            Vp9InterPredictor.BuildSpatialMotionVectorCandidateSet(
                referenceBlock1,
                decodedModeBlocks,
                header.ReferenceFrameSignBiases,
                previousFrameMotionVectors));
        var newMvReferenceMotionVector0 = Vp9MotionVectorSyntax.LowerPrecision(
            candidates0.Count >= 1 ? candidates0[0] : new Vp9MotionVector(0, 0),
            header.AllowHighPrecisionMv);
        var newMvReferenceMotionVector1 = Vp9MotionVectorSyntax.LowerPrecision(
            candidates1.Count >= 1 ? candidates1[0] : new Vp9MotionVector(0, 0),
            header.AllowHighPrecisionMv);
        var subModes = new Vp9InterPredictionMode[4];
        var subMotionVectors0 = new Vp9MotionVector[4];
        var subMotionVectors1 = new Vp9MotionVector[4];

        switch (modeBlock.ModeInfo.BlockSize)
        {
            case Vp9BlockSize.Block4X4:
                for (var block = 0; block < 4; block++)
                {
                    if (!TryReadCompoundSub8X8PrimaryMotionVector(
                            ref reader,
                            compressedHeader,
                            header,
                            referenceBlock0,
                            referenceBlock1,
                            decodedModeBlocks,
                            previousFrameMotionVectors,
                            newMvReferenceMotionVector0,
                            newMvReferenceMotionVector1,
                            block,
                            subModes,
                            subMotionVectors0,
                            subMotionVectors1,
                            out diagnostic))
                    {
                        return false;
                    }
                }

                break;

            case Vp9BlockSize.Block4X8:
                if (!TryReadCompoundSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        referenceBlock0,
                        referenceBlock1,
                        decodedModeBlocks,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector0,
                        newMvReferenceMotionVector1,
                        0,
                        subModes,
                        subMotionVectors0,
                        subMotionVectors1,
                        out diagnostic))
                {
                    return false;
                }

                subModes[2] = subModes[0];
                subMotionVectors0[2] = subMotionVectors0[0];
                subMotionVectors1[2] = subMotionVectors1[0];
                if (!TryReadCompoundSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        referenceBlock0,
                        referenceBlock1,
                        decodedModeBlocks,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector0,
                        newMvReferenceMotionVector1,
                        1,
                        subModes,
                        subMotionVectors0,
                        subMotionVectors1,
                        out diagnostic))
                {
                    return false;
                }

                subModes[3] = subModes[1];
                subMotionVectors0[3] = subMotionVectors0[1];
                subMotionVectors1[3] = subMotionVectors1[1];
                break;

            case Vp9BlockSize.Block8X4:
                if (!TryReadCompoundSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        referenceBlock0,
                        referenceBlock1,
                        decodedModeBlocks,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector0,
                        newMvReferenceMotionVector1,
                        0,
                        subModes,
                        subMotionVectors0,
                        subMotionVectors1,
                        out diagnostic))
                {
                    return false;
                }

                subModes[1] = subModes[0];
                subMotionVectors0[1] = subMotionVectors0[0];
                subMotionVectors1[1] = subMotionVectors1[0];
                if (!TryReadCompoundSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        referenceBlock0,
                        referenceBlock1,
                        decodedModeBlocks,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector0,
                        newMvReferenceMotionVector1,
                        2,
                        subModes,
                        subMotionVectors0,
                        subMotionVectors1,
                        out diagnostic))
                {
                    return false;
                }

                subModes[3] = subModes[2];
                subMotionVectors0[3] = subMotionVectors0[2];
                subMotionVectors1[3] = subMotionVectors1[2];
                break;

            default:
                diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                    "VP9 compound sub-8x8 MV reader received a non-sub-8x8 block size.");
                return false;
        }

        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 compound sub-8x8 inter mode-info ended unexpectedly.");
            return false;
        }

        resolvedModeInfo = modeBlock.ModeInfo with
        {
            PredictionMode = subModes[3],
            InterSubModes = subModes
        };
        interSubMotionVectors = subMotionVectors0;
        compoundInterSubMotionVectors = subMotionVectors1;
        motionVector0 = subMotionVectors0[3];
        motionVector1 = subMotionVectors1[3];
        return true;
    }

    private static bool TryReadCompoundSub8X8PrimaryMotionVector(
        ref Vp9BoolReader reader,
        Vp9CompressedHeader compressedHeader,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe referenceBlock0,
        Vp9InterBlockModeInfoProbe referenceBlock1,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors,
        Vp9MotionVector newMvReferenceMotionVector0,
        Vp9MotionVector newMvReferenceMotionVector1,
        int blockIndex,
        Vp9InterPredictionMode[] subModes,
        Vp9MotionVector[] subMotionVectors0,
        Vp9MotionVector[] subMotionVectors1,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var predictionMode = Vp9InterModeInfoSyntax.ReadInterPredictionMode(
            ref reader,
            compressedHeader.FrameContext,
            referenceBlock0.ModeInfo.InterModeContext);
        subModes[blockIndex] = predictionMode;
        Vp9MotionVector motionVector0;
        Vp9MotionVector motionVector1;
        if (predictionMode == Vp9InterPredictionMode.NewMv)
        {
            motionVector0 = Vp9MotionVectorSyntax.ReadMotionVector(
                ref reader,
                compressedHeader.FrameContext.MotionVectorProbabilities,
                newMvReferenceMotionVector0,
                header.AllowHighPrecisionMv);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                    "VP9 compound sub-8x8 first NEWMV motion vector ended unexpectedly.");
                return false;
            }

            motionVector1 = Vp9MotionVectorSyntax.ReadMotionVector(
                ref reader,
                compressedHeader.FrameContext.MotionVectorProbabilities,
                newMvReferenceMotionVector1,
                header.AllowHighPrecisionMv);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                    "VP9 compound sub-8x8 second NEWMV motion vector ended unexpectedly.");
                return false;
            }
        }
        else
        {
            var subBlockCandidates0 = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
                header,
                referenceBlock0,
                Vp9InterPredictor.BuildSub8X8MotionVectorCandidateSet(
                    referenceBlock0,
                    decodedModeBlocks,
                    blockIndex,
                    header.ReferenceFrameSignBiases,
                    previousFrameMotionVectors));
            var subBlockCandidates1 = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
                header,
                referenceBlock1,
                Vp9InterPredictor.BuildSub8X8MotionVectorCandidateSet(
                    referenceBlock1,
                    decodedModeBlocks,
                    blockIndex,
                    header.ReferenceFrameSignBiases,
                    previousFrameMotionVectors));
            if (!Vp9InterPredictor.TrySelectSub8X8MotionVector(
                    predictionMode,
                    blockIndex,
                    subBlockCandidates0,
                    subMotionVectors0,
                    out motionVector0,
                    out diagnostic) ||
                !Vp9InterPredictor.TrySelectSub8X8MotionVector(
                    predictionMode,
                    blockIndex,
                    subBlockCandidates1,
                    subMotionVectors1,
                    out motionVector1,
                    out diagnostic))
            {
                return false;
            }
        }

        if (!Vp9InterPredictor.IsValidMotionVector(motionVector0) ||
            !Vp9InterPredictor.IsValidMotionVector(motionVector1))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 compound sub-8x8 mode-info decoded an out-of-range motion vector.");
            return false;
        }

        subMotionVectors0[blockIndex] = motionVector0;
        subMotionVectors1[blockIndex] = motionVector1;
        diagnostic = null;
        return true;
    }

    private static Vp9InterBlockModeInfoProbe CreateSingleReferenceView(
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9InterReferenceFrame referenceFrame)
    {
        return modeBlock with
        {
            ModeInfo = modeBlock.ModeInfo with
            {
                ReferenceMode = Vp9ReferenceMode.Single,
                ReferenceFrame = referenceFrame,
                CompoundReferenceFrame = null,
                CompoundInterContext = null,
                CompoundReferenceContext = null
            },
            MotionVector = null,
            CompoundMotionVector = null,
            InterSubMotionVectors = [],
            CompoundInterSubMotionVectors = []
        };
    }

    private static bool TryReadSub8X8InterBlockMotionVectors(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        Vp9MotionVectorCandidateSet candidates,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors,
        out Vp9InterModeInfoProbe resolvedModeInfo,
        out IReadOnlyList<Vp9MotionVector> interSubMotionVectors,
        out Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        resolvedModeInfo = modeBlock.ModeInfo;
        interSubMotionVectors = [];
        motionVector = default;
        diagnostic = null;

        var subModes = new Vp9InterPredictionMode[4];
        var subMotionVectors = new Vp9MotionVector[4];
        var newMvReferenceMotionVector = Vp9MotionVectorSyntax.LowerPrecision(
            candidates.Count >= 1 ? candidates[0] : new Vp9MotionVector(0, 0),
            header.AllowHighPrecisionMv);
        switch (modeBlock.ModeInfo.BlockSize)
        {
            case Vp9BlockSize.Block4X4:
                for (var block = 0; block < 4; block++)
                {
                    if (!TryReadSub8X8PrimaryMotionVector(
                            ref reader,
                            compressedHeader,
                            header,
                            modeBlock,
                            decodedModeBlocks,
                            candidates,
                            previousFrameMotionVectors,
                            newMvReferenceMotionVector,
                            block,
                            subModes,
                            subMotionVectors,
                            out diagnostic))
                    {
                        return false;
                    }
                }

                break;

            case Vp9BlockSize.Block4X8:
                if (!TryReadSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        modeBlock,
                        decodedModeBlocks,
                        candidates,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector,
                        0,
                        subModes,
                        subMotionVectors,
                        out diagnostic))
                {
                    return false;
                }

                subModes[2] = subModes[0];
                subMotionVectors[2] = subMotionVectors[0];
                if (!TryReadSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        modeBlock,
                        decodedModeBlocks,
                        candidates,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector,
                        1,
                        subModes,
                        subMotionVectors,
                        out diagnostic))
                {
                    return false;
                }

                subModes[3] = subModes[1];
                subMotionVectors[3] = subMotionVectors[1];
                break;

            case Vp9BlockSize.Block8X4:
                if (!TryReadSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        modeBlock,
                        decodedModeBlocks,
                        candidates,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector,
                        0,
                        subModes,
                        subMotionVectors,
                        out diagnostic))
                {
                    return false;
                }

                subModes[1] = subModes[0];
                subMotionVectors[1] = subMotionVectors[0];
                if (!TryReadSub8X8PrimaryMotionVector(
                        ref reader,
                        compressedHeader,
                        header,
                        modeBlock,
                        decodedModeBlocks,
                        candidates,
                        previousFrameMotionVectors,
                        newMvReferenceMotionVector,
                        2,
                        subModes,
                        subMotionVectors,
                        out diagnostic))
                {
                    return false;
                }

                subModes[3] = subModes[2];
                subMotionVectors[3] = subMotionVectors[2];
                break;

            default:
                diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                    "VP9 sub-8x8 MV reader received a non-sub-8x8 block size.");
                return false;
        }

        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 sub-8x8 inter mode-info ended unexpectedly.");
            return false;
        }

        resolvedModeInfo = modeBlock.ModeInfo with
        {
            PredictionMode = subModes[3],
            InterSubModes = subModes
        };
        interSubMotionVectors = subMotionVectors;
        motionVector = subMotionVectors[3];
        return true;
    }

    private static bool TryReadSub8X8PrimaryMotionVector(
        ref Vp9BoolReader reader,
        Vp9CompressedHeader compressedHeader,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        Vp9MotionVectorCandidateSet candidates,
        Vp9PreviousFrameMotionVectors? previousFrameMotionVectors,
        Vp9MotionVector newMvReferenceMotionVector,
        int blockIndex,
        Vp9InterPredictionMode[] subModes,
        Vp9MotionVector[] subMotionVectors,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var predictionMode = Vp9InterModeInfoSyntax.ReadInterPredictionMode(
            ref reader,
            compressedHeader.FrameContext,
            modeBlock.ModeInfo.InterModeContext);
        subModes[blockIndex] = predictionMode;
        Vp9MotionVector motionVector;
        if (predictionMode == Vp9InterPredictionMode.NewMv)
        {
            motionVector = Vp9MotionVectorSyntax.ReadMotionVector(
                ref reader,
                compressedHeader.FrameContext.MotionVectorProbabilities,
                newMvReferenceMotionVector,
                header.AllowHighPrecisionMv);
            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket(
                    "VP9 sub-8x8 NEWMV motion vector ended unexpectedly.");
                return false;
            }
        }
        else
        {
            var subBlockCandidates = Vp9InterPredictor.ClampReferenceMotionVectorCandidates(
                header,
                modeBlock,
                Vp9InterPredictor.BuildSub8X8MotionVectorCandidateSet(
                    modeBlock,
                    decodedModeBlocks,
                    blockIndex,
                    header.ReferenceFrameSignBiases,
                    previousFrameMotionVectors));
            if (!Vp9InterPredictor.TrySelectSub8X8MotionVector(
                    predictionMode,
                    blockIndex,
                    subBlockCandidates,
                    subMotionVectors,
                    out motionVector,
                    out diagnostic))
            {
                return false;
            }
        }

        if (!Vp9InterPredictor.IsValidMotionVector(motionVector))
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                "VP9 sub-8x8 mode-info decoded an out-of-range motion vector.");
            return false;
        }

        subMotionVectors[blockIndex] = motionVector;

        diagnostic = null;
        return true;
    }

    private static bool TryValidateSub8X8InterMotionVector(
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9MotionVector motionVector,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (modeBlock.ModeInfo.BlockSize >= Vp9BlockSize.Block8X8)
        {
            return true;
        }

        if (modeBlock.ModeInfo.InterSubModes.Count > 0)
        {
            if (modeBlock.InterSubMotionVectors.Count > 0)
            {
                if (modeBlock.InterSubMotionVectors.Count != 4)
                {
                    diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                        "VP9 sub-8x8 inter mode-info requires four sub-block motion vectors.");
                    return false;
                }

                foreach (var subMotionVector in modeBlock.InterSubMotionVectors)
                {
                    if (!Vp9InterPredictor.IsValidMotionVector(subMotionVector))
                    {
                        diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                            "VP9 sub-8x8 inter mode-info contains an out-of-range motion vector.");
                        return false;
                    }
                }

                if (modeBlock.CompoundInterSubMotionVectors.Count > 0)
                {
                    if (modeBlock.CompoundInterSubMotionVectors.Count != 4)
                    {
                        diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                            "VP9 compound sub-8x8 inter mode-info requires four compound sub-block motion vectors.");
                        return false;
                    }

                    foreach (var subMotionVector in modeBlock.CompoundInterSubMotionVectors)
                    {
                        if (!Vp9InterPredictor.IsValidMotionVector(subMotionVector))
                        {
                            diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
                                "VP9 compound sub-8x8 inter mode-info contains an out-of-range motion vector.");
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        if (Vp9InterPredictor.IsValidMotionVector(motionVector))
        {
            return true;
        }

        diagnostic = Vp9DecodeDiagnostic.InvalidPacket(
            "VP9 sub-8x8 inter mode-info decoded an out-of-range motion vector.");
        return false;
    }

    private static void ReadPartitionSyntax(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry,
        Vp9KeyFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext coefficientContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        List<Vp9ModeInfoProbe> modes,
        List<Vp9CoefficientBlockGroupProbe> coefficientGroups)
    {
        if (miRow >= state.Header.TileInfo.MiRows || miColumn >= state.Header.TileInfo.MiColumns)
        {
            return;
        }

        var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
        var hasRows = miRow + hbs < state.Header.TileInfo.MiRows;
        var hasColumns = miColumn + hbs < state.Header.TileInfo.MiColumns;
        var partition = Vp9PartitionSyntax.ReadPartition(
            ref reader,
            syntaxContext.GetPartitionContext(miRow, miColumn, blockSize),
            hasRows,
            hasColumns);
        var subsize = Vp9ModeInfoSyntax.GetSubsize(blockSize, partition);
        var childPath = partitionPath.Concat([partition]).ToArray();
        if (hbs == 0)
        {
            ReadBlockSyntax(
                ref reader,
                state,
                geometry,
                syntaxContext,
                coefficientContext,
                miRow,
                miColumn,
                subsize,
                childPath,
                modes,
                coefficientGroups);
            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
            return;
        }

        switch (partition)
        {
            case Vp9PartitionType.None:
                ReadBlockSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                break;

            case Vp9PartitionType.Horizontal:
                ReadBlockSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                if (hasRows)
                {
                    ReadBlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow + hbs,
                        miColumn,
                        subsize,
                        childPath,
                        modes,
                        coefficientGroups);
                }

                break;

            case Vp9PartitionType.Vertical:
                ReadBlockSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                if (hasColumns)
                {
                    ReadBlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn + hbs,
                        subsize,
                        childPath,
                        modes,
                        coefficientGroups);
                }

                break;

            case Vp9PartitionType.Split:
                ReadPartitionSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                ReadPartitionSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow,
                    miColumn + hbs,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                ReadPartitionSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow + hbs,
                    miColumn,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                ReadPartitionSyntax(
                    ref reader,
                    state,
                    geometry,
                    syntaxContext,
                    coefficientContext,
                    miRow + hbs,
                    miColumn + hbs,
                    subsize,
                    childPath,
                    modes,
                    coefficientGroups);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(partition), partition, "Unsupported VP9 partition type.");
        }

        if (blockSize >= Vp9BlockSize.Block8X8 &&
            (blockSize == Vp9BlockSize.Block8X8 || partition != Vp9PartitionType.Split))
        {
            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
        }
    }

    private static void ReadBlockSyntax(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry,
        Vp9KeyFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext coefficientContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        List<Vp9ModeInfoProbe> modes,
        List<Vp9CoefficientBlockGroupProbe> coefficientGroups)
    {
        if (blockSize is not (
            Vp9BlockSize.Block4X4 or
            Vp9BlockSize.Block4X8 or
            Vp9BlockSize.Block8X4 or
            Vp9BlockSize.Block8X8 or
            Vp9BlockSize.Block8X16 or
            Vp9BlockSize.Block16X8 or
            Vp9BlockSize.Block16X16 or
            Vp9BlockSize.Block16X32 or
            Vp9BlockSize.Block32X16 or
            Vp9BlockSize.Block32X32 or
            Vp9BlockSize.Block32X64 or
            Vp9BlockSize.Block64X32 or
            Vp9BlockSize.Block64X64))
        {
            throw new NotSupportedException(
                $"VP9 key-frame syntax probe does not support leaf block size {blockSize}.");
        }

        var modeInfo = ReadModeInfoAfterPartition(
            ref reader,
            state,
            geometry,
            syntaxContext,
            miRow,
            miColumn,
            blockSize,
            partitionPath);
        modes.Add(modeInfo);

        for (var plane = 0; plane < 3; plane++)
        {
            coefficientGroups.Add(Vp9ResidualSyntax.ReadPlaneCoefficientBlocks(
                ref reader,
                state,
                modeInfo,
                coefficientContext,
                plane));
        }
    }

    private static string CreateFullFrameSyntaxTruncatedMessage(
        int tileIndex,
        int miRow,
        int miColumn,
        IReadOnlyList<Vp9ModeInfoProbe> modes,
        IReadOnlyList<Vp9CoefficientBlockGroupProbe> coefficientGroups)
    {
        var message = $"VP9 full-frame syntax probe ended unexpectedly at tile {tileIndex} MI ({miRow},{miColumn}); parsed {modes.Count} mode infos and {coefficientGroups.Count} coefficient groups in this superblock";
        if (modes.Count > 0)
        {
            var lastMode = modes[^1];
            message += $"; last mode MI ({lastMode.MiRow},{lastMode.MiColumn}) block {lastMode.BlockSize} transform {lastMode.TransformSize} skip {lastMode.Skip} Y {lastMode.YMode} UV {lastMode.UvMode}";
        }

        if (coefficientGroups.Count > 0)
        {
            var lastGroup = coefficientGroups[^1];
            message += $"; last coefficient group block {lastGroup.BlockSize} transform {lastGroup.TransformSize} blocks {lastGroup.Blocks.Count}";
        }

        return message + ".";
    }

    private static string CreateInterFullFrameSyntaxTruncatedMessage(
        int tileIndex,
        int miRow,
        int miColumn,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> modes,
        IReadOnlyList<Vp9CoefficientBlockGroupProbe> coefficientGroups)
    {
        var message = $"VP9 full inter residual probe ended unexpectedly at tile {tileIndex} MI ({miRow},{miColumn}); parsed {modes.Count} inter mode infos and {coefficientGroups.Count} coefficient groups in this superblock";
        if (modes.Count > 0)
        {
            var lastMode = modes[^1];
            message += $"; last inter mode MI ({lastMode.MiRow},{lastMode.MiColumn}) block {lastMode.ModeInfo.BlockSize} transform {lastMode.ModeInfo.TransformSize} skip {lastMode.ModeInfo.Skip} ref {lastMode.ModeInfo.ReferenceFrame} prediction {lastMode.ModeInfo.PredictionMode}";
        }

        if (coefficientGroups.Count > 0)
        {
            var lastGroup = coefficientGroups[^1];
            message += $"; last coefficient group block {lastGroup.BlockSize} transform {lastGroup.TransformSize} blocks {lastGroup.Blocks.Count}";
        }

        return message + ".";
    }

    private static bool TryReadFirstBlock16X16LumaTx4Group(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry,
        Vp9KeyFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext coefficientContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        out Vp9ModeInfoProbe? modeInfo,
        out Vp9CoefficientBlockGroupProbe? coefficientGroup)
    {
        modeInfo = null;
        coefficientGroup = null;

        if (miRow >= state.Header.TileInfo.MiRows || miColumn >= state.Header.TileInfo.MiColumns)
        {
            return false;
        }

        var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
        var hasRows = miRow + hbs < state.Header.TileInfo.MiRows;
        var hasColumns = miColumn + hbs < state.Header.TileInfo.MiColumns;
        var partition = Vp9PartitionSyntax.ReadPartition(
            ref reader,
            syntaxContext.GetPartitionContext(miRow, miColumn, blockSize),
            hasRows,
            hasColumns);
        var subsize = Vp9ModeInfoSyntax.GetSubsize(blockSize, partition);
        var childPath = partitionPath.Concat([partition]).ToArray();
        if (hbs == 0)
        {
            var found = TryReadFirstBlock16X16LumaTx4BlockSyntax(
                ref reader,
                state,
                geometry,
                syntaxContext,
                coefficientContext,
                miRow,
                miColumn,
                subsize,
                childPath,
                out modeInfo,
                out coefficientGroup);
            if (!found)
            {
                syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
            }

            return found;
        }

        switch (partition)
        {
            case Vp9PartitionType.None:
                if (TryReadFirstBlock16X16LumaTx4BlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup))
                {
                    return true;
                }

                break;

            case Vp9PartitionType.Horizontal:
                if (TryReadFirstBlock16X16LumaTx4BlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup))
                {
                    return true;
                }

                if (hasRows &&
                    TryReadFirstBlock16X16LumaTx4BlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow + hbs,
                        miColumn,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup))
                {
                    return true;
                }

                break;

            case Vp9PartitionType.Vertical:
                if (TryReadFirstBlock16X16LumaTx4BlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup))
                {
                    return true;
                }

                if (hasColumns &&
                    TryReadFirstBlock16X16LumaTx4BlockSyntax(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn + hbs,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup))
                {
                    return true;
                }

                break;

            case Vp9PartitionType.Split:
                if (TryReadFirstBlock16X16LumaTx4Group(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup) ||
                    TryReadFirstBlock16X16LumaTx4Group(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow,
                        miColumn + hbs,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup) ||
                    TryReadFirstBlock16X16LumaTx4Group(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow + hbs,
                        miColumn,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup) ||
                    TryReadFirstBlock16X16LumaTx4Group(
                        ref reader,
                        state,
                        geometry,
                        syntaxContext,
                        coefficientContext,
                        miRow + hbs,
                        miColumn + hbs,
                        subsize,
                        childPath,
                        out modeInfo,
                        out coefficientGroup))
                {
                    return true;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(partition), partition, "Unsupported VP9 partition type.");
        }

        if (blockSize >= Vp9BlockSize.Block8X8 &&
            (blockSize == Vp9BlockSize.Block8X8 || partition != Vp9PartitionType.Split))
        {
            syntaxContext.UpdatePartitionContext(miRow, miColumn, blockSize, subsize);
        }

        return false;
    }

    private static bool TryReadFirstBlock16X16LumaTx4BlockSyntax(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry,
        Vp9KeyFrameSyntaxContext syntaxContext,
        Vp9CoefficientEntropyContext coefficientContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath,
        out Vp9ModeInfoProbe modeInfo,
        out Vp9CoefficientBlockGroupProbe? coefficientGroup)
    {
        modeInfo = ReadModeInfoAfterPartition(
            ref reader,
            state,
            geometry,
            syntaxContext,
            miRow,
            miColumn,
            blockSize,
            partitionPath);

        if (!modeInfo.Skip &&
            modeInfo.BlockSize == Vp9BlockSize.Block16X16 &&
            modeInfo.TransformSize == Vp9TransformSize.Tx4X4)
        {
            coefficientGroup = Vp9ResidualSyntax.ReadPlaneCoefficientBlocks(
                ref reader,
                state,
                modeInfo,
                coefficientContext,
                plane: 0);
            return true;
        }

        for (var plane = 0; plane < 3; plane++)
        {
            _ = Vp9ResidualSyntax.ReadPlaneCoefficientBlocks(
                ref reader,
                state,
                modeInfo,
                coefficientContext,
                plane);
        }

        coefficientGroup = null;
        return false;
    }

    private static Vp9ModeInfoProbe ReadFirstLeafModeInfo(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry)
    {
        var miRow = geometry.MiRowStart;
        var miColumn = geometry.MiColumnStart;
        var blockSize = Vp9BlockSize.Block64X64;
        var partitionPath = new List<Vp9PartitionType>();
        var syntaxContext = Vp9KeyFrameSyntaxContext.Create(state.Header);

        while (true)
        {
            var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
            var hasRows = miRow + hbs < state.Header.TileInfo.MiRows;
            var hasColumns = miColumn + hbs < state.Header.TileInfo.MiColumns;
            var context = syntaxContext.GetPartitionContext(miRow, miColumn, blockSize);
            var partition = Vp9PartitionSyntax.ReadPartition(ref reader, context, hasRows, hasColumns);
            partitionPath.Add(partition);
            blockSize = Vp9ModeInfoSyntax.GetSubsize(blockSize, partition);

            if (partition != Vp9PartitionType.Split)
            {
                break;
            }

            if (blockSize < Vp9BlockSize.Block8X8)
            {
                throw new NotSupportedException("VP9 sub-8x8 key-frame mode info is not supported yet.");
            }
        }

        if (blockSize < Vp9BlockSize.Block8X8)
        {
            throw new NotSupportedException("VP9 sub-8x8 key-frame mode info is not supported yet.");
        }

        return ReadModeInfoAfterPartition(
            ref reader,
            state,
            geometry,
            syntaxContext,
            miRow,
            miColumn,
            blockSize,
            partitionPath);
    }

    private static Vp9ModeInfoProbe ReadModeInfoAfterPartition(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry,
        Vp9KeyFrameSyntaxContext syntaxContext,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        IReadOnlyList<Vp9PartitionType> partitionPath)
    {
        var skipContext = syntaxContext.GetSkipContext(miRow, miColumn, geometry.MiColumnStart);
        var skip = Vp9ModeInfoSyntax.ReadSkip(ref reader, state.CompressedHeader.FrameContext, skipContext);
        var transformSizeContext = syntaxContext.GetTransformSizeContext(
            miRow,
            miColumn,
            geometry.MiColumnStart,
            blockSize);
        var transformSize = Vp9ModeInfoSyntax.ReadTransformSize(
            ref reader,
            state.CompressedHeader,
            blockSize,
            transformSizeContext);
        var ySubModes = ReadYSubModes(
            ref reader,
            syntaxContext,
            miRow,
            miColumn,
            geometry.MiColumnStart,
            blockSize,
            out var yMode);
        var uvMode = Vp9ModeInfoSyntax.ReadUvMode(ref reader, yMode);
        syntaxContext.SetModeInfo(miRow, miColumn, blockSize, skip, transformSize, yMode, ySubModes);

        return new Vp9ModeInfoProbe(
            geometry.Buffer.Index,
            miRow,
            miColumn,
            blockSize,
            partitionPath,
            skip,
            skipContext,
            transformSize,
            transformSizeContext,
            yMode,
            uvMode,
            ySubModes);
    }

    private static IReadOnlyList<Vp9PredictionMode> ReadYSubModes(
        ref Vp9BoolReader reader,
        Vp9KeyFrameSyntaxContext syntaxContext,
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        Vp9BlockSize blockSize,
        out Vp9PredictionMode yMode)
    {
        var subModes = new Vp9PredictionMode[4];
        switch (blockSize)
        {
            case Vp9BlockSize.Block4X4:
                for (var block = 0; block < subModes.Length; block++)
                {
                    subModes[block] = ReadYSubMode(ref reader, syntaxContext, miRow, miColumn, tileMiColumnStart, block, subModes);
                }

                yMode = subModes[3];
                return subModes;

            case Vp9BlockSize.Block4X8:
                subModes[0] = subModes[2] = ReadYSubMode(ref reader, syntaxContext, miRow, miColumn, tileMiColumnStart, 0, subModes);
                subModes[1] = subModes[3] = ReadYSubMode(ref reader, syntaxContext, miRow, miColumn, tileMiColumnStart, 1, subModes);
                yMode = subModes[3];
                return subModes;

            case Vp9BlockSize.Block8X4:
                subModes[0] = subModes[1] = ReadYSubMode(ref reader, syntaxContext, miRow, miColumn, tileMiColumnStart, 0, subModes);
                subModes[2] = subModes[3] = ReadYSubMode(ref reader, syntaxContext, miRow, miColumn, tileMiColumnStart, 2, subModes);
                yMode = subModes[3];
                return subModes;

            default:
                var yModeContext = syntaxContext.GetYModeContext(miRow, miColumn, tileMiColumnStart);
                yMode = Vp9ModeInfoSyntax.ReadYMode(ref reader, yModeContext.Above, yModeContext.Left);
                Array.Fill(subModes, yMode);
                return subModes;
        }
    }

    private static Vp9PredictionMode ReadYSubMode(
        ref Vp9BoolReader reader,
        Vp9KeyFrameSyntaxContext syntaxContext,
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        int block,
        IReadOnlyList<Vp9PredictionMode> currentSubModes)
    {
        var context = syntaxContext.GetYSubModeContext(miRow, miColumn, tileMiColumnStart, block, currentSubModes);
        return Vp9ModeInfoSyntax.ReadYMode(ref reader, context.Above, context.Left);
    }

    private static byte[] ReadAboveEdge(ReadOnlySpan<byte> plane, int stride, int x, int y, int size)
    {
        var above = new byte[size];
        plane.Slice(((y - 1) * stride) + x, size).CopyTo(above);
        return above;
    }

    private static byte[] ReadLeftEdge(ReadOnlySpan<byte> plane, int stride, int x, int y, int size)
    {
        var left = new byte[size];
        for (var row = 0; row < size; row++)
        {
            left[row] = plane[((y + row) * stride) + x - 1];
        }

        return left;
    }

    private static int GetFirstLeafTx32GridSize(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block32X32 => 1,
            Vp9BlockSize.Block64X64 => 2,
            _ => throw new NotSupportedException(
                $"VP9 first-leaf Y DC reconstruction probe does not support block size {blockSize}.")
        };
    }

    private sealed class Vp9DirectInterResidualScratch
    {
        public List<Vp9CoefficientBlockGroupProbe> Groups { get; } = new(3);
    }
}
