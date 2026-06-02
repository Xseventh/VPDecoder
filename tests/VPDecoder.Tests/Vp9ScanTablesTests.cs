namespace VPDecoder.Tests;

public sealed class Vp9ScanTablesTests
{
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
}
