namespace VPDecoder.Tests;

public sealed class Vp9TileGeometryBuilderTests
{
    [Fact]
    public void Build_ForExternalMainFrame_ReturnsLibvpxTileBoundaries()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var header = Vp9FrameHeaderParser.Parse(packet);
        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var diagnostic), diagnostic?.Message);

        var geometries = Vp9TileGeometryBuilder.Build(header, tileBuffers);

        Assert.Equal(8, geometries.Count);
        Assert.Equal([0, 40, 80, 120, 168, 208, 248, 288], geometries.Select(tile => tile.MiColumnStart).ToArray());
        Assert.Equal([40, 80, 120, 168, 208, 248, 288, 332], geometries.Select(tile => tile.MiColumnEnd).ToArray());
        Assert.All(geometries, tile =>
        {
            Assert.Equal(0, tile.MiRowStart);
            Assert.Equal(169, tile.MiRowEnd);
            Assert.Equal(22, tile.SuperblockRows);
        });
        Assert.Equal([5, 5, 5, 6, 5, 5, 5, 6], geometries.Select(tile => tile.SuperblockColumns).ToArray());
        Assert.Equal(924, geometries.Sum(tile => tile.SuperblockCount));
    }

    [Fact]
    public void TryCreate_ForExternalMainFrame_AttachesTileGeometries()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var header = Vp9FrameHeaderParser.Parse(packet);
        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, header, out var compressedHeader, out var compressedDiagnostic), compressedDiagnostic?.Message);
        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var layoutDiagnostic), layoutDiagnostic?.Message);

        Assert.True(Vp9KeyFrameDecodeState.TryCreate(header, compressedHeader!, tileBuffers, out var state, out var diagnostic), diagnostic?.Message);

        Assert.NotNull(state);
        Assert.Equal(8, state.TileGeometries.Count);
        Assert.Equal(1112, state.TileGeometries[0].Buffer.Size);
        Assert.Equal(169, state.TileGeometries[0].MiRows);
        Assert.Equal(40, state.TileGeometries[0].MiColumns);
    }

    private static byte[] ReadRequiredSample(string path, int expectedLength, string expectedSha256)
    {
        Assert.True(File.Exists(path), $"Required VP9 acceptance sample is missing: {path}");
        var packet = File.ReadAllBytes(path);
        Assert.Equal(expectedLength, packet.Length);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet)).ToLowerInvariant();
        Assert.Equal(expectedSha256, hash);
        return packet;
    }
}
