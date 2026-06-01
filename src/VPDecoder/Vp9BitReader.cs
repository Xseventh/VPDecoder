namespace VPDecoder;

internal ref struct Vp9BitReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _bitOffset;

    public Vp9BitReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _bitOffset = 0;
    }

    public int BytesRead => (_bitOffset + 7) >> 3;

    public bool ReadBit()
    {
        if ((_bitOffset >> 3) >= _buffer.Length)
        {
            throw new Vp9HeaderParseException(Vp9DecodeDiagnostic.TruncatedPacket("VP9 header ended unexpectedly."));
        }

        var bit = (_buffer[_bitOffset >> 3] >> (7 - (_bitOffset & 7))) & 1;
        _bitOffset++;
        return bit != 0;
    }

    public int ReadLiteral(int bits)
    {
        if (bits < 0 || bits > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "Literal bit count must be between 0 and 31.");
        }

        var value = 0;
        for (var bit = bits - 1; bit >= 0; bit--)
        {
            if (ReadBit())
            {
                value |= 1 << bit;
            }
        }

        return value;
    }

    public int ReadSignedLiteral(int bits)
    {
        var value = ReadLiteral(bits);
        return ReadBit() ? -value : value;
    }
}
