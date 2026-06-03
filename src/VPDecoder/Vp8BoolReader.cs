namespace VPDecoder;

internal ref struct Vp8BoolReader
{
    private const int ValueSize = 64;
    private const int LotsOfBits = 0x40000000;

    private static ReadOnlySpan<byte> NormTable =>
    [
        0, 7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    ];

    private readonly ReadOnlySpan<byte> _buffer;
    private ulong _value;
    private uint _range;
    private int _count;
    private int _position;

    public Vp8BoolReader(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            throw new Vp8BoolReaderException(
                Vp8DecodeDiagnostic.TruncatedPacket("VP8 bool reader input is empty."));
        }

        _buffer = buffer;
        _value = 0;
        _range = 255;
        _count = -8;
        _position = 0;

        Fill();
    }

    public bool HasError => _count > ValueSize && _count < LotsOfBits;

    public bool ReadBit()
    {
        return Read(128);
    }

    public bool Read(int probability)
    {
        if (probability is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "VP8 probability must be between 0 and 255.");
        }

        if (_count < 0)
        {
            Fill();
        }

        var split = (uint)((_range * probability + (256 - probability)) >> 8);
        var bigSplit = (ulong)split << (ValueSize - 8);
        var bit = false;
        var range = split;
        var value = _value;

        if (value >= bigSplit)
        {
            range = _range - split;
            value -= bigSplit;
            bit = true;
        }

        var shift = GetNormalizationShift((int)range);
        _range = range << shift;
        _value = value << shift;
        _count -= shift;
        return bit;
    }

    public int ReadLiteral(int bits)
    {
        if (bits is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "Literal bit count must be between 0 and 31.");
        }

        var literal = 0;
        for (var bit = bits - 1; bit >= 0; bit--)
        {
            if (ReadBit())
            {
                literal |= 1 << bit;
            }
        }

        return literal;
    }

    internal static int GetNormalizationShift(int range)
    {
        if (range is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "VP8 bool reader range must be between 0 and 255.");
        }

        return NormTable[range];
    }

    private void Fill()
    {
        var shift = ValueSize - 8 - (_count + 8);
        var bytesLeft = _buffer.Length - _position;
        var bitsLeft = bytesLeft * 8;
        var bitsOver = shift + 8 - bitsLeft;
        var loopEnd = 0;

        if (bitsOver >= 0)
        {
            _count += LotsOfBits;
            loopEnd = bitsOver;
        }

        if (bitsOver >= 0 && bitsLeft == 0)
        {
            return;
        }

        while (shift >= loopEnd)
        {
            _count += 8;
            _value |= (ulong)_buffer[_position] << shift;
            _position++;
            shift -= 8;
        }
    }
}
