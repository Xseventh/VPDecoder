namespace VPDecoder.Tests;

public sealed class Vp8BoolReaderTests
{
    [Fact]
    public void Constructor_WhenInputIsEmpty_ThrowsTruncatedPacketDiagnostic()
    {
        var ex = Assert.Throws<Vp8BoolReaderException>(() => _ = new Vp8BoolReader([]));

        Assert.Equal(Vp8DecodeDiagnosticCode.TruncatedPacket, ex.Diagnostic.Code);
    }

    [Fact]
    public void ReadBit_AllZeroInput_ReturnsFalseUntilEndOfStream()
    {
        var reader = new Vp8BoolReader([0x00, 0x00]);

        for (var i = 0; i < 32; i++)
        {
            Assert.False(reader.ReadBit());
        }

        Assert.True(reader.HasError);
    }

    [Fact]
    public void ReadBit_AllOneInput_DoesNotRequireVp9MarkerBit()
    {
        var reader = new Vp8BoolReader([0xff]);

        Assert.True(reader.ReadBit());
    }

    [Fact]
    public void ReadLiteral_AllZeroInput_ReturnsZero()
    {
        var reader = new Vp8BoolReader([0x00, 0x00]);

        Assert.Equal(0, reader.ReadLiteral(8));
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 6)]
    [InlineData(32, 2)]
    [InlineData(64, 1)]
    [InlineData(120, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 0)]
    [InlineData(255, 0)]
    public void GetNormalizationShift_MatchesLibvpxNormTable(int range, int expected)
    {
        Assert.Equal(expected, Vp8BoolReader.GetNormalizationShift(range));
    }
}
