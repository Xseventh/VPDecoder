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
    Vp9MotionVector? MotionVector = null);

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
        out Vp9DecodeDiagnostic? diagnostic)
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
                        out diagnostic))
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
        out Vp9DecodeDiagnostic? diagnostic)
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
                if (!TryReadInterSuperblockResidualSyntax(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        geometry,
                        syntaxContext,
                        entropyContext,
                        geometry.MiRowStart,
                        geometry.MiColumnStart,
                        out var syntaxProbe,
                        out diagnostic))
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
        out Vp9DecodeDiagnostic? diagnostic)
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
                                miRow,
                                miColumn,
                                out var syntaxProbe,
                                out diagnostic))
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
        out Vp9DecodeDiagnostic? diagnostic)
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
                out diagnostic))
        {
            return false;
        }

        if (!TryReconstructInterFrameFromProbes(
                header,
                probes,
                referenceFrames,
                out reconstructedFrame,
                out var predictedProbes,
                out diagnostic))
        {
            return false;
        }

        probes = predictedProbes;
        return true;
    }

    public static bool TryReconstructInterFrameFromProbes(
        Vp9FrameHeader header,
        IReadOnlyList<Vp9InterSuperblockSyntaxProbe> syntaxProbes,
        Vp9ReferenceFrameStore referenceFrames,
        out Vp9ReconstructedFrame? reconstructedFrame,
        out IReadOnlyList<Vp9InterSuperblockSyntaxProbe> predictedProbes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        reconstructedFrame = null;
        predictedProbes = [];
        diagnostic = null;

        try
        {
            var destination = Vp9YuvFrameBuffer.Create(header.Width, header.Height);
            var reconstructedProbes = new List<Vp9InterSuperblockSyntaxProbe>(syntaxProbes.Count);
            var predictedModeBlocks = new List<Vp9InterBlockModeInfoProbe>();
            foreach (var probe in syntaxProbes)
            {
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

                    var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
                        modeBlock,
                        predictedModeBlocks);
                    if (!Vp9InterPredictor.TrySelectMotionVector(
                            modeBlock,
                            candidates,
                            out var motionVector,
                            out diagnostic))
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

                    var predictedModeBlock = modeBlock with
                    {
                        MotionVector = motionVector
                    };
                    predictedModeBlocks.Add(predictedModeBlock);
                    predictedProbeModeBlocks.Add(predictedModeBlock);

                    var groupOffset = modeIndex * 3;
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
            reconstructedFrame = Vp9ReconstructedFrame.FromInter(
                destination.ToDecodedFrame(),
                reconstructedProbes,
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
        out Vp9DecodeDiagnostic? diagnostic)
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
                    out diagnostic))
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
                        out diagnostic))
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
                        out diagnostic))
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
                            out diagnostic))
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
                        out diagnostic))
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
                            out diagnostic))
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
                        out diagnostic))
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
                        out diagnostic))
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
                        out diagnostic))
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
                            out diagnostic))
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
        out Vp9DecodeDiagnostic? diagnostic)
    {
        var singleReferenceContexts = syntaxContext.GetSingleReferenceContexts(
            miRow,
            miColumn,
            geometry.MiColumnStart);
        var contexts = new Vp9InterModeInfoContexts(
            Skip: syntaxContext.GetSkipContext(miRow, miColumn, geometry.MiColumnStart),
            IntraInter: syntaxContext.GetIntraInterContext(miRow, miColumn, geometry.MiColumnStart),
            TransformSize: syntaxContext.GetTransformSizeContext(miRow, miColumn, geometry.MiColumnStart, blockSize),
            SingleReference0: singleReferenceContexts.Context0,
            SingleReference1: singleReferenceContexts.Context1,
            InterMode: syntaxContext.GetInterModeContext(
                miRow,
                miColumn,
                blockSize,
                geometry.MiColumnStart,
                geometry.MiColumnEnd));
        if (!Vp9InterModeInfoSyntax.TryReadSupportedInterBlock(
                ref reader,
                header,
                compressedHeader,
                blockSize,
                contexts,
                out var modeInfo,
                out diagnostic))
        {
            return false;
        }

        if (modeInfo is null)
        {
            diagnostic = Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 inter mode-info probe succeeded without returning a mode-info record.");
            return false;
        }

        var modeBlock = new Vp9InterBlockModeInfoProbe(
            geometry.Buffer.Index,
            miRow,
            miColumn,
            partitionPath,
            modeInfo);
        if (!TryReadInterBlockMotionVector(
                ref reader,
                header,
                compressedHeader,
                modeBlock,
                modes,
                out modeBlock,
                out diagnostic))
        {
            return false;
        }

        modes.Add(modeBlock);
        syntaxContext.SetModeInfo(miRow, miColumn, modeInfo);
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
        int miRow,
        int miColumn,
        out Vp9InterSuperblockSyntaxProbe? probe,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        probe = null;
        var partitions = new List<Vp9PartitionProbe>();
        var modes = new List<Vp9InterBlockModeInfoProbe>();
        if (!TryReadInterPartitionModeInfo(
                ref reader,
                header,
                compressedHeader,
                geometry,
                syntaxContext,
                miRow,
                miColumn,
                Vp9BlockSize.Block64X64,
                [],
                partitions,
                modes,
                out diagnostic))
        {
            return false;
        }

        var coefficientGroups = new List<Vp9CoefficientBlockGroupProbe>(checked(modes.Count * 3));
        foreach (var modeBlock in modes)
        {
            for (var plane = 0; plane < 3; plane++)
            {
                coefficientGroups.Add(
                    Vp9ResidualSyntax.ReadInterPlaneCoefficientBlocks(
                        ref reader,
                        header,
                        compressedHeader,
                        dequantTables,
                        modeBlock,
                        entropyContext,
                        plane));
            }
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

        if (!Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
                referenceFrame,
                destination,
                Vp9Plane.Y,
                destinationX,
                destinationY,
                visibleWidth,
                visibleHeight,
                motionVector,
                out diagnostic))
        {
            return false;
        }

        var chromaMotionVector = new Vp9MotionVector(motionVector.Row / 2, motionVector.Column / 2);
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

        if (!Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
                referenceFrame,
                destination,
                Vp9Plane.U,
                chromaDestinationX,
                chromaDestinationY,
                chromaWidth,
                chromaHeight,
                chromaMotionVector,
                out diagnostic))
        {
            return false;
        }

        return Vp9MotionCompensator.TryCopyWholePixelPlaneBlock(
            referenceFrame,
            destination,
            Vp9Plane.V,
            chromaDestinationX,
            chromaDestinationY,
            chromaWidth,
            chromaHeight,
            chromaMotionVector,
            out diagnostic);
    }

    private static bool TryPredictInterBlock(
        Vp9ReferenceFrameStore referenceFrames,
        Vp9FrameHeader header,
        Vp9YuvFrameBuffer destination,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> predictedModeBlocks,
        out Vp9InterBlockModeInfoProbe predictedModeBlock,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        predictedModeBlock = modeBlock;
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

        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
            modeBlock,
            predictedModeBlocks);
        if (!Vp9InterPredictor.TrySelectMotionVector(
                modeBlock,
                candidates,
                out var motionVector,
                out diagnostic))
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

    private static bool TryReadInterBlockMotionVector(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9InterBlockModeInfoProbe modeBlock,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> decodedModeBlocks,
        out Vp9InterBlockModeInfoProbe resolvedModeBlock,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        resolvedModeBlock = modeBlock;
        var candidates = Vp9InterPredictor.BuildSpatialMotionVectorCandidates(
            modeBlock,
            decodedModeBlocks);
        Vp9MotionVector motionVector;
        if (modeBlock.ModeInfo.PredictionMode == Vp9InterPredictionMode.NewMv)
        {
            if (candidates.Count < 1)
            {
                diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 NEWMV requires a same-reference spatial MV candidate; different-reference and previous-frame MV fallback are not supported yet.");
                return false;
            }

            var referenceMotionVector = Vp9MotionVectorSyntax.LowerPrecision(
                candidates[0],
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
                     modeBlock.ModeInfo.PredictionMode,
                     candidates,
                     out motionVector,
                     out diagnostic))
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
            MotionVector = motionVector
        };
        diagnostic = null;
        return true;
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
}
