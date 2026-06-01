using System.Buffers.Binary;

namespace VPDecoder;

public static class Vp9FrameLayoutParser
{
    public static bool TryReadTileBuffers(
        ReadOnlySpan<byte> packet,
        Vp9FrameHeader header,
        out IReadOnlyList<Vp9TileBuffer> tileBuffers,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        tileBuffers = [];
        diagnostic = null;

        if (header.HeaderSizeInBytes < 0 || header.FirstPartitionSize <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 frame header has an invalid first partition size.");
            return false;
        }

        var tileDataOffset = header.HeaderSizeInBytes + header.FirstPartitionSize;
        if (tileDataOffset > packet.Length)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile data begins past the packet boundary.");
            return false;
        }

        var tileCount = checked(header.TileInfo.TileColumns * header.TileInfo.TileRows);
        if (tileCount <= 0)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 frame has no tile buffers.");
            return false;
        }

        var position = tileDataOffset;
        var buffers = new Vp9TileBuffer[tileCount];
        for (var i = 0; i < tileCount; i++)
        {
            int? sizeFieldOffset = null;
            int size;
            if (i == tileCount - 1)
            {
                size = packet.Length - position;
            }
            else
            {
                if (position + 4 > packet.Length)
                {
                    diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 tile size table extends past the packet boundary.");
                    return false;
                }

                sizeFieldOffset = position;
                size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(position, 4)));
                position += 4;
            }

            if (size <= 0)
            {
                diagnostic = Vp9DecodeDiagnostic.InvalidPacket($"VP9 tile {i} has invalid size {size}.");
                return false;
            }

            if (size > packet.Length - position)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket($"VP9 tile {i} extends past the packet boundary.");
                return false;
            }

            buffers[i] = new Vp9TileBuffer(i, sizeFieldOffset, position, size);
            position += size;
        }

        if (position != packet.Length)
        {
            diagnostic = Vp9DecodeDiagnostic.InvalidPacket("VP9 tile parsing did not consume the full packet.");
            return false;
        }

        tileBuffers = buffers;
        return true;
    }
}
