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
    Vp9TransformType TransformType,
    int Row4,
    int Column4,
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
        int initialCoefficientContext = 0,
        int row4 = 0,
        int column4 = 0)
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

        return ReadCoefficientBlock(
            ref reader,
            state,
            modeInfo,
            modeInfo.TransformSize,
            GetTransformType(modeInfo, plane: 0, modeInfo.TransformSize, row4, column4),
            row4,
            column4,
            planeType,
            referenceType,
            state.DequantTables.YDc,
            state.DequantTables.YAc,
            initialCoefficientContext);
    }

    public static Vp9CoefficientBlockGroupProbe ReadPlaneCoefficientBlocks(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9ModeInfoProbe modeInfo,
        Vp9CoefficientEntropyContext entropyContext,
        int plane)
    {
        if (plane is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.");
        }

        var transformSize = plane == 0
            ? modeInfo.TransformSize
            : GetUvTransformSize(modeInfo.BlockSize, modeInfo.TransformSize);
        var width4 = GetVisiblePlaneWidthIn4x4Blocks(state.Header, modeInfo.BlockSize, modeInfo.MiColumn, plane);
        var height4 = GetVisiblePlaneHeightIn4x4Blocks(state.Header, modeInfo.BlockSize, modeInfo.MiRow, plane);
        var step = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(transformSize);
        var blocks = new List<Vp9CoefficientBlockProbe>();

        ThrowIfUnsupportedResidualGrid(modeInfo, plane, transformSize);

        if (modeInfo.Skip)
        {
            entropyContext.ClearBlock(
                plane,
                GetPlaneX4(modeInfo.MiColumn, plane),
                GetPlaneY4(modeInfo.MiRow, plane),
                width4,
                height4);
            for (var row = 0; row < height4; row += step)
            {
                for (var column = 0; column < width4; column += step)
                {
                    var transformType = GetTransformType(modeInfo, plane, transformSize, row, column);
                    blocks.Add(CreateBlockProbe(
                        modeInfo,
                        transformSize,
                        transformType,
                        row,
                        column,
                        plane == 0 ? 0 : 1,
                        referenceType: 0,
                        initialCoefficientContext: 0,
                        eob: 0,
                        new int[Vp9ScanTables.GetMaximumEob(transformSize)]));
                }
            }

            return new Vp9CoefficientBlockGroupProbe(modeInfo.TileIndex, modeInfo.BlockSize, transformSize, blocks);
        }

        var dc = plane == 0 ? state.DequantTables.YDc : state.DequantTables.UvDc;
        var ac = plane == 0 ? state.DequantTables.YAc : state.DequantTables.UvAc;
        var planeType = plane == 0 ? 0 : 1;
        var originX4 = GetPlaneX4(modeInfo.MiColumn, plane);
        var originY4 = GetPlaneY4(modeInfo.MiRow, plane);
        for (var row = 0; row < height4; row += step)
        {
            for (var column = 0; column < width4; column += step)
            {
                var x4 = originX4 + column;
                var y4 = originY4 + row;
                var context = entropyContext.GetInitialContext(plane, x4, y4, transformSize);
                var transformType = GetTransformType(modeInfo, plane, transformSize, row, column);
                var block = ReadCoefficientBlock(
                    ref reader,
                    state,
                    modeInfo,
                    transformSize,
                    transformType,
                    row,
                    column,
                    planeType,
                    referenceType: 0,
                    dc,
                    ac,
                    context);
                ThrowIfUnsupportedResidualBlock(modeInfo, plane, row, column, block);
                blocks.Add(block);
                entropyContext.SetTransformContext(plane, x4, y4, transformSize, block.Eob > 0);
            }
        }

        return new Vp9CoefficientBlockGroupProbe(modeInfo.TileIndex, modeInfo.BlockSize, transformSize, blocks);
    }

    private static Vp9CoefficientBlockProbe ReadCoefficientBlock(
        ref Vp9BoolReader reader,
        Vp9KeyFrameDecodeState state,
        Vp9ModeInfoProbe modeInfo,
        Vp9TransformSize transformSize,
        Vp9TransformType transformType,
        int row4,
        int column4,
        int planeType,
        int referenceType,
        int dcDequant,
        int acDequant,
        int initialCoefficientContext)
    {
        var maxEob = Vp9ScanTables.GetMaximumEob(transformSize);
        var coefficients = new int[maxEob];
        if (modeInfo.Skip)
        {
            return CreateBlockProbe(
                modeInfo,
                transformSize,
                transformType,
                row4,
                column4,
                planeType,
                referenceType,
                initialCoefficientContext,
                eob: 0,
                coefficients);
        }

        var probabilities = state.CompressedHeader.FrameContext.CoefficientProbabilities;
        var scan = Vp9ScanTables.GetScan(transformSize, transformType);
        var neighbors = Vp9ScanTables.GetNeighbors(transformSize, transformType);
        var tokenCache = new byte[maxEob];
        var coefficientIndex = 0;
        var coefficientContext = initialCoefficientContext;
        var dq = dcDequant;

        while (coefficientIndex < maxEob)
        {
            var band = Vp9ScanTables.GetBand(transformSize, coefficientIndex);
            var probabilityIndex = state.CompressedHeader.FrameContext.GetCoefficientProbabilityIndex(
                (int)transformSize,
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
                        transformSize,
                        transformType,
                        row4,
                        column4,
                        planeType,
                        referenceType,
                        initialCoefficientContext,
                        coefficientIndex,
                        coefficients);
                }

                coefficientContext = Vp9ScanTables.GetCoefficientContext(neighbors, tokenCache, coefficientIndex);
                band = Vp9ScanTables.GetBand(transformSize, coefficientIndex);
                probabilityIndex = state.CompressedHeader.FrameContext.GetCoefficientProbabilityIndex(
                    (int)transformSize,
                    planeType,
                    referenceType,
                    band,
                    coefficientContext,
                    0);
                dq = acDequant;
            }

            var coefficient = ReadNonZeroCoefficientValue(ref reader, probabilities[probabilityIndex + 2]);
            var dequantizedValue = Dequantize(coefficient.TokenValue, dq, transformSize);
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
                dq = acDequant;
            }
        }

        return CreateBlockProbe(
            modeInfo,
            transformSize,
            transformType,
            row4,
            column4,
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
        var step = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(modeInfo.TransformSize);
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                var blockIndex = (row * gridSize) + column;
                var row4 = row * step;
                var column4 = column * step;
                var transformType = GetTransformType(modeInfo, plane: 0, modeInfo.TransformSize, row4, column4);
                if (modeInfo.Skip)
                {
                    blocks[blockIndex] = CreateBlockProbe(
                        modeInfo,
                        modeInfo.TransformSize,
                        transformType,
                        row4,
                        column4,
                        planeType: 0,
                        referenceType: 0,
                        initialCoefficientContext: 0,
                        eob: 0,
                        new int[Vp9ScanTables.GetMaximumEob(modeInfo.TransformSize)]);
                    continue;
                }

                var context = (row > 0 && nonZeroContexts[row - 1, column] ? 1 : 0) +
                    (column > 0 && nonZeroContexts[row, column - 1] ? 1 : 0);
                var block = ReadFirstYCoefficientBlock(ref reader, state, modeInfo, context, row4, column4);
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

    private static Vp9TransformType GetTransformType(
        Vp9ModeInfoProbe modeInfo,
        int plane,
        Vp9TransformSize transformSize,
        int row4,
        int column4)
    {
        if (plane != 0)
        {
            return Vp9TransformType.DctDct;
        }

        var mode = GetYTransformPredictionMode(modeInfo, transformSize, row4, column4);
        return mode switch
        {
            Vp9PredictionMode.Dc or Vp9PredictionMode.D45 => Vp9TransformType.DctDct,
            Vp9PredictionMode.Vertical or Vp9PredictionMode.D117 or Vp9PredictionMode.D63 => Vp9TransformType.AdstDct,
            Vp9PredictionMode.Horizontal or Vp9PredictionMode.D153 or Vp9PredictionMode.D207 => Vp9TransformType.DctAdst,
            Vp9PredictionMode.D135 or Vp9PredictionMode.TrueMotion => Vp9TransformType.AdstAdst,
            _ => throw new ArgumentOutOfRangeException(nameof(modeInfo), mode, "Unsupported VP9 intra prediction mode.")
        };
    }

    private static Vp9PredictionMode GetYTransformPredictionMode(
        Vp9ModeInfoProbe modeInfo,
        Vp9TransformSize transformSize,
        int row4,
        int column4)
    {
        if (modeInfo.BlockSize >= Vp9BlockSize.Block8X8 || transformSize != Vp9TransformSize.Tx4X4)
        {
            return modeInfo.YMode;
        }

        if (row4 is < 0 or > 1 || column4 is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row4),
                "VP9 sub-8x8 transform block coordinates must be within the owning 8x8 mode-info block.");
        }

        if (modeInfo.YSubModes.Count != 4)
        {
            throw new InvalidOperationException("VP9 sub-8x8 mode info must carry exactly four Y sub-modes.");
        }

        return modeInfo.YSubModes[(row4 * 2) + column4];
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

    private static void ThrowIfUnsupportedResidualBlock(
        Vp9ModeInfoProbe modeInfo,
        int plane,
        int row4,
        int column4,
        Vp9CoefficientBlockProbe block)
    {
        if (Vp9BlockReconstructor.IsDcOnlyOrEmpty(block))
        {
            return;
        }

        if (block.TransformSize is Vp9TransformSize.Tx4X4 or Vp9TransformSize.Tx8X8 or Vp9TransformSize.Tx16X16 or Vp9TransformSize.Tx32X32 &&
            block.Eob <= 1024)
        {
            return;
        }

        throw new NotSupportedException(
            $"VP9 full-frame residual probe currently supports only DC-only, TX4, TX8, TX16, or TX32 coefficient blocks; got MI ({modeInfo.MiRow},{modeInfo.MiColumn}) plane {plane} block {modeInfo.BlockSize} transform {block.TransformSize}/{block.TransformType} transform offset ({row4},{column4}) eob {block.Eob}.");
    }

    private static void ThrowIfUnsupportedResidualGrid(
        Vp9ModeInfoProbe modeInfo,
        int plane,
        Vp9TransformSize transformSize)
    {
        if (modeInfo.Skip)
        {
            return;
        }

        if (plane == 0 &&
            modeInfo.BlockSize >= Vp9BlockSize.Block16X16 &&
            transformSize is Vp9TransformSize.Tx4X4 or Vp9TransformSize.Tx8X8)
        {
            throw new NotSupportedException(
                $"VP9 full-frame residual probe does not support {transformSize} transform grids in {modeInfo.BlockSize} blocks yet at MI ({modeInfo.MiRow},{modeInfo.MiColumn}) plane {plane}.");
        }
    }

    private static Vp9TransformSize GetUvTransformSize(Vp9BlockSize blockSize, Vp9TransformSize yTransformSize)
    {
        var chromaWidth4 = GetPlaneWidthIn4x4Blocks(blockSize, plane: 1);
        var chromaHeight4 = GetPlaneHeightIn4x4Blocks(blockSize, plane: 1);
        var maxChromaTransformSize = Math.Min(chromaWidth4, chromaHeight4) switch
        {
            >= 8 => Vp9TransformSize.Tx32X32,
            >= 4 => Vp9TransformSize.Tx16X16,
            >= 2 => Vp9TransformSize.Tx8X8,
            _ => Vp9TransformSize.Tx4X4
        };

        return (Vp9TransformSize)Math.Min((int)yTransformSize, (int)maxChromaTransformSize);
    }

    private static int GetPlaneWidthIn4x4Blocks(Vp9BlockSize blockSize, int plane)
    {
        var miWidth = Vp9ModeInfoSyntax.GetBlockWidthInMiUnits(blockSize);
        return plane == 0 ? miWidth * 2 : miWidth;
    }

    private static int GetPlaneHeightIn4x4Blocks(Vp9BlockSize blockSize, int plane)
    {
        var miHeight = Vp9ModeInfoSyntax.GetBlockHeightInMiUnits(blockSize);
        return plane == 0 ? miHeight * 2 : miHeight;
    }

    private static int GetVisiblePlaneWidthIn4x4Blocks(
        Vp9FrameHeader header,
        Vp9BlockSize blockSize,
        int miColumn,
        int plane)
    {
        var blockWidth = GetPlaneWidthIn4x4Blocks(blockSize, plane);
        var remainingMi = Math.Max(0, header.TileInfo.MiColumns - miColumn);
        var remainingPlane4 = plane == 0 ? remainingMi * 2 : remainingMi;
        return Math.Min(blockWidth, remainingPlane4);
    }

    private static int GetVisiblePlaneHeightIn4x4Blocks(
        Vp9FrameHeader header,
        Vp9BlockSize blockSize,
        int miRow,
        int plane)
    {
        var blockHeight = GetPlaneHeightIn4x4Blocks(blockSize, plane);
        var remainingMi = Math.Max(0, header.TileInfo.MiRows - miRow);
        var remainingPlane4 = plane == 0 ? remainingMi * 2 : remainingMi;
        return Math.Min(blockHeight, remainingPlane4);
    }

    private static int GetPlaneX4(int miColumn, int plane)
    {
        return plane == 0 ? miColumn * 2 : miColumn;
    }

    private static int GetPlaneY4(int miRow, int plane)
    {
        return plane == 0 ? miRow * 2 : miRow;
    }

    private static Vp9CoefficientBlockProbe CreateBlockProbe(
        Vp9ModeInfoProbe modeInfo,
        Vp9TransformSize transformSize,
        Vp9TransformType transformType,
        int row4,
        int column4,
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
            transformSize,
            transformType,
            row4,
            column4,
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
