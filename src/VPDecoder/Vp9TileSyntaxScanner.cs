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

                var token = Vp9ResidualSyntax.ReadFirstYCoefficientToken(ref reader, state, modeInfo);
                if (token.DequantizedValue is null)
                {
                    continue;
                }

                var blockSize = modeInfo.BlockSize == Vp9BlockSize.Block64X64 ? 32 : 32;
                var yOffset = state.FrameBuffer.YPlane.Offset +
                    (modeInfo.MiRow * 8 * state.FrameBuffer.YStride) +
                    (modeInfo.MiColumn * 8);
                var plane = state.FrameBuffer.Pixels.AsSpan(
                    state.FrameBuffer.YPlane.Offset,
                    state.FrameBuffer.YPlane.Length);
                var block = state.FrameBuffer.Pixels.AsSpan(yOffset);
                Vp9IntraPredictor.PredictDc(block, state.FrameBuffer.YStride, blockSize, [], []);
                Vp9DcOnlyReconstructor.AddDcOnly(
                    plane,
                    state.FrameBuffer.YStride,
                    modeInfo.MiColumn * 8,
                    modeInfo.MiRow * 8,
                    blockSize,
                    token.DequantizedValue.Value);
            }

            frame = state.FrameBuffer.ToDecodedFrame();
            return true;
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
}
