namespace VPDecoder.Tests;

public sealed class Vp9ResidualSyntaxTests
{
    [Theory]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X4, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block4X8, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X4, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X8, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block8X16, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X8, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X16, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block16X32, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X16, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X32, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block32X64, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X32, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16)]
    [InlineData(Vp9BlockSize.Block64X64, Vp9TransformSize.Tx32X32, Vp9TransformSize.Tx32X32)]
    public void GetUvTransformSizeForYuv420_UsesLibvpxLookup(
        Vp9BlockSize blockSize,
        Vp9TransformSize yTransformSize,
        Vp9TransformSize expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetUvTransformSizeForYuv420(blockSize, yTransformSize));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 14)]
    [InlineData(8, 16)]
    [InlineData(15, 30)]
    public void GetPlaneLeftContextOffset_ForLuma_UsesLuma4x4Rows(int miRow, int expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetPlaneLeftContextOffset(miRow, plane: 0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(8, 0)]
    [InlineData(10, 2)]
    [InlineData(15, 7)]
    [InlineData(16, 0)]
    public void GetPlaneLeftContextOffset_ForYuv420Chroma_MatchesLibvpxPointerOffset(int miRow, int expected)
    {
        Assert.Equal(expected, Vp9ResidualSyntax.GetPlaneLeftContextOffset(miRow, plane: 1));
        Assert.Equal(expected, Vp9ResidualSyntax.GetPlaneLeftContextOffset(miRow, plane: 2));
    }
}
