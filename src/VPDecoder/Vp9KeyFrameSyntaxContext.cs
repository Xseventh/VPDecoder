namespace VPDecoder;

internal sealed class Vp9KeyFrameSyntaxContext
{
    private readonly int _miColumns;
    private readonly int _miRows;
    private readonly Vp9ModeInfoContextEntry?[] _modeInfoGrid;
    private readonly byte[] _abovePartitionContext;
    private readonly byte[] _leftPartitionContext = new byte[8];

    private Vp9KeyFrameSyntaxContext(int miColumns, int miRows)
    {
        _miColumns = miColumns;
        _miRows = miRows;
        _modeInfoGrid = new Vp9ModeInfoContextEntry?[checked(miColumns * miRows)];
        _abovePartitionContext = new byte[miColumns];
    }

    public static Vp9KeyFrameSyntaxContext Create(Vp9FrameHeader header)
    {
        return new Vp9KeyFrameSyntaxContext(header.TileInfo.MiColumns, header.TileInfo.MiRows);
    }

    public void ResetLeftPartitionContext()
    {
        Array.Clear(_leftPartitionContext);
    }

    public int GetPartitionContext(int miRow, int miColumn, Vp9BlockSize blockSize)
    {
        ValidateMiPosition(miRow, miColumn);
        var above = _abovePartitionContext[miColumn];
        var left = _leftPartitionContext[miRow & 7];
        return Vp9PartitionSyntax.GetPartitionContext(
            above,
            left,
            Vp9ModeInfoSyntax.GetPartitionContextLog2(blockSize));
    }

    public void UpdatePartitionContext(int miRow, int miColumn, Vp9BlockSize partitionBlockSize, Vp9BlockSize subsize)
    {
        ValidateMiPosition(miRow, miColumn);
        var widthInMiUnits = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(partitionBlockSize);
        var update = GetPartitionContextUpdate(subsize);
        var width = Math.Min(widthInMiUnits, _miColumns - miColumn);
        for (var i = 0; i < width; i++)
        {
            _abovePartitionContext[miColumn + i] = update.Above;
            _leftPartitionContext[(miRow + i) & 7] = update.Left;
        }
    }

    public int GetSkipContext(int miRow, int miColumn, int tileMiColumnStart)
    {
        ValidateMiPosition(miRow, miColumn);
        var above = TryGetAbove(miRow, miColumn, out var aboveInfo) && aboveInfo.Skip ? 1 : 0;
        var left = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo) && leftInfo.Skip ? 1 : 0;
        return above + left;
    }

    public int GetTransformSizeContext(int miRow, int miColumn, int tileMiColumnStart, Vp9BlockSize blockSize)
    {
        ValidateMiPosition(miRow, miColumn);
        var maxTransformSize = Vp9ModeInfoSyntax.GetMaximumTransformSize(blockSize);
        var max = (int)maxTransformSize;
        var hasAbove = TryGetAbove(miRow, miColumn, out var aboveInfo);
        var hasLeft = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo);
        var above = hasAbove && !aboveInfo.Skip ? (int)aboveInfo.TransformSize : max;
        var left = hasLeft && !leftInfo.Skip ? (int)leftInfo.TransformSize : max;

        if (!hasLeft)
        {
            left = above;
        }

        if (!hasAbove)
        {
            above = left;
        }

        return (above + left) > max ? 1 : 0;
    }

    public (Vp9PredictionMode Above, Vp9PredictionMode Left) GetYModeContext(
        int miRow,
        int miColumn,
        int tileMiColumnStart)
    {
        ValidateMiPosition(miRow, miColumn);
        var above = TryGetAbove(miRow, miColumn, out var aboveInfo)
            ? aboveInfo.YMode
            : Vp9PredictionMode.Dc;
        var left = TryGetLeft(miRow, miColumn, tileMiColumnStart, out var leftInfo)
            ? leftInfo.YMode
            : Vp9PredictionMode.Dc;
        return (above, left);
    }

    public void SetModeInfo(
        int miRow,
        int miColumn,
        Vp9BlockSize blockSize,
        bool skip,
        Vp9TransformSize transformSize,
        Vp9PredictionMode yMode)
    {
        ValidateMiPosition(miRow, miColumn);
        var width = Math.Min(Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(blockSize), _miColumns - miColumn);
        var height = Math.Min(Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(blockSize), _miRows - miRow);
        var entry = new Vp9ModeInfoContextEntry(skip, transformSize, yMode);
        for (var row = 0; row < height; row++)
        {
            var offset = ((miRow + row) * _miColumns) + miColumn;
            for (var column = 0; column < width; column++)
            {
                _modeInfoGrid[offset + column] = entry;
            }
        }
    }

    private bool TryGetAbove(int miRow, int miColumn, out Vp9ModeInfoContextEntry entry)
    {
        if (miRow > 0)
        {
            var existing = _modeInfoGrid[((miRow - 1) * _miColumns) + miColumn];
            if (existing.HasValue)
            {
                entry = existing.Value;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private bool TryGetLeft(
        int miRow,
        int miColumn,
        int tileMiColumnStart,
        out Vp9ModeInfoContextEntry entry)
    {
        if (miColumn > tileMiColumnStart)
        {
            var existing = _modeInfoGrid[(miRow * _miColumns) + miColumn - 1];
            if (existing.HasValue)
            {
                entry = existing.Value;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private void ValidateMiPosition(int miRow, int miColumn)
    {
        if (miRow < 0 || miRow >= _miRows)
        {
            throw new ArgumentOutOfRangeException(nameof(miRow), "VP9 MI row is outside the frame.");
        }

        if (miColumn < 0 || miColumn >= _miColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(miColumn), "VP9 MI column is outside the frame.");
        }
    }

    private static (byte Above, byte Left) GetPartitionContextUpdate(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block4X4 => (15, 15),
            Vp9BlockSize.Block4X8 => (15, 14),
            Vp9BlockSize.Block8X4 => (14, 15),
            Vp9BlockSize.Block8X8 => (14, 14),
            Vp9BlockSize.Block8X16 => (14, 12),
            Vp9BlockSize.Block16X8 => (12, 14),
            Vp9BlockSize.Block16X16 => (12, 12),
            Vp9BlockSize.Block16X32 => (12, 8),
            Vp9BlockSize.Block32X16 => (8, 12),
            Vp9BlockSize.Block32X32 => (8, 8),
            Vp9BlockSize.Block32X64 => (8, 0),
            Vp9BlockSize.Block64X32 => (0, 8),
            Vp9BlockSize.Block64X64 => (0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "Unsupported VP9 block size.")
        };
    }

    private readonly record struct Vp9ModeInfoContextEntry(
        bool Skip,
        Vp9TransformSize TransformSize,
        Vp9PredictionMode YMode);
}
