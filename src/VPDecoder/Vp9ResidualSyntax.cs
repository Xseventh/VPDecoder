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

public sealed record Vp9CoefficientBlockProbe(
    int TileIndex,
    Vp9TransformSize TransformSize,
    int PlaneType,
    int ReferenceType,
    int InitialCoefficientContext,
    int Eob,
    int NonZeroCount,
    int FirstNonZeroRasterIndex,
    int LastNonZeroRasterIndex,
    int[] DequantizedCoefficients,
    string CoefficientsSha256);

public sealed record Vp9CoefficientBlockGroupProbe(
    int TileIndex,
    Vp9BlockSize BlockSize,
    Vp9TransformSize TransformSize,
    IReadOnlyList<Vp9CoefficientBlockProbe> Blocks);

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

        if (modeInfo.Skip)
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

        var coefficient = ReadNonZeroCoefficientValue(ref reader, probabilities[probabilityIndex + 2]);
        var dequantizedValue = Dequantize(coefficient.TokenValue, state.DequantTables.YDc, modeInfo.TransformSize);
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
            GetToken(coefficient.TokenValue),
            dequantizedValue);
    }

    public static Vp9CoefficientBlockProbe ReadFirstYCoefficientBlock(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9ModeInfoProbe modeInfo,
        int initialCoefficientContext = 0)
    {
        const int planeType = 0;
        const int referenceType = 0;

        if (initialCoefficientContext is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCoefficientContext),
                "VP9 coefficient entropy context must be between 0 and 2.");
        }

        if (modeInfo.TransformSize != Vp9TransformSize.Tx32X32)
        {
            throw new NotSupportedException("VP9 first Y coefficient block probe currently supports only TX32 blocks.");
        }

        var maxEob = Vp9ScanTables.GetMaximumEob(modeInfo.TransformSize);
        var coefficients = new int[maxEob];
        if (modeInfo.Skip)
        {
            return CreateBlockProbe(
                modeInfo,
                planeType,
                referenceType,
                initialCoefficientContext,
                eob: 0,
                coefficients);
        }

        var probabilities = state.CompressedHeader.FrameContext.CoefficientProbabilities;
        var scan = Vp9ScanTables.GetDefaultScan(modeInfo.TransformSize);
        var neighbors = Vp9ScanTables.GetDefaultNeighbors(modeInfo.TransformSize);
        var tokenCache = new byte[maxEob];
        var coefficientIndex = 0;
        var coefficientContext = initialCoefficientContext;
        var dq = state.DequantTables.YDc;

        while (coefficientIndex < maxEob)
        {
            var band = Vp9ScanTables.GetBand(modeInfo.TransformSize, coefficientIndex);
            var probabilityIndex = state.CompressedHeader.FrameContext.GetCoefficientProbabilityIndex(
                (int)modeInfo.TransformSize,
                planeType,
                referenceType,
                band,
                coefficientContext,
                0);

            if (!reader.Read(probabilities[probabilityIndex]))
            {
                break;
            }

            while (!reader.Read(probabilities[probabilityIndex + 1]))
            {
                tokenCache[scan[coefficientIndex]] = 0;
                coefficientIndex++;
                if (coefficientIndex >= maxEob)
                {
                    return CreateBlockProbe(
                        modeInfo,
                        planeType,
                        referenceType,
                        initialCoefficientContext,
                        coefficientIndex,
                        coefficients);
                }

                coefficientContext = Vp9ScanTables.GetCoefficientContext(neighbors, tokenCache, coefficientIndex);
                band = Vp9ScanTables.GetBand(modeInfo.TransformSize, coefficientIndex);
                probabilityIndex = state.CompressedHeader.FrameContext.GetCoefficientProbabilityIndex(
                    (int)modeInfo.TransformSize,
                    planeType,
                    referenceType,
                    band,
                    coefficientContext,
                    0);
                dq = state.DequantTables.YAc;
            }

            var coefficient = ReadNonZeroCoefficientValue(ref reader, probabilities[probabilityIndex + 2]);
            var dequantizedValue = Dequantize(coefficient.TokenValue, dq, modeInfo.TransformSize);
            if (reader.ReadBit())
            {
                dequantizedValue = -dequantizedValue;
            }

            var rasterIndex = scan[coefficientIndex];
            coefficients[rasterIndex] = dequantizedValue;
            tokenCache[rasterIndex] = coefficient.TokenCacheValue;
            coefficientIndex++;
            if (coefficientIndex < maxEob)
            {
                coefficientContext = Vp9ScanTables.GetCoefficientContext(neighbors, tokenCache, coefficientIndex);
                dq = state.DequantTables.YAc;
            }
        }

        return CreateBlockProbe(
            modeInfo,
            planeType,
            referenceType,
            initialCoefficientContext,
            coefficientIndex,
            coefficients);
    }

    public static Vp9CoefficientBlockGroupProbe ReadFirstYCoefficientBlocks(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9ModeInfoProbe modeInfo)
    {
        if (modeInfo.TransformSize != Vp9TransformSize.Tx32X32)
        {
            throw new NotSupportedException("VP9 first Y coefficient block group probe currently supports only TX32 blocks.");
        }

        var gridSize = GetFirstLeafTx32GridSize(modeInfo.BlockSize);
        var blocks = new Vp9CoefficientBlockProbe[gridSize * gridSize];
        var nonZeroContexts = new bool[gridSize, gridSize];
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                var blockIndex = (row * gridSize) + column;
                if (modeInfo.Skip)
                {
                    blocks[blockIndex] = CreateBlockProbe(
                        modeInfo,
                        planeType: 0,
                        referenceType: 0,
                        initialCoefficientContext: 0,
                        eob: 0,
                        new int[Vp9ScanTables.GetMaximumEob(modeInfo.TransformSize)]);
                    continue;
                }

                var context = (row > 0 && nonZeroContexts[row - 1, column] ? 1 : 0) +
                    (column > 0 && nonZeroContexts[row, column - 1] ? 1 : 0);
                var block = ReadFirstYCoefficientBlock(ref reader, state, modeInfo, context);
                blocks[blockIndex] = block;
                nonZeroContexts[row, column] = block.Eob > 0;
            }
        }

        return new Vp9CoefficientBlockGroupProbe(
            modeInfo.TileIndex,
            modeInfo.BlockSize,
            modeInfo.TransformSize,
            blocks);
    }

    private static Vp9DecodedCoefficient ReadNonZeroCoefficientValue(ref Vp9BoolReader reader, int pivotProbability)
    {
        if (!reader.Read(pivotProbability))
        {
            return new Vp9DecodedCoefficient(1, 1);
        }

        if (!Vp9CoefficientProbabilities.TryGetPareto8Full(pivotProbability, out var pareto))
        {
            throw new NotSupportedException($"VP9 Pareto coefficient probability pivot {pivotProbability} is invalid.");
        }

        if (reader.Read(pareto[0]))
        {
            if (reader.Read(pareto[3]))
            {
                if (reader.Read(pareto[5]))
                {
                    if (reader.Read(pareto[7]))
                    {
                        return new Vp9DecodedCoefficient(
                            67 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category6),
                            5);
                    }

                    return new Vp9DecodedCoefficient(
                        35 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category5),
                        5);
                }

                if (reader.Read(pareto[6]))
                {
                    return new Vp9DecodedCoefficient(
                        19 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category4),
                        5);
                }

                return new Vp9DecodedCoefficient(
                    11 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category3),
                    5);
            }

            if (reader.Read(pareto[4]))
            {
                return new Vp9DecodedCoefficient(
                    7 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category2),
                    4);
            }

            return new Vp9DecodedCoefficient(
                5 + ReadCoefficientCategory(ref reader, Vp9CoefficientProbabilities.Category1),
                4);
        }

        if (reader.Read(pareto[1]))
        {
            return new Vp9DecodedCoefficient(
                3 + (reader.Read(pareto[2]) ? 1 : 0),
                3);
        }

        return new Vp9DecodedCoefficient(2, 2);
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

    private static int Dequantize(int tokenValue, int dequant, Vp9TransformSize transformSize)
    {
        var value = tokenValue * dequant;
        return transformSize == Vp9TransformSize.Tx32X32 ? value >> 1 : value;
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

    private static Vp9CoefficientBlockProbe CreateBlockProbe(
        Vp9ModeInfoProbe modeInfo,
        int planeType,
        int referenceType,
        int initialCoefficientContext,
        int eob,
        int[] coefficients)
    {
        var nonZeroCount = 0;
        var firstNonZero = -1;
        var lastNonZero = -1;
        for (var i = 0; i < coefficients.Length; i++)
        {
            if (coefficients[i] == 0)
            {
                continue;
            }

            nonZeroCount++;
            if (firstNonZero < 0)
            {
                firstNonZero = i;
            }

            lastNonZero = i;
        }

        return new Vp9CoefficientBlockProbe(
            modeInfo.TileIndex,
            modeInfo.TransformSize,
            planeType,
            referenceType,
            initialCoefficientContext,
            eob,
            nonZeroCount,
            firstNonZero,
            lastNonZero,
            coefficients,
            HashCoefficients(coefficients));
    }

    private static string HashCoefficients(ReadOnlySpan<int> coefficients)
    {
        var bytes = new byte[coefficients.Length * sizeof(int)];
        for (var i = 0; i < coefficients.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(i * sizeof(int), sizeof(int)),
                coefficients[i]);
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static int GetFirstLeafTx32GridSize(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block32X32 => 1,
            Vp9BlockSize.Block64X64 => 2,
            _ => throw new NotSupportedException(
                $"VP9 first Y coefficient block group probe does not support block size {blockSize}.")
        };
    }

    private readonly record struct Vp9DecodedCoefficient(int TokenValue, byte TokenCacheValue);
}
