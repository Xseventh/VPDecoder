namespace VPDecoder;

public enum Vp9PartitionType
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Split = 3
}

public sealed record Vp9PartitionProbe(
    int TileIndex,
    int MiRow,
    int MiColumn,
    int PartitionContext,
    Vp9PartitionType PartitionType);

internal static class Vp9PartitionSyntax
{
    private const int PartitionContexts = 16;
    private const int PartitionProbabilityCount = 3;

    private static ReadOnlySpan<sbyte> PartitionTree => [0, 2, -1, 4, -2, -3];

    private static ReadOnlySpan<byte> KeyFramePartitionProbabilities =>
    [
        158, 97, 94,
        93, 24, 99,
        85, 119, 44,
        62, 59, 67,
        149, 53, 53,
        94, 20, 48,
        83, 53, 24,
        52, 18, 18,
        150, 40, 39,
        78, 12, 26,
        67, 33, 11,
        24, 7, 5,
        174, 35, 49,
        68, 11, 27,
        57, 15, 9,
        12, 3, 3
    ];

    public static Vp9PartitionType ReadPartition(
        ref Vp9BoolReader reader,
        int partitionContext,
        bool hasRows,
        bool hasColumns)
    {
        var probabilities = GetKeyFrameProbabilities(partitionContext);
        if (hasRows && hasColumns)
        {
            return (Vp9PartitionType)Vp9TreeReader.ReadTree(ref reader, PartitionTree, probabilities);
        }

        if (!hasRows && hasColumns)
        {
            return reader.Read(probabilities[1]) ? Vp9PartitionType.Split : Vp9PartitionType.Horizontal;
        }

        if (hasRows)
        {
            return reader.Read(probabilities[2]) ? Vp9PartitionType.Split : Vp9PartitionType.Vertical;
        }

        return Vp9PartitionType.Split;
    }

    public static int GetPartitionContext(int aboveContext, int leftContext, int blockSizeLog2)
    {
        var above = (aboveContext >> blockSizeLog2) & 1;
        var left = (leftContext >> blockSizeLog2) & 1;
        return (left * 2 + above) + (blockSizeLog2 * 4);
    }

    private static ReadOnlySpan<byte> GetKeyFrameProbabilities(int partitionContext)
    {
        if (partitionContext is < 0 or >= PartitionContexts)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionContext), "VP9 partition context must be between 0 and 15.");
        }

        return KeyFramePartitionProbabilities.Slice(
            partitionContext * PartitionProbabilityCount,
            PartitionProbabilityCount);
    }
}
