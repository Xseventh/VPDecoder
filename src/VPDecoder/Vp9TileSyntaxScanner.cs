namespace VPDecoder;

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
                        if (!IsDcOnlyOrEmpty(coefficients))
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

    private static Vp9ModeInfoProbe ReadFirstLeafModeInfo(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9TileGeometry geometry)
    {
        var miRow = geometry.MiRowStart;
        var miColumn = geometry.MiColumnStart;
        var blockSize = Vp9BlockSize.Block64X64;
        var partitionPath = new List<Vp9PartitionType>();

        while (true)
        {
            var hbs = Vp9ModeInfoSyntax.GetHalfBlockSizeInMiUnits(blockSize);
            var hasRows = miRow + hbs < state.Header.TileInfo.MiRows;
            var hasColumns = miColumn + hbs < state.Header.TileInfo.MiColumns;
            var context = Vp9PartitionSyntax.GetPartitionContext(
                aboveContext: 0,
                leftContext: 0,
                Vp9ModeInfoSyntax.GetPartitionContextLog2(blockSize));
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

        var skip = Vp9ModeInfoSyntax.ReadSkip(ref reader, state.CompressedHeader.FrameContext, out var skipContext);
        var transformSize = Vp9ModeInfoSyntax.ReadTransformSize(
            ref reader,
            state.CompressedHeader,
            blockSize,
            out var transformSizeContext);
        var yMode = Vp9ModeInfoSyntax.ReadFirstLeafYMode(ref reader);
        var uvMode = Vp9ModeInfoSyntax.ReadUvMode(ref reader, yMode);

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
            uvMode);
    }

    private static bool IsDcOnlyOrEmpty(Vp9CoefficientBlockProbe coefficients)
    {
        return coefficients.Eob switch
        {
            0 => coefficients.NonZeroCount == 0,
            1 => coefficients.NonZeroCount == 1 &&
                coefficients.FirstNonZeroRasterIndex == 0 &&
                coefficients.LastNonZeroRasterIndex == 0,
            _ => false
        };
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
