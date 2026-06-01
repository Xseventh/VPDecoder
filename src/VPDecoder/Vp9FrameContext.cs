namespace VPDecoder;

public sealed class Vp9FrameContext
{
    private const int CoefficientProbabilityCount =
        Vp9FrameContextConstants.TransformSizes *
        Vp9FrameContextConstants.PlaneTypes *
        Vp9FrameContextConstants.ReferenceTypes *
        Vp9FrameContextConstants.CoefficientBands *
        Vp9FrameContextConstants.CoefficientContexts *
        Vp9FrameContextConstants.UnconstrainedNodes;

    private Vp9FrameContext(
        Vp9TxProbabilities txProbabilities,
        byte[] skipProbabilities,
        byte[] coefficientProbabilities)
    {
        TxProbabilities = txProbabilities;
        SkipProbabilities = skipProbabilities;
        CoefficientProbabilities = coefficientProbabilities;
    }

    public Vp9TxProbabilities TxProbabilities { get; }

    public byte[] SkipProbabilities { get; }

    public byte[] CoefficientProbabilities { get; }

    public static Vp9FrameContext CreateDefault()
    {
        var coefficientProbabilities = new byte[CoefficientProbabilityCount];
        Array.Fill(coefficientProbabilities, (byte)128);
        return new Vp9FrameContext(
            Vp9TxProbabilities.CreateDefault(),
            [192, 128, 64],
            coefficientProbabilities);
    }

    public int GetCoefficientProbabilityIndex(
        int transformSize,
        int planeType,
        int referenceType,
        int coefficientBand,
        int coefficientContext,
        int node)
    {
        return (((((transformSize * Vp9FrameContextConstants.PlaneTypes) + planeType) *
                    Vp9FrameContextConstants.ReferenceTypes + referenceType) *
                    Vp9FrameContextConstants.CoefficientBands + coefficientBand) *
                    Vp9FrameContextConstants.CoefficientContexts + coefficientContext) *
                    Vp9FrameContextConstants.UnconstrainedNodes + node;
    }
}

public sealed class Vp9TxProbabilities
{
    private Vp9TxProbabilities(byte[,] thirtyTwoByThirtyTwo, byte[,] sixteenBySixteen, byte[,] eightByEight)
    {
        ThirtyTwoByThirtyTwo = thirtyTwoByThirtyTwo;
        SixteenBySixteen = sixteenBySixteen;
        EightByEight = eightByEight;
    }

    public byte[,] ThirtyTwoByThirtyTwo { get; }

    public byte[,] SixteenBySixteen { get; }

    public byte[,] EightByEight { get; }

    public static Vp9TxProbabilities CreateDefault()
    {
        return new Vp9TxProbabilities(
            new byte[,]
            {
                { 3, 136, 37 },
                { 5, 52, 13 }
            },
            new byte[,]
            {
                { 20, 152 },
                { 15, 101 }
            },
            new byte[,]
            {
                { 100 },
                { 66 }
            });
    }
}

internal static class Vp9FrameContextConstants
{
    public const int TransformSizes = 4;
    public const int TransformSizeContexts = 2;
    public const int PlaneTypes = 2;
    public const int ReferenceTypes = 2;
    public const int CoefficientBands = 6;
    public const int CoefficientContexts = 6;
    public const int UnconstrainedNodes = 3;
    public const int SkipContexts = 3;
}
