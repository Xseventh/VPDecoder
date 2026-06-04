namespace VPDecoder;

internal sealed class Vp9PreviousFrameMotionVectors
{
    private readonly Vp9PreviousFrameMotionVectorEntry?[] _grid;

    private Vp9PreviousFrameMotionVectors(
        int width,
        int height,
        int miRows,
        int miColumns,
        Vp9PreviousFrameMotionVectorEntry?[] grid)
    {
        Width = width;
        Height = height;
        MiRows = miRows;
        MiColumns = miColumns;
        _grid = grid;
    }

    public int Width { get; }

    public int Height { get; }

    public int MiRows { get; }

    public int MiColumns { get; }

    public static Vp9PreviousFrameMotionVectors FromReconstructedFrame(
        Vp9FrameHeader header,
        Vp9ReconstructedFrame reconstructedFrame)
    {
        return FromModeBlocks(
            header.Width,
            header.Height,
            header.TileInfo.MiRows,
            header.TileInfo.MiColumns,
            reconstructedFrame.InterSuperblocks.SelectMany(superblock => superblock.ModeInfos));
    }

    public static Vp9PreviousFrameMotionVectors FromModeBlocks(
        int width,
        int height,
        int miRows,
        int miColumns,
        IEnumerable<Vp9InterBlockModeInfoProbe> modeBlocks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(miRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(miColumns);

        var grid = new Vp9PreviousFrameMotionVectorEntry?[checked(miRows * miColumns)];
        foreach (var modeBlock in modeBlocks)
        {
            if (!modeBlock.ModeInfo.IsInterBlock || modeBlock.MotionVector is not { } motionVector)
            {
                continue;
            }

            var widthInMiUnits = Math.Min(
                Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeBlock.ModeInfo.BlockSize),
                miColumns - modeBlock.MiColumn);
            var heightInMiUnits = Math.Min(
                Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeBlock.ModeInfo.BlockSize),
                miRows - modeBlock.MiRow);
            if (widthInMiUnits <= 0 || heightInMiUnits <= 0)
            {
                throw new ArgumentException(
                    "VP9 previous-frame MV metadata contains a mode info outside the visible MI grid.",
                    nameof(modeBlocks));
            }

            var entry = new Vp9PreviousFrameMotionVectorEntry(
                modeBlock.ModeInfo.ReferenceFrame,
                motionVector,
                modeBlock.ModeInfo.CompoundReferenceFrame,
                modeBlock.CompoundMotionVector);
            for (var row = 0; row < heightInMiUnits; row++)
            {
                var offset = ((modeBlock.MiRow + row) * miColumns) + modeBlock.MiColumn;
                for (var column = 0; column < widthInMiUnits; column++)
                {
                    grid[offset + column] = entry;
                }
            }
        }

        return new Vp9PreviousFrameMotionVectors(width, height, miRows, miColumns, grid);
    }

    public bool CanUseFor(Vp9FrameHeader header)
    {
        return !header.ErrorResilientMode &&
            header.Width == Width &&
            header.Height == Height &&
            header.TileInfo.MiRows == MiRows &&
            header.TileInfo.MiColumns == MiColumns;
    }

    public bool TryGetEntryAtMi(
        int miRow,
        int miColumn,
        out Vp9PreviousFrameMotionVectorEntry entry)
    {
        if (miRow < 0 || miRow >= MiRows || miColumn < 0 || miColumn >= MiColumns)
        {
            entry = default;
            return false;
        }

        var existing = _grid[(miRow * MiColumns) + miColumn];
        if (existing is { } value)
        {
            entry = value;
            return true;
        }

        entry = default;
        return false;
    }
}

internal readonly record struct Vp9PreviousFrameMotionVectorEntry(
    Vp9InterReferenceFrame ReferenceFrame,
    Vp9MotionVector MotionVector,
    Vp9InterReferenceFrame? CompoundReferenceFrame,
    Vp9MotionVector? CompoundMotionVector);
