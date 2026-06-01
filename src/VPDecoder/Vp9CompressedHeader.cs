namespace VPDecoder;

public sealed record Vp9CompressedHeader(
    Vp9TransformMode TransformMode,
    Vp9FrameContext FrameContext,
    int TxProbabilityUpdateCount,
    int CoefficientProbabilityUpdateCount,
    int SkipProbabilityUpdateCount);

public enum Vp9TransformMode
{
    Only4X4 = 0,
    Allow8X8 = 1,
    Allow16X16 = 2,
    Allow32X32 = 3,
    Select = 4
}
