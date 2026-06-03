namespace VPDecoder;

internal static class Vp8KeyFrameBModeProbabilities
{
    public const int BlockPredictionModes = 10;
    public const int ProbabilitiesPerContext = 9;
    public const int Count = BlockPredictionModes * BlockPredictionModes * ProbabilitiesPerContext;

    // Flattened from libvpx vp8/common/vp8_entropymodedata.h as [above][left][node].
    private const string EncodedValues =
        "53gwWXNxeJhwmLNAfqp2LkZfr0WPUFVSSJtnODoKq9q9EQ2YkEcKJqvVkCIachoRoyzDFQqteRhQwxo+LEBVqi43E4igIc5HPxQIcnLQDAniUSgLYLZUHRAkhrdZiWJlaqWUSLtkgp1vIEtQQmanY0o+KOqAKTUJsvGNGghraE8MG9n/VxEHSisakkmmMRedQSZpoDM0H3OAV0RHLHIzD7oXLykObra3FRHCQi0ZZsW9FxIWWFiTliouLcTNK2G3dVUmI7M9JzXIVxoVK+irOCIzaHJmHV1NazYgGjMBUSsfJxxVqzqlWmJAIhZ0zhciK6ZJRBlqFkCrJOFyIhMVZoS8EEx8PhJOX1U5MjAzwWUjn9dvWS5vPJQfrNvkFRJvcHFNVbP/JnhyKCoBxPXRChltZFAIK5oBMxpHWCsdjKbVJSuaPT8em0MtRAHRjk5OEP+AIsWrKSgFZtO3BAHdMzIRqNHAFxlSfWIqWGhVda9SX1Q1WYBkcWUtS097LzOAUasBOREFR2Y5NSkxcxUCCmb/phcGJiENeTlJGgFVKQpDik1uWi9yZR0QClWAZcQaORIKZmbVIhQrdRQPJKOARAEaih8kqxumJizlQ1c6qVJzGjuzPztatDumXUmaKCgVdI/RIievOS4WGIABNhElLw8QtyLfMS23LhEhtwZiDyC3QSBJcxyAF4DNKAMJczPAEgbfVyUJcztNQBUvaDcs2gk2NYLiQFpGzSgpFxo5NjlwuAUpJqbVHiIahZh0CiCGSyAMM8D/oCszJxM13RpyIEn/HwlB6gIPAXZJWB8jQ2ZVN7pVOBUXbzvNLSXANyZGfElmASJiZj1HJSI1H/PARTxHJkl3HN4lRC2AIgEvC/WrPhETRpJVNz5GSw8JCUD/uHcQJSslmmSjVaABPwlciBxAIMlVVgYcBUD/GfgBOAgRhIn/N3SAOg8UUoc5GnkopDIfiZqFGSPaM2csg4N7HwaeVihAh5TgLbeAFhoRg/CaDgHRUwwNNsD/RC8cLRAVW0DeBwHFOBUnmzyKF2bVVRpVVYCAIJKrEgsHP5CrBAT2IxsKkq6rDBqAvlAjY7RQfjYtVX4vV7AzKRQgZUuAi3aSdIBVOCkPsOxVJQk+kiQTHqv/YRsURx4Rd3b/ERKKZSY8ijdGKxqOii09PtsBUbxAICkUdZeOFBWjcBMMPcOAMAQY";

    private static readonly byte[] Values = DecodeValues();

    public static ReadOnlySpan<byte> GetProbabilities(
        Vp8BlockPredictionMode above,
        Vp8BlockPredictionMode left)
    {
        return Values.AsSpan(GetIndex(above, left, entropyNode: 0), ProbabilitiesPerContext);
    }

    public static byte GetProbability(
        Vp8BlockPredictionMode above,
        Vp8BlockPredictionMode left,
        int entropyNode)
    {
        return Values[GetIndex(above, left, entropyNode)];
    }

    private static int GetIndex(Vp8BlockPredictionMode above, Vp8BlockPredictionMode left, int entropyNode)
    {
        if (above is < 0 or >= (Vp8BlockPredictionMode)BlockPredictionModes)
        {
            throw new ArgumentOutOfRangeException(nameof(above));
        }

        if (left is < 0 or >= (Vp8BlockPredictionMode)BlockPredictionModes)
        {
            throw new ArgumentOutOfRangeException(nameof(left));
        }

        if (entropyNode is < 0 or >= ProbabilitiesPerContext)
        {
            throw new ArgumentOutOfRangeException(nameof(entropyNode));
        }

        return (((int)above * BlockPredictionModes + (int)left) * ProbabilitiesPerContext) + entropyNode;
    }

    private static byte[] DecodeValues()
    {
        var values = Convert.FromBase64String(EncodedValues);
        if (values.Length != Count)
        {
            throw new InvalidOperationException(
                $"VP8 key-frame B-mode probability table has {values.Length} entries; expected {Count}.");
        }

        return values;
    }
}
