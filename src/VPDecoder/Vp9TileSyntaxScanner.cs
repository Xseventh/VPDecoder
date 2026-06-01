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
}
