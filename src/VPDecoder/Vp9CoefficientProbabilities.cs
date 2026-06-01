namespace VPDecoder;

internal static class Vp9CoefficientProbabilities
{
    private static readonly byte[] ParetoPivot80 = [164, 119, 153, 211, 143, 253, 217, 249];
    private static readonly byte[] ParetoPivot140 = [221, 152, 175, 242, 180, 255, 240, 253];

    public static ReadOnlySpan<byte> Category1 => [159];

    public static ReadOnlySpan<byte> Category2 => [165, 145];

    public static ReadOnlySpan<byte> Category3 => [173, 148, 140];

    public static ReadOnlySpan<byte> Category4 => [176, 155, 140, 135];

    public static ReadOnlySpan<byte> Category5 => [180, 157, 141, 134, 130];

    public static ReadOnlySpan<byte> Category6 => [254, 254, 254, 252, 249, 243, 230, 196, 177, 153, 140, 133, 130, 129];

    public static bool TryGetPareto8Full(int pivotProbability, out ReadOnlySpan<byte> probabilities)
    {
        probabilities = pivotProbability switch
        {
            80 => ParetoPivot80,
            140 => ParetoPivot140,
            _ => ReadOnlySpan<byte>.Empty
        };
        return !probabilities.IsEmpty;
    }
}
