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
    public const int IntraBlockReferenceType = 0;
    public const int InterBlockReferenceType = 1;

    private static readonly Vp9TransformSize[][] Yuv420UvTransformSizeLookup =
    [
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx8X8],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx16X16],
        [Vp9TransformSize.Tx4X4, Vp9TransformSize.Tx8X8, Vp9TransformSize.Tx16X16, Vp9TransformSize.Tx32X32]
    ];
    private static readonly int[][] ZeroCoefficientArrays = CreateZeroCoefficientArrays();
    private static readonly string[] ZeroCoefficientHashes = CreateZeroCoefficientHashes();

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
        return ReadIntraPlaneCoefficientBlocks(
            ref reader,
            state.Header,
            state.CompressedHeader.FrameContext,
            state.DequantTables,
            modeInfo,
            entropyContext,
            plane);
    }

    private static Vp9CoefficientBlockGroupProbe ReadIntraPlaneCoefficientBlocks(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9FrameContext frameContext,
        Vp9DequantTables dequantTables,
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
        var width4 = GetVisiblePlaneWidthIn4x4Blocks(header, modeInfo.BlockSize, modeInfo.MiColumn, plane);
        var height4 = GetVisiblePlaneHeightIn4x4Blocks(header, modeInfo.BlockSize, modeInfo.MiRow, plane);
        var step = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(transformSize);
        var blocks = new List<Vp9CoefficientBlockProbe>();

        if (modeInfo.Skip)
        {
            entropyContext.ClearBlock(
                plane,
                GetPlaneX4(modeInfo.MiColumn, plane),
                GetPlaneLeftContextOffset(modeInfo.MiRow, plane),
                GetPlaneWidthIn4x4Blocks(modeInfo.BlockSize, plane),
                GetPlaneHeightIn4x4Blocks(modeInfo.BlockSize, plane));
            for (var row = 0; row < height4; row += step)
            {
                for (var column = 0; column < width4; column += step)
                {
                    var transformType = GetTransformType(modeInfo, plane, transformSize, row, column);
                    blocks.Add(CreateZeroBlockProbe(
                        modeInfo,
                        transformSize,
                        transformType,
                        row,
                        column,
                        plane == 0 ? 0 : 1,
                        referenceType: 0,
                        initialCoefficientContext: 0));
                }
            }

            return new Vp9CoefficientBlockGroupProbe(modeInfo.TileIndex, modeInfo.BlockSize, transformSize, blocks);
        }

        var dc = plane == 0 ? dequantTables.YDc : dequantTables.UvDc;
        var ac = plane == 0 ? dequantTables.YAc : dequantTables.UvAc;
        var planeType = plane == 0 ? 0 : 1;
        var originX4 = GetPlaneX4(modeInfo.MiColumn, plane);
        var originY4 = GetPlaneLeftContextOffset(modeInfo.MiRow, plane);
        for (var row = 0; row < height4; row += step)
        {
            for (var column = 0; column < width4; column += step)
            {
                var x4 = originX4 + column;
                var y4 = originY4 + row;
                var context = entropyContext.GetInitialContext(plane, x4, y4, transformSize);
                var transformType = GetTransformType(modeInfo, plane, transformSize, row, column);
                var visibleWidth4 = Math.Min(step, width4 - column);
                var visibleHeight4 = Math.Min(step, height4 - row);
                var block = ReadCoefficientBlock(
                    ref reader,
                    frameContext,
                    modeInfo.TileIndex,
                    modeInfo.Skip,
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
                entropyContext.SetTransformContext(
                    plane,
                    x4,
                    y4,
                    transformSize,
                    block.Eob > 0,
                    visibleWidth4,
                    visibleHeight4);
            }
        }

        return new Vp9CoefficientBlockGroupProbe(modeInfo.TileIndex, modeInfo.BlockSize, transformSize, blocks);
    }

    public static int GetReferenceType(bool isInterBlock)
    {
        return isInterBlock ? InterBlockReferenceType : IntraBlockReferenceType;
    }

    public static Vp9TransformType GetInterTransformType(Vp9InterModeInfoProbe modeInfo, int plane)
    {
        if (!modeInfo.IsInterBlock)
        {
            throw new NotSupportedException("VP9 inter residual syntax requires an inter-predicted block.");
        }

        if (plane is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.");
        }

        return Vp9TransformType.DctDct;
    }

    public static Vp9CoefficientBlockGroupProbe CreateSkippedInterPlaneCoefficientBlocks(
        Vp9FrameHeader header,
        Vp9InterBlockModeInfoProbe modeBlock,
        int plane)
    {
        if (plane is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.");
        }

        var modeInfo = modeBlock.ModeInfo;
        if (!modeInfo.IsInterBlock)
        {
            if (!modeInfo.Skip)
            {
                throw new NotSupportedException(
                    "VP9 skipped inter residual scaffold requires skip=true; non-skipped inter-frame intra residual synthesis is not supported here.");
            }

            return CreateSkippedIntraPlaneCoefficientBlocks(header, modeBlock.ToIntraModeInfoProbe(), plane);
        }

        if (!modeInfo.Skip)
        {
            throw new NotSupportedException(
                "VP9 inter residual scaffold currently supports only skipped inter blocks; non-skipped inter residual decoding is not supported yet.");
        }

        var transformSize = plane == 0
            ? modeInfo.TransformSize
            : GetUvTransformSize(modeInfo.BlockSize, modeInfo.TransformSize);
        var width4 = GetVisiblePlaneWidthIn4x4Blocks(header, modeInfo.BlockSize, modeBlock.MiColumn, plane);
        var height4 = GetVisiblePlaneHeightIn4x4Blocks(header, modeInfo.BlockSize, modeBlock.MiRow, plane);
        var step = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(transformSize);
        var transformType = GetInterTransformType(modeInfo, plane);
        var planeType = plane == 0 ? 0 : 1;
        var blocks = new List<Vp9CoefficientBlockProbe>();
        for (var row = 0; row < height4; row += step)
        {
            for (var column = 0; column < width4; column += step)
            {
                blocks.Add(CreateZeroBlockProbe(
                    modeBlock.TileIndex,
                    transformSize,
                    transformType,
                    row,
                    column,
                    planeType,
                    InterBlockReferenceType,
                    initialCoefficientContext: 0));
            }
        }

        return new Vp9CoefficientBlockGroupProbe(
            modeBlock.TileIndex,
            modeInfo.BlockSize,
            transformSize,
            blocks);
    }

    private static Vp9CoefficientBlockGroupProbe CreateSkippedIntraPlaneCoefficientBlocks(
        Vp9FrameHeader header,
        Vp9ModeInfoProbe modeInfo,
        int plane)
    {
        var transformSize = plane == 0
            ? modeInfo.TransformSize
            : GetUvTransformSize(modeInfo.BlockSize, modeInfo.TransformSize);
        var width4 = GetVisiblePlaneWidthIn4x4Blocks(header, modeInfo.BlockSize, modeInfo.MiColumn, plane);
        var height4 = GetVisiblePlaneHeightIn4x4Blocks(header, modeInfo.BlockSize, modeInfo.MiRow, plane);
        var step = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(transformSize);
        var planeType = plane == 0 ? 0 : 1;
        var blocks = new List<Vp9CoefficientBlockProbe>();
        for (var row = 0; row < height4; row += step)
        {
            for (var column = 0; column < width4; column += step)
            {
                blocks.Add(CreateZeroBlockProbe(
                    modeInfo,
                    transformSize,
                    GetTransformType(modeInfo, plane, transformSize, row, column),
                    row,
                    column,
                    planeType,
                    IntraBlockReferenceType,
                    initialCoefficientContext: 0));
            }
        }

        return new Vp9CoefficientBlockGroupProbe(
            modeInfo.TileIndex,
            modeInfo.BlockSize,
            transformSize,
            blocks);
    }

    public static Vp9CoefficientBlockGroupProbe ReadInterPlaneCoefficientBlocks(
        ref Vp9BoolReader reader,
        Vp9FrameHeader header,
        Vp9CompressedHeader compressedHeader,
        Vp9DequantTables dequantTables,
        Vp9InterBlockModeInfoProbe modeBlock,
        Vp9CoefficientEntropyContext entropyContext,
        int plane)
    {
        if (plane is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, "VP9 plane index must be 0, 1, or 2.");
        }

        var modeInfo = modeBlock.ModeInfo;
        if (!modeInfo.IsInterBlock)
        {
            return ReadIntraPlaneCoefficientBlocks(
                ref reader,
                header,
                compressedHeader.FrameContext,
                dequantTables,
                modeBlock.ToIntraModeInfoProbe(),
                entropyContext,
                plane);
        }

        if (modeInfo.Skip)
        {
            entropyContext.ClearBlock(
                plane,
                GetPlaneX4(modeBlock.MiColumn, plane),
                GetPlaneLeftContextOffset(modeBlock.MiRow, plane),
                GetPlaneWidthIn4x4Blocks(modeInfo.BlockSize, plane),
                GetPlaneHeightIn4x4Blocks(modeInfo.BlockSize, plane));
            return CreateSkippedInterPlaneCoefficientBlocks(header, modeBlock, plane);
        }

        var transformSize = plane == 0
            ? modeInfo.TransformSize
            : GetUvTransformSize(modeInfo.BlockSize, modeInfo.TransformSize);
        var width4 = GetVisiblePlaneWidthIn4x4Blocks(header, modeInfo.BlockSize, modeBlock.MiColumn, plane);
        var height4 = GetVisiblePlaneHeightIn4x4Blocks(header, modeInfo.BlockSize, modeBlock.MiRow, plane);
        var step = Vp9CoefficientEntropyContext.GetTransformSizeIn4x4Blocks(transformSize);
        var dc = plane == 0 ? dequantTables.YDc : dequantTables.UvDc;
        var ac = plane == 0 ? dequantTables.YAc : dequantTables.UvAc;
        var planeType = plane == 0 ? 0 : 1;
        var originX4 = GetPlaneX4(modeBlock.MiColumn, plane);
        var originY4 = GetPlaneLeftContextOffset(modeBlock.MiRow, plane);
        var blocks = new List<Vp9CoefficientBlockProbe>();
        for (var row = 0; row < height4; row += step)
        {
            for (var column = 0; column < width4; column += step)
            {
                var x4 = originX4 + column;
                var y4 = originY4 + row;
                var context = entropyContext.GetInitialContext(plane, x4, y4, transformSize);
                var visibleWidth4 = Math.Min(step, width4 - column);
                var visibleHeight4 = Math.Min(step, height4 - row);
                var block = ReadCoefficientBlock(
                    ref reader,
                    compressedHeader.FrameContext,
                    modeBlock.TileIndex,
                    skip: false,
                    transformSize,
                    GetInterTransformType(modeInfo, plane),
                    row,
                    column,
                    planeType,
                    InterBlockReferenceType,
                    dc,
                    ac,
                    context);
                blocks.Add(block);
                entropyContext.SetTransformContext(
                    plane,
                    x4,
                    y4,
                    transformSize,
                    block.Eob > 0,
                    visibleWidth4,
                    visibleHeight4);
            }
        }

        return new Vp9CoefficientBlockGroupProbe(
            modeBlock.TileIndex,
            modeInfo.BlockSize,
            transformSize,
            blocks);
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
        return ReadCoefficientBlock(
            ref reader,
            state.CompressedHeader.FrameContext,
            modeInfo.TileIndex,
            modeInfo.Skip,
            transformSize,
            transformType,
            row4,
            column4,
            planeType,
            referenceType,
            dcDequant,
            acDequant,
            initialCoefficientContext);
    }

    private static Vp9CoefficientBlockProbe ReadCoefficientBlock(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        int tileIndex,
        bool skip,
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
        if (skip)
        {
            return CreateZeroBlockProbe(
                tileIndex,
                transformSize,
                transformType,
                row4,
                column4,
                planeType,
                referenceType,
                initialCoefficientContext);
        }

        var probabilities = frameContext.CoefficientProbabilities;
        var coefficientIndex = 0;
        var coefficientContext = initialCoefficientContext;
        var dq = dcDequant;
        var band = Vp9ScanTables.GetBand(transformSize, coefficientIndex);
        var probabilityIndex = frameContext.GetCoefficientProbabilityIndex(
            (int)transformSize,
            planeType,
            referenceType,
            band,
            coefficientContext,
            0);

        if (!reader.Read(probabilities[probabilityIndex]))
        {
            return CreateZeroBlockProbe(
                tileIndex,
                transformSize,
                transformType,
                row4,
                column4,
                planeType,
                referenceType,
                initialCoefficientContext);
        }

        var coefficients = new int[maxEob];
        var scan = Vp9ScanTables.GetScan(transformSize, transformType);
        var neighbors = Vp9ScanTables.GetNeighbors(transformSize, transformType);
        Span<byte> tokenCache = stackalloc byte[maxEob];
        while (coefficientIndex < maxEob)
        {
            while (!reader.Read(probabilities[probabilityIndex + 1]))
            {
                tokenCache[scan[coefficientIndex]] = 0;
                coefficientIndex++;
                if (coefficientIndex >= maxEob)
                {
                    return CreateBlockProbe(
                        tileIndex,
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
                probabilityIndex = frameContext.GetCoefficientProbabilityIndex(
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
            if (coefficientIndex >= maxEob)
            {
                break;
            }

            coefficientContext = Vp9ScanTables.GetCoefficientContext(neighbors, tokenCache, coefficientIndex);
            band = Vp9ScanTables.GetBand(transformSize, coefficientIndex);
            probabilityIndex = frameContext.GetCoefficientProbabilityIndex(
                (int)transformSize,
                planeType,
                referenceType,
                band,
                coefficientContext,
                0);
            dq = acDequant;

            if (!reader.Read(probabilities[probabilityIndex]))
            {
                break;
            }
        }

        return CreateBlockProbe(
            tileIndex,
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
                    blocks[blockIndex] = CreateZeroBlockProbe(
                        modeInfo,
                        modeInfo.TransformSize,
                        transformType,
                        row4,
                        column4,
                        planeType: 0,
                        referenceType: 0,
                        initialCoefficientContext: 0);
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

    private static Vp9TransformSize GetUvTransformSize(Vp9BlockSize blockSize, Vp9TransformSize yTransformSize)
    {
        return GetUvTransformSizeForYuv420(blockSize, yTransformSize);
    }

    internal static Vp9TransformSize GetUvTransformSizeForYuv420(
        Vp9BlockSize blockSize,
        Vp9TransformSize yTransformSize)
    {
        if ((uint)blockSize >= (uint)Yuv420UvTransformSizeLookup.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "Unsupported VP9 block size.");
        }

        if (yTransformSize is < Vp9TransformSize.Tx4X4 or > Vp9TransformSize.Tx32X32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yTransformSize),
                yTransformSize,
                "Unsupported VP9 transform size.");
        }

        return Yuv420UvTransformSizeLookup[(int)blockSize][(int)yTransformSize];
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

    internal static int GetPlaneLeftContextOffset(int miRow, int plane)
    {
        if (miRow < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(miRow), "VP9 MI row cannot be negative.");
        }

        return plane == 0 ? miRow * 2 : ((miRow * 2) & 15) >> 1;
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
        return CreateBlockProbe(
            modeInfo.TileIndex,
            transformSize,
            transformType,
            row4,
            column4,
            planeType,
            referenceType,
            initialCoefficientContext,
            eob,
            coefficients);
    }

    private static Vp9CoefficientBlockProbe CreateZeroBlockProbe(
        Vp9ModeInfoProbe modeInfo,
        Vp9TransformSize transformSize,
        Vp9TransformType transformType,
        int row4,
        int column4,
        int planeType,
        int referenceType,
        int initialCoefficientContext)
    {
        return CreateZeroBlockProbe(
            modeInfo.TileIndex,
            transformSize,
            transformType,
            row4,
            column4,
            planeType,
            referenceType,
            initialCoefficientContext);
    }

    private static Vp9CoefficientBlockProbe CreateZeroBlockProbe(
        int tileIndex,
        Vp9TransformSize transformSize,
        Vp9TransformType transformType,
        int row4,
        int column4,
        int planeType,
        int referenceType,
        int initialCoefficientContext)
    {
        return new Vp9CoefficientBlockProbe(
            tileIndex,
            transformSize,
            transformType,
            row4,
            column4,
            planeType,
            referenceType,
            initialCoefficientContext,
            Eob: 0,
            NonZeroCount: 0,
            FirstNonZeroRasterIndex: -1,
            LastNonZeroRasterIndex: -1,
            DequantizedCoefficients: GetZeroCoefficientArray(transformSize),
            CoefficientsSha256: GetZeroCoefficientHash(transformSize));
    }

    private static Vp9CoefficientBlockProbe CreateBlockProbe(
        int tileIndex,
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
            tileIndex,
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

    private static string GetZeroCoefficientHash(Vp9TransformSize transformSize)
    {
        return ZeroCoefficientHashes[(int)transformSize];
    }

    private static int[] GetZeroCoefficientArray(Vp9TransformSize transformSize)
    {
        return ZeroCoefficientArrays[(int)transformSize];
    }

    private static int[][] CreateZeroCoefficientArrays()
    {
        var arrays = new int[4][];
        arrays[(int)Vp9TransformSize.Tx4X4] = new int[Vp9ScanTables.GetMaximumEob(Vp9TransformSize.Tx4X4)];
        arrays[(int)Vp9TransformSize.Tx8X8] = new int[Vp9ScanTables.GetMaximumEob(Vp9TransformSize.Tx8X8)];
        arrays[(int)Vp9TransformSize.Tx16X16] = new int[Vp9ScanTables.GetMaximumEob(Vp9TransformSize.Tx16X16)];
        arrays[(int)Vp9TransformSize.Tx32X32] = new int[Vp9ScanTables.GetMaximumEob(Vp9TransformSize.Tx32X32)];
        return arrays;
    }

    private static string[] CreateZeroCoefficientHashes()
    {
        var hashes = new string[4];
        hashes[(int)Vp9TransformSize.Tx4X4] = HashZeroCoefficients(Vp9TransformSize.Tx4X4);
        hashes[(int)Vp9TransformSize.Tx8X8] = HashZeroCoefficients(Vp9TransformSize.Tx8X8);
        hashes[(int)Vp9TransformSize.Tx16X16] = HashZeroCoefficients(Vp9TransformSize.Tx16X16);
        hashes[(int)Vp9TransformSize.Tx32X32] = HashZeroCoefficients(Vp9TransformSize.Tx32X32);
        return hashes;
    }

    private static string HashZeroCoefficients(Vp9TransformSize transformSize)
    {
        var bytes = new byte[Vp9ScanTables.GetMaximumEob(transformSize) * sizeof(int)];
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string HashCoefficients(ReadOnlySpan<int> coefficients)
    {
        if (BitConverter.IsLittleEndian)
        {
            return Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Runtime.InteropServices.MemoryMarshal.AsBytes(coefficients)))
                .ToLowerInvariant();
        }

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
