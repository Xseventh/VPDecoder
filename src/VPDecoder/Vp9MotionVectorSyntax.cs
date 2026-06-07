namespace VPDecoder;

internal enum Vp9MotionVectorJoint
{
    Zero = 0,
    HorizontalNonZeroVerticalZero = 1,
    HorizontalZeroVerticalNonZero = 2,
    HorizontalNonZeroVerticalNonZero = 3
}

internal static class Vp9MotionVectorSyntax
{
    private const int HighPrecisionReferenceThreshold = 64;
    private const int Class0Size = 2;
    private const int Class0Bits = 1;

    private static ReadOnlySpan<sbyte> JointTree =>
    [
        0, 2,
        -1, 4,
        -2, -3
    ];

    private static ReadOnlySpan<sbyte> ClassTree =>
    [
        0, 2,
        -1, 4,
        6, 8,
        -2, -3,
        10, 12,
        -4, -5,
        -6, 14,
        16, 18,
        -7, -8,
        -9, -10
    ];

    private static ReadOnlySpan<sbyte> FractionalPrecisionTree =>
    [
        0, 2,
        -1, 4,
        -2, -3
    ];

    public static Vp9MotionVector ReadMotionVector(
        ref Vp9BoolReader reader,
        Vp9MotionVectorProbabilities probabilities,
        Vp9MotionVector reference,
        bool allowHighPrecisionMv)
    {
        var joint = ReadJoint(ref reader, probabilities);
        var useHighPrecision = allowHighPrecisionMv && UseHighPrecision(reference);
        var row = 0;
        var column = 0;

        if (HasVerticalComponent(joint))
        {
            row = ReadComponent(ref reader, probabilities.Components[0], useHighPrecision);
        }

        if (HasHorizontalComponent(joint))
        {
            column = ReadComponent(ref reader, probabilities.Components[1], useHighPrecision);
        }

        return new Vp9MotionVector(reference.Row + row, reference.Column + column);
    }

    public static Vp9MotionVectorJoint ReadJoint(
        ref Vp9BoolReader reader,
        Vp9MotionVectorProbabilities probabilities)
    {
        return (Vp9MotionVectorJoint)Vp9TreeReader.ReadTree(ref reader, JointTree, probabilities.Joints);
    }

    public static bool UseHighPrecision(Vp9MotionVector reference)
    {
        return Math.Abs(reference.Row) < HighPrecisionReferenceThreshold &&
            Math.Abs(reference.Column) < HighPrecisionReferenceThreshold;
    }

    public static Vp9MotionVector LowerPrecision(Vp9MotionVector motionVector, bool allowHighPrecisionMv)
    {
        if (allowHighPrecisionMv && UseHighPrecision(motionVector))
        {
            return motionVector;
        }

        return new Vp9MotionVector(
            LowerComponentPrecision(motionVector.Row),
            LowerComponentPrecision(motionVector.Column));
    }

    private static int ReadComponent(
        ref Vp9BoolReader reader,
        Vp9MotionVectorComponentProbabilities probabilities,
        bool useHighPrecision)
    {
        var sign = reader.Read(probabilities.Sign);
        var motionVectorClass = Vp9TreeReader.ReadTree(ref reader, ClassTree, probabilities.Classes);
        var class0 = motionVectorClass == 0;
        int d;
        var magnitude = 0;

        if (class0)
        {
            d = reader.Read(probabilities.Class0[0]) ? 1 : 0;
        }
        else
        {
            var bitCount = motionVectorClass + Class0Bits - 1;
            d = 0;
            for (var bit = 0; bit < bitCount; bit++)
            {
                if (reader.Read(probabilities.Bits[bit]))
                {
                    d |= 1 << bit;
                }
            }

            magnitude = Class0Size << (motionVectorClass + 2);
        }

        Span<byte> class0FractionalProbabilities = stackalloc byte[3];
        var fractionalProbabilities = GetFractionalProbabilities(
            probabilities,
            class0,
            d,
            class0FractionalProbabilities);
        var fractional = Vp9TreeReader.ReadTree(ref reader, FractionalPrecisionTree, fractionalProbabilities);
        var highPrecision = useHighPrecision
            ? (reader.Read(class0 ? probabilities.Class0Hp : probabilities.Hp) ? 1 : 0)
            : 1;

        magnitude += ((d << 3) | (fractional << 1) | highPrecision) + 1;
        return sign ? -magnitude : magnitude;
    }

    private static int LowerComponentPrecision(int component)
    {
        if ((component & 1) == 0)
        {
            return component;
        }

        return component + (component > 0 ? -1 : 1);
    }

    private static ReadOnlySpan<byte> GetFractionalProbabilities(
        Vp9MotionVectorComponentProbabilities probabilities,
        bool class0,
        int d,
        Span<byte> class0FractionalProbabilities)
    {
        if (!class0)
        {
            return probabilities.Fp;
        }

        if (d is < 0 or >= Class0Size)
        {
            throw new ArgumentOutOfRangeException(nameof(d), "VP9 class0 motion vector offset is outside the fractional probability table.");
        }

        class0FractionalProbabilities[0] = probabilities.Class0Fp[d, 0];
        class0FractionalProbabilities[1] = probabilities.Class0Fp[d, 1];
        class0FractionalProbabilities[2] = probabilities.Class0Fp[d, 2];
        return class0FractionalProbabilities;
    }

    private static bool HasVerticalComponent(Vp9MotionVectorJoint joint)
    {
        return joint is
            Vp9MotionVectorJoint.HorizontalZeroVerticalNonZero or
            Vp9MotionVectorJoint.HorizontalNonZeroVerticalNonZero;
    }

    private static bool HasHorizontalComponent(Vp9MotionVectorJoint joint)
    {
        return joint is
            Vp9MotionVectorJoint.HorizontalNonZeroVerticalZero or
            Vp9MotionVectorJoint.HorizontalNonZeroVerticalNonZero;
    }
}
