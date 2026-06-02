namespace VPDecoder.Tests;

internal static class Vp9TestPackets
{
    public static byte[] CreateOrdinaryInterFramePacket(
        bool errorResilientMode = false,
        bool sizeFromReference = false,
        bool stopAfterSizeReference = true,
        int frameContextIndex = 2,
        int tileInfoWidth = 16)
    {
        var writer = new BitWriter();
        WriteFramePrefix(writer, showFrame: true, errorResilientMode);
        if (!errorResilientMode)
        {
            writer.WriteLiteral(0, 2);
        }

        writer.WriteLiteral(0x05, 8);
        writer.WriteLiteral(0, 3);
        writer.WriteBit(false);
        writer.WriteLiteral(1, 3);
        writer.WriteBit(true);
        writer.WriteLiteral(7, 3);
        writer.WriteBit(false);

        writer.WriteBit(sizeFromReference);
        if (sizeFromReference)
        {
            if (stopAfterSizeReference)
            {
                return writer.ToArray();
            }
        }
        else
        {
            writer.WriteBit(false);
            writer.WriteBit(false);
            WriteFrameSize(writer, width: 16, height: 8);
        }

        writer.WriteBit(true);
        WriteFrameSize(writer, width: 10, height: 6);
        writer.WriteBit(true);
        writer.WriteBit(false);
        writer.WriteLiteral(2, 2);

        WriteFrameContext(writer, errorResilientMode, frameContextIndex);
        WriteLoopFilterQuantSegmentationTileAndPartition(writer, tileInfoWidth);
        return writer.ToArray();
    }

    public static byte[] CreateHiddenProfile0IntraOnlyFramePacket()
    {
        var writer = new BitWriter();
        WriteFramePrefix(writer, showFrame: false, errorResilientMode: false);
        writer.WriteBit(true);
        writer.WriteLiteral(3, 2);
        writer.WriteLiteral(0x49, 8);
        writer.WriteLiteral(0x83, 8);
        writer.WriteLiteral(0x42, 8);
        writer.WriteLiteral(0x80, 8);
        WriteFrameSize(writer, width: 16, height: 8);
        writer.WriteBit(false);
        WriteFrameContext(writer, errorResilientMode: false, frameContextIndex: 1);
        WriteLoopFilterQuantSegmentationTileAndPartition(writer, tileInfoWidth: 16);
        return writer.ToArray();
    }

    private static void WriteFramePrefix(BitWriter writer, bool showFrame, bool errorResilientMode)
    {
        writer.WriteLiteral(2, 2);
        writer.WriteBit(false);
        writer.WriteBit(false);
        writer.WriteBit(false);
        writer.WriteBit(true);
        writer.WriteBit(showFrame);
        writer.WriteBit(errorResilientMode);
    }

    private static void WriteFrameSize(BitWriter writer, int width, int height)
    {
        writer.WriteLiteral(width - 1, 16);
        writer.WriteLiteral(height - 1, 16);
    }

    private static void WriteFrameContext(BitWriter writer, bool errorResilientMode, int frameContextIndex)
    {
        if (!errorResilientMode)
        {
            writer.WriteBit(true);
            writer.WriteBit(true);
        }

        writer.WriteLiteral(frameContextIndex, 2);
    }

    private static void WriteLoopFilterQuantSegmentationTileAndPartition(BitWriter writer, int tileInfoWidth)
    {
        writer.WriteLiteral(0, 6);
        writer.WriteLiteral(0, 3);
        writer.WriteBit(false);
        writer.WriteLiteral(1, 8);
        writer.WriteBit(false);
        writer.WriteBit(false);
        writer.WriteBit(false);
        writer.WriteBit(false);
        WriteTileInfo(writer, tileInfoWidth);
        writer.WriteLiteral(1, 16);
    }

    private static void WriteTileInfo(BitWriter writer, int width)
    {
        var miColumns = AlignPowerOfTwo(width, 3) >> 3;
        var superblockColumns = AlignPowerOfTwo(miColumns, 3) >> 3;

        var minLog2TileColumns = 0;
        while ((64 << minLog2TileColumns) < superblockColumns)
        {
            minLog2TileColumns++;
        }

        var maxLog2TileColumns = 1;
        while ((superblockColumns >> maxLog2TileColumns) >= 4)
        {
            maxLog2TileColumns++;
        }

        maxLog2TileColumns--;
        if (maxLog2TileColumns > minLog2TileColumns)
        {
            writer.WriteBit(false);
        }

        writer.WriteBit(false);
    }

    private static int AlignPowerOfTwo(int value, int n)
    {
        var alignment = (1 << n) - 1;
        return (value + alignment) & ~alignment;
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _currentByte;
        private int _bitsInCurrentByte;

        public void WriteBit(bool bit)
        {
            if (bit)
            {
                _currentByte |= 1 << (7 - _bitsInCurrentByte);
            }

            _bitsInCurrentByte++;
            if (_bitsInCurrentByte == 8)
            {
                FlushCurrentByte();
            }
        }

        public void WriteLiteral(int value, int bits)
        {
            for (var bit = bits - 1; bit >= 0; bit--)
            {
                WriteBit(((value >> bit) & 1) != 0);
            }
        }

        public byte[] ToArray()
        {
            if (_bitsInCurrentByte > 0)
            {
                FlushCurrentByte();
            }

            return [.. _bytes];
        }

        private void FlushCurrentByte()
        {
            _bytes.Add((byte)_currentByte);
            _currentByte = 0;
            _bitsInCurrentByte = 0;
        }
    }
}
