namespace VPDecoder;

internal sealed record Vp8TokenPartitionLayout(IReadOnlyList<Vp8TokenPartition> Partitions);

internal readonly record struct Vp8TokenPartition(int Index, int Offset, int Size);

internal static class Vp8TokenPartitionLayoutBuilder
{
    public static int GetPartitionIndexForMacroblockRow(int row, int partitionCount)
    {
        if (row < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if (partitionCount is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(partitionCount),
                partitionCount,
                "VP8 token partition count must be 1, 2, 4, or 8.");
        }

        return row & (partitionCount - 1);
    }

    public static bool TryValidateUsedPartitions(
        Vp8TokenPartitionLayout layout,
        int macroblockRows,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (macroblockRows <= 0)
        {
            diagnostic = Vp8DecodeDiagnostic.InvalidPacket("VP8 macroblock row count must be positive.");
            return false;
        }

        var usedPartitionCount = Math.Min(layout.Partitions.Count, macroblockRows);
        for (var partitionIndex = 0; partitionIndex < usedPartitionCount; partitionIndex++)
        {
            if (layout.Partitions[partitionIndex].Size == 0)
            {
                diagnostic = Vp8DecodeDiagnostic.TruncatedPacket(
                    $"VP8 token partition {partitionIndex} is empty.");
                return false;
            }
        }

        return true;
    }

    public static bool TryCreate(
        ReadOnlySpan<byte> packet,
        Vp8FrameHeader header,
        Vp8KeyFrameSyntaxHeader syntaxHeader,
        out Vp8TokenPartitionLayout? layout,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        layout = null;
        diagnostic = null;

        var partitionCount = syntaxHeader.TokenPartitionCount;
        if (partitionCount is not (1 or 2 or 4 or 8))
        {
            diagnostic = Vp8DecodeDiagnostic.InvalidPacket(
                $"Invalid VP8 token partition count {partitionCount}.");
            return false;
        }

        var sizeTableOffset = header.HeaderSizeInBytes + header.FirstPartitionSize;
        if (sizeTableOffset > packet.Length)
        {
            diagnostic = Vp8DecodeDiagnostic.TruncatedPacket(
                "VP8 first partition extends past the packet boundary.");
            return false;
        }

        var sizeTableLength = checked(3 * (partitionCount - 1));
        var tokenDataOffset = sizeTableOffset + sizeTableLength;
        if (tokenDataOffset > packet.Length)
        {
            diagnostic = Vp8DecodeDiagnostic.TruncatedPacket(
                "VP8 token partition size table extends past the packet boundary.");
            return false;
        }

        var availableTokenBytes = packet.Length - tokenDataOffset;
        var partitions = new Vp8TokenPartition[partitionCount];
        var consumedTokenBytes = 0;
        for (var i = 0; i < partitionCount - 1; i++)
        {
            var sizeOffset = sizeTableOffset + (3 * i);
            var partitionSize =
                packet[sizeOffset] |
                (packet[sizeOffset + 1] << 8) |
                (packet[sizeOffset + 2] << 16);
            if (consumedTokenBytes + partitionSize > availableTokenBytes)
            {
                diagnostic = Vp8DecodeDiagnostic.TruncatedPacket(
                    $"VP8 token partition {i} extends past the packet boundary.");
                return false;
            }

            partitions[i] = new Vp8TokenPartition(
                i,
                tokenDataOffset + consumedTokenBytes,
                partitionSize);
            consumedTokenBytes += partitionSize;
        }

        partitions[^1] = new Vp8TokenPartition(
            partitionCount - 1,
            tokenDataOffset + consumedTokenBytes,
            availableTokenBytes - consumedTokenBytes);
        layout = new Vp8TokenPartitionLayout(partitions);
        return true;
    }
}
