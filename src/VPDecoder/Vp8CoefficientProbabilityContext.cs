namespace VPDecoder;

internal sealed class Vp8CoefficientProbabilityContext
{
    public const int BlockTypes = 4;
    public const int CoefficientBands = 8;
    public const int PreviousCoefficientContexts = 3;
    public const int EntropyNodes = 11;
    public const int Count = BlockTypes * CoefficientBands * PreviousCoefficientContexts * EntropyNodes;

    private const string EncodedDefaults =
        "gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA/Yj+/+TbgICAgIC9gfL/49X/24CAgGp+4/zW0f//gICAAWL4/+zi//+AgIC1he7+3er/moCAgE6GyvfGtP/bgICAAbn5//P/gICAgIC4lvf/7OCAgICAgE1u2P/s5oCAgICAAWX7//H/gICAgICqi/H87NH//4CAgCV0xPPk////gICAAcz+//X/gICAgIDPoPr/7oCAgICAgGZn5//Tq4CAgICAAZj8//D/gICAgICxh/P/6uGAgICAgFCB0//C4ICAgICAAQH/gICAgICAgID2Af+AgICAgICAgP+AgICAgICAgICAxiPt38G7oqCRmz6DLcbdrLDcnfzdAUQvktCVp92i/9+AAZXx/93g//+AgIC4jer93tz/x4CAgFFjtfKwvvnK//+AAYHo/dbF8sT//4BjedL6ycb/yoCAgBdbo/Kqu/fS//+AAcj2/+r/gICAgIBtsvH/5/X//4CAgCyCyf3NwP//gICAAYTv+9vR/6WAgIBeiOH72r7//4CAgBZkrvW6of/HgICAAbb5/+jrgICAgIB8j/H/4+qAgICAgCNNtfvB0//NgICAAZ33/+zn//+AgIB5jev/4eP//4CAgC1jvPvD2f/ggICAAQH7/9X/gICAgIDLAfj//4CAgICAgIkBsf/g/4CAgICA/Qn4+8/Q/8CAgICvDeDzwbn5xv//gEkRq92hs+yn/+qAAV/3/dS3//+AgIDvWvT609H//4CAgJtNw/i8w///gICAARjv+9rb/82AgIDJM9v/xLqAgICAgEUuvu/J2v/kgICAAb/7//+AgICAgIDfpfn/1f+AgICAgI18+P//gICAgICAARD4//+AgICAgIC+JOb/7P+AgICAgJUB/4CAgICAgICAAeL/gICAgICAgID3wP+AgICAgICAgPCA/4CAgICAgICAAYb8//+AgICAgIDVPvr//4CAgICAgDdd/4CAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAyhjV67q/3KDwr/9+Jrboqbjkrv+7gD0uituXsvCq/9iAAXDm+se/95///4CmbeT809f/roCAgCdNouistPWy//+AATTc9sbH+dz//4B8Sr/zt8H63f//gBhHgtuaqvO2//+AAbbh+dvw/+CAgICVluL82M3/q4CAgBxsqvK3wv7f//+AAVHm/MzL/8CAgIB7ZtH3vMT/6YCAgBRfmfOkrf/LgICAAd74/9jVgICAgICor/b8683//4CAgC901//T1P//gICAAXns/dTW//+AgICNVNX8ycr/24CAgCpQoPCiuf/NgICAAQH/gICAgICAgID0Af+AgICAgICAgO4B/4CAgICAgICA";

    private static readonly byte[] DefaultProbabilities = DecodeDefaults();

    private readonly byte[] _probabilities;

    private Vp8CoefficientProbabilityContext(byte[] probabilities)
    {
        _probabilities = probabilities;
    }

    public static Vp8CoefficientProbabilityContext CreateDefault()
    {
        return new Vp8CoefficientProbabilityContext((byte[])DefaultProbabilities.Clone());
    }

    public static Vp8CoefficientProbabilityContext Create(Vp8KeyFrameSyntaxHeader syntaxHeader)
    {
        var context = CreateDefault();
        foreach (var update in syntaxHeader.CoefficientProbabilityUpdates)
        {
            context.SetProbability(
                update.BlockType,
                update.CoefficientBand,
                update.PreviousCoefficientContext,
                update.EntropyNode,
                update.Probability);
        }

        return context;
    }

    public ReadOnlySpan<byte> GetProbabilities(int blockType, int coefficientBand, int previousCoefficientContext)
    {
        return _probabilities.AsSpan(
            GetIndex(blockType, coefficientBand, previousCoefficientContext, entropyNode: 0),
            EntropyNodes);
    }

    public byte GetProbability(int blockType, int coefficientBand, int previousCoefficientContext, int entropyNode)
    {
        return _probabilities[GetIndex(blockType, coefficientBand, previousCoefficientContext, entropyNode)];
    }

    private void SetProbability(
        int blockType,
        int coefficientBand,
        int previousCoefficientContext,
        int entropyNode,
        byte probability)
    {
        _probabilities[GetIndex(blockType, coefficientBand, previousCoefficientContext, entropyNode)] = probability;
    }

    private static int GetIndex(
        int blockType,
        int coefficientBand,
        int previousCoefficientContext,
        int entropyNode)
    {
        if (blockType is < 0 or >= BlockTypes)
        {
            throw new ArgumentOutOfRangeException(nameof(blockType));
        }

        if (coefficientBand is < 0 or >= CoefficientBands)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficientBand));
        }

        if (previousCoefficientContext is < 0 or >= PreviousCoefficientContexts)
        {
            throw new ArgumentOutOfRangeException(nameof(previousCoefficientContext));
        }

        if (entropyNode is < 0 or >= EntropyNodes)
        {
            throw new ArgumentOutOfRangeException(nameof(entropyNode));
        }

        return (((blockType * CoefficientBands + coefficientBand) * PreviousCoefficientContexts + previousCoefficientContext) *
            EntropyNodes) + entropyNode;
    }

    private static byte[] DecodeDefaults()
    {
        var values = Convert.FromBase64String(EncodedDefaults);
        if (values.Length != Count)
        {
            throw new InvalidOperationException(
                $"VP8 coefficient probability table has {values.Length} entries; expected {Count}.");
        }

        return values;
    }
}
