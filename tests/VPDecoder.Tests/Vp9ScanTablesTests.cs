namespace VPDecoder.Tests;

public sealed class Vp9ScanTablesTests
{
    [Fact]
    public void GetDefaultScan_ForTx4_ReturnsLibvpxDefaultOrder()
    {
        var scan = Vp9ScanTables.GetDefaultScan(Vp9TransformSize.Tx4X4);

        Assert.Equal(16, scan.Length);
        Assert.Equal([0, 4, 1, 5, 8, 2], scan[..6].ToArray());
        Assert.Equal([13, 10, 7, 14, 11, 15], scan[^6..].ToArray());
    }

    [Fact]
    public void GetDefaultNeighbors_ForTx4_ReturnsExpectedContextNeighbors()
    {
        var neighbors = Vp9ScanTables.GetDefaultNeighbors(Vp9TransformSize.Tx4X4);

        Assert.Equal(34, neighbors.Length);
        Assert.Equal([0, 0, 0, 0, 0, 0, 1, 4], neighbors[..8].ToArray());
        Assert.Equal([7, 10, 11, 14, 0, 0], neighbors[^6..].ToArray());
    }

    [Fact]
    public void GetDefaultScan_ForTx8_ReturnsLibvpxDefaultOrder()
    {
        var scan = Vp9ScanTables.GetDefaultScan(Vp9TransformSize.Tx8X8);

        Assert.Equal(64, scan.Length);
        Assert.Equal([0, 8, 1, 16, 9, 2], scan[..6].ToArray());
        Assert.Equal([61, 54, 47, 62, 55, 63], scan[^6..].ToArray());
    }

    [Fact]
    public void GetDefaultNeighbors_ForTx8_ReturnsExpectedContextNeighbors()
    {
        var neighbors = Vp9ScanTables.GetDefaultNeighbors(Vp9TransformSize.Tx8X8);

        Assert.Equal(130, neighbors.Length);
        Assert.Equal([0, 0, 0, 0, 0, 0, 8, 8], neighbors[..8].ToArray());
        Assert.Equal([47, 54, 55, 62, 0, 0], neighbors[^6..].ToArray());
    }

    [Fact]
    public void GetDefaultScan_ForTx16_ReturnsLibvpxDefaultOrder()
    {
        var scan = Vp9ScanTables.GetDefaultScan(Vp9TransformSize.Tx16X16);

        Assert.Equal(256, scan.Length);
        Assert.Equal([0, 16, 1, 32, 17, 2], scan[..6].ToArray());
        Assert.Equal([236, 237, 191, 206, 252, 222, 253, 207, 238, 223, 254, 239, 255], scan[^13..].ToArray());
    }

    [Fact]
    public void GetDefaultNeighbors_ForTx16_ReturnsExpectedContextNeighbors()
    {
        var neighbors = Vp9ScanTables.GetDefaultNeighbors(Vp9TransformSize.Tx16X16);

        Assert.Equal(514, neighbors.Length);
        Assert.Equal([0, 0, 0, 0, 0, 0, 16, 16], neighbors[..8].ToArray());
        Assert.Equal([239, 254, 0, 0], neighbors[^4..].ToArray());
    }

    [Fact]
    public void GetScan_ForTx8AdstDct_ReturnsLibvpxRowOrder()
    {
        var scan = Vp9ScanTables.GetScan(Vp9TransformSize.Tx8X8, Vp9TransformType.AdstDct);

        Assert.Equal(64, scan.Length);
        Assert.Equal([0, 1, 2, 8, 9, 3], scan[..6].ToArray());
        Assert.Equal([61, 47, 54, 55, 62, 63], scan[^6..].ToArray());
    }

    [Fact]
    public void GetNeighbors_ForTx8AdstDct_ReturnsLibvpxRowNeighbors()
    {
        var neighbors = Vp9ScanTables.GetNeighbors(Vp9TransformSize.Tx8X8, Vp9TransformType.AdstDct);

        Assert.Equal(130, neighbors.Length);
        Assert.Equal([0, 0, 0, 0, 1, 1, 0, 0], neighbors[..8].ToArray());
        Assert.Equal([61, 61, 62, 62, 0, 0], neighbors[^6..].ToArray());
    }

    [Fact]
    public void GetScan_ForTx16DctAdst_ReturnsLibvpxColumnOrder()
    {
        var scan = Vp9ScanTables.GetScan(Vp9TransformSize.Tx16X16, Vp9TransformType.DctAdst);

        Assert.Equal(256, scan.Length);
        Assert.Equal([0, 16, 32, 48, 1, 64], scan[..6].ToArray());
        Assert.Equal([175, 206, 237, 191, 253, 222, 238, 207, 254, 223, 239, 255], scan[^12..].ToArray());
    }

    [Fact]
    public void GetNeighbors_ForTx16DctAdst_ReturnsLibvpxColumnNeighbors()
    {
        var neighbors = Vp9ScanTables.GetNeighbors(Vp9TransformSize.Tx16X16, Vp9TransformType.DctAdst);

        Assert.Equal(514, neighbors.Length);
        Assert.Equal([0, 0, 0, 0, 16, 16, 32, 32], neighbors[..8].ToArray());
        Assert.Equal([239, 239, 0, 0], neighbors[^4..].ToArray());
    }

    [Fact]
    public void GetScan_ForTx32NonDctTypes_ReturnsDefaultOrder()
    {
        var expected = Vp9ScanTables.GetDefaultScan(Vp9TransformSize.Tx32X32).ToArray();

        Assert.Equal(expected, Vp9ScanTables.GetScan(Vp9TransformSize.Tx32X32, Vp9TransformType.AdstDct).ToArray());
        Assert.Equal(expected, Vp9ScanTables.GetScan(Vp9TransformSize.Tx32X32, Vp9TransformType.DctAdst).ToArray());
        Assert.Equal(expected, Vp9ScanTables.GetScan(Vp9TransformSize.Tx32X32, Vp9TransformType.AdstAdst).ToArray());
    }
}
