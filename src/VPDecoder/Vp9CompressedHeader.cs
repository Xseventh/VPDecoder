namespace VPDecoder;

public sealed record Vp9CompressedHeader(
    Vp9TransformMode TransformMode);

public enum Vp9TransformMode
{
    Only4X4 = 0,
    Allow8X8 = 1,
    Allow16X16 = 2,
    Allow32X32 = 3,
    Select = 4
}
