namespace VPDecoder.Tests;

public sealed class Vp9KeyFrameDecodeStateTests
{
    [Fact]
    public void TryCreate_ForExternalMainFrame_CreatesStateWithoutReturningPixels()
    {
        var packet = ReadRequiredSample(
            "/tmp/vp9-main-frame-0.vp9",
            30398,
            "4c57b8dda880711b174483a27e1691c6c9aa9a6721351d041425f8dafb23b7e9");
        var header = Vp9FrameHeaderParser.Parse(packet);
        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, header, out var compressedHeader, out var compressedDiagnostic), compressedDiagnostic?.Message);
        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var layoutDiagnostic), layoutDiagnostic?.Message);

        var created = Vp9KeyFrameDecodeState.TryCreate(
            header,
            compressedHeader!,
            tileBuffers,
            out var state,
            out var diagnostic);

        Assert.True(created, diagnostic?.Message);
        Assert.NotNull(state);
        Assert.Equal(new Vp9DequantTables(50, 58, 50, 58), state.DequantTables);
        Assert.Equal(8, state.TileBuffers.Count);
        Assert.Equal(5_386_368, state.FrameBuffer.Pixels.Length);
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
