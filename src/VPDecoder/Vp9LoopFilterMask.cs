namespace VPDecoder;

internal sealed class Vp9LoopFilterSuperblockMask
{
    public Vp9LoopFilterSuperblockMask(
        int miRow,
        int miColumn,
        byte filterLevel,
        Vp9LoopFilterThresholds thresholds,
        int sharpnessLevel)
    {
        MiRow = miRow;
        MiColumn = miColumn;
        FilterLevel = filterLevel;
        Thresholds = thresholds;
        _sharpnessLevel = sharpnessLevel;
    }

    private readonly int _sharpnessLevel;

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

    public Vp9LoopFilterThresholds GetLumaThresholds(int bitIndex)
    {
        if (bitIndex is < 0 or >= 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitIndex), bitIndex, "VP9 luma loop-filter bit index must be 0..63.");
        }

        return GetThresholdsForLevel(LevelsY[bitIndex]);
    }

    public Vp9LoopFilterThresholds GetLumaThresholds(int bitIndex, ReadOnlySpan<Vp9LoopFilterThresholds> thresholdsByLevel)
    {
        return thresholdsByLevel[LevelsY[bitIndex]];
    }

    public Vp9LoopFilterThresholds GetChromaThresholds(int row, int column)
    {
        if (row is < 0 or >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "VP9 chroma loop-filter row must be 0..3.");
        }

        if (column is < 0 or >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "VP9 chroma loop-filter column must be 0..3.");
        }

        return GetThresholdsForLevel(LevelsY[((row * 2) * 8) + (column * 2)]);
    }

    public Vp9LoopFilterThresholds GetChromaThresholds(int row, int column, ReadOnlySpan<Vp9LoopFilterThresholds> thresholdsByLevel)
    {
        return thresholdsByLevel[LevelsY[((row * 2) * 8) + (column * 2)]];
    }

    private Vp9LoopFilterThresholds GetThresholdsForLevel(int filterLevel)
    {
        if (filterLevel == FilterLevel)
        {
            return Thresholds;
        }

        if (filterLevel is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(filterLevel), filterLevel, "VP9 loop-filter level must be 0..63.");
        }

        return Vp9LoopFilter.GetThresholds(filterLevel, _sharpnessLevel);
    }
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

        masks = BuildMasks(header, reconstructedFrame, filterLevel);
        return true;
    }

    public static bool TryBuildInterFrameMasks(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame,
        out IReadOnlyList<Vp9LoopFilterSuperblockMask> masks,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        masks = [];
        diagnostic = ValidateInterInputs(header, reconstructedFrame);
        if (diagnostic is not null)
        {
            return false;
        }

        masks = BuildMasks(
            header,
            reconstructedFrame,
            header.LoopFilter.FilterLevel,
            modeBlock => Vp9LoopFilter.GetInterFrameFilterLevel(header.LoopFilter, modeBlock.InterModeInfo!));
        return true;
    }

    public static bool TryBuildInterFrameMasks(
        Vp9FrameHeader header,
        Vp9DecodedFrame frame,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> modeBlocks,
        out IReadOnlyList<Vp9LoopFilterSuperblockMask> masks,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        masks = [];
        diagnostic = ValidateInterInputs(header, frame);
        if (diagnostic is not null)
        {
            return false;
        }

        try
        {
            masks = BuildInterMasks(header, modeBlocks);
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
                "VP9 inter-frame loop filter mask allocation overflowed.");
            return false;
        }
        catch (OutOfMemoryException)
        {
            diagnostic = Vp9DecodeDiagnostic.AllocationLimitExceeded(
                "VP9 inter-frame loop filter mask allocation failed.");
            return false;
        }
    }

    private static IReadOnlyList<Vp9LoopFilterSuperblockMask> BuildMasks(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame,
        int filterLevel)
    {
        return BuildMasks(header, reconstructedFrame, filterLevel, _ => filterLevel);
    }

    private static IReadOnlyList<Vp9LoopFilterSuperblockMask> BuildMasks(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame,
        int filterLevel,
        Func<Vp9ReconstructedModeBlock, int> getModeBlockFilterLevel)
    {
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
                var mask = new Vp9LoopFilterSuperblockMask(
                    miRow,
                    miColumn,
                    (byte)filterLevel,
                    thresholds,
                    header.LoopFilter.SharpnessLevel);
                var key = GetSuperblockKey(superblockRow, superblockColumn, superblockColumns);
                if (modeBlocksBySuperblock.TryGetValue(key, out var modeBlocks))
                {
                    foreach (var modeBlock in modeBlocks)
                    {
                        BuildMaskForModeBlock(
                            mask,
                            reconstructedFrame,
                            modeBlock,
                            getModeBlockFilterLevel(modeBlock));
                    }
                }

                AdjustMask(header, mask);
                builtMasks.Add(mask);
            }
        }

        return builtMasks;
    }

    private static IReadOnlyList<Vp9LoopFilterSuperblockMask> BuildInterMasks(
        Vp9FrameHeader header,
        IReadOnlyList<Vp9InterBlockModeInfoProbe> modeBlocks)
    {
        var filterLevel = header.LoopFilter.FilterLevel;
        var thresholds = Vp9LoopFilter.GetThresholds(filterLevel, header.LoopFilter.SharpnessLevel);
        var superblockColumns = (header.TileInfo.MiColumns + SuperblockSizeInMiUnits - 1) / SuperblockSizeInMiUnits;
        var superblockRows = (header.TileInfo.MiRows + SuperblockSizeInMiUnits - 1) / SuperblockSizeInMiUnits;
        var builtMasks = new List<Vp9LoopFilterSuperblockMask>(checked(superblockColumns * superblockRows));

        for (var superblockRow = 0; superblockRow < superblockRows; superblockRow++)
        {
            var miRow = superblockRow * SuperblockSizeInMiUnits;
            for (var superblockColumn = 0; superblockColumn < superblockColumns; superblockColumn++)
            {
                var miColumn = superblockColumn * SuperblockSizeInMiUnits;
                builtMasks.Add(new Vp9LoopFilterSuperblockMask(
                    miRow,
                    miColumn,
                    (byte)filterLevel,
                    thresholds,
                    header.LoopFilter.SharpnessLevel));
            }
        }

        foreach (var modeBlock in modeBlocks)
        {
            if (modeBlock.MiRow < 0 ||
                modeBlock.MiRow >= header.TileInfo.MiRows ||
                modeBlock.MiColumn < 0 ||
                modeBlock.MiColumn >= header.TileInfo.MiColumns)
            {
                throw new ArgumentException(
                    "VP9 inter loop filter metadata contains a mode info outside the visible MI grid.",
                    nameof(modeBlocks));
            }

            var superblockRow = modeBlock.MiRow / SuperblockSizeInMiUnits;
            var superblockColumn = modeBlock.MiColumn / SuperblockSizeInMiUnits;
            var key = GetSuperblockKey(superblockRow, superblockColumn, superblockColumns);
            BuildMaskForInterModeBlock(
                builtMasks[key],
                header,
                modeBlock,
                Vp9LoopFilter.GetInterFrameFilterLevel(header.LoopFilter, modeBlock.ModeInfo));
        }

        foreach (var mask in builtMasks)
        {
            AdjustMask(header, mask);
        }

        return builtMasks;
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

        return ValidateCommonInputs(header, reconstructedFrame);
    }

    private static Vp9DecodeDiagnostic? ValidateInterInputs(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame)
    {
        if (header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 inter-frame loop filter mask builder requires an ordinary inter frame.");
        }

        var diagnostic = ValidateCommonInputs(header, reconstructedFrame);
        if (diagnostic is not null)
        {
            return diagnostic;
        }

        if (reconstructedFrame.ModeBlocks.Any(modeBlock => modeBlock.InterModeInfo is null))
        {
            return Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 inter-frame loop filter mask builder received non-inter mode metadata.");
        }

        return null;
    }

    private static Vp9DecodeDiagnostic? ValidateInterInputs(
        Vp9FrameHeader header,
        Vp9DecodedFrame frame)
    {
        if (header.FrameType != Vp9FrameType.InterFrame || header.IntraOnly)
        {
            return Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 inter-frame loop filter mask builder requires an ordinary inter frame.");
        }

        var diagnostic = ValidateCommonInputs(header, frame);
        if (diagnostic is not null)
        {
            return diagnostic;
        }

        return null;
    }

    private static Vp9DecodeDiagnostic? ValidateCommonInputs(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame)
    {
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

    private static Vp9DecodeDiagnostic? ValidateCommonInputs(
        Vp9FrameHeader header,
        Vp9DecodedFrame frame)
    {
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

        if (header.Width != frame.Width || header.Height != frame.Height)
        {
            return Vp9DecodeDiagnostic.InternalDecodeFailure(
                "VP9 loop filter mask builder received a decoded frame with mismatched dimensions.");
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
            var superblockRow = modeBlock.MiRow / SuperblockSizeInMiUnits;
            var superblockColumn = modeBlock.MiColumn / SuperblockSizeInMiUnits;
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
        Vp9ReconstructedModeBlock modeBlock,
        int filterLevel)
    {
        if (filterLevel == 0)
        {
            return;
        }

        var rowInSuperblock = modeBlock.MiRow - mask.MiRow;
        var columnInSuperblock = modeBlock.MiColumn - mask.MiColumn;
        if (rowInSuperblock is < 0 or >= SuperblockSizeInMiUnits ||
            columnInSuperblock is < 0 or >= SuperblockSizeInMiUnits)
        {
            throw new ArgumentException("VP9 mode block does not belong to the target superblock.", nameof(modeBlock));
        }

        var blockSizeIndex = (int)modeBlock.BlockSize;
        var yTransformSizeIndex = (int)modeBlock.TransformSize;
        var uvTransformSize = Vp9ResidualSyntax.GetUvTransformSizeForYuv420(
            modeBlock.BlockSize,
            modeBlock.TransformSize);
        var uvTransformSizeIndex = (int)uvTransformSize;
        var shiftY = columnInSuperblock + (rowInSuperblock << 3);
        var shiftUv = (columnInSuperblock >> 1) + ((rowInSuperblock >> 1) << 2);
        var buildUv = FirstBlockIn16X16[rowInSuperblock, columnInSuperblock];

        SetLoopFilterLevels(mask, reconstructedFrame, modeBlock, rowInSuperblock, columnInSuperblock, filterLevel);

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

        if (modeBlock.InterModeInfo is { IsInterBlock: true, Skip: true })
        {
            return;
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

        if (modeBlock.TransformSize == Vp9TransformSize.Tx4X4)
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

    private static void BuildMaskForInterModeBlock(
        Vp9LoopFilterSuperblockMask mask,
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        int filterLevel)
    {
        if (filterLevel == 0)
        {
            return;
        }

        var rowInSuperblock = modeBlock.MiRow - mask.MiRow;
        var columnInSuperblock = modeBlock.MiColumn - mask.MiColumn;
        if (rowInSuperblock is < 0 or >= SuperblockSizeInMiUnits ||
            columnInSuperblock is < 0 or >= SuperblockSizeInMiUnits)
        {
            throw new ArgumentException("VP9 mode block does not belong to the target superblock.", nameof(modeBlock));
        }

        var blockSize = modeBlock.ModeInfo.BlockSize;
        var transformSize = modeBlock.ModeInfo.TransformSize;
        var blockSizeIndex = (int)blockSize;
        var yTransformSizeIndex = (int)transformSize;
        var uvTransformSize = Vp9ResidualSyntax.GetUvTransformSizeForYuv420(
            blockSize,
            transformSize);
        var uvTransformSizeIndex = (int)uvTransformSize;
        var shiftY = columnInSuperblock + (rowInSuperblock << 3);
        var shiftUv = (columnInSuperblock >> 1) + ((rowInSuperblock >> 1) << 2);
        var buildUv = FirstBlockIn16X16[rowInSuperblock, columnInSuperblock];

        SetLoopFilterLevels(
            mask,
            header.TileInfo.MiRows,
            header.TileInfo.MiColumns,
            modeBlock.MiRow,
            modeBlock.MiColumn,
            blockSize,
            rowInSuperblock,
            columnInSuperblock,
            filterLevel);

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

        if (modeBlock.ModeInfo is { IsInterBlock: true, Skip: true })
        {
            return;
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

        if (transformSize == Vp9TransformSize.Tx4X4)
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
        Vp9ReconstructedModeBlock modeBlock,
        int rowInSuperblock,
        int columnInSuperblock,
        int filterLevel)
    {
        SetLoopFilterLevels(
            mask,
            reconstructedFrame.MiRows,
            reconstructedFrame.MiColumns,
            modeBlock.MiRow,
            modeBlock.MiColumn,
            modeBlock.BlockSize,
            rowInSuperblock,
            columnInSuperblock,
            filterLevel);
    }

    private static void SetLoopFilterLevels(
        Vp9LoopFilterSuperblockMask mask,
        int miRows,
        int miColumns,
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        int rowInSuperblock,
        int columnInSuperblock,
        int filterLevel)
    {
        var width = Math.Min(
            Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(blockSize),
            Math.Min(
                miColumns - miColumn,
                SuperblockSizeInMiUnits - columnInSuperblock));
        var height = Math.Min(
            Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(blockSize),
            Math.Min(
                miRows - miRow,
                SuperblockSizeInMiUnits - rowInSuperblock));

        for (var row = 0; row < height; row++)
        {
            var index = ((rowInSuperblock + row) * SuperblockSizeInMiUnits) + columnInSuperblock;
            Array.Fill(mask.LevelsY, (byte)filterLevel, index, width);
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
