namespace VPDecoder;

public static class Vp8FrameHeaderParser
{
    private const byte SyncCode0 = 0x9d;
    private const byte SyncCode1 = 0x01;
    private const byte SyncCode2 = 0x2a;

    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        out Vp8FrameHeader? header,
        out Vp8DecodeDiagnostic? diagnostic)
    {
        header = null;
        diagnostic = null;

        try
        {
            header = Parse(packet);
            return true;
        }
        catch (Vp8HeaderParseException ex)
        {
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    public static Vp8FrameHeader Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            throw new Vp8HeaderParseException(Vp8DecodeDiagnostic.InvalidPacket("VP8 packet is empty."));
        }

        if (packet.Length < 3)
        {
            throw new Vp8HeaderParseException(Vp8DecodeDiagnostic.TruncatedPacket("VP8 frame tag is truncated."));
        }

        var frameTag = packet[0] | (packet[1] << 8) | (packet[2] << 16);
        var frameType = (frameTag & 0x1) == 0 ? Vp8FrameType.KeyFrame : Vp8FrameType.InterFrame;
        var version = (frameTag >> 1) & 0x7;
        var showFrame = ((frameTag >> 4) & 0x1) != 0;
        var firstPartitionSize = frameTag >> 5;

        if (frameType == Vp8FrameType.InterFrame)
        {
            return new Vp8FrameHeader(
                packet.Length,
                HeaderSizeInBytes: 3,
                frameType,
                version,
                showFrame,
                firstPartitionSize,
                SyncCodeValid: false,
                Width: 0,
                Height: 0,
                HorizontalScale: 0,
                VerticalScale: 0);
        }

        if (packet.Length < 10)
        {
            throw new Vp8HeaderParseException(Vp8DecodeDiagnostic.TruncatedPacket("VP8 key-frame uncompressed header is truncated."));
        }

        var syncCodeValid = packet[3] == SyncCode0 && packet[4] == SyncCode1 && packet[5] == SyncCode2;
        if (!syncCodeValid)
        {
            throw new Vp8HeaderParseException(Vp8DecodeDiagnostic.InvalidPacket("Invalid VP8 key-frame sync code."));
        }

        var rawWidth = packet[6] | (packet[7] << 8);
        var rawHeight = packet[8] | (packet[9] << 8);
        var width = rawWidth & 0x3fff;
        var height = rawHeight & 0x3fff;
        if (width <= 0 || height <= 0)
        {
            throw new Vp8HeaderParseException(Vp8DecodeDiagnostic.InvalidPacket("Invalid VP8 key-frame dimensions."));
        }

        return new Vp8FrameHeader(
            packet.Length,
            HeaderSizeInBytes: 10,
            frameType,
            version,
            showFrame,
            firstPartitionSize,
            SyncCodeValid: true,
            width,
            height,
            HorizontalScale: rawWidth >> 14,
            VerticalScale: rawHeight >> 14);
    }
}
