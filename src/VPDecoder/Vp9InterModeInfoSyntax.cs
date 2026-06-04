namespace VPDecoder;

internal enum Vp9InterPredictionMode
{
    NearestMv = 0,
    NearMv = 1,
    ZeroMv = 2,
    NewMv = 3
}

internal enum Vp9InterReferenceFrame
{
    Last = 1,
    Golden = 2,
    AltRef = 3
}

internal readonly record struct Vp9MotionVector(int Row, int Column);

internal readonly record struct Vp9InterModeInfoContexts(
    int Skip,
    int IntraInter,
    int TransformSize,
    int SingleReference0,
    int SingleReference1,
    int InterMode,
    int SwitchableInterpolation);

internal sealed record Vp9InterModeInfoProbe(
    Vp9BlockSize BlockSize,
    bool Skip,
    int SkipContext,
    bool IsInterBlock,
    int IntraInterContext,
    Vp9TransformSize TransformSize,
    int TransformSizeContext,
    Vp9ReferenceMode ReferenceMode,
    Vp9InterReferenceFrame ReferenceFrame,
    int SingleReferenceContext0,
    int? SingleReferenceContext1,
    Vp9InterPredictionMode PredictionMode,
    int InterModeContext,
    Vp9InterpolationFilter InterpolationFilter)
{
    public Vp9PredictionMode YMode { get; init; } = Vp9PredictionMode.Dc;

    public Vp9PredictionMode UvMode { get; init; } = Vp9PredictionMode.Dc;

    public IReadOnlyList<Vp9PredictionMode> YSubModes { get; init; } = [];
}

internal static class Vp9InterModeInfoSyntax
{
    private static ReadOnlySpan<sbyte> InterModeTree =>
    [
        -2, 2,
        0, 4,
        -1, -3
    ];

    private static ReadOnlySpan<sbyte> IntraModeTree =>
    [
        0, 2,
        -9, 4,
        -1, 6,
        8, 12,
        -2, 10,
        -4, -5,
        -3, 14,
        -8, 16,
        -6, -7
    ];

    private static ReadOnlySpan<sbyte> SwitchableInterpolationTree =>
    [
        -1, 2,
        0, -2
    ];

    public static bool TryReadSupportedInterBlock(
        ref Vp9BoolReader reader,
        Vp9FrameHeader frameHeader,
        Vp9CompressedHeader compressedHeader,
        Vp9BlockSize blockSize,
        Vp9InterModeInfoContexts contexts,
        out Vp9InterModeInfoProbe? probe,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        probe = null;
        diagnostic = null;

        if (frameHeader.Segmentation.Enabled)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 inter-frame segmentation mode-info is not supported yet.");
            return false;
        }

        var skip = Vp9ModeInfoSyntax.ReadSkip(ref reader, compressedHeader.FrameContext, contexts.Skip);
        var isInterBlock = ReadIsInterBlock(ref reader, compressedHeader.FrameContext, contexts.IntraInter);
        var allowTransformSelect = !skip || !isInterBlock;
        var transformSizeContext = allowTransformSelect &&
            compressedHeader.TransformMode == Vp9TransformMode.Select &&
            blockSize >= Vp9BlockSize.Block8X8
            ? contexts.TransformSize
            : 0;
        var transformSize = Vp9ModeInfoSyntax.ReadTransformSize(
            ref reader,
            compressedHeader,
            blockSize,
            transformSizeContext,
            allowSelect: allowTransformSelect);

        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter mode-info ended unexpectedly.");
            return false;
        }

        if (!isInterBlock)
        {
            if (!TryReadInterFrameIntraModes(
                    ref reader,
                    compressedHeader.FrameContext,
                    blockSize,
                    out var yMode,
                    out var uvMode,
                    out var ySubModes,
                    out diagnostic))
            {
                return false;
            }

            if (reader.HasError)
            {
                diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter-frame intra mode-info ended unexpectedly.");
                return false;
            }

            probe = new Vp9InterModeInfoProbe(
                blockSize,
                skip,
                contexts.Skip,
                isInterBlock,
                contexts.IntraInter,
                transformSize,
                transformSizeContext,
                compressedHeader.ReferenceMode,
                Vp9InterReferenceFrame.Last,
                contexts.SingleReference0,
                null,
                Vp9InterPredictionMode.ZeroMv,
                contexts.InterMode,
                Vp9InterpolationFilter.Switchable)
            {
                YMode = yMode,
                UvMode = uvMode,
                YSubModes = ySubModes
            };
            return true;
        }

        if (compressedHeader.ReferenceMode != Vp9ReferenceMode.Single)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 compound or selectable block reference modes are not supported yet.");
            return false;
        }

        var (referenceFrame, singleReferenceContext1) = ReadSingleReferenceFrame(
            ref reader,
            compressedHeader.FrameContext,
            contexts.SingleReference0,
            contexts.SingleReference1);
        Vp9InterPredictionMode predictionMode;
        Vp9InterpolationFilter interpolationFilter;
        if (blockSize < Vp9BlockSize.Block8X8)
        {
            interpolationFilter = ReadInterBlockInterpolationFilter(
                ref reader,
                frameHeader,
                compressedHeader.FrameContext,
                contexts.SwitchableInterpolation);
            if (!TryReadUniformSub8X8InterPredictionMode(
                    ref reader,
                    compressedHeader.FrameContext,
                    contexts.InterMode,
                    blockSize,
                    out predictionMode,
                    out diagnostic))
            {
                return false;
            }
        }
        else
        {
            predictionMode = ReadInterPredictionMode(
                ref reader,
                compressedHeader.FrameContext,
                contexts.InterMode);
            interpolationFilter = ReadInterBlockInterpolationFilter(
                ref reader,
                frameHeader,
                compressedHeader.FrameContext,
                contexts.SwitchableInterpolation);
        }

        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter mode-info ended unexpectedly.");
            return false;
        }

        probe = new Vp9InterModeInfoProbe(
            blockSize,
            skip,
            contexts.Skip,
            isInterBlock,
            contexts.IntraInter,
            transformSize,
            transformSizeContext,
            compressedHeader.ReferenceMode,
            referenceFrame,
            contexts.SingleReference0,
            singleReferenceContext1,
            predictionMode,
            contexts.InterMode,
            interpolationFilter);
        return true;
    }

    public static bool ReadIsInterBlock(ref Vp9BoolReader reader, Vp9FrameContext frameContext, int intraInterContext)
    {
        if (intraInterContext is < 0 or >= Vp9FrameContextConstants.IntraInterContexts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intraInterContext),
                "VP9 intra/inter context is outside the probability table.");
        }

        return reader.Read(frameContext.IntraInterProbabilities[intraInterContext]);
    }

    public static (Vp9InterReferenceFrame ReferenceFrame, int? Context1) ReadSingleReferenceFrame(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        int context0,
        int context1)
    {
        ValidateReferenceContext(context0, nameof(context0));
        ValidateReferenceContext(context1, nameof(context1));

        var bit0 = reader.Read(frameContext.SingleReferenceProbabilities[context0, 0]);
        if (!bit0)
        {
            return (Vp9InterReferenceFrame.Last, null);
        }

        var bit1 = reader.Read(frameContext.SingleReferenceProbabilities[context1, 1]);
        return (bit1 ? Vp9InterReferenceFrame.AltRef : Vp9InterReferenceFrame.Golden, context1);
    }

    public static Vp9InterPredictionMode ReadInterPredictionMode(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        int interModeContext)
    {
        if (interModeContext is < 0 or >= Vp9FrameContextConstants.InterModeContexts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interModeContext),
                "VP9 inter mode context is outside the probability table.");
        }

        var probabilities = new byte[Vp9FrameContextConstants.InterModes - 1];
        for (var i = 0; i < probabilities.Length; i++)
        {
            probabilities[i] = frameContext.InterModeProbabilities[interModeContext, i];
        }

        return (Vp9InterPredictionMode)Vp9TreeReader.ReadTree(ref reader, InterModeTree, probabilities);
    }

    public static Vp9PredictionMode ReadInterFrameYMode(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        int blockSizeGroup)
    {
        if (blockSizeGroup is < 0 or >= Vp9FrameContextConstants.BlockSizeGroups)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockSizeGroup),
                "VP9 inter-frame intra Y mode block-size group is outside the probability table.");
        }

        var probabilities = new byte[Vp9FrameContextConstants.IntraModes - 1];
        for (var i = 0; i < probabilities.Length; i++)
        {
            probabilities[i] = frameContext.InterFrameYModeProbabilities[blockSizeGroup, i];
        }

        return (Vp9PredictionMode)Vp9TreeReader.ReadTree(ref reader, IntraModeTree, probabilities);
    }

    public static Vp9PredictionMode ReadInterFrameUvMode(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        Vp9PredictionMode yMode)
    {
        if (yMode is < Vp9PredictionMode.Dc or > Vp9PredictionMode.TrueMotion)
        {
            throw new ArgumentOutOfRangeException(nameof(yMode), yMode, "VP9 intra Y mode is outside the probability table.");
        }

        var probabilities = new byte[Vp9FrameContextConstants.IntraModes - 1];
        for (var i = 0; i < probabilities.Length; i++)
        {
            probabilities[i] = frameContext.InterFrameUvModeProbabilities[(int)yMode, i];
        }

        return (Vp9PredictionMode)Vp9TreeReader.ReadTree(ref reader, IntraModeTree, probabilities);
    }

    public static int GetIntraModeBlockSizeGroup(Vp9BlockSize blockSize)
    {
        return blockSize switch
        {
            Vp9BlockSize.Block4X4 or
                Vp9BlockSize.Block4X8 or
                Vp9BlockSize.Block8X4 => 0,
            Vp9BlockSize.Block8X8 or
                Vp9BlockSize.Block8X16 or
                Vp9BlockSize.Block16X8 => 1,
            Vp9BlockSize.Block16X16 or
                Vp9BlockSize.Block16X32 or
                Vp9BlockSize.Block32X16 => 2,
            Vp9BlockSize.Block32X32 or
                Vp9BlockSize.Block32X64 or
                Vp9BlockSize.Block64X32 or
                Vp9BlockSize.Block64X64 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "Unsupported VP9 block size.")
        };
    }

    public static bool TryReadInterFrameIntraModes(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        Vp9BlockSize blockSize,
        out Vp9PredictionMode yMode,
        out Vp9PredictionMode uvMode,
        out IReadOnlyList<Vp9PredictionMode> ySubModes,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        yMode = default;
        uvMode = default;
        ySubModes = [];
        diagnostic = null;

        Span<Vp9PredictionMode> subModes = stackalloc Vp9PredictionMode[4];
        switch (blockSize)
        {
            case Vp9BlockSize.Block4X4:
                for (var block = 0; block < subModes.Length; block++)
                {
                    subModes[block] = ReadInterFrameYMode(ref reader, frameContext, blockSizeGroup: 0);
                }

                yMode = subModes[3];
                ySubModes = subModes.ToArray();
                break;

            case Vp9BlockSize.Block4X8:
                subModes[0] = subModes[2] = ReadInterFrameYMode(ref reader, frameContext, blockSizeGroup: 0);
                subModes[1] = subModes[3] = yMode = ReadInterFrameYMode(ref reader, frameContext, blockSizeGroup: 0);
                ySubModes = subModes.ToArray();
                break;

            case Vp9BlockSize.Block8X4:
                subModes[0] = subModes[1] = ReadInterFrameYMode(ref reader, frameContext, blockSizeGroup: 0);
                subModes[2] = subModes[3] = yMode = ReadInterFrameYMode(ref reader, frameContext, blockSizeGroup: 0);
                ySubModes = subModes.ToArray();
                break;

            default:
                yMode = ReadInterFrameYMode(
                    ref reader,
                    frameContext,
                    GetIntraModeBlockSizeGroup(blockSize));
                break;
        }

        uvMode = ReadInterFrameUvMode(ref reader, frameContext, yMode);
        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 inter-frame intra mode-info ended unexpectedly.");
            return false;
        }

        return true;
    }

    public static bool TryReadUniformSub8X8InterPredictionMode(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        int interModeContext,
        Vp9BlockSize blockSize,
        out Vp9InterPredictionMode predictionMode,
        out Vp9DecodeDiagnostic? diagnostic)
    {
        predictionMode = default;
        diagnostic = null;

        Span<Vp9InterPredictionMode> subModes = stackalloc Vp9InterPredictionMode[4];
        switch (blockSize)
        {
            case Vp9BlockSize.Block4X4:
                for (var block = 0; block < subModes.Length; block++)
                {
                    subModes[block] = ReadInterPredictionMode(ref reader, frameContext, interModeContext);
                }

                break;

            case Vp9BlockSize.Block4X8:
                subModes[0] = subModes[2] = ReadInterPredictionMode(ref reader, frameContext, interModeContext);
                subModes[1] = subModes[3] = ReadInterPredictionMode(ref reader, frameContext, interModeContext);
                break;

            case Vp9BlockSize.Block8X4:
                subModes[0] = subModes[1] = ReadInterPredictionMode(ref reader, frameContext, interModeContext);
                subModes[2] = subModes[3] = ReadInterPredictionMode(ref reader, frameContext, interModeContext);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(blockSize),
                    blockSize,
                    "VP9 sub-8x8 inter mode reader received a non-sub-8x8 block size.");
        }

        if (reader.HasError)
        {
            diagnostic = Vp9DecodeDiagnostic.TruncatedPacket("VP9 sub-8x8 inter mode-info ended unexpectedly.");
            return false;
        }

        predictionMode = subModes[0];
        for (var i = 1; i < subModes.Length; i++)
        {
            if (subModes[i] != predictionMode)
            {
                diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                    "VP9 sub-8x8 inter blocks with mixed sub-block prediction modes are not supported yet.");
                return false;
            }
        }

        if (predictionMode == Vp9InterPredictionMode.NewMv)
        {
            diagnostic = Vp9DecodeDiagnostic.UnsupportedInterFrameFeature(
                "VP9 sub-8x8 NEWMV inter prediction mode is not supported yet.");
            return false;
        }

        return true;
    }

    public static Vp9InterpolationFilter ReadSwitchableInterpolationFilter(
        ref Vp9BoolReader reader,
        Vp9FrameContext frameContext,
        int switchableInterpolationContext)
    {
        if (switchableInterpolationContext is < 0 or >= Vp9FrameContextConstants.SwitchableFilterContexts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(switchableInterpolationContext),
                "VP9 switchable interpolation context is outside the probability table.");
        }

        var probabilities = new byte[Vp9FrameContextConstants.SwitchableFilters - 1];
        for (var i = 0; i < probabilities.Length; i++)
        {
            probabilities[i] = frameContext.SwitchableInterpolationProbabilities[switchableInterpolationContext, i];
        }

        return (Vp9InterpolationFilter)Vp9TreeReader.ReadTree(ref reader, SwitchableInterpolationTree, probabilities);
    }

    private static Vp9InterpolationFilter ReadInterBlockInterpolationFilter(
        ref Vp9BoolReader reader,
        Vp9FrameHeader frameHeader,
        Vp9FrameContext frameContext,
        int switchableInterpolationContext)
    {
        return frameHeader.InterpolationFilter switch
        {
            Vp9InterpolationFilter.Switchable => ReadSwitchableInterpolationFilter(
                ref reader,
                frameContext,
                switchableInterpolationContext),
            Vp9InterpolationFilter.EightTapSmooth or
                Vp9InterpolationFilter.EightTap or
                Vp9InterpolationFilter.EightTapSharp or
                Vp9InterpolationFilter.Bilinear => frameHeader.InterpolationFilter,
            _ => throw new ArgumentOutOfRangeException(
                nameof(frameHeader),
                frameHeader.InterpolationFilter,
                "VP9 inter frame has an invalid interpolation filter.")
        };
    }

    private static void ValidateReferenceContext(int context, string parameterName)
    {
        if (context is < 0 or >= Vp9FrameContextConstants.ReferenceContexts)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "VP9 reference context is outside the probability table.");
        }
    }
}
