namespace VPDecoder;

public enum Vp9CoefficientToken
{
    Zero = 0,
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Category1 = 5,
    Category2 = 6,
    Category3 = 7,
    Category4 = 8,
    Category5 = 9,
    Category6 = 10,
    EndOfBlock = 11
}

public sealed record Vp9CoefficientTokenProbe(
    int TileIndex,
    Vp9TransformSize TransformSize,
    int PlaneType,
    int ReferenceType,
    int CoefficientBand,
    int CoefficientContext,
    Vp9CoefficientToken Token,
    int? DequantizedValue);

internal static class Vp9ResidualSyntax
{
    public static Vp9CoefficientTokenProbe ReadFirstYCoefficientToken(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9ModeInfoProbe modeInfo)
    {
        const int planeType = 0;
        const int referenceType = 0;
        const int coefficientBand = 0;
        const int coefficientContext = 0;
        var probabilityIndex = state.CompressedHeader.FrameContext.GetCoefficientProbabilityIndex(
            (int)modeInfo.TransformSize,
            planeType,
            referenceType,
            coefficientBand,
            coefficientContext,
            0);
        var probabilities = state.CompressedHeader.FrameContext.CoefficientProbabilities;

        if (!reader.Read(probabilities[probabilityIndex]))
        {
            return new Vp9CoefficientTokenProbe(
                modeInfo.TileIndex,
                modeInfo.TransformSize,
                planeType,
                referenceType,
                coefficientBand,
                coefficientContext,
                Vp9CoefficientToken.EndOfBlock,
                null);
        }

        if (!reader.Read(probabilities[probabilityIndex + 1]))
        {
            return new Vp9CoefficientTokenProbe(
                modeInfo.TileIndex,
                modeInfo.TransformSize,
                planeType,
                referenceType,
                coefficientBand,
                coefficientContext,
                Vp9CoefficientToken.Zero,
                0);
        }

        var tokenValue = 1;
        if (reader.Read(probabilities[probabilityIndex + 2]))
        {
            if (!Vp9CoefficientProbabilities.TryGetPareto8Full(probabilities[probabilityIndex + 2], out var pareto))
            {
                throw new NotSupportedException(
                    $"VP9 Pareto coefficient probability pivot {probabilities[probabilityIndex + 2]} is not supported by this syntax probe yet.");
            }

            if (reader.Read(pareto[0]))
            {
                if (reader.Read(pareto[3]))
                {
                    if (reader.Read(pareto[5]))
                    {
                        if (reader.Read(pareto[7]))
                        {
                            tokenValue = 67 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category6);
                        }
                        else
                        {
                            tokenValue = 35 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category5);
                        }
                    }
                    else if (reader.Read(pareto[6]))
                    {
                        tokenValue = 19 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category4);
                    }
                    else
                    {
                        tokenValue = 11 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category3);
                    }
                }
                else if (reader.Read(pareto[4]))
                {
                    tokenValue = 7 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category2);
                }
                else
                {
                    tokenValue = 5 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category1);
                }
            }
            else if (reader.Read(pareto[1]))
            {
                tokenValue = 3 + (reader.Read(pareto[2]) ? 1 : 0);
            }
            else
            {
                tokenValue = 2;
            }
        }

        var dequantizedValue = modeInfo.TransformSize == Vp9TransformSize.Tx32X32
            ? (tokenValue * state.DequantTables.YDc) >> 1
            : tokenValue * state.DequantTables.YDc;
        if (reader.ReadBit())
        {
            dequantizedValue = -dequantizedValue;
        }

        return new Vp9CoefficientTokenProbe(
            modeInfo.TileIndex,
            modeInfo.TransformSize,
            planeType,
            referenceType,
            coefficientBand,
            coefficientContext,
            GetToken(tokenValue),
            dequantizedValue);
    }

    private static int ReadCoefficientCategory(ref Vp9BoolReader reader, ReadOnlySpan<byte> probabilities)
    {
        var value = 0;
        for (var bit = 0; bit < probabilities.Length; bit++)
        {
            value = (value << 1) | (reader.Read(probabilities[bit]) ? 1 : 0);
        }

        return value;
    }

    private static Vp9CoefficientToken GetToken(int tokenValue)
    {
        return tokenValue switch
        {
            1 => Vp9CoefficientToken.One,
            2 => Vp9CoefficientToken.Two,
            3 => Vp9CoefficientToken.Three,
            4 => Vp9CoefficientToken.Four,
            >= 5 and <= 6 => Vp9CoefficientToken.Category1,
            >= 7 and <= 10 => Vp9CoefficientToken.Category2,
            >= 11 and <= 18 => Vp9CoefficientToken.Category3,
            >= 19 and <= 34 => Vp9CoefficientToken.Category4,
            >= 35 and <= 66 => Vp9CoefficientToken.Category5,
            _ => Vp9CoefficientToken.Category6
        };
    }
}
