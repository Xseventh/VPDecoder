namespace VPDecoder.Tests;

public sealed class Vp9CompressedHeaderParserTests
{
    [Fact]
    public void TryParse_ExternalMainFrameSample_ReturnsTransformModeWhenPresent()
    {
        var path = "/tmp/vp9-main-frame-0.vp9";
        if (!File.Exists(path))
        {
            return;
        }

        var packet = File.ReadAllBytes(path);
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);

        Assert.True(Vp9CompressedHeaderParser.TryParse(packet, frameHeader, out var compressedHeader, out var diagnostic), diagnostic?.Message);
        Assert.NotNull(compressedHeader);
        Assert.Equal(Vp9TransformMode.Select, compressedHeader.TransformMode);
    }

    [Fact]
    public void TryParse_WhenBoolReaderMarkerIsInvalid_ReturnsInvalidPacketDiagnostic()
    {
        var packet = CreatePaddedMainFramePacket();
        var frameHeader = Vp9FrameHeaderParser.Parse(packet);
        packet[frameHeader.HeaderSizeInBytes] = 0xff;

        Assert.False(Vp9CompressedHeaderParser.TryParse(packet, frameHeader, out _, out var diagnostic));

        Assert.NotNull(diagnostic);
        Assert.Equal(Vp9DecodeDiagnosticCode.InvalidPacket, diagnostic.Code);
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
