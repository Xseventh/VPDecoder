namespace VPDecoder;

internal static class Vp9TreeReader
{
    public static int ReadTree(
        ref Vp9BoolReader reader,
        scoped ReadOnlySpan<sbyte> tree,
        scoped ReadOnlySpan<byte> probabilities)
    {
        var index = 0;
        while (true)
        {
            var probabilityIndex = index >> 1;
            if (probabilityIndex >= probabilities.Length)
            {
                throw new Vp9BoolReaderException(
                    Vp9DecodeDiagnostic.InvalidPacket("VP9 tree probability index is outside the probability table."));
            }

            var next = tree[index + (reader.Read(probabilities[probabilityIndex]) ? 1 : 0)];
            if (next <= 0)
            {
                return -next;
            }

            index = next;
        }
    }
}
