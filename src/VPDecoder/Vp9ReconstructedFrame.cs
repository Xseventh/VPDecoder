namespace VPDecoder;

internal sealed class Vp9ReconstructedFrame
{
    private readonly Vp9ReconstructedModeBlock?[] _modeGrid;

    public Vp9ReconstructedFrame(
        Vp9DecodedFrame frame,
        IReadOnlyList<Vp9SuperblockSyntaxProbe> superblocks,
        int miRows,
        int miColumns)
    {
        Frame = frame;
        Superblocks = superblocks;
        MiRows = miRows;
        MiColumns = miColumns;

        var modeBlocks = new List<Vp9ReconstructedModeBlock>();
        var coefficientGroupCount = 0;
        var coefficientBlockCount = 0;
        foreach (var superblock in superblocks)
        {
            var expectedGroupCount = checked(superblock.ModeInfos.Count * 3);
            if (superblock.CoefficientGroups.Count != expectedGroupCount)
            {
                throw new ArgumentException(
                    "VP9 reconstructed frame metadata received mismatched mode/coefficient groups.",
                    nameof(superblocks));
            }

            for (var i = 0; i < superblock.ModeInfos.Count; i++)
            {
                var groupOffset = i * 3;
                var groups = new[]
                {
                    superblock.CoefficientGroups[groupOffset],
                    superblock.CoefficientGroups[groupOffset + 1],
                    superblock.CoefficientGroups[groupOffset + 2]
                };
                coefficientGroupCount += groups.Length;
                coefficientBlockCount += groups.Sum(group => group.Blocks.Count);
                modeBlocks.Add(new Vp9ReconstructedModeBlock(superblock.ModeInfos[i], groups));
            }
        }

        ModeBlocks = modeBlocks;
        CoefficientGroupCount = coefficientGroupCount;
        CoefficientBlockCount = coefficientBlockCount;
        _modeGrid = BuildModeGrid(modeBlocks, miRows, miColumns);
        CoveredMiUnitCount = _modeGrid.Count(entry => entry is not null);
    }

    public Vp9DecodedFrame Frame { get; }

    public IReadOnlyList<Vp9SuperblockSyntaxProbe> Superblocks { get; }

    public IReadOnlyList<Vp9ReconstructedModeBlock> ModeBlocks { get; }

    public int MiRows { get; }

    public int MiColumns { get; }

    public int CoefficientGroupCount { get; }

    public int CoefficientBlockCount { get; }

    public int CoveredMiUnitCount { get; }

    public bool TryGetModeBlockAtMi(int miRow, int miColumn, out Vp9ReconstructedModeBlock? modeBlock)
    {
        if (miRow < 0 || miRow >= MiRows || miColumn < 0 || miColumn >= MiColumns)
        {
            modeBlock = null;
            return false;
        }

        modeBlock = _modeGrid[(miRow * MiColumns) + miColumn];
        return modeBlock is not null;
    }

    private static Vp9ReconstructedModeBlock?[] BuildModeGrid(
        IReadOnlyList<Vp9ReconstructedModeBlock> modeBlocks,
        int miRows,
        int miColumns)
    {
        if (miRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(miRows), "VP9 MI row count must be positive.");
        }

        if (miColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(miColumns), "VP9 MI column count must be positive.");
        }

        var grid = new Vp9ReconstructedModeBlock?[checked(miRows * miColumns)];
        foreach (var modeBlock in modeBlocks)
        {
            var modeInfo = modeBlock.ModeInfo;
            var width = Math.Min(
                Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(modeInfo.BlockSize),
                miColumns - modeInfo.MiColumn);
            var height = Math.Min(
                Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(modeInfo.BlockSize),
                miRows - modeInfo.MiRow);
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException(
                    "VP9 reconstructed frame metadata contains a mode info outside the visible MI grid.",
                    nameof(modeBlocks));
            }

            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var index = ((modeInfo.MiRow + row) * miColumns) + modeInfo.MiColumn + column;
                    if (grid[index] is not null)
                    {
                        throw new ArgumentException(
                            "VP9 reconstructed frame metadata contains overlapping mode infos.",
                            nameof(modeBlocks));
                    }

                    grid[index] = modeBlock;
                }
            }
        }

        return grid;
    }
}

internal sealed record Vp9ReconstructedModeBlock(
    Vp9ModeInfoProbe ModeInfo,
    IReadOnlyList<Vp9CoefficientBlockGroupProbe> CoefficientGroups)
{
    public int NonZeroCoefficientBlockCount =>
        CoefficientGroups.Sum(group => group.Blocks.Count(block => block.Eob > 0));
}
