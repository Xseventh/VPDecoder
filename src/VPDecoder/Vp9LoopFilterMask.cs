namespace VPDecoder;

internal sealed class Vp9LoopFilterSuperblockMask
{
    public Vp9LoopFilterSuperblockMask(
        int miRow,
        int miColumn,
        byte filterLevel,
        Vp9LoopFilterThresholds thresholds)
    {
        MiRow = miRow;
        MiColumn = miColumn;
        FilterLevel = filterLevel;
        Thresholds = thresholds;
    }

    public int MiRow { get; }

    public int MiColumn { get; }

    public byte FilterLevel { get; }

    public Vp9LoopFilterThresholds Thresholds { get; }

    public ulong[] LeftY { get; } = new ulong[4];

    public ulong[] AboveY { get; } = new ulong[4];

    public ushort[] LeftUv { get; } = new ushort[4];

    public ushort[] AboveUv { get; } = new ushort[4];

    public ulong Internal4x4Y { get; set; }

    public ushort Internal4x4Uv { get; set; }

    public byte[] LevelsY { get; } = new byte[64];

    public int ActiveLevelCount => LevelsY.Count(level => level != 0);

    public bool HasAnyFilter =>
        Internal4x4Y != 0 ||
        Internal4x4Uv != 0 ||
        LeftY.Any(mask => mask != 0) ||
        AboveY.Any(mask => mask != 0) ||
        LeftUv.Any(mask => mask != 0) ||
        AboveUv.Any(mask => mask != 0);
}

internal static class Vp9LoopFilterMaskBuilder
{
    private const int SuperblockSizeInMiUnits = 8;

    private const ulong LeftBorder = 0x1111111111111111UL;
    private const ulong AboveBorder = 0x000000ff000000ffUL;
    private const ushort LeftBorderUv = 0x1111;
    private const ushort AboveBorderUv = 0x000f;

    private static readonly ulong[] LeftTransformMask =
    [
        0xffffffffffffffffUL,
        0xffffffffffffffffUL,
        0x5555555555555555UL,
        0x1111111111111111UL
    ];

    private static readonly ulong[] AboveTransformMask =
    [
        0xffffffffffffffffUL,
        0xffffffffffffffffUL,
        0x00ff00ff00ff00ffUL,
        0x000000ff000000ffUL
    ];

    private static readonly ulong[] LeftPredictionMask =
    [
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000101UL,
        0x0000000000000001UL,
        0x0000000000000101UL,
        0x0000000001010101UL,
        0x0000000000000101UL,
        0x0000000001010101UL,
        0x0101010101010101UL,
        0x0000000001010101UL,
        0x0101010101010101UL
    ];

    private static readonly ulong[] AbovePredictionMask =
    [
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000003UL,
        0x0000000000000003UL,
        0x0000000000000003UL,
        0x000000000000000fUL,
        0x000000000000000fUL,
        0x000000000000000fUL,
        0x00000000000000ffUL,
        0x00000000000000ffUL
    ];

    private static readonly ulong[] SizeMask =
    [
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000001UL,
        0x0000000000000101UL,
        0x0000000000000003UL,
        0x0000000000000303UL,
        0x0000000003030303UL,
        0x0000000000000f0fUL,
        0x000000000f0f0f0fUL,
        0x0f0f0f0f0f0f0f0fUL,
        0x00000000ffffffffUL,
        0xffffffffffffffffUL
    ];

    private static readonly ushort[] LeftTransformMaskUv =
    [
        0xffff,
        0xffff,
        0x5555,
        0x1111
    ];

    private static readonly ushort[] AboveTransformMaskUv =
    [
        0xffff,
        0xffff,
        0x0f0f,
        0x000f
    ];

    private static readonly ushort[] LeftPredictionMaskUv =
    [
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0011,
        0x0001,
        0x0011,
        0x1111,
        0x0011,
        0x1111
    ];

    private static readonly ushort[] AbovePredictionMaskUv =
    [
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0003,
        0x0003,
        0x0003,
        0x000f,
        0x000f
    ];

    private static readonly ushort[] SizeMaskUv =
    [
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0001,
        0x0011,
        0x0003,
        0x0033,
        0x3333,
        0x00ff,
        0xffff
    ];

    private static readonly bool[,] FirstBlockIn16X16 =
    {
        { true, false, true, false, true, false, true, false },
        { false, false, false, false, false, false, false, false },
        { true, false, true, false, true, false, true, false },
        { false, false, false, false, false, false, false, false },
        { true, false, true, false, true, false, true, false },
        { false, false, false, false, false, false, false, false },
        { true, false, true, false, true, false, true, false },
        { false, false, false, false, false, false, false, false }
    };

    public static bool TryBuildKeyFrameMasks(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame,
        out IReadOnlyList<Vp9LoopFilterSuperblockMask> masks,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        masks = [];
        diagnostic = ValidateInputs(header, reconstructedFrame);
        if (diagnostic is not null)
        {
            return false;
        }

        var filterLevel = Vp9LoopFilter.GetKeyFrameFilterLevel(header.LoopFilter);
        if (filterLevel == 0)
        {
            return true;
        }

        var thresholds = Vp9LoopFilter.GetThresholds(filterLevel, header.LoopFilter.SharpnessLevel);
        var superblockColumns = (reconstructedFrame.MiColumns + SuperblockSizeInMiUnits - 1) / SuperblockSizeInMiUnits;
        var superblockRows = (reconstructedFrame.MiRows + SuperblockSizeInMiUnits - 1) / SuperblockSizeInMiUnits;
        var modeBlocksBySuperblock = GroupModeBlocksBySuperblock(reconstructedFrame, superblockColumns);
        var builtMasks = new List<Vp9LoopFilterSuperblockMask>(checked(superblockColumns * superblockRows));

        for (var superblockRow = 0; superblockRow < superblockRows; superblockRow++)
        {
            var miRow = superblockRow * SuperblockSizeInMiUnits;
            for (var superblockColumn = 0; superblockColumn < superblockColumns; superblockColumn++)
            {
                var miColumn = superblockColumn * SuperblockSizeInMiUnits;
                var mask = new Vp9LoopFilterSuperblockMask(miRow, miColumn, (byte)filterLevel, thresholds);
                var key = GetSuperblockKey(superblockRow, superblockColumn, superblockColumns);
                if (modeBlocksBySuperblock.TryGetValue(key, out var modeBlocks))
                {
                    foreach (var modeBlock in modeBlocks)
                    {
                        BuildMaskForModeBlock(mask, reconstructedFrame, modeBlock);
                    }
                }

                AdjustMask(header, mask);
                builtMasks.Add(mask);
            }
        }

        masks = builtMasks;
        return true;
    }

    private static Vp9DecodeDiagnostic? ValidateInputs(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame)
    {
        if (header.FrameType != Vp9FrameType.KeyFrame)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 loop filter mask builder currently supports only key frames.");
        }

        if (header.BitDepth != 8)
        {
            return Vp9DecodeDiagnostic.UnsupportedBitDepth(
                "VP9 loop filter mask builder currently supports only 8-bit frames.");
        }

        if (header.SubsamplingX != 1 || header.SubsamplingY != 1)
        {
            return Vp9DecodeDiagnostic.UnsupportedChromaSubsampling(
                "VP9 loop filter mask builder currently supports only YUV420 frames.");
        }

        if (header.Segmentation.Enabled)
        {
            return Vp9DecodeDiagnostic.UnsupportedFeature(
                "VP9 segmentation loop filter levels are not supported yet.");
        }

        if (header.TileInfo.MiRows != reconstructedFrame.MiRows ||
            header.TileInfo.MiColumns != reconstructedFrame.MiColumns)
        {
            return Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 loop filter mask builder received a reconstructed frame with mismatched MI dimensions.");
        }

        return null;
    }

    private static Dictionary<int, List<Vp9ReconstructedModeBlock>> GroupModeBlocksBySuperblock(
        Vp9ReconstructedFrame reconstructedFrame,
        int superblockColumns)
    {
        var grouped = new Dictionary<int, List<Vp9ReconstructedModeBlock>>();
        foreach (var modeBlock in reconstructedFrame.ModeBlocks)
        {
            var superblockRow = modeBlock.ModeInfo.MiRow / SuperblockSizeInMiUnits;
            var superblockColumn = modeBlock.ModeInfo.MiColumn / SuperblockSizeInMiUnits;
            var key = GetSuperblockKey(superblockRow, superblockColumn, superblockColumns);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped.Add(key, list);
            }

            list.Add(modeBlock);
        }

        return grouped;
    }

    private static int GetSuperblockKey(int superblockRow, int superblockColumn, int superblockColumns)
    {
        return (superblockRow * superblockColumns) + superblockColumn;
    }

    private static void BuildMaskForModeBlock(
        Vp9LoopFilterSuperblockMask mask,
        Vp9ReconstructedFrame reconstructedFrame,
        Vp9ReconstructedModeBlock modeBlock)
    {
        var modeInfo = modeBlock.ModeInfo;
        var rowInSuperblock = modeInfo.MiRow - mask.MiRow;
        var columnInSuperblock = modeInfo.MiColumn - mask.MiColumn;
        if (rowInSuperblock is < 0 or >= SuperblockSizeInMiUnits ||
            columnInSuperblock is < 0 or >= SuperblockSizeInMiUnits)
        {
            throw new ArgumentException("VP9 mode block does not belong to the target superblock.", nameof(modeBlock));
        }

        var blockSizeIndex = (int)modeInfo.BlockSize;
        var yTransformSizeIndex = (int)modeInfo.TransformSize;
        var uvTransformSize = Vp9ResidualSyntax.GetUvTransformSizeForYuv420(
            modeInfo.BlockSize,
            modeInfo.TransformSize);
        var uvTransformSizeIndex = (int)uvTransformSize;
        var shiftY = columnInSuperblock + (rowInSuperblock << 3);
        var shiftUv = (columnInSuperblock >> 1) + ((rowInSuperblock >> 1) << 2);
        var buildUv = FirstBlockIn16X16[rowInSuperblock, columnInSuperblock];

        SetLoopFilterLevels(mask, reconstructedFrame, modeInfo, rowInSuperblock, columnInSuperblock);

        mask.AboveY[yTransformSizeIndex] |= AbovePredictionMask[blockSizeIndex] << shiftY;
        mask.LeftY[yTransformSizeIndex] |= LeftPredictionMask[blockSizeIndex] << shiftY;

        if (buildUv)
        {
            mask.AboveUv[uvTransformSizeIndex] = (ushort)(
                mask.AboveUv[uvTransformSizeIndex] |
                (ushort)(AbovePredictionMaskUv[blockSizeIndex] << shiftUv));
            mask.LeftUv[uvTransformSizeIndex] = (ushort)(
                mask.LeftUv[uvTransformSizeIndex] |
                (ushort)(LeftPredictionMaskUv[blockSizeIndex] << shiftUv));
        }

        mask.AboveY[yTransformSizeIndex] |=
            (SizeMask[blockSizeIndex] & AboveTransformMask[yTransformSizeIndex]) << shiftY;
        mask.LeftY[yTransformSizeIndex] |=
            (SizeMask[blockSizeIndex] & LeftTransformMask[yTransformSizeIndex]) << shiftY;

        if (buildUv)
        {
            mask.AboveUv[uvTransformSizeIndex] = (ushort)(
                mask.AboveUv[uvTransformSizeIndex] |
                (ushort)((SizeMaskUv[blockSizeIndex] & AboveTransformMaskUv[uvTransformSizeIndex]) << shiftUv));
            mask.LeftUv[uvTransformSizeIndex] = (ushort)(
                mask.LeftUv[uvTransformSizeIndex] |
                (ushort)((SizeMaskUv[blockSizeIndex] & LeftTransformMaskUv[uvTransformSizeIndex]) << shiftUv));
        }

        if (modeInfo.TransformSize == Vp9TransformSize.Tx4X4)
        {
            mask.Internal4x4Y |= SizeMask[blockSizeIndex] << shiftY;
        }

        if (buildUv && uvTransformSize == Vp9TransformSize.Tx4X4)
        {
            mask.Internal4x4Uv = (ushort)(
                mask.Internal4x4Uv |
                (ushort)((SizeMaskUv[blockSizeIndex] & 0xffff) << shiftUv));
        }
    }

    private static void SetLoopFilterLevels(
        Vp9LoopFilterSuperblockMask mask,
        Vp9ReconstructedFrame reconstructedFrame,
        Vp9ModeInfoProbe modeInfo,
        int rowInSuperblock,
        int columnInSuperblock)
    {
        var width = Math.Min(
            Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeInfo.BlockSize),
            Math.Min(
                reconstructedFrame.MiColumns - modeInfo.MiColumn,
                SuperblockSizeInMiUnits - columnInSuperblock));
        var height = Math.Min(
            Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeInfo.BlockSize),
            Math.Min(
                reconstructedFrame.MiRows - modeInfo.MiRow,
                SuperblockSizeInMiUnits - rowInSuperblock));

        for (var row = 0; row < height; row++)
        {
            var index = ((rowInSuperblock + row) * SuperblockSizeInMiUnits) + columnInSuperblock;
            Array.Fill(mask.LevelsY, mask.FilterLevel, index, width);
        }
    }

    private static void AdjustMask(Vp9FrameHeader header, Vp9LoopFilterSuperblockMask mask)
    {
        const int tx4 = (int)Vp9TransformSize.Tx4X4;
        const int tx8 = (int)Vp9TransformSize.Tx8X8;
        const int tx16 = (int)Vp9TransformSize.Tx16X16;
        const int tx32 = (int)Vp9TransformSize.Tx32X32;

        mask.LeftY[tx16] |= mask.LeftY[tx32];
        mask.AboveY[tx16] |= mask.AboveY[tx32];
        mask.LeftUv[tx16] = (ushort)(mask.LeftUv[tx16] | mask.LeftUv[tx32]);
        mask.AboveUv[tx16] = (ushort)(mask.AboveUv[tx16] | mask.AboveUv[tx32]);

        mask.LeftY[tx8] |= mask.LeftY[tx4] & LeftBorder;
        mask.LeftY[tx4] &= ~LeftBorder;
        mask.AboveY[tx8] |= mask.AboveY[tx4] & AboveBorder;
        mask.AboveY[tx4] &= ~AboveBorder;
        mask.LeftUv[tx8] = (ushort)(mask.LeftUv[tx8] | (mask.LeftUv[tx4] & LeftBorderUv));
        mask.LeftUv[tx4] = (ushort)(mask.LeftUv[tx4] & ~LeftBorderUv);
        mask.AboveUv[tx8] = (ushort)(mask.AboveUv[tx8] | (mask.AboveUv[tx4] & AboveBorderUv));
        mask.AboveUv[tx4] = (ushort)(mask.AboveUv[tx4] & ~AboveBorderUv);

        if (mask.MiRow + SuperblockSizeInMiUnits > header.TileInfo.MiRows)
        {
            var rows = header.TileInfo.MiRows - mask.MiRow;
            var maskY = (1UL << (rows << 3)) - 1;
            var maskUv = (ushort)((1 << (((rows + 1) >> 1) << 2)) - 1);
            for (var i = tx4; i < tx32; i++)
            {
                mask.LeftY[i] &= maskY;
                mask.AboveY[i] &= maskY;
                mask.LeftUv[i] = (ushort)(mask.LeftUv[i] & maskUv);
                mask.AboveUv[i] = (ushort)(mask.AboveUv[i] & maskUv);
            }

            mask.Internal4x4Y &= maskY;
            mask.Internal4x4Uv = (ushort)(mask.Internal4x4Uv & maskUv);

            if (rows == 1)
            {
                mask.AboveUv[tx8] = (ushort)(mask.AboveUv[tx8] | mask.AboveUv[tx16]);
                mask.AboveUv[tx16] = 0;
            }
            else if (rows == 5)
            {
                var wideLastRow = (ushort)(mask.AboveUv[tx16] & 0xff00);
                mask.AboveUv[tx8] = (ushort)(mask.AboveUv[tx8] | wideLastRow);
                mask.AboveUv[tx16] = (ushort)(mask.AboveUv[tx16] & ~wideLastRow);
            }
        }

        if (mask.MiColumn + SuperblockSizeInMiUnits > header.TileInfo.MiColumns)
        {
            var columns = header.TileInfo.MiColumns - mask.MiColumn;
            var maskY = ((1UL << columns) - 1) * 0x0101010101010101UL;
            var maskUv = (ushort)(((1 << ((columns + 1) >> 1)) - 1) * 0x1111);
            var maskUvInternal = (ushort)(((1 << (columns >> 1)) - 1) * 0x1111);
            for (var i = tx4; i < tx32; i++)
            {
                mask.LeftY[i] &= maskY;
                mask.AboveY[i] &= maskY;
                mask.LeftUv[i] = (ushort)(mask.LeftUv[i] & maskUv);
                mask.AboveUv[i] = (ushort)(mask.AboveUv[i] & maskUv);
            }

            mask.Internal4x4Y &= maskY;
            mask.Internal4x4Uv = (ushort)(mask.Internal4x4Uv & maskUvInternal);

            if (columns == 1)
            {
                mask.LeftUv[tx8] = (ushort)(mask.LeftUv[tx8] | mask.LeftUv[tx16]);
                mask.LeftUv[tx16] = 0;
            }
            else if (columns == 5)
            {
                var wideLastColumn = (ushort)(mask.LeftUv[tx16] & 0xcccc);
                mask.LeftUv[tx8] = (ushort)(mask.LeftUv[tx8] | wideLastColumn);
                mask.LeftUv[tx16] = (ushort)(mask.LeftUv[tx16] & ~wideLastColumn);
            }
        }

        if (mask.MiColumn == 0)
        {
            for (var i = tx4; i < tx32; i++)
            {
                mask.LeftY[i] &= 0xfefefefefefefefeUL;
                mask.LeftUv[i] = (ushort)(mask.LeftUv[i] & 0xeeee);
            }
        }
    }
}
