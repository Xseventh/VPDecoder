namespace VPDecoder.Tests;

public sealed class Vp9FrameLayoutParserTests
{
    [Fact]
    public void TryReadTileBuffers_ExternalMainFrameSample_ReturnsExpectedTileSizesWhenPresent()
    {
        var path = "/tmp/vp9-main-frame-0.vp9";
        if (!File.Exists(path))
        {
            return;
        }

        var packet = File.ReadAllBytes(path);
        var header = Vp9FrameHeaderParser.Parse(packet);

        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var diagnostic), diagnostic?.Message);
        Assert.Equal([1112, 3397, 3933, 5933, 5968, 4215, 3777, 1697], tileBuffers.Select(tile => tile.Size).ToArray());
        Assert.Equal(338, tileBuffers[0].SizeFieldOffset);
        Assert.Equal(packet.Length, tileBuffers[^1].DataOffset + tileBuffers[^1].Size);
    }

    [Fact]
    public void TryReadTileBuffers_ExternalAlphaFrameSample_ReturnsExpectedTileSizesWhenPresent()
    {
        var path = "/tmp/vp9-alpha-frame-0.vp9";
        if (!File.Exists(path))
        {
            return;
        }

        var packet = File.ReadAllBytes(path);
        var header = Vp9FrameHeaderParser.Parse(packet);

        Assert.True(Vp9FrameLayoutParser.TryReadTileBuffers(packet, header, out var tileBuffers, out var diagnostic), diagnostic?.Message);
        Assert.Equal([129, 774, 862, 1293, 1153, 811, 751, 272], tileBuffers.Select(tile => tile.Size).ToArray());
        Assert.Equal(160, tileBuffers[0].SizeFieldOffset);
        Assert.Equal(packet.Length, tileBuffers[^1].DataOffset + tileBuffers[^1].Size);
    }

    [Fact]
    public void TryReadTileBuffers_WhenCompressedHeaderExtendsPastPacket_ReturnsTruncatedPacketDiagnostic()
    {
        var header = Vp9FrameHeaderParser.Parse(CreatePaddedMainFramePacket());
        var truncated = new byte[header.HeaderSizeInBytes + header.FirstPartitionSize - 1];

        Assert.False(Vp9FrameLayoutParser.TryReadTileBuffers(truncated, header, out _, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.TruncatedPacket, diagnostic.Code);
    }

    private static byte[] CreatePaddedMainFramePacket()
    {
        byte[] header =
        [
            0x82, 0x49, 0x83, 0x42, 0x10, 0xa5, 0xf0, 0x54, 0x76,
            0x04, 0x38, 0x24, 0x1c, 0x18, 0x66, 0x1c, 0x02, 0x80
        ];
        var packet = new byte[header.Length + 320];
        header.CopyTo(packet, 0);
        return packet;
    }
}
