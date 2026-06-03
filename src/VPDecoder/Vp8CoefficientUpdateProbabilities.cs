namespace VPDecoder;

internal static class Vp8CoefficientUpdateProbabilities
{
    public const int BlockTypes = 4;
    public const int CoefficientBands = 8;
    public const int PreviousCoefficientContexts = 3;
    public const int EntropyNodes = 11;

    public const int Count = BlockTypes * CoefficientBands * PreviousCoefficientContexts * EntropyNodes;

    // Flattened from libvpx vp8/common/coefupdateprobs.h in decoder loop order.
    private const string EncodedValues =
        "////////////////////////////////////////////sPb////////////f8fz///////////n9/f////////////T8///////////q/v7///////////3///////////////b+///////////v/f7///////////7//v////////////j+///////////7//7///////////////////////////3+///////////7/v7///////////7//v////////////79//7////////6//7//v////////7/////////////////////////////////////////////////////////2f/////////////h/PH9///+/////+r68fr9//3+//////7////////////f/v7//////////+79/v7///////////j+///////////5/v////////////////////////////3////////////3/v////////////////////////////3+///////////8//////////////////////////////7+///////////9//////////////////////////////79///////////6//////////////7/////////////////////////////////////////////////////////uvv6///////////q+/T+//////////v78/3+//7///////3+///////////s/f7///////////v9/f7+//////////7+///////////+/v7///////////////////////////7////////////+/v////////////7////////////////////////////+////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////+P/////////////6/vz+//////////j++f3///////////39///////////2/f3///////////z++/7+//////////78///////////4/v3///////////3//v7///////////v+///////////1+/7///////////39/v////////////v9///////////8/f7////////////+//////////////z////////////5//7//////////////v/////////////9///////////6///////////////////////////////////////////+////////////////////////////";

    private static readonly byte[] Values = DecodeValues();

    public static byte GetProbability(
        int blockType,
        int coefficientBand,
        int previousCoefficientContext,
        int entropyNode)
    {
        return Values[GetIndex(blockType, coefficientBand, previousCoefficientContext, entropyNode)];
    }

    public static int GetIndex(
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

    private static byte[] DecodeValues()
    {
        var values = Convert.FromBase64String(EncodedValues);
        if (values.Length != Count)
        {
            throw new InvalidOperationException(
                $"VP8 coefficient update probability table has {values.Length} entries; expected {Count}.");
        }

        return values;
    }
}
