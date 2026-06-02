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
        byte[] coefficientProbabilities,
        byte[,] interFrameYModeProbabilities,
        byte[,] interFrameUvModeProbabilities,
        byte[,] partitionProbabilities,
        byte[,] switchableInterpolationProbabilities,
        byte[,] interModeProbabilities,
        byte[] intraInterProbabilities,
        byte[] compoundInterProbabilities,
        byte[,] singleReferenceProbabilities,
        byte[] compoundReferenceProbabilities,
        Vp9MotionVectorProbabilities motionVectorProbabilities)
    {
        TxProbabilities = txProbabilities;
        SkipProbabilities = skipProbabilities;
        CoefficientProbabilities = coefficientProbabilities;
        InterFrameYModeProbabilities = interFrameYModeProbabilities;
        InterFrameUvModeProbabilities = interFrameUvModeProbabilities;
        PartitionProbabilities = partitionProbabilities;
        SwitchableInterpolationProbabilities = switchableInterpolationProbabilities;
        InterModeProbabilities = interModeProbabilities;
        IntraInterProbabilities = intraInterProbabilities;
        CompoundInterProbabilities = compoundInterProbabilities;
        SingleReferenceProbabilities = singleReferenceProbabilities;
        CompoundReferenceProbabilities = compoundReferenceProbabilities;
        MotionVectorProbabilities = motionVectorProbabilities;
    }

    public Vp9TxProbabilities TxProbabilities { get; }

    public byte[] SkipProbabilities { get; }

    public byte[] CoefficientProbabilities { get; }

    public byte[,] InterFrameYModeProbabilities { get; }

    public byte[,] InterFrameUvModeProbabilities { get; }

    public byte[,] PartitionProbabilities { get; }

    public byte[,] SwitchableInterpolationProbabilities { get; }

    public byte[,] InterModeProbabilities { get; }

    public byte[] IntraInterProbabilities { get; }

    public byte[] CompoundInterProbabilities { get; }

    public byte[,] SingleReferenceProbabilities { get; }

    public byte[] CompoundReferenceProbabilities { get; }

    public Vp9MotionVectorProbabilities MotionVectorProbabilities { get; }

    public static Vp9FrameContext CreateDefault()
    {
        var coefficientProbabilities = new byte[CoefficientProbabilityCount];
        CopyCoefficientDefaults(coefficientProbabilities, 0, Vp9DefaultCoefficientProbabilities.FourByFour);
        CopyCoefficientDefaults(coefficientProbabilities, 1, Vp9DefaultCoefficientProbabilities.EightByEight);
        CopyCoefficientDefaults(coefficientProbabilities, 2, Vp9DefaultCoefficientProbabilities.SixteenBySixteen);
        CopyCoefficientDefaults(coefficientProbabilities, 3, Vp9DefaultCoefficientProbabilities.ThirtyTwoByThirtyTwo);
        return new Vp9FrameContext(
            Vp9TxProbabilities.CreateDefault(),
            [192, 128, 64],
            coefficientProbabilities,
            new byte[,]
            {
                { 65, 32, 18, 144, 162, 194, 41, 51, 98 },
                { 132, 68, 18, 165, 217, 196, 45, 40, 78 },
                { 173, 80, 19, 176, 240, 193, 64, 35, 46 },
                { 221, 135, 38, 194, 248, 121, 96, 85, 29 }
            },
            new byte[,]
            {
                { 120, 7, 76, 176, 208, 126, 28, 54, 103 },
                { 48, 12, 154, 155, 139, 90, 34, 117, 119 },
                { 67, 6, 25, 204, 243, 158, 13, 21, 96 },
                { 97, 5, 44, 131, 176, 139, 48, 68, 97 },
                { 83, 5, 42, 156, 111, 152, 26, 49, 152 },
                { 80, 5, 58, 178, 74, 83, 33, 62, 145 },
                { 86, 5, 32, 154, 192, 168, 14, 22, 163 },
                { 85, 5, 32, 156, 216, 148, 19, 29, 73 },
                { 77, 7, 64, 116, 132, 122, 37, 126, 120 },
                { 101, 21, 107, 181, 192, 103, 19, 67, 125 }
            },
            new byte[,]
            {
                { 199, 122, 141 }, { 147, 63, 159 }, { 148, 133, 118 }, { 121, 104, 114 },
                { 174, 73, 87 }, { 92, 41, 83 }, { 82, 99, 50 }, { 53, 39, 39 },
                { 177, 58, 59 }, { 68, 26, 63 }, { 52, 79, 25 }, { 17, 14, 12 },
                { 222, 34, 30 }, { 72, 16, 44 }, { 58, 32, 12 }, { 10, 7, 6 }
            },
            new byte[,]
            {
                { 235, 162 },
                { 36, 255 },
                { 34, 3 },
                { 149, 144 }
            },
            new byte[,]
            {
                { 2, 173, 34 },
                { 7, 145, 85 },
                { 7, 166, 63 },
                { 7, 94, 66 },
                { 8, 64, 46 },
                { 17, 81, 31 },
                { 25, 29, 30 }
            },
            [9, 102, 187, 225],
            [239, 183, 119, 96, 41],
            new byte[,]
            {
                { 33, 16 },
                { 77, 74 },
                { 142, 142 },
                { 172, 170 },
                { 238, 247 }
            },
            [50, 126, 123, 221, 226],
            Vp9MotionVectorProbabilities.CreateDefault());
    }

    public Vp9FrameContext Clone()
    {
        return new Vp9FrameContext(
            TxProbabilities.Clone(),
            (byte[])SkipProbabilities.Clone(),
            (byte[])CoefficientProbabilities.Clone(),
            (byte[,])InterFrameYModeProbabilities.Clone(),
            (byte[,])InterFrameUvModeProbabilities.Clone(),
            (byte[,])PartitionProbabilities.Clone(),
            (byte[,])SwitchableInterpolationProbabilities.Clone(),
            (byte[,])InterModeProbabilities.Clone(),
            (byte[])IntraInterProbabilities.Clone(),
            (byte[])CompoundInterProbabilities.Clone(),
            (byte[,])SingleReferenceProbabilities.Clone(),
            (byte[])CompoundReferenceProbabilities.Clone(),
            MotionVectorProbabilities.Clone());
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

    private static void CopyCoefficientDefaults(byte[] coefficientProbabilities, int transformSize, ReadOnlySpan<byte> defaults)
    {
        var transformSizeLength = Vp9FrameContextConstants.PlaneTypes *
            Vp9FrameContextConstants.ReferenceTypes *
            Vp9FrameContextConstants.CoefficientBands *
            Vp9FrameContextConstants.CoefficientContexts *
            Vp9FrameContextConstants.UnconstrainedNodes;
        if (defaults.Length != transformSizeLength)
        {
            throw new InvalidOperationException("VP9 default coefficient probability table has an unexpected length.");
        }

        defaults.CopyTo(coefficientProbabilities.AsSpan(transformSize * transformSizeLength, transformSizeLength));
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

    public Vp9TxProbabilities Clone()
    {
        return new Vp9TxProbabilities(
            (byte[,])ThirtyTwoByThirtyTwo.Clone(),
            (byte[,])SixteenBySixteen.Clone(),
            (byte[,])EightByEight.Clone());
    }
}

public sealed class Vp9MotionVectorProbabilities
{
    private Vp9MotionVectorProbabilities(byte[] joints, Vp9MotionVectorComponentProbabilities[] components)
    {
        Joints = joints;
        Components = components;
    }

    public byte[] Joints { get; }

    public Vp9MotionVectorComponentProbabilities[] Components { get; }

    public static Vp9MotionVectorProbabilities CreateDefault()
    {
        return new Vp9MotionVectorProbabilities(
            [32, 64, 96],
            [
                new Vp9MotionVectorComponentProbabilities(
                    128,
                    [224, 144, 192, 168, 192, 176, 192, 198, 198, 245],
                    [216],
                    [136, 140, 148, 160, 176, 192, 224, 234, 234, 240],
                    new byte[,] { { 128, 128, 64 }, { 96, 112, 64 } },
                    [64, 96, 64],
                    160,
                    128),
                new Vp9MotionVectorComponentProbabilities(
                    128,
                    [216, 128, 176, 160, 176, 176, 192, 198, 198, 208],
                    [208],
                    [136, 140, 148, 160, 176, 192, 224, 234, 234, 240],
                    new byte[,] { { 128, 128, 64 }, { 96, 112, 64 } },
                    [64, 96, 64],
                    160,
                    128)
            ]);
    }

    public Vp9MotionVectorProbabilities Clone()
    {
        return new Vp9MotionVectorProbabilities(
            (byte[])Joints.Clone(),
            [Components[0].Clone(), Components[1].Clone()]);
    }
}

public sealed class Vp9MotionVectorComponentProbabilities
{
    public Vp9MotionVectorComponentProbabilities(
        byte sign,
        byte[] classes,
        byte[] class0,
        byte[] bits,
        byte[,] class0Fp,
        byte[] fp,
        byte class0Hp,
        byte hp)
    {
        Sign = sign;
        Classes = classes;
        Class0 = class0;
        Bits = bits;
        Class0Fp = class0Fp;
        Fp = fp;
        Class0Hp = class0Hp;
        Hp = hp;
    }

    public byte Sign { get; set; }

    public byte[] Classes { get; }

    public byte[] Class0 { get; }

    public byte[] Bits { get; }

    public byte[,] Class0Fp { get; }

    public byte[] Fp { get; }

    public byte Class0Hp { get; set; }

    public byte Hp { get; set; }

    public Vp9MotionVectorComponentProbabilities Clone()
    {
        return new Vp9MotionVectorComponentProbabilities(
            Sign,
            (byte[])Classes.Clone(),
            (byte[])Class0.Clone(),
            (byte[])Bits.Clone(),
            (byte[,])Class0Fp.Clone(),
            (byte[])Fp.Clone(),
            Class0Hp,
            Hp);
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
    public const int PartitionContexts = 16;
    public const int PartitionTypes = 4;
    public const int BlockSizeGroups = 4;
    public const int IntraModes = 10;
    public const int SwitchableFilterContexts = 4;
    public const int SwitchableFilters = 3;
    public const int InterModeContexts = 7;
    public const int InterModes = 4;
    public const int IntraInterContexts = 4;
    public const int CompoundInterContexts = 5;
    public const int ReferenceContexts = 5;
    public const int MotionVectorJoints = 4;
    public const int MotionVectorClasses = 11;
    public const int MotionVectorClass0Size = 2;
    public const int MotionVectorOffsetBits = 10;
    public const int MotionVectorFractionalPrecisionSize = 4;
}
