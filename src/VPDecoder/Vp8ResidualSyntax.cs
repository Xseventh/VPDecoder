namespace VPDecoder;

internal static class Vp8ResidualSyntax
{
    private static ReadOnlySpan<byte> CoefficientBands =>
    [
        0, 1, 2, 3, 6, 4, 5, 6,
        6, 6, 6, 6, 6, 6, 6, 7,
        0
    ];

    private static ReadOnlySpan<byte> ZigZag =>
    [
        0, 1, 4, 8, 5, 2, 3, 6,
        9, 12, 13, 10, 7, 11, 14, 15
    ];

    private static ReadOnlySpan<byte> Category1 => [159];
    private static ReadOnlySpan<byte> Category2 => [165, 145];
    private static ReadOnlySpan<byte> Category3 => [173, 148, 140];
    private static ReadOnlySpan<byte> Category4 => [176, 155, 140, 135];
    private static ReadOnlySpan<byte> Category5 => [180, 157, 141, 134, 130];
    private static ReadOnlySpan<byte> Category6 => [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129];

    public static Vp8CoefficientBlock ReadBlock(
        ref Vp8BoolReader reader,
        Vp8CoefficientProbabilityContext probabilities,
        int blockType,
        int initialCoefficientContext,
        int startCoefficient)
    {
        if (initialCoefficientContext is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCoefficientContext),
                "VP8 coefficient context must be between 0 and 2.");
        }

        if (startCoefficient is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startCoefficient),
                "VP8 residual block reader currently supports start coefficient 0 or 1.");
        }

        var coefficients = new int[16];
        var coefficientIndex = startCoefficient;
        var nodeProbabilities = probabilities.GetProbabilities(
            blockType,
            CoefficientBands[coefficientIndex],
            initialCoefficientContext);
        if (!reader.Read(nodeProbabilities[0]))
        {
            return new Vp8CoefficientBlock(Eob: 0, coefficients);
        }

        while (true)
        {
            coefficientIndex++;
            if (!reader.Read(nodeProbabilities[1]))
            {
                nodeProbabilities = probabilities.GetProbabilities(
                    blockType,
                    CoefficientBands[coefficientIndex],
                    previousCoefficientContext: 0);
            }
            else
            {
                var coefficient = ReadNonZeroCoefficient(ref reader, nodeProbabilities);
                coefficients[ZigZag[coefficientIndex - 1]] = ReadSigned(ref reader, coefficient);
                nodeProbabilities = probabilities.GetProbabilities(
                    blockType,
                    CoefficientBands[coefficientIndex],
                    previousCoefficientContext: coefficient == 1 ? 1 : 2);

                if (coefficientIndex == 16 || !reader.Read(nodeProbabilities[0]))
                {
                    return new Vp8CoefficientBlock(coefficientIndex, coefficients);
                }
            }

            if (coefficientIndex == 16)
            {
                return new Vp8CoefficientBlock(16, coefficients);
            }
        }
    }

    private static int ReadNonZeroCoefficient(ref Vp8BoolReader reader, ReadOnlySpan<byte> probabilities)
    {
        if (!reader.Read(probabilities[2]))
        {
            return 1;
        }

        if (!reader.Read(probabilities[3]))
        {
            if (!reader.Read(probabilities[4]))
            {
                return 2;
            }

            return 3 + (reader.Read(probabilities[5]) ? 1 : 0);
        }

        if (!reader.Read(probabilities[6]))
        {
            if (!reader.Read(probabilities[7]))
            {
                return 5 + (reader.Read(Category1[0]) ? 1 : 0);
            }

            var value = 7 + (2 * (reader.Read(Category2[0]) ? 1 : 0));
            return value + (reader.Read(Category2[1]) ? 1 : 0);
        }

        var bit1 = reader.Read(probabilities[8]) ? 1 : 0;
        var bit0 = reader.Read(probabilities[9 + bit1]) ? 1 : 0;
        var category = (2 * bit1) + bit0;
        return category switch
        {
            0 => 11 + ReadCoefficientCategory(ref reader, Category3),
            1 => 19 + ReadCoefficientCategory(ref reader, Category4),
            2 => 35 + ReadCoefficientCategory(ref reader, Category5),
            _ => 67 + ReadCoefficientCategory(ref reader, Category6)
        };
    }

    private static int ReadCoefficientCategory(ref Vp8BoolReader reader, ReadOnlySpan<byte> categoryProbabilities)
    {
        var value = 0;
        foreach (var probability in categoryProbabilities)
        {
            value += value + (reader.Read(probability) ? 1 : 0);
        }

        return value;
    }

    private static int ReadSigned(ref Vp8BoolReader reader, int value)
    {
        return reader.ReadBit() ? -value : value;
    }
}

internal sealed record Vp8CoefficientBlock(
    int Eob,
    int[] Coefficients);
