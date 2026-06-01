namespace VPDecoder;

public sealed record Vp9TileGeometry(
    int TileRow,
    int TileColumn,
    int MiRowStart,
    int MiRowEnd,
    int MiColumnStart,
    int MiColumnEnd,
    Vp9TileBuffer Buffer)
{
    public int MiRows => MiRowEnd - MiRowStart;

    public int MiColumns => MiColumnEnd - MiColumnStart;

    public int SuperblockRows => DivideRoundUp(MiRows, Vp9TileGeometryBuilder.MiBlockSize);

    public int SuperblockColumns => DivideRoundUp(MiColumns, Vp9TileGeometryBuilder.MiBlockSize);

    public int SuperblockCount => SuperblockRows * SuperblockColumns;

    private static int DivideRoundUp(int value, int divisor)
    {
        return (value + divisor - 1) / divisor;
    }
}

public static class Vp9TileGeometryBuilder
{
    public const int MiBlockSize = 8;

    public static IReadOnlyList<Vp9TileGeometry> Build(
        Vp9FrameHeader header,
        IReadOnlyList<Vp9TileBuffer> tileBuffers)
    {
        var tileRows = header.TileInfo.TileRows;
        var tileColumns = header.TileInfo.TileColumns;
        var expectedTileCount = checked(tileRows * tileColumns);
        if (tileBuffers.Count != expectedTileCount)
        {
            throw new ArgumentException("VP9 tile buffer count does not match the frame tile layout.", nameof(tileBuffers));
        }

        var geometries = new Vp9TileGeometry[expectedTileCount];
        for (var tileRow = 0; tileRow < tileRows; tileRow++)
        {
            var miRowStart = GetTileOffset(tileRow, header.TileInfo.MiRows, header.TileInfo.Log2TileRows);
            var miRowEnd = GetTileOffset(tileRow + 1, header.TileInfo.MiRows, header.TileInfo.Log2TileRows);
            for (var tileColumn = 0; tileColumn < tileColumns; tileColumn++)
            {
                var miColumnStart = GetTileOffset(tileColumn, header.TileInfo.MiColumns, header.TileInfo.Log2TileColumns);
                var miColumnEnd = GetTileOffset(tileColumn + 1, header.TileInfo.MiColumns, header.TileInfo.Log2TileColumns);
                var tileIndex = (tileRow * tileColumns) + tileColumn;
                geometries[tileIndex] = new Vp9TileGeometry(
                    tileRow,
                    tileColumn,
                    miRowStart,
                    miRowEnd,
                    miColumnStart,
                    miColumnEnd,
                    tileBuffers[tileIndex]);
            }
        }

        return geometries;
    }

    private static int GetTileOffset(int index, int miCount, int log2TileCount)
    {
        var alignedMiCount = AlignPowerOfTwo(miCount, 3);
        var superblockCount = alignedMiCount >> 3;
        var offset = ((index * superblockCount) >> log2TileCount) << 3;
        return Math.Min(offset, miCount);
    }

    private static int AlignPowerOfTwo(int value, int n)
    {
        var alignment = (1 << n) - 1;
        return (value + alignment) & ~alignment;
    }
}
