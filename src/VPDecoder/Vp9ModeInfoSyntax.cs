namespace VPDecoder;

public enum Vp9PredictionMode
{
    Dc = 0,
    Vertical = 1,
    Horizontal = 2,
    D45 = 3,
    D135 = 4,
    D117 = 5,
    D153 = 6,
    D207 = 7,
    D63 = 8,
    TrueMotion = 9
}

public enum Vp9TransformSize
{
    Tx4X4 = 0,
    Tx8X8 = 1,
    Tx16X16 = 2,
    Tx32X32 = 3
}

public enum Vp9BlockSize
{
    Block4X4,
    Block4X8,
    Block8X4,
    Block8X8,
    Block8X16,
    Block16X8,
    Block16X16,
    Block16X32,
    Block32X16,
    Block32X32,
    Block32X64,
    Block64X32,
    Block64X64
}

public sealed record Vp9ModeInfoProbe(
    int TileIndex,
    int MiRow,
    int MiColumn,
    Vp9BlockSize BlockSize,
    IReadOnlyList<Vp9PartitionType> PartitionPath,
    bool Skip,
    int SkipContext,
    Vp9TransformSize TransformSize,
    int TransformSizeContext,
    Vp9PredictionMode YMode,
    Vp9PredictionMode UvMode);

internal static class Vp9ModeInfoSyntax
{
    private static ReadOnlySpan<sbyte> IntraModeTree =>
    [
        0, 2,
        -9, 4,
        -1, 6,
        8, 12,
        -2, 10,
        -4, -5,
        -3, 14,
        -8, 16,
        -6, -7
    ];

    private static ReadOnlySpan<byte> KeyFrameYModeDcDcProbabilities =>
    [
        137, 30, 42, 148, 151, 207, 70, 52, 91
    ];

    private static ReadOnlySpan<byte> KeyFrameUvModeProbabilities =>
    [
        144, 11, 54, 157, 195, 130, 46, 58, 108,
        118, 15, 123, 148, 131, 101, 44, 93, 131,
        113, 12, 23, 188, 226, 142, 26, 32, 125,
        120, 11, 50, 123, 163, 135, 64, 77, 103,
        113, 9, 36, 155, 111, 157, 32, 44, 161,
        116, 9, 55, 176, 76, 96, 37, 61, 149,
        115, 9, 28, 141, 161, 167, 21, 25, 193,
        120, 12, 32, 145, 195, 142, 32, 38, 86,
        116, 12, 64, 120, 140, 125, 49, 115, 121,
        102, 19, 66, 162, 182, 122, 35, 59, 128
    ];

    public static bool ReadSkip(ref Vp9BoolReader reader, Vp9FrameContext frameContext, out int skipContext)
    {
        skipContext = 0;
        return reader.Read(frameContext.SkipProbabilities[skipContext]);
    }

    public static Vp9TransformSize ReadTransformSize(
        ref Vp9BoolReader reader,
        Vp9CompressedHeader compressedHeader,
        Vp9BlockSize blockSize,
        out int transformSizeContext)
    {
        var maxTransformSize = GetMaximumTransformSize(blockSize);
        if (compressedHeader.TransformMode != Vp9TransformMode.Select || blockSize < Vp9BlockSize.Block8X8)
        {
            transformSizeContext = 0;
            return (Vp9TransformSize)Math.Min((int)maxTransformSize, GetBiggestTransformSize(compressedHeader.TransformMode));
        }

        transformSizeContext = maxTransformSize == Vp9TransformSize.Tx4X4 ? 0 : 1;
        var probabilities = GetTransformProbabilities(compressedHeader.FrameContext.TxProbabilities, maxTransformSize, transformSizeContext);
        var txSize = reader.Read(probabilities[0]) ? 1 : 0;
        if (txSize != 0 && maxTransformSize >= Vp9TransformSize.Tx16X16)
        {
            txSize += reader.Read(probabilities[1]) ? 1 : 0;
            if (txSize != 1 && maxTransformSize >= Vp9TransformSize.Tx32X32)
            {
                txSize += reader.Read(probabilities[2]) ? 1 : 0;
            }
        }

        return (Vp9TransformSize)txSize;
    }

    public static Vp9PredictionMode ReadFirstLeafYMode(ref Vp9BoolReader reader)
    {
        return (Vp9PredictionMode)Vp9TreeReader.ReadTree(ref reader, IntraModeTree, KeyFrameYModeDcDcProbabilities);
    }

    public static Vp9PredictionMode ReadUvMode(ref Vp9BoolReader reader, Vp9PredictionMode yMode)
    {
        var offset = (int)yMode * 9;
        return (Vp9PredictionMode)Vp9TreeReader.ReadTree(
            ref reader,
            IntraModeTree,
            KeyFrameUvModeProbabilities.Slice(offset, 9));
    }

    public static Vp9BlockSize GetSubsize(Vp9BlockSize blockSize, Vp9PartitionType partitionType)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block64X64 => partitionType switch
            {
                Vp9PartitionType.None => Vp9BlockSize.Block64X64,
                Vp9PartitionType.Horizontal => Vp9BlockSize.Block64X32,
                Vp9PartitionType.Vertical => Vp9BlockSize.Block32X64,
                Vp9PartitionType.Split => Vp9BlockSize.Block32X32,
                _ => throw new ArgumentOutOfRangeException(nameof(partitionType))
            },
            Vp9BlockSize.Block32X32 => partitionType switch
            {
                Vp9PartitionType.None => Vp9BlockSize.Block32X32,
                Vp9PartitionType.Horizontal => Vp9BlockSize.Block32X16,
                Vp9PartitionType.Vertical => Vp9BlockSize.Block16X32,
                Vp9PartitionType.Split => Vp9BlockSize.Block16X16,
                _ => throw new ArgumentOutOfRangeException(nameof(partitionType))
            },
            Vp9BlockSize.Block16X16 => partitionType switch
            {
                Vp9PartitionType.None => Vp9BlockSize.Block16X16,
                Vp9PartitionType.Horizontal => Vp9BlockSize.Block16X8,
                Vp9PartitionType.Vertical => Vp9BlockSize.Block8X16,
                Vp9PartitionType.Split => Vp9BlockSize.Block8X8,
                _ => throw new ArgumentOutOfRangeException(nameof(partitionType))
            },
            Vp9BlockSize.Block8X8 => partitionType switch
            {
                Vp9PartitionType.None => Vp9BlockSize.Block8X8,
                Vp9PartitionType.Horizontal => Vp9BlockSize.Block8X4,
                Vp9PartitionType.Vertical => Vp9BlockSize.Block4X8,
                Vp9PartitionType.Split => Vp9BlockSize.Block4X4,
                _ => throw new ArgumentOutOfRangeException(nameof(partitionType))
            },
            _ => throw new NotSupportedException($"VP9 partition subsize is not supported for block size {blockSize}.")
        };
    }

    public static int GetHalfBlockSizeInMiUnits(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block64X64 => 4,
            Vp9BlockSize.Block32X32 => 2,
            Vp9BlockSize.Block16X16 => 1,
            Vp9BlockSize.Block8X8 => 0,
            _ => 0
        };
    }

    public static int GetPartitionContextLog2(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block64X64 => 3,
            Vp9BlockSize.Block32X32 => 2,
            Vp9BlockSize.Block16X16 => 1,
            Vp9BlockSize.Block8X8 => 0,
            _ => 0
        };
    }

    private static Vp9TransformSize GetMaximumTransformSize(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            <= Vp9BlockSize.Block8X4 => Vp9TransformSize.Tx4X4,
            <= Vp9BlockSize.Block16X8 => Vp9TransformSize.Tx8X8,
            <= Vp9BlockSize.Block32X16 => Vp9TransformSize.Tx16X16,
            _ => Vp9TransformSize.Tx32X32
        };
    }

    private static int GetBiggestTransformSize(Vp9TransformMode transformMode)
    {
        return transformMode switch
        {
            Vp9TransformMode.Only4X4 => 0,
            Vp9TransformMode.Allow8X8 => 1,
            Vp9TransformMode.Allow16X16 => 2,
            Vp9TransformMode.Allow32X32 or Vp9TransformMode.Select => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(transformMode))
        };
    }

    private static byte[] GetTransformProbabilities(
        Vp9TxProbabilities probabilities,
        Vp9TransformSize maxTransformSize,
        int context)
    {
        return maxTransformSize switch
        {
            Vp9TransformSize.Tx8X8 => [probabilities.EightByEight[context, 0]],
            Vp9TransformSize.Tx16X16 => [probabilities.SixteenBySixteen[context, 0], probabilities.SixteenBySixteen[context, 1]],
            Vp9TransformSize.Tx32X32 => [probabilities.ThirtyTwoByThirtyTwo[context, 0], probabilities.ThirtyTwoByThirtyTwo[context, 1], probabilities.ThirtyTwoByThirtyTwo[context, 2]],
            _ => throw new ArgumentOutOfRangeException(nameof(maxTransformSize))
        };
    }
}
